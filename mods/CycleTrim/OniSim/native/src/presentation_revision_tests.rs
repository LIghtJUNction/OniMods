use crate::frames::NewFrame;
use crate::frames_data::GameData;
use crate::frames_plan::{ALLOCATE, ELEMENTS};
use crate::messages::execute;
use crate::state::State;
use crate::status::Result;
use crate::tables::DiseaseTable;
use std::sync::Arc;

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

fn fixture() -> State {
    let mut state = State::default();
    ok(state.initialize(None));
    let mut elements = 3_i32.to_le_bytes().to_vec();
    for (index, hash) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c].into_iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = if index == 2 { 3 } else { 0 };
        record[12..16].copy_from_slice(&1_f32.to_le_bytes());
        record[84..88].fill(255);
        elements.extend_from_slice(&record);
    }
    elements.extend_from_slice(&[0_u8; 12]);
    ok(execute(&mut state, ELEMENTS, elements.len(), 1, &elements));
    let mut allocate = 4_i32.to_le_bytes().to_vec();
    allocate.extend_from_slice(&4_i32.to_le_bytes());
    allocate.extend_from_slice(&[0, 0]);
    ok(execute(&mut state, ALLOCATE, allocate.len(), 1, &allocate));
    ok(execute(&mut state, crate::world_messages::CLEAR, 0, 1, &[]));
    state.diseases = Some(Arc::new(DiseaseTable { diseases: Vec::new(), element_count: 3 }));
    state.grid.as_mut().unwrap().initialized = true;
    state
}

fn textures(data: &GameData) -> Vec<Vec<u8>> {
    data.presentation.pointers().into_iter().zip([8, 4, 4, 4, 1]).map(|(pointer, stride)| {
        // The caller owns GameData or holds its ReadyLease throughout this copy.
        unsafe { std::slice::from_raw_parts(pointer as *const u8, data.grid.len() * stride) }.to_vec()
    }).collect()
}

#[test]
fn presentation_revision_follows_real_worker_publication_and_idle_buffer_reuse() {
    let mut state = fixture();
    assert!(state.presentation_update.is_none());
    ok(state.start_worker(None));
    assert!(state.presentation_update.is_none(), "constructor images are not a completed publication");
    let worker = state.worker.as_ref().unwrap().handle();
    let mut ready = ok(worker.wait_ready());
    let mut previous = None;
    let mut first_pointers = [0; 5];
    for index in 0..8 {
        ok(state.finish_prepare(ready.data_mut()));
        let published = state.presentation_update.unwrap();
        assert_eq!(published.generation, state.generation);
        assert_eq!(published.cell_count, 16);
        assert_eq!(published.pointers, ready.data().presentation.pointers());
        if index == 0 { first_pointers = published.pointers; }
        assert_eq!(published.revision, if index == 0 { 1 } else { 2 });
        if index % 2 == 0 { assert_eq!(published.pointers, first_pointers); }
        else { assert_ne!(published.pointers, first_pointers); }
        let pixels = textures(ready.data());
        if let Some(before) = previous { assert_eq!(pixels, before); }
        previous = Some(pixels);
        ok(ready.release());
        ready = ok(worker.wait_ready());
    }

    // The first zero-time frame synchronizes the previously unsynchronized
    // read snapshot. Refill both buffers before sharing their identity again.
    let before = state.presentation_update.unwrap().revision;
    for index in 0..5 {
        ok(state.submit_frame(NewFrame { elapsed: 0.0, regions: Vec::new() }));
        ok(ready.release());
        ready = ok(worker.wait_ready());
        ok(state.finish_prepare(ready.data_mut()));
        assert_eq!(state.presentation_update.unwrap().revision, before + if index == 0 { 1 } else { 2 });
    }

    let stable = state.presentation_update.unwrap().revision;
    let stable_images = textures(ready.data());
    for index in 0..20 {
        if index % 2 == 0 {
            ok(state.submit_frame(NewFrame { elapsed: 0.0, regions: Vec::new() }));
        }
        ok(ready.release());
        ready = ok(worker.wait_ready());
        ok(state.finish_prepare(ready.data_mut()));
        assert_eq!(state.presentation_update.unwrap().revision, stable,
            "empty/zero alternation after synchronization must not invalidate derived images");
        assert_eq!(textures(ready.data()), stable_images);
    }

    let published = state.presentation_update.unwrap();
    ok(state.with_world_mut(|world| {
        let cell = world.internal_index(5);
        world.write.element[cell] = 2;
        world.write.mass[cell] = 25.0;
        world.write.temperature[cell] = 375.0;
        Ok(())
    }));
    assert_eq!(state.presentation_update.unwrap(), published, "unpublished world changes do not change metadata early");
    ok(state.submit_frame(NewFrame { elapsed: 0.0, regions: Vec::new() }));
    ok(ready.release());
    ready = ok(worker.wait_ready());
    ok(state.finish_prepare(ready.data_mut()));
    assert!(state.presentation_update.unwrap().revision > published.revision);
    assert_eq!(state.grid.as_ref().unwrap().mass[5], 25.0);
    ok(ready.release());
}

