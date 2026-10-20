use crate::events::EventQueues;
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{add_mass, clear_cell, cvtt, notify, phase};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

// AddGas, ELF 0x5f700. Incoming defaults belong to the command processor.
pub(crate) fn add_gas(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    element: u16,
    mass: f32,
    temperature: f32,
    disease: u8,
    count: i32,
) {
    let previous = world.write.element[cell];
    if previous == element {
        add_mass(
            &mut world.write,
            diseases,
            cell,
            mass,
            temperature,
            disease,
            count,
        );
        return;
    }
    let state = phase(elements, previous);
    let displaced = match state {
        1 => displace_gas(world, elements, diseases, events, cell, previous),
        2 => displace_liquid(world, elements, diseases, events, cell, previous),
        _ => false,
    };
    if state == 0 || displaced {
        world.write.element[cell] = element;
        add_mass(
            &mut world.write,
            diseases,
            cell,
            mass,
            temperature,
            disease,
            count,
        );
        notify(world, events, cell);
        return;
    }
    // Each neighbor gets its empty check before its same-element check.
    for destination in [cell - 1, cell + 1, cell + world.stride] {
        let previous = world.write.element[destination];
        if previous == world.vacuum_element {
            world.write.element[destination] = element;
            world.write.mass[destination] = mass;
            world.write.temperature[destination] = temperature;
            world.write.disease_idx[destination] = disease;
            world.write.disease_count[destination] = count;
            // Direct placement retains both disease auxiliary fields.
            notify(world, events, destination);
            return;
        }
        if previous == element {
            add_mass(
                &mut world.write,
                diseases,
                destination,
                mass,
                temperature,
                disease,
                count,
            );
            return;
        }
    }
    let previous_mass = world.write.mass[cell];
    // COMISS incoming,previous / JAE: unordered follows subtraction too.
    if !(mass >= previous_mass) {
        world.write.mass[cell] = previous_mass - mass;
        return;
    }
    let remaining = mass - previous_mass;
    world.write.element[cell] = element;
    world.write.mass[cell] = 0.0;
    world.write.temperature[cell] = 0.0;
    let remaining_count = cvtt((remaining / mass) * count as f32);
    add_mass(
        &mut world.write,
        diseases,
        cell,
        remaining,
        temperature,
        disease,
        remaining_count,
    );
    let result = world.write.mass[cell];
    // The zero/unordered diagnostic branch also leads to ClearCell.
    if result.is_nan() || result <= f32::from_bits(0x00800000) {
        clear_cell(&mut world.write, world.vacuum_element, cell);
    }
    notify(world, events, cell);
}
