//! Content identities must distinguish retained pixels in alternating images.

use crate::frames_data::GameData;
use crate::frames_engine::Engine;
use crate::native_chain_tests::fixture;
use crate::world::{PhysicsWorld, Region};

struct Runner {
    engine: Engine,
    buffers: [GameData; 2],
    regions: Vec<Region>,
    step: usize,
}

#[derive(Clone, Debug)]
struct Snapshot {
    pointer: usize,
    revision: u64,
    images: Vec<Vec<u8>>,
}

impl Snapshot {
    fn from_data(data: &GameData) -> Self {
        let pointers = data.presentation.pointers();
        Self {
            pointer: pointers[0],
            revision: data.presentation_revision,
            images: pointers.into_iter().zip([8, 4, 4, 4, 1]).map(|(pointer, stride)| {
                // The caller owns GameData or holds its immutable ReadyLease.
                unsafe { std::slice::from_raw_parts(pointer as *const u8, data.grid.len() * stride) }.to_vec()
            }).collect(),
        }
    }
}

impl Runner {
    fn new() -> Self {
        let (mut state, regions) = fixture::create(32, 24, "pure");
        let mut world = state.world.take().unwrap();
        world.initialize_boundary();
        world.sync_read_from_write();
        world.publish_to(state.grid.as_mut().unwrap());
        let buffers = [GameData::new(state.grid.as_ref().unwrap()).unwrap(),
            GameData::new(state.grid.as_ref().unwrap()).unwrap()];
        let engine = Engine::new(world, state.elements.take().unwrap(), state.diseases.take(),
            std::mem::take(&mut state.thermal), std::mem::take(&mut state.mass),
            std::mem::take(&mut state.radiation_emitters));
        Self { engine, buffers, regions, step: 0 }
    }

    fn change(&mut self, apply: impl FnOnce(&mut PhysicsWorld)) {
        // Same invalidation used by the live DEFINE_OFFSETS/FREE message path.
        self.engine.invalidate_empty_publications();
        apply(&mut self.engine.world);
    }

    fn empty(&mut self) -> Snapshot {
        let [first, second] = &mut self.buffers;
        let (output, old) = if self.step % 2 == 0 { (first, &*second) } else { (second, &*first) };
        output.retired_slots.clear();
        self.engine.run_batch(Vec::new(), &self.regions, output, old)
            .unwrap_or_else(|error| panic!("{}", error.message));
        self.step += 1;
        Snapshot::from_data(output)
    }
}

#[test]
fn retained_sun_pixels_require_distinct_identities_after_dynamic_world_offsets() {
    for clear in [false, true] {
        let mut runner = Runner::new();
        let original = runner.empty();
        runner.change(|world| {
            // A different top-right gas opacity gives B a different sunlight
            // history while A retains the preceding completed image.
            for x in world.width / 2 + 1..=world.width {
                world.write.mass[world.height * world.stride + x] = 1000.0;
            }
        });
        let changed = runner.empty();
        assert_ne!(original.images[4], changed.images[4], "authored histories must differ");
        runner.change(|world| {
            world.world_offsets = if clear { Vec::new() }
                else { vec![[0, 0, world.width as i32 / 2, world.height as i32]] };
        });
        let first = runner.empty();
        let second = runner.empty();
        assert_eq!(first.images[4], original.images[4]);
        assert_eq!(second.images[4], changed.images[4]);
        assert_ne!(first.images[4], second.images[4],
            "two fresh updates retain different sun pixels outside their current offsets");
        assert_ne!(first.revision, second.revision);
        for _ in 0..8 {
            let next = runner.empty();
            let own = if next.pointer == first.pointer { &first } else { &second };
            assert_eq!(next.images, own.images, "the fix must preserve original retained pixels");
            assert_eq!(next.revision, own.revision,
                "unequal alternating images must keep the identity of their own buffer; clear={clear}");
        }

        // Full coverage removes the historical difference. After the two real
        // updates, exact equality can once again authorize a shared identity.
        runner.change(|world| {
            world.world_offsets = vec![[0, 0, world.width as i32, world.height as i32]];
        });
        let restored_first = runner.empty();
        let restored_second = runner.empty();
        assert_eq!(restored_first.images, restored_second.images);
        for _ in 0..8 {
            let next = runner.empty();
            assert_eq!(next.images, restored_second.images);
            assert_eq!(next.revision, restored_second.revision,
                "a new equality proof may merge the identical images again");
        }
    }
}

