//! Incremental scalar-row versus production SIMD benchmark. Outputs CSV.
#[path = "../src/presentation_flow.rs"]
mod flow;

use std::{hint::black_box, time::Instant};

fn original_update(
    output: &mut [[f32; 2]],
    width: usize,
    old: &[u16],
    new: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
) {
    for (public, out) in output.iter_mut().enumerate() {
        let cell = (public / width + 1) * (width + 2) + public % width + 1;
        let scale = if old[cell] != new[cell] {
            0.0
        } else if mass[cell] > 1.0 {
            1.0 / mass[cell]
        } else {
            1.0
        };
        let [right, left, down, up] = flow[cell];
        *out = [(right - left) * scale, (up - down) * scale];
    }
}

// Frozen pre-SIMD production algorithm. Benchmark this complete row loop,
// rather than the new scalar fallback's different call/inlining structure.
fn previous_row_update(
    output: &mut [[f32; 2]],
    width: usize,
    height: usize,
    old_element: &[u16],
    element: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
) {
    let stride = width + 2;
    assert_eq!(output.len(), width * height);
    for y in 0..height {
        let start = (y + 1) * stride + 1;
        let range = start..start + width;
        let old = &old_element[range.clone()];
        let new = &element[range.clone()];
        let mass = &mass[range.clone()];
        let flow = &flow[range];
        let out = &mut output[y * width..(y + 1) * width];
        for x in 0..width {
            let scale = if old[x] != new[x] {
                0.0
            } else if mass[x] > 1.0 {
                1.0 / mass[x]
            } else {
                1.0
            };
            let [right, left, down, up] = flow[x];
            out[x] = [(right - left) * scale, (up - down) * scale];
        }
    }
}

fn main() {
    const PAIRS: usize = 41;
    const WARMUP: usize = 5;
    println!(
        "pattern,width,height,scalar_median_us,simd_median_us,speedup,scalar_min_us,scalar_p95_us,simd_min_us,simd_p95_us,scalar_samples_us,simd_samples_us,original_median_us,original_speedup,original_samples_us"
    );
    let mut no_regression = true;
    let mut improvement = false;
    for pattern in ["mixed", "thin", "changed", "empty"] {
        for (width, height) in [
            (1, 1),
            (3, 5),
            (17, 19),
            (96, 96),
            (256, 384),
            (512, 512),
            (1024, 1024),
        ] {
            let count = (width + 2) * (height + 2);
            let mut state = 0x1234_5678_u32;
            let mut next = || {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                state
            };
            let old: Vec<_> = (0..count).map(|_| (next() % 200) as u16).collect();
            let new: Vec<_> = old
                .iter()
                .map(|&v| {
                    if pattern == "changed" || next() % 7 == 0 {
                        v ^ 1
                    } else {
                        v
                    }
                })
                .collect();
            let mass: Vec<_> = (0..count)
                .map(|_| match pattern {
                    "thin" => (next() % 100) as f32 / 127.0,
                    "empty" => 0.0,
                    _ => (next() % 100_000) as f32 / 31.0,
                })
                .collect();
            let values: Vec<[f32; 4]> = (0..count)
                .map(|_| {
                    std::array::from_fn(|_| {
                        if pattern == "empty" {
                            0.0
                        } else {
                            (next() % 100_000) as f32 / 127.0 - 400.0
                        }
                    })
                })
                .collect();
            let mut scalar = vec![[0.0; 2]; width * height];
            let mut candidate = scalar.clone();
            let mut original = scalar.clone();
            previous_row_update(&mut scalar, width, height, &old, &new, &mass, &values);
            original_update(&mut original, width, &old, &new, &mass, &values);
            flow::update(&mut candidate, width, height, &old, &new, &mass, &values);
            assert!(
                scalar
                    .iter()
                    .flatten()
                    .zip(candidate.iter().flatten())
                    .all(|(a, b)| a.to_bits() == b.to_bits()),
                "bit mismatch {pattern} {width}x{height}"
            );
            assert!(
                original
                    .iter()
                    .flatten()
                    .zip(candidate.iter().flatten())
                    .all(|(a, b)| a.to_bits() == b.to_bits()),
                "original bit mismatch {pattern} {width}x{height}"
            );
            let repeat = (1_000_000 / (width * height)).clamp(1, 1000);
            let mut samples: [Vec<f64>; 3] = std::array::from_fn(|_| Vec::new());
            for pair in 0..WARMUP + PAIRS {
                for offset in 0..3 {
                    let kind = (pair + offset) % 3;
                    let start = Instant::now();
                    for _ in 0..repeat {
                        if kind == 0 {
                            previous_row_update(
                                black_box(&mut scalar),
                                black_box(width),
                                black_box(height),
                                black_box(&old),
                                black_box(&new),
                                black_box(&mass),
                                black_box(&values),
                            );
                            black_box(&scalar);
                        } else if kind == 1 {
                            flow::update(
                                black_box(&mut candidate),
                                black_box(width),
                                black_box(height),
                                black_box(&old),
                                black_box(&new),
                                black_box(&mass),
                                black_box(&values),
                            );
                            black_box(&candidate);
                        } else {
                            original_update(
                                black_box(&mut original),
                                black_box(width),
                                black_box(&old),
                                black_box(&new),
                                black_box(&mass),
                                black_box(&values),
                            );
                            black_box(&original);
                        }
                    }
                    if pair >= WARMUP {
                        samples[kind].push(start.elapsed().as_secs_f64() * 1e6 / repeat as f64);
                    }
                }
            }
            let ordered = samples.clone().map(|mut v| {
                v.sort_by(f64::total_cmp);
                v
            });
            let a = &ordered[0];
            let b = &ordered[1];
            let speedup = a[PAIRS / 2] / b[PAIRS / 2];
            let original_speedup = ordered[2][PAIRS / 2] / b[PAIRS / 2];
            if width >= 256 {
                no_regression &= speedup >= 1.0 / 1.03;
                improvement |= speedup >= 1.10;
            }
            let series = |values: &[f64]| {
                values
                    .iter()
                    .map(|x| format!("{x:.6}"))
                    .collect::<Vec<_>>()
                    .join(";")
            };
            println!(
                "{pattern},{width},{height},{:.6},{:.6},{speedup:.6},{:.6},{:.6},{:.6},{:.6},{},{},{:.6},{original_speedup:.6},{}",
                a[PAIRS / 2],
                b[PAIRS / 2],
                a[0],
                a[(PAIRS - 1) * 95 / 100],
                b[0],
                b[(PAIRS - 1) * 95 / 100],
                series(&samples[0]),
                series(&samples[1]),
                ordered[2][PAIRS / 2],
                series(&samples[2])
            );
            if width >= 256 {
                eprintln!(
                    "{pattern} {width}x{height}: SIMD {speedup:.3}x scalar row; {original_speedup:.3}x original"
                );
            }
        }
    }
    eprintln!(
        "Gate: no >3% colony-sized regression={no_regression}; >=10% improvement={improvement}"
    );
    if !(no_regression && improvement) {
        std::process::exit(2);
    }
}
