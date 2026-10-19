use std::fs::File;
use std::hint::black_box;
use std::io::{BufWriter, Write};
use std::time::Instant;

use crate::shared::{consume_index, disease_checksum, disease_table, success, Emitter};
use crate::tables::DiseaseTable;
use crate::world::{PhysicsWorld, Region};
use crate::{baseline, candidate};

const MIN_SAMPLE_NS: u128 = 10_000_000;
const TARGET_SAMPLE_NS: u128 = 20_000_000;
const PAIRS: usize = 8;
const MAX_ITERATIONS: u64 = 1 << 24;

#[derive(Clone, Copy)]
pub(crate) enum Kind { Search, Update }

impl Kind {
    pub(crate) fn name(self) -> &'static str {
        match self { Self::Search => "search", Self::Update => "update" }
    }
}

#[derive(Clone, Copy)]
pub(crate) enum Algorithm { Baseline, Candidate }

impl Algorithm {
    pub(crate) fn name(self) -> &'static str {
        match self { Self::Baseline => "baseline", Self::Candidate => "candidate" }
    }
}

#[derive(Clone, Copy)]
pub(crate) struct Case {
    pub(crate) kind: Kind,
    pub(crate) width: usize,
    pub(crate) height: usize,
    pub(crate) depth: u8,
    pub(crate) emitter_count: usize,
}

impl Case {
    pub(crate) fn id(self) -> String {
        format!("{}_{}x{}_d{}_e{}", self.kind.name(), self.width, self.height, self.depth, self.emitter_count)
    }
}

pub(crate) fn cases() -> Vec<Case> {
    let mut output = Vec::new();
    for width in [256, 512, 1024] {
        for depth in [1, 4, 16, 64, 255] {
            output.push(Case { kind: Kind::Search, width, height: width, depth, emitter_count: 1 });
            for emitter_count in [1, 8, 32] {
                output.push(Case { kind: Kind::Update, width, height: width, depth, emitter_count });
            }
        }
    }
    output
}

pub(crate) struct Fixture {
    pub(crate) case: Case,
    pub(crate) world: PhysicsWorld,
    pub(crate) region: Region,
    pub(crate) emitters: Vec<Emitter>,
    pub(crate) visited_cells: usize,
}

pub(crate) fn fixture(case: Case) -> Fixture {
    let mut world = PhysicsWorld::fixture(case.width + 2, case.height + 2);
    world.write.disease_idx.fill(0);
    world.write.disease_count.fill(1_000_000);
    world.write.disease_infestation.fill(17);
    world.write.disease_growth.fill(0.25);
    for (index, element) in world.read.element.iter_mut().enumerate() {
        *element = (index % 4) as u16;
    }
    let region = world.full_region();
    let mut emitters = Vec::new();
    let mut visited_cells = 0;
    for index in 0..case.emitter_count {
        let x = 1 + (case.width / 2 + index * 97) % case.width;
        let y = 1 + (case.height / 2 + index * 193) % case.height;
        emitters.push(Emitter {
            cell: (y * world.stride + x) as i32,
            disease_idx: 0,
            max_depth: case.depth,
            emit_count: 1,
            emit_interval: 0.0,
            elapsed: 0.0,
        });
        visited_cells += candidate::reachable_cells(&world, x as i32, y as i32, case.depth).count();
    }
    Fixture { case, world, region, emitters, visited_cells }
}

#[inline(never)]
pub(crate) fn baseline_search(world: &PhysicsWorld, x: i32, y: i32, depth: u8) -> u64 {
    let cells = success(baseline::reachable_cells(black_box(world), black_box(x), black_box(y), black_box(depth)));
    let mut checksum = 0x123456789abcdef0;
    for cell in cells {
        checksum = consume_index(checksum, cell);
    }
    black_box(checksum)
}

