use crate::events::EventQueues;
use crate::frames_plan::{ALLOCATE, ELEMENTS};
use crate::messages::execute;
use crate::state::State;
use crate::status::Result;
use crate::tables::DiseaseTable;
use std::sync::Arc;

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

fn send(state: &mut State, id: i32, payload: &[u8]) {
    ok(execute(state, id, payload.len(), 1, payload));
}

fn fixture() -> State {
    let mut state = State::default();
    ok(state.initialize(None));
    let mut elements = 3_i32.to_le_bytes().to_vec();
    for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c]
        .into_iter()
        .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = if index == 2 { 3 } else { 0 };
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        record[84..88].fill(255);
        elements.extend_from_slice(&record);
    }
    elements.extend_from_slice(&[0_u8; 12]); // three empty names
    send(&mut state, ELEMENTS, &elements);
    let mut allocate = 4_i32.to_le_bytes().to_vec();
    allocate.extend_from_slice(&4_i32.to_le_bytes());
    allocate.extend_from_slice(&[0, 0]);
    send(&mut state, ALLOCATE, &allocate);
    send(&mut state, crate::world_messages::CLEAR, &[]);
    state.diseases = Some(Arc::new(DiseaseTable {
        diseases: Vec::new(),
        element_count: 3,
    }));
    state.grid.as_mut().unwrap().initialized = true;
    state
}

#[test]
fn compact_load_plan_maps_offset_rows_without_touching_other_cells() {
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    world.write.mass.fill(123.0);
    world.write.radiation.fill(77.0);
    world.write.disease_count.fill(9);
    world.backwall_mass.fill(91.0);
    let mut image = b"SIMSAVE\0".to_vec();
    for value in [15_i32, 2, 2, 2, 1] {
        image.extend_from_slice(&value.to_le_bytes());
    }
    image.push(0);
    for index in 0..4 {
        image.extend_from_slice(&0x6d95058c_u32.to_le_bytes());
        for value in [
            300.0 + index as f32,
            1.0 + index as f32,
            10.0 + index as f32,
        ] {
            image.extend_from_slice(&value.to_le_bytes());
        }
    }
    image.extend_from_slice(&[0; 32]);
    for index in 0..4 {
        image.extend_from_slice(&0x2d39bf75_u32.to_le_bytes());
        image.extend_from_slice(&(50.0 + index as f32).to_le_bytes());
        image.extend_from_slice(&500_f32.to_le_bytes());
    }
    let plan = ok(crate::save::plan_load(
        world,
        &elements,
        Some(&diseases),
        &image,
        0,
    ));
    assert!(world.write.mass.iter().all(|&mass| mass == 123.0));
    plan.apply(world);
    let targets = [8, 9, 14, 15];
    for target in 0..world.write.mass.len() {
        if let Some(index) = targets.iter().position(|&cell| cell == target) {
            assert_eq!(world.write.mass[target], 1.0 + index as f32);
            assert_eq!(world.write.temperature[target], 300.0 + index as f32);
            assert_eq!(world.write.radiation[target], 10.0 + index as f32);
            assert_eq!(world.write.disease_count[target], 0);
            assert_eq!(world.backwall_mass[target], 50.0 + index as f32);
        } else {
            assert_eq!(world.write.mass[target], 123.0);
            assert_eq!(world.write.radiation[target], 77.0);
            assert_eq!(world.write.disease_count[target], 9);
            assert_eq!(world.backwall_mass[target], 91.0);
        }
    }
}

