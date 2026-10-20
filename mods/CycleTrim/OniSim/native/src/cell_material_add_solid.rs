use crate::events::EventQueues;
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{add_mass, cvtt, notify, phase, smin};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

fn fill_existing(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    cell: usize,
    element: u16,
    remaining: &mut f32,
    temperature: f32,
    disease: u8,
    disease_count: i32,
) {
    if world.write.element[cell] != element {
        return;
    }
    // CreateElementsTable 0x37df2/0x37e03 maps postprocess +0x20 to max_mass.
    let capacity = elements.elements[element as usize].record.max_mass - world.write.mass[cell];
    if !(capacity > 0.0) {
        return;
    }
    let amount = smin(capacity, *remaining);
    let count = cvtt(disease_count as f32 * (amount / *remaining));
    add_mass(
        &mut world.write,
        diseases,
        cell,
        amount,
        temperature,
        disease,
        count,
    );
    *remaining -= amount;
}

// ELF 0x5f0a0: subtype 0 fills a column; subtype 1 adds at the given cell.
pub(crate) fn add_solid(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    element: u16,
    mut mass: f32,
    temperature: f32,
    disease: u8,
    disease_count: i32,
    subtype: i32,
) {
    if subtype == 1 {
        let previous = world.write.element[cell];
        match phase(elements, previous) {
            3 if previous != element => return,
            1 => {
                displace_gas(world, elements, diseases, events, cell, previous);
            }
            2 => {
                displace_liquid(world, elements, diseases, events, cell, previous);
            }
            _ => {}
        }
        // This path does not assign the incoming element, even after displacement fails.
        add_mass(
            &mut world.write,
            diseases,
            cell,
            mass,
            temperature,
            disease,
            disease_count,
        );
        // The official temperature assertion at 0x5f13e..0x5f18e only reports;
        // it does not clamp or roll back the preceding addition.
        notify(world, events, cell);
        return;
    }
    if subtype != 0 {
        return;
    }

    let stride = world.stride;
    let mut row = cell / stride;
    let column = cell % stride;
    fill_existing(
        world,
        elements,
        diseases,
        cell - stride,
        element,
        &mut mass,
        temperature,
        disease,
        disease_count,
    );
    fill_existing(
        world,
        elements,
        diseases,
        cell,
        element,
        &mut mass,
        temperature,
        disease,
        disease_count,
    );
    if !(mass > 0.0) || row >= world.padded_height - 1 {
        return;
    }

    let mut destination = row * stride + column;
    while phase(elements, world.write.element[destination]) == 3 {
        row += 1;
        if row == world.padded_height - 1 {
            return;
        }
        destination += stride;
    }
    let previous = world.write.element[destination];
    if elements.elements[previous as usize].record.id as u32 == 0xa9360b34 {
        return;
    }
    match phase(elements, previous) {
        1 => {
            displace_gas(world, elements, diseases, events, destination, previous);
        }
        2 => {
            displace_liquid(world, elements, diseases, events, destination, previous);
        }
        _ => {}
    }
    world.write.element[destination] = element;
    world.write.mass[destination] = mass;
    world.write.temperature[destination] = temperature;
    // The official assertion at 0x5f321..0x5f365 only reports invalid
    // nonempty temperatures or temperatures above 10000.0, then continues.
    notify(world, events, destination);
    // Both fills and final placement retain the original input disease count.
    world.write.disease_idx[destination] = disease;
    world.write.disease_count[destination] = disease_count;
    world.write.disease_infestation[destination] = 0;
    world.write.disease_growth[destination] = 0.0;
}
