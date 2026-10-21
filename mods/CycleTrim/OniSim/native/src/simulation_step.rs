//! Serial form of SimBase::UpdateData (official ELF 0x69730).
//!
//! Every copy below is the complete active CellSOA -> old CellSOA copy in the
//! original. Post-fluid copies are omitted only when the phase reached no
//! CellSOA-mutating path and read/write still contain the same words.
//! In particular there is no copy between disease diffusion,
//! components, and postprocessing, nor at the end of a region.

use crate::events::EventQueues;
use crate::mass_components::MassComponents;
use crate::radiation::RadiationEmitters;
use crate::simulation_step_fluid::{gas_phase, liquid_phase};
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
    ]
    .iter()
    .any(|&length| length != count)
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
        world.sync_read_from_write();
        for index in 0..world.regions.len() {
            let region = world.regions[index];
            let mut temperature_events = EventQueues::default();
            temperature_phase(world, elements, &mut temperature_events, region);
            world.sync_read_from_write();
            if gas_phase(world, elements, diseases, events, region) {
                world.sync_read_from_write();
            }
            // Gas events are already in the main queue. Temperature task events
            // join after them, at ConsolidateEvents, rather than at task execution.
            consolidate_events(events, &mut temperature_events);
            // read and write Cells are equal at phase entry. If no cell-mutating
            // helper was reached, copying those same bytes again is redundant.
            // Flow/event work still executes in its original order.
            if liquid_phase(world, elements, diseases, events, region) {
                world.sync_read_from_write();
            }
            disease_diffusion(world, elements, diseases, events, region)?;
            if world.radiation_enabled {
                crate::radiation::update_region(world, elements, diseases, region)?;
            }
            update_components(world, elements, diseases, events, systems, &region)?;
            let bounds = postprocess_bounds(world, region);
            decay_inactive(world, elements, bounds);
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
            crate::disease::post_process_bounds(
                world, diseases, bounds.0, bounds.1, bounds.2, bounds.3,
            )?;
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

fn update_components(
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
    systems
        .radiation
        .update(FIXED_DT, world, elements, *region)?;
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

fn disease_diffusion(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) -> Result<()> {
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            if world.read.disease_idx[cell] != u8::MAX
                || world.read.disease_idx[cell + 1] != u8::MAX
            {
                crate::disease::update_cells(
                    world,
                    elements,
                    diseases,
                    events,
                    FIXED_DT,
                    cell,
                    cell + 1,
                )?;
            }
            if world.read.disease_idx[cell] != u8::MAX
                || world.read.disease_idx[cell + world.stride] != u8::MAX
            {
                crate::disease::update_cells(
                    world,
                    elements,
                    diseases,
                    events,
                    FIXED_DT,
                    cell,
                    cell + world.stride,
                )?;
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
