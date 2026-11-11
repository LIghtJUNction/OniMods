use rayon::prelude::*;
use crate::events::{EventQueues, SpawnFXInfo};
use crate::fluid_helpers::{clear_disease, cvtt, game_index, notify, phase};
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
    let mass = world.write.mass[cell];
    // 质量守恒保证：绝不因为气体薄（<0.001）就蒸发成真空。只有近零残余才清理
    if mass <= 1e-6 {
        world.flow[cell] = [0.0; 4];
        crate::fluid_displacement::evaporate(world, events, cell);
    }
    // 现代物理模型：气体扩散由 GPU/CPU 真实浓度渗透与分子量浮力偏好负责，
    // 废除古老元胞自动机在 postprocess 中的随机粗暴交换，大幅提升帧率并消除阶梯伪影
    crate::sublimation::apply_with_source(world, elements, diseases, events, cell, source_element);
}

pub(crate) fn cell(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) -> Result<()> {
    let source_element = world.write.element[cell];
    if source_element == world.vacuum_element {
        return Ok(());
    }
    if world.write.mass[cell] <= 1e-6 {
        world.flow[cell] = [0.0; 4];
        crate::fluid_displacement::evaporate(world, events, cell);
        return Ok(());
    }
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

fn inert_gas_row(world: &PhysicsWorld, elements: &ElementTable,
    min_x: usize, max_x: usize, y: usize) -> bool {
    let start = y * world.stride + min_x;
    world.write.element[start..start + max_x - min_x].iter()
        .zip(&world.write.mass[start..start + max_x - min_x])
        .all(|(&id, &mass)| id == world.vacuum_element
            || (mass > 1e-6 && elements.elements[id as usize].record.state & 3 == 1))
}

/// A gas/vacuum rectangle with no reactive solid neighbors has no postprocess
/// side effects. Confirm this against current cells after components and space
/// decay, so later changes and adjacent sublimators retain ordered processing.
pub(crate) fn region(world: &mut PhysicsWorld, elements: &ElementTable,
    diseases: &DiseaseTable, events: &mut EventQueues, bounds: (usize, usize, usize, usize)) -> Result<()> {
    let (min_x, max_x, min_y, max_y) = bounds;
    if min_x >= max_x || min_y >= max_y { return Ok(()); }
    let vacuum = world.vacuum_element;
    let environment = crate::float_env::FloatEnv::capture();
    let inert_row = |y: usize| {
        let _environment = environment.install();
        let start = y * world.stride + min_x;
        world.write.element[start..start + max_x - min_x].iter()
            .zip(&world.write.mass[start..start + max_x - min_x])
            .all(|(&id, &mass)| id == vacuum || (mass > 1e-6 && elements.elements[id as usize].record.state & 3 == 1))
    };
    let inert = if let Some(pool) = crate::parallel::pool((max_x - min_x) * (max_y - min_y)) {
        pool.install(|| (min_y..max_y).into_par_iter().with_min_len(8).all(inert_row))
    } else { (min_y..max_y).all(inert_row) };
    if inert {
        let donor = |cell: usize| {
            let record = &elements.elements[world.write.element[cell] as usize].record;
            record.state & 3 == 3 && record.sublimate_index != u16::MAX
        };
        let horizontal = (min_x..max_x).any(|x|
            donor((min_y - 1) * world.stride + x) || donor(max_y * world.stride + x));
        let vertical = (min_y..max_y).any(|y|
            donor(y * world.stride + min_x - 1) || donor(y * world.stride + max_x));
        if !horizontal && !vertical { return Ok(()); }
    }
    for y in min_y..max_y {
        // Earlier rows can change this row. Inspect the current state directly
        // before skipping it, preserving ordered liquid and sublimation work.
        if inert_gas_row(world, elements, min_x, max_x, y) {
            let donor = |cell: usize| {
                let record = &elements.elements[world.write.element[cell] as usize].record;
                record.state & 3 == 3 && record.sublimate_index != u16::MAX
            };
            let adjacent = (min_x..max_x).any(|x|
                donor((y - 1) * world.stride + x) || donor((y + 1) * world.stride + x)
                    || donor(y * world.stride + x));
            if !adjacent && !donor(y * world.stride + min_x - 1)
                && !donor(y * world.stride + max_x) {
                continue;
            }
        }
        for x in min_x..max_x {
            cell(world, elements, diseases, events, y * world.stride + x)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn inert_region_skip_preserves_cells_events_rng_and_reactive_halo() {
        for case in ["gas", "vacuum", "trace", "halo", "mixed"] {
            let make = || {
                let (mut state, _) = crate::native_chain_tests::fixture::create(96, 64,
                    if case == "mixed" { "mixed" } else { "pure" });
                let mut world = state.world.take().unwrap();
                world.initialize_boundary();
                let mut elements = (*state.elements.take().unwrap()).clone();
                elements.elements[6].record.sublimate_index = 3;
                elements.elements[6].record.sublimate_probability = 1.0;
                elements.elements[6].record.sublimate_rate = 0.1;
                elements.elements[6].record.sublimate_efficiency = 1.0;
                let source = world.stride * 20 + 20;
                if case == "vacuum" { world.write.element[source] = world.vacuum_element; }
                if case == "trace" { world.write.mass[source] = 1e-7; }
                if case == "halo" {
                    let source = world.stride * 3 + 20;
                    world.write.element[source] = 6;
                    world.write.mass[source] = 5.0;
                }
                world.sync_read_from_write();
                (world, elements, state.diseases.take().unwrap())
            };
            let (mut actual, elements, diseases) = make();
            let (mut expected, _, _) = make();
            let bounds = (4, 90, 4, 60);
            let mut actual_events = EventQueues::default();
            let mut expected_events = EventQueues::default();
            region(&mut actual, &elements, &diseases, &mut actual_events, bounds).unwrap();
            for y in bounds.2..bounds.3 { for x in bounds.0..bounds.1 {
                let cell_index = y * expected.stride + x;
                cell(&mut expected, &elements, &diseases, &mut expected_events, cell_index).unwrap();
            }}
            crate::native_chain_tests::snapshot::assert_equal(case,
                &crate::native_chain_tests::snapshot::world(&actual),
                &crate::native_chain_tests::snapshot::world(&expected));
            crate::native_chain_tests::snapshot::assert_equal(case,
                &crate::native_chain_tests::snapshot::events(&actual_events),
                &crate::native_chain_tests::snapshot::events(&expected_events));
        }
    }
}
