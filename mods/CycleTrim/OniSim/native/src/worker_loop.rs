//! The worker half never executes simulation code while holding the mailbox.

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::Arc;

use super::Shared;
use crate::status::{Error, Result, Status};
use crate::world::Region;

pub(super) fn entry<F, D, R>(
    shared: Arc<Shared<F, D>>,
    output: D,
    old: D,
    regions: Vec<Region>,
    runner: R,
) where
    F: Send + 'static,
    D: Send + 'static,
    R: FnMut(Vec<F>, &[Region], &mut D, &D) -> Result<()>,
{
    let _float_environment = match crate::float_env::FloatEnvGuard::enter() {
        Ok(environment) => environment,
        Err(error) => {
            shared.fail(error);
            return;
        }
    };
    let outcome = catch_unwind(AssertUnwindSafe(|| {
        run(&shared, output, old, regions, runner);
    }));
    if outcome.is_err() {
        shared.fail(Error::new(
            Status::InternalFailure,
            "simulation worker panicked",
        ));
    }
}

fn run<F, D, R>(
    shared: &Shared<F, D>,
    mut output: D,
    mut old: D,
    mut regions: Vec<Region>,
    mut runner: R,
) where
    R: FnMut(Vec<F>, &[Region], &mut D, &D) -> Result<()>,
{
    loop {
        let frames = {
            let mut mailbox = shared.lock();
            if mailbox.stopping || mailbox.fault.is_some() {
                return;
            }
            if let Some(latest) = mailbox.latest_regions.take() {
                regions = latest;
            }
            std::mem::take(&mut mailbox.frames)
        };

        // The closure exclusively owns active simulation state. Submissions
        // accepted now remain in the mailbox for the next BeginFrameProcessing.
        if let Err(error) = runner(frames, &regions, &mut output, &old) {
            shared.fail(error);
            return;
        }

        let mut returned = {
            let mut mailbox = shared.lock();
            if mailbox.stopping || mailbox.fault.is_some() {
                return;
            }
            mailbox.ready = Some(output);
            shared.changed.notify_all();
            loop {
                if mailbox.stopping || mailbox.fault.is_some() {
                    return;
                }
                if let Some(returned) = mailbox.returned.take() {
                    break returned;
                }
                mailbox = shared.wait(mailbox);
            }
        };
        if let Some(handoff) = returned.handoff.take() {
            handoff(&mut old);
        }
        output = old;
        old = returned.data;
    }
}
