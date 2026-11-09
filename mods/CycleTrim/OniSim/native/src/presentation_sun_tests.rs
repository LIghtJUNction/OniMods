use super::*;
use std::{hint::black_box, time::Instant};

fn table() -> ElementTable {
    let records = [
        (0x2d39bf75_u32, 0_u8, 1.0_f32, 0.0_f32),
        (0x6d95058c, 3, 1.0, 1.0),
        (0xa9360b34, 3, 1.0, 1.0),
        (OVERRIDE_HASH, 1, 2.0, 0.05),
        (10, 1, 3.0, 0.1),
        (11, 2, 1000.0, 0.4),
    ];
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&(records.len() as i32).to_le_bytes());
    for (index, (id, state, mass, absorption)) in records.into_iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&id.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = state;
        record[140..144].copy_from_slice(&absorption.to_le_bytes());
        record[156..160].copy_from_slice(&mass.to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + records.len() * 4, 0);
    ElementTable::parse(&bytes).unwrap()
}

fn fixture(width: usize, height: usize, pattern: &str) -> (PhysicsWorld, ElementTable) {
    let elements = table();
    let mut world = PhysicsWorld::new(width, height, 1, &elements)
        .unwrap_or_else(|_| panic!("could not allocate sunlight fixture"));
    let mut random = 0x1234_5678_u32;
    for cell in 0..world.write.element.len() {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        world.write.element[cell] = match pattern {
            "vacuum" => 0,
            "gas" | "blocked" => 4,
            _ => (random % 6) as u16,
        };
        world.write.mass[cell] = if pattern == "vacuum" {
            0.0
        } else {
            (random % 100_000) as f32 / 127.0
        };
        world.write.properties[cell] = match pattern {
            "blocked" if (cell / world.stride) % 17 == 0 => 0x20,
            "override" => 0x30,
            "mixed" => ((random >> 8) as u8) & 0x30,
            _ => 0,
        };
    }
    world.world_offsets = vec![[0, 0, width as i32, height as i32]];
    if pattern == "split" && width > 2 && height > 2 {
        world.world_offsets = vec![
            [0, 0, width as i32 / 2, height as i32],
            [
                width as i32 / 2,
                0,
                width as i32 - width as i32 / 2,
                height as i32,
            ],
            [1, 1, width as i32 - 2, height as i32 - 2],
        ];
    }
    (world, elements)
}

// Frozen production loop from 07e18765 (CycleTrim 0.4.10).
fn original(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable) {
    let override_index = elements
        .elements
        .iter()
        .rposition(|element| element.record.id as u32 == OVERRIDE_HASH);
    for &[x, y, width, height] in &world.world_offsets {
        if width <= 0 {
            continue;
        }
        let top = (y as i64 + height as i64 - 1) as usize;
        let (x, y, width) = (x as usize, y as usize, width as usize);
        for column in x..x + width {
            exposed[top * world.width + column] = 255;
            if height <= 1 {
                continue;
            }
            let mut remaining = 1.0;
            for row in (y..top).rev() {
                let public = row * world.width + column;
                let current = (row + 1) * world.stride + column + 1;
                let above = current + world.stride;
                let flags = world.write.properties[current];
                let index = if flags & 0x30 == 0x30 {
                    override_index.unwrap()
                } else {
                    world.write.element[above] as usize
                };
                let record = &elements.elements[index].record;
                let mass_scale = if record.state & 3 == 3 {
                    f32::INFINITY
                } else {
                    1.0 / record.default_mass
                };
                let absorption =
                    record.light_absorption * smin(world.write.mass[above] * mass_scale, 1.0);
                let value = smax(remaining - absorption, 0.0);
                remaining = if flags & 0x30 != 0x30 && flags & 0x20 != 0 {
                    0.0
                } else {
                    value
                };
                exposed[public] = cvtt_f32_i32(value * 255.0) as u8;
            }
        }
    }
}

#[test]
fn matches_original_offsets_overlaps_and_degenerate_rectangles() {
    for (width, height) in [(1, 1), (3, 5), (17, 19), (63, 64), (64, 65), (65, 66), (129, 71), (256, 384)] {
        for pattern in ["vacuum", "gas", "mixed", "blocked", "override", "split"] {
            let (mut world, elements) = fixture(width, height, pattern);
            // Check degenerate rectangles independently: both height 0 and -1
            // write the same top=0 row, so grouping them could hide a failure.
            let layouts = [
                world.world_offsets.clone(),
                vec![[0, 1, width as i32, 0]],
                vec![[0, 2, width as i32, -1]],
                vec![[-99, -99, 0, 0]],
                vec![[i32::MIN, i32::MIN, -1, i32::MIN]],
                Vec::new(),
            ];
            for offsets in layouts {
                world.world_offsets = offsets;
                let mut a = vec![0x96; width * height];
                let mut b = a.clone();
                original(&mut a, &world, &elements);
                update(&mut b, &world, &elements);
                assert_eq!(a, b, "{width}x{height} {pattern} {:?}", world.world_offsets);
            }
        }
    }
}

