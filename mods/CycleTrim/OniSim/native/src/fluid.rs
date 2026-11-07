use crate::events::EventQueues;
use crate::fluid_helpers::{
    add_disease, add_mass, are_liquids_miscible, clear_cell, clear_disease, cvtt, game_index,
    liquid_density, notify, phase, smax, smin, swap_material,
};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{Cells, PhysicsWorld};

pub(crate) fn is_liquid_permeable(cells: &Cells, tables: &ElementTable, cell: usize) -> bool {
    cells.properties[cell] & 2 == 0
        && tables.elements[cells.element[cell] as usize].record.state & 2 == 0
}

fn transfer_liquid(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    source: usize,
    element: u16,
    destination: usize,
    amount: f32,
    disease: u8,
    count: i32,
) -> bool {
    let temperature = world.read.temperature[source];
    let previous_element = world.write.element[destination];
    if previous_element == element {
        add_mass(
            &mut world.write,
            diseases,
            destination,
            amount,
            temperature,
            disease,
            count,
        );
        return true;
    }
    if previous_element == world.sink_element {
        world.write.mass[destination] = 0.0;
        return true;
    }
    match phase(tables, previous_element) {
        2 => {
            // 互溶流体多组分混合（如洁净水与污染水，盐水与水）
            let record = &tables.elements[element as usize].record;
            let prev_record = &tables.elements[previous_element as usize].record;
            if are_liquids_miscible(record, prev_record) {
                if world.mixture_mass[destination] <= 0.0 || world.mixture_element[destination] == element {
                    world.mixture_element[destination] = element;
                    world.mixture_mass[destination] += amount;
                    // 主组分始终保持质量最大项
                    if world.mixture_mass[destination] > world.write.mass[destination] {
                        let dom_elem = world.mixture_element[destination];
                        let dom_mass = world.mixture_mass[destination];
                        world.mixture_element[destination] = world.write.element[destination];
                        world.mixture_mass[destination] = world.write.mass[destination];
                        world.write.element[destination] = dom_elem;
                        world.write.mass[destination] = dom_mass;
                    }
                    notify(world, events, destination);
                    return true;
                }
            }
            return false;
        }
        1 => {
            // 1.0 m³ 空间模型：当液体未充满格（<95%容积）时，气体保留在空隙容积，不被挤掉
            let max_m = tables.elements[element as usize].record.max_mass.max(1.0);
            let liq_vol = amount / max_m;
            if liq_vol < 0.95 {
                let gas_elem = previous_element;
                let gas_mass = world.write.mass[destination];
                world.write.element[destination] = element;
                world.write.mass[destination] = amount;
                world.write.temperature[destination] = temperature;
                world.mixture_element[destination] = gas_elem;
                world.mixture_mass[destination] = gas_mass;
                notify(world, events, destination);
                return true;
            }
            if !displace_gas(
                world,
                tables,
                diseases,
                events,
                destination,
                previous_element,
            ) {
                return false;
            }
        }
        _ => {}
    }
    world.write.element[destination] = element;
    add_mass(
        &mut world.write,
        diseases,
        destination,
        amount,
        temperature,
        disease,
        count,
    );
    notify(world, events, destination);
    true
}

pub(crate) fn displace_liquid_directional(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
    _element: u16,
) -> bool {
    if source >= world.write.mass.len()
        || destination >= world.write.mass.len()
        || world.write.mass[source] <= 0.0
        || world.write.properties[destination] & 2 != 0
    {
        return false;
    }
    let element = world.write.element[destination];
    if tables.elements[element as usize].record.state & 2 != 0 {
        if phase(tables, element) != 2 || element != world.write.element[source] {
            return false;
        }
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
        clear_cell(&mut world.write, world.vacuum_element, source);
        notify(world, events, source);
        return true;
    }
    if !displace_gas(world, tables, diseases, events, destination, element) {
        return false;
    }
    swap_material(world, events, source, destination);
    true
}

