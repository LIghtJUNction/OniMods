//! Run with the native host suite. Explicit pools avoid global worker-env races.
use super::*;

fn world() -> PhysicsWorld {
    let (mut state, _) = crate::native_chain_tests::fixture::create(96, 64, "pure");
    state.world.take().unwrap()
}

fn pool() -> rayon::ThreadPool {
    rayon::ThreadPoolBuilder::new()
        .num_threads(2)
        .build()
        .unwrap()
}

// Independent row-major traversal is the reference for block packing and offsets.
fn serial_reference(
    world: &PhysicsWorld,
    key: (usize, usize, usize, usize),
) -> (Vec<Cell>, Vec<(u16, u32)>) {
    let (x, y, width, height) = key;
    let mut cells = Vec::new();
    let mut extras = Vec::new();
    for row in y..y + height {
        for column in x..x + width {
            let cell = row * world.stride + column;
            let extra = world.extra_mixtures.get(&cell).map(Vec::as_slice);
            let input = Cell::read(world, cell, extra);
            cells.push(input);
            if input.properties == 0
                && let Some(extra) = extra
            {
                extras.extend(extra.iter().map(|&(id, mass)| (id, mass.to_bits())));
            }
        }
    }
    (cells, extras)
}

fn expect_match(
    snapshot: &GasRegionSnapshot,
    world: &PhysicsWorld,
    key: (usize, usize, usize, usize),
    pool: &rayon::ThreadPool,
    expected: bool,
) {
    assert_eq!(
        snapshot.matches_with_pool(world, key, None),
        expected,
        "serial match"
    );
    assert_eq!(
        snapshot.matches_with_pool(world, key, Some(pool)),
        expected,
        "parallel match"
    );
}

#[test]
fn extras_keep_row_order_full_bits_and_partial_block_offsets() {
    let mut world = world();
    let pool = pool();
    let key = (3, 4, 33, 25);
    let masses = [0x3e80_0001, 0, 0x8000_0000, 1, 0x7fc0_1234, 0xffc0_5678];
    for row in 0..key.3 {
        for column in 0..key.2 {
            if (row + column) % 3 == 0 {
                let cell = (key.1 + row) * world.stride + key.0 + column;
                let entries = (0..1 + (row + column) % masses.len())
                    .map(|index| (3 + (index % 2) as u16, f32::from_bits(masses[index])))
                    .collect();
                world.extra_mixtures.insert(cell, entries);
            }
        }
    }
    let (cells, extras) = serial_reference(&world, key);
    let mut snapshot = GasRegionSnapshot::default();
    for workers in [Some(&pool), None, Some(&pool)] {
        snapshot.capture_with_pool(&world, key, workers);
        assert!(snapshot.cells == cells, "row-major cell order");
        assert_eq!(snapshot.extra, extras, "all extra IDs and f32 bits");
        assert_eq!(
            snapshot.extra_offsets.len(),
            5,
            "three full blocks and one tail"
        );
        for block in 0..4 {
            let block_key = (key.0, key.1 + block * 8, key.2, (key.3 - block * 8).min(8));
            let (_, expected) = serial_reference(&world, block_key);
            assert_eq!(
                &snapshot.extra[snapshot.extra_offsets[block]..snapshot.extra_offsets[block + 1]],
                expected.as_slice(),
                "block={block}"
            );
        }
        expect_match(&snapshot, &world, key, &pool, true);
    }
}

#[test]
fn extras_anywhere_else_do_not_change_the_region() {
    let mut world = world();
    let pool = pool();
    world.world_offsets = vec![[0, 0, 48, 64], [48, 0, 48, 64]];
    let key = (1, 1, 40, 33);
    let outside = 50 * world.stride + 80;
    world.extra_mixtures.insert(outside, vec![(3, 0.25)]);
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, key, Some(&pool));
    assert!(snapshot.extra.is_empty());
    assert_eq!(snapshot.extra_offsets, vec![0; 6]);
    world
        .extra_mixtures
        .get_mut(&outside)
        .unwrap()
        .push((5, 1000.0));
    expect_match(&snapshot, &world, key, &pool, true);
    let inside = key.1 * world.stride + key.0;
    world.extra_mixtures.insert(inside, vec![(4, 0.25)]);
    expect_match(&snapshot, &world, key, &pool, false);
}

