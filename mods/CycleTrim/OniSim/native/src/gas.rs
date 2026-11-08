//! Gas transfers recovered from the official Linux simulation binary.
use crate::events::EventQueues;
use crate::fluid_helpers::{
    add_disease, clear_disease, cvtt, do_displacement, notify, phase, smax, smin,
};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

/// Exchange composition across a fluid edge without changing either cell's
/// total mass. An edge requiring three species is left untouched until the
/// storage can represent it, rather than dropping a component.
fn diffuse_pair(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues, a: usize, b: usize) {
    let ea = world.write.element[a];
    let eb = world.write.element[b];
    let pa = phase(tables, ea);
    if (pa != 1 && pa != 2) || phase(tables, eb) != pa || world.write.properties[a] & 1 != 0 || world.write.properties[b] & 1 != 0 { return; }
    let mut ids = [ea, ea];
    let mut count = 1;
    for (id, mass) in [(eb, world.write.mass[b]), (world.mixture_element[a], world.mixture_mass[a]), (world.mixture_element[b], world.mixture_mass[b])] {
        if mass <= 0.0 || ids[..count].contains(&id) { continue; }
        if count == 2 { return; }
        ids[count] = id;
        count += 1;
    }
    if count != 2 || phase(tables, ids[1]) != pa { return; }
    if pa == 2 && !crate::fluid_helpers::are_liquids_miscible(&tables.elements[ids[0] as usize].record, &tables.elements[ids[1] as usize].record) { return; }
    let total_a = world.write.mass[a] + world.mixture_mass[a];
    let total_b = world.write.mass[b] + world.mixture_mass[b];
    if total_a <= 0.0 || total_b <= 0.0 { return; }
    let amount = |cell: usize, id: u16| -> f32 {
        (if world.write.element[cell] == id { world.write.mass[cell] } else { 0.0 }) +
        (if world.mixture_element[cell] == id { world.mixture_mass[cell] } else { 0.0 })
    };
    let a0 = amount(a, ids[0]);
    let b0 = amount(b, ids[0]);
    let transfer = (a0 / total_a - b0 / total_b) * total_a.min(total_b) * 0.05;
    if transfer.abs() < 0.000001 { return; }
    for (cell, total, first) in [(a, total_a, a0 - transfer), (b, total_b, b0 + transfer)] {
        let first = first.clamp(0.0, total);
        let second = total - first;
        if first >= second {
            world.write.element[cell] = ids[0]; world.write.mass[cell] = first;
            world.mixture_element[cell] = ids[1]; world.mixture_mass[cell] = second;
        } else {
            world.write.element[cell] = ids[1]; world.write.mass[cell] = second;
            world.mixture_element[cell] = ids[0]; world.mixture_mass[cell] = first;
        }
        notify(world, events, cell);
    }
}

pub(crate) fn diffuse_region(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues, region: crate::world::Region, gpu_solid: Option<&[u32]>, liquid_solid: Option<&[u32]>) {
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width + 1);
    let max_y = (region.max_y as usize).min(world.height + 1);
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y * world.stride + x;
            let gpu_max_x = (region.max_x as usize).min(world.width);
            let gpu_max_y = (region.max_y as usize).min(world.height);
            let gpu_width = gpu_max_x.saturating_sub(min_x);
            let gpu_height = gpu_max_y.saturating_sub(min_y);
            let eligible = |ax: usize, ay: usize, bx: usize, by: usize| {
                let phase = phase(tables, world.write.element[ay * world.stride + ax]);
                let mask = if phase == 1 { gpu_solid } else if phase == 2 { liquid_solid } else { None };
                mask.is_some_and(|solid| {
                    if gpu_width < 2 || gpu_height < 2 || solid.len() != gpu_width * gpu_height { return false; }
                    let a = (ay - min_y) * gpu_width + ax - min_x;
                    let b = (by - min_y) * gpu_width + bx - min_x;
                    solid[a] == 0 && solid[b] == 0
                })
            };
            let right_on_gpu = x + 1 < gpu_max_x && y < gpu_max_y && eligible(x, y, x + 1, y);
            let down_on_gpu = x < gpu_max_x && y + 1 < gpu_max_y && eligible(x, y, x, y + 1);
            if x + 1 < max_x && !right_on_gpu { diffuse_pair(world, tables, events, cell, cell + 1); }
            if y + 1 < max_y && !down_on_gpu { diffuse_pair(world, tables, events, cell, cell + world.stride); }
        }
    }
}