pub(crate) fn do_liquid_pressure_displacement(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    element: u16,
    a: usize,
    b: usize,
    c: usize,
) -> f32 {
    if a >= world.write.mass.len() || b >= world.write.mass.len() || c >= world.write.mass.len() {
        return 0.0;
    }
    let element_b = world.read.element[b];
    if world.write.properties[b] & 2 != 0
        || element_b == element
        || world.write.element[a] != element
        || world.write.element[b] != element_b
        || phase(tables, element_b) != 2
    {
        return 0.0;
    }

    // 激进真实物理：互溶液体（洁净水、污染水、盐水）接触界面自然对流混溶形成多组分
    let record_a = &tables.elements[element as usize].record;
    let record_b = &tables.elements[element_b as usize].record;
    if are_liquids_miscible(record_a, record_b) {
        let diff = (world.read.mass[a] - world.read.mass[b]).abs();
        let amount = smin(smax(diff * 0.1, 10.0), world.write.mass[a] * 0.25);
        if amount > 0.0 && (world.mixture_mass[b] <= 0.0 || world.mixture_element[b] == element) {
            world.mixture_element[b] = element;
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
    let available = ((world.read.mass[a] - world.flow[a][0])
        - (world.flow[a][1] + world.flow[a][2]))
        - world.flow[a][3];
    if !(available > world.read.mass[c] + world.read.mass[b]) {
        return 0.0;
    }
    if !displace_liquid_directional(world, tables, diseases, events, b, c, element_b) {
        return 0.0;
    }
    let amount = available * f32::from_bits(0x3e000000);
    world.write.mass[b] += amount;
    world.write.temperature[b] = world.read.temperature[a];
    world.write.element[b] = element;
    notify(world, events, b);
    world.write.mass[a] = smax(world.write.mass[a] - amount, 0.0);
    let count = cvtt(world.read.disease_count[a] as f32 * f32::from_bits(0x3e000000));
    add_disease(
        &mut world.write,
        diseases,
        b,
        world.read.disease_idx[a],
        count,
    );
    world.write.disease_count[a] = world.write.disease_count[a].wrapping_sub(count);
    if world.write.disease_count[a] <= 0 {
        clear_disease(&mut world.write, a);
    }
    amount
}

fn remove_transfer(
    world: &mut PhysicsWorld,
    source: usize,
    amount: f32,
    count: i32,
    direction: usize,
) {
    if world.write.disease_idx[source] == world.read.disease_idx[source] {
        world.write.disease_count[source] = world.write.disease_count[source].wrapping_sub(count);
        if world.write.disease_count[source] <= 0 {
            clear_disease(&mut world.write, source);
        }
    }
    world.write.mass[source] -= amount;
    world.flow[source][direction] += amount;
}

fn spawn_falling(
    world: &PhysicsWorld,
    tables: &ElementTable,
    events: &mut EventQueues,
    source: usize,
    destination: usize,
    mass: f32,
    count: i32,
) -> bool {
    if world.falling_disabled || world.read.properties[destination - world.stride] & 2 != 0 {
        return false;
    }
    let Some(game_cell) = game_index(world, destination) else {
        return false;
    };
    if world.active_mask[game_cell as usize] == 0 && !world.simulate_all_active {
        return false;
    }
    let element = world.read.element[source];
    let temperature = world.read.temperature[source];
    let record = &tables.elements[element as usize].record;
    if !(temperature >= record.low_temperature - 3.0)
        || !(record.high_temperature + 3.0 >= temperature)
    {
        return false;
    }
    events.spawn_falling_liquid(
        game_cell,
        element,
        world.read.disease_idx[source],
        mass,
        temperature,
        count,
    );
    true
}

pub(crate) fn update_liquid(
    world: &mut PhysicsWorld,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) {
    let element = world.read.element[cell];
    let record = &tables.elements[element as usize].record;
    let mut remaining = world.read.mass[cell];
    let mut germs = world.read.disease_count[cell];
    let disease = world.read.disease_idx[cell];
    let down = cell - world.stride;
    let down_element = world.read.element[down];
    if phase(tables, world.write.element[down]) != 3 && world.read.properties[down] & 2 == 0 {
        if down_element == element {
            let below_mass = if phase(tables, down_element) == 2 {
                world.read.mass[down]
            } else {
                0.0
            };
            let threshold = smax(f32::from_bits(0x3f8147ae) * remaining, record.max_mass);
            let amount = smin(
                smax(threshold - below_mass, 0.0) * 0.5,
                smin(record.viscosity, remaining),
            );
            if (!(remaining > record.min_vertical_flow) || record.min_vertical_flow <= amount)
                && amount > 0.0
            {
                let count = cvtt(germs as f32 * (amount / remaining));
                if transfer_liquid(
                    world, tables, diseases, events, cell, element, down, amount, disease, count,
                ) {
                    germs = germs.wrapping_sub(count);
                    remove_transfer(world, cell, amount, count, 2);
                    remaining -= amount;
                }
            }
        } else if phase(tables, down_element) == 2 {
            // 下方是另一种液体：比较实际密度，较重液体下沉置换较轻液体！
            let down_record = &tables.elements[down_element as usize].record;
            if liquid_density(record) > liquid_density(down_record) {
                swap_material(world, events, cell, down);
                return;
            }
        } else if tables.elements[down_element as usize].record.state & 2 == 0 {
            if down_element == world.sink_element {
                clear_cell(&mut world.write, world.vacuum_element, cell);
                notify(world, events, cell);
                return;
            }
            let left_shelf = is_liquid_permeable(&world.write, tables, cell - 1)
                && !is_liquid_permeable(&world.write, tables, down - 1);
            let right_shelf = is_liquid_permeable(&world.write, tables, cell + 1)
                && !is_liquid_permeable(&world.write, tables, down + 1);
            if !left_shelf && !right_shelf {
                swap_material(world, events, cell, down);
            } else if spawn_falling(world, tables, events, cell, cell, remaining, germs) {
                clear_cell(&mut world.write, world.vacuum_element, cell);
                notify(world, events, cell);
            }
            return;
        }
    }
    if remaining <= 0.0 {
        return;
    }
    for (side, direction) in [(cell - 1, 0), (cell + 1, 1)] {
        let side_element = world.read.element[side];
        let side_record = &tables.elements[side_element as usize].record;
        let is_miscible = are_liquids_miscible(record, side_record);
        if (side_element != element && !is_miscible && side_record.state & 2 != 0)
            || phase(tables, world.write.element[side]) == 3
            || world.read.properties[side] & 2 != 0
        {
            continue;
        }
        let difference = if phase(tables, side_element) == 2 {
            remaining - world.read.mass[side]
        } else {
            remaining
        };
        let amount = smin(difference * 0.25, smin(record.viscosity, remaining));
        if !(record.min_horizontal_flow <= amount && 0.0 < amount) {
            continue;
        }
        let count = cvtt(germs as f32 * (amount / remaining));
        let can_fall = (phase(tables, world.read.element[down]) == 3
            || world.read.properties[down] & 2 != 0)
            && is_liquid_permeable(&world.write, tables, side - world.stride);
        let spawned = can_fall && spawn_falling(world, tables, events, cell, side, amount, count);
        if !spawned
            && !transfer_liquid(
                world, tables, diseases, events, cell, element, side, amount, disease, count,
            )
        {
            continue;
        }
        remove_transfer(world, cell, amount, count, direction);
        remaining -= amount;
        if remaining <= 0.0 {
            return;
        }
        germs = germs.wrapping_sub(count);
    }
    let up = cell + world.stride;
    let up_element = world.read.element[up];
    if (up_element == element || tables.elements[up_element as usize].record.state & 2 == 0)
        && phase(tables, world.write.element[up]) != 3
        && world.read.properties[up] & 2 == 0
    {
        let upper = if phase(tables, up_element) == 2 {
            f32::from_bits(0x3f8147ae) * world.read.mass[up]
        } else {
            0.0
        };
        let threshold = smax(upper, record.max_mass);
        let mut amount = smax(remaining - threshold, 0.0) * 0.5;
        if threshold + threshold > remaining {
            amount = smin(amount, smin(record.viscosity, remaining));
        }
        if f32::from_bits(0x3c23d70a) < amount {
            let count = cvtt(germs as f32 * (amount / remaining));
            if transfer_liquid(
                world, tables, diseases, events, cell, element, up, amount, disease, count,
            ) {
                remove_transfer(world, cell, amount, count, 3);
                remaining -= amount;
            }
        }
        if remaining <= 0.0 {
            return;
        }
    }
    for (diagonal, side) in [(down + 1, cell + 1), (down - 1, cell - 1)] {
        let diag_elem = world.write.element[diagonal];
        let diag_phase = phase(tables, diag_elem);
        let can_slide_diagonal = if diag_phase == 1 {
            true
        } else if diag_phase == 2 {
            // 对角线是另一种更轻的液体：较重液体可沿斜面滑落置换
            liquid_density(record) > liquid_density(&tables.elements[diag_elem as usize].record)
        } else {
            false
        };

        if can_slide_diagonal
            && (phase(tables, world.write.element[side]) == 3
                || world.write.properties[side] & 1 != 0)
            && phase(tables, world.write.element[down]) != 3
            && world.write.properties[cell] & 1 == 0
            && world.write.properties[diagonal] & 2 == 0
            && world.write.properties[down] & 3 == 0
        {
            swap_material(world, events, cell, diagonal);
            return;
        }
    }
}
