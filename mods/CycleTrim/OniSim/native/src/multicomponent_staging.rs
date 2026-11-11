//! Per-device host staging for gas transport. Every input is refreshed for
//! each region; only allocation capacity survives a timestep.

use super::*;
use rayon::prelude::*;

#[derive(Default)]
pub(crate) struct GasStaging {
    pub(super) idle: std::collections::HashMap<(usize, usize, usize, usize), IdleGasRegion>,
    pub(super) idle_hits: usize,
    pub(super) preparations: usize,
    pub(super) local_elements: Vec<u16>,
    pub(super) active_elements: Vec<u16>,
    pub(super) local_channel_of_element: Vec<usize>,
    pub(super) sparse_cells: Vec<onisim_gpu::multicomponent::PackedSparseCell>,
    pub(super) masks: Vec<u16>,
    pub(super) mass: Vec<f32>,
    pub(super) enthalpy: Vec<f32>,
    pub(super) temperature: Vec<f32>,
    pub(super) solid: Vec<u32>,
    pub(super) velocity_x: Vec<f32>,
    pub(super) velocity_y: Vec<f32>,
    pub(super) species: Vec<SpeciesInfo>,
    pub(super) gpu_species: Vec<onisim_gpu::multicomponent::SpeciesInfo>,
    pub(super) species_conductivity: Vec<f32>,
    pub(super) next_mass: Vec<f32>,
    pub(super) next_enthalpy: Vec<f32>,
    pub(super) next_temperature: Vec<f32>,
    pub(super) projected: Vec<onisim_gpu::multicomponent::ProjectedLiquidCell>,
}

#[derive(Debug, PartialEq)]
pub(super) struct GasRegion {
    pub(super) min_x: usize,
    pub(super) min_y: usize,
    pub(super) width: usize,
    pub(super) height: usize,
    pub(super) channels: usize,
    pub(super) use_gpu: bool,
    pub(super) sparse: bool,
    pub(super) local_projection: bool,
}

fn reset<T: Clone>(values: &mut Vec<T>, length: usize, value: T) {
    values.clear();
    values.resize(length, value);
}

// Slice inputs keep the cell loop independent of Vec metadata and the dense
// layout. Each packed record, stencil mask and wall flag is written once.
fn pack_sparse_region(
    world: &PhysicsWorld,
    elements: &ElementTable,
    channels: &[usize],
    min_x: usize,
    min_y: usize,
    width: usize,
    height: usize,
    cells: &mut [onisim_gpu::multicomponent::PackedSparseCell],
    masks: &mut [u16],
    solid: &mut [u32],
) -> bool {
    let mut any_mixture = false;
    for (y, ((cells, masks), solid)) in cells
        .chunks_exact_mut(width)
        .zip(masks.chunks_exact_mut(width))
        .zip(solid.chunks_exact_mut(width))
        .take(height)
        .enumerate()
    {
        let begin = (min_y + y) * world.stride + min_x;
        let end = begin + width;
        let primary = &world.write.element[begin..end];
        let primary_mass = &world.write.mass[begin..end];
        let secondary = &world.mixture_element[begin..end];
        let secondary_mass = &world.mixture_mass[begin..end];
        let temperatures = &world.write.temperature[begin..end];
        let properties = &world.write.properties[begin..end];
        for (x, ((packed, mask), blocked)) in cells.iter_mut().zip(masks).zip(solid).enumerate() {
            let temperature = temperatures[x].max(1.0);
            let m1 = primary_mass[x];
            let m2 = secondary_mass[x];
            let phase = elements.elements[primary[x] as usize].record.state & 3;
            let primary_channel = channels[primary[x] as usize];
            let secondary_channel = channels
                .get(secondary[x] as usize)
                .copied()
                .unwrap_or(usize::MAX);
            let wall = properties[x] & 1 != 0
                || (m1 > 0.0 && (phase != 1 || primary_channel == usize::MAX))
                || (m2 > 0.0 && secondary_channel == usize::MAX);
            *blocked = u32::from(wall);
            if wall {
                *packed = onisim_gpu::multicomponent::PackedSparseCell {
                    temperature,
                    ..Default::default()
                };
                *mask = 0;
                continue;
            }
            let has_primary = m1 > 1e-6 && m1.is_finite();
            let has_secondary = m2 > 1e-6 && m2.is_finite();
            let first = if has_primary { primary_channel } else { 0 };
            let second = if has_secondary {
                secondary_channel
            } else {
                first
            };
            *packed = onisim_gpu::multicomponent::PackedSparseCell {
                first: first as u16,
                second: second as u16,
                first_mass: if has_primary { m1 } else { 0.0 },
                second_mass: if has_secondary { m2 } else { 0.0 },
                temperature,
            };
            *mask = (if has_primary { 1u16 << first } else { 0 })
                | (if has_secondary { 1u16 << second } else { 0 });
            any_mixture |= *mask != 0;
        }
    }
    any_mixture
}