/// Move a gas secondary phase upward by one cell per simulation step.
/// If the destination cannot represent it, leave the bubble in place.
pub(crate) fn rise_bubbles(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues) {
    // Empty spans are common even on large maps. Skip them without visiting
    // the remaining cell fields, while preserving top-to-bottom and left-to-
    // right ordering for every span that can contain a bubble. NaNs must not
    // be classified as empty: the original `mass <= 0` test doesn't do so.
    let mut droplets = Vec::new();
    for y in (1..=world.height).rev() {
        for start in (1..=world.width).step_by(64) {
            let end = (start + 64).min(world.width + 1);
            let row = y * world.stride;
            if world.mixture_mass[row + start..row + end].iter().all(|&mass| mass <= 0.0) {
                continue;
            }
            for x in start..end {
                let src = y * world.stride + x;
                let dst = src + world.stride;
                let mass = world.mixture_mass[src];
                let element = world.mixture_element[src];
                if y > 1 && mass.is_finite() && mass > 0.0
                    && phase(tables, world.write.element[src]) == 1 && phase(tables, element) == 2 {
                    droplets.push(src);
                    continue;
                }
                if y == world.height { continue; }
                if mass <= 0.0 || phase(tables, world.write.element[src]) != 2
                    || phase(tables, element) != 1 || world.write.properties[dst] & 1 != 0 {
                    continue;
                }
                let target = world.write.element[dst];
                let target_phase = phase(tables, target);
                if target == world.vacuum_element {
                    world.write.element[dst] = element;
                    world.write.mass[dst] = mass;
                    world.write.temperature[dst] = world.write.temperature[src];
                } else if target == element && target_phase == 1 {
                    let old = world.write.mass[dst];
                    world.write.temperature[dst] = (world.write.temperature[dst] * old + world.write.temperature[src] * mass) / (old + mass);
                    world.write.mass[dst] += mass;
                } else if (target_phase == 1 || target_phase == 2)
                    && (world.mixture_mass[dst] <= 0.0 || world.mixture_element[dst] == element) {
                    world.mixture_element[dst] = element;
                    world.mixture_mass[dst] += mass;
                } else {
                    continue;
                }
                world.mixture_element[src] = world.vacuum_element;
                world.mixture_mass[src] = 0.0;
                notify(world, events, src);
                notify(world, events, dst);
            }
        }
    }
    settle_liquid_droplets(world, tables, events, &droplets);
}

/// Settle liquid droplets suspended as a secondary component in gas.
/// Process bottom to top so a droplet moves at most one cell per step.
fn settle_liquid_droplets(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues, droplets: &[usize]) {
    for &src in droplets.iter().rev() {
        let dst = src - world.stride;
        let mass = world.mixture_mass[src];
        let element = world.mixture_element[src];
        if !mass.is_finite() || mass <= 0.0 || phase(tables, world.write.element[src]) != 1
            || phase(tables, element) != 2 || world.write.properties[dst] & 1 != 0 { continue; }
        let target = world.write.element[dst];
        let target_phase = phase(tables, target);
        if target == world.vacuum_element && world.mixture_mass[dst] <= 0.0 {
            world.write.element[dst] = element;
            world.write.mass[dst] = mass;
            world.write.temperature[dst] = world.write.temperature[src];
        } else if target == element || (target_phase == 1
            && (world.mixture_mass[dst] <= 0.0 || world.mixture_element[dst] == element)) {
            let cp = |id: u16| tables.elements[id as usize].record.specific_heat_capacity.max(0.0);
            let old_capacity = world.write.mass[dst] * cp(target)
                + world.mixture_mass[dst].max(0.0) * cp(world.mixture_element[dst]);
            let incoming_capacity = mass * cp(element);
            let total = old_capacity + incoming_capacity;
            if total > 0.0 {
                world.write.temperature[dst] = (world.write.temperature[dst] * old_capacity
                    + world.write.temperature[src] * incoming_capacity) / total;
            }
            if target == element { world.write.mass[dst] += mass; }
            else { world.mixture_element[dst] = element; world.mixture_mass[dst] += mass; }
        } else { continue; }
        world.mixture_element[src] = world.vacuum_element;
        world.mixture_mass[src] = 0.0;
        notify(world, events, src);
        notify(world, events, dst);
    }
}

