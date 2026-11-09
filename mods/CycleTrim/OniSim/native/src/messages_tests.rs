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
fn empty_batches_reuse_both_derived_buffers_and_invalidate_after_world_change() {
    use crate::frames::QueuedFrame;
    use crate::frames_data::GameData;
    use crate::frames_engine::Engine;
    use crate::grid::{Grid, DESCRIPTOR_SIZE};
    use crate::multicomponent_publication::{OniSimMulticomponentCellHeader, OniSimMulticomponentSpeciesRecord};
    use std::sync::Mutex;

    fn snapshot(data: &GameData) -> (Vec<Vec<u8>>, Vec<OniSimMulticomponentCellHeader>, Vec<OniSimMulticomponentSpeciesRecord>) {
        let sizes = [8, 4, 4, 4, 1];
        let textures = data.presentation.pointers().into_iter().zip(sizes).map(|(ptr, size)| {
            // The production publication owns these fixed-length live buffers.
            unsafe { std::slice::from_raw_parts(ptr as *const u8, data.grid.len() * size) }.to_vec()
        }).collect();
        let view = data.multicomponent.descriptor(0).unwrap();
        let cells = unsafe { std::slice::from_raw_parts(view.cells, view.cell_count as usize) }.to_vec();
        let species = unsafe { std::slice::from_raw_parts(view.species, view.total_species_records as usize) }.to_vec();
        (textures, cells, species)
    }

    fn image(grid: &Grid) -> Vec<u8> {
        let mut bytes = vec![0; grid.image_len()];
        grid.write_image(&mut bytes);
        bytes
    }

    fn descriptor(grid: &Grid) -> Vec<u8> {
        // Owned, initialized descriptor storage remains live throughout this read.
        unsafe { std::slice::from_raw_parts(grid.descriptor(), DESCRIPTOR_SIZE).to_vec() }
    }

    fn bits(values: &[f32]) -> Vec<u32> {
        values.iter().map(|value| value.to_bits()).collect()
    }

    fn empty_frame(early: crate::frames_commands::EarlyCommands) -> QueuedFrame {
        QueuedFrame {
            elapsed: 0.0, early, material: Default::default(), world: Default::default(),
            thermal: Default::default(), mass: Default::default(), radiation: Default::default(),
        }
    }

    let mut state = fixture();
    let mut world = state.world.take().unwrap();
    world.world_offsets = vec![[0, 0, 4, 4]];
    let cell = world.internal_index(5);
    for public in 0..16 {
        let internal = world.internal_index(public);
        world.write.element[internal] = 2;
        world.write.mass[internal] = 10.0 + public as f32;
        world.write.temperature[internal] = 300.0 + public as f32;
        world.write.properties[internal] = (public % 8) as u8;
        world.write.insulation[internal] = public as u8;
        world.write.strength[internal] = 100 + public as u8;
        world.write.radiation[internal] = f32::from_bits(0x8000_0000 | public as u32);
        world.write.disease_count[internal] = public as i32;
        world.write.disease_infestation[internal] = 200 + public as u8;
        world.write.disease_growth[internal] = f32::from_bits(0x7fc0_0000 | public as u32);
        world.backwall_element[internal] = 2;
        world.backwall_mass[internal] = 20.0 + public as f32;
        world.backwall_temperature[internal] = 350.0 + public as f32;
    }
    world.sync_read_from_write();
    let grid = state.grid.as_ref().unwrap();
    let mut buffers = [ok(GameData::new(grid)), ok(GameData::new(grid))];
    for (slot, data) in buffers.iter_mut().enumerate() {
        // Distinct initial cells catch one-slot warmup; distinct reserved bytes
        // must survive every publication, including cache invalidation.
        data.grid.load_image(&vec![0xa0 + slot as u8; data.grid.image_len()]);
        data.grid.radiation.fill(-999.0 - slot as f32);
        data.grid.accumulated_flow.fill(-888.0 - slot as f32);
    }
    let engine = Arc::new(Mutex::new(Engine::new(world,
        Arc::clone(state.elements.as_ref().unwrap()), state.diseases.clone(),
        std::mem::take(&mut state.thermal), std::mem::take(&mut state.mass),
        std::mem::take(&mut state.radiation_emitters))));
    engine.lock().unwrap().gpu = None;
    state.engine = Some(Arc::clone(&engine));
    let mut observed_flow_resets = [false; 2];

    // Each mode lasts long enough to warm both slots and cross the global
    // fifteen-publication reset twice. Do not assume Initialize resets its phase.
    for iteration in 0..256 {
        let zero_frame = (iteration / 64) % 2 != 0;
        if iteration % 64 == 0 {
            ok(state.with_world_mut(|world| {
                world.accumulated_flow.fill(3.5 + iteration as f32);
                if iteration == 192 {
                    world.backwall_element[cell] = u16::MAX;
                    world.backwall_mass[cell] = 123.0;
                    world.backwall_temperature[cell] = 456.0;
                }
                Ok(())
            }));
        }
        if iteration == 32 {
            ok(state.with_world_mut(|world| {
                world.write.mass[cell] = 25.0;
                world.write.temperature[cell] = 375.0;
                world.write.radiation[cell] = f32::from_bits(0x7fc0_1234);
                world.write.disease_growth[cell] = -0.0;
                Ok(())
            }));
        }
        if iteration == 96 {
            send(&mut state, crate::frames_commands::SET_STRENGTH, &float_command(5, 77.0));
            send(&mut state, crate::frames_commands::SET_INSULATION, &float_command(5, 0.5));
            let mut backwall = [0u8; 16];
            backwall[..4].copy_from_slice(&5_i32.to_le_bytes());
            backwall[4..6].copy_from_slice(&1_u16.to_le_bytes());
            backwall[8..12].copy_from_slice(&42_f32.to_le_bytes());
            backwall[12..].copy_from_slice(&280_f32.to_le_bytes());
            send(&mut state, crate::frames_commands::MODIFY_BACKWALL, &backwall);
            let mut disease = [0u8; 12];
            disease[..4].copy_from_slice(&5_i32.to_le_bytes());
            disease[4] = u8::MAX;
            disease[8..].copy_from_slice(&19_i32.to_le_bytes());
            send(&mut state, crate::frames_commands::MODIFY_DISEASE, &disease);
            let mut radiation = [0u8; 12];
            radiation[..4].copy_from_slice(&6_i32.to_le_bytes());
            radiation[4..8].copy_from_slice(&12_f32.to_le_bytes());
            radiation[8..].copy_from_slice(&43_i32.to_le_bytes());
            send(&mut state, -1_914_877_797, &radiation);
        }
        if iteration == 160 {
            // Real synchronous message routing invalidates an Engine-owned world.
            send(&mut state, crate::world_messages::CLEAR, &[]);
        }
        if iteration == 224 {
            let elements = Arc::clone(state.elements.as_ref().unwrap());
            let diseases = state.diseases.clone();
            // Exercise save's backwall normalization through the mutation guard.
            // This is not a call to the process-global SIM_BeginSave FFI entry.
            ok(state.with_world_mut(|world| {
                crate::save::serialize(world, &elements, diseases.as_deref(), 0, 0).map(|_| ())
            }));
        }
        let frames = if zero_frame {
            let mut debug = [0u8; 12];
            debug[..4].copy_from_slice(&(iteration as f32).to_le_bytes());
            debug[4..8].copy_from_slice(&0.001_f32.to_le_bytes());
            debug[8] = (iteration % 2) as u8;
            send(&mut state, crate::frames_commands::SET_DEBUG, &debug);
            let extra_early = state.early.clone();
            let mut frame = empty_frame(std::mem::take(&mut state.early));
            frame.radiation = state.radiation_emitters.take_pending();
            let mut frames = vec![frame];
            if iteration % 17 == 0 { frames.push(empty_frame(extra_early)); }
            frames
        } else { Vec::new() };
        let frame_count = frames.len();
        let [first, second] = &mut buffers;
        let (output, old) = if iteration % 2 == 0 { (first, &*second) } else { (second, &*first) };
        output.retired_slots.clear();
        // ReadyLease normally places incoming visibility into this next output slot.
        output.visible.fill((iteration % 251) as u8);
        let before_descriptor = descriptor(&output.grid);
        let texture_pointers = output.presentation.pointers();
        let mut expected = ok(Grid::new(4, 4, 0, 0));
        expected.copy_from(&output.grid);
        let mut engine = engine.lock().unwrap();
        let incoming_visibility = output.visible.clone();
        let previous_visibility = engine.world.active_mask.clone();
        let flow_before: Vec<_> = (0..16)
            .map(|public| engine.world.accumulated_flow[engine.world.internal_index(public)])
            .collect();
        ok(engine.run_batch(frames, &[], output, old));
        if flow_before.iter().any(|value| value.to_bits() != 0)
            && engine.world.accumulated_flow.iter().all(|value| value.to_bits() == 0)
        { observed_flow_resets[usize::from(zero_frame)] = true; }

        // Rebuild only the public snapshot, not a second Engine. The flow image
        // belongs to the pre-reset publication even when the world is now zero.
        engine.world.publish_to(&mut expected);
        expected.accumulated_flow.copy_from_slice(&flow_before);
        assert_eq!(image(&output.grid), image(&expected), "grid image at batch {iteration}");
        assert_eq!(bits(&output.grid.radiation), bits(&expected.radiation), "radiation at {iteration}");
        assert_eq!(bits(&output.grid.accumulated_flow), bits(&flow_before), "flow at {iteration}");
        expected.set_num_frames(frame_count as i32);
        ok(output.events.publish_to(&mut expected));
        let after_descriptor = descriptor(&output.grid);
        let expected_descriptor = descriptor(&expected);
        assert_eq!(&after_descriptor[..4], &expected_descriptor[..4]);
        assert_eq!(&after_descriptor[4..100], &before_descriptor[4..100]);
        assert_eq!(&after_descriptor[100..448], &expected_descriptor[100..448]);
        assert_eq!(&after_descriptor[448..456], &before_descriptor[448..456]);
        assert_eq!(texture_pointers, output.presentation.pointers());
        for (index, pointer) in texture_pointers.into_iter().enumerate() {
            let at = 456 + index * 8;
            assert_eq!(u64::from_le_bytes(after_descriptor[at..at + 8].try_into().unwrap()), pointer as u64);
        }
        assert_eq!(output.retired_slots.len(), frame_count);
        assert_eq!(engine.world.active_mask, incoming_visibility);
        assert_eq!(output.visible, previous_visibility);
        if zero_frame {
            assert_eq!(engine.world.building_heat_transfer_scale, iteration as f32);
            assert_eq!(engine.world.simulate_all_active, iteration % 2 != 0);
        }
        if iteration == 96 {
            assert_eq!(output.grid.strength[5], 77);
            assert_eq!(output.grid.insulation[5], 127);
            assert_eq!(output.grid.backwall_mass[5], 42.0);
            assert_eq!(output.grid.disease_count[5], 24);
            assert_eq!(output.events.radiation_consumed_callbacks.len(), 1);
        }
        if iteration == 224 { assert_eq!(output.grid.backwall_mass[5], 0.0); }
        let cached = snapshot(output);
        ok(output.presentation.update(&engine.world, &engine.elements));
        output.multicomponent.update_from_grid_with_mixture_at(&output.grid, &engine.elements, |public| {
            let internal = engine.world.internal_index(public);
            (engine.world.mixture_element[internal], engine.world.mixture_mass[internal])
        });
        assert_eq!(cached, snapshot(output), "derived snapshot at batch {iteration}");
    }
    // Other Engine tests share the global reset phase. Verify both idle modes,
    // without requiring each individual segment to own a reset publication.
    assert!(observed_flow_resets.into_iter().all(|observed| observed),
        "both idle modes must cross a real flow reset");
}