#[test]
fn each_block_detects_extra_bits_order_and_source_field_changes() {
    let mut world = world();
    let pool = pool();
    let key = (2, 3, 40, 25);
    for row in [0, 7, 8, 15, 16, 23, 24] {
        let cell = (key.1 + row) * world.stride + key.0 + 39;
        world
            .extra_mixtures
            .insert(cell, vec![(3, 0.25), (4, -0.0)]);
    }
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, key, Some(&pool));
    for row in [0, 7, 8, 15, 16, 23, 24] {
        let cell = (key.1 + row) * world.stride + key.0 + 39;
        world.extra_mixtures.get_mut(&cell).unwrap()[0].1 = f32::from_bits(0.25f32.to_bits() + 1);
        expect_match(&snapshot, &world, key, &pool, false);
        world.extra_mixtures.get_mut(&cell).unwrap()[0].1 = 0.25;
        world.extra_mixtures.get_mut(&cell).unwrap()[1].1 = 0.0;
        expect_match(&snapshot, &world, key, &pool, false);
        world.extra_mixtures.get_mut(&cell).unwrap()[1].1 = -0.0;
        world.extra_mixtures.get_mut(&cell).unwrap().swap(0, 1);
        expect_match(&snapshot, &world, key, &pool, false);
        world.extra_mixtures.get_mut(&cell).unwrap().swap(0, 1);
        let entries = world.extra_mixtures.remove(&cell).unwrap();
        expect_match(&snapshot, &world, key, &pool, false);
        world.extra_mixtures.insert(cell, entries);
    }
    let cell = (key.1 + 24) * world.stride + key.0;
    for field in 0..6 {
        let primary = world.write.element[cell];
        let secondary = world.mixture_element[cell];
        let mass = world.write.mass[cell];
        let mixture = world.mixture_mass[cell];
        let temperature = world.write.temperature[cell];
        match field {
            0 => world.write.element[cell] = 4,
            1 => world.mixture_element[cell] = 4,
            2 => world.write.mass[cell] = f32::from_bits(mass.to_bits() + 1),
            3 => world.mixture_mass[cell] = 0.125,
            4 => world.write.temperature[cell] = f32::from_bits(temperature.to_bits() + 1),
            _ => world.write.properties[cell] |= 1,
        }
        expect_match(&snapshot, &world, key, &pool, false);
        world.write.element[cell] = primary;
        world.mixture_element[cell] = secondary;
        world.write.mass[cell] = mass;
        world.mixture_mass[cell] = mixture;
        world.write.temperature[cell] = temperature;
        world.write.properties[cell] &= !1;
    }
    expect_match(&snapshot, &world, key, &pool, true);
}

#[test]
fn all_gas_wall_causes_ignore_blocked_state_but_wake_when_opened() {
    let mut world = world();
    let pool = pool();
    let key = (2, 2, 40, 25);
    let walls = [2, 9, 10, 26].map(|row| row * world.stride + 2);
    world.write.properties[walls[0]] |= 1;
    world.write.element[walls[1]] = 6;
    world.mixture_element[walls[2]] = 5;
    world.mixture_mass[walls[2]] = 0.125;
    world.extra_mixtures.insert(walls[3], vec![(5, 0.25)]);
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, key, Some(&pool));
    assert!(snapshot.extra.is_empty(), "walls capture only obstruction");
    for cell in walls {
        world.write.temperature[cell] = f32::NAN;
        world.write.mass[cell] = 1000.0;
    }
    world
        .extra_mixtures
        .get_mut(&walls[3])
        .unwrap()
        .extend(vec![(3, 0.25); u16::MAX as usize + 9]);
    expect_match(&snapshot, &world, key, &pool, true);
    // Switch between distinct wall causes without changing obstruction.
    world.write.properties[walls[0]] &= !1;
    world.write.element[walls[0]] = 6;
    expect_match(&snapshot, &world, key, &pool, true);
    world.write.element[walls[0]] = 3;
    expect_match(&snapshot, &world, key, &pool, false);
}

