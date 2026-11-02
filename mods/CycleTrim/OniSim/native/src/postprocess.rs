use crate::events::{EventQueues, SpawnFXInfo};
use crate::fluid_helpers::{clear_disease, cvtt, game_index, notify, phase, swap_material};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

fn offgas_fx(world: &PhysicsWorld, events: &mut EventQueues, cell: usize, fx_hash: i32) {
    if let Some(cell_idx) = game_index(world, cell) {
        events.spawn_fx_info.push(SpawnFXInfo {
            cell_idx,
            fx_hash,
            rotation: 0.0,
        });
    }
}

fn liquid(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    source_element: u16,
) {
    let source = &elements.elements[source_element as usize].record;
    if world.write.mass[cell] < 0.01 {
        world.flow[cell] = [0.0; 4];
        crate::fluid_displacement::evaporate(world, events, cell);
        return;
    }
    if crate::postprocess_pressure::density_displacement(
        world,
        elements,
        events,
        cell,
        source,
        f32::from_bits(0x3e99999a),
    ) {
        return;
    }
    let maximum = source.max_mass;
    if maximum < world.write.mass[cell] {
        if crate::postprocess_pressure::pressure_break(
            world,
            elements,
            events,
            cell,
            world.write.mass[cell] / maximum,
        ) {
            return;
        }
        let source_mass = world.write.mass[cell];
        let above = cell + world.stride;
        let above_mass = world.write.mass[above];
        let above_element = world.write.element[above];
        if 1.5 * maximum < source_mass
            && above_mass < source_mass
            && phase(elements, above_element) == 2
        {
            let scaled = above_mass * f32::from_bits(0x3f8147ae);
            let threshold = if maximum <= scaled { scaled } else { maximum };
            if threshold < source_mass
                && crate::fluid_displacement::displace_liquid(
                    world,
                    elements,
                    diseases,
                    events,
                    above,
                    above_element,
                )
            {
                let old_mass = world.write.mass[cell];
                let amount = old_mass * f32::from_bits(0x3efeb9f3);
                let count = cvtt(world.write.disease_count[cell] as f32 * (amount / old_mass));
                world.write.mass[above] = amount;
                world.write.temperature[above] = world.write.temperature[cell];
                world.write.element[above] = world.write.element[cell];
                world.write.disease_idx[above] = world.write.disease_idx[cell];
                world.write.disease_count[above] = count;
                world.write.disease_infestation[above] = 0;
                world.write.disease_growth[above] = 0.0;
                notify(world, events, above);
                world.write.mass[cell] -= amount;
                world.write.disease_count[cell] =
                    world.write.disease_count[cell].wrapping_sub(count);
                if world.write.disease_count[cell] <= 0 {
                    clear_disease(&mut world.write, cell);
                }
                return;
            }
        }
    }
    if crate::partial_transition::apply(world, elements, diseases, events, cell) {
        return;
    }
    let above = cell + world.stride;
    if world.write.mass[above] >= 1.8 {
        return;
    }
    let target = source.sublimate_index;
    if target == u16::MAX || target == world.vacuum_element {
        return;
    }
    let above_element = world.write.element[above];
    if elements.elements[above_element as usize].record.state & 2 != 0 {
        return;
    }
    // RNG advances before checking the destination's impermeability bit.
    let sample = world.next_random() as f32 * f32::from_bits(0x38000100);
    if world.write.properties[above] & 1 != 0 || source.sublimate_probability <= sample {
        return;
    }
    let source_mass = world.write.mass[cell];
    let mut amount = source.off_gas_probability * source_mass;
    if 1.0 <= amount {
        amount = 1.0;
    }
    let below = cell - world.stride;
    let below_mass = world.write.mass[below];
    let draw_below =
        world.write.element[below] == world.write.element[cell] && source_mass < below_mass;
    if draw_below {
        amount = source.off_gas_probability * below_mass;
        if 1.0 <= amount {
            amount = 1.0;
        }
    }
    if source_mass - amount < 0.01 {
        crate::offgas::transfer_with_source(
            world,
            elements,
            events,
            cell,
            cell,
            source_mass,
            true,
            source_element,
        );
        if draw_below && 0.0 < amount - source_mass {
            let merge = world.write.disease_idx[below] == world.write.disease_idx[cell]
                || world.write.disease_idx[cell] == u8::MAX;
            crate::offgas::transfer_with_source(
                world,
                elements,
                events,
                below,
                cell,
                amount - source_mass,
                merge,
                source_element,
            );
        }
        offgas_fx(world, events, cell, source.sublimate_fx);
        return;
    }
    if amount <= 0.0 {
        return;
    }
    if target != above_element
        && above_element != world.vacuum_element
        && !crate::gas::displace_gas(world, elements, diseases, events, above, above_element)
    {
        return;
    }
    let space = 1.8 - world.write.mass[above];
    let output_mass = source.sublimate_efficiency * amount;
    if space < output_mass {
        amount *= space / output_mass;
    }
    let merge = world.write.disease_idx[cell] == world.write.disease_idx[above]
        || world.write.disease_idx[above] == u8::MAX;
    crate::offgas::transfer_with_source(
        world,
        elements,
        events,
        cell,
        above,
        amount,
        merge,
        source_element,
    );
    world.accumulated_flow[cell] += amount;
    offgas_fx(world, events, cell, source.sublimate_fx);
}

