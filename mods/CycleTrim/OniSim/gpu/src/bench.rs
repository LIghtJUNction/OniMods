use onisim_gpu::{FlowGpu, FlowInput, cpu};
use serde::Serialize;
use std::{hint::black_box, time::Instant};

struct Fixture {
    width: usize,
    height: usize,
    old: Vec<u16>,
    new: Vec<u16>,
    mass: Vec<f32>,
    flow: Vec<[f32; 4]>,
}

impl Fixture {
    fn new(width: usize, height: usize, seed: u32) -> Self {
        let count = (width + 2) * (height + 2);
        let mut random = seed;
        let mut next = || {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            random
        };
        let old: Vec<_> = (0..count).map(|_| (next() % 200) as u16).collect();
        let new = old
            .iter()
            .map(|&x| if next() % 7 == 0 { x + 1 } else { x })
            .collect();
        let mass = (0..count)
            .map(|_| (next() % 100_000) as f32 / 31.0)
            .collect();
        let flow = (0..count)
            .map(|_| std::array::from_fn(|_| (next() % 100_000) as f32 / 127.0 - 400.0))
            .collect();
        Self {
            width,
            height,
            old,
            new,
            mass,
            flow,
        }
    }
    fn input(&self) -> FlowInput<'_> {
        FlowInput {
            old_element: &self.old,
            element: &self.new,
            mass: &self.mass,
            flow: &self.flow,
        }
    }
    fn cpu(&self, output: &mut [[f32; 2]]) {
        cpu::update(
            output,
            self.width,
            self.height,
            &self.old,
            &self.new,
            &self.mass,
            &self.flow,
        );
    }
    // Original production indexing and arithmetic, retained as a differential
    // oracle. The candidate benchmark calls the actual shared CPU helper.
    fn original(&self, output: &mut [[f32; 2]]) {
        for (public, value) in output.iter_mut().enumerate() {
            let cell = (public / self.width + 1) * (self.width + 2) + public % self.width + 1;
            let scale = if self.old[cell] != self.new[cell] {
                0.0
            } else if self.mass[cell] > 1.0 {
                1.0 / self.mass[cell]
            } else {
                1.0
            };
            let [right, left, down, up] = self.flow[cell];
            *value = [(right - left) * scale, (up - down) * scale];
        }
    }
}

#[derive(Serialize)]
struct Comparison {
    bit_mismatches: usize,
    semantic_mismatches: usize,
    nonfinite_mismatches: usize,
    max_finite_absolute_error: f64,
}
impl Comparison {
    fn include(&mut self, other: Self) {
        self.bit_mismatches += other.bit_mismatches;
        self.semantic_mismatches += other.semantic_mismatches;
        self.nonfinite_mismatches += other.nonfinite_mismatches;
        self.max_finite_absolute_error = self
            .max_finite_absolute_error
            .max(other.max_finite_absolute_error);
    }
}
fn compare(a: &[[f32; 2]], b: &[[f32; 2]]) -> Comparison {
    let mut result = Comparison {
        bit_mismatches: 0,
        semantic_mismatches: 0,
        nonfinite_mismatches: 0,
        max_finite_absolute_error: 0.0,
    };
    for (&a, &b) in a.iter().flatten().zip(b.iter().flatten()) {
        if a.to_bits() != b.to_bits() {
            result.bit_mismatches += 1;
            if !(a.is_nan() && b.is_nan()) {
                result.semantic_mismatches += 1;
            }
            if a.is_finite() && b.is_finite() {
                result.max_finite_absolute_error = result
                    .max_finite_absolute_error
                    .max((a as f64 - b as f64).abs());
            } else if !(a.is_nan() && b.is_nan()) {
                result.nonfinite_mismatches += 1;
            }
        }
    }
    result
}

