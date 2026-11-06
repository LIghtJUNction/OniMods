use super::*;
use crate::mass_components::allocation;
use crate::world::Cells;

// The frozen pre-integration implementation calls the same current production
// world, numeric, disease and event helpers as the candidate.
#[path = "mass_components_flood_baseline.rs"]
mod previous;

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

fn elements() -> ElementTable {
    let mut bytes = 8_i32.to_le_bytes().to_vec();
    for index in 0..8_u16 {
        let mut record = [0_u8; 164];
        let hash = match index {
            0 => 0x2d39bf75_u32,
            4 => 0xa9360b34,
            7 => 0x6d95058c,
            _ => u32::from(index),
        };
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&index.to_le_bytes());
        record[10] = (index % 4) as u8;
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 32]);
    ElementTable::parse(&bytes).unwrap()
}

fn diseases() -> DiseaseTable {
    let mut bytes = 2_i32.to_le_bytes().to_vec();
    bytes.extend_from_slice(&0_i32.to_le_bytes());
    for index in 0..2 {
        bytes.extend_from_slice(&0_i32.to_le_bytes());
        let mut header = [0_u8; 76];
        header[4..8].copy_from_slice(&(1.0 + index as f32).to_le_bytes());
        bytes.extend_from_slice(&header);
    }
    DiseaseTable::parse(&bytes, 0).unwrap()
}

fn next(state: &mut u64) -> u64 {
    *state ^= *state << 13;
    *state ^= *state >> 7;
    *state ^= *state << 17;
    *state
}

fn sample(state: &mut u64, exceptional: bool) -> f32 {
    let word = next(state) as u32;
    if exceptional {
        let words = [
            0, 0x80000000, 1, 0x7f800000, 0xff800000, 0x7fc12345, 0x7f812345, word,
        ];
        f32::from_bits(words[(word as usize >> 4) % words.len()])
    } else {
        0.1 + (word % 20000) as f32 * 0.125
    }
}

fn populate(world: &mut PhysicsWorld, mut seed: u64, exceptional: bool) {
    for cell in 0..world.write.element.len() {
        world.write.element[cell] = (next(&mut seed) % 8) as u16;
        world.write.mass[cell] = sample(&mut seed, exceptional);
        world.write.temperature[cell] = sample(&mut seed, exceptional);
        world.write.properties[cell] = next(&mut seed) as u8;
        world.write.insulation[cell] = next(&mut seed) as u8;
        world.write.strength[cell] = next(&mut seed) as u8;
        world.write.disease_idx[cell] = [0, 1, 255][next(&mut seed) as usize % 3];
        world.write.disease_count[cell] = (next(&mut seed) % 10000) as i32;
        world.write.disease_infestation[cell] = next(&mut seed) as u8;
        world.write.disease_growth[cell] = sample(&mut seed, exceptional);
        world.write.radiation[cell] = sample(&mut seed, exceptional);
    }
    world.read.copy_from(&world.write);
    world.dirty.fill(false);
    world.dirty_flags.fill(0);
}

fn words(left: &[f32], right: &[f32]) {
    assert_eq!(left.len(), right.len());
    for (a, b) in left.iter().zip(right) {
        assert_eq!(a.to_bits(), b.to_bits());
    }
}

fn cells(left: &Cells, right: &Cells) {
    assert_eq!(left.element, right.element);
    assert_eq!(left.properties, right.properties);
    assert_eq!(left.insulation, right.insulation);
    assert_eq!(left.strength, right.strength);
    assert_eq!(left.disease_idx, right.disease_idx);
    assert_eq!(left.disease_count, right.disease_count);
    assert_eq!(left.disease_infestation, right.disease_infestation);
    words(&left.mass, &right.mass);
    words(&left.temperature, &right.temperature);
    words(&left.disease_growth, &right.disease_growth);
    words(&left.radiation, &right.radiation);
}

fn events(left: &EventQueues, right: &EventQueues) {
    let ordered = |queue: &EventQueues| {
        queue
            .substance_change_info
            .iter()
            .map(|event| (event.cell_idx, event.old_elem_idx, event.new_elem_idx))
            .collect::<Vec<_>>()
    };
    assert_eq!(ordered(left), ordered(right));
}

fn info(value: ConsumedMassInfo) -> (i32, u16, u8, u8, u32, u32, i32) {
    (
        value.sim_handle,
        value.removed_elem_idx,
        value.disease_idx,
        value.pad0,
        value.mass.to_bits(),
        value.temperature.to_bits(),
        value.disease_count,
    )
}

