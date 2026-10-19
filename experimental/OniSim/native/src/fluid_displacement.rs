use crate::events::EventQueues;
use crate::fluid_helpers::{add_mass, clear_cell, notify, phase};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

// ELF 0x53730: candidate order and duplicates are retained.
pub(crate) fn displace_liquid_simple(
    world: &mut PhysicsWorld,
    _elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    source: usize,
    element: u16,
    candidates: &[usize],
) -> bool {
    if source >= world.write.mass.len() {
        return false;
    }
    let initial_mass = world.write.mass[source];
    if initial_mass < f32::from_bits(0x3c23d70a) || candidates.is_empty() {
        return false;
    }
    debug_assert!(candidates.len() <= 4);
    let mut accepted = [0usize; 4];
    let mut count = 0;
    for &cell in candidates {
        if cell >= world.write.mass.len() {
            continue;
        }
        let destination_element = world.write.element[cell];
        if (destination_element == element || destination_element == world.vacuum_element)
            && world.write.properties[cell] & 2 == 0
        {
            accepted[count] = cell;
            count += 1;
        }
    }
    if count == 0 {
        return false;
    }
    let divisor = count as i32;
    let germ_count = world.write.disease_count[source]
        .wrapping_add(divisor)
        .wrapping_sub(1)
        / divisor;
    let amount = initial_mass / count as f32;
    for &destination in &accepted[..count] {
        let temperature = world.write.temperature[source];
        let disease = world.write.disease_idx[source];
        add_mass(
            &mut world.write,
            diseases,
            destination,
            amount,
            temperature,
            disease,
            germ_count,
        );
        world.write.element[destination] = element;
        notify(world, events, destination);
    }
    clear_cell(&mut world.write, world.vacuum_element, source);
    notify(world, events, source);
    true
}

// ELF 0x53a20: a failed first distribution may move one adjacent gas cell.
pub(crate) fn displace_liquid(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    source: usize,
    element: u16,
) -> bool {
    if source >= world.write.mass.len()
        || world.write.mass[source] <= 0.0
        || phase(elements, world.write.element[source]) != 2
    {
        return false;
    }
    let candidates = [
        source.checked_add(1).unwrap_or(usize::MAX),
        source.checked_sub(1).unwrap_or(usize::MAX),
        source.checked_add(world.stride).unwrap_or(usize::MAX),
        source.checked_sub(world.stride).unwrap_or(usize::MAX),
    ];
    if displace_liquid_simple(
        world,
        elements,
        diseases,
        events,
        source,
        element,
        &candidates,
    ) {
        return true;
    }
    for offset in 0..4 {
        let destination = candidates[(world.tick_seed as usize + offset) & 3];
        if destination >= world.write.mass.len() {
            continue;
        }
        let destination_element = world.write.element[destination];
        if phase(elements, destination_element) == 1
            && displace_gas(
                world,
                elements,
                diseases,
                events,
                destination,
                destination_element,
            )
        {
            return displace_liquid_simple(
                world,
                elements,
                diseases,
                events,
                source,
                element,
                &candidates,
            );
        }
    }
    false
}

// ELF 0x6f510: one clear, then one substance notification.
pub(crate) fn evaporate(world: &mut PhysicsWorld, events: &mut EventQueues, cell: usize) {
    clear_cell(&mut world.write, world.vacuum_element, cell);
    notify(world, events, cell);
}
