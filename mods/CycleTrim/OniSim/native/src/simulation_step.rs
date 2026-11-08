//! Serial form of SimBase::UpdateData (official ELF 0x69730).
//!
//! Copies preserve the original active CellSOA -> old CellSOA snapshots.
//! Post-fluid copies are omitted when no mutating path ran, and the
//! multicomponent diffusion snapshot copies only its modified region.
//! In particular there is no copy between disease diffusion,
//! components, and postprocessing, nor at the end of a region.

use crate::events::EventQueues;
use crate::mass_components::MassComponents;
use crate::radiation::RadiationEmitters;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::thermal_components::ThermalComponents;
use crate::world::{Cells, PhysicsWorld, Region};

pub(crate) const FIXED_DT: f32 = f32::from_bits(0x3e4c_cccd);
pub(crate) const TICKS_PER_SECOND: f32 = 5.0;

pub(crate) struct StepSystems<'a> {
    pub(crate) mass: &'a mut MassComponents,
    pub(crate) thermal: &'a mut ThermalComponents,
    pub(crate) radiation: &'a mut RadiationEmitters,
    pub(crate) gpu: Option<&'a mut crate::gpu_sim::GpuSimulation>,
}

fn invalid(message: &'static str) -> Error {
    Error::new(Status::InvalidState, message)
}

fn validate_cells(
    cells: &Cells,
    count: usize,
    elements: &ElementTable,
    diseases: &DiseaseTable,
) -> Result<()> {
    let lengths = [
        cells.element.len(),
        cells.temperature.len(),
        cells.mass.len(),
        cells.properties.len(),
        cells.insulation.len(),
        cells.strength.len(),
        cells.disease_idx.len(),
        cells.disease_count.len(),
        cells.disease_infestation.len(),
        cells.disease_growth.len(),
        cells.radiation.len(),
    ];
    if lengths.iter().any(|&length| length != count) {
        return Err(invalid("physics CellSOA arrays have different lengths"));
    }
    // CPU/GPU steps mutate these indices directly. Activity flags do not track
    // every write, so release builds must also revalidate before table indexing.
    if !crate::id_validation::valid_u16::<false>(&cells.element, elements.len()) {
        return Err(invalid("physics cell references an absent element"));
    }
    if !crate::id_validation::valid_u8(&cells.disease_idx, diseases.len()) {
        return Err(invalid("physics cell references an absent disease"));
    }
    Ok(())
}

#[cfg(test)]
thread_local! {
    static PREFLIGHT_CALLS: std::cell::Cell<usize> = const { std::cell::Cell::new(0) };
}

#[cfg(test)]
pub(crate) fn take_preflight_calls() -> usize {
    PREFLIGHT_CALLS.with(|calls| calls.replace(0))
}

