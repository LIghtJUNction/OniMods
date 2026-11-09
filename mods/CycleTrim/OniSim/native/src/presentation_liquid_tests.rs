// Frozen complete liquid update and helpers from 1f4151e.
mod original {
    //! Independent liquid-image equations from official ELF 0x646f0..0x64dd0.

    use crate::numeric::{cvtt_f32_i32, smax, smin};
    use crate::tables::{ElementRecord, ElementTable};
    use crate::world::PhysicsWorld;

    const MASS_SCALE: f32 = f32::from_bits(0x3a83126f);
    const MASS_ALPHA_LIMIT: f32 = f32::from_bits(0x4479ffff);
    const EXPONENT: f32 = f32::from_bits(0x3ee66666);
    const BYTE_SCALE: f32 = f32::from_bits(0x3b808081);

    pub(super) fn update(
        liquid: &mut [u32],
        liquid_data: &mut [u32],
        material: &mut [u32],
        exposed: &[u8],
        world: &PhysicsWorld,
        elements: &ElementTable,
    ) {
        // Officially the last public row retains its zero constructor image.
        for y in 0..world.height.saturating_sub(1) {
            for x in 0..world.width {
                let public = y * world.width + x;
                let cell = world.internal_index(public);
                let element = record(world, elements, cell);
                if element.state & 3 != 2 {
                    liquid[public] = 0;
                    liquid_data[public] = 0;
                    material[public] = 0;
                    continue;
                }
                let above = cell + world.stride;
                let mass = world.write.mass[cell];
                let alpha = if (world.read.properties[above] | record(world, elements, above).state)
                    & 2
                    == 0
                    && !(mass > MASS_ALPHA_LIMIT)
                {
                    cvtt_f32_i32((mass * MASS_SCALE).powf(EXPONENT) * 255.0) as u32
                } else {
                    255
                };
                let gradient = estimated_gradient(cell, true, exposed, world, elements);
                // 真实多组分混合液体视觉渲染：将所有组分液体的颜色按质量占比物理混合（支持任意数量混合）！
                let mut total_liquid_mass = 0.0f32;
                let mut total_r = 0.0f32;
                let mut total_g = 0.0f32;
                let mut total_b = 0.0f32;

                world.for_each_cell_species(cell, |elem_id, sp_mass| {
                    if sp_mass <= 0.0 || elem_id as usize >= elements.elements.len() {
                        return;
                    }
                    let rec = &elements.elements[elem_id as usize].record;
                    if rec.state & 3 != 2 {
                        return;
                    }
                    let sp_rgb = gradient_color(rec, gradient) & 0x00ff_ffff;
                    total_liquid_mass += sp_mass;
                    total_r += sp_mass * (sp_rgb & 0xff) as f32;
                    total_g += sp_mass * ((sp_rgb >> 8) & 0xff) as f32;
                    total_b += sp_mass * ((sp_rgb >> 16) & 0xff) as f32;
                });

                let rgb = if total_liquid_mass > 0.0 {
                    let r = ((total_r / total_liquid_mass).round() as u32).min(255);
                    let g = ((total_g / total_liquid_mass).round() as u32).min(255);
                    let b = ((total_b / total_liquid_mass).round() as u32).min(255);
                    (r & 0xff) | ((g & 0xff) << 8) | ((b & 0xff) << 16)
                } else {
                    gradient_color(element, gradient) & 0x00ff_ffff
                };
                liquid[public] = (alpha << 24) | rgb;
                liquid_data[public] =
                    transition_color(element, world.write.temperature[cell], elements);
                material[public] = element.material_properties & 0xff00_000f;
            }
        }
    }

