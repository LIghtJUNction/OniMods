//! Official temperature exchange kernels, reconstructed from libSimDLL.

use crate::fluid_helpers::game_index;
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const MIN_TEMPERATURE: f32 = 1.0;
const MAX_TEMPERATURE: f32 = 10_000.0;
const INSULATION_GEOMETRIC_MEAN_THRESHOLD: f32 = f32::from_bits(0x477e_00fe);
const CONDUCTIVITY_SCALE: f32 = f32::from_bits(0x3781_0183);
const MAX_TRANSFER_FRACTION: f64 = f64::from_bits(0x3fc9_9999_a000_0000);
const CELL_TRANSFER_STEP: f64 = 0.25;
const BACKWALL_TRANSFER_STEP: f64 = 2.0;
const GAS_MULTIPLIER: f32 = 1.0;

fn record(table: &ElementTable, index: u16) -> &ElementRecord {
    &table.elements[index as usize].record
}

fn phase_multiplier(record: &ElementRecord, phase: u8) -> f32 {
    match phase & 3 {
        1 => record.surface_area_gas,
        2 => record.surface_area_liquid,
        3 => record.surface_area_solid,
        _ => GAS_MULTIPLIER,
    }
}

fn ordered_min(a: f64, b: f64) -> f64 {
    if a <= b {
        a
    } else {
        b
    }
}

fn ordered_min_f32(a: f32, b: f32) -> f32 {
    if a <= b {
        a
    } else {
        b
    }
}

fn insulation_conductivity(
    table: &ElementTable,
    element_a: u16,
    element_b: u16,
    insulation_a: u8,
    insulation_b: u8,
) -> (f32, f32) {
    let a = record(table, element_a);
    let b = record(table, element_b);
    let ia = f32::from(insulation_a);
    let ib = f32::from(insulation_b);
    let ia2 = ia * ia;
    let ib2 = ib * ib;
    let ka = ia2 * CONDUCTIVITY_SCALE * a.thermal_conductivity;
    let kb = b.thermal_conductivity * CONDUCTIVITY_SCALE * ib2;
    let mixed =
        if ia2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD || ib2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
            ordered_min_f32(kb, ka)
        } else {
            (ka.ln() + kb.ln()) * 0.5
        };
    let effective =
        if ia2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD || ib2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
            mixed
        } else {
            mixed.exp()
        };
    (
        effective,
        phase_multiplier(a, b.state) * phase_multiplier(b, a.state),
    )
}

/// Update one pair using the official old-snapshot/new-write temperature rule.
/// `a` and `b` are padded internal indexes selected by the element kernel.
pub(crate) fn update_temperature(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    a: usize,
    b: usize,
) {
    let read = &world.read;
    let write = &mut world.write;
    let ta = read.temperature[a];
    let tb = read.temperature[b];
    let insulation_a = read.insulation[a];
    let insulation_b = read.insulation[b];
    let ea = read.element[a];
    let eb = read.element[b];
    let (conductivity, surface) =
        insulation_conductivity(elements, ea, eb, insulation_a, insulation_b);

    let contact = conductivity * surface;

    let ca = read.mass[a] * record(elements, ea).specific_heat_capacity;
    let cb = read.mass[b] * record(elements, eb).specific_heat_capacity;
    let (new_a, new_b) = if ta <= tb {
        let equilibrium =
            ((cb as f64 * tb as f64) + (ca as f64 * ta as f64)) / (cb as f64 + ca as f64);
        let delta = tb as f64 - ta as f64;
        let step = CELL_TRANSFER_STEP * delta;
        let cap_a = step * ca as f64;
        let cap_b = step * cb as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_a, conductance_cap);
        energy = ordered_min(energy, cap_b);
        let candidate_a = energy / ca as f64 + ta as f64;
        let new_a = if equilibrium <= candidate_a {
            equilibrium
        } else {
            candidate_a
        };
        let candidate_b = tb as f64 - energy / cb as f64;
        let new_b = if candidate_b <= equilibrium {
            candidate_b
        } else {
            equilibrium
        };
        (new_a, new_b)
    } else {
        let equilibrium =
            ((cb as f64 * tb as f64) + (ca as f64 * ta as f64)) / (cb as f64 + ca as f64);
        let delta = ta as f64 - tb as f64;
        let step = CELL_TRANSFER_STEP * delta;
        let cap_a = step * cb as f64;
        let cap_b = step * ca as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_a, conductance_cap);
        energy = ordered_min(energy, cap_b);
        let candidate_a = ta as f64 - energy / ca as f64;
        let new_a = if equilibrium <= candidate_a {
            candidate_a
        } else {
            equilibrium
        };
        let candidate_b = energy / cb as f64 + tb as f64;
        let new_b = if candidate_b <= equilibrium {
            candidate_b
        } else {
            equilibrium
        };
        (new_a, new_b)
    };

    let out_a = new_a as f32 + (write.temperature[a] - ta);
    let out_b = new_b as f32 + (write.temperature[b] - tb);
    write.temperature[a] = out_a.clamp(MIN_TEMPERATURE, MAX_TEMPERATURE);
    write.temperature[b] = out_b.clamp(MIN_TEMPERATURE, MAX_TEMPERATURE);
}