#[inline(never)]
pub(crate) fn candidate_search(world: &PhysicsWorld, x: i32, y: i32, depth: u8) -> u64 {
    let cells = candidate::reachable_cells(black_box(world), black_box(x), black_box(y), black_box(depth));
    let mut checksum = 0x123456789abcdef0;
    for cell in cells {
        checksum = consume_index(checksum, cell);
    }
    black_box(checksum)
}

#[inline(never)]
pub(crate) fn baseline_update(world: &mut PhysicsWorld, table: &DiseaseTable, region: &Region, data: &mut [baseline::DiseaseEmitterData]) {
    success(baseline::update(black_box(world), black_box(table), black_box(0.0), black_box(region), black_box(data)));
}

#[inline(never)]
pub(crate) fn candidate_update(world: &mut PhysicsWorld, table: &DiseaseTable, region: &Region, data: &mut [candidate::DiseaseEmitterData]) {
    success(candidate::update(black_box(world), black_box(table), black_box(0.0), black_box(region), black_box(data)));
}

struct Measurement {
    iterations: u64,
    elapsed_ns: u128,
    checksum: u64,
}

fn measure(fixture: &Fixture, table: &DiseaseTable, algorithm: Algorithm, iterations: u64) -> Measurement {
    let (elapsed_ns, checksum) = match fixture.case.kind {
        Kind::Search => {
            let emitter = fixture.emitters[0];
            let x = emitter.cell % fixture.world.stride as i32;
            let y = emitter.cell / fixture.world.stride as i32;
            let function = match algorithm { Algorithm::Baseline => baseline_search, Algorithm::Candidate => candidate_search };
            let mut checksum = 0u64;
            let start = Instant::now();
            for _ in 0..iterations {
                checksum = checksum.wrapping_add(function(&fixture.world, x, y, fixture.case.depth));
            }
            let elapsed = start.elapsed().as_nanos();
            (elapsed, black_box(checksum))
        }
        Kind::Update => {
            // The same complete seed is restored before every block, outside
            // the clock. Stable positive same-species updates do not change
            // branch selection when the algorithms use different batch sizes.
            let mut world = fixture.world.clone();
            match algorithm {
                Algorithm::Baseline => {
                    let mut data: Vec<_> = fixture.emitters.iter().map(|e| e.baseline()).collect();
                    let start = Instant::now();
                    for _ in 0..iterations {
                        baseline_update(&mut world, table, &fixture.region, &mut data);
                    }
                    let elapsed = start.elapsed().as_nanos();
                    let checksum = data.iter().fold(disease_checksum(&world.write), |acc, e| acc.rotate_left(3) ^ e.elapsed.to_bits() as u64);
                    (elapsed, black_box(checksum))
                }
                Algorithm::Candidate => {
                    let mut data: Vec<_> = fixture.emitters.iter().map(|e| e.candidate()).collect();
                    let start = Instant::now();
                    for _ in 0..iterations {
                        candidate_update(&mut world, table, &fixture.region, &mut data);
                    }
                    let elapsed = start.elapsed().as_nanos();
                    let checksum = data.iter().fold(disease_checksum(&world.write), |acc, e| acc.rotate_left(3) ^ e.elapsed.to_bits() as u64);
                    (elapsed, black_box(checksum))
                }
            }
        }
    };
    Measurement { iterations, elapsed_ns, checksum }
}

fn row(writer: &mut impl Write, fixture: &Fixture, phase: &str, pair: i32, order: &str, algorithm: Algorithm, result: &Measurement) {
    let case = fixture.case;
    writeln!(writer, "{phase},{},{},{},{},{},{},{},{},{},{},{pair},{order},{},{},{},{:.9},{:016x}",
        case.id(), case.kind.name(), case.width, case.height,
        fixture.world.stride, fixture.world.padded_height, case.depth, case.depth - 1,
        case.emitter_count, fixture.visited_cells, algorithm.name(), result.iterations,
        result.elapsed_ns, result.elapsed_ns as f64 / result.iterations as f64, result.checksum
    ).expect("write sample");
}