#[cfg(test)]
mod bubble_tests {
    use super::*;

    fn fixture() -> (PhysicsWorld, ElementTable) {
        let mut bytes = 6_i32.to_le_bytes().to_vec();
        for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c, 123, 124, 125].into_iter().enumerate() {
            let mut record = [0_u8; 164];
            record[..4].copy_from_slice(&hash.to_le_bytes());
            record[4..8].fill(255);
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = [0, 0, 3, 2, 1, 1][index];
            record[12..16].copy_from_slice(&1_f32.to_le_bytes());
            record[84..88].fill(255);
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0; 24]);
        let table = ElementTable::parse(&bytes).unwrap();
        let mut world = PhysicsWorld::new(2, 3, 0, &table).unwrap();
        world.write.element.fill(world.vacuum_element);
        (world, table)
    }

    #[test]
    fn bubble_rises_through_water_and_emerges_without_losing_mass() {
        let (mut world, table) = fixture();
        let cell = world.stride + 1;
        let above = cell + world.stride;
        let surface = above + world.stride;
        for c in [cell, above] {
            world.write.element[c] = 3;
            world.write.mass[c] = 1000.0;
        }
        let mut events = EventQueues::default();
        let diseases = DiseaseTable { diseases: Vec::new(), element_count: table.len() };
        crate::cell_material_add_gas::add_gas(&mut world, &table, &diseases, &mut events,
            cell, 4, 2.5, 300.0, 255, 0);
        assert_eq!(world.mixture_mass[cell], 2.5);
        rise_bubbles(&mut world, &table, &mut events);
        assert_eq!(world.mixture_mass[cell], 0.0);
        assert_eq!(world.mixture_mass[above], 2.5);
        rise_bubbles(&mut world, &table, &mut events);
        assert_eq!(world.mixture_mass[above], 0.0);
        assert_eq!(world.write.element[surface], 4);
        assert_eq!(world.write.mass[surface], 2.5);
        assert_eq!(world.write.mass[cell] + world.write.mass[above], 2000.0);
    }

    #[test]
    fn bubble_stays_under_a_solid_ceiling() {
        let (mut world, table) = fixture();
        let cell = world.stride + 1;
        world.write.element[cell] = 3;
        world.write.mass[cell] = 1000.0;
        world.write.element[cell + world.stride] = 2;
        world.write.mass[cell + world.stride] = 1000.0;
        world.mixture_element[cell] = 4;
        world.mixture_mass[cell] = 2.5;
        rise_bubbles(&mut world, &table, &mut EventQueues::default());
        assert_eq!(world.mixture_mass[cell], 2.5);
    }

    #[test]
    fn liquid_droplet_falls_through_gas_into_matching_liquid() {
        let (mut world, table) = fixture();
        let bottom = world.stride + 1;
        let middle = bottom + world.stride;
        let top = middle + world.stride;
        world.write.element[bottom] = 3;
        world.write.mass[bottom] = 10.0;
        world.write.temperature[bottom] = 200.0;
        for cell in [middle, top] {
            world.write.element[cell] = 4;
            world.write.mass[cell] = 2.0;
            world.write.temperature[cell] = 400.0;
        }
        world.mixture_element[top] = 3;
        world.mixture_mass[top] = 1.0;
        let mut events = EventQueues::default();
        rise_bubbles(&mut world, &table, &mut events);
        assert_eq!(world.mixture_mass[top], 0.0);
        assert_eq!(world.mixture_mass[middle], 1.0);
        assert_eq!(world.write.mass[bottom], 10.0);
        rise_bubbles(&mut world, &table, &mut events);
        assert_eq!(world.mixture_mass[middle], 0.0);
        assert_eq!(world.write.mass[bottom], 11.0);
        assert!((world.write.temperature[bottom] - (2400.0 / 11.0)).abs() < 0.001);
        assert_eq!(world.write.mass[middle] + world.write.mass[top], 4.0);
    }

    #[test]
    fn droplet_stays_when_destination_is_full_or_solid() {
        let (mut world, table) = fixture();
        let bottom = world.stride + 1;
        let top = bottom + world.stride;
        world.write.element[top] = 4;
        world.mixture_element[top] = 3;
        world.mixture_mass[top] = 1.0;
        world.write.element[bottom] = 4;
        world.write.mass[bottom] = 1.0;
        world.mixture_element[bottom] = 5;
        world.mixture_mass[bottom] = 1.0;
        rise_bubbles(&mut world, &table, &mut EventQueues::default());
        assert_eq!(world.mixture_mass[top], 1.0);
        world.write.element[bottom] = 2;
        rise_bubbles(&mut world, &table, &mut EventQueues::default());
        assert_eq!(world.mixture_mass[top], 1.0);
    }

    #[test]
    fn sparse_bubbles_cross_span_boundaries_without_skipping_last_column() {
        let (_, table) = fixture();
        let mut world = PhysicsWorld::new(130, 3, 0, &table).unwrap();
        world.write.element.fill(world.vacuum_element);
        for x in [1, 64, 65, 130] {
            let cell = world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1000.0;
            world.mixture_element[cell] = 4;
            world.mixture_mass[cell] = x as f32;
        }
        rise_bubbles(&mut world, &table, &mut EventQueues::default());
        for x in [1, 64, 65, 130] {
            let source = world.stride + x;
            let destination = source + world.stride;
            assert_eq!(world.mixture_mass[source], 0.0);
            assert_eq!(world.write.element[destination], 4);
            assert_eq!(world.write.mass[destination], x as f32);
        }
    }

    #[test]
    fn gpu_owned_gas_edges_are_not_diffused_twice_on_cpu() {
        let (_, table) = fixture();
        let mut world = PhysicsWorld::new(4, 4, 0, &table).unwrap();
        world.write.element.fill(world.vacuum_element);
        let a=world.stride+1; let b=a+1;
        world.write.element[a]=4; world.write.mass[a]=9.0;
        world.mixture_element[a]=5; world.mixture_mass[a]=1.0;
        world.write.element[b]=4; world.write.mass[b]=6.0;
        world.mixture_element[b]=5; world.mixture_mass[b]=4.0;
        let region=crate::world::Region {min_x:1,min_y:1,max_x:4,max_y:4,..Default::default()};
        diffuse_region(&mut world,&table,&mut EventQueues::default(),region,Some(&[0; 9]),None);
        assert_eq!((world.mixture_mass[a],world.mixture_mass[b]),(1.0,4.0));
        diffuse_region(&mut world,&table,&mut EventQueues::default(),region,Some(&[1,0,0,0,0,0,0,0,0]),None);
        assert!(world.mixture_mass[a]>1.0 && world.mixture_mass[b]<4.0);
    }

    #[test]
    fn mixed_gas_diffuses_even_when_both_cells_have_the_same_primary() {
        let (mut world, table) = fixture();
        let a = world.stride + 1;
        let b = a + 1;
        world.write.element[a] = 4; world.write.mass[a] = 9.0;
        world.mixture_element[a] = 5; world.mixture_mass[a] = 1.0;
        world.write.element[b] = 4; world.write.mass[b] = 6.0;
        world.mixture_element[b] = 5; world.mixture_mass[b] = 4.0;
        diffuse_pair(&mut world, &table, &mut EventQueues::default(), a, b);
        assert!(world.mixture_mass[a] > 1.0);
        assert!(world.mixture_mass[b] < 4.0);
        assert!((world.mixture_mass[a] + world.mixture_mass[b] - 5.0).abs() < 0.0001);
        assert!((world.write.mass[a] + world.write.mass[b] - 15.0).abs() < 0.0001);
    }

    #[test]
    fn exchange_keeps_mixture_with_its_cell_material() {
        let (mut world, _) = fixture();
        let a = world.stride + 1;
        let b = a + 1;
        world.write.element[a] = 4; world.write.mass[a] = 9.0;
        world.mixture_element[a] = 5; world.mixture_mass[a] = 1.0;
        world.write.element[b] = 3; world.write.mass[b] = 1000.0;
        crate::fluid_helpers::swap_material(&mut world, &mut EventQueues::default(), a, b);
        assert_eq!((world.write.element[b], world.write.mass[b]), (4, 9.0));
        assert_eq!((world.mixture_element[b], world.mixture_mass[b]), (5, 1.0));
    }
}

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