/// Exchange heat between a cell and its backwall; the wall temperature is
/// updated in place in the official ABI's single backwall arrays.
pub(crate) fn update_backwall_temperature(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut crate::events::EventQueues,
    cell: usize,
) {
    let cell_element = world.read.element[cell];
    let wall_element = world.backwall_element[cell];
    if wall_element == world.vacuum_element || cell_element == world.vacuum_element {
        return;
    }

    let cell_record = record(elements, cell_element);
    let wall_record = record(elements, wall_element);
    let insulation = f32::from(world.read.insulation[cell]);
    let insulation2 = insulation * insulation;
    let cell_k = insulation2 * CONDUCTIVITY_SCALE * cell_record.thermal_conductivity;
    let wall_k = wall_record.thermal_conductivity;
    let conductance = if insulation2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
        ordered_min_f32(wall_k, cell_k)
    } else {
        (wall_k.ln() + cell_k.ln()) * 0.5
    };
    let conductance = if insulation2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
        conductance
    } else {
        conductance.exp()
    };
    let surface = phase_multiplier(cell_record, wall_record.state)
        * phase_multiplier(wall_record, cell_record.state);
    let contact = conductance * surface;

    let tc = world.read.temperature[cell];
    let tw = world.backwall_temperature[cell];
    let cc = world.read.mass[cell] * cell_record.specific_heat_capacity;
    let cw = world.backwall_mass[cell] * wall_record.specific_heat_capacity;
    let (next_cell, next_wall) = if tc <= tw {
        let equilibrium =
            ((cw as f64 * tw as f64) + (cc as f64 * tc as f64)) / (cw as f64 + cc as f64);
        let delta = tw as f64 - tc as f64;
        let step = BACKWALL_TRANSFER_STEP * delta;
        let cap_cell = step * cc as f64;
        let cap_wall = step * cw as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_cell, conductance_cap);
        energy = ordered_min(energy, cap_wall);
        let cell_candidate = tc as f64 + energy / cc as f64;
        let wall_candidate = tw as f64 - energy / cw as f64;
        (
            if equilibrium <= cell_candidate {
                equilibrium
            } else {
                cell_candidate
            },
            if equilibrium <= wall_candidate {
                wall_candidate
            } else {
                equilibrium
            },
        )
    } else {
        let equilibrium =
            ((cw as f64 * tw as f64) + (cc as f64 * tc as f64)) / (cw as f64 + cc as f64);
        let delta = tc as f64 - tw as f64;
        let step = BACKWALL_TRANSFER_STEP * delta;
        let cap_cell = step * cw as f64;
        let cap_wall = step * cc as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_cell, conductance_cap);
        energy = ordered_min(energy, cap_wall);
        let cell_candidate = tc as f64 - energy / cc as f64;
        let wall_candidate = tw as f64 + energy / cw as f64;
        (
            if equilibrium <= cell_candidate {
                cell_candidate
            } else {
                equilibrium
            },
            if wall_candidate <= equilibrium {
                wall_candidate
            } else {
                equilibrium
            },
        )
    };

    let write = &mut world.write;
    write.temperature[cell] = next_cell as f32 + (write.temperature[cell] - tc);
    let next_wall = next_wall as f32;
    world.backwall_temperature[cell] = next_wall;
    if next_wall < wall_record.low_temperature - 3.0
        || next_wall > wall_record.high_temperature + 3.0
    {
        if let Some(game_cell) = game_index(world, cell) {
            events.backwall_should_transition(game_cell);
        }
    }
}
