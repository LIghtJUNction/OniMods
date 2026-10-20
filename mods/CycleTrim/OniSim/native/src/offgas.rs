//! Shared official off-gas mass transfer helper (FUN_0016f980).

use crate::events::EventQueues;
use crate::fluid_helpers::{clear_disease, cvtt, game_index};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

/// Transfer `amount` out of `source` into `destination`. When `merge_disease`
/// is true, disease counts follow the source mass proportionally.
pub(crate) fn transfer(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
    amount: f32,
    merge_disease: bool,
) {
    let source_element = world.write.element[source];
    transfer_with_source(
        world,
        elements,
        events,
        source,
        destination,
        amount,
        merge_disease,
        source_element,
    );
}

/// Variant for callers that captured the native post-process record before
/// another phase changed the active element at `source`.
pub(crate) fn transfer_with_source(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
    amount: f32,
    merge_disease: bool,
    source_element: u16,
) {
    let destination_element = world.write.element[destination];
    let source_record = &elements.elements[source_element as usize].record;
    let output_element = source_record.sublimate_index;
    let source_mass = world.write.mass[source];
    let source_temperature = world.write.temperature[source];
    let source_count = world.write.disease_count[source];

    world.write.mass[source] = source_mass - amount;
    let moved_germs = if merge_disease {
        let count = cvtt(source_count as f32 * (amount / source_mass));
        world.write.disease_count[source] = source_count.wrapping_sub(count);
        if world.write.disease_count[source] < 1 {
            clear_disease(&mut world.write, source);
        }
        count
    } else {
        0
    };

    if merge_disease && world.write.disease_idx[destination] != world.write.disease_idx[source] {
        clear_disease(&mut world.write, destination);
        world.write.disease_idx[destination] = world.write.disease_idx[source];
    }

    let prior_mass = if destination_element == output_element {
        world.write.mass[destination]
    } else {
        world.write.element[destination] = output_element;
        if let Some(game) = game_index(world, destination) {
            events.substance_change(game);
        }
        world.mark_dirty(destination, 0x1f);
        clear_disease(&mut world.write, destination);
        0.0
    };
    let prior_temperature = world.write.temperature[destination];
    let emitted_mass = amount * source_record.sublimate_efficiency;
    let total_mass = emitted_mass + prior_mass;
    let new_temperature = if total_mass <= 0.0 {
        0.0
    } else {
        let weighted =
            (prior_mass * prior_temperature + emitted_mass * source_temperature) / total_mass;
        if source_temperature < prior_temperature {
            if source_temperature <= weighted {
                if weighted <= prior_temperature {
                    weighted
                } else {
                    prior_temperature
                }
            } else {
                prior_temperature
            }
        } else {
            let candidate = if weighted <= source_temperature {
                weighted
            } else {
                source_temperature
            };
            if prior_temperature <= weighted {
                candidate
            } else {
                prior_temperature
            }
        }
    };
    world.write.temperature[destination] = new_temperature;
    world.write.mass[destination] = prior_mass + emitted_mass;
    world.write.disease_count[destination] =
        world.write.disease_count[destination].wrapping_add(moved_germs);
    if world.write.disease_count[destination] < 1 {
        clear_disease(&mut world.write, destination);
    }
}
