//! Gas transfers recovered from the official Linux simulation binary.
use crate::events::EventQueues;
use crate::fluid_helpers::{
    add_disease, clear_disease, cvtt, do_displacement, notify, phase, smax, smin,
};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

/// Exchange composition across a fluid edge without changing either cell's
/// total mass. Supports arbitrary species mixtures across adjacent cells.
fn diffuse_pair(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues, a: usize, b: usize) {
    let ea = world.write.element[a];
    let eb = world.write.element[b];
    let pa = phase(tables, ea);
    let pb = phase(tables, eb);
    if pa != 1 || pb != 1 || world.write.properties[a] & 1 != 0 || world.write.properties[b] & 1 != 0 { return; }

    let mut ids = [0u16; 16];
    let mut count = 0;
    world.for_each_cell_species(a, |id, mass| {
        if mass > 0.0 && phase(tables, id) == 1 && !ids[..count].contains(&id) && count < 16 {
            ids[count] = id;
            count += 1;
        }
    });
    world.for_each_cell_species(b, |id, mass| {
        if mass > 0.0 && phase(tables, id) == 1 && !ids[..count].contains(&id) && count < 16 {
            ids[count] = id;
            count += 1;
        }
    });
    if count <= 1 { return; }

    let species_mass_at = |world: &PhysicsWorld, cell: usize, target: u16| -> f32 {
        let mut m = 0.0f32;
        world.for_each_cell_species(cell, |id, mass| {
            if id == target { m += mass; }
        });
        m
    };

    let mut total_a = 0.0f32;
    world.for_each_cell_species(a, |_, m| total_a += m);
    let mut total_b = 0.0f32;
    world.for_each_cell_species(b, |_, m| total_b += m);
    if total_a <= 0.0 || total_b <= 0.0 { return; }

    let is_vertical = b == a + world.stride;
    let mut any_transfer = false;
    let min_total = total_a.min(total_b);
    for &id in &ids[..count] {
        let a_mass = species_mass_at(world, a, id);
        let b_mass = species_mass_at(world, b, id);
        let conc_diff = a_mass / total_a - b_mass / total_b;

        // 真实自然物理：气体趋向于先向四周充分扩散，垂直方向附带温和的分子量沉降偏好
        let transfer = if is_vertical {
            // a 为下方格子，b 为上方格子。
            let molar = tables.elements.get(id as usize)
                .map(|e| e.record.molar_mass.max(1.0))
                .unwrap_or(32.0);
            // 相对氧气(32)基准：重气体(CO2 44, 氯气 71)温和偏好向下沉至 a，轻气体(氢气 2, 甲烷 16)温和偏好向上浮至 b
            let drift_bias = ((molar - 32.0) / 44.0) * 0.006;
            (conc_diff + drift_bias) * min_total * 0.05
        } else {
            // 水平方向：高通量向四周充分扩散平推
            conc_diff * min_total * 0.08
        };
        if transfer.abs() >= 1e-6 {
            if transfer > 0.0 {
                let real_transfer = transfer.min(a_mass);
                world.add_species_mass(tables, b, id, real_transfer);
                if world.write.element[a] == id {
                    world.write.mass[a] = (world.write.mass[a] - real_transfer).max(0.0);
                } else if world.mixture_element[a] == id {
                    world.mixture_mass[a] = (world.mixture_mass[a] - real_transfer).max(0.0);
                } else if let Some(extras) = world.extra_mixtures.get_mut(&a) {
                    if let Some(pos) = extras.iter().position(|(e, _)| *e == id) {
                        extras[pos].1 = (extras[pos].1 - real_transfer).max(0.0);
                    }
                }
            } else {
                let real_transfer = (-transfer).min(b_mass);
                world.add_species_mass(tables, a, id, real_transfer);
                if world.write.element[b] == id {
                    world.write.mass[b] = (world.write.mass[b] - real_transfer).max(0.0);
                } else if world.mixture_element[b] == id {
                    world.mixture_mass[b] = (world.mixture_mass[b] - real_transfer).max(0.0);
                } else if let Some(extras) = world.extra_mixtures.get_mut(&b) {
                    if let Some(pos) = extras.iter().position(|(e, _)| *e == id) {
                        extras[pos].1 = (extras[pos].1 - real_transfer).max(0.0);
                    }
                }
            }
            any_transfer = true;
        }
    }
    if any_transfer {
        world.normalize_cell_mixtures(a);
        world.normalize_cell_mixtures(b);
        notify(world, events, a);
        notify(world, events, b);
    }
}

