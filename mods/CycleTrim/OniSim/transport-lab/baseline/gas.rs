//! Gas transfers recovered from the official Linux simulation binary.
use crate::events::EventQueues;
use crate::fluid_helpers::{add_disease, clear_disease, cvtt, do_displacement, notify, smax, smin};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

#[derive(Clone, Debug)]
pub(crate) struct GasObliteration {
    pub(crate) element_a: u16,
    pub(crate) element_b: u16,
    pub(crate) product: u16,
    pub(crate) probability: f32,
    pub(crate) min_mass: f32,
    pub(crate) destroy_percent: f32,
    pub(crate) required_multiplier: f32,
    pub(crate) creation_multiplier: f32,
}

// ELF 0x6f120: the returned flow retains its original sign after source swapping.
pub(crate) fn update_pressure(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    a: usize,
    element_a: u16,
    phase_a: u8,
    coefficient: f32,
    b: usize,
) -> f32 {
    let element_b = world.read.element[b];
    let record_b = &tables.elements[element_b as usize].record;
    let ma = world.read.mass[a];
    let mb = world.read.mass[b];
    let coefficient = if phase_a == 1 {
        coefficient
    } else {
        record_b.flow
    };
    let flow = smax(smin(coefficient * (ma - mb), 0.125 * ma), -0.125 * mb);
    let (src, dst, old_mass, amount, source_element, dest_element, dest_phase) = if flow < 0.0 {
        (b, a, mb, -flow, element_b, element_a, phase_a)
    } else {
        (a, b, ma, flow, element_a, element_b, record_b.state & 3)
    };
    let amount = smin(amount, old_mass);
    let fraction = smax(smin(amount / old_mass, 1.0), 0.0);
    let germ_count = cvtt(world.read.disease_count[src] as f32 * fraction);
    let germ_id = world.write.disease_idx[src];
    let source_temperature = world.write.temperature[src];
    let dest_temperature = world.write.temperature[dst];
    let dest_mass = world.write.mass[dst];
    let combined_mass = dest_mass + amount;
    let temperature = if combined_mass <= 0.0 {
        0.0
    } else {
        let weighted = (dest_mass * dest_temperature + source_temperature * amount) / combined_mass;
        if dest_temperature > source_temperature {
            if weighted < source_temperature || weighted.is_nan() || source_temperature.is_nan() {
                source_temperature
            } else {
                smin(dest_temperature, weighted)
            }
        } else if dest_temperature <= weighted {
            smin(source_temperature, weighted)
        } else {
            dest_temperature
        }
    };
    world.write.mass[dst] = combined_mass;
    world.write.temperature[dst] = temperature;
    add_disease(&mut world.write, diseases, dst, germ_id, germ_count);
    world.write.mass[src] = smax(world.write.mass[src] - amount, 0.0);
    let remaining = world.write.disease_count[src].wrapping_sub(germ_count);
    world.write.disease_count[src] = remaining;
    if remaining <= 0 {
        clear_disease(&mut world.write, src);
    }
    if dest_phase == 0 {
        if dest_element == world.sink_element {
            world.write.mass[dst] = 0.0;
            world.write.temperature[dst] = 0.0;
            clear_disease(&mut world.write, dst);
        } else {
            world.write.element[dst] = source_element;
            notify(world, events, dst);
        }
    }
    flow
}

// ELF 0x6ee60: dst is a cell index, rather than a directional offset.
pub(crate) fn displace_gas_directional(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    src: usize,
    dst: usize,
    element: u16,
) -> bool {
    if src >= world.write.mass.len()
        || dst >= world.write.mass.len()
        || world.write.mass[src] <= 0.0
    {
        return false;
    }
    let target = world.write.element[dst];
    if (target != element && target != world.vacuum_element) || world.write.properties[dst] & 1 != 0
    {
        return false;
    }
    do_displacement(world, tables, diseases, events, src, dst);
    true
}

// ELF 0x6eed0: first move B into C, then replace B using A's snapshot state.
pub(crate) fn do_gas_pressure_displacement(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    element_a: u16,
    a: usize,
    b: usize,
    c: usize,
) -> f32 {
    if a >= world.write.mass.len() || b >= world.write.mass.len() || c >= world.write.mass.len() {
        return 0.0;
    }
    let element_b = world.read.element[b];
    let old_mass = world.read.mass[a];
    if tables.elements[element_b as usize].record.state & 3 != 1
        || !(old_mass > world.read.mass[b])
        || world.write.element[a] != element_a
        || world.write.element[b] != element_b
        || !displace_gas_directional(world, tables, diseases, events, b, c, element_b)
    {
        return 0.0;
    }
    let amount = smin(old_mass * 0.125, world.write.mass[a]);
    world.write.mass[b] += amount;
    world.write.temperature[b] = world.read.temperature[a];
    world.write.element[b] = element_a;
    notify(world, events, b);
    world.write.mass[a] = smax(world.write.mass[a] - amount, 0.0);
    let count = cvtt(world.read.disease_count[a] as f32 * 0.125);
    let id = world.read.disease_idx[a];
    add_disease(&mut world.write, diseases, b, id, count);
    let remaining = world.write.disease_count[a].wrapping_sub(count);
    world.write.disease_count[a] = remaining;
    if remaining <= 0 {
        clear_disease(&mut world.write, a);
    }
    amount
}