/// Pure storage/table validation; it never advances RNG, cells, or components.
fn preflight(
    world: &PhysicsWorld,
    elements: &ElementTable,
    diseases: Option<&DiseaseTable>,
    dt: f32,
) -> Result<()> {
    #[cfg(test)]
    PREFLIGHT_CALLS.with(|calls| calls.set(calls.get() + 1));
    if !dt.is_finite() || dt <= 0.0 {
        return Err(Error::new(
            Status::InvalidArgument,
            "physics elapsed time must be positive",
        ));
    }
    let diseases = diseases.ok_or_else(|| invalid("disease table is not initialized"))?;
    if elements.is_empty()
        || diseases.element_count != elements.len()
        || diseases
            .diseases
            .iter()
            .any(|disease| disease.elements.len() != elements.len())
    {
        return Err(invalid("physics metadata tables are incomplete"));
    }
    let count = world
        .stride
        .checked_mul(world.padded_height)
        .ok_or_else(|| invalid("physics grid dimensions overflow"))?;
    if world.stride != world.width + 2
        || world.padded_height != world.height + 2
        || count > i32::MAX as usize
        || world.stride > i32::MAX as usize - 63
        || world.padded_height > i32::MAX as usize - 63
    {
        return Err(invalid("physics padded dimensions are inconsistent"));
    }
    validate_cells(&world.read, count, elements, diseases)?;
    validate_cells(&world.write, count, elements, diseases)?;
    if [
        world.backwall_element.len(),
        world.backwall_mass.len(),
        world.backwall_temperature.len(),
        world.flow.len(),
        world.dirty.len(),
        world.dirty_flags.len(),
        world.mixture_element.len(),
        world.mixture_mass.len(),
    ]
    .iter()
    .any(|&length| length != count)
        || world.species_mass.len() != count * crate::multicomponent_flux::GAS_CHANNELS
        || world.active_mask.len() != world.width * world.height
        || !crate::id_validation::valid_u16::<true>(&world.backwall_element, elements.len())
        || world
            .inactive_mask
            .as_ref()
            .is_some_and(|mask| mask.len() != count)
    {
        return Err(invalid(
            "physics auxiliary arrays have inconsistent lengths",
        ));
    }
    if world.scan_direction != -1 && world.scan_direction != 1 {
        return Err(invalid("physics scan direction is not a unit step"));
    }
    for region in &world.regions {
        if region.min_x < 1
            || region.min_y < 1
            || region.max_x < region.min_x
            || region.max_y < region.min_y
            || region.max_x as usize >= world.stride
            || region.max_y as usize >= world.padded_height
        {
            return Err(invalid("physics active region is outside padded interiors"));
        }
    }
    for element in &elements.elements {
        for target in [
            element.record.low_transition,
            element.record.high_transition,
            element.record.sublimate_index,
            element.record.convert_index,
        ] {
            if target != u16::MAX && target as usize >= elements.len() {
                return Err(invalid(
                    "physics element references an absent transition target",
                ));
            }
        }
    }
    Ok(())
}

/// A successful preflight borrows the exact state until one fixed step runs.
/// The private, non-cloneable token is consumed by run; subsequent substeps
/// must validate their newly mutated world again.
pub(crate) struct PreparedStep<'a> {
    world: &'a mut PhysicsWorld,
    elements: &'a ElementTable,
    diseases: &'a DiseaseTable,
}

impl<'a> PreparedStep<'a> {
    pub(crate) fn new(
        world: &'a mut PhysicsWorld,
        elements: &'a ElementTable,
        diseases: &'a DiseaseTable,
        elapsed: f32,
    ) -> Result<Self> {
        preflight(world, elements, Some(diseases), elapsed)?;
        Ok(Self {
            world,
            elements,
            diseases,
        })
    }