#[test]
fn matches_original_unmasked_exact_arithmetic() {
    let _restore = crate::float_env::FloatEnv::capture().install();
    let (mut world, mut elements) = fixture(129, 71, "vacuum");
    elements.elements[0].record.default_mass = 1.0;
    elements.elements[0].record.light_absorption = 0.0;
    let mut raw = 0_u32;
    unsafe {
        std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut raw, options(nostack, preserves_flags));
    }
    for exception in 0..6 {
        let mode = ((raw | 0x1f80) & !0x3f) & !(1 << (exception + 7));
        let mut a = vec![0x96; world.width * world.height];
        let mut b = a.clone();
        world.world_offsets.push([1, 2, 127, 64]);
        unsafe {
            std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &mode, options(nostack, preserves_flags));
        }
        original(black_box(&mut a), black_box(&world), black_box(&elements));
        update(black_box(&mut b), black_box(&world), black_box(&elements));
        let mut after = 0_u32;
        unsafe {
            std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut after, options(nostack, preserves_flags));
            std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &raw, options(nostack, preserves_flags));
        }
        assert_eq!(a, b, "unmasked exception {exception}");
        assert_eq!(after, mode, "unexpected arithmetic flags for exception {exception}");
    }
}

#[test]
fn matches_original_material_cache_collisions_and_table_changes() {
    let (mut world, mut elements) = fixture(129, 71, "mixed");
    let gas = elements.elements[4].clone();
    elements.elements.resize(198, gas);
    for (index, element) in elements.elements.iter_mut().enumerate().skip(6) {
        element.record.default_mass = (index + 1) as f32;
        element.record.light_absorption = (index % 17) as f32 / 128.0;
    }
    for (cell, element) in world.write.element.iter_mut().enumerate() {
        // The four distinct entries all collide in the direct-mapped cache.
        *element = [4, 68, 132, 196][cell % 4];
    }
    for iteration in 0..4 {
        let mut a = vec![0x96; world.width * world.height];
        let mut b = a.clone();
        original(&mut a, &world, &elements);
        update(&mut b, &world, &elements);
        assert_eq!(a, b, "table change {iteration}");
        elements.elements[68].record.default_mass *= 0.5;
        elements.elements[132].record.light_absorption += 0.0625;
    }
}

#[test]
fn unused_materials_do_not_raise_exceptions() {
    let _restore = crate::float_env::FloatEnv::capture().install();
    let (mut world, mut elements) = fixture(129, 71, "gas");
    world.write.mass.fill(0.5);
    elements.elements[4].record.default_mass = 1.0;
    elements.elements[4].record.light_absorption = 0.0;
    for (index, element) in elements.elements.iter_mut().enumerate() {
        if index != 4 {
            element.record.default_mass = 0.0;
            element.record.light_absorption = f32::from_bits(0x7f80_0001);
        }
    }
    let mut a = vec![0x96; world.width * world.height];
    let mut b = a.clone();
    let mode = 0x1f80_u32;
    let mut flags = [0_u32; 2];
    for (index, (output, implementation)) in [
        (&mut a, original as fn(&mut [u8], &PhysicsWorld, &ElementTable)),
        (&mut b, update as fn(&mut [u8], &PhysicsWorld, &ElementTable)),
    ].into_iter().enumerate() {
        unsafe {
            std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &mode, options(nostack, preserves_flags));
        }
        implementation(black_box(output), black_box(&world), black_box(&elements));
        unsafe {
            std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut flags[index], options(nostack, preserves_flags));
        }
    }
    assert_eq!(a, b);
    assert_eq!(flags, [mode; 2]);
}

#[test]
fn matches_original_ieee_words_and_fp_environment() {
    for pattern in ["mixed", "override"] {
        check_ieee_words_and_fp_environment(pattern);
    }
}