pub(crate) fn diffuse_region(world: &mut PhysicsWorld, tables: &ElementTable, events: &mut EventQueues, region: crate::world::Region, gpu_solid: Option<&[u32]>, liquid_solid: Option<&[u32]>) {
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width + 1);
    let max_y = (region.max_y as usize).min(world.height + 1);
    let gpu_max_x = (region.max_x as usize).min(world.width);
    let gpu_max_y = (region.max_y as usize).min(world.height);
    let gpu_width = gpu_max_x.saturating_sub(min_x);
    let gpu_height = gpu_max_y.saturating_sub(min_y);
    // A fully open gas mask excludes positive-mass liquid cells; a fully open
    // liquid mask consists entirely of representable liquid cells. Either
    // case leaves only the boundary strip for the ordered CPU fallback.
    let full_mask = |mask: Option<&[u32]>| mask.is_some_and(|m|
        gpu_width >= 2 && gpu_height >= 2 && m.len() == gpu_width * gpu_height
            && m.iter().all(|&value| value == 0));
    if full_mask(gpu_solid) || full_mask(liquid_solid) {
        for y in min_y..max_y {
            let start_x = if y + 1 < gpu_max_y { gpu_max_x.saturating_sub(1).max(min_x) } else { min_x };
            for x in start_x..max_x {
                let cell = y * world.stride + x;
                if x + 1 < max_x && !(x + 1 < gpu_max_x && y < gpu_max_y) {
                    diffuse_pair(world, tables, events, cell, cell + 1);
                }
                if y + 1 < max_y && !(x < gpu_max_x && y + 1 < gpu_max_y) {
                    diffuse_pair(world, tables, events, cell, cell + world.stride);
                }
            }
        }
        return;
    }
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y * world.stride + x;
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

    // 激进真实物理：相邻不同气体直接进行分子扩散与压力渗透混合，形成多组分共存相（支持任意数量混合）
    if element_a != element_b {
        let diff = (old_mass - world.read.mass[b]).abs();
        let amount = smin(smax(diff * 0.1, 0.05 * old_mass), world.write.mass[a] * 0.25);
        if amount > 0.001 {
            let heat = |id: u16| tables.elements[id as usize].record.specific_heat_capacity.max(0.0);
            let prev_temp = world.write.temperature[b];
            let added_capacity = amount * heat(element_a);
            let mut previous_capacity = 0.0f32;
            world.for_each_cell_species(b, |id, m| {
                previous_capacity += m * heat(id);
            });
            if previous_capacity + added_capacity > 0.0 {
                world.write.temperature[b] = (prev_temp * previous_capacity
                    + world.write.temperature[a] * added_capacity) / (previous_capacity + added_capacity);
            }
            world.add_species_mass(tables, b, element_a, amount);
            world.write.mass[a] = smax(world.write.mass[a] - amount, 0.0);
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
        let allows_gas = target == element || target == world.vacuum_element;

        if allows_gas && world.write.properties[dst] & 1 == 0 {
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
    let src_temperature = world.write.temperature[src];
    let dst_temperature = world.write.temperature[dst];
    let total = dst_mass + src_mass;
    if total > 0.0 {
        world.write.temperature[dst] =
            (dst_temperature * dst_mass + src_temperature * src_mass) / total;
    }
    world.add_species_mass(tables, dst, src_element, src_mass);

    world.write.mass[src] = 0.0;
    world.write.element[src] = world.vacuum_element;
    world.clear_cell_mixtures(src);
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