    pub(crate) fn run(self, events: &mut EventQueues, systems: &mut StepSystems<'_>) -> Result<()> {
        let Self {
            world,
            elements,
            diseases,
        } = self;
        world.scan_direction = -world.scan_direction;
        if let Some(gpu) = systems.gpu.as_deref_mut() {
            let pending = gpu.begin_move_bubbles(world, elements).map_err(|error|
                Error::new(Status::InternalFailure, format!("GPU bubble movement failed: {error}")))?;
            gpu.begin_step(elements);
            if let Some(pending) = pending {
                gpu.finish_move_bubbles(world, events, pending).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU bubble readback failed: {error}")))?;
            }
        } else {
            crate::gas::rise_bubbles(world, elements, events);
        }
        world.sync_read_from_write();
        crate::multicomponent_flux::refresh_active_gas_channels(world, elements);

        let gpu_thermal_active = if let Some(ref mut gpu) = systems.gpu {
            gpu.step_thermal(world, elements, FIXED_DT).map_err(|error| {
                Error::new(Status::InternalFailure, format!("GPU thermal diffusion failed: {error}"))
            })?;
            true
        } else {
            false
        };
        if gpu_thermal_active {
            // The snapshots were synchronized immediately before the thermal
            // dispatch. step_thermal writes only temperature, so copying every
            // CellSOA field again would move an entire world unnecessarily.
            world.read.temperature.copy_from_slice(&world.write.temperature);
        }

        for index in 0..world.regions.len() {
            let region = world.regions[index];
            static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
            static SAMPLE: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
            let profiling = *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
                && SAMPLE.fetch_add(1,std::sync::atomic::Ordering::Relaxed) % 61 == 0;
            let mut mark = profiling.then(std::time::Instant::now);
            let mut lap = || match mark.as_mut() {
                Some(start) => { let now = std::time::Instant::now(); let elapsed = now.duration_since(*start).as_secs_f64() * 1000.0; *start = now; elapsed },
                None => 0.0,
            };
            let mut temperature_events = EventQueues::default();
            if gpu_thermal_active {
                if let Some(gpu) = systems.gpu.as_deref_mut() {
                    gpu.transition_region(world, elements, &mut temperature_events, region).map_err(|error|
                        Error::new(Status::InternalFailure, format!("GPU phase transition failed: {error}")))?;
                }
            } else {
                temperature_phase(world, elements, &mut temperature_events, region);
            }
            world.read.copy_region_from(&world.write, world.stride,
                region.min_x.max(1) as usize, (region.max_x as usize).min(world.width + 1),
                region.min_y.max(1) as usize, (region.max_y as usize).min(world.height + 1));
            let temperature_ms = lap();
            let (gpu_pressure, skip_gas_displacement) = if let Some(gpu) = systems.gpu.as_deref_mut() {
                let ran = gpu.pressure_region(world, elements, events, region, 1).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU gas pressure failed: {error}")))?;
                (ran, ran && gpu.gas_displacement_unneeded)
            } else { (false, false) };
            if crate::simulation_step_fluid::gas_phase_with_pressure(world, elements, diseases, events, region, gpu_pressure, skip_gas_displacement) {
                world.read.copy_fluid_region_from(&world.write, world.stride,
                    region.min_x.max(1) as usize, (region.max_x as usize).min(world.width + 1),
                    region.min_y.max(1) as usize, (region.max_y as usize).min(world.height + 1));
            }
            let gas_phase_ms = lap();
            // Gas pressure can now introduce gas into a liquid cell. Snapshot
            // the liquid diffusion input only after that update is visible.
            let pending_liquid = if let Some(gpu) = systems.gpu.as_deref_mut() {
                crate::multicomponent_flux::begin_sparse_liquid_region(world, elements, region, gpu)?
            } else { None };
            let gpu_diffusion = gpu_thermal_active && world.gas_channel_count > 0;
            let mut diffusion_solid = Vec::new();
            let gpu_transport_ran = crate::multicomponent_flux::step_region_with_mask(
                world, elements, events, region, systems.gpu.as_deref_mut(), gpu_diffusion, Some(&mut diffusion_solid),
            )?;
            let transport_ms = lap();
            let mut liquid_solid = Vec::new();
            // The gas pass treats liquid and liquid-containing cells as walls,
            // so it cannot create an eligible liquid region after preparation.
            let liquid_gpu_ran = if let Some(work) = pending_liquid {
                crate::multicomponent_flux::finish_sparse_liquid_region(work, world, events, &mut liquid_solid)?
            } else { false };
            crate::gas::diffuse_region(world, elements, events, region,
                if gpu_diffusion && gpu_transport_ran { Some(&diffusion_solid) } else { None },
                if liquid_gpu_ran { Some(&liquid_solid) } else { None });
            // Both GPU projections and diffuse_region write only inside this
            // rectangle. The snapshots were equal immediately before them.
            // Avoid copying all eleven full-world arrays for every region.
            world.read.copy_region_from(&world.write, world.stride,
                region.min_x.max(1) as usize, (region.max_x as usize).min(world.width + 1),
                region.min_y.max(1) as usize, (region.max_y as usize).min(world.height + 1));
            let diffusion_ms = lap();
            // Gas events are already in the main queue. Temperature task events
            // join after them, at ConsolidateEvents, rather than at task execution.
            consolidate_events(events, &mut temperature_events);
            // read and write Cells are equal at phase entry. If no cell-mutating
            // helper was reached, copying those same bytes again is redundant.
            // Flow/event work still executes in its original order.
            let gpu_liquid_flow = false;
            if crate::simulation_step_fluid::liquid_phase_with_flow(world, elements, diseases, events, region, gpu_liquid_flow) {
                world.read.copy_fluid_region_from(&world.write, world.stride,
                    region.min_x.max(1) as usize, (region.max_x as usize).min(world.width + 1),
                    region.min_y.max(1) as usize, (region.max_y as usize).min(world.height + 1));
            }
            if let Some(gpu) = systems.gpu.as_deref_mut() {
                gpu.diffuse_diseases(world, elements, diseases, region).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU disease diffusion failed: {error}")))?;
            } else {
                disease_diffusion(world, elements, diseases, events, region)?;
            }
            let pending_radiation = if world.radiation_enabled {
                if let Some(gpu) = systems.gpu.as_deref_mut() {
                    let maybe = gpu.begin_radiation_region(world, elements, diseases, region).map_err(|error|
                        Error::new(Status::InternalFailure, format!("GPU radiation failed: {error}")))?;
                    if maybe.is_none() {
                        crate::radiation::update_region(world, elements, diseases, region)?;
                    }
                    maybe
                } else {
                    crate::radiation::update_region(world, elements, diseases, region)?;
                    None
                }
            } else { None };
            update_nonradiation_components(world, elements, diseases, events, systems, &region)?;
            let bounds = postprocess_bounds(world, region);
            let pending_decay = if let Some(gpu) = systems.gpu.as_deref_mut() {
                gpu.begin_inactive_decay(world, elements, bounds).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU inactive decay failed: {error}")))?
            } else { None };
            if let Some(work) = pending_radiation {
                let gpu = systems.gpu.as_deref_mut().ok_or_else(|| invalid("pending radiation has no GPU"))?;
                gpu.finish_radiation_region(world, work).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU radiation readback failed: {error}")))?;
            }
            systems.radiation.update(FIXED_DT, world, elements, region)?;
            if let Some(work) = pending_decay {
                let gpu = systems.gpu.as_deref_mut().ok_or_else(|| invalid("pending inactive decay has no GPU"))?;
                gpu.finish_inactive_decay(world, work).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU inactive decay readback failed: {error}")))?;
            } else if systems.gpu.is_none() {
                decay_inactive(world, elements, bounds);
            }
            for y in bounds.2..bounds.3 {
                for x in bounds.0..bounds.1 {
                    crate::postprocess::cell(
                        world,
                        elements,
                        diseases,
                        events,
                        y * world.stride + x,
                    )?;
                }
            }
            if let Some(gpu) = systems.gpu.as_deref_mut() {
                gpu.grow_diseases(world, diseases, bounds).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU disease growth failed: {error}")))?;
            } else {
                crate::disease::post_process_bounds(
                    world, diseases, bounds.0, bounds.1, bounds.2, bounds.3,
                )?;
            }
            let remaining_ms = lap();
            if profiling {
                eprintln!("[CycleTrim OniSim] region profile index={index} temperature+sync={temperature_ms:.3}ms gas+sync={gas_phase_ms:.3}ms transport={transport_ms:.3}ms diffusion+sync={diffusion_ms:.3}ms remaining={remaining_ms:.3}ms");
            }
        }
        world.tick_seed = world.tick_seed.wrapping_add(1);
        Ok(())
    }
}

