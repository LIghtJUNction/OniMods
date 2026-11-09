//! Payload ownership across the production publication barrier.

use super::*;
use std::sync::atomic::{AtomicUsize, Ordering};

#[derive(Default)]
struct Observations {
    addresses: [AtomicUsize; 2],
    drops: [AtomicUsize; 2],
}

struct Payload {
    slot: usize,
    batch: usize,
    handoff: usize,
    observations: Arc<Observations>,
}

impl Payload {
    fn new(slot: usize, observations: &Arc<Observations>) -> Self {
        Self { slot, batch: 0, handoff: 0, observations: Arc::clone(observations) }
    }

    fn observe_address(&self) {
        // Record an address value only; never dereference a saved raw pointer.
        // The first observation is in the real worker runner, before publish.
        let address = self as *const Self as usize;
        let observed = &self.observations.addresses[self.slot];
        let first = observed.compare_exchange(0, address, Ordering::SeqCst, Ordering::SeqCst)
            .unwrap_or_else(|previous| previous);
        assert!(first == 0 || first == address,
            "payload {} header moved across the publication barrier: {first:#x} -> {address:#x}", self.slot);
    }
}

impl Drop for Payload {
    fn drop(&mut self) {
        self.observations.drops[self.slot].fetch_add(1, Ordering::SeqCst);
    }
}

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

#[test]
fn payload_headers_stay_at_their_runner_addresses_across_handoffs() {
    let observations = Arc::new(Observations::default());
    let mut worker = ok(Worker::<(), Payload>::spawn(
        Payload::new(0, &observations), Payload::new(1, &observations), Vec::new(),
        |_, _, output, old| {
            output.observe_address();
            old.observe_address();
            assert_ne!(output.slot, old.slot);
            output.batch = old.batch + 1;
            Ok(())
        },
    ));
    let handle = worker.handle();
    let mut ready = ok(handle.wait_ready());
    for handoff in 0..8 {
        let data = ready.data();
        // This fails in the old by-value implementation: the header has moved
        // from the worker stack into this thread's ReadyLease.
        data.observe_address();
        assert_eq!(data.slot, handoff % 2);
        assert_eq!(data.batch, handoff + 1);
        assert_eq!(data.handoff, handoff);
        let previous_slot = data.slot;
        ok(ready.release_with(move |old| {
            old.observe_address();
            assert_ne!(old.slot, previous_slot, "handoff must modify old, not the returned publication");
            old.handoff = handoff + 1;
        }));
        ready = ok(handle.wait_ready());
    }
    ready.data().observe_address();
    assert_eq!(ready.data().handoff, 8);
    assert_ne!(observations.addresses[0].load(Ordering::SeqCst),
        observations.addresses[1].load(Ordering::SeqCst));
    let retained_slot = ready.data().slot;
    ok(worker.shutdown());
    assert_eq!(observations.drops[retained_slot].load(Ordering::SeqCst), 0);
    assert_eq!(observations.drops[1 - retained_slot].load(Ordering::SeqCst), 1);
    drop(ready);
    drop(handle);
    drop(worker);
    for count in &observations.drops {
        assert_eq!(count.load(Ordering::SeqCst), 1, "each payload must be destroyed exactly once");
    }
}

#[test]
fn panicking_runner_retains_the_previous_payload_until_the_last_owner_drops() {
    let observations = Arc::new(Observations::default());
    let mut first = true;
    let mut worker = ok(Worker::<(), Payload>::spawn(
        Payload::new(0, &observations), Payload::new(1, &observations), Vec::new(),
        move |_, _, _, _| {
            if first {
                first = false;
                Ok(())
            } else {
                panic!("authored second-batch failure");
            }
        },
    ));
    let handle = worker.handle();
    let last_owner = handle.clone();
    let ready = ok(handle.wait_ready());
    let previous_slot = ready.data().slot;
    ok(ready.release());
    assert_eq!(handle.wait_ready().err().unwrap().message, "simulation worker panicked");
    assert_eq!(worker.shutdown().err().unwrap().message, "simulation worker panicked");
    assert_eq!(observations.drops[previous_slot].load(Ordering::SeqCst), 0);
    assert_eq!(observations.drops[1 - previous_slot].load(Ordering::SeqCst), 1);
    drop(handle);
    drop(worker);
    assert_eq!(observations.drops[previous_slot].load(Ordering::SeqCst), 0);
    drop(last_owner);
    for count in &observations.drops {
        assert_eq!(count.load(Ordering::SeqCst), 1, "fault retention must not leak or double-drop a payload");
    }
}
