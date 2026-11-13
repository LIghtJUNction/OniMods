//! Compare both discovery paths with the original complete serial traversal.
use super::*;

type Rectangle = (usize, usize, usize, usize);

fn fixture(width: usize, height: usize) -> (PhysicsWorld, ElementTable, Rectangle) {
    let mut descriptions = vec![
        (0x2d39bf75_u32, 0_u8),
        (0x6d95058c, 3),
        (0xa9360b34, 3),
        (0x12345678, 1),
    ];
    descriptions.extend((0..20).map(|index| (100 + index, 0x12)));
    let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
    for (index, &(hash, state)) in descriptions.iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = state;
        record[12..16].copy_from_slice(&1.0_f32.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + descriptions.len() * 4, 0);
    let elements = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(width + 4, height + 5, 7, &elements).unwrap();
    let key = (2, 3, width, height);
    for row in key.1..key.1 + height {
        for column in key.0..key.0 + width {
            let cell = row * world.stride + column;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1.0;
        }
    }
    // One large world with no active gas cells is still scanned for liquid IDs.
    world.inactive_mask = Some(vec![1; world.write.element.len()]);
    world.active_mask.fill(0);
    (world, elements, key)
}

fn cell(world: &PhysicsWorld, key: Rectangle, offset: usize) -> usize {
    (key.1 + offset / key.2) * world.stride + key.0 + offset % key.2
}

fn put(
    world: &mut PhysicsWorld,
    key: Rectangle,
    offset: usize,
    primary: (u16, f32),
    secondary: (u16, f32),
) {
    let index = cell(world, key, offset);
    world.write.element[index] = primary.0;
    world.write.mass[index] = primary.1;
    world.mixture_element[index] = secondary.0;
    world.mixture_mass[index] = secondary.1;
}

fn reference(world: &PhysicsWorld, elements: &ElementTable, key: Rectangle) -> Option<Vec<u16>> {
    let (min_x, min_y, width, height) = key;
    let mut ids = Vec::new();
    for y in min_y..min_y + height {
        for x in min_x..min_x + width {
            let index = y * world.stride + x;
            for (id, mass) in [
                (world.write.element[index], world.write.mass[index]),
                (world.mixture_element[index], world.mixture_mass[index]),
            ] {
                if mass > 0.0
                    && mass.is_finite()
                    && elements
                        .elements
                        .get(id as usize)
                        .is_some_and(|e| e.record.state & 3 == 2)
                    && !ids.contains(&id)
                {
                    ids.push(id);
                    if ids.len() > MAX_REGION_LIQUIDS {
                        return None;
                    }
                }
            }
        }
    }
    if ids.len() < 2 { None } else { Some(ids) }
}

fn expect(world: &PhysicsWorld, elements: &ElementTable, key: Rectangle, expected: Option<&[u16]>) {
    let expected = expected.map(<[u16]>::to_vec);
    assert_eq!(
        reference(world, elements, key),
        expected,
        "original traversal"
    );
    assert_eq!(
        discover_with_pool(world, elements, key, None),
        expected,
        "no pool"
    );
    for workers in [1, 2, 3] {
        let pool = rayon::ThreadPoolBuilder::new()
            .num_threads(workers)
            .build()
            .unwrap();
        for _ in 0..3 {
            assert_eq!(
                discover_with_pool(world, elements, key, Some(&pool)),
                expected,
                "workers={workers}; deterministic indexed merge"
            );
        }
    }
}

#[test]
fn large_sleeping_gas_world_with_zero_or_one_liquid_skips_gpu_channels() {
    let (mut world, elements, key) = fixture(257, 97);
    expect(&world, &elements, key, None);
    let count = key.2 * key.3;
    for offset in [17, 2047, 2048, 12000, count - 1] {
        put(&mut world, key, offset, (3, 1.0), (4, 0.01));
    }
    expect(&world, &elements, key, None);
    for offset in [18, 2049, count - 2] {
        put(&mut world, key, offset, (4, 1.0), (4, 0.01));
    }
    expect(&world, &elements, key, None);
}