fn consolidate_events(events: &mut EventQueues, task: &mut EventQueues) {
    macro_rules! append {
        ($($field:ident),* $(,)?) => { $(events.$field.append(&mut task.$field);)* };
    }
    append!(
        solid_info,
        liquid_change_info,
        solid_substance_change_info,
        substance_change_info,
        callback_info,
        spawn_falling_liquid_info,
        dig_info,
        spawn_ore_info,
        spawn_fx_info,
        unstable_cell_info,
        world_damage_info,
        building_temperatures,
        mass_consumed_callbacks,
        mass_emitted_callbacks,
        disease_consumption_callbacks,
        component_state_changed_messages,
        removed_mass_entries,
        emitted_mass_entries,
        element_chunk_infos,
        element_chunk_melted_infos,
        building_overheat_infos,
        building_no_longer_overheated_infos,
        building_melted_infos,
        cell_melted_infos,
        backwall_element_changed_infos,
        backwall_should_transition_infos,
        disease_emitted_infos,
        disease_consumed_infos,
        radiation_consumed_callbacks
    );
}

fn update_nonradiation_components(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    systems: &mut StepSystems<'_>,
    region: &Region,
) -> Result<()> {
    // SimData constructor registration order, ELF 0x3e470, +0xb0 vector.
    systems
        .mass
        .update_consumers(world, elements, diseases, FIXED_DT, region, events)?;
    systems
        .mass
        .update_emitters(world, elements, diseases, FIXED_DT, region, events)?;
    systems
        .mass
        .update_disease_emitters(world, diseases, FIXED_DT, region, events)?;
    systems
        .thermal
        .update_chunks(FIXED_DT, region, world, elements, events)?;
    systems
        .thermal
        .update_buildings(FIXED_DT, region, world, elements, events)?;
    systems
        .thermal
        .update_contacts(FIXED_DT, region, world, events)?;
    systems
        .mass
        .update_disease_consumers(world, diseases, FIXED_DT, region, events)?;
    Ok(())
}

