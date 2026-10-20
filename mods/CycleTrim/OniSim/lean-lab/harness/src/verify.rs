use std::fs;
use std::time::Instant;

use crate::shared::{disease_table, random_world, success, Emitter, Rng};
use crate::tables::DiseaseTable;
use crate::world::{Cells, PhysicsWorld, Region};
use crate::{baseline, candidate};

#[derive(Default)]
struct Counts {
    cases: u64,
    cells: u64,
    steps: u64,
}

fn search(world: &PhysicsWorld, x: i32, y: i32, depth: u8, counts: &mut Counts) {
    let mut old = success(baseline::reachable_cells(world, x, y, depth));
    let mut new: Vec<_> = candidate::reachable_cells(world, x, y, depth).collect();
    old.sort_unstable();
    new.sort_unstable();
    assert!(old.windows(2).all(|pair| pair[0] != pair[1]), "baseline duplicate");
    assert!(new.windows(2).all(|pair| pair[0] != pair[1]), "candidate duplicate");
    if old != new {
        let difference = old.iter().zip(&new).position(|(a, b)| a != b);
        panic!(
            "search mismatch: {}x{} start=({x},{y}) depth={depth}, old={} new={}, first={difference:?}",
            world.stride, world.padded_height, old.len(), new.len()
        );
    }
    let cx = x as u16 as usize;
    let cy = y as u16 as usize;
    if depth == 0 || cx == 0 || cy == 0 || cx >= world.stride || cy >= world.padded_height {
        assert!(old.is_empty(), "an invalid seed must abort the entire search");
    }
    for &cell in &new {
        assert!(cell < world.read.element.len(), "out-of-storage index");
        let (px, py) = (cell % world.stride, cell / world.stride);
        assert!(px > 0 && py > 0, "zero coordinate is excluded");
        assert!(px <= u16::MAX as usize && py <= u16::MAX as usize);
    }
    counts.cases += 1;
    counts.cells += old.len() as u64;
}

fn equal<T: Eq + std::fmt::Debug>(old: &[T], new: &[T], label: &str) {
    assert_eq!(old.len(), new.len(), "length: {label}");
    if let Some(index) = old.iter().zip(new).position(|(a, b)| a != b) {
        panic!("{label}[{index}] mismatch: {:?} != {:?}", old[index], new[index]);
    }
}

fn equal_float(old: &[f32], new: &[f32], label: &str) {
    assert_eq!(old.len(), new.len(), "length: {label}");
    if let Some(index) = old.iter().zip(new).position(|(a, b)| a.to_bits() != b.to_bits()) {
        panic!("{label}[{index}] bit mismatch: {:08x} != {:08x}", old[index].to_bits(), new[index].to_bits());
    }
}

fn equal_other_fields(old: &Cells, new: &Cells) {
    equal(&old.element, &new.element, "element");
    equal_float(&old.temperature, &new.temperature, "temperature");
    equal_float(&old.mass, &new.mass, "mass");
    equal(&old.properties, &new.properties, "properties");
    equal(&old.insulation, &new.insulation, "insulation");
    equal(&old.strength, &new.strength, "strength");
    equal_float(&old.radiation, &new.radiation, "radiation");
}

fn equal_cells(old: &Cells, new: &Cells) {
    equal_other_fields(old, new);
    equal(&old.disease_idx, &new.disease_idx, "disease_idx");
    equal(&old.disease_count, &new.disease_count, "disease_count");
    equal(&old.disease_infestation, &new.disease_infestation, "disease_infestation");
    equal_float(&old.disease_growth, &new.disease_growth, "disease_growth");
}

pub(crate) fn compare_update(
    seed: &PhysicsWorld,
    table: &DiseaseTable,
    region: &Region,
    emitters: &[Emitter],
    dts: &[f32],
) -> u64 {
    let mut old_world = seed.clone();
    let mut new_world = seed.clone();
    let mut old_data: Vec<_> = emitters.iter().map(|value| value.baseline()).collect();
    let mut new_data: Vec<_> = emitters.iter().map(|value| value.candidate()).collect();
    for &dt in dts {
        success(baseline::update(&mut old_world, table, dt, region, &mut old_data));
        success(candidate::update(&mut new_world, table, dt, region, &mut new_data));
        equal_cells(&old_world.write, &new_world.write);
        equal_cells(&old_world.read, &new_world.read);
        equal_cells(&old_world.read, &seed.read);
        equal_other_fields(&old_world.write, &seed.write);
        for (old, new) in old_data.iter().zip(&new_data) {
            assert_eq!(old.cell, new.cell);
            assert_eq!(old.disease_idx, new.disease_idx);
            assert_eq!(old.max_depth, new.max_depth);
            assert_eq!(old.emit_count, new.emit_count);
            assert_eq!(old.emit_interval.to_bits(), new.emit_interval.to_bits());
            assert_eq!(old.elapsed.to_bits(), new.elapsed.to_bits(), "emitter elapsed bits");
        }
    }
    dts.len() as u64
}

