//! Check the restricted history proof against complete production image updates.

use super::*;

fn table() -> ElementTable {
    let entries = [(0x2d39bf75_u32, 0_u8), (0x6d95058c, 3), (0xa9360b34, 3),
        (10, 1), (11, 2), (12, 2), (13, 2)];
    let mut bytes = (entries.len() as i32).to_le_bytes().to_vec();
    for (index, (hash, phase)) in entries.into_iter().enumerate() {
        let mut raw = [0_u8; 164];
        raw[..4].copy_from_slice(&hash.to_le_bytes());
        raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        raw[10] = phase;
        bytes.extend_from_slice(&raw);
    }
    bytes.resize(bytes.len() + entries.len() * 4, 0);
    let mut table = ElementTable::parse(&bytes).unwrap();
    for (index, element) in table.elements.iter_mut().enumerate() {
        let record = &mut element.record;
        record.color = 0x0012_3456_u32.wrapping_mul(index as u32 + 1);
        record.material_properties = 0xa000_0000 | index as u32;
        record.gradient_count = 3;
        record.gradient = [0x0022_99dd, 0x8044_bbff, 0xff99_ddff, 0, 0, 0];
        for color in &mut record.gradient[..3] {
            *color = (*color & 0xff00_0000)
                | (color.wrapping_add(index as u32 * 0x0003_0107) & 0x00ff_ffff);
        }
        record.low_temperature = 273.15;
        record.high_temperature = 373.15;
        record.low_transition = u16::MAX;
        record.high_transition = 3;
        record.default_mass = if record.state & 3 == 2 { 1000.0 } else { 1.0 };
        record.light_absorption = 0.2;
    }
    table
}

// Different successful updates create legal prior image/cache histories. The
// last stage includes three liquid species, gas/solid cells and alpha surfaces.
fn paint(world: &mut PhysicsWorld, stage: usize) {
    world.write.element.fill(world.boundary_element);
    world.write.mass.fill(0.0);
    world.write.temperature.fill(300.0);
    world.write.properties.fill(0);
    world.read.properties.fill(0);
    world.mixture_element.fill(world.vacuum_element);
    world.mixture_mass.fill(0.0);
    world.extra_mixtures.clear();
    world.flow.fill([0.0; 4]);
    for public in 0..world.width * world.height {
        let cell = world.internal_index(public);
        let element = match stage {
            0 => 4,
            1 => 3,
            _ => [4, 5, 6, 3, 1][public % 5],
        };
        world.write.element[cell] = element;
        world.write.mass[cell] = match stage {
            0 => 400.0,
            1 => 0.5,
            _ => 70.0 + (public % 7) as f32 * 90.0,
        };
        world.write.temperature[cell] = 270.0 + ((public + stage * 3) % 11) as f32 * 11.0;
        world.read.properties[cell] = if public % 4 == 0 { 2 } else { 0 };
        world.flow[cell] = [stage as f32, public as f32, 2.0, 1.0];
        if stage == 2 && element >= 4 {
            world.mixture_element[cell] = 4 + (element - 4 + 1) % 3;
            world.mixture_mass[cell] = 19.0;
            world.extra_mixtures.insert(cell, vec![
                (4 + (element - 4 + 2) % 3, 11.0), (3, 2.0), (element, 8.0),
            ]);
        }
    }
    world.read.element.copy_from_slice(&world.write.element);
}

fn fixture(width: usize, height: usize) -> (PhysicsWorld, ElementTable) {
    let table = table();
    let mut world = PhysicsWorld::new(width, height, 7, &table).unwrap();
    world.world_offsets = vec![[0, 0, width as i32, height as i32]];
    paint(&mut world, 0);
    (world, table)
}

fn assert_final_row_zero(images: &PresentationBuffers, width: usize, height: usize) {
    let final_row = width * (height - 1)..width * height;
    for image in [&images.liquid, &images.liquid_data, &images.material] {
        assert!(image[final_row.clone()].iter().all(|&pixel| pixel == 0));
    }
}