    fn record<'a>(
        world: &PhysicsWorld,
        elements: &'a ElementTable,
        cell: usize,
    ) -> &'a ElementRecord {
        &elements.elements[world.write.element[cell] as usize].record
    }

    fn estimated_gradient(
        cell: usize,
        check_above: bool,
        exposed: &[u8],
        world: &PhysicsWorld,
        elements: &ElementTable,
    ) -> f32 {
        let state = record(world, elements, cell).state;
        if state & 3 != 2 {
            return if state & 2 != 0 { 1.0 } else { 0.0 };
        }
        let above = cell + world.stride;
        if record(world, elements, above).state & 2 == 0 {
            return 0.0;
        }
        let public = world.public_index(cell).unwrap();
        let value = if exposed[public] == 255 {
            0.0
        } else {
            (1.0 - exposed[public] as f32 * BYTE_SCALE)
                * smin(world.write.mass[cell] / 1000.0, 1.0).powf(EXPONENT)
        };
        if value > 0.0 && check_above {
            smin(
                estimated_gradient(above, false, exposed, world, elements),
                value,
            )
        } else {
            smin(1.0, value)
        }
    }

    fn gradient_color(record: &ElementRecord, value: f32) -> u32 {
        let mut color = record.color;
        let count = record.gradient_count as usize;
        for index in 0..count {
            let first = record.gradient[index];
            let threshold = (first >> 24) as f32 * BYTE_SCALE;
            if !(threshold <= value) {
                continue;
            }
            let second = record
                .gradient
                .get(index + 1)
                .copied()
                .filter(|_| index + 1 < count)
                .unwrap_or(first);
            let interval = if index + 1 < count {
                (second >> 24) as f32 * BYTE_SCALE - threshold
            } else {
                0.0
            };
            let fraction = smax(smin(1.0, (value - threshold) / interval), 0.0);
            color = lerp_color(first, second, fraction);
        }
        color
    }

    fn lerp_color(first: u32, second: u32, fraction: f32) -> u32 {
        let mut color = 0;
        for shift in [0, 8, 16, 24] {
            let a = if shift == 24 {
                (first as i32) >> 24
            } else {
                ((first >> shift) & 255) as i32
            };
            let b = if shift == 24 {
                (second as i32) >> 24
            } else {
                ((second >> shift) & 255) as i32
            };
            let channel = cvtt_f32_i32((b - a) as f32 * fraction).wrapping_add(a);
            color |= (channel as u32).wrapping_shl(shift);
        }
        color
    }

    fn transition_color(record: &ElementRecord, temperature: f32, elements: &ElementTable) -> u32 {
        let fraction = ((3.0 + temperature) - record.low_temperature)
            / ((6.0 + record.high_temperature) - record.low_temperature);
        let alpha = if fraction < 0.0 {
            0
        } else if fraction > 1.0 {
            0xff00_0000
        } else {
            (cvtt_f32_i32(255.0 * fraction) as u32) << 24
        };
        // COMISS/JB selects the low transition for an unordered fraction too.
        let transition = if fraction >= 0.5 {
            record.high_transition
        } else {
            record.low_transition
        };
        let color = if transition == u16::MAX {
            0x00ff_ffff
        } else {
            elements.elements[transition as usize].record.color & 0x00ff_ffff
        };
        alpha | color
    }
}

use super::*;
use std::{hint::black_box, time::Instant};

#[derive(Clone)]
struct Images {
    liquid: Vec<u32>,
    data: Vec<u32>,
    material: Vec<u32>,
}

impl Images {
    fn new(count: usize) -> Self {
        Self {
            liquid: vec![0x1234_5678; count],
            data: vec![0x89ab_cdef; count],
            material: vec![0x7654_3210; count],
        }
    }

    fn assert_matches(&self, other: &Self) {
        for (name, a, b) in [
            ("liquid", &self.liquid, &other.liquid),
            ("data", &self.data, &other.data),
            ("material", &self.material, &other.material),
        ] {
            assert_eq!(a.len(), b.len());
            assert_eq!(
                a.iter().zip(b).enumerate().find(|(_, (a, b))| a != b),
                None,
                "first differing {name} pixel"
            );
        }
    }
}

