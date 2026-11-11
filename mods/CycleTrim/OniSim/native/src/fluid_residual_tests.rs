use super::*;

fn fixture() -> (PhysicsWorld, ElementTable, DiseaseTable, usize) {
    let mut bytes = 24_i32.to_le_bytes().to_vec();
    for index in 0..24_u16 {
        let mut record = [0_u8; 164];
        let hash = match index {
            0 => 0x2d39bf75_u32,
            1 => 0xa9360b34_u32,
            2 => 0x6d95058c_u32,
            _ => 1000 + index as u32,
        };
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&index.to_le_bytes());
        record[10] = if index < 2 { 0 } else { 1 + (index % 3) as u8 };
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0; 96]);
    let elements = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(8, 8, 0, &elements).unwrap();
    // Cells::new uses u16::MAX until a grid image is loaded. These tests
    // construct the world directly, so initialize its interior and padding.
    world.write.element.fill(world.vacuum_element);
    world.initialize_boundary();
    let cell = 4 * world.stride + 4;
    world.write.element[cell] = 4; // Liquid.
    world.write.mass[cell] = 10.0;
    world.write.temperature[cell] = 321.0;
    world.mixture_element[cell] = 3; // Gas, deliberately cross-phase.
    world.mixture_mass[cell] = 0.10748;
    world.sync_read_from_write();
    (world, elements, DiseaseTable::default(), cell)
}

#[test]
fn clearing_primary_promotes_residue_without_cooling_it() {
    let (mut world, _, _, cell) = fixture();
    clear_primary(&mut world, cell);
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 321.0);
    assert_eq!(world.mixture_mass[cell], 0.0);
}

#[test]
fn twenty_cross_phase_residues_survive_and_largest_is_published() {
    let (mut world, _, _, cell) = fixture();
    world.extra_mixtures.insert(cell, (5..24).map(|id| (id, id as f32)).collect());
    let mut before = Vec::new();
    world.for_each_cell_species(cell, |id, mass| {
        if id != 4 { before.push((id, mass)); }
    });
    clear_primary(&mut world, cell);
    let mut after = Vec::new();
    world.for_each_cell_species(cell, |id, mass| after.push((id, mass)));
    before.sort_by_key(|p| p.0);
    after.sort_by_key(|p| p.0);
    assert_eq!(after.len(), 20);
    assert_eq!(before, after);
    assert_eq!(world.write.element[cell], 23);
    assert_eq!(world.write.mass[cell], 23.0);
    assert_eq!(world.write.temperature[cell], 321.0);
}

#[test]
fn historical_vacuum_uses_read_temperature_then_explicit_ambient_estimate() {
    for (read_temperature, expected) in [(310.0, 310.0), (0.0, 293.15), (f32::NAN, 293.15)] {
        let (mut world, _, _, cell) = fixture();
        clear_cell(&mut world.write, world.vacuum_element, cell);
        world.read.temperature[cell] = read_temperature;
        let mut events = EventQueues::default();
        notify(&mut world, &mut events, cell);
        assert_eq!(world.write.element[cell], 3);
        assert_eq!(world.write.mass[cell], 0.10748);
        assert_eq!(world.write.temperature[cell], expected);
        assert!(!world.restore_empty_primary(cell));
    }
}

#[test]
fn directional_transfer_keeps_residue_and_transfers_only_primary() {
    let (mut world, elements, diseases, cell) = fixture();
    let destination = cell + 1;
    world.write.element[destination] = 4;
    world.write.mass[destination] = 2.0;
    world.write.temperature[destination] = 321.0;
    let mut events = EventQueues::default();
    assert!(crate::fluid::displace_liquid_directional(
        &mut world, &elements, &diseases, &mut events, cell, destination, 4,
    ));
    assert_eq!(world.write.mass[destination], 12.0);
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 321.0);
}

#[test]
fn simple_displacement_and_evaporation_preserve_residue() {
    let (mut world, elements, diseases, cell) = fixture();
    let destination = cell + 1;
    let mut events = EventQueues::default();
    assert!(crate::fluid_displacement::displace_liquid_simple(
        &mut world, &elements, &diseases, &mut events, cell, 4, &[destination],
    ));
    assert_eq!(world.write.mass[destination], 10.0);
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    world.mixture_element[cell] = 5;
    world.mixture_mass[cell] = 0.05;
    crate::fluid_displacement::evaporate(&mut world, &mut events, cell);
    assert_eq!(world.write.element[cell], 5);
    assert_eq!(world.write.mass[cell], 0.05);
    assert_eq!(world.write.temperature[cell], 321.0);
}

#[test]
fn pure_primary_still_clears_to_vacuum() {
    let (mut world, _, _, cell) = fixture();
    world.clear_cell_mixtures(cell);
    clear_primary(&mut world, cell);
    assert_eq!(world.write.element[cell], world.vacuum_element);
    assert_eq!(world.write.mass[cell], 0.0);
    assert_eq!(world.write.temperature[cell], 0.0);
}

#[test]
fn falling_primary_leaves_cross_phase_inventory_with_valid_temperature() {
    let (mut world, mut elements, diseases, cell) = fixture();
    elements.elements[4].record.low_temperature = 1.0;
    elements.elements[4].record.high_temperature = 1000.0;
    world.simulate_all_active = true;
    // An open side over a solid ledge selects the falling-object path.
    let down = cell - world.stride;
    world.write.element[down - 1] = 2;
    world.write.properties[down - 1] = 2;
    world.write.element[down - world.stride] = world.vacuum_element;
    world.sync_read_from_write();
    let mut events = EventQueues::default();
    crate::fluid::update_liquid(&mut world, &elements, &diseases, &mut events, cell, 0);
    assert_eq!(events.spawn_falling_liquid_info.len(), 1);
    let falling = &events.spawn_falling_liquid_info[0];
    assert_eq!(falling.elem_idx, 4);
    assert_eq!(falling.mass, 10.0);
    assert_eq!(falling.temperature, 321.0);
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 321.0);
}

