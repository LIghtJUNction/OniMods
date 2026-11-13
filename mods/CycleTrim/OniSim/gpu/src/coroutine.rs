//! Cooperative execution on the existing simulation worker, without a new
//! process, background runtime thread, or blocking-task pool.

use std::cell::RefCell;
use std::future::Future;

thread_local! {
    static EXECUTOR: RefCell<Option<tokio::runtime::Runtime>> = const { RefCell::new(None) };
}

/// Drive a simulation coroutine and its timers on the caller's current thread.
/// Call only at a synchronous simulation boundary, never inside another runtime.
pub fn run<F: Future>(future: F) -> F::Output {
    EXECUTOR.with(|slot| {
        let mut runtime = slot.borrow_mut();
        let runtime = runtime.get_or_insert_with(|| {
            tokio::runtime::Builder::new_current_thread()
                .enable_time()
                .build()
                .expect("simulation coroutine runtime initialization failed")
        });
        runtime.block_on(future)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn waiting_coroutine_runs_ready_tasks_on_the_same_thread() {
        let caller = std::thread::current().id();
        run(async {
            let (sender, receiver) = tokio::sync::oneshot::channel();
            let task = tokio::spawn(async move {
                tokio::task::yield_now().await;
                sender.send(std::thread::current().id()).unwrap();
            });
            assert_eq!(receiver.await.unwrap(), caller);
            task.await.unwrap();
        });
        // The cached executor is reusable at subsequent native boundaries.
        assert_eq!(run(async { std::thread::current().id() }), caller);
    }

    #[test]
    fn dropping_task_group_cancels_suspended_work() {
        use std::sync::{
            Arc,
            atomic::{AtomicBool, Ordering},
        };
        struct Guard(Arc<AtomicBool>);
        impl Drop for Guard {
            fn drop(&mut self) {
                self.0.store(true, Ordering::SeqCst);
            }
        }
        run(async {
            let dropped = Arc::new(AtomicBool::new(false));
            let guard = Guard(dropped.clone());
            let mut group = tokio::task::JoinSet::new();
            group.spawn(async move {
                let _guard = guard;
                std::future::pending::<()>().await;
            });
            tokio::task::yield_now().await;
            group.shutdown().await;
            assert!(dropped.load(Ordering::SeqCst));
        });
    }
}
