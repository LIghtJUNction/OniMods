//! Owned simulation batches and the FrameSync publication barrier.
//!
//! The runner owns simulation state (directly or through a separate Engine
//! mutex), never the global FFI State mutex. A ready result must be returned by
//! Prepare before the runner may reuse either of its two GameData buffers.

use std::panic::{AssertUnwindSafe, catch_unwind, resume_unwind};
use std::sync::{Arc, Condvar, Mutex, MutexGuard};
use std::thread::{self, JoinHandle};

use crate::status::{Error, Result, Status};
use crate::world::Region;

#[path = "worker_loop.rs"]
mod run;

pub(crate) const MAX_QUEUED_FRAMES: usize = 30;

type Handoff<D> = Box<dyn FnOnce(&mut D) + Send + 'static>;

struct Returned<D> {
    data: D,
    handoff: Option<Handoff<D>>,
}

struct Fault {
    status: Status,
    message: String,
}

impl Fault {
    fn error(&self) -> Error {
        Error::new(self.status, self.message.clone())
    }
}

struct Mailbox<F, D> {
    frames: Vec<F>,
    latest_regions: Option<Vec<Region>>,
    ready: Option<D>,
    returned: Option<Returned<D>>,
    leased: bool,
    stopping: bool,
    fault: Option<Fault>,
}

struct Shared<F, D> {
    mailbox: Mutex<Mailbox<F, D>>,
    changed: Condvar,
}

impl<F, D> Shared<F, D> {
    fn lock(&self) -> MutexGuard<'_, Mailbox<F, D>> {
        match self.mailbox.lock() {
            Ok(guard) => guard,
            Err(poison) => {
                let mut guard = poison.into_inner();
                self.poisoned(&mut guard);
                guard
            }
        }
    }

    fn wait<'a>(&self, guard: MutexGuard<'a, Mailbox<F, D>>) -> MutexGuard<'a, Mailbox<F, D>> {
        match self.changed.wait(guard) {
            Ok(guard) => guard,
            Err(poison) => {
                let mut guard = poison.into_inner();
                self.poisoned(&mut guard);
                guard
            }
        }
    }

    fn poisoned(&self, mailbox: &mut Mailbox<F, D>) {
        mailbox.fault.get_or_insert_with(|| Fault {
            status: Status::InternalFailure,
            message: "simulation worker mailbox is poisoned".into(),
        });
        mailbox.stopping = true;
        self.changed.notify_all();
    }

    fn fail(&self, error: Error) {
        let mut mailbox = self.lock();
        mailbox.fault.get_or_insert(Fault {
            status: error.status,
            message: error.message,
        });
        self.changed.notify_all();
    }

    fn request_stop(&self) {
        self.lock().stopping = true;
        self.changed.notify_all();
    }
}

fn available<F, D>(mailbox: &Mailbox<F, D>) -> Result<()> {
    if let Some(fault) = &mailbox.fault {
        return Err(fault.error());
    }
    if mailbox.stopping {
        return Err(Error::new(
            Status::InvalidState,
            "simulation worker is shut down",
        ));
    }
    Ok(())
}

pub(crate) struct Worker<F: Send + 'static, D: Send + 'static> {
    shared: Arc<Shared<F, D>>,
    thread: Option<JoinHandle<()>>,
}

/// A State-independent submission/wait handle. Clone it while holding State,
/// then release State before waiting. Only Worker retains the join handle.
pub(crate) struct WorkerHandle<F: Send + 'static, D: Send + 'static> {
    shared: Arc<Shared<F, D>>,
}

impl<F: Send + 'static, D: Send + 'static> Clone for WorkerHandle<F, D> {
    fn clone(&self) -> Self {
        Self {
            shared: Arc::clone(&self.shared),
        }
    }
}

impl<F: Send + 'static, D: Send + 'static> Worker<F, D> {
    /// Like the official Sim thread, starts with one batch even if no frames
    /// are queued. After each Prepare release the next batch may also be empty.
    pub(crate) fn spawn<R>(output: D, old: D, regions: Vec<Region>, runner: R) -> Result<Self>
    where
        R: FnMut(Vec<F>, &[Region], &mut D, &D) -> Result<()> + Send + 'static,
    {
        let shared = Arc::new(Shared {
            mailbox: Mutex::new(Mailbox {
                frames: Vec::new(),
                latest_regions: None,
                ready: None,
                returned: None,
                leased: false,
                stopping: false,
                fault: None,
            }),
            changed: Condvar::new(),
        });
        let worker_shared = Arc::clone(&shared);
        let thread = thread::Builder::new()
            .name("onisim-physics".into())
            .spawn(move || run::entry(worker_shared, output, old, regions, runner))
            .map_err(|error| {
                Error::new(
                    Status::ResourceLimit,
                    format!("could not start simulation worker: {error}"),
                )
            })?;
        Ok(Self {
            shared,
            thread: Some(thread),
        })
    }

