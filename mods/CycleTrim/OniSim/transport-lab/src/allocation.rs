#[cfg(feature = "allocation-count")]
use std::alloc::{GlobalAlloc, Layout, System};
use std::sync::atomic::{AtomicU64, Ordering};
pub static CALLS: AtomicU64 = AtomicU64::new(0);
pub static BYTES: AtomicU64 = AtomicU64::new(0);
#[cfg(feature = "allocation-count")]
struct Counter;
#[cfg(feature = "allocation-count")]
unsafe impl GlobalAlloc for Counter {
    unsafe fn alloc(&self, l: Layout) -> *mut u8 {
        CALLS.fetch_add(1, Ordering::Relaxed);
        BYTES.fetch_add(l.size() as u64, Ordering::Relaxed);
        System.alloc(l)
    }
    unsafe fn dealloc(&self, p: *mut u8, l: Layout) {
        System.dealloc(p, l)
    }
    unsafe fn realloc(&self, p: *mut u8, l: Layout, s: usize) -> *mut u8 {
        CALLS.fetch_add(1, Ordering::Relaxed);
        BYTES.fetch_add(s as u64, Ordering::Relaxed);
        System.realloc(p, l, s)
    }
}
#[cfg(feature = "allocation-count")]
#[global_allocator]
static ALLOCATOR: Counter = Counter;
pub fn reset() {
    CALLS.store(0, Ordering::Relaxed);
    BYTES.store(0, Ordering::Relaxed);
}
pub fn read() -> (u64, u64) {
    (CALLS.load(Ordering::Relaxed), BYTES.load(Ordering::Relaxed))
}
