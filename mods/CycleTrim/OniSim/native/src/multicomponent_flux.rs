//! Production promotion of the GPU multicomponent operator's CPU reference.
//! Global gas channels + upwind face flux. No wgpu dependency in the game .so;
//! CYCLETRIM_ONISIM_GPU remains reserved for a later Vulkan handoff.

use crate::events::EventQueues;
use crate::fluid_helpers::notify;
use crate::simulation_step::FIXED_DT;
use crate::tables::ElementTable;
use crate::world::{PhysicsWorld, Region};

pub(crate) const GAS_CHANNELS: usize = 8;

#[derive(Clone, Copy, Debug)]
struct SpeciesInfo {
    element_id: u16,
    vertical_drift: f32,
}

/// Build stable gas-channel slots from the element table (first N gas elements).
pub(crate) fn rebuild_gas_channels(world: &mut PhysicsWorld, elements: &ElementTable) {
    world.gas_channel_count = 0;
    world.gas_channel_of_element.fill(u8::MAX);
    world.gas_element_of_channel = [0; GAS_CHANNELS];
    for (index, element) in elements.elements.iter().enumerate() {
        if element.record.state & 3 != 1 {
            continue;
        }
        if world.gas_channel_count >= GAS_CHANNELS {
            break;
        }
        let channel = world.gas_channel_count;
        world.gas_element_of_channel[channel] = index as u16;
        if index < world.gas_channel_of_element.len() {
            world.gas_channel_of_element[index] = channel as u8;
        }
        world.gas_channel_count += 1;
    }
}

pub(crate) fn clear_species_cell(world: &mut PhysicsWorld, cell: usize) {
    let start = cell * GAS_CHANNELS;
    let end = start + GAS_CHANNELS;
    if end <= world.species_mass.len() {
        world.species_mass[start..end].fill(0.0);
    }
    world.mixture_mass[cell] = 0.0;
    world.mixture_element[cell] = world.vacuum_element;
}

/// Mirror the legacy primary/mixture fields into the dense species tensor.
pub(crate) fn ingest_write_cell(world: &mut PhysicsWorld, elements: &ElementTable, cell: usize) {
    let start = cell * GAS_CHANNELS;
    if start + GAS_CHANNELS > world.species_mass.len() {
        return;
    }
    world.species_mass[start..start + GAS_CHANNELS].fill(0.0);
    let element = world.write.element[cell] as usize;
    let mass = world.write.mass[cell];
    if mass > 0.0
        && element < elements.elements.len()
        && elements.elements[element].record.state & 3 == 1
    {
        let channel = world.gas_channel_of_element.get(element).copied().unwrap_or(u8::MAX);
        if channel != u8::MAX {
            world.species_mass[start + channel as usize] = mass;
        }
    }
    let mix_mass = world.mixture_mass[cell];
    let mix_element = world.mixture_element[cell] as usize;
    if mix_mass > 0.0
        && mix_element < elements.elements.len()
        && elements.elements[mix_element].record.state & 3 == 1
    {
        let channel = world
            .gas_channel_of_element
            .get(mix_element)
            .copied()
            .unwrap_or(u8::MAX);
        if channel != u8::MAX {
            world.species_mass[start + channel as usize] += mix_mass;
        }
    }
}

/// Project dense species back onto legacy Grid + mixture overlay (top two masses).
pub(crate) fn project_cell_to_write(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
) {
    let start = cell * GAS_CHANNELS;
    if start + GAS_CHANNELS > world.species_mass.len() {
        return;
    }
    let channels = world.gas_channel_count;
    if channels == 0 {
        return;
    }

    let mut best = (0usize, 0.0f32);
    let mut second = (0usize, 0.0f32);
    for channel in 0..channels {
        let mass = world.species_mass[start + channel].max(0.0);
        world.species_mass[start + channel] = mass;
        if mass > best.1 {
            second = best;
            best = (channel, mass);
        } else if mass > second.1 {
            second = (channel, mass);
        }
    }

    if best.1 <= 0.0 {
        if world.write.mass[cell] > 0.0
            && elements.elements[world.write.element[cell] as usize].record.state & 3 == 1
        {
            world.write.mass[cell] = 0.0;
            world.write.element[cell] = world.vacuum_element;
            world.mixture_mass[cell] = 0.0;
            world.mixture_element[cell] = world.vacuum_element;
            notify(world, events, cell);
        }
        return;
    }

    let primary_element = world.gas_element_of_channel[best.0];
    let mix_element = world.gas_element_of_channel[second.0];
    let changed = world.write.element[cell] != primary_element
        || (world.write.mass[cell] - best.1).abs() > 1e-4
        || world.mixture_element[cell] != mix_element
        || (world.mixture_mass[cell] - second.1).abs() > 1e-4;
    world.write.element[cell] = primary_element;
    world.write.mass[cell] = best.1;
    if second.1 > 0.0 {
        world.mixture_element[cell] = mix_element;
        world.mixture_mass[cell] = second.1;
    } else {
        world.mixture_element[cell] = world.vacuum_element;
        world.mixture_mass[cell] = 0.0;
    }
    if changed {
        notify(world, events, cell);
    }
}

