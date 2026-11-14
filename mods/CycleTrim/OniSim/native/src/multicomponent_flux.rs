//! Regional gas transport with GPU pressure/thermal operators and a CPU
//! upwind fallback; both paths share exact host packing and Grid projection.

use crate::events::EventQueues;
use crate::fluid_helpers::notify;
use crate::simulation_step::FIXED_DT;
use crate::tables::ElementTable;
use crate::world::{PhysicsWorld, Region};

#[path = "multicomponent_staging.rs"]
mod staging;
pub(crate) use staging::GasStaging;
#[path = "multicomponent_projection.rs"]
mod projection;

pub(crate) const GAS_CHANNELS: usize = 8;
// Local GPU tensors are independent of the fixed legacy projection table.
// The stencil mask below uses u16, so keep this limit at most 16.
const MAX_REGION_GASES: usize = 16;
const MAX_REGION_LIQUIDS: usize = 16;

#[derive(Clone, Copy, Debug, PartialEq)]
struct SpeciesInfo {
    element_id: u16,
    vertical_drift: f32,
    specific_heat: f32,
    conductivity: f32,
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

/// Map active gases when at most two species are present in the regions.
/// The dense tensor is reconstructed from the legacy fields before each
/// region step, so a mapping change must invalidate its old contents.
pub(crate) fn refresh_active_gas_channels(world: &mut PhysicsWorld, elements: &ElementTable) {
    let mut ids = [0usize; 2];
    let mut count = 0usize;
    for region in &world.regions {
        let min_x = region.min_x.max(1) as usize;
        let min_y = region.min_y.max(1) as usize;
        let max_x = (region.max_x as usize).min(world.width);
        let max_y = (region.max_y as usize).min(world.height);
        for y in min_y..max_y {
            for x in min_x..max_x {
                let cell = y * world.stride + x;
                for (id, mass) in [(world.write.element[cell], world.write.mass[cell]),
                    (world.mixture_element[cell], world.mixture_mass[cell])] {
                    let id = id as usize;
                    if mass.is_finite() && mass > 0.0 && elements.elements.get(id).is_some_and(|e| e.record.state & 3 == 1) {
                        if !ids[..count].contains(&id) {
                            // Three active gases retain the existing mapping.
                            if count == ids.len() { return; }
                            ids[count] = id;
                            count += 1;
                        }
                    }
                }
            }
        }
    }
    if count == 0 { return; }
    ids[..count].sort_unstable();
    if count == world.gas_channel_count && ids[..count].iter().enumerate()
        .all(|(slot, &id)| world.gas_element_of_channel[slot] as usize == id) { return; }
    world.gas_channel_of_element.fill(u8::MAX);
    world.gas_element_of_channel.fill(0);
    world.gas_channel_count = count;
    for (slot, &id) in ids[..count].iter().enumerate() {
        world.gas_element_of_channel[slot] = id as u16;
        world.gas_channel_of_element[id] = slot as u8;
    }
    world.species_mass.fill(0.0);
}

/// Count gas species in the region, stopping as soon as the count reaches three.
pub(crate) fn region_gas_species_up_to_three(world: &PhysicsWorld, elements: &ElementTable, region: Region) -> usize {
    let mut ids = [0u16; 2];
    let mut count = 0;
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width);
    let max_y = (region.max_y as usize).min(world.height);
    for y in min_y..max_y { for x in min_x..max_x {
        let cell = y * world.stride + x;
        for (id,mass) in [(world.write.element[cell],world.write.mass[cell]), (world.mixture_element[cell],world.mixture_mass[cell])] {
            if mass > 0.0 && mass.is_finite() && elements.elements.get(id as usize).is_some_and(|e| e.record.state & 3 == 1)
                && !ids[..count].contains(&id) {
                if count == 2 { return 3; }
                ids[count] = id; count += 1;
            }
        }
    }}
    count
}

pub(crate) fn clear_species_cell(world: &mut PhysicsWorld, cell: usize) {
    let start = cell * GAS_CHANNELS;
    let end = start + GAS_CHANNELS;
    if end <= world.species_mass.len() {
        world.species_mass[start..end].fill(0.0);
    }
    world.clear_cell_mixtures(cell);
}

/// Mirror the primary/mixture/extra fields into the dense species tensor.
pub(crate) fn ingest_write_cell(world: &mut PhysicsWorld, elements: &ElementTable, cell: usize) {
    let start = cell * GAS_CHANNELS;
    if start + GAS_CHANNELS > world.species_mass.len() {
        return;
    }
    world.species_mass[start..start + GAS_CHANNELS].fill(0.0);
    let mut species = [(0u16, 0.0f32); 16];
    let mut count = 0;
    world.for_each_cell_species(cell, |elem_id, mass| {
        if count < 16 {
            species[count] = (elem_id, mass);
            count += 1;
        }
    });
    for &(elem_id, mass) in &species[..count] {
        let element = elem_id as usize;
        if mass > 0.0
            && element < elements.elements.len()
            && elements.elements[element].record.state & 3 == 1
        {
            let channel = world.gas_channel_of_element.get(element).copied().unwrap_or(u8::MAX);
            if channel != u8::MAX {
                world.species_mass[start + channel as usize] += mass;
            }
        }
    }
}

/// Project dense species back onto Grid, mixture overlay, and extra_mixtures (all representable species).
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

    let mut ch_masses = [(0usize, 0.0f32); GAS_CHANNELS];
    let mut count = 0;
    for channel in 0..channels {
        let mass = world.species_mass[start + channel].max(0.0);
        world.species_mass[start + channel] = mass;
        if mass > 0.0 {
            ch_masses[count] = (channel, mass);
            count += 1;
        }
    }
    ch_masses[..count].sort_unstable_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));

    if count == 0 {
        if elements.elements[world.write.element[cell] as usize].record.state & 3 == 1
        {
            world.write.mass[cell] = 0.0;
            world.write.element[cell] = world.vacuum_element;
            world.clear_cell_mixtures(cell);
            notify(world, events, cell);
        }
        return;
    }

    let primary_ch = ch_masses[0].0;
    let primary_mass = ch_masses[0].1;
    let primary_element = world.gas_element_of_channel[primary_ch];

    let (mix_element, mix_mass) = if count >= 2 {
        let mix_ch = ch_masses[1].0;
        (world.gas_element_of_channel[mix_ch], ch_masses[1].1)
    } else {
        (world.vacuum_element, 0.0)
    };

    if count >= 3 {
        let extras = world.extra_mixtures.entry(cell).or_default();
        extras.clear();
        for &(ch, mass) in &ch_masses[2..count] {
            extras.push((world.gas_element_of_channel[ch], mass));
        }
    } else {
        world.extra_mixtures.remove(&cell);
    }

    project_gases(world, events, cell, primary_element, primary_mass, mix_element, mix_mass);
}

fn project_gases(world: &mut PhysicsWorld, events: &mut EventQueues, cell: usize,
    primary_element: u16, primary_mass: f32, mix_element: u16, mix_mass: f32) {
    let public_cell = crate::fluid_helpers::game_index(world, cell);
    project_gases_at(world, events, cell, public_cell, primary_element, primary_mass, mix_element, mix_mass);
}

