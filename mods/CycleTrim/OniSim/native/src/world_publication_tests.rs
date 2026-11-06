use super::*;
use std::hint::black_box;
use std::time::Instant;

fn table() -> ElementTable {
    let mut bytes = 3_i32.to_le_bytes().to_vec();
    for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c]
        .into_iter()
        .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 12]);
    ElementTable::parse(&bytes).unwrap()
}

fn next(state: &mut u32) -> u32 {
    *state ^= *state << 13;
    *state ^= *state >> 17;
    *state ^= *state << 5;
    *state
}

fn fill_world(world: &mut PhysicsWorld, seed: u32, uniform: bool) {
    let mut state = seed;
    for cell in 0..world.write.element.len() {
        let word = if uniform { 0 } else { next(&mut state) };
        world.write.element[cell] = (word % 3) as u16;
        world.write.temperature[cell] = f32::from_bits(word);
        world.write.mass[cell] = f32::from_bits(word.rotate_left(3));
        world.write.properties[cell] = (word >> 4) as u8;
        world.write.insulation[cell] = (word >> 8) as u8;
        world.write.strength[cell] = (word >> 12) as u8;
        world.write.radiation[cell] = f32::from_bits(word.rotate_left(7));
        world.accumulated_flow[cell] = f32::from_bits(word.rotate_left(11));
        world.write.disease_idx[cell] = (word >> 16) as u8;
        world.write.disease_count[cell] = word.rotate_left(13) as i32;
        world.write.disease_infestation[cell] = (word >> 20) as u8;
        world.write.disease_growth[cell] = f32::from_bits(word.rotate_left(17));
        world.backwall_element[cell] = ((word >> 24) % 3) as u16;
        world.backwall_mass[cell] = f32::from_bits(word.rotate_left(19));
        world.backwall_temperature[cell] = f32::from_bits(word.rotate_left(23));
    }
}

fn populated_grid(width: usize, height: usize, seed: u32) -> Grid {
    let mut grid = Grid::new(width as i32, height as i32, 1, 1)
        .unwrap_or_else(|_| panic!("could not create fixture grid"));
    let mut state = seed;
    let mut image: Vec<_> = (0..grid.image_len())
        .map(|_| next(&mut state) as u8)
        .collect();
    image[..4].copy_from_slice(&(width as i32).to_le_bytes());
    image[4..8].copy_from_slice(&(height as i32).to_le_bytes());
    image[8..12].copy_from_slice(&0x12345678_u32.to_le_bytes());
    grid.load_image(&image);
    grid.started = true;
    grid.set_num_frames(17);
    for index in 0..5 {
        grid.set_texture_pointer(index, 0x12340000 + index * 256);
    }
    grid.set_event_span(0, 3, 0x56780000);
    grid
}

// Exact previous production implementation, retained as the baseline.
fn original_publish(world: &PhysicsWorld, grid: &mut Grid) {
    for public in 0..grid.len() {
        let internal = world.internal_index(public);
        grid.element[public] = world.write.element[internal];
        grid.temperature[public] = world.write.temperature[internal];
        grid.mass[public] = world.write.mass[internal];
        grid.properties[public] = world.write.properties[internal];
        grid.insulation[public] = world.write.insulation[internal];
        grid.strength[public] = world.write.strength[internal];
        grid.radiation[public] = world.write.radiation[internal];
        grid.accumulated_flow[public] = world.accumulated_flow[internal];
        grid.disease_idx[public] = world.write.disease_idx[internal];
        grid.disease_count[public] = world.write.disease_count[internal];
        grid.set_disease_runtime(
            public,
            world.write.disease_infestation[internal],
            world.write.disease_growth[internal],
        );
        grid.backwall_element[public] = world.backwall_element[internal];
        grid.backwall_mass[public] = world.backwall_mass[internal];
        grid.backwall_temperature[public] = world.backwall_temperature[internal];
    }
}

fn image(grid: &Grid) -> Vec<u8> {
    let mut image = vec![0; grid.image_len()];
    grid.write_image(&mut image);
    image
}

fn descriptor(grid: &Grid) -> Vec<u8> {
    // Grid::descriptor always points to its owned DESCRIPTOR_SIZE allocation.
    unsafe { std::slice::from_raw_parts(grid.descriptor(), crate::grid::DESCRIPTOR_SIZE).to_vec() }
}

fn compare(expected: &Grid, actual: &Grid) {
    // Save images include all cell/backwall padding and disease reserved bytes.
    assert_eq!(image(expected), image(actual));
    assert_eq!(
        expected
            .radiation
            .iter()
            .map(|x| x.to_bits())
            .collect::<Vec<_>>(),
        actual
            .radiation
            .iter()
            .map(|x| x.to_bits())
            .collect::<Vec<_>>()
    );
    assert_eq!(
        expected
            .accumulated_flow
            .iter()
            .map(|x| x.to_bits())
            .collect::<Vec<_>>(),
        actual
            .accumulated_flow
            .iter()
            .map(|x| x.to_bits())
            .collect::<Vec<_>>()
    );
}

