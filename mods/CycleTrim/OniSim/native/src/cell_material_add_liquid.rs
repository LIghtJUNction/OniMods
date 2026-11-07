use crate::events::EventQueues;
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{add_mass, cvtt, notify, phase, swap_material};
use crate::gas::displace_gas;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

// AddLiquid, ELF 0x5fc20. The solid-neighbor search precedes displacement.
pub(crate) fn add_liquid(
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
    if world.write.element[cell] == element {
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
    let stride = world.stride;
    let mut destination = cell;
    let mut all_solid = false;
    if phase(elements, world.write.element[cell]) == 3 {
        let neighbors = [cell - 1, cell + 1, cell - stride, cell + stride];
        if let Some(found) = neighbors
            .into_iter()
            .find(|&neighbor| phase(elements, world.write.element[neighbor]) != 3)
        {
            destination = found;
        } else {
            all_solid = true;
        }
    }
    let old_element = world.write.element[destination];
    let old_record = &elements.elements[old_element as usize].record;
    let state = old_record.state & 3;
    if !all_solid && old_record.id as u32 != 0xa9360b34 {
        if state == 0 {
            world.write.element[destination] = element;
            add_mass(
                &mut world.write,
                diseases,
                destination,
                mass,
                temperature,
                disease,
                count,
            );
            notify(world, events, destination);
            return;
        }
        if state == 2 && crate::fluid_helpers::are_liquids_miscible(&elements.elements[element as usize].record, old_record) {
            // 互溶液体混溶：形成多组分液体相
            if world.mixture_mass[destination] <= 0.0 || world.mixture_element[destination] == element {
                world.mixture_element[destination] = element;
                world.mixture_mass[destination] += mass;
                if world.mixture_mass[destination] > world.write.mass[destination] {
                    let dom_elem = world.mixture_element[destination];
                    let dom_mass = world.mixture_mass[destination];
                    world.mixture_element[destination] = world.write.element[destination];
                    world.mixture_mass[destination] = world.write.mass[destination];
                    world.write.element[destination] = dom_elem;
                    world.write.mass[destination] = dom_mass;
                }
                notify(world, events, destination);
                return;
            }
        }
        if state == 1 {
            // 液体进入气体格：若液体体积 < 0.95，气体保留在剩余空隙容积
            let max_m = elements.elements[element as usize].record.max_mass.max(1.0);
            let liq_vol = mass / max_m;
            if liq_vol < 0.95 {
                let gas_elem = old_element;
                let gas_mass = world.write.mass[destination];
                world.write.element[destination] = element;
                world.write.mass[destination] = mass;
                world.write.temperature[destination] = temperature;
                world.mixture_element[destination] = gas_elem;
                world.mixture_mass[destination] = gas_mass;
                notify(world, events, destination);
                return;
            }
        }
        let displaced = if state == 1 {
            displace_gas(world, elements, diseases, events, destination, old_element)
        } else {
            displace_liquid(world, elements, diseases, events, destination, old_element)
        };
        if displaced {
            world.write.element[destination] = element;
            add_mass(
                &mut world.write,
                diseases,
                destination,
                mass,
                temperature,
                disease,
                count,
            );
            notify(world, events, destination);
            return;
        }
        let upper = destination + stride;
        if state == 1 && phase(elements, world.write.element[upper]) == 2 {
            // Official swap notifies upper before the requested destination.
            swap_material(world, events, upper, destination);
        }
        for neighbor in [
            destination + 1,
            destination - 1,
            upper,
            destination - stride,
        ] {
            if world.write.element[neighbor] == element {
                add_mass(
                    &mut world.write,
                    diseases,
                    neighbor,
                    mass,
                    temperature,
                    disease,
                    count,
                );
                return;
            }
        }
    }
    let previous_mass = world.write.mass[destination];
    // COMISS incoming,previous / JAE: less or unordered only subtracts.
    if !(mass >= previous_mass) {
        world.write.mass[destination] = previous_mass - mass;
        return;
    }
    let remaining = mass - previous_mass;
    world.write.element[destination] = element;
    world.write.mass[destination] = 0.0;
    world.write.temperature[destination] = 0.0;
    let remaining_count = cvtt((remaining / mass) * count as f32);
    add_mass(
        &mut world.write,
        diseases,
        destination,
        remaining,
        temperature,
        disease,
        remaining_count,
    );
    // Unlike AddGas, this only changes the element; retained mass/germs survive.
    if world.write.mass[destination] <= f32::from_bits(0x00800000) {
        world.write.element[destination] = world.vacuum_element;
    }
    notify(world, events, destination);
}
