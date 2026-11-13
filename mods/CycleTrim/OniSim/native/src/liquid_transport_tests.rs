use super::*;

fn fixture() -> (PhysicsWorld, ElementTable, DiseaseTable, Region) {
    let mut bytes = 25_i32.to_le_bytes().to_vec();
    for id in 0..25_u16 {
        let mut record = [0_u8; 164];
        let hash = match id {
            0 => 0xa9360b34_u32,
            1 => 0x2d39bf75,
            2 => 0x6d95058c,
            _ => 100 + id as u32,
        };
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&id.to_le_bytes());
        record[10] = match id {
            0 | 1 => 0,
            2 => 3,
            3 | 4 => 1,
            _ => 2,
        };
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        record[40..44].copy_from_slice(&1000_f32.to_le_bytes());
        record[52..56].copy_from_slice(&1000_f32.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0; 100]);
    let elements = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(8, 12, 0, &elements).unwrap();
    world.write.element.fill(2);
    world.write.mass.fill(0.0);
    world.write.temperature.fill(300.0);
    world.write.disease_idx.fill(255);
    let region = Region {
        min_x: 1,
        max_x: 9,
        min_y: 1,
        max_y: 13,
        ..Default::default()
    };
    (world, elements, DiseaseTable::default(), region)
}
fn put(world: &mut PhysicsWorld, x: usize, y: usize, id: u16, mass: f32) -> usize {
    let cell = y * world.stride + x;
    world.write.element[cell] = id;
    world.write.mass[cell] = mass;
    cell
}
fn mass(world: &PhysicsWorld, id: u16) -> f32 {
    let mut result = 0.0;
    for cell in 0..world.write.mass.len() {
        world.for_each_cell_species(cell, |element, amount| {
            if element == id {
                result += amount;
            }
        });
    }
    result
}
fn step(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    region: Region,
) {
    world.sync_read_from_write();
    let _ = crate::simulation_step_fluid::liquid_phase(
        world,
        elements,
        diseases,
        &mut EventQueues::default(),
        region,
    );
}

#[test]
fn enclosed_gas_counterflows_while_liquid_falls_without_particles() {
    for pressure in [0.001, 2.0, 2000.0] {
        let (mut world, elements, diseases, region) = fixture();
        let source = put(&mut world, 4, 7, 5, 1000.0);
        let below = put(&mut world, 4, 6, 3, pressure);
        step(&mut world, &elements, &diseases, region);
        assert_eq!(world.write.element[below], 5);
        assert!((world.write.mass[below] - 1000.0).abs() < 0.01);
        assert_eq!(world.write.element[source], 3);
        assert!((mass(&world, 3) - pressure).abs() < 0.001);
        assert!((mass(&world, 5) - 1000.0).abs() < 0.01);
    }
}

#[test]
fn partial_fill_returns_only_the_displaced_gas_volume() {
    let (mut world, elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 250.0);
    let target = put(&mut world, 4, 6, 3, 8.0);
    let mut flow = LiquidTransport::default();
    assert!(flow.face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        0.25,
        2
    ));
    assert_eq!(world.write.element[target], 5);
    assert_eq!(world.write.mass[target], 250.0);
    assert_eq!(world.write.mass[source], 2.0);
    assert_eq!(world.mixture_mass[target], 6.0);
    assert_eq!(mass(&world, 3), 8.0);
}