#[test]
#[ignore = "manual full-grid empty-publication benchmark"]
fn benchmark_empty_publication_reuse() {
    use crate::frames_data::GameData;
    use crate::frames_engine::Engine;
    use crate::grid::Grid;
    use crate::world::PhysicsWorld;
    use std::time::Instant;

    let mut state = fixture();
    let elements = state.elements.take().unwrap();
    let mut world = ok(PhysicsWorld::new(736, 304, 0, &elements));
    world.write.element.fill(2);
    world.write.mass.fill(1.0);
    world.write.temperature.fill(300.0);
    world.world_offsets = vec![[0, 0, 736, 304]];
    world.sync_read_from_write();
    let grid = ok(Grid::new(736, 304, 0, 0));
    let mut buffers = [ok(GameData::new(&grid)), ok(GameData::new(&grid))];
    let mut engine = Engine::new(world, elements, state.diseases.take(),
        std::mem::take(&mut state.thermal), std::mem::take(&mut state.mass), std::mem::take(&mut state.radiation_emitters));
    engine.gpu = None;
    let mut step = 0usize;
    for (zero_frame, reuse) in [(false,false),(false,true),(true,false),(true,true),(true,true),(true,false)] {
        let start = Instant::now();
        for _ in 0..30 {
            if !reuse { engine.invalidate_empty_publications(); }
            let [first, second] = &mut buffers;
            let (output, old) = if step % 2 == 0 { (first, &*second) } else { (second, &*first) };
            output.retired_slots.clear();
            let frames = if zero_frame { vec![crate::frames::QueuedFrame {
                elapsed: 0.0, early: Default::default(), material: Default::default(),
                world: Default::default(), thermal: Default::default(), mass: Default::default(),
                radiation: Default::default(),
            }] } else { Vec::new() };
            ok(engine.run_batch(frames, &[], output, old));
            step += 1;
        }
        eprintln!("idle publication zero_frame={zero_frame} reuse={reuse}: {:.3} ms/batch", start.elapsed().as_secs_f64() * 1000.0 / 30.0);
    }
}