/// Upwind face-flux step matching `onisim-gpu::multicomponent::cpu_step`.
fn cpu_step(
    mass: &[f32],
    enthalpy: &[f32],
    velocity_x: &[f32],
    velocity_y: &[f32],
    solid: &[u32],
    species: &[SpeciesInfo],
    width: usize,
    height: usize,
    channels: usize,
    dt: f32,
    mass_output: &mut [f32],
    enthalpy_output: &mut [f32],
) {
    let idx = |x: usize, y: usize, c: usize| (y * width + x) * channels + c;
    mass_output.copy_from_slice(mass);
    enthalpy_output.copy_from_slice(enthalpy);
    for y in 0..height {
        for x in 0..width {
            let cell = y * width + x;
            if solid[cell] != 0 {
                mass_output[cell * channels..(cell + 1) * channels].fill(0.0);
                enthalpy_output[cell * channels..(cell + 1) * channels].fill(0.0);
                continue;
            }
            for c in 0..channels {
                let mut mass_div = 0.0f32;
                let mut enthalpy_div = 0.0f32;
                let drift = species[c].vertical_drift;
                if x + 1 < width && solid[cell + 1] == 0 {
                    let v = 0.5 * (velocity_x[cell] + velocity_x[cell + 1]);
                    let donor = if v >= 0.0 { idx(x, y, c) } else { idx(x + 1, y, c) };
                    mass_div += v * mass[donor];
                    enthalpy_div += v * enthalpy[donor];
                }
                if x > 0 && solid[cell - 1] == 0 {
                    let v = 0.5 * (velocity_x[cell - 1] + velocity_x[cell]);
                    let donor = if v >= 0.0 { idx(x - 1, y, c) } else { idx(x, y, c) };
                    mass_div -= v * mass[donor];
                    enthalpy_div -= v * enthalpy[donor];
                }
                if y + 1 < height && solid[cell + width] == 0 {
                    let v = 0.5 * (velocity_y[cell] + velocity_y[cell + width]) + drift;
                    let donor = if v >= 0.0 { idx(x, y, c) } else { idx(x, y + 1, c) };
                    mass_div += v * mass[donor];
                    enthalpy_div += v * enthalpy[donor];
                }
                if y > 0 && solid[cell - width] == 0 {
                    let v = 0.5 * (velocity_y[cell - width] + velocity_y[cell]) + drift;
                    let donor = if v >= 0.0 { idx(x, y - 1, c) } else { idx(x, y, c) };
                    mass_div -= v * mass[donor];
                    enthalpy_div -= v * enthalpy[donor];
                }
                let slot = idx(x, y, c);
                mass_output[slot] = mass[slot] - dt * mass_div;
                enthalpy_output[slot] = enthalpy[slot] - dt * enthalpy_div;
            }
        }
    }
}

