pub(crate) use crate::disease_fields::add_disease_fields;
use crate::events::EventQueues;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{Cells, PhysicsWorld};

pub(crate) use crate::numeric::{cvtt_f32_i32 as cvtt, smax, smin};

pub(crate) fn game_index(world: &PhysicsWorld, cell: usize) -> Option<i32> {
    game_index_for_shape(world.width, world.height, world.stride, cell)
}

#[inline]
pub(crate) fn game_index_for_shape(
    width: usize,
    height: usize,
    stride: usize,
    cell: usize,
) -> Option<i32> {
    let stride = stride as i64;
    let index = (cell as i64 / stride - 1) * (stride - 2) + cell as i64 % stride - 1;
    (index >= 0 && index < (width * height) as i64).then_some(index as i32)
}

pub(crate) fn notify(world: &mut PhysicsWorld, events: &mut EventQueues, cell: usize) {
    if let Some(index) = game_index(world, cell) {
        events.substance_change(index);
    }
    world.mark_dirty(cell, 0x1f);
}

pub(crate) fn clear_disease(cells: &mut Cells, cell: usize) {
    cells.disease_idx[cell] = 255;
    cells.disease_count[cell] = 0;
    cells.disease_infestation[cell] = 0;
    cells.disease_growth[cell] = 0.0;
}

pub(crate) fn clear_cell(cells: &mut Cells, vacuum: u16, cell: usize) {
    cells.element[cell] = vacuum;
    cells.mass[cell] = 0.0;
    cells.temperature[cell] = 0.0;
    clear_disease(cells, cell);
}

pub(crate) fn calculate_disease(
    tables: &DiseaseTable,
    old_id: u8,
    old_count: i32,
    new_id: u8,
    new_count: i32,
) -> (u8, i32) {
    if old_id == new_id {
        return (old_id, old_count.wrapping_add(new_count));
    }
    if old_id == 255 {
        return (new_id, new_count);
    }
    if new_id == 255 {
        return (old_id, old_count);
    }
    let old_strength = tables.diseases[old_id as usize].strength * old_count as f32;
    let new_strength = tables.diseases[new_id as usize].strength * new_count as f32;
    if old_strength > new_strength {
        let remaining = cvtt(new_count as f32 - (old_strength / new_strength) * old_count as f32);
        if remaining < 0 {
            (old_id, remaining.wrapping_neg())
        } else {
            (new_id, remaining)
        }
    } else if old_count < 0 {
        (new_id, old_count.wrapping_neg())
    } else {
        (old_id, old_count)
    }
}

#[inline(always)]
pub(crate) fn add_disease(
    cells: &mut Cells,
    tables: &DiseaseTable,
    cell: usize,
    disease: u8,
    count: i32,
) {
    add_disease_fields(
        tables,
        &mut cells.disease_idx[cell],
        &mut cells.disease_count[cell],
        &mut cells.disease_infestation[cell],
        &mut cells.disease_growth[cell],
        disease,
        count,
    );
}

pub(crate) fn add_mass_and_update_temperature(
    cells: &mut Cells,
    diseases: &DiseaseTable,
    cell: usize,
    previous_mass: f32,
    previous_temperature: f32,
    amount: f32,
    temperature: f32,
    disease: u8,
    count: i32,
) {
    let sum = amount + previous_mass;
    if !(sum > 0.0) {
        cells.temperature[cell] = 0.0;
        clear_disease(cells, cell);
        return;
    }
    let weighted = (amount * temperature + previous_mass * previous_temperature) / sum;
    let bounded = if previous_temperature > temperature {
        if temperature <= weighted {
            smin(previous_temperature, weighted)
        } else {
            temperature
        }
    } else if previous_temperature <= weighted {
        smin(weighted, temperature)
    } else {
        previous_temperature
    };
    cells.mass[cell] = sum;
    cells.temperature[cell] = bounded;
    add_disease(cells, diseases, cell, disease, count);
}

pub(crate) fn add_mass(
    cells: &mut Cells,
    diseases: &DiseaseTable,
    cell: usize,
    amount: f32,
    temperature: f32,
    disease: u8,
    count: i32,
) {
    let previous_mass = cells.mass[cell];
    let previous_temperature = cells.temperature[cell];
    add_mass_and_update_temperature(
        cells,
        diseases,
        cell,
        previous_mass,
        previous_temperature,
        amount,
        temperature,
        disease,
        count,
    );
}

pub(crate) fn do_displacement(
    world: &mut PhysicsWorld,
    _tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
) {
    let mass = world.write.mass[source];
    let temperature = world.write.temperature[source];
    let disease = world.write.disease_idx[source];
    let count = world.write.disease_count[source];
    add_mass(
        &mut world.write,
        diseases,
        destination,
        mass,
        temperature,
        disease,
        count,
    );
    world.write.element[destination] = world.write.element[source];
    clear_cell(&mut world.write, world.vacuum_element, source);
    notify(world, events, source);
    notify(world, events, destination);
}

pub(crate) fn swap_material(
    world: &mut PhysicsWorld,
    events: &mut EventQueues,
    a: usize,
    b: usize,
) {
    world.write.element.swap(a, b);
    world.write.mass.swap(a, b);
    world.write.temperature.swap(a, b);
    world.write.disease_idx.swap(a, b);
    world.write.disease_count.swap(a, b);
    world.write.disease_infestation.swap(a, b);
    world.write.disease_growth.swap(a, b);
    notify(world, events, a);
    notify(world, events, b);
}

pub(crate) fn phase(tables: &ElementTable, element: u16) -> u8 {
    tables.elements[element as usize].record.state & 3
}
