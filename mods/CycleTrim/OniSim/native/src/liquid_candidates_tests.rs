//! Differential coverage of the production liquid candidate scanner.
use super::*;
use crate::tables::ElementTable;

fn fixture(width: usize, height: usize) -> (PhysicsWorld, Region) {
    let descriptions = [
        (0x2d39bf75_u32, 0_u8),
        (0x6d95058c, 3),
        (0xa9360b34, 0),
        (0x10000003, 1),
        (0x10000004, 2),
    ];
    let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
    for (index, &(hash, phase)) in descriptions.iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + descriptions.len() * 4, 0);
    let elements = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(width + 3, height + 4, 0, &elements).unwrap();
    world.write.element.fill(3);
    world.write.mass.fill(1.0);
    let region = Region {
        min_x: 2,
        min_y: 3,
        max_x: 2 + width as i32,
        max_y: 3 + height as i32,
        ..Default::default()
    };
    (world, region)
}

fn reference(world: &PhysicsWorld, region: Region) -> (Vec<usize>, Option<[i32; 4]>) {
    let mut cells = Vec::new();
    let mut bounds: Option<[i32; 4]> = None;
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            let primary = (world.write.element[cell], world.write.mass[cell]);
            let secondary = (world.mixture_element[cell], world.mixture_mass[cell]);
            let is_liquid = |&(element, mass): &(u16, f32)| {
                mass > 0.0 && world.element_phase[element as usize] == 2
            };
            if [primary, secondary].iter().any(is_liquid)
                || world
                    .extra_mixtures
                    .get(&cell)
                    .is_some_and(|extra| extra.iter().any(is_liquid))
            {
                cells.push(cell);
                bounds = Some(match bounds {
                    Some([left, bottom, right, top]) => [
                        i32::min(left, x),
                        i32::min(bottom, y),
                        i32::max(right, x + 1),
                        i32::max(top, y + 1),
                    ],
                    None => [x, y, x + 1, y + 1],
                });
            }
        }
    }
    (cells, bounds)
}

#[test]
fn candidates_match_all_component_slots_in_stable_cell_order() {
    let pool = rayon::ThreadPoolBuilder::new()
        .num_threads(3)
        .build()
        .unwrap();
    for (width, height) in [(1, 1), (67, 131), (257, 97), (8193, 1), (513, 257)] {
        let (mut world, region) = fixture(width, height);
        for y in region.min_y..region.max_y {
            for x in region.min_x..region.max_x {
                let cell = y as usize * world.stride + x as usize;
                match (x * 19 + y * 31) % 7 {
                    0 => {
                        world.write.element[cell] = 4;
                    }
                    1 => {
                        world.mixture_element[cell] = 4;
                        world.mixture_mass[cell] = 0.01;
                    }
                    2 => {
                        world.extra_mixtures.insert(cell, vec![(3, 0.2), (4, 0.01)]);
                    }
                    3 => {
                        world.write.element[cell] = 4;
                        world.write.mass[cell] = f32::NAN;
                    }
                    4 => {
                        world.mixture_element[cell] = 4;
                        world.mixture_mass[cell] = -0.0;
                    }
                    5 => {
                        world.extra_mixtures.insert(cell, vec![(4, -1.0), (3, 0.5)]);
                    }
                    _ => {}
                }
            }
        }
        // Final partial blocks and exclusive rectangle edges must be retained.
        let last = (region.max_y as usize - 1) * world.stride + region.max_x as usize - 1;
        world.write.element[last] = 4;
        world.write.mass[last] = 1.0;
        world.write.element[last + 1] = 4;
        let expected = reference(&world, region);
        let mut scanner = LiquidCandidates::default();
        for execution in [None, Some(&pool)] {
            for _ in 0..3 {
                scanner.scan_with_pool(&world, region, execution);
                assert_eq!(scanner.cells(), expected.0, "{width}x{height}");
                assert_eq!(scanner.bounds(), expected.1, "{width}x{height}");
            }
        }
    }
}

#[test]
fn reused_candidates_clear_old_cells_and_bounds_between_regions() {
    let pool = rayon::ThreadPoolBuilder::new()
        .num_threads(2)
        .build()
        .unwrap();
    let (mut world, region) = fixture(257, 97);
    world.write.element.fill(4);
    let mut scanner = LiquidCandidates::default();
    scanner.scan_with_pool(&world, region, Some(&pool));
    assert_eq!(scanner.cells().len(), 257 * 97);
    let small = Region {
        max_x: region.min_x + 3,
        max_y: region.min_y + 2,
        ..region
    };
    scanner.scan_with_pool(&world, small, Some(&pool));
    assert_eq!(scanner.cells().len(), 6);
    assert_eq!(scanner.bounds(), Some([2, 3, 5, 5]));
    world.write.mass.fill(0.0);
    for next in [
        region,
        small,
        Region {
            max_x: region.min_x,
            ..region
        },
    ] {
        scanner.scan_with_pool(&world, next, Some(&pool));
        assert!(scanner.cells().is_empty());
        assert_eq!(scanner.bounds(), None);
    }
    let last = (region.max_y as usize - 1) * world.stride + region.max_x as usize - 1;
    world.mixture_mass[last] = 3.0;
    world.mixture_element[last] = 4;
    scanner.scan_with_pool(&world, region, Some(&pool));
    assert_eq!(
        (scanner.cells().to_vec(), scanner.bounds()),
        reference(&world, region)
    );
}

#[test]
fn warmed_scan_reuses_candidate_capacity() {
    let pool = rayon::ThreadPoolBuilder::new()
        .num_threads(3)
        .build()
        .unwrap();
    let (mut world, region) = fixture(257, 97);
    world.write.element.fill(4);
    let mut scanner = LiquidCandidates::default();
    for execution in [None, Some(&pool)] {
        scanner.scan_with_pool(&world, region, execution);
        let merged = (
            scanner.merged.cells.as_ptr(),
            scanner.merged.cells.capacity(),
        );
        let blocks: Vec<_> = scanner
            .blocks
            .iter()
            .map(|block| (block.cells.as_ptr(), block.cells.capacity()))
            .collect();
        for _ in 0..8 {
            scanner.scan_with_pool(&world, region, execution);
            assert_eq!(
                (
                    scanner.merged.cells.as_ptr(),
                    scanner.merged.cells.capacity()
                ),
                merged
            );
            assert_eq!(
                scanner
                    .blocks
                    .iter()
                    .map(|block| (block.cells.as_ptr(), block.cells.capacity()))
                    .collect::<Vec<_>>(),
                blocks
            );
        }
    }
}

#[path = "liquid_candidates_benchmark.rs"]
mod benchmark;