fn larger_batch(iterations: u64, elapsed_ns: u128, target_ns: u128) -> u64 {
    let factor = (target_ns / elapsed_ns.max(1) + 1).clamp(2, 8) as u64;
    let result = iterations.saturating_mul(factor).min(MAX_ITERATIONS);
    assert!(result > iterations, "minimum timing duration cannot be reached under iteration limit");
    result
}

fn calibrate(writer: &mut impl Write, fixture: &Fixture, table: &DiseaseTable, algorithm: Algorithm) -> u64 {
    let mut iterations = 1;
    loop {
        let result = measure(fixture, table, algorithm, iterations);
        row(writer, fixture, "warmup", -1, "none", algorithm, &result);
        if result.elapsed_ns >= MIN_SAMPLE_NS { break; }
        iterations = larger_batch(iterations, result.elapsed_ns, MIN_SAMPLE_NS);
    }
    loop {
        let result = measure(fixture, table, algorithm, iterations);
        row(writer, fixture, "calibration", -1, "none", algorithm, &result);
        if result.elapsed_ns >= TARGET_SAMPLE_NS { return iterations; }
        iterations = larger_batch(iterations, result.elapsed_ns, TARGET_SAMPLE_NS);
    }
}

fn validate_fixture(fixture: &Fixture, table: &DiseaseTable) {
    for emitter in &fixture.emitters {
        let x = emitter.cell % fixture.world.stride as i32;
        let y = emitter.cell / fixture.world.stride as i32;
        let mut old = success(baseline::reachable_cells(&fixture.world, x, y, emitter.max_depth));
        let mut new: Vec<_> = candidate::reachable_cells(&fixture.world, x, y, emitter.max_depth).collect();
        old.sort_unstable();
        new.sort_unstable();
        assert_eq!(old, new, "benchmark fixture cell set");
        assert!(new.windows(2).all(|pair| pair[0] != pair[1]));
    }
    if matches!(fixture.case.kind, Kind::Update) {
        crate::verify::compare_update(&fixture.world, table, &fixture.region, &fixture.emitters, &[0.0]);
    }
}

pub(crate) fn run(path: &str) {
    let mut writer = BufWriter::new(File::create(path).expect("create sample CSV"));
    writeln!(writer, "phase,case_id,kind,width,height,stride,padded_height,max_depth,radius,emitters,visited_cells,pair,order,algorithm,iterations,elapsed_ns,ns_per_call,checksum").expect("write header");
    let table = disease_table(false);
    let all_cases = cases();
    for (case_number, case) in all_cases.iter().copied().enumerate() {
        let fixture = fixture(case);
        validate_fixture(&fixture, &table);
        let mut old_iterations = calibrate(&mut writer, &fixture, &table, Algorithm::Baseline);
        let mut new_iterations = calibrate(&mut writer, &fixture, &table, Algorithm::Candidate);
        for pair in 0..PAIRS {
            let (order, algorithms) = if pair % 2 == 0 {
                ("AB", [Algorithm::Baseline, Algorithm::Candidate])
            } else {
                ("BA", [Algorithm::Candidate, Algorithm::Baseline])
            };
            for algorithm in algorithms {
                let iterations = match algorithm { Algorithm::Baseline => &mut old_iterations, Algorithm::Candidate => &mut new_iterations };
                loop {
                    let result = measure(&fixture, &table, algorithm, *iterations);
                    if result.elapsed_ns >= MIN_SAMPLE_NS {
                        row(&mut writer, &fixture, "measured", pair as i32, order, algorithm, &result);
                        break;
                    }
                    row(&mut writer, &fixture, "short", pair as i32, order, algorithm, &result);
                    *iterations = larger_batch(*iterations, result.elapsed_ns, TARGET_SAMPLE_NS);
                }
            }
        }
        writer.flush().expect("flush samples");
        eprintln!("BENCH {}/{} {} K={}", case_number + 1, all_cases.len(), case.id(), fixture.visited_cells);
    }
}