fn pack_dense_gpu_cell(world: &PhysicsWorld, elements: &ElementTable, channels: &[usize],
    cell: usize, mass: &mut [f32], temperature: &mut f32, solid: &mut u32) -> bool {
    mass.fill(0.0);
    *temperature = world.write.temperature[cell].max(1.0);
    let phase = elements.elements[world.write.element[cell] as usize].record.state & 3;
    let mut blocked = world.write.properties[cell] & 1 != 0 || (phase != 1 && world.write.mass[cell] > 0.0);
    let mut present = false;
    if !blocked {
        world.for_each_cell_species(cell, |id, amount| {
            if amount > 0.0 {
                let channel = channels.get(id as usize).copied().unwrap_or(usize::MAX);
                if channel == usize::MAX { blocked = true; }
                else { mass[channel] += amount; present = true; }
            }
        });
    }
    *solid = u32::from(blocked);
    if blocked { mass.fill(0.0); }
    present && !blocked
}

impl GasStaging {
    pub(super) fn prepare(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        region: Region,
        gpu_available: bool,
        minimum_gpu_cells: usize,
    ) -> Option<GasRegion> {
        if world.gas_channel_count == 0 {
            rebuild_gas_channels(world, elements);
        }
        self.prepare_snapshot(world, elements, region, gpu_available, minimum_gpu_cells)
    }