#[test]
fn oversized_sentinel_never_reuses_and_never_truncates_extras() {
    let mut world = world();
    let pool = pool();
    let key = (1, 1, 40, 25);
    let cell = 25 * world.stride + 40;
    let mut snapshot = GasRegionSnapshot::default();
    for count in [
        u16::MAX as usize - 1,
        u16::MAX as usize,
        u16::MAX as usize + 17,
    ] {
        world
            .extra_mixtures
            .insert(cell, vec![(4, f32::from_bits(0x3e80_0001)); count]);
        snapshot.capture_with_pool(&world, key, Some(&pool));
        assert_eq!(snapshot.extra.len(), count);
        assert!(
            snapshot
                .extra
                .iter()
                .all(|&entry| entry == (4, 0x3e80_0001))
        );
        expect_match(&snapshot, &world, key, &pool, count < u16::MAX as usize);
    }
}

#[test]
fn warm_capture_reuses_buffers_across_serial_parallel_shrink_and_empty() {
    let mut world = world();
    let pool = pool();
    let key = (1, 1, 40, 25);
    for row in 1..=25 {
        world
            .extra_mixtures
            .insert(row * world.stride + 1, vec![(4, 0.25); 3]);
    }
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, key, Some(&pool));
    let cells = (snapshot.cells.as_ptr(), snapshot.cells.capacity());
    let extras = (snapshot.extra.as_ptr(), snapshot.extra.capacity());
    let offsets = (
        snapshot.extra_offsets.as_ptr(),
        snapshot.extra_offsets.capacity(),
    );
    for region in [key, (1, 1, 40, 9), (1, 1, 0, 25), (1, 1, 40, 0), key] {
        for workers in [None, Some(&pool)] {
            snapshot.capture_with_pool(&world, region, workers);
            expect_match(&snapshot, &world, region, &pool, true);
            assert_eq!((snapshot.cells.as_ptr(), snapshot.cells.capacity()), cells);
            assert_eq!((snapshot.extra.as_ptr(), snapshot.extra.capacity()), extras);
            assert_eq!(
                (
                    snapshot.extra_offsets.as_ptr(),
                    snapshot.extra_offsets.capacity()
                ),
                offsets
            );
        }
    }
}

#[test]
fn equal_cell_count_with_different_width_uses_flat_comparison() {
    let mut world = world();
    let pool = pool();
    world.write.mass.fill(1.0);
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, (1, 1, 16, 16), Some(&pool));
    expect_match(&snapshot, &world, (1, 1, 32, 8), &pool, true);
    expect_match(&snapshot, &world, (1, 1, 32, 9), &pool, false);
}

#[test]
fn shared_custom_index_is_sync_and_sparse_iteration_order_is_irrelevant() {
    fn assert_sync<T: Sync>() {}
    assert_sync::<crate::cell_mixtures::CellMixtures>();
    assert_sync::<PhysicsWorld>();

    let mut world = world();
    let pool = pool();
    let key = (1, 1, 40, 25);
    let cells = [
        world.stride + 1,
        9 * world.stride + 20,
        25 * world.stride + 40,
    ];
    for (index, &cell) in cells.iter().enumerate() {
        world
            .extra_mixtures
            .insert(cell, vec![(4, (index + 1) as f32 * 0.125)]);
    }
    let mut snapshot = GasRegionSnapshot::default();
    snapshot.capture_with_pool(&world, key, Some(&pool));
    let expected = snapshot.extra.clone();
    // Removing the first sparse entry moves the last entry into its slot.
    // Reinsert in a different order while preserving exact per-cell values.
    for &cell in &cells {
        let entries = world.extra_mixtures.remove(&cell).unwrap();
        world.extra_mixtures.insert(cell, entries);
    }
    expect_match(&snapshot, &world, key, &pool, true);
    snapshot.capture_with_pool(&world, key, Some(&pool));
    assert_eq!(snapshot.extra, expected);
}
