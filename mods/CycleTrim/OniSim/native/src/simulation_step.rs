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
        onisim_gpu::coroutine::run(self.run_async(events, systems))
    }

    async fn run_async(self, events: &mut EventQueues, systems: &mut StepSystems<'_>) -> Result<()> {
        let Self {
            world,
            elements,
            diseases,
        } = self;
        static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
        static SAMPLE: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
        let profiling = *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && SAMPLE.fetch_add(1, std::sync::atomic::Ordering::Relaxed) % 17 == 0;
        let step_started = profiling.then(std::time::Instant::now);
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
        if world.gas_channel_count == 0 {
            crate::multicomponent_flux::rebuild_gas_channels(world, elements);
        }

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

        if let Some(started) = step_started {
            eprintln!("[CycleTrim OniSim] step profile tick={} global_setup+thermal={:.3}ms",
                world.tick_seed, started.elapsed().as_secs_f64() * 1000.0);
        }
        let gpu_diffusion = gpu_thermal_active && world.gas_channel_count > 0;
        let mut gas_batch = if world.regions.len() > 1 && gpu_diffusion {
            if let Some(gpu) = systems.gpu.as_deref_mut().filter(|gpu| !gpu.gas_cpu_regions_preferred) {
                Some(crate::multicomponent_flux::GasBatch::prepare(world, elements, gpu, gpu_diffusion)?)
            } else { None }
        } else { None };
        if let Some(started) = step_started {
            eprintln!("[CycleTrim OniSim] step profile tick={} through_gas_batch={:.3}ms",
                world.tick_seed, started.elapsed().as_secs_f64() * 1000.0);
        }
        let (mut diffusion_solid, mut liquid_solid) = if let Some(gpu) = systems.gpu.as_deref_mut() {
            (std::mem::take(&mut gpu.diffusion_solid), std::mem::take(&mut gpu.liquid_solid))
        } else { (Vec::new(), Vec::new()) };
        for index in 0..world.regions.len() {
            let region = world.regions[index];
            let mut mark = profiling.then(std::time::Instant::now);
            let mut lap = || match mark.as_mut() {
                Some(start) => { let now = std::time::Instant::now(); let elapsed = now.duration_since(*start).as_secs_f64() * 1000.0; *start = now; elapsed },
                None => 0.0,
            };
            let mut temperature_events = EventQueues::default();
            let temperature_changed = if gpu_thermal_active {
                if let Some(gpu) = systems.gpu.as_deref_mut() {
                    gpu.transition_region(world, elements, &mut temperature_events, region).map_err(|error|
                        Error::new(Status::InternalFailure, format!("GPU phase transition failed: {error}")))?
                } else { false }
            } else {
                temperature_phase(world, elements, &mut temperature_events, region);
                true
            };
            if temperature_changed {
                world.read.copy_region_from(&world.write, world.stride,
                    region.min_x.max(1) as usize, (region.max_x as usize).min(world.width + 1),
                    region.min_y.max(1) as usize, (region.max_y as usize).min(world.height + 1));
            }
            let temperature_ms = lap();
            // Species transport owns gas motion. The legacy one-element
            // pressure/displacement pass must not privilege Grid's display slot.
            let gas_phase_ms = lap();
            // Gas pressure can now introduce gas into a liquid cell. Snapshot
            // the liquid diffusion input only after that update is visible.
            let pending_liquid = if let Some(gpu) = systems.gpu.as_deref_mut() {
                crate::multicomponent_flux::begin_sparse_liquid_region(world, elements, region, gpu)?
            } else { None };
            // Readback owns only private staging. JoinSet cancels it if this
            // frame fails; publication remains after the gas phase below.
            let pending_liquid = pending_liquid.map(|work| {
                let mut readbacks = tokio::task::JoinSet::new();
                readbacks.spawn(crate::multicomponent_flux::read_sparse_liquid_region(work));
                readbacks
            });
            diffusion_solid.clear();
            let gpu_transport_ran = if let (Some(batch), Some(gpu)) = (gas_batch.as_mut(), systems.gpu.as_deref_mut()) {
                batch.apply_async(index, world, elements, events, gpu, gpu_diffusion, &mut diffusion_solid).await?
            } else {
                let gpu_available = systems.gpu.is_some();
                // CPU-immediate execution preserves the batched path's small
                // region policy, including in host tests with tiny worlds.
                let minimum_gpu_cells = if world.regions.len() > 1 || !cfg!(test) { 256 } else { 0 };
                crate::multicomponent_flux::step_region_with_dispatch_async(
                    world, elements, events, region, systems.gpu.as_deref_mut(), gpu_diffusion,
                    Some(&mut diffusion_solid), gpu_available, minimum_gpu_cells, pending_liquid.is_some(),
                ).await?
            };
            let transport_ms = lap();
            liquid_solid.clear();
            // The gas pass treats liquid and liquid-containing cells as walls,
            // so it cannot create an eligible liquid region after preparation.
            let liquid_gpu_ran = if let Some(mut readbacks) = pending_liquid {
                let ready = readbacks.join_next().await
                    .ok_or_else(|| invalid("liquid readback task missing"))?
                    .map_err(|error| Error::new(crate::status::Status::InternalFailure, format!("liquid readback coroutine failed: {error}")))?;
                crate::multicomponent_flux::publish_sparse_liquid_region(ready, world, events, &mut liquid_solid,
                    systems.gpu.as_deref_mut().ok_or_else(|| invalid("pending liquid work lost its GPU device"))?)?
            } else { false };
            crate::gas::diffuse_region(world, elements, events, region,
                if gpu_diffusion && gpu_transport_ran { Some(&diffusion_solid) } else { None },
                if liquid_gpu_ran { Some(&liquid_solid) } else { None });
            // Both GPU projections and diffuse_region write only inside this
            // rectangle. The snapshots were equal immediately before them.
            // Avoid copying all eleven full-world arrays for every region.
            world.read.copy_fluid_region_from(&world.write, world.stride,
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
            let liquid_flow_ms = lap();
            if let Some(gpu) = systems.gpu.as_deref_mut() {
                gpu.diffuse_diseases(world, elements, diseases, region).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU disease diffusion failed: {error}")))?;
            } else {
                disease_diffusion(world, elements, diseases, events, region)?;
            }
            let disease_diffusion_ms = lap();
            // The CPU visits each cell and scatters only actual emitters.
            // GPU gather checked 25 neighbors twice for every cell, then paid
            // an upload/readback barrier per world. Paired sparse and dense
            // benchmarks favor this direct traversal, with matching outputs.
            crate::radiation::update_region(world, elements, diseases, region)?;
            let radiation_ms = lap();
            update_nonradiation_components(world, elements, diseases, events, systems, &region)?;
            let components_ms = lap();
            let bounds = postprocess_bounds(world, region);
            systems.radiation.update(FIXED_DT, world, elements, region)?;
            let emitters_ms = lap();
            decay_inactive(world, elements, bounds);
            crate::postprocess::region(world, elements, diseases, events, bounds)?;
            let postprocess_ms = lap();
            if let Some(gpu) = systems.gpu.as_deref_mut() {
                gpu.grow_diseases(world, diseases, bounds).map_err(|error|
                    Error::new(Status::InternalFailure, format!("GPU disease growth failed: {error}")))?;
            } else {
                crate::disease::post_process_bounds(
                    world, diseases, bounds.0, bounds.1, bounds.2, bounds.3,
                )?;
            }
            let disease_growth_ms = lap();
            if profiling {
                eprintln!("[CycleTrim OniSim] region profile index={index} temperature+sync={temperature_ms:.3}ms gas+sync={gas_phase_ms:.3}ms transport={transport_ms:.3}ms diffusion+sync={diffusion_ms:.3}ms liquid_flow_and_sync_wall={liquid_flow_ms:.3}ms disease_diffusion={disease_diffusion_ms:.3}ms radiation={radiation_ms:.3}ms components={components_ms:.3}ms emitters={emitters_ms:.3}ms postprocess={postprocess_ms:.3}ms disease_growth={disease_growth_ms:.3}ms");
            }
        }
        if let (Some(batch), Some(gpu)) = (gas_batch, systems.gpu.as_deref_mut()) {
            batch.retain(gpu);
        }
        if let Some(started) = step_started {
            eprintln!("[CycleTrim OniSim] step total tick={} wall={:.3}ms",
                world.tick_seed, started.elapsed().as_secs_f64() * 1000.0);
        }
        if let Some(gpu) = systems.gpu.as_deref_mut() {
            gpu.diffusion_solid = diffusion_solid;
            gpu.liquid_solid = liquid_solid;
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
    if world.inactive_mask.is_none() {
        return;
    }
    for y in bounds.2..bounds.3 {
        for x in bounds.0..bounds.1 {
            let cell = y * world.stride + x;
            if !space_exposed(world, cell) {
                continue;
            }
            apply_space_decay(world, elements, cell);
        }
    }
}

fn space_exposed(world: &PhysicsWorld, cell: usize) -> bool {
    // The save/BackwallManager representation uses MAX for an absent wall;
    // removal and transition messages also use the Vacuum element sentinel.
    world
        .inactive_mask
        .as_ref()
        .is_some_and(|zones| zones[cell] == u8::MAX)
        && (world.backwall_element[cell] == world.vacuum_element
            || world.backwall_element[cell] == u16::MAX)
}

/// Remove each phase's escaped mass proportionately across all its species.
/// Storage order never affects the loss rate or which species survives.
pub(crate) fn apply_space_decay(world: &mut PhysicsWorld, elements: &ElementTable, cell: usize) {
    let mut totals = [0.0f64; 4];
    world.for_each_cell_species(cell, |id, mass| {
        totals[(elements.elements[id as usize].record.state & 3) as usize] +=
            f64::from(mass.max(0.0));
    });
    if totals[1] == 0.0 && totals[2] == 0.0 {
        return;
    }
    let mut fraction = [1.0f32; 4];
    // Preserve the existing absolute kg/s ceilings, but drain at most 5% of
    // the present phase per 0.2s tick. A newly arriving small parcel can keep
    // spreading into adjacent exposed cells instead of vanishing in one tick.
    for (phase, kilograms_per_second) in [(1, 0.1f64), (2, 100.0f64)] {
        let total = totals[phase];
        if total > 0.0 {
            let loss = (kilograms_per_second * f64::from(FIXED_DT))
                .min(total * 0.25 * f64::from(FIXED_DT));
            let remaining = total - loss;
            fraction[phase] = if remaining <= 1e-6 {
                0.0
            } else {
                (remaining / total) as f32
            };
        }
    }
    let scale = |id: u16| fraction[(elements.elements[id as usize].record.state & 3) as usize];
    world.write.mass[cell] *= scale(world.write.element[cell]);
    world.mixture_mass[cell] *= scale(world.mixture_element[cell]);
    if let Some(extras) = world.extra_mixtures.get_mut(&cell) {
        for (id, mass) in extras.iter_mut() {
            *mass *= scale(*id);
        }
        extras.retain(|&(_, mass)| mass > 0.0);
    }
    // Scaling one phase does not reorder its species. Avoid the legacy
    // normalization's fixed-size temporary array and its component limit.
    if world.extra_mixtures.get(&cell).is_some_and(Vec::is_empty) {
        world.extra_mixtures.remove(&cell);
    }
    if world.write.mass[cell] == 0.0 {
        world.write.element[cell] = world.vacuum_element;
    }
    if world.mixture_mass[cell] == 0.0 {
        world.mixture_element[cell] = world.vacuum_element;
    }
    crate::multicomponent_flux::ingest_write_cell(world, elements, cell);
    let old = totals.iter().sum::<f64>();
    let remaining = totals
        .iter()
        .zip(fraction)
        .map(|(mass, scale)| mass * f64::from(scale))
        .sum::<f64>();
    if old > 0.0 {
        world.write.disease_count[cell] =
            (f64::from(world.write.disease_count[cell]) * remaining / old) as i32;
    }
    world.flow[cell] = [-FIXED_DT, FIXED_DT, FIXED_DT, -FIXED_DT];
}

#[cfg(test)]
mod space_tests {
    use super::*;

    fn table_with_gases(gases: usize) -> ElementTable {
        let mut descriptions = vec![(0x2d39_bf75_u32, 0), (0xa936_0b34, 0), (0x6d95_058c, 3)];
        descriptions.extend((0..gases).map(|id| (101 + id as u32, 1)));
        descriptions.push((1000, 2));
        let count = descriptions.len();
        let mut bytes = (count as i32).to_le_bytes().to_vec();
        for (index, (hash, phase)) in descriptions.into_iter().enumerate() {
            let mut raw = [0u8; 164];
            raw[..4].copy_from_slice(&hash.to_le_bytes());
            raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            raw[10] = phase;
            raw[12..16].copy_from_slice(&1.0f32.to_le_bytes());
            bytes.extend_from_slice(&raw);
        }
        bytes.resize(bytes.len() + count * 4, 0);
        ElementTable::parse(&bytes).unwrap()
    }

    #[test]
    fn space_drains_every_component_without_resurrection_but_backwalls_seal() {
        let table = table_with_gases(3);
        let mut w = PhysicsWorld::new(4, 4, 42, &table).unwrap();
        w.write.element.fill(0);
        w.inactive_mask = Some(vec![255; w.write.mass.len()]);
        let a = w.stride + 1;
        let sealed = a + 1;
        let water = a + 2;
        let interior = a + 3;
        for cell in [a, sealed, interior] {
            w.write.element[cell] = 3;
            w.write.mass[cell] = 0.04;
            w.mixture_element[cell] = 4;
            w.mixture_mass[cell] = 0.03;
            w.extra_mixtures.insert(cell, vec![(5, 0.02)]);
        }
        w.backwall_element[sealed] = 2;
        w.inactive_mask.as_mut().unwrap()[interior] = 0;
        w.backwall_element[water] = u16::MAX;
        w.write.element[water] = 6;
        w.write.mass[water] = 30.0;
        crate::multicomponent_flux::rebuild_gas_channels(&mut w, &table);
        let bounds = (1, 5, 1, 5);
        decay_inactive(&mut w, &table, bounds);
        assert!(w.write.mass[a] < 0.04 && w.mixture_mass[a] < 0.03);
        assert!(w.extra_mixtures[&a][0].1 < 0.02);
        assert!((w.write.mass[water] - 28.5).abs() < 1e-5);
        for _ in 0..4 {
            decay_inactive(&mut w, &table, bounds);
        }
        assert!(
            w.write.mass[a] > 0.0 && w.write.mass[water] > 0.0,
            "leakage must be gradual"
        );
        for _ in 0..512 {
            decay_inactive(&mut w, &table, bounds);
        }
        assert_eq!(w.write.mass[a], 0.0);
        assert_eq!(w.mixture_mass[a], 0.0);
        assert!(!w.extra_mixtures.contains_key(&a));
        assert_eq!(w.write.mass[water], 0.0);
        let mut events = EventQueues::default();
        crate::multicomponent_flux::project_cell_to_write(&mut w, &table, &mut events, a);
        assert_eq!(
            w.write.mass[a], 0.0,
            "publication cannot resurrect escaped gas"
        );
        assert_eq!(w.write.mass[sealed], 0.04);
        assert_eq!(w.mixture_mass[sealed], 0.03);
        assert_eq!(
            w.write.mass[interior], 0.04,
            "ordinary vacuum surroundings do not imply space exposure"
        );
    }

    #[test]
    fn gas_spreads_through_exposed_cells_while_leaking_gradually() {
        let table = table_with_gases(1);
        let mut world = PhysicsWorld::new(9, 9, 42, &table).unwrap();
        world.write.element.fill(world.vacuum_element);
        world.write.mass.fill(0.0);
        world.write.temperature.fill(300.0);
        world.write.properties.fill(0);
        world.inactive_mask = Some(vec![255; world.write.mass.len()]);
        world.backwall_element.fill(u16::MAX);
        let source = 4 * world.stride + 4;
        world.write.element[source] = 3;
        world.write.mass[source] = 0.01;
        let region = Region { min_x: 1, min_y: 1, max_x: 9, max_y: 9, ..Region::default() };
        for _ in 0..4 {
            let before: f32 = world.write.mass.iter().sum();
            crate::multicomponent_flux::step_region(&mut world, &table,
                &mut EventQueues::default(), region, None, false).unwrap();
            decay_inactive(&mut world, &table, (1, 9, 1, 9));
            let after: f32 = world.write.mass.iter().sum();
            assert!(after < before && after > before * 0.94, "exposure must leak progressively");
        }
        assert!(world.write.mass[source + 1] > 0.0);
        assert!(world.write.mass[source + world.stride] > 0.0);
    }

    #[test]
    fn small_space_parcels_survive_and_all_twenty_species_leak_equally() {
        let table = table_with_gases(20);
        let mut w = PhysicsWorld::new(4, 4, 42, &table).unwrap();
        let cell = w.stride + 1;
        w.write.element[cell] = 3;
        w.write.mass[cell] = 0.0001;
        w.mixture_element[cell] = 4;
        w.mixture_mass[cell] = 0.0001;
        w.extra_mixtures
            .insert(cell, (5..23).map(|id| (id, 0.0001)).collect());
        crate::multicomponent_flux::rebuild_gas_channels(&mut w, &table);
        apply_space_decay(&mut w, &table, cell);
        let mut count = 0;
        w.for_each_cell_species(cell, |_, mass| {
            count += 1;
            assert!(
                (mass - 0.000095).abs() < 1e-10,
                "every species keeps the same fraction"
            );
        });
        assert_eq!(count, 20, "space leakage must not truncate species");
        w.write.element[cell] = 23;
        w.write.mass[cell] = 0.005;
        w.clear_cell_mixtures(cell);
        apply_space_decay(&mut w, &table, cell);
        assert!(
            (w.write.mass[cell] - 0.00475).abs() < 1e-7,
            "small liquid parcels must survive too"
        );
    }
}