fn project_gases_at(world: &mut PhysicsWorld, events: &mut EventQueues, cell: usize,
    public_cell: Option<i32>, primary_element: u16, primary_mass: f32, mix_element: u16, mix_mass: f32) {
    // A depleted primary must not leave a zero-mass element in Grid. Promote
    // a surviving secondary instead of losing it or publishing ghost gas.
    let (primary_element, primary_mass, mix_element, mix_mass) = if primary_mass == 0.0 {
        if mix_mass > 0.0 {
            (mix_element, mix_mass, world.vacuum_element, 0.0)
        } else {
            (world.vacuum_element, 0.0, world.vacuum_element, 0.0)
        }
    } else {
        (primary_element, primary_mass, mix_element, mix_mass)
    };
    let mix_element = if mix_mass > 0.0 { mix_element } else { world.vacuum_element };
    let changed = world.write.element[cell] != primary_element
        || (world.write.mass[cell] - primary_mass).abs() > 1e-4
        || world.mixture_element[cell] != mix_element
        || (world.mixture_mass[cell] - mix_mass).abs() > 1e-4;
    world.write.element[cell] = primary_element;
    world.write.mass[cell] = primary_mass;
    if mix_mass > 0.0 {
        world.mixture_element[cell] = mix_element;
        world.mixture_mass[cell] = mix_mass;
    } else {
        world.mixture_element[cell] = world.vacuum_element;
        world.mixture_mass[cell] = 0.0;
    }
    if changed {
        if let Some(index) = public_cell { events.substance_change(index); }
        world.mark_dirty(cell, 0x1f);
    }
}

/// Diffuse local liquid species on GPU without applying the gas flow model.
/// Cells containing other phases remain CPU-owned.
/// One reusable host workspace per GPU device; no per-region cache is retained.
#[derive(Default)]
pub(crate) struct LiquidStaging {
    cells: Vec<onisim_gpu::multicomponent::PackedSparseCell>,
    masks: Vec<u16>,
    solid: Vec<u32>,
    projected: Vec<onisim_gpu::multicomponent::ProjectedLiquidCell>,
}

pub(crate) struct PendingLiquidRegion {
    min_x:usize, min_y:usize, width:usize, height:usize,
    full_region: (usize, usize, usize, usize),
    ids:Vec<u16>, species:Vec<onisim_gpu::multicomponent::SpeciesInfo>, staging:LiquidStaging,
    pending:onisim_gpu::multicomponent::PendingLiquidProjection,
}

#[path = "multicomponent_liquid_discovery.rs"]
mod liquid_discovery;

pub(crate) fn begin_sparse_liquid_region(world:&PhysicsWorld, elements:&ElementTable, region:Region,
    gpu:&mut crate::gpu_sim::GpuSimulation) -> crate::status::Result<Option<PendingLiquidRegion>> {
    let min_x=region.min_x.max(1) as usize; let min_y=region.min_y.max(1) as usize;
    let max_x=(region.max_x as usize).min(world.width); let max_y=(region.max_y as usize).min(world.height);
    if max_x.saturating_sub(min_x)<2 || max_y.saturating_sub(min_y)<2 {return Ok(None);}
    let (width,height)=(max_x-min_x,max_y-min_y);
    let full_region = (min_x, min_y, width, height);
    let Some(discovered) = liquid_discovery::discover_bounded(world, elements, full_region) else {
        return Ok(None);
    };
    let ids = discovered.ids;
    #[cfg(test)]
    let reference = crate::native_chain_tests::BENCHMARK_INLINE_COROUTINES.load(std::sync::atomic::Ordering::SeqCst);
    #[cfg(not(test))]
    let reference = false;
    // Retain a one-cell stencil halo and quantize to stable 32-cell tiles.
    // Small moving pools reuse pipelines instead of changing dimensions each tick.
    let [left, bottom, right, top] = discovered.bounds;
    let tile_min = |value: usize, origin: usize| origin + value.saturating_sub(1).saturating_sub(origin) / 32 * 32;
    let tile_max = |value: usize, origin: usize, limit: usize| (origin + (value + 1 - origin).div_ceil(32) * 32).min(limit);
    let (min_x, min_y, max_x, max_y) = if reference { (min_x, min_y, max_x, max_y) } else {
        (tile_min(left, min_x), tile_min(bottom, min_y), tile_max(right, min_x, max_x), tile_max(top, min_y, max_y))
    };
    let (width, height) = (max_x - min_x, max_y - min_y);
    let species:Vec<_>=ids.iter().map(|&id|onisim_gpu::multicomponent::SpeciesInfo {
        element_id:id, phase:2, specific_heat_capacity:elements.elements[id as usize].record.specific_heat_capacity,vertical_drift:0.0
    }).collect();
    if species.iter().any(|s|!s.specific_heat_capacity.is_finite() || s.specific_heat_capacity<=0.0) {return Ok(None);}
    let count=width*height;
    let mut staging = std::mem::take(&mut gpu.liquid_staging);
    let LiquidStaging { cells, masks, solid, .. } = &mut staging;
    cells.resize(count, onisim_gpu::multicomponent::PackedSparseCell::default());
    cells.fill(onisim_gpu::multicomponent::PackedSparseCell::default());
    masks.resize(count, 0); masks.fill(0);
    solid.resize(count, 1); solid.fill(1);
    let has_extras = !world.extra_mixtures.is_empty();
    for y in 0..height {
        let world_row = (min_y + y) * world.stride + min_x;
        let local_row = y * width;
        for x in 0..width {
            let local = local_row + x;
            let cell = world_row + x;
            // The two-slot GPU projection cannot own arbitrary inventories.
            // Keep all extra-species cells in the volume transport path.
            if world.write.properties[cell] & 1 != 0
                || (has_extras && world.extra_mixtures.contains_key(&cell))
            {
                continue;
            }
            let mass0 = world.write.mass[cell];
            if mass0 <= 0.0 || !mass0.is_finite() {
                continue;
            }
            let temp = world.write.temperature[cell];
            if !temp.is_finite() || temp <= 0.0 {
                continue;
            }
            let Some(primary) = ids.iter().position(|&id| id == world.write.element[cell]) else {
                continue;
            };
            let mix_mass = world.mixture_mass[cell];
            let mix = if mix_mass > 0.0 {
                let Some(channel) = ids.iter().position(|&id| id == world.mixture_element[cell]) else {
                    continue;
                };
                Some(channel)
            } else {
                None
            };
            let second = mix.unwrap_or(primary);
            solid[local] = 0;
            cells[local] = onisim_gpu::multicomponent::PackedSparseCell {
                first: primary as u16,
                second: second as u16,
                first_mass: mass0,
                second_mass: if mix.is_some() { mix_mass } else { 0.0 },
                temperature: temp,
            };
            masks[local] = (1u16 << primary) | if mix.is_some() { 1u16 << second } else { 0 };
        }
    }
    isolate_stencil_masks(masks, solid, width, height, 1);
    if !solid.contains(&0) { gpu.liquid_staging = staging; return Ok(None); }
    let pending = match gpu.begin_sparse_liquid(width, height, ids.len(), cells, solid, &species) {
        Ok(pending) => pending,
        Err(error) => {
            gpu.liquid_staging = staging;
            return Err(crate::status::Error::new(crate::status::Status::InternalFailure,
                format!("GPU liquid diffusion failed: {error}")));
        }
    };
    Ok(Some(PendingLiquidRegion {min_x,min_y,width,height,full_region,ids,species,staging,pending}))
}

#[cfg(test)]
pub(crate) fn finish_sparse_liquid_region(work:PendingLiquidRegion,world:&mut PhysicsWorld,
    events:&mut EventQueues,solid_out:&mut Vec<u32>,gpu:&mut crate::gpu_sim::GpuSimulation) -> crate::status::Result<bool> {
    onisim_gpu::coroutine::run(finish_sparse_liquid_region_async(work, world, events, solid_out, gpu))
}

pub(crate) struct ReadyLiquidRegion {
    min_x: usize, min_y: usize, width: usize, height: usize,
    full_region: (usize, usize, usize, usize),
    ids: Vec<u16>, species: Vec<onisim_gpu::multicomponent::SpeciesInfo>,
    staging: LiquidStaging, result: Result<bool, String>,
}