fn temperature_pair(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    cache: &mut crate::thermal::ThermalContactCache<'_>,
    a: usize,
    b: usize,
) {
    let difference = f32::from_bits(
        (world.read.temperature[a] - world.read.temperature[b]).to_bits() & 0x7fff_ffff,
    );
    let first = &elements.elements[world.read.element[a] as usize].record;
    let second = &elements.elements[world.read.element[b] as usize].record;
    if difference >= 1.0
        && first.thermal_conductivity > 0.0
        && second.thermal_conductivity > 0.0
        && (first.state | second.state) & 0x10 == 0
    {
        crate::thermal::update_temperature(world, cache, a, b);
    }
}

fn temperature_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    region: Region,
) {
    // A single serial SimUpdateTemperatureTask: row-major exchange, then a
    // separate row-major transition pass. All exchange reads remain old cells.
    let mut cache = crate::thermal::ThermalContactCache::new(elements);
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            if world.read.mass[cell] > 0.0 && world.backwall_mass[cell] > 0.0 {
                crate::thermal::update_backwall_temperature(world, elements, events, cell);
            }
            if x < region.max_x - 1 {
                temperature_pair(world, elements, &mut cache, cell, cell + 1);
            }
            if y < region.max_y - 1 {
                temperature_pair(world, elements, &mut cache, cell, cell + world.stride);
            }
        }
    }
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            crate::transition::do_state_transition(
                world,
                elements,
                events,
                y as usize * world.stride + x as usize,
            );
        }
    }
}

pub(crate) fn disease_diffusion(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    _events: &mut EventQueues,
    region: Region,
) -> Result<()> {
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            let first_id = world.read.disease_idx[cell];
            let right_id = world.read.disease_idx[cell + 1];
            if first_id != u8::MAX || right_id != u8::MAX {
                crate::disease::update_cells_known(
                    world,
                    elements,
                    diseases,
                    cell,
                    cell + 1,
                    first_id,
                    right_id,
                );
            }
            let below_id = world.read.disease_idx[cell + world.stride];
            if first_id != u8::MAX || below_id != u8::MAX {
                crate::disease::update_cells_known(
                    world,
                    elements,
                    diseases,
                    cell,
                    cell + world.stride,
                    first_id,
                    below_id,
                );
            }
        }
    }
    Ok(())
}

fn postprocess_bounds(world: &PhysicsWorld, region: Region) -> (usize, usize, usize, usize) {
    let start = |coordinate: i32| (((coordinate - 1) / 32) * 32 + 1).max(coordinate);
    let end = |coordinate: i32, dimension: usize| {
        (((coordinate + 30) / 32) * 32 + 1)
            .min(dimension as i32 - 1)
            .min(coordinate + 1)
    };
    (
        start(region.min_x) as usize,
        end(region.max_x, world.stride) as usize,
        start(region.min_y) as usize,
        end(region.max_y, world.padded_height) as usize,
    )
}

fn decay_inactive(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    bounds: (usize, usize, usize, usize),
) {
    let Some(mask) = world.inactive_mask.as_ref() else {
        return;
    };
    for y in bounds.2..bounds.3 {
        for x in bounds.0..bounds.1 {
            let cell = y * world.stride + x;
            if mask[cell] != u8::MAX || world.backwall_element[cell] != world.vacuum_element {
                continue;
            }
            let decay = match elements.elements[world.write.element[cell] as usize]
                .record
                .state
                & 3
            {
                3 => continue,
                2 => 20.0,
                1 => f32::from_bits(0x3ca3_d70a),
                _ => 0.0,
            };
            let mass = world.write.mass[cell] - decay;
            world.write.mass[cell] = if mass <= 0.0 { 0.0 } else { mass };
            world.flow[cell] = [-FIXED_DT, FIXED_DT, FIXED_DT, -FIXED_DT];
        }
    }
}