#[derive(Serialize)]
struct Timing {
    min_us: f64,
    median_us: f64,
    p95_us: f64,
    max_us: f64,
    samples_us: Vec<f64>,
}
fn summary(mut samples: Vec<f64>) -> Timing {
    let ordered = {
        let mut v = samples.clone();
        v.sort_by(f64::total_cmp);
        v
    };
    Timing {
        min_us: ordered[0],
        median_us: ordered[ordered.len() / 2],
        p95_us: ordered[(ordered.len() - 1) * 95 / 100],
        max_us: *ordered.last().unwrap(),
        samples_us: std::mem::take(&mut samples),
    }
}
#[derive(Serialize)]
struct Case {
    width: usize,
    height: usize,
    cpu_correctness: Comparison,
    gpu_correctness: Comparison,
    original_cpu: Timing,
    row_cpu: Timing,
    gpu_end_to_end: Timing,
    gpu_resident_with_readback: Timing,
    cpu_speedup: f64,
    gpu_speedup: f64,
    gpu_gate_passed: bool,
}
#[derive(Serialize)]
struct Report {
    scope: &'static str,
    adapter: String,
    vendor: u32,
    backend: String,
    warmup: usize,
    pairs: usize,
    correctness_cases: usize,
    reciprocal_stress: Option<Comparison>,
    edge_cases: Vec<Comparison>,
    cases: Vec<Case>,
    passed: bool,
}

