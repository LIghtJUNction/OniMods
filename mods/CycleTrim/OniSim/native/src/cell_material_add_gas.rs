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
    if state == 1 {
        // 多组分气体混合：气体与气体共存，不强行排挤，形成道尔顿分压混合相
        if world.mixture_mass[cell] <= 0.0 || world.mixture_element[cell] == element {
            world.mixture_element[cell] = element;
            world.mixture_mass[cell] += mass;
            if world.mixture_mass[cell] > world.write.mass[cell] {
                let dom_elem = world.mixture_element[cell];
                let dom_mass = world.mixture_mass[cell];
                world.mixture_element[cell] = world.write.element[cell];
                world.mixture_mass[cell] = world.write.mass[cell];
                world.write.element[cell] = dom_elem;
                world.write.mass[cell] = dom_mass;
            }
            notify(world, events, cell);
            return;
        }
    } else if state == 2 {
        // 激进真实物理：气体进入液体中（微气泡、气相分压），绝不湮灭！
        if world.mixture_mass[cell] <= 0.0 || world.mixture_element[cell] == element {
            world.mixture_element[cell] = element;
            world.mixture_mass[cell] += mass;
            notify(world, events, cell);
            return;
        } else {
            // 格内已有其他共存相时，向上浮动冒泡，寻找水面或相邻气体格
            let mut curr = cell;
            for _ in 0..128 {
                let above = curr + world.stride;
                if above >= world.write.mass.len() { break; }
                if phase(elements, world.write.element[above]) != 2 {
                    // 到达水面或气体格！将气体释放到水面上方
                    add_gas(world, elements, diseases, events, above, element, mass, temperature, disease, count);
                    return;
                }
                curr = above;
            }
        }
    }
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