fn small_search(counts: &mut Counts) {
    let depths = [0, 1, 2, 3, 4, 5, 8, 255];
    for stride in 0..=8 {
        for height in 0..=8 {
            let mut world = PhysicsWorld::fixture(stride, height);
            for (index, element) in world.read.element.iter_mut().enumerate() {
                *element = index.wrapping_mul(8191) as u16;
            }
            for x in -2..=stride as i32 + 2 {
                for y in -2..=height as i32 + 2 {
                    for &depth in &depths {
                        search(&world, x, y, depth, counts);
                    }
                }
            }
        }
    }
}

fn random_search(counts: &mut Counts, rng: &mut Rng) {
    for _ in 0..2_000 {
        let stride = 1 + rng.below(96);
        let height = 1 + rng.below(96);
        let mut world = PhysicsWorld::fixture(stride, height);
        for element in &mut world.read.element {
            *element = rng.next() as u16;
        }
        let x = if rng.below(4) == 0 { rng.next() as i32 } else { rng.below(stride + 4) as i32 - 2 };
        let y = if rng.below(4) == 0 { rng.next() as i32 } else { rng.below(height + 4) as i32 - 2 };
        let depth = rng.next() as u8;
        search(&world, x, y, depth, counts);
    }
}

fn coordinate_boundaries(counts: &mut Counts) {
    let long = [i32::MIN, -131073, -65537, -65536, -1, 0, 1, 2, 65534, 65535, 65536, 65537, 131071, i32::MAX];
    let short = [-1, 0, 1, 2, 3, 4, 65537];
    for length in [65535, 65536, 65537, 65538] {
        for vertical in [false, true] {
            let (stride, height) = if vertical { (3, length) } else { (length, 3) };
            let world = PhysicsWorld::fixture(stride, height);
            for &a in &long {
                for &b in &short {
                    for depth in [0, 1, 2, 4, 16, 255] {
                        let (x, y) = if vertical { (b, a) } else { (a, b) };
                        search(&world, x, y, depth, counts);
                    }
                }
            }
        }
    }
}

fn small_updates(counts: &mut Counts, table: &DiseaseTable, rng: &mut Rng) {
    for stride in 3..=7 {
        for height in 3..=7 {
            let world = random_world(stride, height, rng);
            for x in 0..=stride as i32 {
                for y in 0..=height as i32 {
                    for depth in [0, 1, 2, 4, 255] {
                        let positive = Emitter {
                            cell: y * stride as i32 + x,
                            disease_idx: 2,
                            max_depth: depth,
                            emit_count: 7,
                            emit_interval: 0.0,
                            elapsed: 0.0,
                        };
                        let negative = Emitter { emit_count: -4, ..positive };
                        let region = if (x + y) % 2 == 0 {
                            world.full_region()
                        } else {
                            Region { min_x: x, min_y: y, max_x: x, max_y: y, ..Region::default() }
                        };
                        counts.steps += compare_update(&world, table, &region, &[positive, negative], &[0.0, 0.2]);
                        counts.cases += 1;
                        counts.cells += world.write.element.len() as u64 * 2;
                    }
                }
            }
        }
    }
}

fn random_updates(counts: &mut Counts, table: &DiseaseTable, rng: &mut Rng) {
    let times = [0.0f32, -0.0, 0.125, 0.5, 1.0, -1.0, f32::INFINITY, f32::NEG_INFINITY, f32::from_bits(0x7fc45678)];
    let ids = [0u8, 1, 2, 3, 4, 254, 255];
    let amounts = [i32::MIN, -100, -1, 0, 1, 50, i32::MAX];
    for case in 0..1_200 {
        let stride = 3 + rng.below(30);
        let height = 3 + rng.below(30);
        let world = random_world(stride, height, rng);
        let mut data = Vec::new();
        for emitter_index in 0..1 + rng.below(12) {
            let cell = if emitter_index % 3 == 0 {
                ((height / 2) * stride + stride / 2) as i32
            } else {
                rng.below(stride * height + 8) as i32 - 4
            };
            data.push(Emitter {
                cell,
                disease_idx: rng.choose(&ids),
                max_depth: rng.choose(&[0, 1, 2, 4, 16, 64, 255]),
                emit_count: rng.choose(&amounts),
                emit_interval: rng.choose(&times),
                elapsed: rng.choose(&times),
            });
        }
        let mut region = world.full_region();
        match case % 5 {
            0 => { region.min_x = stride as i32 / 2; region.max_x = region.min_x; }
            1 => { region.min_y = height as i32 / 2; region.max_y = region.min_y; }
            2 => { region.min_x = 2; region.max_x = 1; }
            3 => { region.min_x = i32::MIN; region.min_y = i32::MIN; }
            _ => {}
        }
        let dts = [rng.choose(&times), 0.0, 0.2, rng.choose(&times)];
        counts.steps += compare_update(&world, table, &region, &data, &dts);
        counts.cases += 1;
        counts.cells += world.write.element.len() as u64 * dts.len() as u64;
    }
}