#[test]
fn exhausted_identity_never_restores_a_nonzero_id_from_an_old_buffer() {
    let mut runner = Runner::new();
    let mut previous = runner.empty();
    for _ in 0..4 { previous = runner.empty(); }
    assert_ne!(previous.revision, 0);
    runner.engine.exhaust_presentation_revision_for_test();
    for _ in 0..8 {
        let next = runner.empty();
        assert_eq!(next.revision, 0, "even an old per-buffer identity must be disabled");
        assert_eq!(next.images, previous.images);
    }
    runner.change(|world| world.write.mass[world.stride + 1] = 2.0);
    for _ in 0..5 { assert_eq!(runner.empty().revision, 0); }
}

#[test]
fn presentation_content_comparison_preserves_signed_zero_and_nan_payloads() {
    use crate::presentation::PresentationBuffers;
    let mut runner = Runner::new();
    let world = &mut runner.engine.world;
    let cell = world.internal_index(0);
    for (left, right, equal) in [
        (0x8000_0000_u32, 0x0000_0000_u32, false),
        (0x7fc0_1234, 0x7fc0_1234, true),
        (0x7fc0_1234, 0x7fc0_5678, false),
    ] {
        let mut first = PresentationBuffers::new(world.width * world.height).unwrap();
        let mut second = PresentationBuffers::new(world.width * world.height).unwrap();
        world.flow[cell] = [f32::from_bits(left), 0.0, 0.0, 0.0];
        first.update(world, &runner.engine.elements).unwrap();
        world.flow[cell] = [f32::from_bits(right), 0.0, 0.0, 0.0];
        second.update(world, &runner.engine.elements).unwrap();
        let first_bits = unsafe { *(first.pointers()[0] as *const u32) };
        let second_bits = unsafe { *(second.pointers()[0] as *const u32) };
        assert_eq!(first_bits == second_bits, equal,
            "the production flow calculation must preserve the authored bit distinction");
        assert_eq!(first.same_image_bytes(&second), equal,
            "content identity compares bits, including identical NaNs and unequal signed zeros");
    }
}

#[test]
fn worker_publishes_each_retained_image_with_its_own_identity() {
    fn publish(state: &mut crate::state::State, data: &mut GameData) -> Snapshot {
        state.finish_prepare(data).unwrap();
        let snapshot = Snapshot::from_data(data);
        let metadata = state.presentation_update.unwrap();
        assert_eq!(metadata.revision, snapshot.revision);
        assert_eq!(metadata.pointers, data.presentation.pointers());
        snapshot
    }
    let (mut state, _) = fixture::create(32, 24, "pure");
    state.start_worker(None).unwrap();
    let worker = state.worker.as_ref().unwrap().handle();
    let mut ready = worker.wait_ready().unwrap();
    let original = publish(&mut state, ready.data_mut());
    state.with_world_mut(|world| {
        for x in world.width / 2 + 1..=world.width {
            world.write.mass[world.height * world.stride + x] = 1000.0;
        }
        Ok(())
    }).unwrap();
    ready.release().unwrap();
    ready = worker.wait_ready().unwrap();
    let changed = publish(&mut state, ready.data_mut());
    assert_ne!(original.images[4], changed.images[4]);
    state.with_world_mut(|world| {
        world.world_offsets = vec![[0, 0, world.width as i32 / 2, world.height as i32]];
        Ok(())
    }).unwrap();
    let mut retained = Vec::new();
    for step in 0..10 {
        ready.release().unwrap();
        ready = worker.wait_ready().unwrap();
        let next = publish(&mut state, ready.data_mut());
        if step < 2 { retained.push(next.clone()); }
        let expected = &retained[step % 2];
        assert_eq!(next.pointer, expected.pointer);
        assert_eq!(next.images, expected.images);
        assert_eq!(next.revision, expected.revision,
            "State publication must preserve the identity of the current worker buffer");
    }
    assert_ne!(retained[0].images, retained[1].images);
    assert_ne!(retained[0].revision, retained[1].revision);
    ready.release().unwrap();
}