#[test]
fn compact_legacy_load_preserves_omitted_fields_and_clears_unknown_radiation() {
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    world.write.radiation[..3].copy_from_slice(&[11.0, 12.0, 13.0]);
    world.write.disease_count[..3].fill(9);
    world.backwall_mass[..3].fill(91.0);
    let mut image = b"SIMSAVE\0".to_vec();
    for value in [8_i32, 3, 1] {
        image.extend_from_slice(&value.to_le_bytes());
    }
    for hash in [0x6d95058c_u32, 0x12345678, 0x2d39bf75] {
        image.extend_from_slice(&hash.to_le_bytes());
        image.extend_from_slice(&300_f32.to_le_bytes());
        image.extend_from_slice(&1_f32.to_le_bytes());
    }
    image.extend_from_slice(&[0; 12]);
    let plan = ok(crate::save::plan_load(world, &elements, None, &image, 0));
    plan.apply(world);
    assert_eq!(&world.write.radiation[..3], &[11.0, 0.0, 0.0]);
    assert_eq!(&world.write.disease_count[..3], &[9, 9, 9]);
    assert_eq!(&world.backwall_mass[..3], &[91.0, 91.0, 91.0]);
    assert_eq!(&world.write.mass[..3], &[1.0, 0.0, 0.0]);
}

fn float_command(cell: i32, value: f32) -> Vec<u8> {
    let mut bytes = cell.to_le_bytes().to_vec();
    bytes.extend_from_slice(&value.to_le_bytes());
    bytes
}

#[test]
fn batch_load_commits_in_order_and_rejects_invalid_later_images_atomically() {
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    let target = world.internal_index(0);
    world.write.element[target] = 2;
    world.write.mass[target] = 7.0;
    let first = ok(crate::save::serialize(
        world,
        &elements,
        Some(&diseases),
        0,
        0,
    ));
    world.write.mass[target] = 11.0;
    let second = ok(crate::save::serialize(
        world,
        &elements,
        Some(&diseases),
        0,
        0,
    ));
    world.write.mass[target] = 3.0;
    let mut batch = first.clone();
    batch.extend_from_slice(&second);
    assert!(
        !ok(execute(
            &mut state,
            crate::frames_plan::LOAD,
            first.len(),
            2,
            &batch
        ))
        .is_null()
    );
    assert_eq!(state.world.as_ref().unwrap().write.mass[target], 11.0);
    batch[first.len()] = b'X';
    assert!(execute(&mut state, crate::frames_plan::LOAD, first.len(), 2, &batch).is_err());
    assert_eq!(state.world.as_ref().unwrap().write.mass[target], 11.0);
    // A later incompatible options byte must also leave the first image unapplied.
    batch[first.len()] = b'S';
    batch[28] = 1;
    batch[first.len() + 28] = 2;
    assert!(execute(&mut state, crate::frames_plan::LOAD, first.len(), 2, &batch).is_err());
    assert_eq!(state.world.as_ref().unwrap().saved_options, 0);
    assert_eq!(state.world.as_ref().unwrap().write.mass[target], 11.0);
}

#[test]
fn dispatched_cell_commands_wait_for_their_frame_and_preserve_order() {
    let mut state = fixture();
    let cell = state.world.as_ref().unwrap().internal_index(0);
    let initial = state.world.as_ref().unwrap().write.strength[cell];
    let mut batch = float_command(0, 7.0);
    batch.extend_from_slice(&float_command(0, 11.0));
    ok(execute(
        &mut state,
        crate::frames_commands::SET_STRENGTH,
        8,
        2,
        &batch,
    ));
    send(
        &mut state,
        crate::frames_commands::SET_INSULATION,
        &float_command(0, 0.5),
    );
    assert_eq!(state.world.as_ref().unwrap().write.strength[cell], initial);
    let mut events = EventQueues::default();
    ok(state.early.process_before_cells(
        state.world.as_mut().unwrap(),
        state.elements.as_deref().unwrap(),
        state.diseases.as_deref(),
        &mut events,
    ));
    let world = state.world.as_ref().unwrap();
    assert_eq!(world.write.strength[cell], 11);
    assert_eq!(world.write.insulation[cell], 127);
}