#[test]
fn scattered_liquids_keep_first_cell_and_primary_before_secondary_order() {
    let (mut world, elements, key) = fixture(257, 97);
    let count = key.2 * key.3;
    let first_block_end = count.div_ceil(count.div_ceil(crate::parallel::CHUNK_CELLS));
    put(&mut world, key, 0, (9, 1.0), (4, 2.0));
    put(&mut world, key, key.2 - 1, (9, 1.0), (8, 2.0));
    put(&mut world, key, key.2, (8, 1.0), (6, 2.0));
    put(&mut world, key, first_block_end - 1, (5, 1.0), (7, 2.0));
    put(&mut world, key, first_block_end, (4, 1.0), (10, 2.0));
    put(&mut world, key, count - 1, (11, 1.0), (9, 2.0));
    expect(&world, &elements, key, Some(&[9, 4, 8, 6, 5, 7, 10, 11]));
}

#[test]
fn sixteenth_channel_is_kept_and_late_seventeenth_rejects_the_whole_region() {
    let (mut world, elements, key) = fixture(257, 97);
    let count = key.2 * key.3;
    let ids = [19, 4, 17, 6, 15, 8, 13, 10, 11, 12, 9, 14, 7, 16, 5, 18];
    for (slot, &id) in ids.iter().enumerate() {
        let offset = slot * (count / ids.len());
        put(&mut world, key, offset, (id, 1.0), (id, 0.5));
    }
    expect(&world, &elements, key, Some(&ids));
    for mass in [0.0, -0.0, f32::NAN, f32::INFINITY] {
        put(&mut world, key, count - 1, (3, 1.0), (20, mass));
        expect(&world, &elements, key, Some(&ids));
    }
    put(&mut world, key, count - 1, (3, 1.0), (20, 0.01));
    expect(&world, &elements, key, None);
}

#[test]
fn a_single_block_overflow_also_rejects_the_region() {
    let (mut world, elements, key) = fixture(257, 97);
    for offset in 0..17 {
        put(&mut world, key, offset, (4 + offset as u16, 1.0), (0, 0.0));
    }
    expect(&world, &elements, key, None);
}

#[test]
fn invalid_mass_and_invalid_element_ids_never_create_channels() {
    let (mut world, elements, key) = fixture(257, 97);
    let masses = [f32::NAN, f32::INFINITY, f32::NEG_INFINITY, -1.0, -0.0, 0.0];
    for (offset, mass) in masses.into_iter().enumerate() {
        put(
            &mut world,
            key,
            offset,
            (4 + offset as u16, mass),
            (14 + offset as u16, mass),
        );
    }
    put(&mut world, key, 7, (u16::MAX, 1.0), (3, 2.0));
    expect(&world, &elements, key, None);
    put(&mut world, key, 8, (10, f32::from_bits(1)), (11, 1e-20));
    // Discovery historically ignores blocked flags and temperature. Preserve that scope.
    let index = cell(&world, key, 8);
    world.write.properties[index] = 1;
    world.write.temperature[index] = f32::NAN;
    expect(&world, &elements, key, Some(&[10, 11]));
}

#[test]
fn finite_secondary_survives_nan_primary_and_duplicate_slot_ids_are_unique() {
    let (mut world, elements, key) = fixture(257, 97);
    put(&mut world, key, 100, (4, f32::NAN), (9, 1.0));
    put(&mut world, key, 3000, (9, 1.0), (9, 0.5));
    put(&mut world, key, 17000, (5, 1.0), (4, 0.0));
    expect(&world, &elements, key, Some(&[9, 5]));
}

#[test]
fn serial_small_region_preserves_slot_order_without_pool() {
    let (mut world, elements, key) = fixture(3, 2);
    put(&mut world, key, 0, (8, 1.0), (4, 2.0));
    put(&mut world, key, 5, (9, 1.0), (8, 2.0));
    expect(&world, &elements, key, Some(&[8, 4, 9]));
    assert_eq!(discover(&world, &elements, key), Some(vec![8, 4, 9]));
}