/// Run one multicomponent gas transport pass over the active region.
pub(crate) fn step_region(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
) -> bool {
    if world.gas_channel_count == 0 {
        rebuild_gas_channels(world, elements);
    }
    let channels = world.gas_channel_count;
    if channels == 0 {
        return false;
    }

    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width);
    let max_y = (region.max_y as usize).min(world.height);
    if max_x <= min_x || max_y <= min_y {
        return false;
    }
    let width = max_x - min_x;
    let height = max_y - min_y;
    let count = width * height;

    // Ingest legacy write+mixture into dense channels for the window.
    for y in min_y..=max_y {
        for x in min_x..=max_x {
            let cell = y * world.stride + x;
            if cell < world.write.mass.len() {
                ingest_write_cell(world, elements, cell);
            }
        }
    }

    let mut any_mixture = false;
    let mut mass = vec![0.0f32; count * channels];
    let mut enthalpy = vec![0.0f32; count * channels];
    let mut solid = vec![0u32; count];
    let mut velocity_x = vec![0.0f32; count];
    let mut velocity_y = vec![0.0f32; count];
    let mut species = vec![
        SpeciesInfo {
            element_id: 0,
            vertical_drift: 0.0
        };
        channels
    ];
    for channel in 0..channels {
        species[channel] = SpeciesInfo {
            element_id: world.gas_element_of_channel[channel],
            vertical_drift: 0.0,
        };
    }

    for y in 0..height {
        for x in 0..width {
            let public_y = min_y + y;
            let public_x = min_x + x;
            let cell = public_y * world.stride + public_x;
            let local = y * width + x;
            let start = cell * GAS_CHANNELS;
            let impermeable = world.write.properties[cell] & 1 != 0;
            let phase = elements.elements[world.write.element[cell] as usize]
                .record
                .state
                & 3;
            if impermeable || (phase != 1 && world.write.mass[cell] > 0.0) {
                solid[local] = 1;
                continue;
            }
            for channel in 0..channels {
                let m = world.species_mass[start + channel].max(0.0);
                mass[local * channels + channel] = m;
                let temperature = world.write.temperature[cell].max(1.0);
                enthalpy[local * channels + channel] = m * temperature;
                if channel > 0 && m > 0.0 {
                    any_mixture = true;
                }
            }
            // Density-gradient velocity proxy from total gas mass.
            let center = (0..channels).map(|c| mass[local * channels + c]).sum::<f32>();
            let left = if x > 0 {
                (0..channels)
                    .map(|c| mass[(local - 1) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let right = if x + 1 < width {
                (0..channels)
                    .map(|c| mass[(local + 1) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let down = if y > 0 {
                (0..channels)
                    .map(|c| mass[(local - width) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let up = if y + 1 < height {
                (0..channels)
                    .map(|c| mass[(local + width) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            velocity_x[local] = 0.04 * (left - right);
            velocity_y[local] = 0.04 * (down - up);
        }
    }

    // Always run once mixtures exist; also run when multiple gas species are present
    // in the window so pure cells can begin coexistence through flux.
    let multi_present = mass.chunks(channels).any(|chunk| chunk.iter().filter(|&&m| m > 0.0).count() > 1);
    if !any_mixture && !multi_present {
        return false;
    }

    let dt = FIXED_DT * 0.25;
    let mut next_mass = vec![0.0f32; mass.len()];
    let mut next_enthalpy = vec![0.0f32; enthalpy.len()];
    cpu_step(
        &mass,
        &enthalpy,
        &velocity_x,
        &velocity_y,
        &solid,
        &species,
        width,
        height,
        channels,
        dt,
        &mut next_mass,
        &mut next_enthalpy,
    );

    for y in 0..height {
        for x in 0..width {
            let local = y * width + x;
            if solid[local] != 0 {
                continue;
            }
            let cell = (min_y + y) * world.stride + (min_x + x);
            let start = cell * GAS_CHANNELS;
            let mut heat = 0.0f32;
            let mut mass_sum = 0.0f32;
            for channel in 0..channels {
                let m = next_mass[local * channels + channel].max(0.0);
                let h = next_enthalpy[local * channels + channel].max(0.0);
                world.species_mass[start + channel] = m;
                if m > 0.0 {
                    heat += h;
                    mass_sum += m;
                }
            }
            if mass_sum > 0.0 {
                world.write.temperature[cell] = (heat / mass_sum).max(1.0);
            }
            project_cell_to_write(world, elements, events, cell);
        }
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cpu_step_conserves_two_channel_mass() {
        let width = 8usize;
        let height = 8usize;
        let channels = 2usize;
        let count = width * height;
        let mut mass = vec![0.0f32; count * channels];
        let mut enthalpy = vec![0.0f32; count * channels];
        let solid = vec![0u32; count];
        let vx = vec![0.02f32; count];
        let vy = vec![0.0f32; count];
        for cell in 0..count {
            mass[cell * channels] = 1.0;
            mass[cell * channels + 1] = 0.5;
            enthalpy[cell * channels] = 300.0;
            enthalpy[cell * channels + 1] = 150.0;
        }
        let species = [
            SpeciesInfo {
                element_id: 1,
                vertical_drift: 0.0,
            },
            SpeciesInfo {
                element_id: 2,
                vertical_drift: 0.0,
            },
        ];
        let before0: f64 = mass.iter().step_by(channels).map(|v| *v as f64).sum();
        let before1: f64 = mass.iter().skip(1).step_by(channels).map(|v| *v as f64).sum();
        let mut out_m = vec![0.0; mass.len()];
        let mut out_h = vec![0.0; enthalpy.len()];
        cpu_step(
            &mass, &enthalpy, &vx, &vy, &solid, &species, width, height, channels, 0.05, &mut out_m,
            &mut out_h,
        );
        let after0: f64 = out_m.iter().step_by(channels).map(|v| *v as f64).sum();
        let after1: f64 = out_m.iter().skip(1).step_by(channels).map(|v| *v as f64).sum();
        assert!((after0 - before0).abs() / before0 < 1e-5);
        assert!((after1 - before1).abs() / before1 < 1e-5);
    }
}