// ELF 0x534a0: rotate cardinal choices, then try the two +stride diagonals.
pub(crate) fn displace_gas(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    src: usize,
    element: u16,
) -> bool {
    if src >= world.write.mass.len()
        || world.write.mass[src] <= 0.0
        || tables.elements[world.write.element[src] as usize]
            .record
            .state
            & 3
            != 1
    {
        return false;
    }
    let width = world.stride;
    let seed = world.tick_seed as usize;
    let cardinal = [
        src.checked_add(width),
        src.checked_sub(1),
        src.checked_add(1),
        src.checked_sub(width),
    ];
    for n in 0..4 {
        let Some(dst) = cardinal[(seed + n) & 3] else {
            continue;
        };
        if dst >= world.write.mass.len() {
            continue;
        }
        let target = world.write.element[dst];
        if (target == element || target == world.vacuum_element)
            && world.write.properties[dst] & 1 == 0
        {
            do_displacement(world, tables, diseases, events, src, dst);
            return true;
        }
    }
    let diagonal = [
        src.checked_add(width).and_then(|c| c.checked_sub(1)),
        src.checked_add(width).and_then(|c| c.checked_add(1)),
    ];
    let side = [src.checked_sub(1), src.checked_add(1)];
    for n in 0..2 {
        let index = (seed + n) & 1;
        let (Some(dst), Some(side)) = (diagonal[index], side[index]) else {
            continue;
        };
        if dst >= world.write.mass.len() || side >= world.write.mass.len() {
            continue;
        }
        let side_element = world.write.element[side];
        if world.write.element[dst] == element
            && tables.elements[side_element as usize].record.state & 3 != 3
            && world.write.properties[dst] & 1 == 0
        {
            do_displacement(world, tables, diseases, events, src, dst);
            return true;
        }
    }
    false
}

// ELF 0x36170: use the first directed rule and retain the full neighbor requirement.
pub(crate) fn do_gas_interaction_pair(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    rules: &[GasObliteration],
    a: usize,
    b: usize,
) -> bool {
    let element_a = world.write.element[a];
    let element_b = world.write.element[b];
    let Some(rule) = rules
        .iter()
        .find(|rule| rule.element_a == element_a && rule.element_b == element_b)
    else {
        return false;
    };
    let mass_a = world.write.mass[a];
    let mass_b = world.write.mass[b];
    if !(mass_a + mass_b > rule.min_mass) {
        return false;
    }
    world.rng = world.rng.wrapping_mul(214013).wrapping_add(2531011);
    let random = ((world.rng >> 16) & 0x7fff) as f32 * f32::from_bits(0x38000100);
    if random > rule.probability {
        return false;
    }
    let base = rule.destroy_percent * mass_a;
    let required = rule.required_multiplier * base;
    let consumed = smin(mass_b / required, 1.0) * base;
    let temperature_a = world.write.temperature[a];
    let temperature_b = world.write.temperature[b];
    let residual = mass_a - consumed;
    if residual > 0.0 {
        world.write.mass[a] = residual;
        if !displace_gas(world, tables, diseases, events, a, element_a) {
            world.write.mass[a] = mass_a;
            return false;
        }
    }
    world.write.element[a] = rule.product;
    let combined = required + consumed;
    let source_energy = consumed * temperature_a;
    let neighbor_energy = temperature_b * required;
    world.write.temperature[a] = (source_energy + neighbor_energy) / combined;
    world.write.mass[a] = combined * rule.creation_multiplier;
    world.write.mass[b] = smax(mass_b - required, 0.0);
    notify(world, events, a);
    true
}

// ELF 0x6f490: stop at the first successful -stride, -1, +1, +stride pair.
pub(crate) fn do_gas_interaction(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    rules: &[GasObliteration],
    cell: usize,
) -> bool {
    let adjacent = [cell - world.stride, cell - 1, cell + 1, cell + world.stride];
    for neighbor in adjacent {
        if do_gas_interaction_pair(world, tables, diseases, events, rules, cell, neighbor) {
            return true;
        }
    }
    false
}