#[test]
#[ignore = "manual warmed production idle-batch benchmark; compare identical tests at two source revisions"]
fn benchmark_warmed_idle_publication() {
    use crate::frames::QueuedFrame;
    use crate::frames_data::GameData;
    use crate::frames_engine::Engine;
    use crate::grid::Grid;
    use crate::world::PhysicsWorld;
    use std::{hint::black_box, time::Instant};

    const WARMUP_BATCHES: usize = 8;
    const SAMPLES: usize = 21;
    println!("IDLE_BATCH_HEADER,mode,width,height,warmup_batches,samples,batches_per_sample,median_us,p95_us,min_us,max_us,samples_us");
    for (width, height) in [(96, 96), (256, 384), (736, 304)] {
        for zero_frame in [false, true] {
            let mut state = fixture();
            let elements = state.elements.take().unwrap();
            let mut world = ok(PhysicsWorld::new(width, height, 0, &elements));
            world.write.element.fill(2);
            world.write.mass.fill(1.0);
            world.write.temperature.fill(300.0);
            world.world_offsets = vec![[0, 0, width as i32, height as i32]];
            world.sync_read_from_write();
            let grid = ok(Grid::new(width as i32, height as i32, 0, 0));
            let mut buffers = [ok(GameData::new(&grid)), ok(GameData::new(&grid))];
            let mut engine = Engine::new(world, elements, state.diseases.take(),
                std::mem::take(&mut state.thermal), std::mem::take(&mut state.mass),
                std::mem::take(&mut state.radiation_emitters));
            engine.gpu = None;
            let repeats = (8_000_000 / (width * height)).clamp(16, 400);
            let mut step = 0usize;
            let mut run = || {
                let [first, second] = &mut buffers;
                let (output, old) = if step % 2 == 0 { (first, &*second) } else { (second, &*first) };
                output.retired_slots.clear();
                let frames = if zero_frame { vec![QueuedFrame {
                    elapsed: 0.0, early: Default::default(), material: Default::default(),
                    world: Default::default(), thermal: Default::default(), mass: Default::default(),
                    radiation: Default::default(),
                }] } else { Vec::new() };
                ok(black_box(&mut engine).run_batch(frames, &[], output, old));
                black_box(&output.grid);
                step += 1;
            };
            // Keep the existing reuse policy enabled in both executables. This
            // compares the incremental Grid change, not the older derived cache.
            for _ in 0..WARMUP_BATCHES { run(); }
            let mut samples = Vec::with_capacity(SAMPLES);
            for _ in 0..SAMPLES {
                let start = Instant::now();
                for _ in 0..repeats { run(); }
                samples.push(start.elapsed().as_secs_f64() * 1e6 / repeats as f64);
            }
            drop(run);
            for data in &buffers {
                assert!(data.grid.mass.iter().all(|value| *value == 1.0));
                assert!(data.grid.accumulated_flow.iter().all(|value| value.to_bits() == 0));
            }
            let mut ordered = samples.clone();
            ordered.sort_by(f64::total_cmp);
            let series = samples.iter().map(|value| format!("{value:.6}")).collect::<Vec<_>>().join(";");
            let mode = if zero_frame { "zero" } else { "empty" };
            println!("IDLE_BATCH,{mode},{width},{height},{WARMUP_BATCHES},{SAMPLES},{repeats},{:.6},{:.6},{:.6},{:.6},{series}",
                ordered[SAMPLES / 2], ordered[(SAMPLES - 1) * 95 / 100], ordered[0], ordered[SAMPLES - 1]);
        }
    }
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
    let worker = state.worker.as_ref().unwrap().handle();
    // Startup publishes an empty batch. Hold its barrier while submitting so
    // neither OS scheduling nor a faster idle path can split this next batch.
    let initial = ok(worker.wait_ready());
    assert_eq!(
        unsafe { std::ptr::read_unaligned(initial.data().grid.descriptor().cast::<i32>()) },
        0
    );
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
    ok(initial.release());
    let ready = ok(worker.wait_ready());
    let count =
        unsafe { std::ptr::read_unaligned(ready.data().grid.descriptor().cast::<i32>()) };
    assert_eq!(count, 2);
    ok(ready.release());
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
        gpu: None,
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
        gpu: None,
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
fn prepared_step_rejects_invalid_ids_without_activity_flags() {
    let mutations: [(&str, fn(&mut crate::world::PhysicsWorld)); 5] = [
        ("read element", |world| world.read.element[0] = u16::MAX),
        ("write element", |world| world.write.element[0] = u16::MAX),
        ("read disease", |world| world.read.disease_idx[0] = 0),
        ("write disease", |world| world.write.disease_idx[0] = 0),
        ("backwall element", |world| world.backwall_element[0] = 3),
    ];
    for (label, mutate) in mutations {
        let mut state = fixture();
        let elements = Arc::clone(state.elements.as_ref().unwrap());
        let diseases = Arc::clone(state.diseases.as_ref().unwrap());
        let world = state.world.as_mut().unwrap();
        let before = (world.tick_seed, world.rng);
        ok(crate::simulation_step::PreparedStep::new(
            world,
            &elements,
            &diseases,
            crate::simulation_step::FIXED_DT,
        ));
        world.dirty.fill(false);
        world.dirty_flags.fill(0);
        mutate(world);
        match crate::simulation_step::PreparedStep::new(
            world,
            &elements,
            &diseases,
            crate::simulation_step::FIXED_DT,
        ) {
            Ok(_) => panic!("PreparedStep accepted an invalid {label}"),
            Err(error) => assert_eq!(
                error.status as i32,
                crate::status::Status::InvalidState as i32,
                "{label}"
            ),
        }
        assert_eq!((world.tick_seed, world.rng), before, "{label}");
    }
}

#[test]
fn prepared_step_accepts_nullable_disease_and_backwall_ids() {
    let mut state = fixture();
    let elements = Arc::clone(state.elements.as_ref().unwrap());
    let diseases = Arc::clone(state.diseases.as_ref().unwrap());
    let world = state.world.as_mut().unwrap();
    world.read.disease_idx.fill(u8::MAX);
    world.write.disease_idx.fill(u8::MAX);
    world.backwall_element.fill(u16::MAX);
    ok(crate::simulation_step::PreparedStep::new(
        world,
        &elements,
        &diseases,
        crate::simulation_step::FIXED_DT,
    ));
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