#[test]
fn warmed_full_images_ignore_prior_cache_and_written_row_history() {
    let mut saw_distinct_history = false;
    for (width, height) in [(1, 1), (3, 1), (5, 1), (1, 2), (3, 2), (4, 5), (5, 4)] {
        let (mut world, table) = fixture(width, height);
        let mut first = PresentationBuffers::new(width * height).unwrap();
        let mut second = PresentationBuffers::new(width * height).unwrap();
        let pointers = [first.pointers(), second.pointers()];
        first.update(&world, &table).unwrap();
        paint(&mut world, 1);
        second.update(&world, &table).unwrap();
        saw_distinct_history |= !first.same_image_bytes(&second);

        paint(&mut world, 2);
        first.update(&world, &table).unwrap();
        second.update(&world, &table).unwrap();
        assert!(first.same_exposed_history(&second));
        assert!(first.same_image_bytes(&second), "shape {width}x{height}");
        assert_eq!([first.pointers(), second.pointers()], pointers);
        assert_final_row_zero(&first, width, height);
        assert_final_row_zero(&second, width, height);
    }
    assert!(saw_distinct_history, "the warmups must not begin with identical images");
}

#[test]
fn retained_exposed_pixels_are_the_remaining_warmed_history() {
    let (mut world, table) = fixture(5, 6);
    let mut first = PresentationBuffers::new(30).unwrap();
    let mut second = PresentationBuffers::new(30).unwrap();
    first.update(&world, &table).unwrap();
    paint(&mut world, 1);
    second.update(&world, &table).unwrap();
    assert_ne!(first.exposed, second.exposed);

    paint(&mut world, 2);
    world.world_offsets = vec![[0, 0, 2, 6]];
    first.update(&world, &table).unwrap();
    second.update(&world, &table).unwrap();
    assert!(!first.same_exposed_history(&second));
    assert!(!first.same_image_bytes(&second));
    world.world_offsets = vec![[0, 0, 5, 6]];
    first.update(&world, &table).unwrap();
    second.update(&world, &table).unwrap();
    assert!(first.same_exposed_history(&second));
    assert!(first.same_image_bytes(&second));

    // Deliberately violate the warmed-input precondition. The cheap history
    // helper must not be mistaken for the independent full-content oracle.
    second.flow[0][0] = f32::from_bits(first.flow[0][0].to_bits() ^ 0x8000_0000);
    assert!(first.same_exposed_history(&second));
    assert!(!first.same_image_bytes(&second));
}

#[test]
fn same_area_reshape_requires_new_images_and_is_rejected_after_start() {
    use crate::frames_data::GameData;
    use crate::frames_plan::ALLOCATE;
    use crate::messages::execute;
    use crate::state::State;
    use std::sync::Arc;

    fn allocate(state: &mut State, width: i32, height: i32) -> Result<*mut std::ffi::c_void> {
        let mut bytes = width.to_le_bytes().to_vec();
        bytes.extend_from_slice(&height.to_le_bytes());
        bytes.extend_from_slice(&[0, 1]);
        execute(state, ALLOCATE, bytes.len(), 1, &bytes)
    }
    let mut state = State::default();
    state.initialize(None).unwrap();
    state.elements = Some(Arc::new(table()));
    allocate(&mut state, 3, 4).unwrap();
    let world = state.world.as_mut().unwrap();
    paint(world, 0);
    world.world_offsets = vec![[0, 0, 3, 4]];
    let mut previous = GameData::new(state.grid.as_ref().unwrap()).unwrap();
    previous.presentation.update(world, state.elements.as_ref().unwrap()).unwrap();
    assert_ne!(previous.presentation.liquid[8], 0,
        "this pixel is a written row in the previous shape");
    let previous_generation = state.generation;

    allocate(&mut state, 4, 3).unwrap();
    assert!(state.generation > previous_generation);
    assert!(state.presentation_update.is_none());
    let world = state.world.as_mut().unwrap();
    paint(world, 2);
    world.world_offsets = vec![[0, 0, 4, 3]];
    let mut fresh = GameData::new(state.grid.as_ref().unwrap()).unwrap();
    assert_eq!(fresh.presentation_revision, 0);
    assert!(fresh.presentation.liquid.iter().all(|&pixel| pixel == 0));
    fresh.presentation.update(world, state.elements.as_ref().unwrap()).unwrap();
    assert_final_row_zero(&fresh.presentation, 4, 3);
    assert_ne!(fresh.presentation.pointers(), previous.presentation.pointers());

    state.grid.as_mut().unwrap().initialized = true;
    state.start_worker(None).unwrap();
    let worker = state.worker.as_ref().unwrap().handle();
    let mut ready = worker.wait_ready().unwrap();
    state.finish_prepare(ready.data_mut()).unwrap();
    let metadata = state.presentation_update.unwrap();
    let error = allocate(&mut state, 3, 4).err().expect("started shape must be fixed");
    assert!(matches!(error.status, Status::InvalidState));
    assert_eq!(state.presentation_update.unwrap(), metadata);
    assert_final_row_zero(&ready.data().presentation, 4, 3);
    ready.release().unwrap();
}