fn check_ieee_words_and_fp_environment(pattern: &str) {
    let _restore = crate::float_env::FloatEnv::capture().install();
    let edge = [
        0, 0x80000000, 1, 0x00800000, 0x3f800000, 0xbf800000, 0x7f7fffff, 0x7f800000, 0xff800000,
        0x7fc01234, 0x7f801234,
    ];
    let (mut world, mut elements) = fixture(65, 19, pattern);
    for cell in 0..world.write.mass.len() {
        world.write.mass[cell] = f32::from_bits(edge[cell % edge.len()]);
    }
    let mut a = vec![0x96; world.width * world.height];
    let mut b = a.clone();
    for controls in 0..16_u32 {
        for (i, &bits) in edge.iter().enumerate() {
            for (j, record) in elements.elements.iter_mut().enumerate() {
                record.record.default_mass = f32::from_bits(bits);
                record.record.light_absorption = f32::from_bits(edge[(i + j) % edge.len()]);
            }
            let mut raw = 0_u32;
            unsafe {
                std::arch::asm!("stmxcsr [{p}]",p=in(reg)&mut raw,options(nostack,preserves_flags));
            }
            let mode = ((raw | 0x1f80) & !0xe07f)
                | ((controls & 3) << 13)
                | ((controls & 4) << 13)
                | ((controls & 8) << 3);
            unsafe {
                std::arch::asm!("ldmxcsr [{p}]",p=in(reg)&mode,options(nostack,preserves_flags));
            }
            original(black_box(&mut a), black_box(&world), black_box(&elements));
            let mut expected = 0_u32;
            unsafe {
                std::arch::asm!("stmxcsr [{p}]",p=in(reg)&mut expected,options(nostack,preserves_flags));
            }
            unsafe {
                std::arch::asm!("ldmxcsr [{p}]",p=in(reg)&mode,options(nostack,preserves_flags));
            }
            update(black_box(&mut b), black_box(&world), black_box(&elements));
            let mut actual = 0_u32;
            unsafe {
                std::arch::asm!("stmxcsr [{p}]",p=in(reg)&mut actual,options(nostack,preserves_flags));
            }
            assert_eq!(a, b, "controls={controls} mass={bits:x}");
            assert_eq!(expected, actual, "controls={controls} mass={bits:x}");
        }
    }
}

#[test]
#[ignore = "explicit single-core microbenchmark, not part of correctness tests"]
fn benchmark_sun_indexing() {
    const BLOCKS: usize = 17;
    const SAMPLES: usize = 2 * BLOCKS;
    println!(
        "SUN_CSV,pattern,width,height,original_us,candidate_us,speedup,original_samples_us,candidate_samples_us"
    );
    let mut gate = true;
    for pattern in ["gas", "mixed", "vacuum", "blocked", "override", "split"] {
        for (width, height) in [(17, 19), (96, 96), (256, 384), (736, 304)] {
            let (world, elements) = fixture(width, height, pattern);
            let mut a = vec![0; width * height];
            let mut b = a.clone();
            let mut samples: [Vec<f64>; 2] = std::array::from_fn(|_| Vec::new());
            let repeat = (262_144 / (width * height)).clamp(1, 200);
            for _ in 0..8 {
                original(black_box(&mut a), black_box(&world), black_box(&elements));
                update(black_box(&mut b), black_box(&world), black_box(&elements));
            }
            assert_eq!(a, b);
            for block in 0..BLOCKS {
                let order = if block % 2 == 0 { [0, 1, 1, 0] } else { [1, 0, 0, 1] };
                for kind in order {
                    let start = Instant::now();
                    for _ in 0..repeat {
                        if kind == 0 {
                            original(black_box(&mut a), black_box(&world), black_box(&elements));
                            black_box(&a);
                        } else {
                            update(black_box(&mut b), black_box(&world), black_box(&elements));
                            black_box(&b);
                        }
                    }
                    samples[kind].push(start.elapsed().as_secs_f64() * 1e6 / repeat as f64);
                    assert_eq!(a, b);
                }
            }
            assert_eq!(a, b);
            let sorted = samples.clone().map(|mut x| {
                x.sort_by(f64::total_cmp);
                x
            });
            let medians = sorted.map(|x| (x[SAMPLES / 2 - 1] + x[SAMPLES / 2]) * 0.5);
            let ratio = medians[0] / medians[1];
            // Fixed before timing: dense scenes must improve >=10%; meaningful
            // zero/block/override/overlap controls may not regress >3%.
            // The tiny 17x19 case is reported, without a timing admission gate.
            if width >= 96 {
                let minimum = if matches!(pattern, "gas" | "mixed") { 1.0 / 0.90 } else { 1.0 / 1.03 };
                gate &= ratio >= minimum;
            }
            let series = |x: &[f64]| {
                x.iter()
                    .map(|v| format!("{v:.6}"))
                    .collect::<Vec<_>>()
                    .join(";")
            };
            println!(
                "SUN_CSV,{pattern},{width},{height},{:.6},{:.6},{ratio:.6},{},{}",
                medians[0],
                medians[1],
                series(&samples[0]),
                series(&samples[1])
            );
        }
    }
    assert!(
        gate,
        "fixed sunlight locality benchmark gates failed"
    );
}