#[test]
fn sink_removes_primary_but_retains_secondary_inventory() {
    let (mut world, elements, diseases, cell) = fixture();
    let down = cell - world.stride;
    world.write.element[down] = world.sink_element;
    world.sync_read_from_write();
    let mut events = EventQueues::default();
    crate::fluid::update_liquid(&mut world, &elements, &diseases, &mut events, cell, 0);
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 321.0);
}

#[test]
fn transition_ejection_preserves_twenty_cross_phase_residues_and_current_temperature() {
    // Cover both small-solid ore ejection and falling-liquid ejection.
    for target in [2, 4] {
        let (mut world, mut elements, _, cell) = fixture();
        world.simulate_all_active = true;
        world.write.temperature[cell] = 350.0;
        world.extra_mixtures.insert(cell, (5..24).map(|id| (id, id as f32)).collect());
        elements.elements[target as usize].record.default_mass = 100.0;
        elements.elements[target as usize].record.low_temperature = 1.0;
        elements.elements[target as usize].record.high_temperature = 1000.0;
        let mut before = Vec::new();
        world.for_each_cell_species(cell, |id, mass| {
            if id != 4 { before.push((id, mass)); }
        });
        let mut events = EventQueues::default();
        crate::transition::apply_selected_transition(
            &mut world, &elements, &mut events, cell, target, u16::MAX, 0.0, 348.5,
        );
        let mut after = Vec::new();
        world.for_each_cell_species(cell, |id, mass| after.push((id, mass)));
        before.sort_by_key(|p| p.0);
        after.sort_by_key(|p| p.0);
        assert_eq!(before, after);
        assert_eq!(after.len(), 20);
        assert_eq!(world.write.element[cell], 23);
        assert_eq!(world.write.mass[cell], 23.0);
        assert_eq!(world.write.temperature[cell], 348.5);
        if target == 4 {
            assert_eq!(events.spawn_falling_liquid_info.len(), 1);
            assert_eq!(events.spawn_falling_liquid_info[0].mass, 10.0);
        } else {
            assert_eq!(events.spawn_ore_info.len(), 1);
            assert_eq!(events.spawn_ore_info[0].mass, 10.0);
        }
    }
}

#[test]
fn phase_removal_retains_residue_at_current_write_temperature() {
    let (mut world, elements, _, cell) = fixture();
    world.write.temperature[cell] = 345.0; // Read image still has 321 K.
    let mut events = EventQueues::default();
    crate::cell_material_commands::remove_phase(
        &mut world, &elements, &mut events, cell, -10.0, 2,
    );
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 345.0);
}

#[test]
fn vacuum_replacement_clears_all_inventory_without_resurrection() {
    let (mut world, elements, _, cell) = fixture();
    world.extra_mixtures.insert(cell, vec![(5, 2.0)]);
    let vacuum = world.vacuum_element;
    let mut events = EventQueues::default();
    crate::cell_material_commands::replace_element(
        &mut world, &elements, &mut events, cell, vacuum, 0.0, 0.0, 255, 0,
    );
    assert_eq!(world.write.element[cell], vacuum);
    assert_eq!(world.write.mass[cell], 0.0);
    assert_eq!(world.write.temperature[cell], 0.0);
    assert_eq!(world.mixture_mass[cell], 0.0);
    assert!(!world.extra_mixtures.contains_key(&cell));
    assert!(!world.restore_empty_primary(cell));
}

#[test]
fn gas_cancellation_preserves_residue_at_precommand_temperature() {
    let (mut world, elements, diseases, cell) = fixture();
    // A solid primary and blocked neighbours select AddGas's cancellation path.
    world.write.element[cell] = 2;
    world.write.temperature[cell] = 345.0;
    for destination in [cell - 1, cell + 1, cell + world.stride] {
        world.write.element[destination] = 2;
    }
    let mut events = EventQueues::default();
    crate::cell_material_add_gas::add_gas(
        &mut world, &elements, &diseases, &mut events, cell, 6, 10.0, 400.0, 255, 0,
    );
    assert_eq!(world.write.element[cell], 3);
    assert_eq!(world.write.mass[cell], 0.10748);
    assert_eq!(world.write.temperature[cell], 345.0);
}

#[test]
fn flood_and_rectangle_exhaustion_preserve_current_residual_temperature() {
    for flood in [false, true] {
        let (mut world, elements, diseases, cell) = fixture();
        world.write.temperature[cell] = 345.0;
        let mut events = EventQueues::default();
        let mut consumed = crate::events::ConsumedMassInfo {
            disease_idx: 255,
            ..Default::default()
        };
        if flood {
            crate::mass_components::FloodScratch::default().remove_mass(
                &mut world, &elements, &diseases, &mut events, cell, 1, 10.0, 4,
                &mut consumed,
            ).unwrap();
        } else {
            crate::cell_material_rect::rect_remove(
                &mut world, &elements, &diseases, &mut events, 4, 4, 1, 1, 10.0, 4,
                &mut consumed,
            ).unwrap();
        }
        assert_eq!(consumed.mass, 10.0);
        assert_eq!(world.write.element[cell], 3);
        assert_eq!(world.write.mass[cell], 0.10748);
        assert_eq!(world.write.temperature[cell], 345.0);
    }
}