#[test]
fn all_twenty_liquids_fall_in_equal_proportions_with_both_gases_retained() {
    let (mut world, elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 50.0);
    for id in 6..25 {
        world.add_species_mass(&elements, source, id, 50.0);
    }
    let target = put(&mut world, 4, 6, 3, 2.0);
    world.add_species_mass(&elements, target, 4, 3.0);
    let mut flow = LiquidTransport::default();
    assert!(flow.face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        0.5,
        2
    ));
    for id in 5..25 {
        assert!((mass(&world, id) - 50.0).abs() < 0.0001, "species {id}");
    }
    let mut target_count = 0;
    world.for_each_cell_species(target, |id, amount| {
        if id >= 5 {
            target_count += 1;
            assert!((amount - 25.0).abs() < 0.0001);
        }
    });
    assert_eq!(target_count, 20);
    world.normalize_cell_mixtures(target);
    for id in 5..25 {
        assert!((mass(&world, id) - 50.0).abs() < 0.0001);
    }
    assert_eq!(mass(&world, 3), 2.0);
    assert_eq!(mass(&world, 4), 3.0);
}

#[test]
fn waterfall_reaches_the_floor_monotonically_and_spreads_there() {
    let (mut world, elements, diseases, region) = fixture();
    for y in 2..=10 {
        for x in 2..=7 {
            put(&mut world, x, y, 3, 2.0);
        }
    }
    put(&mut world, 4, 10, 5, 1000.0);
    let mut prior_height = 10.0;
    for _ in 0..10 {
        step(&mut world, &elements, &diseases, region);
        let mut height = 0.0;
        for cell in 0..world.write.mass.len() {
            world.for_each_cell_species(cell, |id, amount| {
                if id == 5 {
                    height += amount * (cell / world.stride) as f32;
                }
            });
        }
        height /= 1000.0;
        assert!(
            height <= prior_height + 0.001,
            "rose from {prior_height} to {height}"
        );
        prior_height = height;
        assert!((mass(&world, 5) - 1000.0).abs() < 0.1);
    }
    assert!(
        prior_height < 2.01,
        "water never reached floor: {prior_height}"
    );
    assert!(world.write.mass[2 * world.stride + 3] > 0.0);
}

#[test]
fn permeability_and_viscosity_limit_actual_volume_transfer() {
    let (mut world, mut elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 1000.0);
    let target = put(&mut world, 4, 6, 3, 2.0);
    let mut flow = LiquidTransport::default();
    world.write.properties[target] = 2;
    assert!(!flow.face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        1.0,
        2
    ));
    world.write.properties[target] = 0;
    elements.elements[5].record.viscosity = 100.0;
    assert!(flow.face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        1.0,
        2
    ));
    assert!((world.write.mass[target] - 100.0).abs() < 0.01);
    assert!((world.write.mass[source] - 900.0).abs() < 0.01);
}

#[test]
fn counterflow_preserves_heat_and_germs() {
    let (mut world, elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 500.0);
    let target = put(&mut world, 4, 6, 3, 2.0);
    world.write.temperature[source] = 350.0;
    world.write.temperature[target] = 280.0;
    world.write.disease_count[source] = 100;
    let before = 500.0 * 350.0 + 2.0 * 280.0;
    assert!(LiquidTransport::default().face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        0.5,
        2
    ));
    let mut after = 0.0;
    for cell in [source, target] {
        world.for_each_cell_species(cell, |_, amount| {
            after += amount * world.write.temperature[cell]
        });
    }
    assert!((before - after).abs() < 0.1, "heat {before} -> {after}");
    assert_eq!(
        world.write.disease_count[source] + world.write.disease_count[target],
        100
    );
}

#[test]
fn dense_liquid_sinks_without_swapping_its_newly_received_inventory_back() {
    let (mut world, mut elements, diseases, _) = fixture();
    elements.elements[5].record.molar_mass = 100.0;
    let source = put(&mut world, 4, 7, 5, 1000.0);
    let target = put(&mut world, 4, 6, 6, 1000.0);
    assert!(LiquidTransport::default().face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        1.0,
        2
    ));
    assert_eq!(world.write.element[target], 5);
    assert_eq!(world.write.element[source], 6);
    assert_eq!(mass(&world, 5), 1000.0);
    assert_eq!(mass(&world, 6), 1000.0);
}

