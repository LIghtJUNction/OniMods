//! Differential oracle: host preparation from b570414d, before staging reuse.
//! It freezes the production packing body and prepares the original output vectors.
use super::*;

fn original_prepare(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    region: Region,
    gpu_available: bool,
    minimum_gpu_cells: usize,
) -> Option<(GasRegion, GasStaging)> {
    if world.gas_channel_count == 0 {
        rebuild_gas_channels(world, elements);
    }
    let channels = world.gas_channel_count;
    if channels == 0 {
        return None;
    }

    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(world.width);
    let max_y = (region.max_y as usize).min(world.height);
    if max_x <= min_x || max_y <= min_y {
        return None;
    }
    let width = max_x - min_x;
    let height = max_y - min_y;
    if gpu_available && (width < 2 || height < 2) {
        return None;
    }
    let count = width * height;

    // Map each region's active gases independently of the global table.
    // Stencils that would exceed the two-slot cell representation are still
    // isolated before transport.
    let mut local_elements: Vec<u16> =
        world.gas_element_of_channel[..world.gas_channel_count].to_vec();
    let mut local_projection = false;
    if gpu_available {
        let mut active = Vec::new();
        'scan: for y in min_y..max_y {
            for x in min_x..max_x {
                let cell = y * world.stride + x;
                for (id, mass) in [
                    (world.write.element[cell], world.write.mass[cell]),
                    (world.mixture_element[cell], world.mixture_mass[cell]),
                ] {
                    if mass <= 0.0
                        || !mass.is_finite()
                        || !elements
                            .elements
                            .get(id as usize)
                            .is_some_and(|e| e.record.state & 3 == 1)
                    {
                        continue;
                    }
                    if !active.contains(&id) {
                        active.push(id);
                        if active.len() > MAX_REGION_GASES {
                            break 'scan;
                        }
                    }
                }
            }
        }
        if active.len() < 2 {
            return None;
        }
        if active.len() <= MAX_REGION_GASES {
            local_elements = active;
            local_projection = true;
        }
    }
    let channels = local_elements.len();
    let mut local_channel_of_element = vec![usize::MAX; elements.elements.len()];
    for (channel, &id) in local_elements.iter().enumerate() {
        local_channel_of_element[id as usize] = channel;
    }

    // Packed sparse cells use 16 bytes, equal to three dense channels plus
    // temperature, and avoid expanding the channel tensor on the CPU.
    let use_gpu = gpu_available && count >= minimum_gpu_cells;
    let sparse = use_gpu && channels >= 3 && local_projection;
    let mut sparse_cells = if sparse {
        vec![onisim_gpu::multicomponent::PackedSparseCell::default(); count]
    } else {
        Vec::new()
    };
    let mut masks = if sparse {
        vec![0u16; count]
    } else {
        Vec::new()
    };
    let mut any_mixture = false;
    let mut mass = if sparse {
        Vec::new()
    } else {
        vec![0.0f32; count * channels]
    };
    let mut enthalpy = if use_gpu {
        Vec::new()
    } else {
        vec![0.0f32; count * channels]
    };
    let mut temperature = if sparse {
        Vec::new()
    } else {
        vec![1.0f32; count]
    };
    let mut solid = vec![0u32; count];
    // Sparse GPU input generates velocity on the device; no host arrays are read.
    let mut velocity_x = if sparse {
        Vec::new()
    } else {
        vec![0.0f32; count]
    };
    let mut velocity_y = if sparse {
        Vec::new()
    } else {
        vec![0.0f32; count]
    };
    let mut species = vec![
        SpeciesInfo {
            element_id: 0,
            vertical_drift: 0.0,
            specific_heat: 1.0,
            conductivity: 0.0,
        };
        channels
    ];
    for channel in 0..channels {
        let element_id = local_elements[channel];
        let record = &elements.elements[element_id as usize].record;
        species[channel] = SpeciesInfo {
            element_id,
            vertical_drift: 0.0,
            specific_heat: record.specific_heat_capacity,
            conductivity: record.thermal_conductivity,
        };
    }

    for y in 0..height {
        for x in 0..width {
            let public_y = min_y + y;
            let public_x = min_x + x;
            let cell = public_y * world.stride + public_x;
            let local = y * width + x;
            let cell_temperature = world.write.temperature[cell].max(1.0);
            if sparse {
                sparse_cells[local].temperature = cell_temperature;
            } else {
                temperature[local] = cell_temperature;
            }
            let impermeable = world.write.properties[cell] & 1 != 0;
            let phase = elements.elements[world.write.element[cell] as usize]
                .record
                .state
                & 3;
            // The dense tensor cannot represent every element yet. Treat any
            // cell with an unmapped phase/component as a wall for this pass;
            // projecting a partial tensor would otherwise delete its mass.
            let primary_channel = local_channel_of_element[world.write.element[cell] as usize];
            let mixture_channel = local_channel_of_element
                .get(world.mixture_element[cell] as usize)
                .copied()
                .unwrap_or(usize::MAX);
            let primary_unsupported =
                world.write.mass[cell] > 0.0 && (phase != 1 || primary_channel == usize::MAX);
            let mixture_unsupported =
                world.mixture_mass[cell] > 0.0 && mixture_channel == usize::MAX;
            if impermeable || primary_unsupported || mixture_unsupported {
                solid[local] = 1;
                continue;
            }
            let base = local * channels;
            if sparse {
                let m1 = world.write.mass[cell];
                let m2 = world.mixture_mass[cell];
                let has_m1 = m1 > 1e-6 && m1.is_finite();
                let has_m2 = m2 > 1e-6 && m2.is_finite();
                let first = if has_m1 { primary_channel } else { 0 };
                let second = if has_m2 { mixture_channel } else { first };
                let first_m = if has_m1 { m1 } else { 0.0 };
                let second_m = if has_m2 { m2 } else { 0.0 };
                sparse_cells[local] = onisim_gpu::multicomponent::PackedSparseCell {
                    first: first as u16,
                    second: second as u16,
                    first_mass: first_m,
                    second_mass: second_m,
                    temperature: cell_temperature,
                };
                if first_m > 0.0 {
                    masks[local] |= 1u16 << first;
                    any_mixture = true;
                }
                if second_m > 0.0 {
                    masks[local] |= 1u16 << second;
                    any_mixture = true;
                }
                continue;
            }
            if world.write.mass[cell] > 0.0 && phase == 1 {
                mass[base + primary_channel] = world.write.mass[cell];
                any_mixture = true;
            }
            if world.mixture_mass[cell] > 0.0 {
                mass[base + mixture_channel] += world.mixture_mass[cell];
                any_mixture = true;
            }
            if !use_gpu {
                for channel in 0..channels {
                    let m = mass[base + channel].max(0.0);
                    enthalpy[local * channels + channel] =
                        m * temperature[local] * species[channel].specific_heat;
                }
            }
        }
    }

    if sparse {
        isolate_stencil_masks(&masks, &mut solid, width, height, 2);
    } else {
        isolate_unrepresentable_stencils(
            &mut mass,
            &mut enthalpy,
            &mut solid,
            width,
            height,
            channels,
            2,
        );
    }
    if sparse {
        if !masks
            .iter()
            .zip(&solid)
            .any(|(&mask, &blocked)| mask != 0 && blocked == 0)
        {
            return None;
        }
    } else if !mass.iter().any(|&value| value > 0.0) {
        return None;
    }
    if !use_gpu {
        density_gradient_velocities(
            &mass,
            &solid,
            width,
            height,
            channels,
            &mut velocity_x,
            &mut velocity_y,
        );
    }

    // Even a pure gas region needs its deferred gas-to-gas thermal faces.
    if !any_mixture {
        return None;
    }

    let packed_gpu = use_gpu && channels >= 3;
    let next_mass = if packed_gpu {
        Vec::new()
    } else {
        vec![0.0; mass.len()]
    };
    let projected = if packed_gpu {
        vec![Default::default(); count]
    } else {
        Vec::new()
    };
    let next_enthalpy = if use_gpu {
        Vec::new()
    } else {
        vec![0.0; mass.len()]
    };
    let next_temperature = if use_gpu && !packed_gpu {
        vec![0.0; count]
    } else {
        Vec::new()
    };
    let gpu_species = if use_gpu {
        species
            .iter()
            .map(|s| onisim_gpu::multicomponent::SpeciesInfo {
                element_id: s.element_id,
                phase: 1,
                specific_heat_capacity: s.specific_heat,
                vertical_drift: s.vertical_drift,
            })
            .collect()
    } else {
        Vec::new()
    };
    let species_conductivity = if use_gpu {
        species.iter().map(|s| s.conductivity).collect()
    } else {
        Vec::new()
    };
    Some((
        GasRegion {
            min_x,
            min_y,
            width,
            height,
            channels,
            use_gpu,
            sparse,
            local_projection,
        },
        GasStaging {
            local_elements,
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
            projected,
            next_enthalpy,
            next_temperature,
            active_elements: Vec::new(),
        },
    ))
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
fn reused_staging_matches_frozen_packing_across_regions_and_backends() {
    let mut staging = GasStaging::default();
    for (width, height) in [(128, 64), (8, 8), (17, 19), (96, 32), (3, 7)] {
        for channels in [2, 3, 4, 8, 16, 17, 1] {
            for pattern in ["bands", "mixed", "edge", "empty", "walls"] {
                for gpu_available in [true, false] {
                    let (mut world, elements, region) = fixture(width, height, channels, pattern);
                    let expected =
                        original_prepare(&mut world, &elements, region, gpu_available, 256);
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
            original_prepare(&mut world, &elements, region, true, 256).unwrap();
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
            black_box(original_prepare(
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
                        black_box(original_prepare(
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
        let expected = original_prepare(&mut world, &elements, region, gpu_available, 256);
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