    pub(crate) fn handle(&self) -> WorkerHandle<F, D> {
        WorkerHandle {
            shared: Arc::clone(&self.shared),
        }
    }

    /// Wake either side of the publication barrier and join without requiring
    /// a Prepare release. Call with the global State mutex released.
    pub(crate) fn shutdown(&mut self) -> Result<()> {
        self.shared.request_stop();
        if self
            .thread
            .as_ref()
            .is_some_and(|thread| thread.thread().id() == thread::current().id())
        {
            // Preserve the handle so an external owner can join after a
            // reentrant worker callback has requested shutdown.
            return Err(Error::new(
                Status::InvalidState,
                "simulation worker cannot join itself",
            ));
        }
        if let Some(thread) = self.thread.take()
            && thread.join().is_err()
        {
            self.shared.fail(Error::new(
                Status::InternalFailure,
                "simulation worker terminated during shutdown",
            ));
        }
        if let Some(fault) = &self.shared.lock().fault {
            return Err(fault.error());
        }
        Ok(())
    }
}

impl<F: Send + 'static, D: Send + 'static> WorkerHandle<F, D> {
    /// Capacity/allocation checks happen BEFORE capture seals current commands.
    /// Capture must only move already-owned queues: no Engine locks, parsing,
    /// allocation, or callbacks. Failure leaves capture completely uncalled.
    pub(crate) fn submit_with<C>(&self, capture: C) -> Result<()>
    where
        C: FnOnce() -> (F, Vec<Region>),
    {
        let mut mailbox = self.shared.lock();
        available(&mailbox)?;
        if mailbox.frames.len() == MAX_QUEUED_FRAMES {
            return Err(Error::new(
                Status::ResourceLimit,
                "queued frame limit exceeded",
            ));
        }
        mailbox.frames.try_reserve(1).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "could not reserve simulation frame queue",
            )
        })?;
        let (frame, latest_regions) = match catch_unwind(AssertUnwindSafe(capture)) {
            Ok(captured) => captured,
            Err(panic) => {
                mailbox.fault.get_or_insert_with(|| Fault {
                    status: Status::InternalFailure,
                    message: "simulation frame capture panicked".into(),
                });
                self.shared.changed.notify_all();
                drop(mailbox);
                // FFI containment also marks the partially captured State
                // unusable. Waiters were notified before propagating this panic.
                resume_unwind(panic);
            }
        };
        mailbox.frames.push(frame);
        mailbox.latest_regions = Some(latest_regions);
        self.shared.changed.notify_all();
        Ok(())
    }

    /// Call after releasing the global State mutex. The returned lease holds
    /// no mailbox lock; copying it into the stable public view cannot block
    /// submissions. Only one Prepare may own a lease at a time.
    pub(crate) fn wait_ready(&self) -> Result<ReadyLease<F, D>> {
        let mut mailbox = self.shared.lock();
        loop {
            available(&mailbox)?;
            if mailbox.leased {
                return Err(Error::new(
                    Status::InvalidState,
                    "another Prepare owns the ready simulation frame",
                ));
            }
            if let Some(data) = mailbox.ready.take() {
                mailbox.leased = true;
                return Ok(ReadyLease {
                    shared: Arc::clone(&self.shared),
                    data: Some(data),
                });
            }
            mailbox = self.shared.wait(mailbox);
        }
    }

    pub(crate) fn check(&self) -> Result<()> {
        available(&self.shared.lock())
    }

    /// Observe readiness without consuming the result or releasing the worker.
    pub(crate) fn is_ready(&self) -> Result<bool> {
        let mailbox = self.shared.lock();
        available(&mailbox)?;
        Ok(mailbox.ready.is_some() && !mailbox.leased)
    }

    pub(crate) fn request_stop(&self) {
        self.shared.request_stop();
    }
}

impl<F: Send + 'static, D: Send + 'static> Drop for Worker<F, D> {
    fn drop(&mut self) {
        let _ = self.shutdown();
    }
}

pub(crate) struct ReadyLease<F: Send + 'static, D: Send + 'static> {
    shared: Arc<Shared<F, D>>,
    data: Option<D>,
}

impl<F: Send + 'static, D: Send + 'static> ReadyLease<F, D> {
    pub(crate) fn data(&self) -> &D {
        self.data
            .as_ref()
            .expect("ready lease is consumed only by release")
    }

    pub(crate) fn data_mut(&mut self) -> &mut D {
        self.data
            .as_mut()
            .expect("ready lease is consumed only by release")
    }

    pub(crate) fn release(self) -> Result<()> {
        self.release_inner(None)
    }