fn table() -> ElementTable {
    let descriptions = [
        (0x2d39bf75_u32, 0_u8),
        (0x6d95058c, 3),
        (0xa9360b34, 3),
        (10, 1),
        (11, 2),
        (12, 2),
        (13, 2),
    ];
    let mut bytes = (descriptions.len() as i32).to_le_bytes().to_vec();
    for (index, (hash, state)) in descriptions.into_iter().enumerate() {
        let mut raw = [0_u8; 164];
        raw[..4].copy_from_slice(&hash.to_le_bytes());
        raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        raw[10] = state;
        bytes.extend_from_slice(&raw);
    }
    bytes.resize(bytes.len() + descriptions.len() * 4, 0);
    let mut elements = ElementTable::parse(&bytes).unwrap();
    for (i, element) in elements.elements.iter_mut().enumerate() {
        let record = &mut element.record;
        record.color = 0x8020_4070_u32.wrapping_mul(i as u32 + 1);
        record.material_properties = 0xa13c_5670 | i as u32;
        record.gradient_count = 3;
        record.gradient = [0x0022_99dd, 0x8044_bbff, 0xff99_ddff, 0, 0, 0];
        for color in &mut record.gradient[..3] {
            *color =
                (*color & 0xff00_0000) | (color.wrapping_add(i as u32 * 0x0003_0107) & 0x00ff_ffff);
        }
        record.low_temperature = 273.15;
        record.high_temperature = 373.15;
        record.low_transition = if i % 2 == 0 { u16::MAX } else { 1 };
        record.high_transition = if i % 2 == 0 { 3 } else { u16::MAX };
        record.default_mass = if record.state & 3 == 2 { 1000.0 } else { 1.0 };
        record.light_absorption = 0.15;
    }
    elements
}

fn fixture(width: usize, height: usize, pattern: &str) -> (PhysicsWorld, ElementTable, Vec<u8>) {
    let elements = table();
    let mut world = PhysicsWorld::new(width, height, 11, &elements).unwrap();
    world.write.element.fill(1);
    let mut random = 0x1234_5678_u32;
    for public in 0..width * height {
        let (x, y) = (public % width, public / width);
        let cell = world.internal_index(public);
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        world.write.element[cell] = match pattern {
            "no_liquid" => (public % 4) as u16,
            "mixed" => [4, 5, 3, 1, 6, 0, 3, 4][(x / 7 + y / 11) % 8],
            "surface" if y % 2 == 1 => 3,
            _ => 4 + (public % 3) as u16,
        };
        world.write.mass[cell] = (1 + random % 125_000) as f32 / 113.0;
        world.write.temperature[cell] = 265.0 + (random % 1200) as f32 / 10.0;
        world.read.properties[cell] = if public % 11 == 0 { 2 } else { 0 };
        if pattern == "multicomponent" {
            world.mixture_element[cell] = 4 + ((public + 1) % 3) as u16;
            world.mixture_mass[cell] = 5.0 + (random % 37) as f32;
            world
                .extra_mixtures
                .insert(cell, vec![(4 + ((public + 2) % 3) as u16, 15.5)]);
        }
    }
    world.read.element.copy_from_slice(&world.write.element);
    world
        .world_offsets
        .push([0, 0, width as i32, height as i32]);
    let exposed = (0..width * height).map(|i| (i * 73) as u8).collect();
    (world, elements, exposed)
}

fn reference(
    images: &mut Images,
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
    _: &mut GradientCache,
) {
    original::update(
        &mut images.liquid,
        &mut images.data,
        &mut images.material,
        exposed,
        world,
        elements,
    );
}

fn candidate(
    images: &mut Images,
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
    cache: &mut GradientCache,
) {
    update(
        &mut images.liquid,
        &mut images.data,
        &mut images.material,
        exposed,
        world,
        elements,
        cache,
    )
    .unwrap();
}

