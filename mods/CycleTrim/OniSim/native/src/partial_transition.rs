//! Official partial heat transition between adjacent cells.

use crate::events::EventQueues;
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{clear_disease, cvtt, game_index, notify};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const TRANSFER_MASS: f32 = 5.0;
const TRANSITION_MARGIN: f32 = 3.0;
const MIN_TARGET_MARGIN: f32 = 13.0;
const COOLING_CAP_MULTIPLIER: f32 = 10.0;

fn rec(table: &ElementTable, id: u16) -> &ElementRecord {
    &table.elements[id as usize].record
}

fn reduce_germs(world: &mut PhysicsWorld, cell: usize, amount: i32) {
    world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_sub(amount);
    if world.write.disease_count[cell] < 1 {
        clear_disease(&mut world.write, cell);
    }
}

fn restore_germs(world: &mut PhysicsWorld, cell: usize, amount: i32) {
    world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_add(amount);
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
    let source = rec(elements, source_id);
    let mass = world.write.mass[cell];
    if mass < TRANSFER_MASS || source.high_transition == u16::MAX {
        return false;
    }
    let target_id = source.high_transition;
    let target = rec(elements, target_id);
    let upper = source.high_temperature + TRANSITION_MARGIN;
    let neighbor_temperature = world.write.temperature[neighbor];
    if neighbor_temperature <= upper
        || neighbor_temperature
            < rec(elements, world.write.element[neighbor]).low_temperature + MIN_TARGET_MARGIN
    {
        return false;
    }
    let source_temperature = world.write.temperature[cell];
    if source_temperature >= source.high_temperature - TRANSITION_MARGIN {
        return false;
    }
    let energy = source.specific_heat_capacity * TRANSFER_MASS * (upper - source_temperature);
    let neighbor_capacity = world.write.mass[neighbor]
        * rec(elements, world.write.element[neighbor]).specific_heat_capacity;
    if COOLING_CAP_MULTIPLIER * neighbor_capacity < energy {
        return false;
    }
    let neighbor_new_temperature =
        (neighbor_capacity * neighbor_temperature - energy) / neighbor_capacity;
    let removed_germs = cvtt(world.write.disease_count[cell] as f32 * (TRANSFER_MASS / mass));
    let target_phase = target.state & 3;
    if target_phase != 1 && target_phase != 2 {
        return false;
    }

    world.write.mass[cell] -= TRANSFER_MASS;
    reduce_germs(world, cell, removed_germs);
    world.write.temperature[neighbor] = neighbor_new_temperature;

    if target_phase == 1 {
        let above = cell + world.stride;
        let above_id = world.write.element[above];
        if rec(elements, above_id).state & 3 == 1
            && displace_gas(world, elements, diseases, events, above, above_id)
        {
            world.write.mass[above] = TRANSFER_MASS;
            world.write.temperature[above] = upper;
            world.write.element[above] = target_id;
            notify(world, events, above);
            return true;
        }
    }

    if !displace_liquid(world, elements, diseases, events, cell, source_id) {
        world.write.mass[cell] += TRANSFER_MASS;
        restore_germs(world, cell, removed_germs);
        // ELF 0x71132 restores the cooled neighbour after displacement fails;
        // the source keeps its own temperature.
        world.write.temperature[neighbor] = neighbor_temperature;
        return false;
    }

    world.write.mass[cell] = TRANSFER_MASS;
    world.write.temperature[cell] = upper;
    world.write.element[cell] = target_id;
    if target_phase == 1 && !world.falling_disabled {
        let below = cell.saturating_sub(world.stride);
        if let Some(public) = game_index(world, cell)
            && world.read.properties[below] & 2 == 0
            && (world.active_mask[public as usize] != 0 || world.simulate_all_active)
            && target.low_temperature - TRANSITION_MARGIN <= upper
            && upper <= target.high_temperature + TRANSITION_MARGIN
        {
            events.spawn_falling_liquid(
                public,
                target_id,
                world.write.disease_idx[cell],
                TRANSFER_MASS,
                upper,
                removed_germs,
            );
            world.mark_dirty(cell, 0x1f);
            return true;
        }
    }
    if let Some(public) = game_index(world, cell) {
        events.substance_change(public);
    }
    world.mark_dirty(cell, 0x1f);
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
