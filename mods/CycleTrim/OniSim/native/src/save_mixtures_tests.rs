//! Behavioral regressions for inventories beyond the legacy display slot.
use super::*;
use crate::world::PhysicsWorld;

fn elements() -> ElementTable {
    let mut entries = vec![(0x2d39bf75_u32, 0_u8), (0x6d95058c, 3), (0xa9360b34, 0)];
    for index in 3..25 {
        entries.push((0x10000000 + index as u32, if index == 4 { 2 } else { 1 }));
    }
    let mut bytes = (entries.len() as i32).to_le_bytes().to_vec();
    for (index, &(hash, phase)) in entries.iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[12..16].copy_from_slice(&1.0_f32.to_le_bytes());
        record[52..56].copy_from_slice(&1000.0_f32.to_le_bytes());
        record[60..64].copy_from_slice(&10000.0_f32.to_le_bytes());
        record[84..88].fill(255);
        bytes.extend_from_slice(&record);
    }
    bytes.resize(bytes.len() + entries.len() * 4, 0);
    ElementTable::parse(&bytes).unwrap()
}

fn world(elements: &ElementTable, width: usize, height: usize) -> PhysicsWorld {
    let mut world = PhysicsWorld::new(width, height, 42, elements).unwrap();
    world.write.element.fill(world.vacuum_element);
    world.write.mass.fill(0.0);
    world.write.temperature.fill(0.0);
    world
}

fn fill_mixture(world: &mut PhysicsWorld, cell: usize) {
    world.write.element[cell] = 4;
    world.write.mass[cell] = f32::from_bits(0x447a0001);
    world.write.temperature[cell] = f32::from_bits(0x43965555);
    world.mixture_element[cell] = 3;
    world.mixture_mass[cell] = f32::from_bits(0x3f800001);
    world.extra_mixtures.insert(
        cell,
        (5_u16..25)
            .map(|id| (id, f32::from_bits(0x3e800001 + id as u32 * 7)))
            .collect(),
    );
}

fn inventory(world: &PhysicsWorld, elements: &ElementTable) -> Vec<(u32, Vec<(i32, u32)>)> {
    (0..world.stride * world.padded_height)
        .map(|cell| {
            let mut values = Vec::new();
            world.for_each_cell_species(cell, |id, mass| {
                values.push((elements.elements[id as usize].record.id, mass.to_bits()))
            });
            (world.write.temperature[cell].to_bits(), values)
        })
        .collect()
}

#[test]
fn mixed_inventory_roundtrip_preserves_every_species_bit() {
    let elements = elements();
    let mut original = world(&elements, 5, 3);
    let cell = original.stride + 2;
    fill_mixture(&mut original, cell);
    let before = inventory(&original, &elements);
    let image = serialize(&mut original, &elements, None, 0, 0).unwrap();
    let mut loaded = world(&elements, 5, 3);
    let plan = plan_load(&loaded, &elements, None, &image, 0).unwrap();
    plan.apply(&mut loaded).unwrap();
    assert_eq!(
        inventory(&loaded, &elements),
        before,
        "native save/load must preserve primary, second and all extra inventories"
    );
}

fn image_with_mixture() -> (ElementTable, Vec<u8>, usize) {
    let elements = elements();
    let mut source = world(&elements, 5, 3);
    let cell = source.stride + 2;
    fill_mixture(&mut source, cell);
    let base = encoded_length(15, source.stride * source.padded_height).unwrap();
    (
        elements.clone(),
        serialize(&mut source, &elements, None, 0, 0).unwrap(),
        base,
    )
}

fn resign(image: &mut [u8], base: usize) {
    // Independent bit-at-a-time reference, not the production lookup table.
    let mut crc = !0_u32;
    for (index, &byte) in image.iter().enumerate() {
        if (base + 28..base + 32).contains(&index) {
            continue;
        }
        crc ^= u32::from(byte);
        for _ in 0..8 {
            crc = if crc & 1 == 0 {
                crc >> 1
            } else {
                (crc >> 1) ^ 0xedb88320
            };
        }
    }
    image[base + 28..base + 32].copy_from_slice(&(!crc).to_le_bytes());
}