#[test]
fn sink_remains_a_sink_and_only_drains_the_outgoing_liquid() {
    let (mut world, elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 1000.0);
    let sink = world.sink_element;
    let target = put(&mut world, 4, 6, sink, 0.0);
    assert!(LiquidTransport::default().face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        1.0,
        2
    ));
    assert_eq!(world.write.element[target], sink);
    assert_eq!(world.write.mass[target], 0.0);
    assert_eq!(mass(&world, 5), 0.0);
}

#[test]
fn level_supported_pool_does_not_oscillate() {
    let (mut world, elements, diseases, region) = fixture();
    for x in 2..=7 {
        put(&mut world, x, 2, 5, 500.0);
    }
    step(&mut world, &elements, &diseases, region);
    let expected = world.write.mass.clone();
    for _ in 0..32 {
        step(&mut world, &elements, &diseases, region);
        assert_eq!(world.write.mass, expected);
    }
}

#[test]
fn mixed_column_transmits_pressure_through_a_full_cell() {
    let (mut world, elements, diseases, _) = fixture();
    let source = put(&mut world, 4, 7, 5, 500.0);
    world.add_species_mass(&elements, source, 6, 500.0);
    let target = put(&mut world, 4, 6, 5, 500.0);
    world.add_species_mass(&elements, target, 6, 500.0);
    assert!(LiquidTransport::default().face(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        source,
        target,
        1.0,
        2
    ));
    assert!(cell_volume(&world, &elements, target) > 1.0);
    assert!(cell_volume(&world, &elements, source) < 1.0);
    for id in [5, 6] {
        assert!((mass(&world, id) - 1000.0).abs() < 0.001);
    }
}

#[test]
fn gpu_diffusion_leaves_extra_species_cells_owned_by_volume_transport() {
    let (mut world, elements, _, region) = fixture();
    for y in 3..=8 {
        for x in 2..=7 {
            put(&mut world, x, y, 6, 1000.0);
        }
    }
    let cell = put(&mut world, 4, 6, 5, 1000.0);
    world.add_species_mass(&elements, cell, 6, 10.0);
    world.add_species_mass(&elements, cell, 7, 1.0);
    let mut expected = Vec::new();
    world.for_each_cell_species(cell, |id, amount| expected.push((id, amount)));
    let mut gpu = crate::gpu_sim::GpuSimulation::new(8, 12).unwrap();
    if let Some(pending) =
        crate::multicomponent_flux::begin_sparse_liquid_region(&world, &elements, region, &mut gpu)
            .unwrap()
    {
        crate::multicomponent_flux::finish_sparse_liquid_region(
            pending,
            &mut world,
            &mut EventQueues::default(),
            &mut Vec::new(),
            &mut gpu,
        )
        .unwrap();
    }
    let mut actual = Vec::new();
    world.for_each_cell_species(cell, |id, amount| actual.push((id, amount)));
    assert_eq!(actual, expected);
}

#[test]
fn draining_column_follows_the_bottom_without_stale_pressure_masks_blocking_it() {
    let (mut world, elements, diseases, _) = fixture();
    for y in 5..=7 {
        put(&mut world, 4, y, 5, 1000.0);
    }
    put(&mut world, 4, 4, 3, 2.0);
    let mut covered = vec![0; world.write.mass.len()];
    covered[6 * world.stride + 4] = 4;
    covered[7 * world.stride + 4] = 4;
    let region = Region {
        min_x: 4,
        max_x: 5,
        min_y: 4,
        max_y: 8,
        ..Default::default()
    };
    assert!(LiquidTransport::default().run(
        &mut world,
        &elements,
        &diseases,
        &mut EventQueues::default(),
        region,
        &covered
    ));
    for y in 4..=6 {
        let cell = y * world.stride + 4;
        assert_eq!(world.write.element[cell], 5);
        assert_eq!(world.write.mass[cell], 1000.0);
    }
    assert_eq!(world.write.element[7 * world.stride + 4], 3);
    assert_eq!(mass(&world, 5), 3000.0);
}