#[derive(Clone, Copy, Debug)]
enum Batch {
    Empty,
    Zero,
    Debug,
    Properties,
    Replace,
    Advance,
    ZeroAdvance,
    AdvanceZero,
    Immediate,
}

fn scripted_frame(engine: &crate::frames_engine::Engine, elapsed: f32) -> crate::frames::QueuedFrame {
    let mut frame = crate::frames::QueuedFrame {
        elapsed, early: Default::default(), material: Default::default(),
        world: Default::default(), thermal: Default::default(), mass: Default::default(),
        radiation: Default::default(),
    };
    // Keep the authored cells active, independent of alternating visibility.
    let mut debug = [0_u8; 12];
    debug[..4].copy_from_slice(&0.001_f32.to_le_bytes());
    debug[4..8].copy_from_slice(&0.001_f32.to_le_bytes());
    debug[8] = 1;
    ok(frame.early.queue(crate::frames_commands::SET_DEBUG, &debug,
        &crate::worker_shape::WorldShape::from_world(&engine.world), &engine.elements,
        engine.diseases.as_deref()));
    frame
}

// Invoked only by the parent test below. Independent processes preserve the
// existing process-global fifteen-publication accumulated-flow reset phase.
// This allows every publication and world field to remain in the comparison.
#[test]
fn presentation_revision_engine_oracle_child() {
    use crate::frames_engine::Engine;
    use crate::native_chain_tests::{fixture, snapshot};
    use std::io::Write;

    let Some(path) = std::env::var_os("CYCLETRIM_PRESENTATION_ORACLE_OUTPUT") else { return; };
    let force = std::env::var("CYCLETRIM_PRESENTATION_ORACLE_FORCE").unwrap() == "1";
    let (mut state, regions) = fixture::create(32, 24, "mixed");
    let mut world = state.world.take().unwrap();
    // Match Start's boundary setup before deliberately separating read/write.
    world.initialize_boundary();
    world.sync_read_from_write();
    let top_liquid = 5 * world.stride + 2;
    world.write.mass[top_liquid] = 10.0;
    // Deliberately distinct read properties make the first zero-time sync
    // observable in the liquid alpha image, even without advancing physics.
    world.read.properties[top_liquid + world.stride] = 2;
    world.publish_to(state.grid.as_mut().unwrap());
    let mut buffers = [ok(GameData::new(state.grid.as_ref().unwrap())),
        ok(GameData::new(state.grid.as_ref().unwrap()))];
    let mut engine = Engine::new(world, state.elements.take().unwrap(), state.diseases.take(),
        std::mem::take(&mut state.thermal), std::mem::take(&mut state.mass),
        std::mem::take(&mut state.radiation_emitters));
    assert!(engine.gpu.is_none());
    let mut script = vec![Batch::Empty; 4];
    script.extend([Batch::Zero, Batch::Empty, Batch::Zero]);
    for _ in 0..3 { script.extend([Batch::Empty, Batch::Zero, Batch::Debug, Batch::Empty]); }
    for mutation in [Batch::Replace, Batch::Properties, Batch::Advance,
        Batch::ZeroAdvance, Batch::AdvanceZero, Batch::Immediate]
    {
        script.push(mutation);
        script.extend([Batch::Empty, Batch::Empty, Batch::Zero, Batch::Empty,
            Batch::Debug, Batch::Zero, Batch::Empty, Batch::Zero]);
    }
    let mut file = std::io::BufWriter::new(std::fs::File::create(path).unwrap());
    let mut first_images = None;
    for (index, kind) in script.into_iter().enumerate() {
        let mut frames = Vec::new();
        match kind {
            Batch::Empty => (),
            Batch::Zero | Batch::Debug => {
                let mut frame = scripted_frame(&engine, 0.0);
                if matches!(kind, Batch::Debug) {
                    let mut debug = [0_u8; 12];
                    debug[..4].copy_from_slice(&0.25_f32.to_le_bytes());
                    debug[4..8].copy_from_slice(&0.5_f32.to_le_bytes());
                    debug[8] = 1;
                    ok(frame.early.queue(crate::frames_commands::SET_DEBUG, &debug,
                        &crate::worker_shape::WorldShape::from_world(&engine.world), &engine.elements,
                        engine.diseases.as_deref()));
                }
                frames.push(frame);
            }
            Batch::Properties => {
                let mut frame = scripted_frame(&engine, 0.0);
                let mut bytes = [0_u8; 12];
                let above = engine.world.public_index(top_liquid + engine.world.stride).unwrap();
                bytes[..4].copy_from_slice(&(above as i32).to_le_bytes());
                bytes[4..8].copy_from_slice(&17_i32.to_le_bytes());
                bytes[8] = 2;
                bytes[9] = 1;
                ok(frame.early.queue(crate::frames_commands::CHANGE_PROPERTIES, &bytes,
                    &crate::worker_shape::WorldShape::from_world(&engine.world), &engine.elements,
                    engine.diseases.as_deref()));
                frames.push(frame);
            }
            Batch::Replace => {
                let mut frame = scripted_frame(&engine, 0.0);
                frame.material.modifications.push(crate::cell_material::Modification {
                    cell: top_liquid, callback: 23, temperature: 315.0, mass: 120.0,
                    count: 0, element: 5, replace_type: 1, disease: 255, subtype: 1,
                });
                frames.push(frame);
            }
            Batch::Advance => frames.push(scripted_frame(&engine, 0.2)),
            Batch::ZeroAdvance => frames.extend([scripted_frame(&engine, 0.0), scripted_frame(&engine, 0.2)]),
            Batch::AdvanceZero => frames.extend([scripted_frame(&engine, 0.2), scripted_frame(&engine, 0.0)]),
            Batch::Immediate => {
                // Mirrors State.with_world_mut's required cache invalidation.
                engine.world.write.mass[top_liquid] = 33.0;
                engine.world.write.temperature[top_liquid] = 320.0;
                engine.world.write.properties[top_liquid + engine.world.stride] = 0;
                engine.invalidate_empty_publications();
            }
        }
        if force { engine.invalidate_empty_publications(); }
        let [first, second] = &mut buffers;
        let (output, old) = if index % 2 == 0 { (first, &*second) } else { (second, &*first) };
        output.retired_slots.clear();
        ok(engine.run_batch(frames, &regions, output, old));
        if index == 0 { first_images = Some(textures(output)); }
        if index == 4 {
            assert_ne!(first_images.as_ref().unwrap(), &textures(output),
                "the authored read/write mismatch must exercise a visible first-sync change");
        }
        let mut image = snapshot::publication(&output.grid, output, 1);
        image.bytes("world", &snapshot::world(&engine.world).0);
        image.bytes("residual_time", &engine.residual_time.to_bits().to_le_bytes());
        let label = format!("batch {index}: {kind:?}");
        file.write_all(&(label.len() as u64).to_le_bytes()).unwrap();
        file.write_all(label.as_bytes()).unwrap();
        file.write_all(&(image.0.len() as u64).to_le_bytes()).unwrap();
        file.write_all(&image.0).unwrap();
    }
    file.flush().unwrap();
}

