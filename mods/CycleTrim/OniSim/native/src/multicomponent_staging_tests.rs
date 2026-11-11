//! Reused buffers must behave like fresh buffers across changing inputs.
use super::*;

fn fresh_prepare(world: &mut PhysicsWorld, elements: &ElementTable, region: Region,
    gpu_available: bool, minimum_gpu_cells: usize) -> Option<(GasRegion, GasStaging)> {
    let mut staging = GasStaging::default();
    let region = staging.prepare(world, elements, region, gpu_available, minimum_gpu_cells)?;
    Some((region, staging))
}

fn fixture(
    width: usize,
    height: usize,
    channels: usize,
    pattern: &str,
) -> (PhysicsWorld, ElementTable, Region) {
    let mut descriptions = vec![(0x2d39bf75_u32, 0_u8), (0x6d95058c, 3), (0xa9360b34, 3)];
    descriptions.extend((0..18).map(|index| (10 + index, 1)));
    descriptions.push((100, 2));
    let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
    for (index, &(hash, phase)) in descriptions.iter().enumerate() {
        let mut record = [0u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[12..16].copy_from_slice(&(1.0 + index as f32 * 0.125).to_le_bytes());
        record[16..20].copy_from_slice(&(0.01 + index as f32 * 0.001).to_le_bytes());
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + descriptions.len() * 4, 0);
    let elements = ElementTable::parse(&bytes).unwrap();
    let mut world = PhysicsWorld::new(width + 1, height + 1, 7, &elements).unwrap();
    for y in 0..height {
        for x in 0..width {
            let cell = (y + 1) * world.stride + x + 1;
            let local = y * width + x;
            let channel = x * channels / width;
            world.write.element[cell] = (3 + channel) as u16;
            world.write.mass[cell] = 1.0 + (local % 31) as f32 * 0.01;
            world.mixture_element[cell] = (3 + (channel + 1) % channels) as u16;
            world.mixture_mass[cell] = 0.4 + (local % 13) as f32 * 0.02;
            world.write.temperature[cell] = 290.0 + (local % 39) as f32;
            if pattern == "empty" {
                world.write.mass[cell] = 0.0;
                world.mixture_mass[cell] = 0.0;
            } else if pattern == "walls" || local % 17 == 0 {
                world.write.properties[cell] = 1;
            } else if pattern == "mixed" {
                if local % 11 == 0 {
                    world.write.element[cell] = 0;
                    world.write.mass[cell] = 0.0;
                    world.mixture_mass[cell] = 0.0;
                } else if local % 13 == 0 {
                    world.write.element[cell] = 21;
                } else if local % 19 == 0 {
                    world.mixture_element[cell] = u16::MAX;
                }
            } else if pattern == "edge" {
                let values = [
                    f32::NAN,
                    f32::INFINITY,
                    f32::NEG_INFINITY,
                    -1.0,
                    -0.0,
                    0.0,
                    f32::from_bits(1),
                    1e-7,
                    1e-6,
                    2e-6,
                    1.0,
                ];
                if local % 5 == 0 {
                    world.write.mass[cell] = values[local % values.len()];
                }
                if local % 7 == 0 {
                    world.mixture_mass[cell] = values[(local / 7) % values.len()];
                }
                if local % 11 == 0 {
                    world.write.temperature[cell] = values[(local / 11) % values.len()];
                }
            }
        }
    }
    let region = Region {
        min_x: 1,
        min_y: 1,
        max_x: width as i32 + 1,
        max_y: height as i32 + 1,
        ..Region::default()
    };
    (world, elements, region)
}

fn same_floats(actual: &[f32], expected: &[f32], label: &str) {
    assert_eq!(actual.len(), expected.len(), "{label} length");
    assert_eq!(
        actual.iter().map(|v| v.to_bits()).collect::<Vec<_>>(),
        expected.iter().map(|v| v.to_bits()).collect::<Vec<_>>(),
        "{label} bits"
    );
}

fn same_inputs(actual: &GasStaging, expected: &GasStaging, region: &GasRegion) {
    assert_eq!(actual.local_elements, expected.local_elements);
    assert_eq!(
        actual.local_channel_of_element,
        expected.local_channel_of_element
    );
    assert_eq!(actual.solid, expected.solid);
    for (name, a, b) in [
        ("mass", &actual.mass, &expected.mass),
        ("enthalpy", &actual.enthalpy, &expected.enthalpy),
        ("temperature", &actual.temperature, &expected.temperature),
        (
            "conductivity",
            &actual.species_conductivity,
            &expected.species_conductivity,
        ),
    ] {
        same_floats(a, b, name);
    }
    if region.use_gpu {
        assert!(actual.velocity_x.is_empty() && actual.velocity_y.is_empty());
        assert!(
            expected
                .velocity_x
                .iter()
                .chain(&expected.velocity_y)
                .all(|&v| v == 0.0)
        );
    } else {
        same_floats(&actual.velocity_x, &expected.velocity_x, "velocity_x");
        same_floats(&actual.velocity_y, &expected.velocity_y, "velocity_y");
    }
    let packed = |values: &[onisim_gpu::multicomponent::PackedSparseCell]| {
        values
            .iter()
            .map(|v| {
                (
                    v.first,
                    v.second,
                    v.first_mass.to_bits(),
                    v.second_mass.to_bits(),
                    v.temperature.to_bits(),
                )
            })
            .collect::<Vec<_>>()
    };
    assert_eq!(packed(&actual.sparse_cells), packed(&expected.sparse_cells));
    if region.sparse {
        assert_eq!(actual.masks, expected.masks);
    }
    let species = |values: &[SpeciesInfo]| {
        values
            .iter()
            .map(|s| {
                (
                    s.element_id,
                    s.specific_heat.to_bits(),
                    s.vertical_drift.to_bits(),
                    s.conductivity.to_bits(),
                )
            })
            .collect::<Vec<_>>()
    };
    assert_eq!(species(&actual.species), species(&expected.species));
    let gpu_species = |values: &[onisim_gpu::multicomponent::SpeciesInfo]| {
        values
            .iter()
            .map(|s| {
                (
                    s.element_id,
                    s.phase,
                    s.specific_heat_capacity.to_bits(),
                    s.vertical_drift.to_bits(),
                )
            })
            .collect::<Vec<_>>()
    };
    assert_eq!(
        gpu_species(&actual.gpu_species),
        gpu_species(&expected.gpu_species)
    );
    assert_eq!(actual.next_mass.len(), expected.next_mass.len());
    assert_eq!(actual.next_enthalpy.len(), expected.next_enthalpy.len());
    assert_eq!(
        actual.next_temperature.len(),
        expected.next_temperature.len()
    );
    assert_eq!(actual.projected.len(), expected.projected.len());
}

fn storage_receipt(staging: &GasStaging, label: &str) {
    fn bytes<T>(values: &Vec<T>) -> usize {
        values.capacity() * std::mem::size_of::<T>()
    }
    macro_rules! fields {
        ($($field:ident),+ $(,)?) => {{
            let mut total = 0;
            $(let capacity_bytes = bytes(&staging.$field);
              total += capacity_bytes;
              println!("gas_staging_storage,{label},{},{}", stringify!($field), capacity_bytes);)+
            println!("gas_staging_storage,{label},total,{total}");
        }};
    }
    fields!(
        local_elements,
        active_elements,
        local_channel_of_element,
        sparse_cells,
        masks,
        mass,
        enthalpy,
        temperature,
        solid,
        velocity_x,
        velocity_y,
        species,
        gpu_species,
        species_conductivity,
        next_mass,
        next_enthalpy,
        next_temperature,
        projected
    );
}

#[test]
fn reused_staging_matches_fresh_packing_across_regions_and_backends() {
    let mut staging = GasStaging::default();
    for (width, height) in [(128, 64), (8, 8), (17, 19), (96, 32), (3, 7)] {
        for channels in [2, 3, 4, 8, 16, 17, 1] {
            for pattern in ["bands", "mixed", "edge", "empty", "walls"] {
                for gpu_available in [true, false] {
                    let (mut world, elements, region) = fixture(width, height, channels, pattern);
                    let expected =
                        fresh_prepare(&mut world, &elements, region, gpu_available, 256);
                    let actual = staging.prepare(&mut world, &elements, region, gpu_available, 256);
                    assert_eq!(
                        actual.is_some(),
                        expected.is_some(),
                        "{width}x{height} {channels} channels {pattern} gpu={gpu_available}"
                    );
                    if let (Some(actual), Some((expected_region, expected))) = (actual, expected) {
                        assert_eq!(actual, expected_region);
                        same_inputs(&staging, &expected, &actual);
                    }
                }
            }
        }
    }
}

#[test]
fn warmed_staging_reuses_large_buffers_and_fully_overwrites_changed_inputs() {
    let mut staging = GasStaging::default();
    for channels in [2, 4, 16] {
        let (mut world, elements, region) = fixture(736, 304, channels, "bands");
        staging
            .prepare(&mut world, &elements, region, true, 256)
            .unwrap();
        // The two catalogue vectors swap ownership. Warm both capacities first.
        staging
            .prepare(&mut world, &elements, region, true, 256)
            .unwrap();
        let addresses = |s: &GasStaging| {
            [
                s.mass.as_ptr() as usize,
                s.sparse_cells.as_ptr() as usize,
                s.temperature.as_ptr() as usize,
                s.solid.as_ptr() as usize,
                s.masks.as_ptr() as usize,
                s.next_mass.as_ptr() as usize,
                s.projected.as_ptr() as usize,
            ]
        };
        let before = addresses(&staging);
        for cell in 0..world.write.mass.len() {
            world.write.mass[cell] *= 0.75;
            world.mixture_mass[cell] *= 1.25;
            world.write.temperature[cell] += 15.0;
            if cell % 7 == 0 {
                world.write.properties[cell] ^= 1;
            }
        }
        let actual = staging
            .prepare(&mut world, &elements, region, true, 256)
            .unwrap();
        let (expected_region, expected) =
            fresh_prepare(&mut world, &elements, region, true, 256).unwrap();
        assert_eq!(actual, expected_region);
        same_inputs(&staging, &expected, &actual);
        assert_eq!(addresses(&staging), before);
        drop(std::mem::take(&mut staging.solid));
        storage_receipt(
            &staging,
            &format!("736x304-{channels}-cumulative-after-mask-transfer"),
        );
    }
}

#[test]
fn staging_reuse_preserves_cpu_results_and_diffusion_mask_ownership() {
    let (mut actual, elements, region) = fixture(8, 8, 2, "bands");
    let (mut expected, _, _) = fixture(8, 8, 2, "bands");
    let mut staging = GasStaging::default();
    for tick in 0..8 {
        let mut actual_mask = Vec::new();
        let mut expected_mask = Vec::new();
        let mut actual_events = EventQueues::default();
        let mut expected_events = EventQueues::default();
        let reused = step_region_prepared(
            &mut actual,
            &elements,
            &mut actual_events,
            region,
            None,
            false,
            Some(&mut actual_mask),
            true,
            256,
            &mut staging,
        )
        .unwrap();
        let fresh = step_region_with_dispatch(
            &mut expected,
            &elements,
            &mut expected_events,
            region,
            None,
            false,
            Some(&mut expected_mask),
            true,
            256,
        )
        .unwrap();
        assert_eq!(reused, fresh);
        assert_eq!(actual_mask, expected_mask);
        assert!(
            staging.solid.is_empty(),
            "the caller owns the published mask"
        );
        assert_eq!(actual.write.element, expected.write.element);
        assert_eq!(actual.mixture_element, expected.mixture_element);
        same_floats(&actual.write.mass, &expected.write.mass, "updated mass");
        same_floats(
            &actual.mixture_mass,
            &expected.mixture_mass,
            "updated mixture",
        );
        same_floats(
            &actual.write.temperature,
            &expected.write.temperature,
            "updated temperature",
        );
        let cell = (2 + tick % 3) * actual.stride + 3;
        actual.write.properties[cell] ^= 1;
        expected.write.properties[cell] ^= 1;
    }
}

#[test]
#[ignore = "manual paired host staging benchmark; no GPU dispatch"]
fn benchmark_gas_staging() {
    use std::hint::black_box;
    use std::time::Instant;
    let quick = std::env::var_os("CYCLETRIM_STAGING_BENCH_QUICK").is_some();
    let blocks = if quick { 5 } else { 17 };
    println!(
        "gas_staging_sample,width,height,channels,pattern,block,position,variant,iterations,ns"
    );
    let mut failures = Vec::new();
    let mut cases = Vec::new();
    for (width, height) in [(96, 96), (256, 384), (736, 304)] {
        for channels in [2, 4, 16] {
            cases.push((width, height, channels, "bands"));
        }
    }
    cases.extend([
        (8, 8, 2, "bands"),
        (96, 96, 1, "bands"),
        (736, 304, 2, "walls"),
        (736, 304, 2, "empty"),
        (96, 96, 2, "cpu"),
    ]);
    for (width, height, channels, pattern) in cases {
        let gpu_available = pattern != "cpu";
        let (mut world, elements, region) = fixture(width, height, channels, pattern);
        let mut staging = GasStaging::default();
        let iterations = (1_000_000 / (width * height)).clamp(3, 50);
        for _ in 0..5 {
            black_box(fresh_prepare(
                &mut world,
                &elements,
                region,
                gpu_available,
                256,
            ));
            if gpu_available {
                let prepared = black_box(staging.prepare(&mut world, &elements, region, true, 256));
                if prepared.is_some() {
                    // The real caller owns and later drops the diffusion mask.
                    black_box(std::mem::take(&mut staging.solid));
                }
            } else {
                let mut temporary = GasStaging::default();
                black_box(temporary.prepare(&mut world, &elements, region, false, 256));
                black_box(temporary);
            }
        }
        let mut samples = [Vec::new(), Vec::new()];
        for block in 0..blocks {
            let order = if block % 2 == 0 {
                [0, 1, 1, 0]
            } else {
                [1, 0, 0, 1]
            };
            for (position, variant) in order.into_iter().enumerate() {
                let started = Instant::now();
                for _ in 0..iterations {
                    if variant == 0 {
                        black_box(fresh_prepare(
                            black_box(&mut world),
                            black_box(&elements),
                            region,
                            gpu_available,
                            256,
                        ));
                    } else if gpu_available {
                        let prepared = black_box(staging.prepare(
                            black_box(&mut world),
                            black_box(&elements),
                            region,
                            true,
                            256,
                        ));
                        if prepared.is_some() {
                            black_box(std::mem::take(&mut staging.solid));
                        }
                        black_box(&staging);
                    } else {
                        let mut temporary = GasStaging::default();
                        black_box(temporary.prepare(
                            black_box(&mut world),
                            black_box(&elements),
                            region,
                            false,
                            256,
                        ));
                        black_box(temporary);
                    }
                }
                let elapsed = started.elapsed().as_nanos() as f64 / iterations as f64;
                samples[variant].push(elapsed);
                println!(
                    "gas_staging_sample,{width},{height},{channels},{pattern},{block},{position},{variant},{iterations},{elapsed:.1}"
                );
            }
        }
        let expected = fresh_prepare(&mut world, &elements, region, gpu_available, 256);
        let actual = staging.prepare(&mut world, &elements, region, gpu_available, 256);
        assert_eq!(actual.is_some(), expected.is_some());
        if let (Some(actual), Some((expected_region, expected))) = (actual, expected) {
            assert_eq!(actual, expected_region);
            same_inputs(&staging, &expected, &actual);
        }
        if width == 736 && height == 304 && pattern == "bands" {
            drop(std::mem::take(&mut staging.solid));
            storage_receipt(
                &staging,
                &format!("736x304-{channels}-fresh-after-mask-transfer"),
            );
        }
        for side in &mut samples {
            side.sort_by(f64::total_cmp);
        }
        let median = |values: &[f64]| {
            let mid = values.len() / 2;
            (values[mid - 1] + values[mid]) * 0.5
        };
        let old = median(&samples[0]);
        let new = median(&samples[1]);
        let reduction = 100.0 * (1.0 - new / old);
        println!(
            "gas_staging_summary,{width},{height},{channels},{pattern},old_ns={old:.1},new_ns={new:.1},reduction_pct={reduction:.3}"
        );
        let required = if width == 736 && height == 304 && pattern == "bands" {
            5.0
        } else {
            -3.0
        };
        if reduction < required {
            failures.push(format!(
                "{width}x{height} {channels} {pattern}: {reduction:.3}% < {required}%"
            ));
        }
    }
    if !quick {
        assert!(
            failures.is_empty(),
            "host staging performance gate failed: {failures:?}"
        );
    }
}

#[test]
fn later_liquid_component_clears_partially_packed_gas() {
    for gpu_available in [false, true] {
        let (mut world, elements, region) = fixture(8, 8, 2, "bands");
        let cell = 4 * world.stride + 4;
        world.extra_mixtures.insert(cell, vec![(5, 0.2), (21, 1.0)]);
        let mut staging = GasStaging::default();
        let info = staging.prepare(&mut world, &elements, region, gpu_available, 0).unwrap();
        let local = (4 - info.min_y) * info.width + 4 - info.min_x;
        assert_eq!(staging.solid[local], 1);
        let range = local * info.channels..(local + 1) * info.channels;
        assert!(staging.mass[range.clone()].iter().all(|&mass| mass == 0.0));
        if !gpu_available {
            assert!(staging.enthalpy[range].iter().all(|&energy| energy == 0.0));
        }
    }
}

#[test]
fn parallel_packing_matches_serial_species_order_and_every_tensor_bit() {
    for channels in [1, 2, 3, 16] {
        for pattern in ["mixed", "edge", "walls", "empty"] {
            let (mut world, elements, region) = fixture(96, 64, channels, pattern);
            let cell = world.stride * 17 + 23;
            world.extra_mixtures.insert(cell, vec![(20, 0.25), (19, 0.125), (20, 0.0625)]);
            let mut serial = GasStaging::default();
            let mut parallel = GasStaging::default();
            let a = serial.prepare(&mut world, &elements, region, false, 256);
            let b = parallel.prepare(&mut world, &elements, region, true, 256);
            assert_eq!(a.is_some(), b.is_some(), "{channels} {pattern}");
            assert_eq!(serial.local_elements, parallel.local_elements);
            assert_eq!(serial.local_channel_of_element, parallel.local_channel_of_element);
            assert_eq!(serial.solid, parallel.solid);
            same_floats(&serial.mass, &parallel.mass, "parallel mass");
            same_floats(&serial.temperature, &parallel.temperature, "parallel temperature");
        }
    }
}
