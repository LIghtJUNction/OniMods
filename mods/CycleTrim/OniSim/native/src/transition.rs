//! Phase transitions and their ordered official event side effects.

use crate::events::EventQueues;
use crate::fluid_helpers::{clear_cell, cvtt, game_index};
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const PHASE_MARGIN: f32 = 3.0;
const LOW_TRANSITION_SHIFT: f32 = 1.5;
const HIGH_TRANSITION_SHIFT: f32 = 1.5;
const LOW_SHIFT_FLOOR: f32 = -1.5;
const HIGH_SHIFT_FLOOR: f32 = 1.5;
const ORE_MASS_EPSILON: f32 = 0.001;
const SOLID_MASS_FRACTION: f32 = 0.8;
const VACUUM_HASH: u32 = 0x2d39_bf75;

fn record(table: &ElementTable, index: u16) -> &ElementRecord {
    &table.elements[index as usize].record
}

fn resolve_ore(table: &ElementTable, hash: i32) -> u16 {
    if hash as u32 == VACUUM_HASH {
        return u16::MAX;
    }
    table
        .elements
        .iter()
        .position(|element| element.record.id == hash)
        .and_then(|index| u16::try_from(index).ok())
        .unwrap_or(u16::MAX)
}

/// Apply one official state transition to the active cell buffer.
/// Returns false only when the official mass/temperature/target gates do not
/// select a transition.
pub(crate) fn do_state_transition(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
) -> bool {
    let source_element = world.read.element[cell];
    do_state_transition_from_element(world, elements, events, cell, source_element)
}

/// Apply a transition using the active element. ModifyEnergy calls
/// DoStateTransition immediately after changing that active cell in place.
pub(crate) fn do_state_transition_from_active(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
) -> bool {
    let source_element = world.write.element[cell];
    do_state_transition_from_element(world, elements, events, cell, source_element)
}

fn do_state_transition_from_element(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    source_element: u16,
) -> bool {
    let mass = world.write.mass[cell];
    if mass <= 0.0 {
        return false;
    }
    let source = record(elements, source_element);
    let temperature = world.write.temperature[cell];

    // Match the native short-circuit branch order, including a missing low
    // target falling through to the high-temperature check.
    let (target, ore, conversion, shifted_temperature) = if source.low_temperature - PHASE_MARGIN
        <= temperature
        || source.low_transition == u16::MAX
    {
        if temperature <= PHASE_MARGIN + source.high_temperature
            || source.high_transition == u16::MAX
        {
            return false;
        }
        let next_temperature = if temperature > HIGH_SHIFT_FLOOR {
            temperature - HIGH_TRANSITION_SHIFT
        } else {
            0.0
        };
        (
            source.high_transition,
            resolve_ore(elements, source.high_ore_id),
            source.high_ore_conversion,
            next_temperature,
        )
    } else {
        let next_temperature = if temperature > LOW_SHIFT_FLOOR {
            temperature + LOW_TRANSITION_SHIFT
        } else {
            0.0
        };
        (
            source.low_transition,
            resolve_ore(elements, source.low_ore_id),
            source.low_ore_conversion,
            next_temperature,
        )
    };

    world.write.temperature[cell] = shifted_temperature;
    world.write.element[cell] = target;

    // Ore conversion is queued at the transitioned game cell. Mass and germ
    // counts are removed only when that cell maps to a valid public cell.
    if ore != u16::MAX {
        let ore_mass = world.write.mass[cell] * conversion;
        if ore_mass > ORE_MASS_EPSILON
            && let Some(game_cell) = game_index(world, cell)
        {
            let germ_count = cvtt(conversion * world.write.disease_count[cell] as f32);
            events.spawn_ore(
                game_cell,
                ore,
                world.write.disease_idx[cell],
                ore_mass,
                shifted_temperature,
                germ_count,
            );
            world.write.mass[cell] -= ore_mass;
            world.write.disease_count[cell] =
                world.write.disease_count[cell].wrapping_sub(germ_count);
            if world.write.disease_count[cell] < 1 {
                world.write.disease_idx[cell] = u8::MAX;
                world.write.disease_count[cell] = 0;
                world.write.disease_infestation[cell] = 0;
                world.write.disease_growth[cell] = 0.0;
            }
        }
    }

    // CellMelted is emitted after ore conversion, but before any special
    // falling/solid ejection and before the generic substance-change event.
    if world.write.properties[cell] & 0x40 != 0 {
        if let Some(game_cell) = game_index(world, cell) {
            events.cell_melted(game_cell);
        }
        world.mark_dirty(cell, 0x1f);
    }

    let target_record = record(elements, target);
    let current_mass = world.write.mass[cell];
    let current_temperature = world.write.temperature[cell];
    let disease = world.write.disease_idx[cell];
    let disease_count = world.write.disease_count[cell];
    let game_cell = game_index(world, cell);
    let below = cell.checked_sub(world.stride);

    if target_record.state & 3 == 3 {
        // Official ElementTemperatureData +0x14 is default mass. A small
        // solid transition is emitted as ore and the source cell is cleared
        // only when its cell is active or debug editing is enabled.
        if !(SOLID_MASS_FRACTION < current_mass / target_record.default_mass || current_mass <= 0.0)
            && let Some(game) = game_cell
            && (world.simulate_all_active || world.active_mask[game as usize] != 0)
        {
            events.spawn_ore(
                game,
                target,
                disease,
                current_mass,
                current_temperature,
                disease_count,
            );
            clear_cell(&mut world.write, world.vacuum_element, cell);
        }
    } else if !world.falling_disabled
        && let (Some(game), Some(below)) = (game_cell, below)
        && world.read.properties[below] & 2 == 0
        && (world.simulate_all_active || world.active_mask[game as usize] != 0)
        && target_record.low_temperature - PHASE_MARGIN <= current_temperature
        && current_temperature <= target_record.high_temperature + PHASE_MARGIN
    {
        events.spawn_falling_liquid(
            game,
            target,
            disease,
            current_mass,
            current_temperature,
            disease_count,
        );
        clear_cell(&mut world.write, world.vacuum_element, cell);
    }

    // The native computes the public index after special ejection/clear; an
    // invalid padded cell still receives the transition and dirty bits.
    if let Some(game_cell) = game_index(world, cell) {
        events.substance_change(game_cell);
    }
    world.mark_dirty(cell, 0x1f);
    true
}
