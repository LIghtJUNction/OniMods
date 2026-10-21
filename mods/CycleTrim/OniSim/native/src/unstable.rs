use crate::events::{EventQueues, UnstableCellInfo};
use crate::fluid_helpers::{clear_disease, game_index, notify, phase};
use crate::numeric::cvtt_f32_i32;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

// GetStableTicksRemaining returns the decremented/new counter in AL.
fn stable_ticks(world: &mut PhysicsWorld, cell: usize) -> u8 {
    let previous = world.dirty_flags[cell];
    let count = previous & 31;
    let next = if count == 31 {
        let sample = world.next_random() as f32;
        cvtt_f32_i32(sample * f32::from_bits(0x38c00180)).wrapping_add(3) as u8
    } else {
        count.saturating_sub(1)
    };
    world.dirty_flags[cell] = (previous & 0xe0) | (next & 31);
    next
}

fn swap_without_events(world: &mut PhysicsWorld, a: usize, b: usize) {
    world.write.element.swap(a, b);
    world.write.mass.swap(a, b);
    world.write.temperature.swap(a, b);
    world.write.disease_idx.swap(a, b);
    world.write.disease_count.swap(a, b);
    world.write.disease_infestation.swap(a, b);
    world.write.disease_growth.swap(a, b);
}

pub(crate) fn headless_swap(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
) {
    let mut current = cell;
    while let Some(below) = current.checked_sub(world.stride) {
        if phase(elements, world.write.element[below]) == 3 {
            notify(world, events, current);
            return;
        }
        swap_without_events(world, current, below);
        // The original publishes the old position after each swap; the new
        // position is published by the next iteration or final solid check.
        notify(world, events, current);
        current = below;
    }
}

fn emit_falling(
    world: &mut PhysicsWorld,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
    mass: f32,
    count: i32,
) {
    if let Some(cell_idx) = game_index(world, destination) {
        events.unstable_cell_info.push(UnstableCellInfo {
            cell_idx,
            elem_idx: world.write.element[source],
            falling_info: 0,
            disease_idx: world.write.disease_idx[source],
            mass,
            temperature: world.write.temperature[source],
            disease_count: count,
        });
    }
}

fn blocked(world: &PhysicsWorld, elements: &ElementTable, cell: usize) -> bool {
    phase(elements, world.write.element[cell]) == 3
        || world.write.element[cell] == world.sink_element
        || world.write.properties[cell] & 4 != 0
}

fn basic(world: &mut PhysicsWorld, elements: &ElementTable, events: &mut EventQueues, cell: usize) {
    if blocked(world, elements, cell - world.stride) {
        world.mark_dirty(cell, 31);
    } else if stable_ticks(world, cell) == 0 {
        if world.falling_disabled {
            headless_swap(world, elements, events, cell);
        } else {
            let mass = world.write.mass[cell];
            let count = world.write.disease_count[cell];
            emit_falling(world, events, cell, cell, mass, count);
            crate::fluid_displacement::evaporate(world, events, cell);
        }
    }
}

fn diagonals(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) {
    let original_mass = world.write.mass[cell];
    let original_count = world.write.disease_count[cell];
    let minimum = elements.elements[world.write.element[cell] as usize]
        .record
        .min_horizontal_flow;
    let stride = world.stride;
    let mut found = false;
    let mut remaining = u8::MAX;
    for (destination, fraction, required, clear_above) in [
        (cell - stride, 1.0, 0.0, false),
        (cell - stride - 1, 0.5, 1.0, true),
        (cell - stride + 1, 0.5, 1.0, true),
    ] {
        if !((world.write.mass[cell] - required * minimum) * fraction > 0.0)
            || blocked(world, elements, destination)
            || (clear_above
                && (phase(elements, world.write.element[destination + stride]) == 3
                    || world.write.properties[destination + stride] & 4 != 0))
        {
            continue;
        }
        if !found {
            remaining = stable_ticks(world, cell);
        }
        found = true;
        if remaining != 0 {
            continue;
        }
        world.mark_dirty(cell, 31);
        if !world.falling_disabled {
            let mass = fraction * original_mass;
            let count = cvtt_f32_i32(original_count as f32 * fraction);
            emit_falling(world, events, cell, destination, mass, count);
            world.write.mass[cell] -= mass;
            world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_sub(count);
            if world.write.disease_count[cell] < 1 {
                clear_disease(&mut world.write, cell);
            }
            notify(world, events, cell);
            if world.write.mass[cell] <= 0.0 {
                crate::fluid_displacement::evaporate(world, events, cell);
            }
        } else {
            let destination_element = world.write.element[destination];
            match phase(elements, destination_element) {
                1 => {
                    crate::gas::displace_gas(
                        world,
                        elements,
                        diseases,
                        events,
                        destination,
                        destination_element,
                    );
                }
                2 => {
                    crate::fluid_displacement::displace_liquid(
                        world,
                        elements,
                        diseases,
                        events,
                        destination,
                        destination_element,
                    );
                }
                _ => {}
            }
            let mass = world.write.mass[cell] * fraction;
            let count = cvtt_f32_i32(world.write.disease_count[cell] as f32 * fraction);
            world.write.element[destination] = world.write.element[cell];
            world.write.mass[destination] = mass;
            world.write.temperature[destination] = world.write.temperature[cell];
            world.write.disease_idx[destination] = world.write.disease_idx[cell];
            world.write.disease_count[destination] = count;
            // No destination infestation/growth reset occurs in this path.
            notify(world, events, destination);
            world.write.mass[cell] -= mass;
            world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_sub(count);
            if world.write.disease_count[cell] < 1 {
                clear_disease(&mut world.write, cell);
            }
            notify(world, events, cell);
            if world.write.mass[cell] <= f32::MIN_POSITIVE {
                crate::fluid_displacement::evaporate(world, events, cell);
            }
            headless_swap(world, elements, events, destination);
        }
    }
    if !found {
        world.mark_dirty(cell, 31);
    }
}

pub(crate) fn apply(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) {
    if world.saved_options & 1 == 0 {
        basic(world, elements, events, cell);
    } else {
        diagonals(world, elements, diseases, events, cell);
    }
}
