//! Manual ABBA comparison with the pre-refactor production scanner.
use super::*;

// Frozen candidate discovery from 47785a0b. Kept for paired measurements;
// normal execution uses LiquidCandidates.
fn baseline(world: &PhysicsWorld, region: Region) -> (Vec<usize>, Option<[i32; 4]>) {
    let width = (region.max_x - region.min_x).max(0) as usize;
    let height = (region.max_y - region.min_y).max(0) as usize;
    let empty = (i32::MAX, i32::MAX, i32::MIN, i32::MIN);
    let merge = |a: (i32, i32, i32, i32), b: (i32, i32, i32, i32)| {
        (a.0.min(b.0), a.1.min(b.1), a.2.max(b.2), a.3.max(b.3))
    };
    let has_extras = !world.extra_mixtures.is_empty();
    let row_scan = |y: i32| {
        let mut bounds = empty;
        let mut cells = Vec::new();
        let row_base = y as usize * world.stride;
        for x in region.min_x..region.max_x {
            let cell = row_base + x as usize;
            let liquid = (world.write.mass[cell] > 0.0
                && world.element_phase[world.write.element[cell] as usize] == 2)
                || (world.mixture_mass[cell] > 0.0
                    && world.element_phase[world.mixture_element[cell] as usize] == 2)
                || (has_extras
                    && world.extra_mixtures.get(&cell).is_some_and(|species| {
                        species
                            .iter()
                            .any(|&(id, mass)| mass > 0.0 && world.element_phase[id as usize] == 2)
                    }));
            if liquid {
                bounds = merge(bounds, (x, y, x + 1, y + 1));
                cells.push(cell);
            }
        }
        (bounds, cells)
    };
    let (bounds, liquid_cells) =
        if let Some(pool) = crate::parallel::pool(width.saturating_mul(height)) {
            let rows: Vec<_> = pool.install(|| {
                (region.min_y..region.max_y)
                    .into_par_iter()
                    .with_min_len(8)
                    .map(row_scan)
                    .collect()
            });
            let total_cells: usize = rows.iter().map(|(_, c)| c.len()).sum();
            let mut bounds = empty;
            let mut liquid_cells = Vec::with_capacity(total_cells);
            for (row_bounds, row_cells) in rows {
                if !row_cells.is_empty() {
                    bounds = merge(bounds, row_bounds);
                    liquid_cells.extend(row_cells);
                }
            }
            (bounds, liquid_cells)
        } else {
            let mut bounds = empty;
            let mut liquid_cells = Vec::new();
            for y in region.min_y..region.max_y {
                let (row_bounds, row_cells) = row_scan(y);
                if !row_cells.is_empty() {
                    bounds = merge(bounds, row_bounds);
                    liquid_cells.extend(row_cells);
                }
            }
            (bounds, liquid_cells)
        };
    (
        liquid_cells,
        if bounds.0 == i32::MAX {
            None
        } else {
            Some([bounds.0, bounds.1, bounds.2, bounds.3])
        },
    )
}

#[test]
#[ignore = "manual production liquid-candidate ABBA benchmark; does not measure game FPS"]
fn benchmark_liquid_candidate_discovery() {
    use std::hint::black_box;
    use std::time::Instant;
    let iterations = std::env::var("ONISIM_CANDIDATE_ITERATIONS")
        .ok()
        .map(|value| value.parse::<usize>().unwrap())
        .unwrap_or(100);
    assert!(iterations > 0);
    for (width, height) in [(32, 32), (64, 64), (256, 256), (512, 512)] {
        for scene in ["empty", "sparse", "dense", "extras"] {
            let (mut world, region) = fixture(width, height);
            for y in region.min_y..region.max_y {
                for x in region.min_x..region.max_x {
                    let cell = y as usize * world.stride + x as usize;
                    if scene == "dense" || (scene == "sparse" && (x * 19 + y * 31) % 31 == 0) {
                        world.write.element[cell] = 4;
                    } else if scene == "extras" && (x * 19 + y * 31) % 31 == 0 {
                        world.extra_mixtures.insert(cell, vec![(3, 0.5), (4, 0.1)]);
                    }
                }
            }
            let mut scanner = LiquidCandidates::default();
            let expected = baseline(&world, region);
            for _ in 0..12 {
                black_box(baseline(black_box(&world), black_box(region)));
                scanner.scan(black_box(&world), black_box(region));
            }
            assert_eq!(scanner.cells(), expected.0);
            assert_eq!(scanner.bounds(), expected.1);
            let mut samples = [Vec::new(), Vec::new()];
            for round in 0..6 {
                for variant in if round % 2 == 0 {
                    [0, 1, 1, 0]
                } else {
                    [1, 0, 0, 1]
                } {
                    let start = Instant::now();
                    for _ in 0..iterations {
                        if variant == 0 {
                            black_box(baseline(black_box(&world), black_box(region)));
                        } else {
                            scanner.scan(black_box(&world), black_box(region));
                            black_box(scanner.cells());
                            black_box(scanner.bounds());
                        }
                    }
                    samples[variant].push(start.elapsed().as_secs_f64() * 1e6 / iterations as f64);
                }
            }
            for (variant, values) in samples.iter().enumerate() {
                let raw = values
                    .iter()
                    .map(|value| format!("{value:.3}"))
                    .collect::<Vec<_>>()
                    .join(";");
                println!(
                    "LIQUID_CANDIDATE_SAMPLE,{width},{height},{scene},{variant},{iterations},{raw}"
                );
            }
        }
    }
}
