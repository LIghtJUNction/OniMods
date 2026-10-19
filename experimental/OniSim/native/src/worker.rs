//! Owned simulation batches and the FrameSync publication barrier.
//!
//! The runner owns simulation state (directly or through a separate Engine
//! mutex), never the global FFI State mutex. A ready result must be returned by
//! Prepare before the runner may reuse either of its two GameData buffers.

use std::panic::{catch_unwind, resume_unwind, AssertUnwindSafe};
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
        if let Some(thread) = self.thread.take() {
            if thread.join().is_err() {
                self.shared.fail(Error::new(
                    Status::InternalFailure,
                    "simulation worker terminated during shutdown",
                ));
            }
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