#[cfg(target_arch = "x86_64")]
#[test]
fn warmed_full_images_match_at_fixed_fp_controls_with_different_sticky_flags() {
    fn csr() -> u32 {
        let mut value = 0;
        unsafe { std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut value,
            options(nostack, preserves_flags)); }
        value
    }
    fn install(value: u32) {
        unsafe { std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &value,
            options(nostack, preserves_flags)); }
    }
    let _restore = crate::float_env::FloatEnv::capture().install();
    let saved = csr();
    let words = [0_u32, 0x8000_0000, 1, 0x007f_ffff, 0x0080_0000,
        0x3f80_0000, 0xbf80_0000, 0x4479_ffff, 0x447a_0000, 0x7f7f_ffff,
        0x7f80_0000, 0xff80_0000, 0x7fc0_1234, 0xffc0_5678, 0x7f80_1234];
    let (mut world, mut table) = fixture(5, 6);
    paint(&mut world, 2);
    for public in 0..30 {
        let cell = world.internal_index(public);
        world.write.mass[cell] = f32::from_bits(words[public % words.len()]);
        world.write.temperature[cell] = f32::from_bits(words[(public + 3) % words.len()]);
        world.mixture_mass[cell] = f32::from_bits(words[(public + 7) % words.len()]);
        world.flow[cell] = std::array::from_fn(|i| f32::from_bits(words[(public + i) % words.len()]));
    }
    let mut first = PresentationBuffers::new(30).unwrap();
    let mut second = PresentationBuffers::new(30).unwrap();
    for controls in 0..16_u32 {
        let mode = ((saved | 0x1f80) & !0xe07f) | ((controls & 3) << 13)
            | ((controls & 4) << 13) | ((controls & 8) << 3);
        install(mode);
        first.update(&world, &table).unwrap();
        assert_eq!(csr() & !0x3f, mode & !0x3f);
        install(mode | 0x3f);
        second.update(&world, &table).unwrap();
        assert_eq!(csr() & !0x3f, mode & !0x3f);
        assert!(first.same_exposed_history(&second), "controls={controls}");
        assert!(first.same_image_bytes(&second), "controls={controls}");
    }

    // Safely select the unmasked-exception scalar paths: finite inputs and no
    // zero-length gradient interval avoid invalid operations while invalid is
    // unmasked. Inexact and the other exception classes remain masked.
    paint(&mut world, 0);
    for element in &mut table.elements { element.record.gradient_count = 0; }
    let mode = (saved | 0x1f80) & !(0xe07f | 0x80);
    install(mode);
    first.update(&world, &table).unwrap();
    second.update(&world, &table).unwrap();
    assert_eq!(csr() & !0x3f, mode & !0x3f);
    assert_eq!(csr() & 1, 0);
    assert!(first.same_exposed_history(&second));
    assert!(first.same_image_bytes(&second));
}
