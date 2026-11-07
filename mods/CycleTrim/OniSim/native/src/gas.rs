//! Gas transfers recovered from the official Linux simulation binary.
use crate::events::EventQueues;
use crate::fluid_helpers::{
    add_disease, clear_disease, cvtt, do_displacement, notify, phase, smax, smin,
};
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
        || world.write.element[a] != element_a
        || world.write.element[b] != element_b
    {
        return 0.0;
    }

    // 激进真实物理：相邻不同气体直接进行分子扩散与压力渗透混合，形成多组分共存相
    if element_a != element_b {
        let diff = (old_mass - world.read.mass[b]).abs();
        let amount = smin(smax(diff * 0.1, 0.05 * old_mass), world.write.mass[a] * 0.25);
        if amount > 0.001 && (world.mixture_mass[b] <= 0.0 || world.mixture_element[b] == element_a) {
            world.mixture_element[b] = element_a;
            world.mixture_mass[b] += amount;
            world.write.mass[a] = smax(world.write.mass[a] - amount, 0.0);
            if world.mixture_mass[b] > world.write.mass[b] {
                let dom_elem = world.mixture_element[b];
                let dom_mass = world.mixture_mass[b];
                world.mixture_element[b] = world.write.element[b];
                world.mixture_mass[b] = world.write.mass[b];
                world.write.element[b] = dom_elem;
                world.write.mass[b] = dom_mass;
            }
            notify(world, events, a);
            notify(world, events, b);
            return amount;
        }
    }

    if !(old_mass > world.read.mass[b])
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
        let target_phase = phase(tables, target);
        let allows_gas = if target == element || target == world.vacuum_element {
            true
        } else if target_phase == 2 {
            // 当液体未占满 1m³ 格子时，空隙允许气体进入分压
            let target_max = tables.elements[target as usize].record.max_mass.max(1.0);
            let liq_vol = world.write.mass[dst] / target_max;
            liq_vol < 0.95 && (world.mixture_mass[dst] <= 0.0 || world.mixture_element[dst] == element)
        } else {
            false
        };

        if allows_gas && world.write.properties[dst] & 1 == 0 {
            if target_phase == 2 {
                // 液体未占满格：气体作为共存相进入空隙容积
                let gas_mass = world.write.mass[src];
                let _gas_temp = world.write.temperature[src];
                world.mixture_element[dst] = element;
                world.mixture_mass[dst] += gas_mass;
                world.write.mass[src] = 0.0;
                world.write.element[src] = world.vacuum_element;
                notify(world, events, src);
                notify(world, events, dst);
                return true;
            }
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
    // If classic displacement cannot find a same-element or vacuum sink,
    // do not displace; keep the gas in place so mass is strictly conserved.
    false
}

#[allow(dead_code)]
/// Merge `src` gas into `dst` as a second species when `dst` already holds a
/// different gas. Dominant mass stays on the legacy Grid element/mass fields.
fn coexist_gas_into(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    _diseases: &DiseaseTable,
    events: &mut EventQueues,
    src: usize,
    dst: usize,
) -> bool {
    if src >= world.write.mass.len() || dst >= world.write.mass.len() || src == dst {
        return false;
    }
    let src_mass = world.write.mass[src];
    let src_element = world.write.element[src];
    if src_mass <= 0.0 || tables.elements[src_element as usize].record.state & 3 != 1 {
        return false;
    }
    let dst_element = world.write.element[dst];
    let dst_mass = world.write.mass[dst];
    if dst_mass <= 0.0
        || dst_element == src_element
        || dst_element == world.vacuum_element
        || tables.elements[dst_element as usize].record.state & 3 != 1
        || world.write.properties[dst] & 1 != 0
    {
        return false;
    }
    // Only one mixture slot for now. Refuse rather than silently drop mass.
    if world.mixture_mass[dst] > 0.0 && world.mixture_element[dst] != src_element {
        return false;
    }

    let src_temperature = world.write.temperature[src];
    let dst_temperature = world.write.temperature[dst];
    let mix_add = src_mass;
    let existing_mix = if world.mixture_element[dst] == src_element {
        world.mixture_mass[dst]
    } else {
        0.0
    };
    let new_mix = existing_mix + mix_add;

    // Keep the heavier species on the legacy single-element ABI fields.
    if dst_mass >= new_mix {
        world.mixture_element[dst] = src_element;
        world.mixture_mass[dst] = new_mix;
        let total = dst_mass + new_mix;
        world.write.temperature[dst] =
            (dst_temperature * dst_mass + src_temperature * mix_add) / total;
    } else {
        // Promote incoming gas to primary; demote previous primary into mixture.
        world.mixture_element[dst] = dst_element;
        world.mixture_mass[dst] = dst_mass;
        world.write.element[dst] = src_element;
        world.write.mass[dst] = new_mix;
        let total = dst_mass + new_mix;
        world.write.temperature[dst] =
            (dst_temperature * dst_mass + src_temperature * mix_add) / total;
    }

    world.write.mass[src] = 0.0;
    world.write.element[src] = world.vacuum_element;
    world.mixture_mass[src] = 0.0;
    world.mixture_element[src] = world.vacuum_element;
    crate::multicomponent_flux::clear_species_cell(world, src);
    crate::multicomponent_flux::ingest_write_cell(world, tables, dst);
    clear_disease(&mut world.write, src);
    notify(world, events, src);
    notify(world, events, dst);
    true
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