#[test]
fn fifo_matches_previous_order_for_every_three_by_three_obstacle_layout() {
    let table = elements();
    let mut world = ok(PhysicsWorld::new(3, 3, 1, &table));
    world.write.element.fill(1);
    let mut old = previous::FloodScratch::default();
    let mut new = FloodScratch::default();
    let mut comparisons = 0;
    for mask in 0..512 {
        for y in 1..=3 {
            for x in 1..=3 {
                world.write.element[y * world.stride + x] =
                    if mask & (1 << ((y - 1) * 3 + x - 1)) != 0 {
                        3
                    } else {
                        1
                    };
            }
        }
        if mask == 256 {
            old.force_wrap();
            new.generation = u32::MAX;
        }
        for start in 0..25 {
            for depth in 0..7 {
                ok(old.reachable(&world, &table, start, depth));
                ok(new.reachable(&world, &table, start, depth));
                assert_eq!(
                    old.cells, new.cells,
                    "mask={mask} start={start} depth={depth}"
                );
                comparisons += 1;
            }
        }
    }
    assert_eq!(comparisons, 89600);
}

#[test]
fn fifo_mass_removal_preserves_current_helpers_and_event_order() {
    let table = elements();
    let disease_table = diseases();
    let mut old = previous::FloodScratch::default();
    let mut new = FloodScratch::default();
    let mut rng = 0x9a046e3d71_u64;
    for case in 0..1000 {
        let width = 1 + next(&mut rng) as usize % 24;
        let height = 1 + next(&mut rng) as usize % 24;
        let mut before = ok(PhysicsWorld::new(width, height, 1, &table));
        let mut after = ok(PhysicsWorld::new(width, height, 1, &table));
        let seed = next(&mut rng);
        populate(&mut before, seed, case % 7 == 0);
        populate(&mut after, seed, case % 7 == 0);
        let mut old_events = EventQueues::default();
        let mut new_events = EventQueues::default();
        for step in 0..5 {
            let start = next(&mut rng) as usize % before.write.mass.len();
            let depth = [0, 1, 2, 4, 16, 64, 255][next(&mut rng) as usize % 7];
            let target = (next(&mut rng) % 8) as u16;
            let request = if case % 7 == 0 {
                sample(&mut rng, true)
            } else {
                [0.0, 0.125, 1.0, 50.0, 1_000_000.0][next(&mut rng) as usize % 5]
            };
            let mut old_info = ConsumedMassInfo {
                disease_idx: 255,
                ..Default::default()
            };
            let mut new_info = old_info;
            if step == 3 && case % 5 == 0 {
                old.force_wrap();
                new.generation = u32::MAX;
            }
            ok(old.remove_mass(
                &mut before,
                &table,
                &disease_table,
                &mut old_events,
                start,
                depth,
                request,
                target,
                &mut old_info,
            ));
            ok(new.remove_mass(
                &mut after,
                &table,
                &disease_table,
                &mut new_events,
                start,
                depth,
                request,
                target,
                &mut new_info,
            ));
            assert_eq!(old.cells, new.cells, "case={case} step={step}");
            assert_eq!(info(old_info), info(new_info), "case={case} step={step}");
            cells(&before.read, &after.read);
            cells(&before.write, &after.write);
            assert_eq!(before.dirty, after.dirty);
            assert_eq!(before.dirty_flags, after.dirty_flags);
            events(&old_events, &new_events);
        }
    }
}

#[test]
fn fifo_keeps_boundaries_early_exit_and_retained_queue_storage() {
    let table = elements();
    let mut old = previous::FloodScratch::default();
    let mut new = FloodScratch::default();
    for (width, height) in [(1, 1), (31, 2), (2, 31), (256, 256)] {
        let mut world = ok(PhysicsWorld::new(width, height, 1, &table));
        world.write.element.fill(1);
        for start in [
            0,
            1,
            world.stride - 1,
            world.stride + 1,
            world.write.mass.len() - 1,
            usize::MAX,
        ] {
            for depth in [0, 1, 2, 16, 64, 255] {
                ok(old.reachable(&world, &table, start, depth));
                ok(new.reachable(&world, &table, start, depth));
                assert_eq!(old.cells, new.cells);
            }
        }
    }
    let mut world = ok(PhysicsWorld::new(256, 256, 1, &table));
    world.write.element.fill(1);
    world.write.mass.fill(1.0);
    world.write.temperature.fill(300.0);
    old = previous::FloodScratch::default();
    new = FloodScratch::default();
    let start = 128 * world.stride + 128;
    ok(old.reachable(&world, &table, start, 64));
    ok(new.reachable(&world, &table, start, 64));
    assert_eq!(old.cells, new.cells);
    assert_eq!(new.cells.len(), 8065);
    assert!(new.queue.capacity() < old.queue_capacity());
    let capacities = (
        new.marks.capacity(),
        new.cells.capacity(),
        new.queue.capacity(),
    );
    for _ in 0..4 {
        ok(new.reachable(&world, &table, start, 64));
        assert_eq!(
            capacities,
            (
                new.marks.capacity(),
                new.cells.capacity(),
                new.queue.capacity()
            )
        );
    }
    let mut result = ConsumedMassInfo {
        disease_idx: 255,
        ..Default::default()
    };
    ok(new.remove_mass(
        &mut world,
        &table,
        &diseases(),
        &mut EventQueues::default(),
        start,
        64,
        0.125,
        1,
        &mut result,
    ));
    assert_eq!(new.cells, [start]);
    assert_eq!(result.mass.to_bits(), 0.125_f32.to_bits());
    assert!(new.queue.is_empty());
}