#[test]
fn images_match_original_for_shapes_surfaces_and_all_species() {
    let mut cache = GradientCache::default();
    for (width, height) in [
        (1, 1),
        (1, 5),
        (5, 1),
        (3, 5),
        (17, 19),
        (96, 96),
        (256, 384),
    ] {
        for pattern in ["liquid", "mixed", "no_liquid", "surface", "multicomponent"] {
            let (mut world, elements, exposed) = fixture(width, height, pattern);
            for (&cell, extras) in &mut world.extra_mixtures {
                extras.extend_from_slice(&[
                    (3, 7.0),
                    (u16::MAX, 3.0),
                    (world.write.element[cell], 8.0),
                    (world.mixture_element[cell], 9.0),
                    (4, f32::NAN),
                    (5, -1.0),
                ]);
            }
            let mut expected = Images::new(width * height);
            let mut actual = expected.clone();
            reference(&mut expected, &exposed, &world, &elements, &mut cache);
            candidate(&mut actual, &exposed, &world, &elements, &mut cache);
            expected.assert_matches(&actual);
            let bottom = width * height.saturating_sub(1)..width * height;
            assert!(
                actual.liquid[bottom.clone()]
                    .iter()
                    .all(|&v| v == 0x1234_5678)
            );
            assert!(
                actual.data[bottom.clone()]
                    .iter()
                    .all(|&v| v == 0x89ab_cdef)
            );
            assert!(actual.material[bottom].iter().all(|&v| v == 0x7654_3210));
        }
    }
}

#[cfg(target_arch = "x86_64")]
fn csr() -> u32 {
    let mut value = 0;
    unsafe {
        std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut value, options(nostack, preserves_flags));
    }
    value
}

#[cfg(target_arch = "x86_64")]
fn install(value: u32) {
    unsafe {
        std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &value, options(nostack, preserves_flags));
    }
}

#[cfg(target_os = "linux")]
fn errno(value: Option<i32>) -> i32 {
    unsafe extern "C" {
        fn __errno_location() -> *mut i32;
    }
    unsafe {
        let pointer = __errno_location();
        if let Some(value) = value {
            *pointer = value;
        }
        *pointer
    }
}

#[cfg(not(target_os = "linux"))]
fn errno(_: Option<i32>) -> i32 {
    0
}

#[test]
#[cfg(target_arch = "x86_64")]
fn images_match_original_ieee_words_mxcsr_and_errno() {
    let _restore = crate::float_env::FloatEnv::capture().install();
    let saved = csr();
    let edge = [
        0, 0x80000000, 1, 0x007fffff, 0x00800000, 0x3f800000, 0xbf800000, 0x4479ffff, 0x447a0000,
        0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc01234, 0xffc05678, 0x7f801234,
    ];
    let (mut world, mut elements, mut exposed) = fixture(17, 19, "multicomponent");
    let mut expected = Images::new(world.width * world.height);
    let mut actual = expected.clone();
    let mut cache = GradientCache::default();
    // Allocate before recording floating effects; subsequent calls retain this
    // same scratch allocation and must also clear values from the previous call.
    candidate(&mut actual, &exposed, &world, &elements, &mut cache);
    for controls in 0..16_u32 {
        for shift in 0..edge.len() {
            for cell in 0..world.write.element.len() {
                world.write.element[cell] = if (cell + shift) % 11 == 0 {
                    3
                } else {
                    4 + (cell % 3) as u16
                };
                world.write.mass[cell] = f32::from_bits(edge[(cell + shift) % edge.len()]);
                world.write.temperature[cell] =
                    f32::from_bits(edge[(cell * 3 + shift) % edge.len()]);
                world.mixture_mass[cell] = f32::from_bits(edge[(cell * 7 + shift) % edge.len()]);
            }
            for (i, element) in elements.elements.iter_mut().enumerate() {
                element.record.low_temperature = f32::from_bits(edge[(i + shift) % edge.len()]);
                element.record.high_temperature =
                    f32::from_bits(edge[(i * 3 + shift) % edge.len()]);
            }
            exposed.rotate_left(1);
            let mode = ((saved | 0x1f80) & !0xe07f)
                | ((controls & 3) << 13)
                | ((controls & 4) << 13)
                | ((controls & 8) << 3);
            errno(Some(37));
            install(mode);
            reference(
                black_box(&mut expected),
                black_box(&exposed),
                black_box(&world),
                black_box(&elements),
                &mut cache,
            );
            let expected_csr = csr();
            let expected_errno = errno(None);
            errno(Some(37));
            install(mode);
            candidate(
                black_box(&mut actual),
                black_box(&exposed),
                black_box(&world),
                black_box(&elements),
                &mut cache,
            );
            let actual_csr = csr();
            let actual_errno = errno(None);
            expected.assert_matches(&actual);
            assert_eq!(
                expected_csr, actual_csr,
                "MXCSR controls={controls} shift={shift}"
            );
            assert_eq!(
                expected_errno, actual_errno,
                "errno controls={controls} shift={shift}"
            );
        }
    }
}

