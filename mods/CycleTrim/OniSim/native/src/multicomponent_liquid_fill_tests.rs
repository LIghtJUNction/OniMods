use super::*;
use crate::world::PhysicsWorld;

fn liquid_elements() -> ElementTable {
    let mut bytes = 7_i32.to_le_bytes().to_vec();
    for id in 0..7_u16 {
        let mut record = [0_u8; 164];
        let hash = match id {
            0 => 0x2d39bf75_u32, 1 => 0x6d95058c, 2 => 0xa9360b34,
            _ => 0x1000 + id as u32,
        };
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&id.to_le_bytes());
        record[10] = match id { 0 => 0, 1 | 2 => 3, 3 => 1, _ => 2 };
        let capacity = match id { 5 => 500.0_f32, 6 => 2000.0, _ => 1000.0 };
        record[52..56].copy_from_slice(&capacity.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + 7 * 4, 0);
    ElementTable::parse(&bytes).unwrap()
}

#[test]
fn liquid_fill_includes_all_species_and_matches_serial_parallel_and_initial_publication() {
    let elements = liquid_elements();
    let count = 2048;
    let mut grid = Grid::new(count as i32, 1, 0, 0).unwrap();
    grid.temperature.fill(300.0);
    for cell in 0..count {
        (grid.element[cell], grid.mass[cell]) = match cell % 4 {
            0 => (0, 0.0), 1 => (3, 2.0), 2 => (4, 250.0), _ => (4, 2000.0),
        };
    }
    let mut world = PhysicsWorld::from_grid(&grid, &elements).unwrap();
    for cell in 0..count {
        if cell % 4 >= 2 {
            let internal = world.internal_index(cell);
            let overfull = cell % 4 == 3;
            world.mixture_element[internal] = 5;
            world.mixture_mass[internal] = if overfull { 500.0 } else { 125.0 };
            world.extra_mixtures.insert(internal, vec![(6, if overfull { 2000.0 } else { 500.0 })]);
        }
    }
    let state = |_: usize, internal: usize| (world.write.mass[internal], world.write.temperature[internal]);
    let mut serial = MulticomponentBuffers::new(count).unwrap();
    serial.update_world_serial(count, &elements, &world, &state);
    let pool = rayon::ThreadPoolBuilder::new().num_threads(2).build().unwrap();
    let mut parallel = MulticomponentBuffers::new(count).unwrap();
    parallel.update_world_parallel(count, &elements, &world, &pool, &state);
    let mut initial = MulticomponentBuffers::new(count).unwrap();
    initial.update_from_simulation(&elements, &world);
    assert_eq!(serial.cells, parallel.cells);
    assert_eq!(serial.cells, initial.cells);
    assert_eq!(serial.species_len, parallel.species_len);
    assert_eq!(serial.species_len, initial.species_len);
    assert_eq!(serial.species[..serial.species_len], parallel.species[..parallel.species_len]);
    assert_eq!(serial.species[..serial.species_len], initial.species[..initial.species_len]);

    for cell in 0..count {
        let header = serial.cells[cell];
        let records = &serial.species[header.species_offset as usize
            ..header.species_offset as usize + header.species_count as usize];
        let expected = match cell % 4 { 2 => 0.75_f32, 3 => 1.0, _ => 0.0 };
        assert_eq!(header.liquid_volume_fraction.to_bits(), expected.to_bits());
        let total_volume = records.iter().fold(0.0_f32, |total, record| total + record.volume_fraction);
        assert_eq!(total_volume.to_bits(), expected.to_bits());
        if cell % 4 >= 2 {
            assert_eq!(records.len(), 3, "primary, second and extra liquid all count");
            let fractions = if cell % 4 == 2 { [0.25_f32, 0.25, 0.25] } else { [0.5, 0.25, 0.25] };
            for (record, fraction) in records.iter().zip(fractions) {
                assert_eq!(record.volume_fraction.to_bits(), fraction.to_bits());
                assert_eq!(record.partial_pressure_kpa, 0.0, "wire pressure remains a placeholder");
            }
            assert_eq!(header.total_mass, if cell % 4 == 2 { 875.0 } else { 4500.0 });
        }
        assert_eq!(header.gas_pressure_kpa, 0.0);
        assert_eq!(header.liquid_pressure_kpa, 0.0);
    }
    assert_eq!(crate::liquid_fill::raw_fill(2000.0, 1000.0), 2.0,
        "raw transport fill retains compression above one");
}

#[test]
fn legacy_grid_liquid_fill_matches_world_and_replaces_previous_values() {
    let elements = liquid_elements();
    let mut grid = Grid::new(2, 1, 0, 0).unwrap();
    grid.element.copy_from_slice(&[4, 3]);
    grid.mass.copy_from_slice(&[2000.0, 1.0]);
    grid.temperature.fill(300.0);
    let mut world = PhysicsWorld::from_grid(&grid, &elements).unwrap();
    let internal = world.internal_index(0);
    world.mixture_element[internal] = 5;
    world.mixture_mass[internal] = 500.0;
    let mut legacy = MulticomponentBuffers::new(2).unwrap();
    legacy.update_from_grid_with_mixture(&grid, &elements, Some(&[5, 0]), Some(&[500.0, 0.0]));
    let mut authoritative = MulticomponentBuffers::new(2).unwrap();
    authoritative.update_from_simulation(&elements, &world);
    assert_eq!(legacy.cells, authoritative.cells);
    assert_eq!(legacy.species[..3], authoritative.species[..3]);
    assert_eq!(legacy.cells[0].liquid_volume_fraction, 1.0);
    assert_eq!(legacy.species[0].volume_fraction, (2.0_f64 / 3.0) as f32);
    assert_eq!(legacy.species[1].volume_fraction, (1.0_f64 / 3.0) as f32);
    grid.element[0] = 0;
    grid.mass[0] = 0.0;
    legacy.update_from_grid(&grid, &elements);
    assert_eq!(legacy.cells[0].liquid_volume_fraction, 0.0);
    assert_eq!(legacy.cells[0].species_count, 0);
    assert_eq!(legacy.cells[1].liquid_volume_fraction, 0.0);
    assert_eq!(legacy.species[0].volume_fraction, 0.0, "gas has no defined occupied-volume output");
}

#[test]
fn liquid_fill_stays_finite_for_finite_inventory_at_minimum_capacity() {
    let mut elements = liquid_elements();
    elements.elements[4].record.max_mass = 0.0;
    let mut records = [OniSimMulticomponentSpeciesRecord {
        element_id: 4, phase: 2, mass: f32::MAX, temperature: 300.0, ..Default::default()
    }];
    assert_eq!(publish_liquid_fill(&mut records, &elements), 1.0);
    assert_eq!(records[0].volume_fraction, 1.0);
    assert_eq!(records[0].mass.to_bits(), f32::MAX.to_bits());
}