#[test]
fn publication_preserves_all_fields_and_reserved_bytes() {
    let elements = table();
    for (width, height) in [
        (1, 1),
        (1, 17),
        (17, 1),
        (3, 5),
        (17, 19),
        (96, 96),
        (257, 383),
    ] {
        let mut world = PhysicsWorld::new(width, height, 7, &elements)
            .unwrap_or_else(|_| panic!("could not create fixture world"));
        let mut expected = populated_grid(width, height, 19);
        let mut actual = populated_grid(width, height, 19);
        let before = descriptor(&actual);
        for seed in [0x12345678, 0xaabbccdd] {
            fill_world(&mut world, seed, false);
            original_publish(&world, &mut expected);
            world.publish_to(&mut actual);
            compare(&expected, &actual);
            assert_eq!(
                descriptor(&actual),
                before,
                "publication changed descriptor or addresses"
            );
            assert_eq!(
                (
                    actual.seed,
                    actual.radiation_enabled,
                    actual.headless,
                    actual.initialized,
                    actual.started
                ),
                (0x12345678, 1, 1, true, true)
            );
        }
    }
}

#[test]
fn publication_preserves_explicit_ieee_payloads_and_padded_boundaries() {
    let elements = table();
    let mut world = PhysicsWorld::new(11, 7, 7, &elements)
        .unwrap_or_else(|_| panic!("could not create fixture world"));
    fill_world(&mut world, 7, false);
    let words = [
        0, 0x80000000, 1, 0x00800000, 0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc01234, 0xffc05678,
        0x7f801234,
    ];
    for cell in 0..world.write.mass.len() {
        let value = f32::from_bits(words[cell % words.len()]);
        world.write.mass[cell] = value;
        world.write.temperature[cell] = value;
        world.write.radiation[cell] = value;
        world.accumulated_flow[cell] = value;
        world.write.disease_growth[cell] = value;
        world.backwall_mass[cell] = value;
        world.backwall_temperature[cell] = value;
    }
    let mut expected = populated_grid(11, 7, 31);
    let mut actual = populated_grid(11, 7, 31);
    original_publish(&world, &mut expected);
    world.publish_to(&mut actual);
    compare(&expected, &actual);
}

#[test]
#[ignore = "targeted production publication benchmark; run after correctness tests"]
fn benchmark_publication() {
    const WARMUP: usize = 5;
    const PAIRS: usize = 41;
    let elements = table();
    let mut no_regression = true;
    let mut improvement = false;
    println!(
        "PUBHEADER,pattern,width,height,baseline_median_us,candidate_median_us,speedup,baseline_min_us,baseline_p95_us,candidate_min_us,candidate_p95_us,baseline_samples_us,candidate_samples_us"
    );
    for pattern in ["raw", "uniform"] {
        for (width, height) in [
            (1, 1),
            (3, 5),
            (17, 19),
            (96, 96),
            (256, 384),
            (257, 383),
            (512, 512),
            (1024, 1024),
        ] {
            let mut world = PhysicsWorld::new(width, height, 7, &elements)
                .unwrap_or_else(|_| panic!("could not create fixture world"));
            fill_world(&mut world, 0x12345678, pattern == "uniform");
            let mut baseline = populated_grid(width, height, 19);
            let mut candidate = populated_grid(width, height, 19);
            original_publish(&world, &mut baseline);
            world.publish_to(&mut candidate);
            compare(&baseline, &candidate);
            let repeat = (250_000 / (width * height)).clamp(1, 1000);
            let mut samples: [Vec<f64>; 2] = std::array::from_fn(|_| Vec::new());
            for pair in 0..WARMUP + PAIRS {
                for offset in 0..2 {
                    let kind = (pair + offset) % 2;
                    let start = Instant::now();
                    for _ in 0..repeat {
                        if kind == 0 {
                            original_publish(black_box(&world), black_box(&mut baseline));
                            black_box(&baseline);
                        } else {
                            black_box(&world).publish_to(black_box(&mut candidate));
                            black_box(&candidate);
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
            if width >= 96 {
                no_regression &= speedup >= 1.0 / 1.03;
                improvement |= speedup >= 1.10;
            }
            let series = |v: &[f64]| {
                v.iter()
                    .map(|x| format!("{x:.6}"))
                    .collect::<Vec<_>>()
                    .join(";")
            };
            println!(
                "PUBCASE,{pattern},{width},{height},{:.6},{:.6},{speedup:.6},{:.6},{:.6},{:.6},{:.6},{},{}",
                a[PAIRS / 2],
                b[PAIRS / 2],
                a[0],
                a[(PAIRS - 1) * 95 / 100],
                b[0],
                b[(PAIRS - 1) * 95 / 100],
                series(&samples[0]),
                series(&samples[1])
            );
        }
    }
    println!("PUBGATE,no_regression={no_regression},improvement={improvement}");
    assert!(
        no_regression && improvement,
        "publication performance gate failed"
    );
}