#[test]
fn complete_presentation_restarts_cache_and_preserves_published_addresses() {
    use super::super::{PresentationBuffers, flow, sun};
    let (mut world, mut elements, _) = fixture(17, 19, "multicomponent");
    let count = world.width * world.height;
    let mut expected = PresentationBuffers::new(count).unwrap();
    let mut actual = PresentationBuffers::new(count).unwrap();
    let pointers = actual.pointers();
    actual.update(&world, &elements).unwrap();
    let cache_pointer = actual.liquid_gradients.next_row.as_ptr();
    let cache_capacity = actual.liquid_gradients.next_row.capacity();
    for turn in 0..12 {
        for public in 0..count {
            let cell = world.internal_index(public);
            world.write.mass[cell] = (public * 11 + turn * 31) as f32 / 7.0;
            world.write.element[cell] = if turn % 3 == 1 && public < world.width {
                3
            } else {
                4 + (public % 3) as u16
            };
            world.read.properties[cell] = if (public + turn) % 7 == 0 { 2 } else { 0 };
            world.mixture_mass[cell] = (turn + 1) as f32;
        }
        elements.elements[4].record.gradient.rotate_left(1);
        elements.elements[5].record.state = if turn % 4 == 0 { 1 } else { 2 };
        elements.elements[6].record.color = 0x1030_5090_u32.wrapping_mul(turn as u32 + 1);
        expected.validate(&world, &elements).unwrap();
        flow::update(
            &mut expected.flow,
            world.width,
            world.height,
            &world.read.element,
            &world.write.element,
            &world.write.mass,
            &world.flow,
        );
        sun::update(&mut expected.exposed, &world, &elements);
        original::update(
            &mut expected.liquid,
            &mut expected.liquid_data,
            &mut expected.material,
            &expected.exposed,
            &world,
            &elements,
        );
        actual.update(&world, &elements).unwrap();
        assert_eq!(expected.liquid, actual.liquid);
        assert_eq!(expected.liquid_data, actual.liquid_data);
        assert_eq!(expected.material, actual.material);
        assert_eq!(expected.exposed, actual.exposed);
        assert!(
            expected
                .flow
                .iter()
                .flatten()
                .zip(actual.flow.iter().flatten())
                .all(|(a, b)| a.to_bits() == b.to_bits())
        );
        assert_eq!(pointers, actual.pointers());
        assert_eq!(cache_pointer, actual.liquid_gradients.next_row.as_ptr());
        assert_eq!(cache_capacity, actual.liquid_gradients.next_row.capacity());
    }
}

#[test]
#[cfg(target_arch = "x86_64")]
fn unmasked_exceptions_disable_arithmetic_reuse() {
    let _restore = crate::float_env::FloatEnv::capture().install();
    let (mut world, mut elements, mut exposed) = fixture(3, 5, "liquid");
    world.write.mass.fill(1000.0);
    world.write.temperature.fill(300.0);
    exposed.fill(0);
    for element in &mut elements.elements {
        element.record.gradient_count = 0;
    }
    let mut expected = Images::new(exposed.len());
    let mut actual = expected.clone();
    let mut cache = GradientCache::default();
    let mode = (csr() | 0x1f80) & !0xbf; // Unmask invalid; keep the other exceptions masked.
    install(mode);
    reference(&mut expected, &exposed, &world, &elements, &mut cache);
    let expected_csr = csr();
    install(mode);
    candidate(&mut actual, &exposed, &world, &elements, &mut cache);
    let actual_csr = csr();
    expected.assert_matches(&actual);
    assert_eq!(expected_csr, actual_csr);
    assert!(cache.next_row.iter().all(Option::is_none));
}