pub(crate) async fn read_sparse_liquid_region(work: PendingLiquidRegion) -> ReadyLiquidRegion {
    #[cfg(test)]
    crate::native_chain_tests::BENCHMARK_COROUTINE_READBACKS.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
    let PendingLiquidRegion { min_x, min_y, width, height, full_region, ids, species, mut staging, pending } = work;
    staging.projected.resize(width * height, onisim_gpu::multicomponent::ProjectedLiquidCell::default());
    #[cfg(test)]
    let inline = crate::native_chain_tests::BENCHMARK_INLINE_COROUTINES.load(std::sync::atomic::Ordering::SeqCst);
    #[cfg(not(test))]
    let inline = false;
    let result = if inline { pending.finish(&mut staging.projected) }
        else { pending.finish_async(&mut staging.projected).await };
    ReadyLiquidRegion { min_x, min_y, width, height, full_region, ids, species, staging, result }
}

#[cfg(test)]
pub(crate) async fn finish_sparse_liquid_region_async(work: PendingLiquidRegion, world: &mut PhysicsWorld,
    events: &mut EventQueues, solid_out: &mut Vec<u32>, gpu: &mut crate::gpu_sim::GpuSimulation) -> crate::status::Result<bool> {
    let ready = read_sparse_liquid_region(work).await;
    publish_sparse_liquid_region(ready, world, events, solid_out, gpu)
}

pub(crate) fn publish_sparse_liquid_region(work: ReadyLiquidRegion, world: &mut PhysicsWorld,
    events: &mut EventQueues, solid_out: &mut Vec<u32>, gpu: &mut crate::gpu_sim::GpuSimulation) -> crate::status::Result<bool> {
    let ReadyLiquidRegion { min_x, min_y, width, height, full_region, ids, species, mut staging, result } = work;
    let result = (|| {
        let LiquidStaging { solid, projected, .. } = &mut staging;
        if !result.map_err(|error| crate::status::Error::new(crate::status::Status::InternalFailure,
            format!("GPU liquid diffusion failed: {error}")))? { return Ok(false); }
        for y in 0..height { for x in 0..width {
            let local = y * width + x;
            if solid[local] != 0 { continue; }
            let cell = (min_y + y) * world.stride + min_x + x;
            let result = projected[local];
            let first = result.first as usize;
            let second = result.second as usize;
            let capacity = result.first_mass * species[first].specific_heat_capacity
                + result.second_mass * species[second].specific_heat_capacity;
            if capacity > 0.0 { world.write.temperature[cell] = result.enthalpy / capacity; }
            project_gases(world, events, cell, ids[first], result.first_mass, ids[second], result.second_mass);
        }}
        if (min_x, min_y, width, height) == full_region {
            std::mem::swap(solid_out, solid);
        } else {
            solid_out.resize(full_region.2 * full_region.3, 1);
            solid_out.fill(1);
            for row in 0..height {
                let destination = (min_y - full_region.1 + row) * full_region.2 + min_x - full_region.0;
                solid_out[destination..destination + width].copy_from_slice(&solid[row * width..(row + 1) * width]);
            }
        }
        Ok(true)
    })();
    gpu.liquid_staging = staging;
    result
}

#[cfg(test)]
pub(crate) fn diffuse_liquid_region(world: &mut PhysicsWorld, elements: &ElementTable,
    events: &mut EventQueues, region: Region, gpu: &mut crate::gpu_sim::GpuSimulation,
    solid_out: &mut Vec<u32>) -> crate::status::Result<bool> {
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width);
    let max_y = (region.max_y as usize).min(world.height);
    if max_x.saturating_sub(min_x) < 2 || max_y.saturating_sub(min_y) < 2 { return Ok(false); }
    let (width, height) = (max_x - min_x, max_y - min_y);
    let mut ids = Vec::new();
    for y in min_y..max_y { for x in min_x..max_x {
        let cell = y * world.stride + x;
        for (id, mass) in [(world.write.element[cell], world.write.mass[cell]), (world.mixture_element[cell], world.mixture_mass[cell])] {
            if mass > 0.0 && mass.is_finite() && elements.elements.get(id as usize).is_some_and(|e| e.record.state & 3 == 2) && !ids.contains(&id) {
                ids.push(id);
                if ids.len() > MAX_REGION_LIQUIDS { return Ok(false); }
            }
        }
    }}
    if ids.len() < 2 { return Ok(false); }
    let channels = ids.len();
    let count = width * height;
    // Four dense masses plus temperature occupy more than the 16-byte packed
    // input. Expand on the GPU and reuse unchanged input blocks.
    let sparse = channels >= 4;
    let mut mass = if sparse { Vec::new() } else { vec![0.0f32; count * channels] };
    let mut energy = Vec::new();
    let mut temperature = if sparse { Vec::new() } else { vec![0.0f32; count] };
    let mut sparse_cells = if sparse { vec![onisim_gpu::multicomponent::SparseLiquidCell::default();count] } else { Vec::new() };
    let mut masks = if sparse { vec![0u16;count] } else { Vec::new() };
    let mut solid = vec![1u32; count];
    let velocity = if sparse { Vec::new() } else { vec![0.0f32; count] };
    let species: Vec<_> = ids.iter().map(|&id| onisim_gpu::multicomponent::SpeciesInfo {
        element_id:id, phase:2, specific_heat_capacity:elements.elements[id as usize].record.specific_heat_capacity,
        vertical_drift:0.0 }).collect();
    if species.iter().any(|s| !s.specific_heat_capacity.is_finite() || s.specific_heat_capacity <= 0.0) { return Ok(false); }
    for y in 0..height { for x in 0..width {
        let local = y * width + x; let cell = (min_y + y) * world.stride + min_x + x;
        if world.write.properties[cell] & 1 != 0 { continue; }
        let Some(primary) = ids.iter().position(|&id| id == world.write.element[cell]) else { continue; };
        if world.write.mass[cell] <= 0.0 || !world.write.mass[cell].is_finite() ||
            !world.write.temperature[cell].is_finite() || world.write.temperature[cell] <= 0.0 { continue; }
        let mix = if world.mixture_mass[cell] > 0.0 {
            let Some(channel) = ids.iter().position(|&id| id == world.mixture_element[cell]) else { continue; };
            Some(channel)
        } else { None };
        solid[local] = 0;
        if sparse {
            let second = mix.unwrap_or(primary);
            sparse_cells[local] = onisim_gpu::multicomponent::SparseLiquidCell {
                first:primary as u32, second:second as u32, first_mass:world.write.mass[cell],
                second_mass:if mix.is_some() {world.mixture_mass[cell]} else {0.0}, temperature:world.write.temperature[cell] };
            masks[local] = (1u16 << primary) | (if mix.is_some() {1u16 << second} else {0});
        } else {
            temperature[local] = world.write.temperature[cell];
            mass[local*channels+primary] += world.write.mass[cell];
            if let Some(channel) = mix { mass[local*channels+channel] += world.mixture_mass[cell]; }
        }
    }}
    if sparse { isolate_stencil_masks(&masks, &mut solid, width, height, 1); }
    else { isolate_unrepresentable_stencils(&mut mass, &mut energy, &mut solid, width, height, channels, 1); }
    if !solid.contains(&0) { return Ok(false); }
    let mut projected = vec![onisim_gpu::multicomponent::ProjectedLiquidCell::default(); count];
    let result = if sparse { gpu.diffuse_sparse_liquid(width,height,channels,&sparse_cells,&solid,&species,&mut projected) }
        else { let state = onisim_gpu::multicomponent::State { mass:&mass, enthalpy:&energy, velocity_x:&velocity, velocity_y:&velocity, solid:&solid, species:&species };
            gpu.diffuse_liquid(width,height,channels,&state,&temperature,&mut projected) };
    if !result
        .map_err(|error| crate::status::Error::new(crate::status::Status::InternalFailure, format!("GPU liquid diffusion failed: {error}")))? { return Ok(false); }
    for y in 0..height { for x in 0..width {
        let local = y * width + x; if solid[local] != 0 { continue; }
        let cell = (min_y + y) * world.stride + min_x + x;
        let result = projected[local];
        let first = result.first as usize; let second = result.second as usize;
        let capacity = result.first_mass * species[first].specific_heat_capacity + result.second_mass * species[second].specific_heat_capacity;
        if capacity > 0.0 { world.write.temperature[cell] = result.enthalpy / capacity; }
        project_gases(world, events, cell, ids[first], result.first_mass, ids[second], result.second_mass);
    }}
    *solid_out = solid;
    Ok(true)
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
    let flux = |a: f32, b: f32, speed: f32| {
        let cap = 1.0f32.min(0.125 / dt);
        let v = speed.clamp(-cap, cap);
        let delta = if (a-b).abs() <= 1e-7f32.max(a.max(b)*1e-5) { 0.0 } else { a-b };
        v * if v >= 0.0 { a } else { b } + 2.0f32.min(0.125/dt) * delta
    };
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
                    mass_div += flux(mass[idx(x, y, c)], mass[idx(x + 1, y, c)], v);
                    enthalpy_div += flux(enthalpy[idx(x, y, c)], enthalpy[idx(x + 1, y, c)], v);
                }
                if x > 0 && solid[cell - 1] == 0 {
                    let v = 0.5 * (velocity_x[cell - 1] + velocity_x[cell]);
                    mass_div -= flux(mass[idx(x - 1, y, c)], mass[idx(x, y, c)], v);
                    enthalpy_div -= flux(enthalpy[idx(x - 1, y, c)], enthalpy[idx(x, y, c)], v);
                }
                if y + 1 < height && solid[cell + width] == 0 {
                    let v = 0.5 * (velocity_y[cell] + velocity_y[cell + width]) + drift;
                    mass_div += flux(mass[idx(x, y, c)], mass[idx(x, y + 1, c)], v);
                    enthalpy_div += flux(enthalpy[idx(x, y, c)], enthalpy[idx(x, y + 1, c)], v);
                }
                if y > 0 && solid[cell - width] == 0 {
                    let v = 0.5 * (velocity_y[cell - width] + velocity_y[cell]) + drift;
                    mass_div -= flux(mass[idx(x, y - 1, c)], mass[idx(x, y, c)], v);
                    enthalpy_div -= flux(enthalpy[idx(x, y - 1, c)], enthalpy[idx(x, y, c)], v);
                }
                let slot = idx(x, y, c);
                mass_output[slot] = mass[slot] - dt * mass_div;
                enthalpy_output[slot] = enthalpy[slot] - dt * enthalpy_div;
            }
        }
    }
}