#[test]
fn invalid_later_cell_record_keeps_the_existing_queue_unchanged() {
    let mut state = fixture();
    send(
        &mut state,
        crate::frames_commands::SET_STRENGTH,
        &float_command(0, 7.0),
    );
    let mut batch = float_command(0, 99.0);
    batch.extend_from_slice(&float_command(16, 5.0)); // outside a 4x4 public grid
    assert!(
        execute(
            &mut state,
            crate::frames_commands::SET_STRENGTH,
            8,
            2,
            &batch
        )
        .is_err()
    );
    assert_eq!(state.early.len(), 1);
    ok(state.early.process_before_cells(
        state.world.as_mut().unwrap(),
        state.elements.as_deref().unwrap(),
        state.diseases.as_deref(),
        &mut EventQueues::default(),
    ));
    let world = state.world.as_ref().unwrap();
    assert_eq!(world.write.strength[world.internal_index(0)], 7);
}

#[test]
fn full_modify_cell_dispatch_emits_the_requested_callback_and_updates_material() {
    let mut state = fixture();
    let mut payload = vec![0_u8; 28];
    payload[4..8].copy_from_slice(&42_i32.to_le_bytes());
    payload[8..12].copy_from_slice(&300_f32.to_le_bytes());
    payload[12..16].copy_from_slice(&10_f32.to_le_bytes());
    payload[20..22].copy_from_slice(&2_u16.to_le_bytes());
    payload[22] = 1;
    payload[23] = 255;
    payload[24] = 1;
    send(&mut state, crate::cell_material::MODIFY, &payload);
    let mut events = EventQueues::default();
    ok(state.material.take_pending().process_modifications(
        state.world.as_mut().unwrap(),
        state.elements.as_deref().unwrap(),
        state.diseases.as_deref().unwrap(),
        &mut events,
    ));
    let world = state.world.as_ref().unwrap();
    let cell = world.internal_index(0);
    assert_eq!(world.write.element[cell], 2);
    assert_eq!(world.write.mass[cell], 10.0);
    assert_eq!(world.write.temperature[cell], 300.0);
    assert_eq!(events.callback_info.len(), 1);
    assert_eq!(events.callback_info[0].callback_idx, 42);
}

#[test]
fn zone_changes_are_queued_and_saved_options_work_after_worker_ownership() {
    let mut state = fixture();
    send(&mut state, crate::world_messages::SET_ZONES, &[3; 16]);
    let mut change = 0_i32.to_le_bytes().to_vec();
    change.extend_from_slice(&[255, 0, 0, 0]);
    send(&mut state, crate::world_messages::MODIFY_ZONE, &change);
    let world = state.world.as_mut().unwrap();
    let cell = world.internal_index(0);
    assert_eq!(world.inactive_mask.as_ref().unwrap()[cell], 3);
    ok(crate::world_messages::process_pending(
        &mut state.world_messages.take_pending(),
        world,
    ));
    assert_eq!(world.inactive_mask.as_ref().unwrap()[cell], 255);
    ok(state.start_worker(None));
    send(&mut state, crate::frames_commands::SAVED_OPTIONS, &[255, 5]);
    let engine = state.engine.as_ref().unwrap().lock().unwrap();
    assert_eq!(engine.world.saved_options, 5);
    drop(engine);
    ok(state.worker.as_mut().unwrap().shutdown());
}

#[test]
fn component_batch_registers_distinct_handles_in_callback_order() {
    let mut state = fixture();
    let mut batch = 21_i32.to_le_bytes().to_vec();
    batch.extend_from_slice(&22_i32.to_le_bytes());
    ok(execute(&mut state, 1486783027, 4, 2, &batch));
    assert_eq!(state.queued_component_commands, 2);
    let pending = state.mass.take_pending();
    let mut events = EventQueues::default();
    ok(state
        .mass
        .process_pending(pending, state.world.as_ref().unwrap(), &mut events));
    assert_eq!(events.component_state_changed_messages.len(), 2);
    assert_eq!(events.component_state_changed_messages[0].callback_idx, 21);
    assert_eq!(events.component_state_changed_messages[1].callback_idx, 22);
    assert_ne!(
        events.component_state_changed_messages[0].sim_handle,
        events.component_state_changed_messages[1].sim_handle
    );
}