    /// Pack an immutable world snapshot. Independent regions can be prepared
    /// concurrently, with each task owning its staging vectors exclusively.
    pub(super) fn prepare_snapshot(
        &mut self,
        world: &PhysicsWorld,
        elements: &ElementTable,
        region: Region,
        gpu_available: bool,
        minimum_gpu_cells: usize,
    ) -> Option<GasRegion> {
        let Self {
            idle: _,
            idle_hits: _,
            preparations: _,
            local_elements,
            active_elements: _,
            local_channel_of_element,
            sparse_cells,
            masks,
            mass,
            enthalpy,
            temperature,
            solid,
            velocity_x,
            velocity_y,
            species,
            gpu_species,
            species_conductivity,
            next_mass,
            next_enthalpy,
            next_temperature,
            projected,
        } = self;
        let channels = world.gas_channel_count;
        if channels == 0 {
            return None;
        }

        let min_x = region.min_x.max(1) as usize;
        let min_y = region.min_y.max(1) as usize;
        let max_x = (region.max_x as usize).min(world.width);
        let max_y = (region.max_y as usize).min(world.height);
        if max_x <= min_x || max_y <= min_y {
            return None;
        }
        let width = max_x - min_x;
        let height = max_y - min_y;
        if gpu_available && (width < 2 || height < 2) {
            return None;
        }
        let count = width * height;

        // Inventory every species equally. Storage slots are only a Grid adapter.
        reset(local_channel_of_element, elements.elements.len(), usize::MAX);
        local_elements.clear();
        let pool = gpu_available.then(|| crate::parallel::pool(count)).flatten();
        if let Some(pool) = pool {
            let environment = crate::float_env::FloatEnv::capture();
            let absent = (usize::MAX, usize::MAX);
            let first = pool.install(|| (0..height.div_ceil(8)).into_par_iter()
                .fold(|| vec![absent; elements.len()], |mut first, chunk| {
                    let _environment = environment.install();
                    for y in chunk * 8..((chunk + 1) * 8).min(height) {
                        for x in 0..width {
                            let mut component = 0;
                            world.for_each_cell_species((min_y + y) * world.stride + min_x + x, |id, amount| {
                                if amount > 0.0 && amount.is_finite()
                                    && elements.elements.get(id as usize).is_some_and(|e| e.record.state & 3 == 1) {
                                    first[id as usize] = first[id as usize].min((y * width + x, component));
                                }
                                component += 1;
                            });
                        }
                    }
                    first
                }).reduce(|| vec![absent; elements.len()], |mut a, b| {
                    for (a, b) in a.iter_mut().zip(b) { *a = (*a).min(b); }
                    a
                }));
            let mut present: Vec<_> = first.into_iter().enumerate()
                .filter(|&(_, position)| position != absent).collect();
            present.sort_unstable_by_key(|&(_, position)| position);
            local_elements.extend(present.into_iter().map(|(id, _)| id as u16));
            for (channel, &id) in local_elements.iter().enumerate() {
                local_channel_of_element[id as usize] = channel;
            }
        } else {
            for y in min_y..max_y { for x in min_x..max_x {
                world.for_each_cell_species(y * world.stride + x, |id, mass| {
                    if mass > 0.0 && mass.is_finite() && elements.elements.get(id as usize).is_some_and(|e| e.record.state & 3 == 1)
                        && local_channel_of_element[id as usize] == usize::MAX {
                        local_channel_of_element[id as usize] = local_elements.len();
                        local_elements.push(id);
                    }
                });
            }}
        }
        let channels = local_elements.len();
        if channels == 0 { return None; }
        let local_projection = false;
        // Packed sparse cells use 16 bytes, equal to three dense channels plus
        // temperature, and avoid expanding the channel tensor on the CPU.
        let use_gpu = gpu_available && count >= minimum_gpu_cells;
        let sparse = false;
        sparse_cells.resize(if sparse { count } else { 0 }, Default::default());
        masks.resize(if sparse { count } else { 0 }, 0);
        let mut any_mixture = false;
        if use_gpu && pool.is_some() { mass.resize(count * channels, 0.0); }
        else { reset(mass, if sparse { 0 } else { count * channels }, 0.0); }
        enthalpy.resize(if use_gpu { 0 } else { count * channels }, 0.0);
        temperature.resize(if sparse { 0 } else { count }, 1.0);
        solid.resize(count, 0);
        // All GPU gas entries generate velocity on the device, including dense
        // two-channel input. The CPU path still clears its blocked-cell velocities.
        reset(velocity_x, if use_gpu { 0 } else { count }, 0.0);
        reset(velocity_y, if use_gpu { 0 } else { count }, 0.0);
        species.clear();
        gpu_species.clear();
        species_conductivity.clear();
        for channel in 0..channels {
            let element_id = local_elements[channel];
            let record = &elements.elements[element_id as usize].record;
            species.push(SpeciesInfo {
                element_id,
                vertical_drift: 0.0,
                specific_heat: record.specific_heat_capacity,
                conductivity: record.thermal_conductivity,
            });
            if use_gpu {
                gpu_species.push(onisim_gpu::multicomponent::SpeciesInfo {
                    element_id,
                    phase: 1,
                    specific_heat_capacity: record.specific_heat_capacity,
                    vertical_drift: 0.0,
                });
                species_conductivity.push(record.thermal_conductivity);
            }
        }

        if sparse {
            any_mixture = pack_sparse_region(
                world,
                elements,
                local_channel_of_element,
                min_x,
                min_y,
                width,
                height,
                sparse_cells,
                masks,
                solid,
            );
        } else if let Some(pool) = pool.filter(|_| use_gpu) {
            let rows = width * 8;
            let environment = crate::float_env::FloatEnv::capture();
            any_mixture = pool.install(|| mass.par_chunks_mut(rows * channels)
                .zip(temperature.par_chunks_mut(rows)).zip(solid.par_chunks_mut(rows))
                .enumerate().map(|(chunk, ((packed_mass, temperature), solid))| {
                    let _environment = environment.install();
                    let mut any = false;
                    for (local, ((mass, temperature), solid)) in packed_mass.chunks_mut(channels)
                        .zip(temperature).zip(solid).enumerate() {
                        let index = chunk * rows + local;
                        let cell = (min_y + index / width) * world.stride + min_x + index % width;
                        any |= pack_dense_gpu_cell(world, elements, local_channel_of_element,
                            cell, mass, temperature, solid);
                    }
                    any
                }).reduce(|| false, |a, b| a || b));
        } else {
            for y in 0..height {
                for x in 0..width {
                    let public_y = min_y + y;
                    let public_x = min_x + x;
                    let cell = public_y * world.stride + public_x;
                    let local = y * width + x;
                    let cell_temperature = world.write.temperature[cell].max(1.0);
                    temperature[local] = cell_temperature;
                    let impermeable = world.write.properties[cell] & 1 != 0;
                    let phase = elements.elements[world.write.element[cell] as usize]
                        .record
                        .state
                        & 3;
                    // The dense tensor cannot represent every element yet. Treat any
                    // cell with an unmapped phase/component as a wall for this pass;
                    // projecting a partial tensor would otherwise delete its mass.
                    let mut blocked = impermeable || (phase != 1 && world.write.mass[cell] > 0.0);
                    let base = local * channels;
                    let mut has_mass = false;
                    if !blocked {
                        world.for_each_cell_species(cell, |id, amount| {
                            if amount > 0.0 {
                                let channel = local_channel_of_element.get(id as usize)
                                    .copied().unwrap_or(usize::MAX);
                                if channel == usize::MAX {
                                    blocked = true;
                                } else {
                                    mass[base + channel] += amount;
                                    has_mass = true;
                                }
                            }
                        });
                    }
                    solid[local] = u32::from(blocked);
                    if blocked {
                        // A later component may make the whole cell impermeable.
                        mass[base..base + channels].fill(0.0);
                        if !use_gpu { enthalpy[base..base + channels].fill(0.0); }
                        continue;
                    }
                    any_mixture |= has_mass;
                    if !use_gpu {
                        for channel in 0..channels {
                            let m = mass[base + channel].max(0.0);
                            enthalpy[local * channels + channel] =
                                m * temperature[local] * species[channel].specific_heat;
                        }
                    }
                }
            }
        }

        // Even a pure gas region needs its deferred gas-to-gas thermal faces.
        if !any_mixture {
            return None;
        }
        if !use_gpu {
            density_gradient_velocities(
                mass, solid, width, height, channels, velocity_x, velocity_y,
            );
        }

        // Successful transport overwrites every output element before use.
        // Keep existing storage; newly grown elements alone need initialization.
        let packed_gpu = false;
        next_mass.resize(if packed_gpu { 0 } else { mass.len() }, 0.0);
        projected.resize(if packed_gpu { count } else { 0 }, Default::default());
        next_enthalpy.resize(if use_gpu { 0 } else { mass.len() }, 0.0);
        next_temperature.resize(if use_gpu && !packed_gpu { count } else { 0 }, 0.0);
        Some(GasRegion {
            min_x,
            min_y,
            width,
            height,
            channels,
            use_gpu,
            sparse,
            local_projection,
        })
    }
}

#[cfg(test)]
#[path = "multicomponent_staging_tests.rs"]
mod tests;