fn gas(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    source_element: u16,
) {
    let source = &elements.elements[source_element as usize].record;
    let direction = world.gas_scan_direction as isize;
    world.gas_scan_direction = -world.gas_scan_direction;
    let first = cell.checked_add_signed(direction).unwrap();
    let second = cell.checked_add_signed(-direction).unwrap();
    let below = cell - world.stride;
    let above = cell + world.stride;
    let mass = world.write.mass[cell];
    let discard = if f32::from_bits(0x3089705f) <= mass {
        mass < 0.001
            && [first, second, below, above].into_iter().any(|neighbor| {
                !(world.write.mass[neighbor] < 1.0)
                    && phase(elements, world.write.element[neighbor]) == 1
            })
    } else {
        true
    };
    if discard {
        world.flow[cell] = [0.0; 4];
        crate::fluid_displacement::evaporate(world, events, cell);
    }
    if !crate::postprocess_pressure::density_displacement(
        world,
        elements,
        events,
        cell,
        source,
        f32::from_bits(0x3f7d70a4),
    ) && (world.write.properties[cell] & 2 != 0
        || !crate::partial_melt::apply(world, elements, diseases, events, cell))
    {
        let sample = world.next_random() as f32;
        if f32::from_bits(0x46e66499) < sample {
            // The tiny-mass check uses the entering direction, but random
            // swaps use its already-negated value (ELF 0x7233e..0x72361).
            let first = cell
                .checked_add_signed(world.gas_scan_direction as isize)
                .unwrap();
            let second = cell
                .checked_add_signed(-(world.gas_scan_direction as isize))
                .unwrap();
            let mut choice = Some(first);
            let mut temperature = world.write.temperature[first];
            if f32::MAX < temperature
                || world.next_random() as f32 <= 16383.5
                || phase(elements, world.write.element[first]) != 1
            {
                choice = None;
                temperature = f32::MAX;
            }
            let second_temperature = world.write.temperature[second];
            if second_temperature <= temperature
                && 16383.5 < world.next_random() as f32
                && phase(elements, world.write.element[second]) == 1
            {
                choice = Some(second);
                temperature = second_temperature;
            }
            let below_temperature = world.write.temperature[below];
            let below_record = &elements.elements[world.write.element[below] as usize].record;
            if below_temperature <= temperature
                && 16383.5 < world.next_random() as f32
                && below_record.state & 3 == 1
                && below_record.molar_mass < source.molar_mass
            {
                choice = Some(below);
            }
            if let Some(destination) = choice {
                swap_material(world, events, destination, cell);
            }
        }
    }
    crate::sublimation::apply_with_source(world, elements, diseases, events, cell, source_element);
}

pub(crate) fn cell(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) -> Result<()> {
    // PostProcessCell retains this metadata pointer across swaps and phase edits.
    let source_element = world.write.element[cell];
    let state = elements.elements[source_element as usize].record.state;
    match state & 3 {
        2 => liquid(world, elements, diseases, events, cell, source_element),
        3 => {
            if state & 11 == 11 {
                crate::unstable::apply(world, elements, diseases, events, cell);
            }
        }
        1 => gas(world, elements, diseases, events, cell, source_element),
        _ if world.write.properties[cell] & 1 == 0 => {
            crate::sublimation::apply_with_source(
                world,
                elements,
                diseases,
                events,
                cell,
                source_element,
            );
        }
        _ => {}
    }
    Ok(())
}
