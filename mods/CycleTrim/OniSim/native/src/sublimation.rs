//! Official neighbor-driven sublimation and off-gas transfer.

use crate::events::{EventQueues, SpawnFXInfo};
use crate::fluid_displacement::evaporate;
use crate::fluid_helpers::{add_mass_and_update_temperature, clear_disease, cvtt, game_index};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const MIN_SOURCE_MASS: f32 = 1.8;
const RANDOM_SCALE: f32 = f32::from_bits(0x3800_0100);
const RATE_SCALE: f32 = 0.2;
const RESIDUAL_MASS_EPSILON: f32 = 1.0e-9;

fn rec(table: &ElementTable, id: u16) -> &ElementRecord {
    &table.elements[id as usize].record
}

fn emit_fx(
    world: &PhysicsWorld,
    events: &mut EventQueues,
    cell: usize,
    fx_hash: i32,
    rotation: f32,
) {
    if let Some(game_cell) = game_index(world, cell) {
        events.spawn_fx_info.push(SpawnFXInfo {
            cell_idx: game_cell,
            fx_hash,
            rotation,
        });
    }
}

fn consume_neighbor(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    neighbor: usize,
    source_element: u16,
) -> bool {
    let current_id = world.write.element[cell];
    let neighbor_id = world.write.element[neighbor];
    let neighbor_record = rec(elements, neighbor_id);
    if neighbor_record.state & 3 != 3 || neighbor_record.sublimate_index == u16::MAX {
        return false;
    }
    let random = f32::from(world.next_random()) * RANDOM_SCALE;
    if !(random <= neighbor_record.sublimate_probability) {
        return false;
    }

    let rate = RATE_SCALE * neighbor_record.sublimate_rate;
    let neighbor_mass = world.write.mass[neighbor];
    let mut transfer = if neighbor_mass <= rate {
        neighbor_mass
    } else {
        rate
    };
    let remaining = neighbor_mass - transfer;
    let product = neighbor_record.sublimate_index;
    let fx_hash = neighbor_record.sublimate_fx;

    // When the donor cannot provide a full rate quantum, convert its residue
    // in place, matching the early native branch.
    if remaining < rate {
        world.write.element[neighbor] = product;
        world.write.mass[neighbor] = neighbor_mass * neighbor_record.sublimate_efficiency;
        if let Some(game) = game_index(world, neighbor) {
            events.substance_change(game);
            world.mark_dirty(neighbor, 0x1f);
            emit_fx(world, events, neighbor, fx_hash, 0.0);
        } else {
            world.mark_dirty(neighbor, 0x1f);
        }
        return true;
    }

    let donor_temperature = world.write.temperature[neighbor];
    let donor_disease = world.write.disease_idx[neighbor];
    let donor_count = world.write.disease_count[neighbor];
    let mut germs = cvtt((transfer / neighbor_mass) * donor_count as f32);
    let output_mass = neighbor_record.sublimate_efficiency * transfer;

    if product == source_element {
        let prior_mass = world.write.mass[cell];
        let prior_temperature = world.write.temperature[cell];
        let space = MIN_SOURCE_MASS - prior_mass;
        let output_mass = if output_mass > space {
            // ELF 0x71938 caps the product, scales donor removal, and
            // recomputes removed germs using the adjusted donor mass.
            transfer *= space / output_mass;
            germs = cvtt((transfer / neighbor_mass) * donor_count as f32);
            space
        } else {
            output_mass
        };
        add_mass_and_update_temperature(
            &mut world.write,
            diseases,
            cell,
            prior_mass,
            prior_temperature,
            output_mass,
            donor_temperature,
            donor_disease,
            germs,
        );
    } else {
        if rec(elements, source_element).state & 3 != 0
            && !displace_gas(world, elements, diseases, events, cell, current_id)
        {
            return false;
        }
        world.write.element[cell] = product;
        world.write.mass[cell] += output_mass;
        world.write.temperature[cell] = donor_temperature;
        world.write.disease_idx[cell] = donor_disease;
        world.write.disease_count[cell] = germs;
        if germs <= 0 {
            clear_disease(&mut world.write, cell);
        }
        world.write.disease_infestation[cell] = 0;
        world.write.disease_growth[cell] = 0.0;
        if let Some(game) = game_index(world, cell) {
            events.substance_change(game);
        }
        world.mark_dirty(cell, 0x1f);
    }

    world.write.mass[neighbor] -= transfer;
    world.write.disease_count[neighbor] = donor_count.wrapping_sub(germs);
    if world.write.disease_count[neighbor] < 1 {
        clear_disease(&mut world.write, neighbor);
    }
    // DoSublimation 0x71808 adds through SimData+0x58 (flow), not
    // CellSOA's radiation array. Keep the donor's radiation unchanged.
    world.accumulated_flow[neighbor] += transfer;

    let rotation = match neighbor as isize - cell as isize {
        -1 => -90.0,
        1 => 90.0,
        d if d == world.stride as isize => 180.0,
        _ => 0.0,
    };
    emit_fx(world, events, neighbor, fx_hash, rotation);
    if world.write.mass[neighbor] <= RESIDUAL_MASS_EPSILON {
        evaporate(world, events, neighbor);
    }
    true
}

/// Process the four neighbors in native order, preserving the native RNG draw
/// and event order. Returns whether any donor was processed.
pub(crate) fn apply(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) -> bool {
    let source_element = world.write.element[cell];
    apply_with_source(world, elements, diseases, events, cell, source_element)
}

/// Native PostProcessCell retains its initial ElementPostProcessData pointer
/// through earlier substeps, even if those steps change the active element.
pub(crate) fn apply_with_source(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    source_element: u16,
) -> bool {
    if world.write.mass[cell] >= MIN_SOURCE_MASS {
        return false;
    }
    let neighbors = [
        cell + world.stride,
        cell + 1,
        cell.saturating_sub(1),
        cell.saturating_sub(world.stride),
    ];
    let mut changed = false;
    for neighbor in neighbors {
        changed |= consume_neighbor(
            world,
            elements,
            diseases,
            events,
            cell,
            neighbor,
            source_element,
        );
    }
    changed
}