fn density_gradient_velocities(mass: &[f32], solid: &[u32], width: usize, height: usize, channels: usize, velocity_x: &mut [f32], velocity_y: &mut [f32]) {
    for y in 0..height {
        for x in 0..width {
            let local = y * width + x;
            if solid[local] != 0 {
                continue;
            }
            // Density-gradient velocity proxy from total gas mass.
            let center = (0..channels).map(|c| mass[local * channels + c]).sum::<f32>();
            let left = if x > 0 && solid[local - 1] == 0 {
                (0..channels)
                    .map(|c| mass[(local - 1) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let right = if x + 1 < width && solid[local + 1] == 0 {
                (0..channels)
                    .map(|c| mass[(local + 1) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let down = if y > 0 && solid[local - width] == 0 {
                (0..channels)
                    .map(|c| mass[(local - width) * channels + c])
                    .sum::<f32>()
            } else {
                center
            };
            let up = if y + 1 < height && solid[local + width] == 0 {
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

}

fn can_project_without_losing_species(mass: &[f32], channels: usize) -> bool {
    channels != 0 && mass.chunks_exact(channels)
        .all(|cell| cell.iter().filter(|&&value| value > 0.0).take(3).count() <= 2)
}

/// Diffusion followed by transport can carry a species two faces in one pass.
/// Isolate cells whose two-face stencil contains three or more species.
fn isolate_unrepresentable_stencils(mass: &mut [f32], enthalpy: &mut [f32], solid: &mut [u32],
    width: usize, height: usize, channels: usize, radius: isize) {
    isolate_unrepresentable_stencils_reusing_masks(mass, enthalpy, solid,
        width, height, channels, radius, &mut Vec::new());
}

fn isolate_unrepresentable_stencils_reusing_masks(mass: &mut [f32], enthalpy: &mut [f32],
    solid: &mut [u32], width: usize, height: usize, channels: usize, radius: isize,
    masks: &mut Vec<u16>) {
    if channels <= 2 { return; }
    masks.clear();
    masks.resize(width * height, 0);
    for (cell, mask) in masks.iter_mut().enumerate() {
        if solid[cell] == 0 {
            for channel in 0..channels {
                if mass[cell * channels + channel] > 0.0 { *mask |= 1u16 << channel; }
            }
        }
    }
    isolate_stencil_masks(&masks, solid, width, height, radius);
    for (cell,&blocked) in solid.iter().enumerate() {
        if blocked == 0 { continue; }
        mass[cell * channels..(cell + 1) * channels].fill(0.0);
        if !enthalpy.is_empty() { enthalpy[cell * channels..(cell + 1) * channels].fill(0.0); }
    }
}

fn isolate_stencil_masks(masks:&[u16], solid:&mut [u32], width:usize, height:usize, radius:isize) {
    for y in 0..height {
        for x in 0..width {
            let cell = y * width + x;
            if solid[cell] != 0 { continue; }
            // The vast majority of cells in a region are away from its edge.
            // Unroll the two supported stencils there; the checked path below
            // still handles narrow regions and any other radius.
            if (radius == 1 || radius == 2)
                && x >= radius as usize && y >= radius as usize
                && x + (radius as usize) < width && y + (radius as usize) < height {
                let mut mask = masks[cell] | masks[cell - 1] | masks[cell + 1]
                    | masks[cell - width] | masks[cell + width];
                if radius == 2 {
                    mask |= masks[cell - 2] | masks[cell + 2]
                        | masks[cell - 2 * width] | masks[cell + 2 * width]
                        | masks[cell - width - 1] | masks[cell - width + 1]
                        | masks[cell + width - 1] | masks[cell + width + 1];
                }
                if mask.count_ones() > 2 { solid[cell] = 1; }
                continue;
            }
            let mut mask = 0u16;
            for dy in -radius..=radius {
                for dx in -radius..=radius {
                    if dx.abs() + dy.abs() > radius { continue; }
                    let nx = x as isize + dx;
                    let ny = y as isize + dy;
                    if nx >= 0 && nx < width as isize && ny >= 0 && ny < height as isize {
                        mask |= masks[ny as usize * width + nx as usize];
                    }
                }
            }
            // Neighbour masks are immutable; changing this cell's solid flag
            // cannot affect any subsequent stencil decision.
            if mask.count_ones() > 2 { solid[cell] = 1; }
        }
    }
}

#[derive(Clone)]
struct IdleGasRegion {
    source: crate::gas_region_snapshot::GasRegionSnapshot,
    solid: Vec<u32>, species: Vec<SpeciesInfo>, thermal: bool,
}

/// Run one multicomponent gas transport pass over the active region.
#[cfg(test)]
pub(crate) fn step_region(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
    gpu: Option<&mut crate::gpu_sim::GpuSimulation>,
    gpu_diffusion: bool,
) -> crate::status::Result<bool> {
    step_region_with_mask(world, elements, events, region, gpu, gpu_diffusion, None)
}

#[cfg(test)]
pub(crate) fn step_region_with_mask(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
    gpu: Option<&mut crate::gpu_sim::GpuSimulation>,
    gpu_diffusion: bool,
    diffusion_solid: Option<&mut Vec<u32>>,
) -> crate::status::Result<bool> {
    let gpu_available = gpu.is_some();
    step_region_with_dispatch(world, elements, events, region, gpu, gpu_diffusion,
        diffusion_solid, gpu_available, if cfg!(test) { 0 } else { 256 })
}

#[cfg(test)]
pub(crate) fn step_region_with_dispatch(
    world: &mut PhysicsWorld, elements: &ElementTable, events: &mut EventQueues,
    region: Region, gpu: Option<&mut crate::gpu_sim::GpuSimulation>, gpu_diffusion: bool,
    diffusion_solid: Option<&mut Vec<u32>>, gpu_available: bool, minimum_gpu_cells: usize,
) -> crate::status::Result<bool> {
    onisim_gpu::coroutine::run(step_region_with_dispatch_async(world, elements, events,
        region, gpu, gpu_diffusion, diffusion_solid, gpu_available, minimum_gpu_cells, true))
}
pub(crate) async fn step_region_with_dispatch_async(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
    mut gpu: Option<&mut crate::gpu_sim::GpuSimulation>,
    gpu_diffusion: bool,
    diffusion_solid: Option<&mut Vec<u32>>,
    gpu_available: bool,
    minimum_gpu_cells: usize,
    overlap_gpu_readback: bool,
) -> crate::status::Result<bool> {
    let mut staging = gpu.as_deref_mut().map(|device|
        std::mem::take(&mut device.gas_staging)).unwrap_or_default();
    let result = step_region_prepared_with_result_async(world, elements, events, region, gpu.as_deref_mut(),
        gpu_diffusion, diffusion_solid, gpu_available, minimum_gpu_cells, &mut staging, None, overlap_gpu_readback).await;
    if let Some(device) = gpu { device.gas_staging = staging; }
    result
}

#[path = "multicomponent_batch.rs"]
mod batch;
pub(crate) use batch::GasBatch;

#[cfg(test)]
fn step_region_prepared(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
    gpu: Option<&mut crate::gpu_sim::GpuSimulation>,
    gpu_diffusion: bool,
    diffusion_solid: Option<&mut Vec<u32>>,
    gpu_available: bool,
    minimum_gpu_cells: usize,
    staging: &mut GasStaging,
) -> crate::status::Result<bool> {
    onisim_gpu::coroutine::run(step_region_prepared_with_result_async(world, elements, events, region, gpu, gpu_diffusion,
        diffusion_solid, gpu_available, minimum_gpu_cells, staging, None, true))
}

async fn step_region_prepared_with_result_async(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
    mut gpu: Option<&mut crate::gpu_sim::GpuSimulation>,
    gpu_diffusion: bool,
    diffusion_solid: Option<&mut Vec<u32>>,
    gpu_available: bool,
    minimum_gpu_cells: usize,
    staging: &mut GasStaging,
    prepared: Option<staging::GasRegion>,
    overlap_gpu_readback: bool,
) -> crate::status::Result<bool> {
    static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    let started = (*PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
        && world.tick_seed % 17 == 0).then(std::time::Instant::now);
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width);
    let max_y = (region.max_y as usize).min(world.height);
    if max_x <= min_x || max_y <= min_y { return Ok(false); }
    let key = (min_x, min_y, max_x - min_x, max_y - min_y);
    let mut precomputed = prepared.is_some();
    let info = if let Some(info) = prepared { info } else {
        if let Some(old) = staging.idle.get(&key) {
            let same_materials = old.species.iter().all(|species| {
                elements.elements.get(species.element_id as usize).is_some_and(|element|
                    element.record.state & 3 == 1
                    && element.record.specific_heat_capacity == species.specific_heat
                    && element.record.thermal_conductivity == species.conductivity)
            });
            if old.thermal == gpu_diffusion && same_materials && old.source.matches(world, key) {
                staging.idle_hits += 1;
                if let Some(output) = diffusion_solid { output.clone_from(&old.solid); }
                return Ok(true);
            }
        }
        staging.idle.remove(&key);
        staging.preparations += 1;
        let Some(info) = staging.prepare(world, elements, region,
            gpu_available, minimum_gpu_cells) else { return Ok(false); };
        info
    };
    let prepared_at = started.map(|start| start.elapsed());
    let staging::GasRegion { width, height, channels, use_gpu, sparse, .. } = info;
    if overlap_gpu_readback && !precomputed && use_gpu && !sparse && staging.mass.len() <= 1024 * 1024 {
        if let Some(device) = gpu.as_deref_mut().filter(|g| g.coroutine_cpu_gas_enabled()) {
            // Move private staging into the existing bounded worker pool. The
            // coroutine retains exclusive world ownership and publishes below.
            let mut owned = std::mem::take(staging);
            let (returned, result) = device.cpu_work_async(move |spaces| {
                let state = onisim_gpu::multicomponent::State {
                    mass: &owned.mass, enthalpy: &owned.enthalpy,
                    velocity_x: &owned.velocity_x, velocity_y: &owned.velocity_y,
                    solid: &owned.solid, species: &owned.gpu_species,
                };
                let jobs = [crate::gpu_sim::GasBatchJob {
                    dimensions: (width, height, channels), state,
                    temperature: &owned.temperature, conductivity: &owned.species_conductivity,
                    mass_output: &mut owned.next_mass, temperature_output: &mut owned.next_temperature,
                }];
                let result = crate::gpu_sim::GpuSimulation::compute_cpu_gas_in(
                    spaces, &jobs, gpu_diffusion, FIXED_DT * 0.25);
                if matches!(result, Ok(true)) {
                    std::mem::swap(&mut owned.next_mass, &mut spaces[0].mass_output);
                    std::mem::swap(&mut owned.next_temperature, &mut spaces[0].temperature_output);
                }
                let completed = matches!(result, Ok(true));
                ((owned, result), completed)
            }).await.map_err(|error| crate::status::Error::new(
                crate::status::Status::InternalFailure, error))?;
            *staging = returned;
            precomputed = result.map_err(|error| crate::status::Error::new(
                crate::status::Status::InternalFailure, error))?;
        }
    }
    let GasStaging { sparse_cells, mass, enthalpy, temperature,
        solid, velocity_x, velocity_y, species, gpu_species, species_conductivity,
        next_mass, projected, next_enthalpy, next_temperature, .. } = staging;
    let dt = FIXED_DT * 0.25;
    // Full-channel output preserves every species; the old two-slot GPU
    // projection is not authoritative simulation state.
    let packed_gpu = false;
    if !precomputed {
        if let Some(gpu) = if use_gpu { gpu } else { None } {
            let state = onisim_gpu::multicomponent::State {
                mass: mass, enthalpy: enthalpy, velocity_x: velocity_x,
                velocity_y: velocity_y, solid: solid, species: gpu_species,
            };
            if sparse {
                if !gpu.step_sparse_gas_projected(width,height,channels,&state,temperature,species_conductivity,gpu_diffusion,dt,sparse_cells,projected)
                    .map_err(|error| crate::status::Error::new(crate::status::Status::InternalFailure,format!("GPU sparse gas pressure/transport failed: {error}")))? {
                    return Ok(false);
                }
            } else if packed_gpu {
                if !gpu.step_gas_projected(width,height,channels,&state,temperature,species_conductivity,gpu_diffusion,dt,projected)
                    .map_err(|error| crate::status::Error::new(crate::status::Status::InternalFailure,format!("GPU gas pressure/transport failed: {error}")))? {
                    return Ok(false);
                }
            } else {
                gpu.step_gas_owned(width, height, channels, &state, temperature, species_conductivity, gpu_diffusion, dt, next_mass, next_temperature)
                    .map_err(|error| crate::status::Error::new(crate::status::Status::InternalFailure,format!("GPU gas pressure/transport failed: {error}")))?;
            }
        } else {
            cpu_step(mass, enthalpy, velocity_x, velocity_y, solid, species,
                width, height, channels, dt, next_mass, next_enthalpy);
        }
    }

    let computed_at = started.map(|start| start.elapsed());
    let settled = projection::apply(world, elements, events, staging, &info);
    let GasStaging { idle, solid, species, .. } = staging;
    if settled {
        // Retain the current worlds together. Clearing a four-entry cache while
        // stepping eight worlds caused every following tick to miss again.
        let capacity = world.regions.len().max(4);
        if idle.len() >= capacity {
            idle.retain(|&(x, y, width, height), _| {
                world.regions.iter().any(|r| {
                    let left = r.min_x.max(1) as usize;
                    let bottom = r.min_y.max(1) as usize;
                    x == left
                        && y == bottom
                        && width == (r.max_x as usize).min(world.width).saturating_sub(left)
                        && height == (r.max_y as usize).min(world.height).saturating_sub(bottom)
                })
            });
            if idle.len() >= capacity {
                if let Some(evicted_key) = idle.keys().copied().min() {
                    idle.remove(&evicted_key);
                }
            }
        }
        idle.insert(key, IdleGasRegion { source: crate::gas_region_snapshot::GasRegionSnapshot::capture(world, key),
            solid: solid.clone(), species: species.clone(), thermal: gpu_diffusion });
    }
    if let Some(output) = diffusion_solid { std::mem::swap(output, solid); }
    if let Some(start) = started {
        let prepared = prepared_at.unwrap();
        let computed = computed_at.unwrap();
        eprintln!("[CycleTrim OniSim] gas transport detail prepare={:.3}ms compute={:.3}ms publish+idle={:.3}ms",
            prepared.as_secs_f64() * 1000.0, (computed - prepared).as_secs_f64() * 1000.0,
            (start.elapsed() - computed).as_secs_f64() * 1000.0);
    }
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn gas_fixture(width: usize, height: usize) -> (PhysicsWorld, ElementTable, Region) {
        let descriptions = [
            (0x2d39bf75_u32, 0_u8), (0x6d95058c, 3), (0xa9360b34, 3),
            (10, 1), (11, 1),
        ];
        let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
        for (index, (hash, phase)) in descriptions.into_iter().enumerate() {
            let mut record = [0u8; 164];
            record[..4].copy_from_slice(&hash.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = phase;
            record[12..16].copy_from_slice(&(1.0 + index as f32 * 0.1).to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.resize(bytes.len() + descriptions.len() * 4, 0);
        let elements = ElementTable::parse(&bytes).unwrap();
        let mut world = PhysicsWorld::new(width + 1, height + 1, 7, &elements).unwrap();
        for y in 1..=height {
            for x in 1..=width {
                let cell = y * world.stride + x;
                world.write.element[cell] = 3;
                world.write.mass[cell] = 1.0 + x as f32 * 0.01;
                world.mixture_element[cell] = 4;
                world.mixture_mass[cell] = 0.4 + y as f32 * 0.02;
                world.write.temperature[cell] = 290.0 + (x + y) as f32;
            }
        }
        world.write.properties[2 * world.stride + 2] = 1;
        let region = Region {
            min_x: 1, min_y: 1, max_x: width as i32 + 1, max_y: height as i32 + 1,
            ..Region::default()
        };
        (world, elements, region)
    }

    #[test]
    fn liquid_workspace_reuses_storage_after_warmup_and_resets_obstacles() {
        let (mut world, mut elements, region) = gas_fixture(16, 16);
        elements.elements[3].record.state = 2;
        elements.elements[4].record.state = 2;
        let mut gpu = crate::gpu_sim::GpuSimulation::new(world.stride, world.padded_height).unwrap();
        let mut mask = Vec::new();
        let mut storage = None;
        for iteration in 0..5 {
            let cell = 5 * world.stride + 5;
            world.write.properties[cell] = if iteration % 2 == 0 { 1 } else { 0 };
            let pending = begin_sparse_liquid_region(&world, &elements, region, &mut gpu).unwrap().unwrap();
            assert!(finish_sparse_liquid_region(pending, &mut world, &mut EventQueues::default(), &mut mask, &mut gpu).unwrap());
            let local = (5 - region.min_y as usize) * 16 + 5 - region.min_x as usize;
            assert_eq!(mask[local] != 0, iteration % 2 == 0, "old obstacle must not survive workspace reuse");
            let scratch = &gpu.liquid_staging;
            let identity = (scratch.cells.as_ptr(), scratch.masks.as_ptr(), scratch.projected.as_ptr());
            if iteration == 1 { storage = Some(identity); }
            if iteration > 1 { assert_eq!(storage.unwrap(), identity, "warm staging buffers must not be reallocated"); }
        }
        // Sparse pools retain the full-region ownership mask while transferring
        // only a quantized crop with the original stencil halo.
        let (mut world, mut elements, region) = gas_fixture(96, 64);
        elements.elements[3].record.state = 2;
        elements.elements[4].record.state = 2;
        world.write.element.fill(0);
        world.write.mass.fill(0.0);
        world.mixture_mass.fill(0.0);
        world.write.properties.fill(0);
        for y in 20..30 { for x in 35..45 {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1.0;
            world.mixture_element[cell] = 4;
            world.mixture_mass[cell] = 0.5;
        }}
        let pending = begin_sparse_liquid_region(&world, &elements, region, &mut gpu).unwrap().unwrap();
        assert_eq!((pending.width, pending.height), (32, 32));
        assert!(finish_sparse_liquid_region(pending, &mut world, &mut EventQueues::default(), &mut mask, &mut gpu).unwrap());
        assert_eq!(mask.len(), 96 * 64);
        assert_eq!(mask[(24 - 1) * 96 + 40 - 1], 0);
        assert_eq!(mask[(10 - 1) * 96 + 10 - 1], 1);
    }

    #[test]
    fn depleted_display_slot_becomes_vacuum_or_promotes_surviving_species() {
        for (primary_mass, second_mass, expected_element, expected_mass) in [
            (0.0_f32, 0.0_f32, 0_u16, 0.0_f32),
            (0.0, 3e-12, 4, 3e-12),
            (1e-12, 0.0, 3, 1e-12),
        ] {
            let (mut world, _, _) = gas_fixture(4, 4);
            let cell = world.stride + 1;
            let expected_element = if expected_element == 0 { world.vacuum_element } else { expected_element };
            project_gases(&mut world, &mut EventQueues::default(), cell, 3, primary_mass, 4, second_mass);
            assert_eq!(world.write.element[cell], expected_element);
            assert_eq!(world.write.mass[cell].to_bits(), expected_mass.to_bits());
            assert_eq!(world.mixture_mass[cell], 0.0);
        }
    }

    #[test]
    fn settled_region_sleeps_and_added_gas_wakes_it() {
        let (mut world, table, region) = gas_fixture(8,8);
        for y in 1..=8 { for x in 1..=8 {
            let c=y*world.stride+x;
            world.write.mass[c]=1.0; world.mixture_mass[c]=0.5;
            world.write.temperature[c]=300.0;
        }}
        let mut staging=GasStaging::default();
        for _ in 0..3 {
            step_region_prepared(&mut world,&table,&mut EventQueues::default(),region,
                None,false,None,false,256,&mut staging).unwrap();
        }
        assert!(staging.idle_hits>0,"settled region did not sleep");
        assert_eq!(staging.preparations, 1, "sleep must precede dense tensor preparation");
        let hits=staging.idle_hits; let c=3*world.stride+3;
        let before=world.write.mass[c+1]; world.write.mass[c]+=1.0;
        step_region_prepared(&mut world,&table,&mut EventQueues::default(),region,
            None,false,None,false,256,&mut staging).unwrap();
        assert_eq!(staging.idle_hits,hits,"changed gas incorrectly reused sleeping state");
        assert!(world.write.mass[c+1]>before,"new gas did not spread after wake");
    }

    #[test]
    fn sleeping_source_checks_extra_species_geometry_and_temperature() {
        let (mut world, _, _) = gas_fixture(8, 8);
        let key = (1, 1, 8, 8);
        let cell = 3 * world.stride + 3;
        world.extra_mixtures.insert(cell, vec![(4, 0.25)]);
        let old = crate::gas_region_snapshot::GasRegionSnapshot::capture(&world, key);
        assert!(old.matches(&world, key));
        world.extra_mixtures.get_mut(&cell).unwrap()[0].1 = 0.5;
        assert!(!old.matches(&world, key), "third species must wake the solver");
        world.extra_mixtures.get_mut(&cell).unwrap()[0].1 = 0.25;
        world.write.temperature[cell] += 1.0;
        assert!(!old.matches(&world, key), "heat source must wake the solver");
        world.write.temperature[cell] -= 1.0;
        world.write.properties[cell] ^= 1;
        assert!(!old.matches(&world, key), "geometry must wake the solver");
        world.write.properties[cell] ^= 1;
        world.mixture_mass[cell] += 0.5;
        assert!(!old.matches(&world, key), "second display slot is equally authoritative");
    }

    #[test]
    fn species_storage_order_does_not_change_motion() {
        let (mut a, table, region) = gas_fixture(8,8);
        let (mut b, _, _) = gas_fixture(8,8);
        for cell in 0..b.write.mass.len() {
            if b.mixture_mass[cell] > 0.0 {
                std::mem::swap(&mut b.write.element[cell], &mut b.mixture_element[cell]);
                std::mem::swap(&mut b.write.mass[cell], &mut b.mixture_mass[cell]);
            }
        }
        for _ in 0..20 {
            step_region(&mut a,&table,&mut EventQueues::default(),region,None,false).unwrap();
            step_region(&mut b,&table,&mut EventQueues::default(),region,None,false).unwrap();
        }
        for cell in 0..a.write.mass.len() {
            for id in [3,4] {
                let total = |w: &PhysicsWorld| { let mut m=0.0; w.for_each_cell_species(cell, |e,v| {if e==id {m+=v;}}); m };
                assert!((total(&a)-total(&b)).abs()<1e-5,"storage order changed cell {cell} species {id}");
            }
        }
    }

    #[test]
    fn pure_gas_spreads_into_vacuum_without_a_second_species() {
        let (mut world, table, region) = gas_fixture(8,8);
        world.write.element.fill(world.vacuum_element);
        world.write.mass.fill(0.0); world.mixture_mass.fill(0.0); world.write.properties.fill(0);
        let center=4*world.stride+4; world.write.element[center]=3; world.write.mass[center]=1.0;
        for _ in 0..128 { step_region(&mut world,&table,&mut EventQueues::default(),region,None,false).unwrap(); }
        assert!(world.write.mass[center+1]>0.0 && world.write.mass[center+world.stride]>0.0);
        assert!(world.write.mass[center]<0.1,"isolated gas must spread rather than remain stuck");
        assert!((world.write.mass.iter().sum::<f32>()-1.0).abs()<1e-4);
    }

    #[test]
    fn small_gpu_regions_prepare_cpu_energy_and_velocity() {
        for (width, height) in [(2, 2), (8, 8), (15, 17)] {
            let (mut expected, elements, region) = gas_fixture(width, height);
            let (mut actual, _, _) = gas_fixture(width, height);
            let before = actual.write.mass.clone();
            assert!(step_region_with_dispatch(&mut expected, &elements,
                &mut EventQueues::default(), region, None, false, None, false, 256).unwrap());
            // A device is available, but this production-sized region is below
            // the dispatch threshold. No GPU is needed to exercise its CPU path.
            assert!(step_region_with_dispatch(&mut actual, &elements,
                &mut EventQueues::default(), region, None, false, None, true, 256).unwrap());
            assert_ne!(actual.write.mass, before, "CPU pressure velocity was left at zero");
            assert_eq!(actual.write.element, expected.write.element);
            assert_eq!(actual.write.mass, expected.write.mass);
            assert_eq!(actual.mixture_element, expected.mixture_element);
            assert_eq!(actual.mixture_mass, expected.mixture_mass);
            assert_eq!(actual.write.temperature, expected.write.temperature);
            assert_eq!(actual.species_mass, expected.species_mass);
        }
    }

    #[test]
    fn stencil_mask_matches_full_neighbour_union() {
        let mut seed=1234567u32;
        for (width,height) in [(13usize,9usize),(1,1),(1,9),(9,1),(2,2),(4,5),(5,4)] {
        for radius in [1isize,2] {
            for _ in 0..20 {
                let mut masks=vec![0u16;width*height];
                let mut solid=vec![0u32;width*height];
                for i in 0..masks.len() {
                    seed=seed.wrapping_mul(1664525).wrapping_add(1013904223);
                    masks[i]=(1u16 << (seed % 12)) | if seed & 16 != 0 {1u16 << ((seed >> 8) % 12)} else {0};
                    solid[i]=u32::from(seed & 31 == 0);
                }
                let mut expected=solid.clone();
                for y in 0..height { for x in 0..width {
                    let mut union=0u16;
                    for yy in 0..height { for xx in 0..width {
                        if (x as isize-xx as isize).abs()+(y as isize-yy as isize).abs() <= radius {
                            union |= masks[yy*width+xx];
                        }
                    }}
                    if union.count_ones()>2 {expected[y*width+x]=1;}
                }}
                isolate_stencil_masks(&masks,&mut solid,width,height,radius);
                assert_eq!(solid,expected);
            }
        }
        }
    }

    #[test]
    fn three_species_output_is_not_projected_into_two_slots() {
        assert!(can_project_without_losing_species(&[1.0, 0.0, 2.0, 0.0, 0.0, 3.0], 3));
        assert!(!can_project_without_losing_species(&[1.0, 0.01, 2.0, 0.0, 0.0, 3.0], 3));
        assert!(!can_project_without_losing_species(&[], 0));
    }

    #[test]
    fn three_species_stencil_isolates_only_at_risk_cells() {
        let (width, height, channels) = (5, 1, 3);
        let mut mass = vec![0.0; width * channels];
        mass[0] = 1.0; mass[channels] = 1.0;
        mass[2 * channels + 1] = 1.0; mass[3 * channels + 2] = 1.0;
        mass[4 * channels + 2] = 1.0;
        let mut enthalpy = mass.iter().map(|v| v * 300.0).collect::<Vec<_>>();
        let mut solid = vec![0; width];
        isolate_unrepresentable_stencils(&mut mass, &mut enthalpy, &mut solid, width, height, channels, 2);
        assert_eq!(solid, [0, 1, 1, 1, 0]);
        assert_eq!(&mass[2 * channels..3 * channels], &[0.0, 0.0, 0.0]);
        assert_eq!(mass[0], 1.0);
        assert_eq!(mass[4 * channels + 2], 1.0);
    }

    #[test]
    fn density_gradients_use_all_populated_neighbours() {
        let mass = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0];
        let solid = [0u32; 9];
        let mut vx = [0.0; 9];
        let mut vy = [0.0; 9];
        density_gradient_velocities(&mass, &solid, 3, 3, 1, &mut vx, &mut vy);
        assert!((vx[4] - (-0.08)).abs() < 1e-6);
        assert!((vy[4] - (-0.24)).abs() < 1e-6);
        assert!((vx[0] - (-0.04)).abs() < 1e-6);
        assert!((vy[0] - (-0.12)).abs() < 1e-6);
    }

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
                specific_heat: 1.0,
                conductivity: 1.0,
            },
            SpeciesInfo {
                element_id: 2,
                vertical_drift: 0.0,
                specific_heat: 1.0,
                conductivity: 1.0,
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
    #[test]
    fn eight_settled_worlds_sleep_together_and_only_changed_world_wakes() {
        let (mut world, table, _) = gas_fixture(32, 8);
        for y in 1..=8 {
            for x in 1..=32 {
                let c = y * world.stride + x;
                world.write.mass[c] = 1.0;
                world.mixture_mass[c] = 0.5;
                world.write.temperature[c] = 300.0;
            }
        }
        let regions: Vec<_> = (0..8)
            .map(|i| Region {
                min_x: 1 + i * 4,
                max_x: 5 + i * 4,
                min_y: 1,
                max_y: 9,
                ..Region::default()
            })
            .collect();
        world.regions = regions.clone();
        let mut staging = GasStaging::default();
        for _ in 0..3 {
            for &region in &regions {
                step_region_prepared(
                    &mut world,
                    &table,
                    &mut EventQueues::default(),
                    region,
                    None,
                    false,
                    None,
                    false,
                    256,
                    &mut staging,
                )
                .unwrap();
            }
        }
        assert_eq!(staging.preparations, 8);
        assert_eq!(staging.idle_hits, 16);
        world.write.mass[3 * world.stride + 14] += 1.0;
        for &region in &regions {
            step_region_prepared(
                &mut world,
                &table,
                &mut EventQueues::default(),
                region,
                None,
                false,
                None,
                false,
                256,
                &mut staging,
            )
            .unwrap();
        }
        assert_eq!(staging.preparations, 9);
        assert_eq!(staging.idle_hits, 23);
    }

    #[test]
    fn sleeping_gas_ignores_blocked_solid_heat_but_wakes_when_wall_opens() {
        let (mut world, _, _) = gas_fixture(8, 8);
        let key = (1, 1, 8, 8);
        let cell = 2 * world.stride + 2;
        world.write.element[cell] = 2;
        world.clear_cell_mixtures(cell);
        let old = crate::gas_region_snapshot::GasRegionSnapshot::capture(&world, key);
        world.write.temperature[cell] += 100.0;
        world.write.mass[cell] += 100.0;
        assert!(
            old.matches(&world, key),
            "blocked rock is not a gas transport input"
        );
        world.write.element[cell] = world.vacuum_element;
        world.write.mass[cell] = 0.0;
        world.write.properties[cell] = 0;
        assert!(
            !old.matches(&world, key),
            "opening a wall must wake the gas solver"
        );
    }

    #[test]
    fn gas_batch_matches_serial_with_equal_shapes_and_invalidated_inputs() {
        let make = || {
            let (mut world, table, _) = gas_fixture(180, 20);
            // Nine worlds exercise chunk boundaries, duplicate shapes, and an
            // overlapping final region whose speculative result becomes stale.
            world.regions = (0..9).map(|i| {
                let x = 1 + i * 20 - if i == 8 { 2 } else { 0 };
                Region { min_x: x, min_y: 1, max_x: x + 20, max_y: 21, ..Default::default() }
            }).collect();
            refresh_active_gas_channels(&mut world, &table);
            (world, table)
        };
        let (mut serial, table) = make();
        let (mut batched, _) = make();
        let mut gpu = crate::gpu_sim::GpuSimulation::new(serial.stride, serial.padded_height).unwrap();
        let mut serial_events = EventQueues::default();
        let mut batch_events = EventQueues::default();
        for tick in 0..4 {
            let mut batch = GasBatch::prepare(&mut batched, &table, &mut gpu, true).unwrap();
            for index in 0..serial.regions.len() {
                // Simulate a phase transition / emitter before consuming this
                // world's result. The final region overlaps the preceding one.
                if index == 1 && tick % 2 == 0 {
                    for world in [&mut serial, &mut batched] {
                        let cell = world.stride * 4 + 27;
                        world.write.temperature[cell] += 31.0;
                        world.write.mass[cell] += 0.8;
                        world.extra_mixtures.entry(cell).or_default().push((3, 0.2));
                    }
                }
                let region = serial.regions[index];
                let mut a = Vec::new();
                let mut b = Vec::new();
                let used = step_region_with_mask(&mut serial, &table, &mut serial_events, region,
                    Some(&mut gpu), true, Some(&mut a)).unwrap();
                assert_eq!(used, batch.apply(index, &mut batched, &table, &mut batch_events, &mut gpu, true, &mut b).unwrap());
                assert_eq!(a, b);
                assert_eq!(crate::native_chain_tests::snapshot::world(&serial).0,
                    crate::native_chain_tests::snapshot::world(&batched).0, "world mismatch at tick {tick}, region {index}");
                assert_eq!(crate::native_chain_tests::snapshot::events(&serial_events).0,
                    crate::native_chain_tests::snapshot::events(&batch_events).0);
            }
            batch.retain(&mut gpu);
        }
    }

    #[test]
    fn gas_batch_sleeping_worlds_stay_asleep_and_wake_on_input_change() {
        let (mut world, table, region) = gas_fixture(20, 20);
        world.write.mass.fill(1.0);
        world.write.temperature.fill(300.0);
        world.mixture_mass.fill(0.0);
        world.regions = vec![region];
        refresh_active_gas_channels(&mut world, &table);
        let mut gpu = crate::gpu_sim::GpuSimulation::new(world.stride, world.padded_height).unwrap();
        let mut events = EventQueues::default();
        for _ in 0..3 {
            let mut batch = GasBatch::prepare(&mut world, &table, &mut gpu, true).unwrap();
            assert!(batch.apply(0, &mut world, &table, &mut events, &mut gpu, true, &mut Vec::new()).unwrap());
            batch.retain(&mut gpu);
        }
        assert_eq!(gpu.gas_batch_staging[0].preparations, 1);
        assert_eq!(gpu.gas_batch_staging[0].idle_hits, 2);
        world.write.mass[world.stride * 3 + 3] += 0.7;
        let mut batch = GasBatch::prepare(&mut world, &table, &mut gpu, true).unwrap();
        batch.apply(0, &mut world, &table, &mut events, &mut gpu, true, &mut Vec::new()).unwrap();
        batch.retain(&mut gpu);
        assert_eq!(gpu.gas_batch_staging[0].preparations, 2);
    }

    #[test]
    #[ignore = "manual paired complete gas staging and publication timing"]
    fn profile_eight_world_gas_batch_with_staging() {
        let make = || {
            let (mut world, table, _) = gas_fixture(256, 128);
            world.regions = (0..8).map(|i| Region { min_x: 1 + i * 32, min_y: 1,
                max_x: 33 + i * 32, max_y: 129, ..Default::default() }).collect();
            refresh_active_gas_channels(&mut world, &table);
            (world, table)
        };
        let (world, _) = make();
        let mut gpu = crate::gpu_sim::GpuSimulation::new(world.stride, world.padded_height).unwrap();
        let mut serial_times = Vec::new();
        let mut batch_times = Vec::new();
        for iteration in 0..110 {
            let mut images = Vec::new();
            for batched in if iteration % 2 == 0 { [false, true] } else { [true, false] } {
                let (mut world, table) = make();
                let mut events = EventQueues::default();
                let started = std::time::Instant::now();
                if batched {
                    let mut batch = GasBatch::prepare(&mut world, &table, &mut gpu, true).unwrap();
                    for index in 0..8 {
                        batch.apply(index, &mut world, &table, &mut events, &mut gpu, true, &mut Vec::new()).unwrap();
                    }
                    batch.retain(&mut gpu);
                } else {
                    for index in 0..8 {
                        let region = world.regions[index];
                        step_region_with_mask(&mut world, &table, &mut events, region, Some(&mut gpu), true, Some(&mut Vec::new())).unwrap();
                    }
                }
                if iteration >= 10 {
                    let elapsed = started.elapsed().as_secs_f64() * 1000.0;
                    if batched { batch_times.push(elapsed); } else { serial_times.push(elapsed); }
                }
                images.push((crate::native_chain_tests::snapshot::world(&world).0,
                    crate::native_chain_tests::snapshot::events(&events).0));
            }
            assert!(images[0] == images[1], "complete gas state and events diverged");
        }
        let wins = serial_times.iter().zip(&batch_times).filter(|(a,b)| b < a).count();
        println!("EIGHT_WORLD_GAS_STAGING_PAIRS,batch_faster={wins}/100");
        for (label, mut times) in [("serial", serial_times), ("batch", batch_times)] {
            times.sort_by(f64::total_cmp);
            println!("EIGHT_WORLD_GAS_STAGING,{label},median_ms={:.6},min_ms={:.6},max_ms={:.6}",
                (times[49]+times[50])*0.5,times[0],times[99]);
        }
    }

}
