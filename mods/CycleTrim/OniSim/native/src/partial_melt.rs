//! Official partial solid-to-liquid transition (DoPartialMelt).

use crate::events::EventQueues;
use crate::fluid_displacement::evaporate;
use crate::fluid_helpers::{clear_disease, cvtt, game_index, notify};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const MELT_MASS: f32 = 5.0;
const TEMP_MARGIN: f32 = 3.0;
const MIN_SOURCE_HEADROOM: f32 = 6.0;

fn rec(table: &ElementTable, id: u16) -> &ElementRecord {
    &table.elements[id as usize].record
}

fn pair(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    neighbor: usize,
) -> bool {
    let source_id = world.write.element[cell];
    let solid_id = world.write.element[neighbor];
    let source = rec(elements, source_id);
    let solid = rec(elements, solid_id);
    if source.state & 2 != 0 || solid.state & 3 != 3 {
        return false;
    }
    let solid_mass = world.write.mass[neighbor];
    if !(solid_mass > MELT_MASS) || world.write.properties[neighbor] & 0x48 != 0 {
        return false;
    }
    let transition = solid.high_transition;
    if transition == u16::MAX {
        return false;
    }
    let target = rec(elements, transition);
    let melt_temperature = solid.high_temperature + TEMP_MARGIN;
    let source_temperature = world.write.temperature[cell];
    let solid_temperature = world.write.temperature[neighbor];
    if !(melt_temperature < source_temperature)
        || !(solid_temperature < solid.high_temperature - TEMP_MARGIN)
    {
        return false;
    }
    let source_capacity = world.write.mass[cell] * source.specific_heat_capacity;
    let required =
        (melt_temperature - solid_temperature) * MELT_MASS * target.specific_heat_capacity;
    if !(required
        <= ((source_temperature - MIN_SOURCE_HEADROOM) - source.low_temperature) * source_capacity)
        || target.state & 3 != 2
    {
        return false;
    }

    let mut emitted = false;
    if !world.falling_disabled {
        if let (Some(public), Some(below)) =
            (game_index(world, cell), cell.checked_sub(world.stride))
        {
            let active = world.active_mask[public as usize] != 0 || world.simulate_all_active;
            if active
                && world.read.properties[below] & 2 == 0
                && target.low_temperature - TEMP_MARGIN <= melt_temperature
                && melt_temperature <= target.high_temperature + TEMP_MARGIN
            {
                let count = world.write.disease_count[neighbor];
                let removed_count = cvtt((count as f32) * (MELT_MASS / solid_mass));
                events.spawn_falling_liquid(
                    public,
                    transition,
                    world.write.disease_idx[neighbor],
                    MELT_MASS,
                    melt_temperature,
                    removed_count,
                );
                let remainder = (solid_temperature * solid_mass * solid.specific_heat_capacity
                    - required)
                    / (solid_mass * solid.specific_heat_capacity);
                world.write.temperature[neighbor] = remainder;
                world.write.mass[neighbor] -= MELT_MASS;
                world.write.disease_count[neighbor] = count.wrapping_sub(removed_count);
                if world.write.disease_count[neighbor] < 1 {
                    clear_disease(&mut world.write, neighbor);
                }
                emitted = true;
            }
        }
    }
    if emitted {
        if solid_mass <= MELT_MASS {
            evaporate(world, events, neighbor);
        }
        return true;
    }

    if !displace_gas(world, elements, diseases, events, cell, source_id) {
        return false;
    }
    world.write.element[cell] = transition;
    world.write.temperature[cell] = melt_temperature;
    world.write.mass[neighbor] -= MELT_MASS;
    let removed_count = cvtt(world.write.disease_count[neighbor] as f32 * (MELT_MASS / solid_mass));
    world.write.disease_count[neighbor] =
        world.write.disease_count[neighbor].wrapping_sub(removed_count);
    if world.write.disease_count[neighbor] < 1 {
        clear_disease(&mut world.write, neighbor);
    }
    if solid_mass <= MELT_MASS {
        evaporate(world, events, neighbor);
    }
    notify(world, events, cell);
    true
}

/// Try adjacent cells in the native wrapper's fixed order.
pub(crate) fn apply(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) -> bool {
    let neighbors = [
        cell.saturating_sub(world.stride),
        cell.saturating_sub(1),
        cell + 1,
        cell + world.stride,
    ];
    for neighbor in neighbors {
        if pair(world, elements, diseases, events, cell, neighbor) {
            return true;
        }
    }
    false
}