    /// Official Prepare writes visibility into the CURRENT old GameData,
    /// rather than the ready result. Run this handoff on old, outside locks,
    /// before swapping the two buffers. This preserves the native mask delay.
    pub(crate) fn release_with<H>(self, handoff: H) -> Result<()>
    where
        H: FnOnce(&mut D) + Send + 'static,
    {
        self.release_inner(Some(Box::new(handoff)))
    }

    fn release_inner(mut self, handoff: Option<Handoff<D>>) -> Result<()> {
        let data = self.data.take().expect("ready lease is consumed only once");
        let mut mailbox = self.shared.lock();
        available(&mailbox)?;
        if !mailbox.leased || mailbox.returned.is_some() || mailbox.ready.is_some() {
            drop(mailbox);
            self.shared.fail(Error::new(
                Status::InternalFailure,
                "simulation publication barrier ownership is inconsistent",
            ));
            return Err(Error::new(
                Status::InternalFailure,
                "simulation publication barrier ownership is inconsistent",
            ));
        }
        mailbox.returned = Some(Returned { data, handoff });
        mailbox.leased = false;
        self.shared.changed.notify_all();
        Ok(())
    }
}

impl<F: Send + 'static, D: Send + 'static> Drop for ReadyLease<F, D> {
    fn drop(&mut self) {
        if self.data.is_none() {
            return;
        }
        let mut mailbox = self.shared.lock();
        mailbox.leased = false;
        if !mailbox.stopping {
            mailbox.fault.get_or_insert_with(|| Fault {
                status: Status::InternalFailure,
                message: "Prepare abandoned a ready simulation frame".into(),
            });
        }
        self.shared.changed.notify_all();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::{
        atomic::{AtomicUsize, Ordering},
        mpsc,
    };
    use std::time::{Duration, Instant};

    fn ok<T>(result: Result<T>) -> T {
        result.unwrap_or_else(|error| panic!("{}", error.message))
    }

    #[test]
    fn full_queue_does_not_capture_or_drop_commands() {
        let (started, entered) = mpsc::channel();
        let (release, gate) = mpsc::channel();
        let mut worker = ok(Worker::<usize, Vec<usize>>::spawn(
            Vec::new(),
            Vec::new(),
            Vec::new(),
            move |frames, _, output, _| {
                if frames.is_empty() {
                    started.send(()).unwrap();
                    gate.recv_timeout(Duration::from_secs(2)).unwrap();
                }
                *output = frames;
                Ok(())
            },
        ));
        let handle = worker.handle();
        entered.recv_timeout(Duration::from_secs(2)).unwrap();
        for frame in 0..MAX_QUEUED_FRAMES {
            ok(handle.submit_with(|| (frame, Vec::new())));
        }
        let captured = AtomicUsize::new(0);
        let error = handle
            .submit_with(|| {
                captured.fetch_add(1, Ordering::SeqCst);
                (999, Vec::new())
            })
            .expect_err("full queue must fail");
        assert!(matches!(error.status, Status::ResourceLimit));
        assert_eq!(captured.load(Ordering::SeqCst), 0);
        release.send(()).unwrap();
        let empty = ok(handle.wait_ready());
        assert!(empty.data().is_empty());
        ok(empty.release());
        let batch = ok(handle.wait_ready());
        assert_eq!(batch.data(), &(0..MAX_QUEUED_FRAMES).collect::<Vec<_>>());
        // Shutdown must also tolerate an outstanding publication lease.
        ok(worker.shutdown());
        drop(batch);
    }

    #[test]
    fn worker_failure_wakes_waiters_and_rejects_later_submissions() {
        let (started, entered) = mpsc::channel();
        let (release, gate) = mpsc::channel();
        let mut worker = ok(Worker::<(), ()>::spawn(
            (),
            (),
            Vec::new(),
            move |_, _, _, _| {
                started.send(()).unwrap();
                gate.recv_timeout(Duration::from_secs(2)).unwrap();
                Err(Error::new(Status::InvalidState, "authored runner failure"))
            },
        ));
        entered.recv_timeout(Duration::from_secs(2)).unwrap();
        let handle = worker.handle();
        let waiting = handle.clone();
        let (completed, result) = mpsc::channel();
        let waiter = thread::spawn(move || {
            let error = waiting
                .wait_ready()
                .err()
                .expect("failed runner cannot publish");
            completed
                .send((error.status as i32, error.message))
                .unwrap();
        });
        release.send(()).unwrap();
        assert_eq!(
            result.recv_timeout(Duration::from_secs(2)).unwrap(),
            (
                Status::InvalidState as i32,
                "authored runner failure".into()
            )
        );
        waiter.join().unwrap();
        assert!(matches!(
            handle
                .submit_with(|| panic!("failed worker must not capture"))
                .err()
                .unwrap()
                .status,
            Status::InvalidState
        ));
        assert_eq!(
            worker.shutdown().err().unwrap().message,
            "authored runner failure"
        );
    }

    #[test]
    fn shutdown_does_not_require_releasing_an_outstanding_publication() {
        let mut worker = ok(Worker::<(), ()>::spawn((), (), Vec::new(), |_, _, _, _| {
            Ok(())
        }));
        let handle = worker.handle();
        let lease = ok(handle.wait_ready());
        // Keep the worker behind its barrier, then stop it. A subsequent
        // Prepare must return immediately, even though this lease was not released.
        ok(worker.shutdown());
        assert!(matches!(
            handle.wait_ready().err().unwrap().status,
            Status::InvalidState
        ));
        assert!(matches!(
            handle.is_ready().err().unwrap().status,
            Status::InvalidState
        ));
        drop(lease);
        ok(worker.shutdown());
    }

    #[test]
    fn repeated_batches_keep_owned_frames_ordered_across_shutdowns() {
        for session in 0..16 {
            let mut worker = ok(Worker::<usize, Vec<usize>>::spawn(
                Vec::new(),
                Vec::new(),
                Vec::new(),
                |frames, _, output, _| {
                    *output = frames;
                    Ok(())
                },
            ));
            let handle = worker.handle();
            let mut ready = ok(handle.wait_ready());
            assert!(ready.data().is_empty());
            for batch in 0..64 {
                let frames = (0..MAX_QUEUED_FRAMES)
                    .map(|item| (session * 64 + batch) * MAX_QUEUED_FRAMES + item)
                    .collect::<Vec<_>>();
                for &frame in &frames {
                    ok(handle.submit_with(|| (frame, Vec::new())));
                }
                ok(ready.release());
                ready = ok(handle.wait_ready());
                assert_eq!(ready.data(), &frames);
            }
            ok(worker.shutdown());
            drop(ready);
            assert!(matches!(
                handle.check().err().unwrap().status,
                Status::InvalidState
            ));
        }
    }

    #[test]
    fn abandoned_lease_and_capture_panic_make_failure_observable() {
        let mut abandoned = ok(Worker::<(), ()>::spawn((), (), Vec::new(), |_, _, _, _| {
            Ok(())
        }));
        let handle = abandoned.handle();
        drop(ok(handle.wait_ready()));
        assert_eq!(
            handle.wait_ready().err().unwrap().message,
            "Prepare abandoned a ready simulation frame"
        );
        assert_eq!(
            abandoned.shutdown().err().unwrap().message,
            "Prepare abandoned a ready simulation frame"
        );

        let mut panicked = ok(Worker::<(), ()>::spawn((), (), Vec::new(), |_, _, _, _| {
            Ok(())
        }));
        let handle = panicked.handle();
        let lease = ok(handle.wait_ready());
        assert!(
            catch_unwind(AssertUnwindSafe(|| {
                let _ = handle.submit_with(|| panic!("authored capture panic"));
            }))
            .is_err()
        );
        assert_eq!(
            handle.check().err().unwrap().message,
            "simulation frame capture panicked"
        );
        assert_eq!(
            panicked.shutdown().err().unwrap().message,
            "simulation frame capture panicked"
        );
        drop(lease);
    }

    #[test]
    fn readiness_observation_preserves_the_publication_barrier() {
        let calls = Arc::new(AtomicUsize::new(0));
        let running_calls = calls.clone();
        let (started, entered) = mpsc::channel();
        let (release, gate) = mpsc::channel();
        let mut worker = ok(Worker::<(), u32>::spawn(
            0,
            0,
            Vec::new(),
            move |_, _, output, _| {
                if running_calls.fetch_add(1, Ordering::SeqCst) == 0 {
                    let _ = started.send(());
                    gate.recv_timeout(Duration::from_secs(2)).map_err(|_| {
                        Error::new(Status::InternalFailure, "test runner gate timed out")
                    })?;
                }
                *output = 42;
                Ok(())
            },
        ));
        let handle = worker.handle();
        entered
            .recv_timeout(Duration::from_secs(2))
            .expect("worker entered runner");
        assert!(!ok(handle.is_ready()));
        release.send(()).expect("release test runner");
        let deadline = Instant::now() + Duration::from_secs(2);
        while !ok(handle.is_ready()) {
            assert!(Instant::now() < deadline, "worker did not become ready");
            std::thread::yield_now();
        }
        assert!(ok(handle.is_ready()));
        assert!(ok(handle.is_ready()));
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        let lease = ok(handle.wait_ready());
        assert_eq!(*lease.data(), 42);
        assert!(!ok(handle.is_ready()));
        assert_eq!(calls.load(Ordering::SeqCst), 1);
        ok(lease.release());
        ok(worker.shutdown());
        assert!(handle.is_ready().is_err());
    }
}