#[test]
fn supplied_element_records_and_region_bounds_match_the_old_contract() {
    let (mut world, mut elements, key) = fixture(257, 97);
    put(&mut world, key, 1, (4, 1.0), (5, 1.0));
    // The helper's predicate stays tied to its supplied table, even in direct host calls.
    elements.elements[4].record.state = 1;
    elements.elements[3].record.state = 2;
    // These are outside the rectangle, despite sharing its rows/padded world.
    let outside = key.1 * world.stride + key.0 - 1;
    world.write.element[outside] = 12;
    world.write.mass[outside] = 1.0;
    let below = (key.1 + key.3) * world.stride + key.0;
    world.mixture_element[below] = 13;
    world.mixture_mass[below] = 1.0;
    expect(&world, &elements, key, Some(&[3, 5]));
}

#[test]
fn capped_summary_count_covers_the_entire_large_region_and_tail() {
    let (mut world, elements, key) = fixture(521, 267);
    assert!(key.2 * key.3 > MAX_BLOCKS * crate::parallel::CHUNK_CELLS);
    assert!(std::mem::size_of::<Summary>() * MAX_BLOCKS <= 4096);
    put(&mut world, key, 0, (8, 1.0), (0, 0.0));
    put(&mut world, key, key.2 * key.3 - 1, (4, 1.0), (9, 1.0));
    expect(&world, &elements, key, Some(&[8, 4, 9]));
}

#[test]
fn varied_sparse_inputs_match_serial_reference_for_both_pool_paths() {
    let (mut world, elements, key) = fixture(129, 67);
    let pool = rayon::ThreadPoolBuilder::new()
        .num_threads(3)
        .build()
        .unwrap();
    let masses = [
        0.0,
        -0.0,
        f32::NAN,
        f32::INFINITY,
        -1.0,
        0.01,
        1.0,
        f32::from_bits(1),
    ];
    let mut random = 17_u32;
    for channels in [1, 2, 5, 16, 17] {
        for offset in 0..key.2 * key.3 {
            random = random.wrapping_mul(1664525).wrapping_add(1013904223);
            let primary = if random % 13 == 0 {
                4 + (random % channels) as u16
            } else {
                3
            };
            let secondary = if random % 7 == 0 {
                4 + ((random >> 8) % channels) as u16
            } else {
                0
            };
            let first_mass = masses[(random as usize >> 16) % masses.len()];
            let second_mass = masses[(random as usize >> 24) % masses.len()];
            put(
                &mut world,
                key,
                offset,
                (primary, first_mass),
                (secondary, second_mass),
            );
        }
        let expected = reference(&world, &elements, key);
        assert_eq!(discover_with_pool(&world, &elements, key, None), expected);
        assert_eq!(
            discover_with_pool(&world, &elements, key, Some(&pool)),
            expected
        );
    }
}

#[test]
fn bounds_cover_positive_primary_and_secondary_liquids_in_both_scan_modes() {
    assert!(std::mem::size_of::<Summary>() * MAX_BLOCKS <= 4096);
    let (mut world, elements, region) = fixture(65, 37);
    let first = 7 * world.stride + 5;
    world.write.element[first] = 4;
    let second = 35 * world.stride + 60;
    world.mixture_element[second] = 5;
    world.mixture_mass[second] = 0.5;
    let invalid = 3 * world.stride + 2;
    world.write.element[invalid] = 6;
    world.write.mass[invalid] = f32::NAN;
    let pool = rayon::ThreadPoolBuilder::new().num_threads(4).build().unwrap();
    for pool in [None, Some(&pool)] {
        let result = discover_bounded_with_pool(&world, &elements, region, pool).unwrap();
        assert_eq!(result.ids, vec![4, 5]);
        assert_eq!(result.bounds, [5, 7, 61, 36]);
    }
}