fn special_updates(counts: &mut Counts, table: &DiseaseTable, rng: &mut Rng) {
    // Explicit overlapping positive/negative and different-species order cases.
    for stride in [3, 7, 32] {
        let world = random_world(stride, stride, rng);
        let cell = (stride / 2 * stride + stride / 2) as i32;
        let mut emitters = Vec::new();
        for (id, count) in [(2, 100), (1, 40), (2, -20), (3, -100), (0, i32::MAX), (0, i32::MIN), (255, 42)] {
            emitters.push(Emitter { cell, disease_idx: id, max_depth: 255, emit_count: count, emit_interval: 0.5, elapsed: 1.0 });
        }
        counts.steps += compare_update(&world, table, &world.full_region(), &emitters, &[0.0, 0.125, 0.5, 1.0]);
        counts.cases += 1;
    }
    for length in [65535, 65536, 65537, 65538] {
        for vertical in [false, true] {
            let (stride, height) = if vertical { (3, length) } else { (length, 3) };
            let mut world = PhysicsWorld::fixture(stride, height);
            world.write.disease_idx.fill(2);
            world.write.disease_count.fill(100);
            let region = world.full_region();
            for value in [0, 1, 65534, 65535, 65536, 65537] {
                let (x, y) = if vertical { (1, value) } else { (value, 1) };
                for depth in [0, 1, 255] {
                    let emitter = Emitter {
                        cell: y * stride as i32 + x,
                        disease_idx: 2,
                        max_depth: depth,
                        emit_count: -7,
                        emit_interval: 0.0,
                        elapsed: 0.0,
                    };
                    counts.steps += compare_update(&world, table, &region, &[emitter], &[0.125]);
                    counts.cases += 1;
                    counts.cells += world.write.element.len() as u64;
                }
            }
        }
    }
    // At zero interval, exactly one emission still occurs in one update.
    let mut world = PhysicsWorld::fixture(5, 5);
    world.write.disease_idx.fill(2);
    world.write.disease_count.fill(10);
    let mut emitter = [candidate::DiseaseEmitterData { cell: 12, disease_idx: 2, max_depth: 1, emit_count: 5, emit_interval: 0.0, elapsed: 0.0 }];
    let region = world.full_region();
    success(candidate::update(&mut world, table, 0.125, &region, &mut emitter));
    assert_eq!(world.write.disease_count[12], 15);
    assert_eq!(emitter[0].elapsed.to_bits(), 0.125f32.to_bits());
    counts.cases += 1;
    counts.steps += 1;
}

pub(crate) fn run(path: &str) {
    let started = Instant::now();
    let mut rng = Rng(0x4f4e495f4c45414e);
    let table = disease_table(true);
    let mut reports = Vec::new();
    let mut total_cases = 0u64;
    let mut total_cells = 0u64;
    let mut total_steps = 0u64;
    macro_rules! group {
        ($name:literal, $body:expr) => {{
            let mut counts = Counts::default();
            let begin = Instant::now();
            ($body)(&mut counts);
            reports.push(format!("{{\"name\":\"{}\",\"cases\":{},\"compared_cells\":{},\"update_steps\":{},\"elapsed_ns\":{}}}", $name, counts.cases, counts.cells, counts.steps, begin.elapsed().as_nanos()));
            eprintln!("PASS {}: {} cases, {} update steps", $name, counts.cases, counts.steps);
            total_cases += counts.cases;
            total_cells += counts.cells;
            total_steps += counts.steps;
        }};
    }
    group!("small_exhaustive_search", small_search);
    group!("random_search", |counts: &mut Counts| random_search(counts, &mut rng));
    group!("u16_coordinate_and_dimension_boundaries", coordinate_boundaries);
    group!("small_exhaustive_updates", |counts: &mut Counts| small_updates(counts, &table, &mut rng));
    group!("random_updates_special_float_bits", |counts: &mut Counts| random_updates(counts, &table, &mut rng));
    group!("overlap_region_timer_and_large_dimension_updates", |counts: &mut Counts| special_updates(counts, &table, &mut rng));
    let report = format!(
        "{{\n  \"status\": \"passed\",\n  \"seed_hex\": \"4f4e495f4c45414e\",\n  \"total_cases\": {total_cases},\n  \"compared_cells\": {total_cells},\n  \"update_steps\": {total_steps},\n  \"elapsed_ns\": {},\n  \"groups\": [{}],\n  \"scope\": \"Actual Rust disease emitter component; minimal world adapter; not an ONI runtime test\"\n}}\n",
        started.elapsed().as_nanos(), reports.join(",\n    ")
    );
    fs::write(path, report).expect("write correctness result");
    eprintln!("PASS total: {total_cases} cases; result: {path}");
}