#[test]
#[ignore = "manual complete liquid-update ABBA benchmark; run without concurrent benchmarks"]
fn benchmark_liquid_update() {
    type Updater = fn(&mut Images, &[u8], &PhysicsWorld, &ElementTable, &mut GradientCache);
    let updaters: [Updater; 2] = [reference, candidate];
    let quick = std::env::var_os("ONISIM_LIQUID_BENCH_QUICK").is_some();
    let (warmup, rounds, visits) = if quick {
        (1, 3, 100_000)
    } else {
        (3, 17, 1_000_000)
    };
    let mut liquid_improves = true;
    let mut no_regression = true;
    println!(
        "LIQUID_CONFIG,warmup_rounds={warmup},measured_rounds={rounds},order=alternating_ABBA_BAAB,samples_per_variant={},warm_cache_bytes_per_column={},first_allocation=before_warmup",
        2 * rounds,
        std::mem::size_of::<Option<f32>>()
    );
    println!(
        "LIQUID_HEADER,pattern,width,height,baseline_median_us,candidate_median_us,baseline_min_us,baseline_max_us,candidate_min_us,candidate_max_us,baseline_samples_us,candidate_samples_us"
    );
    for pattern in ["liquid", "mixed", "no_liquid", "multicomponent"] {
        for (width, height) in [(17, 19), (96, 96), (256, 384), (736, 304)] {
            let (world, elements, mut exposed) = fixture(width, height, pattern);
            // Sunlight is already calculated before the production liquid pass.
            super::super::sun::update(&mut exposed, &world, &elements);
            let mut images = [Images::new(width * height), Images::new(width * height)];
            let mut caches: [GradientCache; 2] = std::array::from_fn(|_| GradientCache::default());
            for kind in 0..2 {
                updaters[kind](
                    &mut images[kind],
                    &exposed,
                    &world,
                    &elements,
                    &mut caches[kind],
                );
            }
            images[0].assert_matches(&images[1]);
            let repeats = (visits / (width * height)).clamp(1, 200);
            let mut samples: [Vec<f64>; 2] = std::array::from_fn(|_| Vec::new());
            for round in 0..warmup + rounds {
                for kind in [0, 1, 1, 0].map(|kind| kind ^ (round & 1)) {
                    let update = black_box(updaters[kind]);
                    let start = Instant::now();
                    for _ in 0..repeats {
                        update(
                            black_box(&mut images[kind]),
                            black_box(&exposed),
                            black_box(&world),
                            black_box(&elements),
                            black_box(&mut caches[kind]),
                        );
                        black_box(&images[kind]);
                    }
                    if round >= warmup {
                        samples[kind].push(start.elapsed().as_secs_f64() * 1e6 / repeats as f64);
                    }
                }
            }
            images[0].assert_matches(&images[1]);
            let sorted = samples.clone().map(|mut samples| {
                samples.sort_by(f64::total_cmp);
                samples
            });
            let medians = sorted.each_ref().map(|s| (s[rounds - 1] + s[rounds]) * 0.5);
            if width >= 96 {
                if pattern == "liquid" {
                    liquid_improves &= medians[1] <= medians[0] * 0.90;
                } else {
                    no_regression &= medians[1] <= medians[0] * 1.03;
                }
            }
            println!(
                "LIQUID,{pattern},{width},{height},{:.6},{:.6},{:.6},{:.6},{:.6},{:.6},{:?},{:?}",
                medians[0],
                medians[1],
                sorted[0][0],
                sorted[0][2 * rounds - 1],
                sorted[1][0],
                sorted[1][2 * rounds - 1],
                samples[0],
                samples[1]
            );
            let checksums = images.each_ref().map(|images| {
                [&images.liquid, &images.data, &images.material].map(|image| {
                    image
                        .iter()
                        .fold(0xcbf2_9ce4_8422_2325_u64, |hash, &pixel| {
                            (hash ^ u64::from(pixel)).wrapping_mul(0x100_0000_01b3)
                        })
                })
            });
            println!(
                "LIQUID_CHECKSUM,{pattern},{width},{height},liquid_data_material,{:016x?},{:016x?}",
                checksums[0], checksums[1]
            );
        }
    }
    if !quick {
        assert!(
            liquid_improves && no_regression,
            "fixed benchmark gates: liquid improves >=10%={liquid_improves}, other scenes regress <=3%={no_regression}"
        );
    }
}