fn set_word(image: &mut [u8], offset: usize, value: u32) {
    image[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
}

#[test]
fn mixed_inventory_rejects_corruption_and_unknown_layout_without_mutation() {
    let (elements, image, base) = image_with_mixture();
    let mut target = world(&elements, 5, 3);
    let old = target.stride + 3;
    fill_mixture(&mut target, old);
    let before = inventory(&target, &elements);
    for cut in base..image.len() {
        assert!(
            plan_load(&target, &elements, None, &image[..cut], 0).is_err(),
            "truncated extension at {cut} must fail"
        );
    }
    for offset in [
        29,
        base,
        base + 8,
        base + 12,
        base + 16,
        base + 20,
        base + 24,
        base + 28,
        base + 32,
        image.len() - 1,
    ] {
        let mut damaged = image.clone();
        damaged[offset] ^= 1;
        assert!(
            plan_load(&target, &elements, None, &damaged, 0).is_err(),
            "corrupt field at {offset} must fail"
        );
    }
    // Even a self-consistent checksum cannot make invalid structure valid.
    let record = base + 32;
    let cases = [
        (base + 8, 2),
        (base + 12, 36),
        (base + 16, u32::MAX),
        (base + 20, 0),
        (base + 24, 1),
        (record, u32::MAX),
        (record + 4, 2),
        (record + 8, u32::MAX),
        (record + 12, f32::NAN.to_bits()),
        (record + 16, 0x12345678),
        (record + 20, (-1.0_f32).to_bits()),
        (record + 24, 0x12345678),
        (record + 28, f32::INFINITY.to_bits()),
        (record + 24, 0x10000004), // second duplicates the primary
        (record + 32, 0x10000003), // extra duplicates the second
        (record + 40, 0x10000005), // repeated extra
        (record + 28, 0),          // empty inventory has no additional record
    ];
    for (offset, value) in cases {
        let mut damaged = image.clone();
        set_word(&mut damaged, offset, value);
        resign(&mut damaged, base);
        assert!(
            plan_load(&target, &elements, None, &damaged, 0).is_err(),
            "invalid semantic field at {offset} must fail"
        );
    }
    let mut trailing = image.clone();
    trailing.push(0);
    assert!(plan_load(&target, &elements, None, &trailing, 0).is_err());
    assert_eq!(inventory(&target, &elements), before);
}

#[test]
fn legacy_save_has_no_extension_and_clears_only_loaded_inventory_range() {
    let elements = elements();
    let mut source = world(&elements, 2, 1);
    source.write.element[5] = 3;
    source.write.mass[5] = 1.25;
    source.write.temperature[5] = 300.0;
    let image = serialize(&mut source, &elements, None, 2, 2).unwrap();
    assert_eq!(image.len(), 29 + 36 * 4 * 3);
    // Independent v15 image construction for the zero-inventory legacy path.
    let mut expected = b"SIMSAVE\0".to_vec();
    for value in [15_i32, 4, 3, 2, 2] {
        expected.extend_from_slice(&value.to_le_bytes());
    }
    expected.push(0);
    for cell in 0..12 {
        expected.extend_from_slice(
            &elements.elements[source.write.element[cell] as usize]
                .record
                .id
                .to_le_bytes(),
        );
        expected.extend_from_slice(&source.write.temperature[cell].to_le_bytes());
        expected.extend_from_slice(&source.write.mass[cell].to_le_bytes());
        expected.extend_from_slice(&0.0_f32.to_le_bytes());
    }
    for _ in 0..12 {
        expected.extend_from_slice(&[0; 8]);
    }
    for _ in 0..12 {
        expected.extend_from_slice(&VACUUM.to_le_bytes());
        expected.extend_from_slice(&[0; 8]);
    }
    assert_eq!(
        image, expected,
        "a pure v15 save remains byte-for-byte compatible"
    );
    let mut target = world(&elements, 9, 8);
    let outside = target.stride + 1;
    fill_mixture(&mut target, outside);
    for y in 2..5 {
        for x in 2..6 {
            let cell = y * target.stride + x;
            fill_mixture(&mut target, cell);
            target.species_mass[cell * crate::multicomponent_flux::GAS_CHANNELS
                ..(cell + 1) * crate::multicomponent_flux::GAS_CHANNELS]
                .fill(99.0);
            target.dirty[cell] = false;
            target.dirty_flags[cell] = 0;
        }
    }
    let outside_before = inventory(&target, &elements)[outside].clone();
    plan_load(&target, &elements, None, &image, 0)
        .unwrap()
        .apply(&mut target)
        .unwrap();
    assert_eq!(inventory(&target, &elements)[outside], outside_before);
    for y in 2..5 {
        for x in 2..6 {
            let cell = y * target.stride + x;
            assert_eq!(target.mixture_mass[cell], 0.0);
            assert!(target.extra_mixtures.get(&cell).is_none());
            assert!(target.dirty[cell] && target.dirty_flags[cell] == 0x1f);
            let dense = &target.species_mass[cell * crate::multicomponent_flux::GAS_CHANNELS
                ..(cell + 1) * crate::multicomponent_flux::GAS_CHANNELS];
            assert!(
                !dense.contains(&99.0),
                "old staging cannot overwrite loaded inventory"
            );
        }
    }
}

#[test]
fn mixed_offset_load_remaps_hashes_and_preserves_outside_cells_and_slot_order() {
    let elements = elements();
    let mut source = world(&elements, 2, 1);
    let source_cell = source.stride + 1;
    fill_mixture(&mut source, source_cell);
    let before = inventory(&source, &elements);
    let image = serialize(&mut source, &elements, None, 2, 3).unwrap();
    let mut reordered = elements.clone();
    reordered.elements.reverse();
    for (index, element) in reordered.elements.iter_mut().enumerate() {
        element.record.index = index as u16;
    }
    let mut target = world(&reordered, 8, 8);
    let outside = target.stride + 1;
    let outside_primary = reordered
        .elements
        .iter()
        .position(|e| e.record.id == 0x10000003)
        .unwrap() as u16;
    target.write.element[outside] = outside_primary;
    target.write.mass[outside] = 0.75;
    target.write.temperature[outside] = 301.0;
    target.mixture_element[outside] = reordered
        .elements
        .iter()
        .position(|e| e.record.id == 0x10000004)
        .unwrap() as u16;
    target.mixture_mass[outside] = 0.125;
    let outside_before = inventory(&target, &reordered)[outside].clone();
    plan_load(&target, &reordered, None, &image, 0)
        .unwrap()
        .apply(&mut target)
        .unwrap();
    let after = inventory(&target, &reordered);
    assert_eq!(after[outside], outside_before);
    for local in 0..source.stride * source.padded_height {
        let target_cell = (3 + local / source.stride) * target.stride + 2 + local % source.stride;
        assert_eq!(
            after[target_cell], before[local],
            "offset local cell {local}"
        );
    }
    let loaded_cell = 4 * target.stride + 3;
    assert_eq!(
        target.extra_mixtures[&loaded_cell].len(),
        20,
        "no fixed species cap"
    );
    assert_eq!(
        target.write.temperature[loaded_cell].to_bits(),
        source.write.temperature[source_cell].to_bits()
    );
    let original_total = before[source_cell]
        .1
        .iter()
        .fold(0_f32, |sum, entry| sum + f32::from_bits(entry.1));
    let restored_total = after[loaded_cell]
        .1
        .iter()
        .fold(0_f32, |sum, entry| sum + f32::from_bits(entry.1));
    assert_eq!(restored_total.to_bits(), original_total.to_bits());
    let dense_start = loaded_cell * crate::multicomponent_flux::GAS_CHANNELS;
    target.for_each_cell_species(loaded_cell, |id, mass| {
        let channel = target.gas_channel_of_element[id as usize] as usize;
        if channel < crate::multicomponent_flux::GAS_CHANNELS {
            assert_eq!(
                target.species_mass[dense_start + channel].to_bits(),
                mass.to_bits()
            );
        }
    });
}

#[test]
fn mixture_slots_without_secondary_and_load_time_transitions_roundtrip_exactly() {
    let mut elements = elements();
    elements.elements[4].record.high_transition = 3;
    elements.elements[4].record.high_temperature = 290.0;
    let mut source = world(&elements, 3, 2);
    let cell = source.stride + 1;
    fill_mixture(&mut source, cell);
    source.mixture_mass[cell] = 0.0;
    source.mixture_element[cell] = source.vacuum_element;
    // An inactive extra is not inventory; every positive extra keeps its bits.
    source.extra_mixtures.get_mut(&cell).unwrap().push((3, 0.0));
    let expected = inventory(&source, &elements);
    let image = serialize(&mut source, &elements, None, 0, 0).unwrap();
    let mut loaded = world(&elements, 3, 2);
    plan_load(&loaded, &elements, None, &image, 0)
        .unwrap()
        .apply(&mut loaded)
        .unwrap();
    assert_eq!(inventory(&loaded, &elements), expected);
    assert_eq!(
        loaded.write.element[cell], 4,
        "legacy load transition must not alter one species of an extended inventory"
    );
    assert_eq!(loaded.mixture_mass[cell], 0.0);
    assert_eq!(loaded.extra_mixtures[&cell].len(), 20);
}

#[test]
fn invalid_source_inventory_is_not_silently_deduplicated_or_saved_partially() {
    let elements = elements();
    for kind in 0..6 {
        let mut source = world(&elements, 3, 2);
        let cell = source.stride + 1;
        fill_mixture(&mut source, cell);
        source.backwall_element[0] = INVALID_ELEMENT;
        source.backwall_mass[0] = 17.0;
        match kind {
            0 => source.mixture_element[cell] = source.write.element[cell],
            1 => source
                .extra_mixtures
                .get_mut(&cell)
                .unwrap()
                .push((3, 0.125)),
            2 => source.mixture_mass[cell] = f32::NAN,
            3 => source.extra_mixtures.get_mut(&cell).unwrap()[0].1 = -1.0,
            4 => source.mixture_element[cell] = u16::MAX,
            _ => {
                source.write.mass[cell] = f32::MAX;
                source.mixture_mass[cell] = f32::MAX;
            }
        }
        assert!(
            serialize(&mut source, &elements, None, 0, 0).is_err(),
            "invalid case {kind}"
        );
        assert_eq!(
            source.backwall_mass[0], 17.0,
            "failed extension validation cannot commit earlier save side effects"
        );
    }
}

fn state_with_world(elements: ElementTable, world: PhysicsWorld) -> crate::state::State {
    let mut state = crate::state::State::default();
    state.initialize(None).unwrap();
    state.grid = Some(
        crate::grid::Grid::new(world.width as i32, world.height as i32, 0, 0)
            .unwrap()
            .into(),
    );
    state.diseases = Some(std::sync::Arc::new(crate::tables::DiseaseTable {
        diseases: Vec::new(),
        element_count: elements.len(),
    }));
    state.elements = Some(std::sync::Arc::new(elements));
    state.world = Some(world);
    state
}

#[test]
fn mixed_load_batch_validates_every_image_before_any_commit() {
    let elements = elements();
    let mut source = world(&elements, 2, 1);
    let cell = source.stride + 1;
    fill_mixture(&mut source, cell);
    let first = serialize(&mut source, &elements, None, 0, 0).unwrap();
    source.mixture_mass[cell] = 2.5;
    let second = serialize(&mut source, &elements, None, 4, 2).unwrap();
    assert_eq!(first.len(), second.len());
    let mut destination = world(&elements, 8, 6);
    let old = destination.stride * 4 + 1;
    fill_mixture(&mut destination, old);
    let before = inventory(&destination, &elements);
    let mut state = state_with_world(elements.clone(), destination);
    let mut damaged = second.clone();
    let last = damaged.len() - 1;
    damaged[last] ^= 1;
    let invalid_batch = [first.as_slice(), damaged.as_slice()].concat();
    let result = crate::messages::execute(
        &mut state,
        crate::frames_plan::LOAD,
        first.len(),
        2,
        &invalid_batch,
    );
    assert!(result.is_err());
    assert_eq!(inventory(state.world.as_ref().unwrap(), &elements), before);
    let valid_batch = [first.as_slice(), second.as_slice()].concat();
    crate::messages::execute(
        &mut state,
        crate::frames_plan::LOAD,
        first.len(),
        2,
        &valid_batch,
    )
    .unwrap();
    let loaded = state.world.as_ref().unwrap();
    assert_eq!(
        loaded.mixture_mass[loaded.stride + 1].to_bits(),
        f32::from_bits(0x3f800001).to_bits()
    );
    assert_eq!(
        loaded.mixture_mass[3 * loaded.stride + 5].to_bits(),
        2.5_f32.to_bits()
    );
    assert_eq!(loaded.extra_mixtures[&(loaded.stride + 1)].len(), 20);
    assert_eq!(loaded.extra_mixtures[&(3 * loaded.stride + 5)].len(), 20);
}

#[test]
fn first_publication_after_load_contains_all_species_before_any_step() {
    let elements = elements();
    let mut source = world(&elements, 5, 3);
    let cell = source.stride + 2;
    fill_mixture(&mut source, cell);
    let expected = inventory(&source, &elements)[cell].1.clone();
    let image = serialize(&mut source, &elements, None, 0, 0).unwrap();
    let mut state = state_with_world(elements.clone(), world(&elements, 5, 3));
    crate::messages::execute(&mut state, crate::frames_plan::LOAD, image.len(), 1, &image).unwrap();
    assert!(
        state.engine.is_none() && state.worker.is_none(),
        "load owns CPU state before a fresh GPU/worker lifecycle is created"
    );
    state.start_worker(None).unwrap();
    let update = state
        .multicomponent_update
        .as_ref()
        .unwrap()
        .as_ref()
        .unwrap();
    assert!(crate::publication_lease::revision() > 0);
    // Cell (x=2,y=1) in padded storage is public cell 1.
    let header = unsafe { *update.cells.add(1) };
    let records = unsafe {
        std::slice::from_raw_parts(
            update.species.add(header.species_offset as usize),
            header.species_count as usize,
        )
    };
    let actual: Vec<_> = records
        .iter()
        .map(|record| {
            (
                elements.elements[record.element_id as usize].record.id,
                record.mass.to_bits(),
            )
        })
        .collect();
    assert_eq!(
        actual, expected,
        "first publication cannot fall back to the dominant-only Grid image"
    );
    let total = expected
        .iter()
        .fold(0_f32, |sum, entry| sum + f32::from_bits(entry.1));
    assert_eq!(header.total_mass.to_bits(), total.to_bits());
    let revision = crate::publication_lease::revision();
    assert!(
        crate::messages::execute(&mut state, crate::frames_plan::LOAD, image.len(), 1, &image)
            .is_err(),
        "hot loading into a live GPU/worker is not supported"
    );
    assert_eq!(crate::publication_lease::revision(), revision);
    state.worker.as_mut().unwrap().shutdown().unwrap();
    crate::publication_lease::clear().unwrap();
}

#[test]
fn inactive_primary_and_small_positive_inventory_do_not_round_or_disappear() {
    let elements = elements();
    let mut original = world(&elements, 3, 2);
    let cell = original.stride + 1;
    original.write.mass[cell] = -0.0;
    original.write.temperature[cell] = 0.0;
    original.mixture_element[cell] = 3;
    original.mixture_mass[cell] = f32::from_bits(1);
    original
        .extra_mixtures
        .insert(cell, vec![(4, f32::from_bits(0x00800001))]);
    let before = inventory(&original, &elements);
    let image = serialize(&mut original, &elements, None, 0, 0).unwrap();
    let mut target = world(&elements, 3, 2);
    plan_load(&target, &elements, None, &image, 0)
        .unwrap()
        .apply(&mut target)
        .unwrap();
    assert_eq!(inventory(&target, &elements), before);
    assert_eq!(target.write.mass[cell].to_bits(), (-0.0_f32).to_bits());
    assert_eq!(target.mixture_mass[cell].to_bits(), 1);
}

#[test]
fn required_extension_marker_and_unknown_elements_never_downgrade_to_legacy() {
    let (elements, image, base) = image_with_mixture();
    let target = world(&elements, 5, 3);
    assert_eq!(
        i32::from_le_bytes(image[8..12].try_into().unwrap()),
        0x4f4d0001
    );
    assert!(plan_load(&target, &elements, None, &image[..base], 0).is_err());
    let mut unmarked = image.clone();
    set_word(&mut unmarked, 8, 15);
    resign(&mut unmarked, base);
    assert!(plan_load(&target, &elements, None, &unmarked, 0).is_err());
    let mut unknown_version = image.clone();
    set_word(&mut unknown_version, 8, 0x4f4d0002);
    assert!(matches!(
        plan_load(&target, &elements, None, &unknown_version, 0)
            .err()
            .unwrap()
            .status,
        Status::Unsupported
    ));
    let mut changed_table = elements.clone();
    changed_table.elements[3].record.id = 0x12345678;
    assert!(
        plan_load(&target, &changed_table, None, &image, 0).is_err(),
        "missing secondary element cannot silently become vacuum"
    );
    changed_table = elements.clone();
    changed_table.elements[4].record.id = 0x12345678;
    assert!(
        plan_load(&target, &changed_table, None, &image, 0).is_err(),
        "missing primary element cannot silently become vacuum"
    );
}

#[test]
fn overlapping_mixed_load_batch_replaces_prior_inventory_without_residue() {
    let elements = elements();
    let mut source = world(&elements, 2, 1);
    let first_cell = source.stride + 1;
    let second_cell = source.stride + 2;
    fill_mixture(&mut source, first_cell);
    let first = serialize(&mut source, &elements, None, 0, 0).unwrap();
    source.clear_cell_mixtures(first_cell);
    source.write.element[first_cell] = source.vacuum_element;
    source.write.mass[first_cell] = 0.0;
    source.write.temperature[first_cell] = 0.0;
    fill_mixture(&mut source, second_cell);
    source.mixture_mass[second_cell] = 2.75;
    source
        .extra_mixtures
        .get_mut(&second_cell)
        .unwrap()
        .reverse();
    let expected = inventory(&source, &elements);
    let second = serialize(&mut source, &elements, None, 0, 0).unwrap();
    assert_eq!(first.len(), second.len());
    let mut target = world(&elements, 2, 1);
    fill_mixture(&mut target, first_cell);
    fill_mixture(&mut target, second_cell);
    let mut state = state_with_world(elements.clone(), target);
    let batch = [first.as_slice(), second.as_slice()].concat();
    crate::messages::execute(&mut state, crate::frames_plan::LOAD, first.len(), 2, &batch).unwrap();
    let loaded = state.world.as_ref().unwrap();
    assert_eq!(inventory(loaded, &elements), expected);
    assert!(loaded.extra_mixtures.get(&first_cell).is_none());
    assert_eq!(loaded.mixture_mass[first_cell], 0.0);
    assert_eq!(loaded.extra_mixtures[&second_cell].len(), 20);
}
