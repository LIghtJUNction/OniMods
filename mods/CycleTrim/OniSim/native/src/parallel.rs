//! Bounded, reusable workers for kernels with exclusive cell ownership.
use std::sync::OnceLock;

use rayon::ThreadPool;

pub(crate) const MIN_PARALLEL_CELLS: usize = 8192;
pub(crate) const CHUNK_CELLS: usize = 2048;

pub(crate) fn pool(work: usize) -> Option<&'static ThreadPool> {
    if work < MIN_PARALLEL_CELLS {
        return None;
    }
    static POOL: OnceLock<Option<ThreadPool>> = OnceLock::new();
    POOL.get_or_init(|| {
        let available = std::thread::available_parallelism().map_or(1, usize::from);
        let workers = std::env::var("CYCLETRIM_ONISIM_WORKERS")
            .ok()
            .and_then(|value| value.parse::<usize>().ok())
            .filter(|&value| value > 0)
            .unwrap_or(available)
            .min(available)
            .min(8);
        if workers <= 1 {
            return None;
        }
        // Resource exhaustion leaves the serial implementation available.
        rayon::ThreadPoolBuilder::new()
            .num_threads(workers)
            .thread_name(|index| format!("OniSim-cell-{index}"))
            .build()
            .ok()
    })
    .as_ref()
}
