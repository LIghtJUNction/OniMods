//! Count allocation operations in a separate build. Never use this build to time.
use std::alloc::{GlobalAlloc, Layout, System};
use std::fs::File;
use std::io::{BufWriter, Write};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};

use crate::benchmark::{self, Algorithm, Kind};
use crate::shared::disease_table;

struct Counting;
static ACTIVE: AtomicBool = AtomicBool::new(false);
static ALLOCS: AtomicU64 = AtomicU64::new(0);
static REALLOCS: AtomicU64 = AtomicU64::new(0);
static REQUESTED: AtomicU64 = AtomicU64::new(0);
static LIVE: AtomicU64 = AtomicU64::new(0);
static PEAK: AtomicU64 = AtomicU64::new(0);

#[global_allocator]
static ALLOCATOR: Counting = Counting;

fn add_live(size: usize) {
    let live = LIVE.fetch_add(size as u64, Ordering::Relaxed) + size as u64;
    PEAK.fetch_max(live, Ordering::Relaxed);
}

unsafe impl GlobalAlloc for Counting {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let pointer = unsafe { System.alloc(layout) };
        if !pointer.is_null() && ACTIVE.load(Ordering::Relaxed) {
            ALLOCS.fetch_add(1, Ordering::Relaxed);
            REQUESTED.fetch_add(layout.size() as u64, Ordering::Relaxed);
            add_live(layout.size());
        }
        pointer
    }

    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        let pointer = unsafe { System.alloc_zeroed(layout) };
        if !pointer.is_null() && ACTIVE.load(Ordering::Relaxed) {
            ALLOCS.fetch_add(1, Ordering::Relaxed);
            REQUESTED.fetch_add(layout.size() as u64, Ordering::Relaxed);
            add_live(layout.size());
        }
        pointer
    }

    unsafe fn dealloc(&self, pointer: *mut u8, layout: Layout) {
        if ACTIVE.load(Ordering::Relaxed) {
            LIVE.fetch_sub(layout.size() as u64, Ordering::Relaxed);
        }
        unsafe { System.dealloc(pointer, layout) };
    }

    unsafe fn realloc(&self, pointer: *mut u8, layout: Layout, size: usize) -> *mut u8 {
        let new_pointer = unsafe { System.realloc(pointer, layout, size) };
        if !new_pointer.is_null() && ACTIVE.load(Ordering::Relaxed) {
            REALLOCS.fetch_add(1, Ordering::Relaxed);
            REQUESTED.fetch_add(size as u64, Ordering::Relaxed);
            LIVE.fetch_sub(layout.size() as u64, Ordering::Relaxed);
            add_live(size);
        }
        new_pointer
    }
}

fn count<F: FnOnce()>(function: F) -> (u64, u64, u64, u64, u64) {
    for counter in [&ALLOCS, &REALLOCS, &REQUESTED, &LIVE, &PEAK] {
        counter.store(0, Ordering::Relaxed);
    }
    ACTIVE.store(true, Ordering::Relaxed);
    function();
    ACTIVE.store(false, Ordering::Relaxed);
    (
        ALLOCS.load(Ordering::Relaxed),
        REALLOCS.load(Ordering::Relaxed),
        REQUESTED.load(Ordering::Relaxed),
        PEAK.load(Ordering::Relaxed),
        LIVE.load(Ordering::Relaxed),
    )
}

pub(crate) fn run(path: &str) {
    let table = disease_table(false);
    let mut writer = BufWriter::new(File::create(path).expect("create allocation CSV"));
    writeln!(writer, "case_id,kind,width,height,max_depth,radius,emitters,visited_cells,algorithm,alloc_calls,realloc_calls,total_requested_bytes,peak_live_bytes,live_bytes_at_end").unwrap();
    for case in benchmark::cases() {
        let fixture = benchmark::fixture(case);
        for algorithm in [Algorithm::Baseline, Algorithm::Candidate] {
            let statistics = match case.kind {
                Kind::Search => {
                    let emitter = fixture.emitters[0];
                    let x = emitter.cell % fixture.world.stride as i32;
                    let y = emitter.cell / fixture.world.stride as i32;
                    count(|| match algorithm {
                        Algorithm::Baseline => {
                            benchmark::baseline_search(&fixture.world, x, y, case.depth);
                        }
                        Algorithm::Candidate => {
                            benchmark::candidate_search(&fixture.world, x, y, case.depth);
                        }
                    })
                }
                Kind::Update => {
                    let mut world = fixture.world.clone();
                    match algorithm {
                        Algorithm::Baseline => {
                            let mut data: Vec<_> =
                                fixture.emitters.iter().map(|e| e.baseline()).collect();
                            count(|| {
                                benchmark::baseline_update(
                                    &mut world,
                                    &table,
                                    &fixture.region,
                                    &mut data,
                                )
                            })
                        }
                        Algorithm::Candidate => {
                            let mut data: Vec<_> =
                                fixture.emitters.iter().map(|e| e.candidate()).collect();
                            count(|| {
                                benchmark::candidate_update(
                                    &mut world,
                                    &table,
                                    &fixture.region,
                                    &mut data,
                                )
                            })
                        }
                    }
                }
            };
            let (allocations, reallocations, requested, peak, live) = statistics;
            assert_eq!(
                live, 0,
                "temporary allocation scope must end with no live storage"
            );
            writeln!(writer, "{},{},{},{},{},{},{},{},{},{allocations},{reallocations},{requested},{peak},{live}",
                case.id(), case.kind.name(), case.width, case.height, case.depth, case.depth - 1,
                case.emitter_count, fixture.visited_cells, algorithm.name()).unwrap();
        }
    }
}