#[test]
fn invalid_radiation_batch_never_applies_an_earlier_delta() {
    let mut state = fixture();
    let mut valid = 0_i32.to_le_bytes().to_vec();
    valid.extend_from_slice(&3_f32.to_le_bytes());
    valid.extend_from_slice(&(-1_i32).to_le_bytes());
    send(&mut state, -1914877797, &valid);
    let mut batch = valid.clone();
    let mut invalid = valid.clone();
    invalid[..4].copy_from_slice(&16_i32.to_le_bytes());
    batch.extend_from_slice(&invalid);
    assert!(execute(&mut state, -1914877797, 12, 2, &batch).is_err());
    assert_eq!(state.queued_component_commands, 1);
    let mut pending = state.radiation_emitters.take_pending();
    let mut events = EventQueues::default();
    let world = state.world.as_mut().unwrap();
    ok(state
        .radiation_emitters
        .process_cell_pending(&mut pending, world, &mut events));
    assert_eq!(world.write.radiation[world.internal_index(0)], 3.0);
}

#[test]
fn interaction_registration_owns_records_and_rejects_invalid_replacement() {
    let mut state = fixture();
    let mut record = [0_u8; 32];
    record[..4].copy_from_slice(&0x6826f67d_u32.to_le_bytes());
    record[6..8].copy_from_slice(&1_u16.to_le_bytes());
    record[8..10].copy_from_slice(&2_u16.to_le_bytes());
    record[16..20].copy_from_slice(&0.5_f32.to_le_bytes());
    let mut payload = 1_i32.to_le_bytes().to_vec();
    payload.extend_from_slice(&(record.as_ptr() as u64).to_le_bytes());
    send(&mut state, crate::interactions::MESSAGE, &payload);
    record[16..20].copy_from_slice(&0.9_f32.to_le_bytes());
    assert_eq!(state.interactions[0].probability, 0.5);
    record[8..10].copy_from_slice(&3_u16.to_le_bytes());
    assert!(execute(&mut state, crate::interactions::MESSAGE, 12, 1, &payload).is_err());
    assert_eq!(state.interactions[0].product, 2);
    assert_eq!(state.interactions[0].probability, 0.5);
}

#[test]
fn initialize_accepts_managed_zero_capacity_tail_but_rejects_nonzero_tail() {
    let mut state = fixture();
    let mut image = 4_i32.to_le_bytes().to_vec();
    image.extend_from_slice(&4_i32.to_le_bytes());
    image.extend_from_slice(&1_u32.to_le_bytes());
    image.extend_from_slice(&[0, 0]);
    for _ in 0..16 {
        let mut cell = [0_u8; 16];
        cell[..2].copy_from_slice(&1_u16.to_le_bytes());
        image.extend_from_slice(&cell);
    }
    for _ in 0..16 {
        let mut cell = [0_u8; 12];
        cell[0] = 255;
        image.extend_from_slice(&cell);
    }
    for _ in 0..16 {
        let mut backwall = [0_u8; 12];
        backwall[..2].copy_from_slice(&1_u16.to_le_bytes());
        image.extend_from_slice(&backwall);
    }
    image.extend_from_slice(&[0; 64]);
    send(&mut state, crate::frames_plan::INITIALIZE, &image);
    let generation = state.generation;
    let world = state.world.as_ref().unwrap();
    assert_eq!(world.write.element[world.internal_index(0)], 1);
    *image.last_mut().unwrap() = 1;
    assert!(
        execute(
            &mut state,
            crate::frames_plan::INITIALIZE,
            image.len(),
            1,
            &image
        )
        .is_err()
    );
    assert_eq!(state.generation, generation);
}