fn run() -> Result<bool, String> {
    let mut args = std::env::args().skip(1);
    let path = args
        .next()
        .ok_or("usage: flow-bench REPORT.json [adapter substring]")?;
    let adapter_name = args.next().unwrap_or_else(|| "nvidia".into());
    if std::path::Path::new(&path).exists() {
        return Err("report already exists; use a new path to preserve previous evidence".into());
    }
    let pairs = 31;
    let warmup = 5;
    let mut report = Report {
        scope: "flow operator only; no whole-frame/game/DLSS acceptance",
        adapter: String::new(),
        vendor: 0,
        backend: String::new(),
        warmup,
        pairs,
        correctness_cases: 0,
        reciprocal_stress: None,
        edge_cases: Vec::new(),
        cases: Vec::new(),
        passed: true,
    };
    // Odd packed-u16 tails, partial workgroups, padding, transitions and reuse.
    for (width, height) in [
        (1, 1),
        (3, 5),
        (17, 19),
        (96, 96),
        (256, 384),
        (512, 512),
        (1024, 1024),
    ] {
        let fixture = Fixture::new(width, height, 0x12345678);
        let mut gpu = pollster::block_on(FlowGpu::new(width, height, &adapter_name))?;
        report.adapter = gpu.adapter.name.clone();
        report.vendor = gpu.adapter.vendor;
        report.backend = format!("{:?}", gpu.adapter.backend);
        let mut original = vec![[0.0; 2]; width * height];
        let mut row = original.clone();
        let mut computed = original.clone();
        if gpu.run_resident(&mut computed).is_ok() {
            return Err("GPU dispatched uninitialized resident inputs".into());
        }
        if gpu
            .run(&fixture.input(), &mut computed[..width * height - 1])
            .is_ok()
        {
            return Err("GPU accepted a wrong-sized output buffer".into());
        }
        let mut cpu_correctness = compare(&original, &row);
        let mut gpu_correctness = compare(&original, &computed);
        for seed in [1, 0x12345678, 0xffffffff] {
            let f = Fixture::new(width, height, seed);
            f.original(&mut original);
            f.cpu(&mut row);
            gpu.run(&f.input(), &mut computed)?;
            cpu_correctness.include(compare(&original, &row));
            gpu_correctness.include(compare(&original, &computed));
            report.correctness_cases += 1;
        }
        fixture.original(&mut original);
        fixture.cpu(&mut row);
        gpu.run(&fixture.input(), &mut computed)?;
        for _ in 0..warmup {
            fixture.original(&mut original);
            fixture.cpu(&mut row);
            gpu.run(&fixture.input(), &mut computed)?;
        }
        let mut samples: [Vec<f64>; 4] = std::array::from_fn(|_| Vec::new());
        for pair in 0..pairs {
            // Rotate order to avoid always giving one backend the colder host.
            for offset in 0..4 {
                let kind = (pair + offset) % 4;
                // Amortize timer overhead on tiny CPU kernels, not GPU work.
                let repeat = if kind < 2 {
                    (200_000 / (width * height)).clamp(1, 1000)
                } else {
                    1
                };
                let start = Instant::now();
                for _ in 0..repeat {
                    match kind {
                        0 => {
                            black_box(&fixture).original(black_box(&mut original));
                            black_box(&original);
                        }
                        1 => {
                            black_box(&fixture).cpu(black_box(&mut row));
                            black_box(&row);
                        }
                        2 => {
                            gpu.run(black_box(&fixture.input()), black_box(&mut computed))?;
                            black_box(&computed);
                        }
                        _ => {
                            gpu.run_resident(black_box(&mut computed))?;
                            black_box(&computed);
                        }
                    }
                }
                samples[kind].push(start.elapsed().as_secs_f64() * 1e6 / repeat as f64);
            }
        }
        let [a, b, c, resident] = samples.map(summary);
        let cpu_speedup = a.median_us / b.median_us;
        let gpu_speedup = b.median_us / c.median_us;
        let gpu_gate_passed = gpu_correctness.bit_mismatches == 0 && gpu_speedup >= 1.10;
        report.passed &= cpu_correctness.bit_mismatches == 0 && gpu_gate_passed;
        eprintln!(
            "{width}x{height}: row CPU {cpu_speedup:.3}x; GPU end-to-end {gpu_speedup:.3}x; GPU differing words {}",
            gpu_correctness.bit_mismatches
        );
        report.cases.push(Case {
            width,
            height,
            cpu_correctness,
            gpu_correctness,
            original_cpu: a,
            row_cpu: b,
            gpu_end_to_end: c,
            gpu_resident_with_readback: resident,
            cpu_speedup,
            gpu_speedup,
            gpu_gate_passed,
        });
    }
    // Exercise every normal reciprocal exponent and varying mantissas. Unity's
    // worker FTZ behavior and GPU subnormal behavior remain separate edge gates.
    let mut stress = Fixture::new(257, 257, 19);
    for i in 0..stress.mass.len() {
        stress.old[i] = 1;
        stress.new[i] = 1;
        let exponent = 127 + (i as u32 % 126);
        let mantissa = (i as u32).wrapping_mul(0x9e3779b9) & 0x7fffff;
        stress.mass[i] = f32::from_bits((exponent << 23) | mantissa);
        stress.flow[i] = [1.0, 0.0, 0.0, 1.0];
    }
    let mut gpu = pollster::block_on(FlowGpu::new(stress.width, stress.height, &adapter_name))?;
    let mut expected = vec![[0.0; 2]; stress.width * stress.height];
    let mut actual = expected.clone();
    stress.original(&mut expected);
    gpu.run(&stress.input(), &mut actual)?;
    let comparison = compare(&expected, &actual);
    report.passed &= comparison.bit_mismatches == 0;
    report.reciprocal_stress = Some(comparison);
    report.correctness_cases += 1;

    // Rejected uploads invalidate the resident snapshot rather than letting a
    // caller accidentally consume a stale previously successful result.
    let bad = FlowInput {
        old_element: &stress.old[..1],
        ..stress.input()
    };
    if gpu.upload(&bad).is_ok() || gpu.run_resident(&mut actual).is_ok() {
        return Err("GPU reused a resident snapshot after an invalid upload".into());
    }

    // Non-finite, signed zero and denormal behavior is a separate mandatory gate.
    let mut f = Fixture::new(17, 19, 7);
    let edge = [
        0.0,
        -0.0,
        1.0,
        f32::from_bits(0x3f800001),
        2.0,
        f32::MAX,
        f32::MIN_POSITIVE,
        f32::from_bits(1),
        f32::INFINITY,
        f32::NEG_INFINITY,
        f32::from_bits(0x7fc00001),
        -3.0,
    ];
    for i in 0..f.mass.len() {
        f.mass[i] = edge[i % edge.len()];
        f.flow[i] = std::array::from_fn(|j| edge[(i + j * 3) % edge.len()]);
    }
    let mut gpu = pollster::block_on(FlowGpu::new(f.width, f.height, &adapter_name))?;
    let mut a = vec![[0.0; 2]; f.width * f.height];
    let mut b = a.clone();
    let mut c = a.clone();
    f.original(&mut a);
    f.cpu(&mut b);
    gpu.run(&f.input(), &mut c)?;
    for value in [compare(&a, &b), compare(&a, &c)] {
        report.passed &= value.bit_mismatches == 0;
        report.edge_cases.push(value);
    }
    let passed = report.passed;
    let data = serde_json::to_vec_pretty(&report).map_err(|e| e.to_string())?;
    use std::io::Write;
    std::fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&path)
        .and_then(|mut file| file.write_all(&data))
        .map_err(|e| e.to_string())?;
    eprintln!("report: {path}; admission gate: {passed}");
    Ok(passed)
}

fn main() {
    match run() {
        Ok(true) => (),
        Ok(false) => std::process::exit(2),
        Err(error) => {
            eprintln!("flow-bench failed: {error}");
            std::process::exit(1);
        }
    }
}