#[test]
fn presentation_revision_alternating_idle_matches_forced_engine_recomputation() {
    use crate::native_chain_tests::snapshot::{Image, assert_equal};
    use std::io::{Cursor, Read};
    use std::process::Command;

    let directory = std::env::temp_dir().join(format!("cycletrim-presentation-oracle-{}", std::process::id()));
    std::fs::create_dir_all(&directory).unwrap();
    let mut results = Vec::new();
    for force in [false, true] {
        let path = directory.join(if force { "forced.bin" } else { "cached.bin" });
        let output = Command::new(std::env::current_exe().unwrap())
            .args(["--exact", "presentation_revision_tests::presentation_revision_engine_oracle_child",
                "--nocapture", "--test-threads=1"])
            .env("CYCLETRIM_ONISIM_GPU", "0").env("CYCLETRIM_ONISIM_WORKERS", "1")
            .env("CYCLETRIM_PRESENTATION_ORACLE_OUTPUT", &path)
            .env("CYCLETRIM_PRESENTATION_ORACLE_FORCE", if force { "1" } else { "0" })
            .output().unwrap();
        assert!(output.status.success(), "oracle subprocess failed; evidence retained at {}\n{}\n{}",
            directory.display(), String::from_utf8_lossy(&output.stdout), String::from_utf8_lossy(&output.stderr));
        results.push(std::fs::read(&path).unwrap());
    }
    fn next(cursor: &mut Cursor<&[u8]>) -> Option<(String, Image)> {
        if cursor.position() as usize == cursor.get_ref().len() { return None; }
        let mut number = [0; 8];
        cursor.read_exact(&mut number).unwrap();
        let mut label = vec![0; u64::from_le_bytes(number) as usize];
        cursor.read_exact(&mut label).unwrap();
        cursor.read_exact(&mut number).unwrap();
        let mut bytes = vec![0; u64::from_le_bytes(number) as usize];
        cursor.read_exact(&mut bytes).unwrap();
        Some((String::from_utf8(label).unwrap(), Image(bytes)))
    }
    let mut cached = Cursor::new(results[0].as_slice());
    let mut forced = Cursor::new(results[1].as_slice());
    let mut count = 0;
    while let Some((label, candidate)) = next(&mut cached) {
        let (expected_label, expected) = next(&mut forced).expect("forced script ended early");
        assert_eq!(label, expected_label);
        assert_equal(&label, &candidate, &expected);
        count += 1;
    }
    assert!(next(&mut forced).is_none());
    assert_eq!(count, 73);
    std::fs::remove_dir_all(directory).unwrap();
}