#[test]
fn frame_batches_publish_every_submitted_frame() {
    let mut state = fixture();
    ok(state.start_worker(None));
    let mut frame = vec![0_u8; 28];
    frame[12..16].copy_from_slice(&4_i32.to_le_bytes());
    frame[16..20].copy_from_slice(&4_i32.to_le_bytes());
    let mut bad = frame.clone();
    bad[..4].copy_from_slice(&f32::NAN.to_le_bytes());
    let mut batch = frame.clone();
    batch.extend_from_slice(&bad);
    assert!(execute(&mut state, crate::frames_plan::FRAME, 28, 2, &batch).is_err());
    assert_eq!(state.scheduler.completed_frames, 0);
    batch = frame.clone();
    batch.extend_from_slice(&frame);
    ok(execute(
        &mut state,
        crate::frames_plan::FRAME,
        28,
        2,
        &batch,
    ));
    assert_eq!(state.scheduler.completed_frames, 2);
    let worker = state.worker.as_ref().unwrap().handle();
    let mut published = 0;
    while published < 2 {
        let ready = ok(worker.wait_ready());
        let count =
            unsafe { std::ptr::read_unaligned(ready.data().grid.descriptor().cast::<i32>()) };
        assert!(count > 0);
        published += count;
        ok(ready.release());
    }
    assert_eq!(published, 2);
    ok(state.worker.as_mut().unwrap().shutdown());
}

#[test]
fn prepared_step_validates_once_before_one_advance() {
    let _environment = ok(crate::float_env::FloatEnvGuard::enter());
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    let seed = world.tick_seed;
    let mut events = EventQueues::default();
    let mut systems = crate::simulation_step::StepSystems {
        mass: &mut state.mass,
        thermal: &mut state.thermal,
        radiation: &mut state.radiation_emitters,
    };
    crate::simulation_step::take_preflight_calls();
    ok(ok(crate::simulation_step::PreparedStep::new(
        world,
        &elements,
        &diseases,
        crate::simulation_step::FIXED_DT,
    ))
    .run(&mut events, &mut systems));
    assert_eq!(crate::simulation_step::take_preflight_calls(), 1);
    assert_eq!(world.tick_seed, seed.wrapping_add(1));
}

#[test]
fn prepared_next_step_revalidates_mutated_state() {
    let _environment = ok(crate::float_env::FloatEnvGuard::enter());
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    let mut events = EventQueues::default();
    let mut systems = crate::simulation_step::StepSystems {
        mass: &mut state.mass,
        thermal: &mut state.thermal,
        radiation: &mut state.radiation_emitters,
    };
    crate::simulation_step::take_preflight_calls();
    ok(ok(crate::simulation_step::PreparedStep::new(
        world,
        &elements,
        &diseases,
        crate::simulation_step::FIXED_DT,
    ))
    .run(&mut events, &mut systems));
    let after_first = world.tick_seed;
    world.write.element[0] = u16::MAX;
    match crate::simulation_step::PreparedStep::new(
        world,
        &elements,
        &diseases,
        crate::simulation_step::FIXED_DT,
    ) {
        Ok(_) => panic!("A later step accepted an unknown element"),
        Err(error) => assert_eq!(
            error.status as i32,
            crate::status::Status::InvalidState as i32
        ),
    }
    assert_eq!(world.tick_seed, after_first);
    assert_eq!(crate::simulation_step::take_preflight_calls(), 2);
}

#[test]
fn prepared_step_rejects_invalid_elapsed_without_mutation() {
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    let seed = world.tick_seed;
    let before: Vec<_> = world
        .write
        .mass
        .iter()
        .map(|value| value.to_bits())
        .collect();
    for elapsed in [0.0, -1.0, f32::NAN, f32::INFINITY] {
        match crate::simulation_step::PreparedStep::new(world, &elements, &diseases, elapsed) {
            Ok(_) => panic!("Invalid elapsed time was accepted"),
            Err(error) => assert_eq!(
                error.status as i32,
                crate::status::Status::InvalidArgument as i32
            ),
        }
        assert_eq!(world.tick_seed, seed);
        assert_eq!(
            world
                .write
                .mass
                .iter()
                .map(|value| value.to_bits())
                .collect::<Vec<_>>(),
            before
        );
    }
}
