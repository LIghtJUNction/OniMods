# Native frame performance work

## Current source baseline, 2026-10-26

Source `35f9785` rebuilds to Linux native
`e8bfcb479c1a093cebfb9239c881c6051ef36a116dd15cccad1d17c9f66e8056`.
Two follow-up experiments are rejected: moving the temperature threshold guard
before element lookups, and enabling Thin LTO with one codegen unit. Each has
eight alternating pairs in all six 96x96 scenes, two warmup batches and six timed
batches per sample, on CPU0 with one worker. All 192 timed final states match
the installed official library exactly, with zero timed major faults. The LTO
file separately passes all 287 conformance cases at one worker.

Neither experiment meets the unchanged CPU performance gates. CPU frequencies
vary substantially within each run, so these measurements do not establish
stable regression magnitudes or a speedup. The source edit is reverted and the
release profile remains unchanged. No 192x192, four-worker, actual-game or FPS
acceptance is claimed for either experiment. See the
[rejection receipt](conformance/results/linux-744825-thermal-guard-lto.json).

## Earlier 2772e526 measurements

The earlier source-built native candidate is
`2772e52680f74a953a8fd18be396e2718ea1ccba1b7dad4fa738bc9cb98dc76f`.
The numeric helper is adopted in source and retained in the current candidate.
The earlier C301/EA8 native/managed pair passed targeted Steam headless and graphical
small-colony acceptance; see RUNTIME-VALIDATION.md. Those receipts do not approve
the new native identity. These earlier runs had host load
and paging, so no whole-game FPS improvement is established. Rejected
variants below have separate identities and are not production replacements.

## Direct official comparison of the current core

The current 2772e526 Rust file and pinned official 85111cd0 library were measured
on CPU16, one worker, eight alternating pairs in each 96x96 scene. All 96 final
states match official exactly, with zero timed major faults. The existing fixed
gates pass: no CPU regression above 3%, one CPU reduction at least 10%, and no
peak RSS increase above 3%. All ranges and live-host context remain recorded.

| Scene | CPU change versus official | Elapsed change |
| --- | ---: | ---: |
| Fluid | -9.52% | -9.30% |
| Thermal | -22.19% | -22.18% |
| Sealed | -22.46% | -22.14% |
| Germ flow | -8.65% | -8.64% |
| Mixed germs | -8.91% | -9.00% |
| Vacuum | -51.99% | -51.59% |

Native child-process peak RSS falls 9.24-60.30% across these authored fixtures.
This includes library/fixture allocation differences and does not establish
Unity/Mono or whole-game memory savings. These are direct official comparisons,
not additive combinations of earlier Rust-to-Rust gains. See the
[exact receipt](conformance/results/linux-744825-pressure-sweep-official.json).

## Current one/four-worker scaling

The same 2772e526 file is also measured with one versus four workers on
CPU0/2/4/6. All 156 final states match official and no timed major faults occur.
Every four-worker sample observes all four persistent worker names and positive
total scheduler runtime. Both 96x96 and 192x192 still fail the new strict gates
and the older scaling criteria: several scenes regress CPU or elapsed time.

A separate 36-sample PMU run compares the same worker configurations. Retired
instructions increase 2.14% germ-flow, 2.14% mixed-germs and 0.80% thermal; cycles
increase 7.92%, 6.52% and 11.10%. This does not isolate scheduling, frequency or
cache effects. It establishes that merely adding threads does not remove the
whole-frame limiter. No default-thread policy is changed from these results.
All counters/outliers remain, with running/enabled at least 98%. See the
[scaling receipt](conformance/results/linux-744825-pressure-sweep-scaling.json).

## Current borrowed pressure sweep

The adopted 2772e526 source borrows the fixed-size pressure arrays once per
serial sweep. Inlining reuses this storage context without copying worlds or
adding heap buffers. Checked indexing, floating expressions, transfer order,
ordered notification/deduplication and dirty bits remain. The old transfer
entry point has no remaining callers and is removed.

Forty-three release Rust tests and 50 pure Python tests pass. New exact-flow-word
fixtures extend the official corpus to 287 cases / 9739 publications; both old
and new natives pass at one/four workers. An independent build from e0e0fe7 plus
the public patch reproduces the exact measured 2772e526 file. Source-body and
export comparisons support review but do not replace executed conformance.

Eight alternating pairs per scene at 96x96 and five at 192x192 compare against
8bd415ce on CPU0/2/4/6, four workers on both sides. All 156 final states match
official, with no timed major faults. Both sizes pass all fixed native gates.

| Scene | 96x96 CPU change | 192x192 CPU change |
| --- | ---: | ---: |
| Fluid | -2.56% | -9.36% |
| Thermal | +0.70% | +2.50% |
| Sealed | -8.44% | -5.15% |
| Germ flow | -7.50% | -10.66% |
| Mixed germs | -7.87% | -9.11% |
| Vacuum | -16.31% | -13.48% |

The separate 24-sample CPU-core PMU run observes fluid instructions -13.38%,
cycles -8.19%; mixed-germ instructions -12.36%, cycles -8.20%. Physics-thread
instructions fall -13.81% / -13.72%; all final states match official and counter
running/enabled is at least 98%. These counts support reduced main-thread work.

Other host workloads remain running. All sample ranges, load/frequency records
and RSS observations are retained. These incremental Rust fixture results are
not direct official-library timing or whole-game FPS/RAM improvements. The new native/managed pair has passed one Steam headless save/load roundtrip;
actual worker names, graphical and concurrent complete-game acceptance remain
pending. The armed memory-probe guard failed, then received a timezone fix with
43 host checks; actual Unity/Mono memory counters remain unverified. See the
[exact receipt](conformance/results/linux-744825-pressure-sweep-borrows.json) and
[base-bound proposal](conformance/proposals/pressure-sweep-borrows.patch).

## Previous 8bd development comparison

The corrected 8bd415ce native passes 41 release tests and both expanded
263-case / 9571-publication official comparison suites, with one and four
workers. An independent build from canonical source reproduces its exact hash.
The new 32-case thermal word corpus exposed an existing MINSS/MINSD operand
order mismatch; the failing old binaries and passing fix are retained.

Eight alternating pairs per scene against the official library, CPU16 and one
worker, retain 96 exact final snapshots. CPU median changes are -3.452% fluid,
-20.683% thermal, -9.286% sealed, -1.356% germ-flow, -1.788% mixed-germs and
-35.463% vacuum. All fixed native gates pass: no scene CPU regression above 3%,
one scene improves at least 10%, and no peak RSS increase above 3%. This is not
a whole-game FPS measurement. Host load/frequency varied; all observations
and their dispersion remain in the raw report.
See [the exact comparison receipt](conformance/results/linux-744825-thermal-minss-official.json).

The checked pressure-slice rewrite reduces static bounds-failure call sites
from 20 to 5. A separate intermediate-binary hardware diagnostic observes
fluid instructions -2.057% and cycles -2.101% versus the preceding Rust
variant. These counters support reduced work, rather than guaranteeing the
larger CPU-time reduction from one noisy run. The later thermal correction
has its own binary identity and fresh official CPU measurements above.

Actual-game headless, graphical, whole-game CPU/RAM and concurrent complete-game
acceptance of the new canonical native remain pending. The main distribution
ZIP and earlier runtime receipts are separate artifacts.

## Previous 8bd hotspot samples

Thirty separate profiles sample preceding 8bd415ce on CPU0/2/4/6 with four
workers, five repeats of each 96x96 scene. The 1 ms own-process userspace CPU
clock sampler records 13,135 samples, including 12,402 in the native library,
with no loss, corruption or thread errors. Every final state matches the pinned
official library. Instrumented timing is not used for acceptance.

Pressure transfer accounts for 36.60% of native samples in fluid, 30.77% sealed,
27.90% germ-flow, 32.86% mixed-germs and 39.21% vacuum. Thermal instead samples
PreparedStep::run (23.35%) and temperature update (19.75%) most heavily. These
are symbol shares in authored fixtures, not whole-game bottleneck shares or
precise stall attribution. Sampling and observer bias remain.

The transfer disassembly still constructs and checks multiple common-length
slices on every call. The now-adopted isolated proposal borrows these fields for a
whole serial pressure sweep, keeping checked accesses and exact transfer order;
it does not copy worlds or dispatch tiles. See the
[canonical profile receipt](conformance/results/linux-744825-canonical-frame-profile.json).

## Compact serial element lookup experiment

The isolated `2390ab48…` candidate stores phase and raw flow in an immutable
8-byte entry per actual element, retaining the full original record. Both
vectors live in one wrapper that exposes no mutable record access; index
checks and eager invalid-destination rejection remain. Dynamic exports match.
All 45 release tests and both 263-case / 9571-publication official comparisons
pass, including the new full-u16-domain and raw-word tests. An independent
build from commit 47e756e plus the public patch reproduces the exact measured
native SHA-256.

The fixed CPU/wall/RSS gates fail at both sizes. Eight alternating pairs per
scene at 96x96 show thermal CPU +7.45%, wall +10.30%; five pairs at 192x192
show vacuum CPU +13.14%, wall +13.49%. Other scenes improve, but these results
do not support adoption. All 156 timed final states match official and have no
timed major faults. Other host workloads remain running; every range and sample
is retained. The six-element authored table is not an actual-game material-table
measurement, and these timings establish no whole-game gain. The production
source remains 8bd415ce. A separate 24-sample CPU-core PMU run counts all
warmed threads: fluid instructions -0.96%, cycles +1.16%; mixed-germ instructions
-1.05%, cycles -1.44%. Physics-thread instruction changes are -1.00% / -1.16%.
This is a small reduction in total work, not a large serial bottleneck removal.
Counter running/enabled remains at least 98%, all final states match official,
and the diagnostic does not override failed timing gates. See the
[proposal](conformance/proposals/element-fluid-properties.patch) and
[receipt](conformance/results/linux-744825-element-fluid-properties.json).

## Cleanup vectorization and main-thread attribution

Two private SSE2 cleanup variants pass 45 release tests and exact official
comparisons at one/four workers. They retain parallel row ownership, integer-only
cleanup and scalar tails. The branch variant avoids old-payload reads for four
cleared cells and unchanged-field stores for four retained cells. It still fails
the original small-grid mixed-germ gate, so neither proposal replaces 8bd415ce.
See `conformance/results/linux-744825-disease-cleanup-sse2{,-fastpaths}.json`.

Six alternating pairs per scene then observe the baseline and branch variant on
CPU0/2/4/6 with the matching `cpu_core` PMU, recording every warmed process thread.
All 24 final snapshots match official; running/enabled is 100% for recorded
counters. The high-cycle mixed-germ candidate outlier remains included.

| Scene | Pool instructions | Whole-process instructions | Whole-process cycles |
| --- | ---: | ---: | ---: |
| Sealed | -39.85% | -1.11% | -1.93% |
| Mixed germs | -1.44% | -0.15% | -1.12% |

About 90-97% of retired instructions remain in `onisim-physics` in these two
fixtures. This identifies a larger target than further cleanup tuning. Instruction
shares are not wall-time shares, and these counters do not turn the failed CPU/
elapsed gates into passes. Frequency, cache and scheduling effects remain in the
live-host timing samples. Actual-game performance is still unmeasured for this
candidate. See `conformance/results/linux-744825-disease-cleanup-counters.json`.

## Actual-game measurement and memory capacity

The experimental PreparedStep candidate `7811921b…` passed a Steam-launched
headless save/load roundtrip on the approved small three-duplicant test colony.
Actual loaded managed and mapped native library hashes were checked. Reload
restored the exact saved native clock; progression and final pause were verified
before and after reload. This does not extend the canonical candidate's graphical
acceptance to the experimental candidate.

Five guarded speed-1 samples after one warmup measured median 0.660 process CPU
seconds per native second (range 0.582–0.686), including control overhead.
These are wall-time-bounded smoke steps, not maximum-throughput or FPS tests.
Each sample incurred 1,705–5,806 major faults. There is no matched baseline,
so these results establish neither a speedup nor CPU as the sole bottleneck.
See [the runtime receipt](conformance/results/linux-744825-prepared-first-step-runtime.json).

The complete owned game/runtime/controller tree peaked at 4,325,855 KiB of PSS
plus SwapPSS across 123 valid samples. Later samples had low resident memory
and substantial swap; that is eviction, not a reduction in the total footprint.
The private capacity forecast was therefore raised to 5 GiB per complete game
plus a 1 GiB host reserve. The resulting four-game forecast is 21 GiB, above this
host's approximately 15.35 GiB physical RAM. This is a conservative forecast for
the measured workload, not a bound for every world or a measured four-game sum.
After normal exit, all five owned processes and the idle controller were gone.
Graphical admission failed with about 2 GiB available; no graphical fallback
or concurrent-game success is claimed. The original save remained unchanged.

`conformance/measure_instance.py` records the same-run paused native clock,
process CPU, resident/PSS/swap memory and page faults through existing runtime
APIs. It preserves failed attempts and performs no automatic baseline selection.

## Rust 2024 and compact Load plans (2026-10-21)

The current build uses Rust 1.99.1, Edition 2024 and resolver 3. Its Load plan
stores rectangle origin/width and version-dependent section presence once per
plan. Per-cell storage is 32 bytes instead of 56 bytes (both layouts verified
with rustc 1.99.1). A per-cell radiation-write flag retains legacy exceptions:
known legacy elements preserve earlier radiation, while unknown/vacuum cells
clear it. All plans remain validated before any batch mutation.

The [Load memory receipt](conformance/results/linux-744825-load-memory.json)
compares six alternating fresh-process pairs: two authored native Load records
for a 736x304 public grid (225828 padded cells per record). The sampled extra RSS
peak median is 24736 KiB before and 14180 KiB after, approximately 10.3 MiB less.
All final native save hashes match; no sample used process swap or incurred a
major fault in the measured Load window. Sampling is every 1ms, and the probe
uses glibc malloc_trim before the window. This does not change the game allocator
or establish the total memory budget of an actual colony. The 42.9% reduction
is in per-cell temporary plan storage, not total game memory.

## Historical SSE numeric helper measurement

The following timing receipt applies to the earlier SSE-only candidate
`937d4131c92119be8f7f86d45607f2df3049b2e1c4e7fb9e4e5690c790b0355a`.
It does not approve the current Rust 1.99.1 build's frame timing.


`native/src/numeric.rs` now uses inlined x86_64 MINSS, MAXSS and CVTTSS2SI
register intrinsics. This removes repeated scalar emulation branches in gas
pressure and other physics loops. Unlike Rust's generic min/max and saturating
casts, these instructions retain the required second-operand NaN/signed-zero
selection and integer-indefinite overflow result. SSE is part of the x86_64
baseline; these unsafe calls use registers and no memory pointers. Other
architectures retain the scalar code; the simulation's supported platform
remains x86_64. The helper does not change MXCSR configuration.

The [measurement receipt](conformance/results/linux-744825-frame-performance.json)
retains all samples from exploratory and final production builds. An eight-pair
run pinned both variants to CPU 4, a performance core on this host:

| Complete frame fixture | Baseline CPU median | Candidate CPU median | Speed ratio |
| --- | ---: | ---: | ---: |
| Mixed fluid | 0.366047 s | 0.339575 s | 1.078 |
| Solid heat | 0.121822 s | 0.118742 s | 1.026 |
| Uniform gas | 0.328529 s | 0.305985 s | 1.074 |

These are process CPU times over six requested-ten-second batches after two
warmup batches. All final publications matched the pinned official run exactly.
The native release test suite passes 25 tests, including 19 boundary words in
both argument positions and one million generated word pairs, repeated with
the simulation's FTZ environment. This checks NaN payloads, signed zero,
subnormals, infinities and i32 conversion boundaries.

Unpinned production timing ranged more widely and gave ratios 1.178 / 0.952 /
1.040 respectively; the earlier private build gave 1.097 / 1.001 / 1.019. Do not
select the largest number as a general speedup. Pinning reduces scheduler
migration across the host's performance and efficiency cores, but does not
provide frequency control or host isolation. Actual colony performance is
still unmeasured.

## Where the time goes

Private diagnostic builds time stages in complete 96x96 native frame fixtures.
Gas pressure takes roughly 52–56% in mixed-fluid and uniform-gas scenes;
postprocessing takes roughly 23–30%. In the solid heat scene, temperature work
dominates. Preflight and copying are much smaller targets. Instrumentation
changes code generation; these proportions guide investigation only.

## Rejected candidates

[Raw compact measurements](conformance/results/linux-744825-frame-experiments.json)
retain eight alternating baseline/candidate samples per scene, exact binary and
harness identities, CPU and wall times, major faults and peak RSS. A separate
pinned official run supplies the exact final-publication comparison. All final
publications in these two runs match. Initialization, observation conversion
and serialization are outside timing; worker execution and host message
orchestration are inside. There is no CPU affinity or frequency control.

| Candidate | Mixed fluid speed ratio | Solid heat | Uniform gas | Decision |
| --- | ---: | ---: | ---: | --- |
| Equal positive finite temperatures + inline disease/numeric helpers | 1.153 | 1.027 | 0.973 | Retain experiment; uniform gas regresses |
| Inline pressure function + disease/numeric helpers | 1.031 | 0.990 | 1.013 | No substantial consistent improvement |

Ratios are baseline median process CPU divided by candidate median process CPU;
values above one are faster. Both runs used one worker, two warmup batches and
six timed batches per process, requesting ten native seconds per batch. Requested
time is not an observed game clock. Timed samples had zero major page faults.
Peak process RSS was below 40 MiB. No game was launched.

Earlier exploratory candidates are retained privately: a zero-flow shortcut
regressed mixed-fluid performance, and codegen-units=1/thin LTO lost roughly
1–4% across the three scenes in six alternating samples. Neither is adopted.
The standalone equal-temperature shortcut passed all 179 one-worker differential
cases, but passing correctness does not justify accepting a performance loss.

Further work should reduce repeated work in gas pressure and postprocessing while
preserving traversal order, floating point endpoints, disease side effects and
ordered events. Parallelizing conflicting cell writes would change simulation
results. Benchmark candidates alone, then run correctness separately; never run
a build or correctness matrix concurrently with a timed sample.

The reusable [frame benchmark](conformance/README.md#complete-native-frame-timing)
replaces isolated disease-emitter timing as the immediate selection gate.
Real colony performance remains a separate Steam acceptance gate.

## CPU work versus memory pressure

The three frame benchmarks identify CPU instruction hot paths; they do not
measure cache misses or RAM bandwidth, and do not establish the limiting resource
in a real colony. Aggregate machine CPU utilization cannot reveal a saturated
simulation thread. Earlier actual-game trials suffered paging and control
timeouts; game acceptance remains separate.

A [small memory probe](conformance/results/linux-744825-memory-probe.json)
initialized an authored 736x304 Oxygen world without taking Python snapshots.
Rust's entire isolated Python process was 82,332 KiB RSS after initialization;
the official process was 73,168 KiB. Both reported zero process swap. These are
one sample per library and include host/parser allocations; they are not native
heap attribution or a complete colony memory budget. No memory reduction is
claimed. This fixture does not include Unity assets, managed objects,
buildings, duplicants or mod-rich save contents.

At the host observation, the game was absent, available memory was approximately
4.9 GiB and occupied swap approximately 15 GiB. Four one-second vmstat intervals
had zero swap-out, only 152–468 KiB/s swap-in and 86–88% aggregate CPU idle. This
does not indicate sustained swap thrashing in that window; it does not predict
resource use after loading a colony. The first vmstat line is a since-boot
average and is excluded from this interval interpretation.

Priorities: remove repeated physics work and parallelize independent writes;
measure allocation rates, live heap and copies inside a representative game
session before changing storage ownership. Reduce simultaneous heavyweight
work during that session. Preserve snapshot/publication lifetimes and exact
simulation results when removing copies or reusing buffers.

Two subsequent eight-pair, CPU-4 experiments against 937d4131 are retained in
the rejected-candidate receipt. Skipping impossible same-element partial-melt
pairs gave ratios 0.998 / 1.063 / 1.013; inlining RNG, phase and density helpers
gave 1.044 / 0.992 / 0.988. Large within-scene variation and mixed results do
not justify adoption. These two candidates remain rejected; later source progress is recorded above.

## Official-library baseline and worker sampling (2026-10-21)

The current `1f56c9b2` Rust library is not faster than the official library in
this eight-pair, CPU-4, one-worker measurement. Each sample times six batches
requesting ten simulation seconds after two warmup batches. All final public
outputs match; every measured sample has zero major faults. Host frequency and
load are uncontrolled; this establishes neither whole-game FPS nor a universal
speed difference. Full ranges and file identities are in
[the official baseline receipt](conformance/results/linux-744825-official-frame-performance.json).

| Scene | Official median CPU ms | Rust median CPU ms | Rust CPU-time increase |
| --- | ---: | ---: | ---: |
| Mixed fluid | 345.25 | 387.57 | 12.26% |
| Solid thermal | 111.68 | 130.84 | 17.15% |
| Uniform gas | 332.95 | 348.91 | 4.80% |
| Germ flow | 447.73 | 502.66 | 12.27% |

A separate CPU-clock IP profile collected 10,351 in-window samples from the
Rust physics worker during 180 fluid batches, with zero lost samples. It does
not time Unity or the main/control thread. The largest self-sample shares were
pressure transfer (24.75%), the disease mutation wrapper (13.50%), postprocessing
(10.94%), gas-phase traversal (10.63%), and the main step (7.50%). IP sampling
has attribution/skid limits and profiler overhead; these are sample shares,
not exact inclusive call costs. See
[the worker profile](conformance/results/linux-744825-worker-profile.json).

Two fixed-size thermal caches, a whole-phase canonical-germ-free classifier,
and forcing one disease helper inline were measured and rejected against their
predeclared acceptance thresholds. No cache or classifier is in production.
The latest local same-species merge candidate passes 29 native tests, including
225,792 metadata/wrapping combinations. Its first five-scene timing had severe drift: host load rose to 25 on 24
logical CPUs and ranges expanded more than threefold. Both full 224-case
conformance runs pass. A separate eight-pair CPU16 holdout reduced germ-flow
CPU time by 6.31% and mixed-germ time by 7.91%, but fluid time only by 1.57%,
below the predeclared 5% general adoption threshold. This proposal remains
private and unadopted. The fifth scene retains two disease
species and at least half the grid infected to exercise mixed-species behavior.

## Early notification deduplication candidate (2026-10-21)

A retained [candidate patch](conformance/proposals/notification-dedup.patch)
suppresses repeated substance notifications before final sorting. A bounded
bitset marks only cells with a retained notification; invalid indices remain
queued for validation, allocation failure keeps the original path, and the
other 28 event queues are unchanged. Five Lean model theorems establish retained
membership, not compiled Rust or full-engine correctness.

The candidate `9ac4bae9` passes 30 release unit tests and the complete official
differential suite at one and four workers: 224 cases and 8440 publications per
run. Eight alternating CPU16 pairs in six 96x96 scenes preserve all final
publications. In vacuum, median CPU time decreases from 574.80 to 417.73 ms
(27.33%); median process peak RSS decreases from 43594 to 32258 KiB (26.00%,
about 11.1 MiB). Other scene CPU median changes stay within the predeclared 3%
regression limit. Every timed sample records zero major faults.

The first measurement exposed an instrumentation error: `getrusage` retains a
pre-exec parent peak and masked the child memory difference. The original run
is retained; the second run measures `/proc/self/status` VmHWM through the
timed batches, before final snapshot conversion, with separate instantaneous
RSS/PSS/swap. A local reproduction and parser regression test cover the fix.
Historical receipts using only `getrusage` are not retrospectively corrected.

See the [experiment receipt](conformance/results/linux-744825-notification-dedup-experiment.json)
for medians, ranges, identities, criteria and proof scope. This is an unadopted
candidate pending headless then graphical game acceptance. Host frequency/load
are uncontrolled; neither these native fixtures nor their Python process memory
establish whole-game FPS, colony memory savings or four-instance compatibility.

Subsequent source review found a capacity-bound defect in this first candidate:
`try_reserve` can grow the retained bitset beyond its advertised 262144-u64
(2 MiB) budget when map dimensions increase. The newly added growing-dimensions
test fails the original code. An isolated V2 uses `try_reserve_exact`; all 31
release unit tests pass, including that regression. The original timing receipts
remain valid for their fixed dimensions, but do not validate the growth bound.
V2 (`4e677de1`) also passes both fresh official one/four-worker matrices, each
with 224 cases and 8440 publications. Its complete eight-pair CPU16 run passes
the same predeclared native gates: vacuum CPU time decreases 23.34%, from
715.52 to 548.49 ms; peak RSS decreases 26.24%, from 43824 to 32324 KiB
(about 11.2 MiB). Other scene CPU median changes range from -4.48% to +0.20%;
all 96 timed samples have zero major faults and exact final publications.
This is a new measurement of the corrected candidate, not a reuse of V1's
27.33% result. Game acceptance remains required; the unstarted first-candidate
group is marked superseded. The
[V2 receipt](conformance/results/linux-744825-notification-dedup-v2.json) and
[reviewable patch](conformance/proposals/notification-dedup-v2.patch) retain the
regression and candidate identity.

Two isolated Steam headless processes subsequently mapped the actual corrected
library. The first hit its startup deadline; the second reached verified engine
readiness but could not load its `0400` working save. Native `WorldGen.CanLoad`
requests read/write access and rejected that file, then fell into world generation.
The integration now creates private `0600` test copies while leaving the original
gameplay save unchanged. The next attempt was refused by the single-instance
memory preflight. Both owned games and their launcher exited normally. These
[runtime attempts](conformance/results/linux-744825-notification-dedup-v2-runtime-attempts.json)
do not validate simulation progress, save/load, graphics or whole-game speed.

The subsequent optional MCP acceptance profile resolves the inspected native
pause through the existing game-owned stop lease. Actual library `4e677de1`
passes a headless three-step checkpoint roundtrip: native progress is about
0.95, 1.20 and 0.95 seconds; reloading restores exactly 51.56664276123047 after
first advancing beyond that saved clock. The process remains paused, with a Null
device and zero camera callbacks. This targeted result does not establish global
performance or broader colony compatibility. Graphical acceptance from that
checkpoint is prepared but its launch was refused by the memory preflight. See
the [updated runtime receipt](conformance/results/linux-744825-notification-dedup-v2-runtime.json).

## Owned pressure tiles: correctness before adoption

Two [parallel pressure executor proposals](lean-lab/pressure-proof/README.md)
now run fixed skew tiles using owned scratch worlds. The dependency model proves
that distinct same-level tiles have disjoint 2x2 task footprints; it is not a
proof of compiled Rust or the complete event merge. Scratch vector capacities
and inline objects total 151272 bytes per worker (about 0.58 MiB for four),
independent of colony dimensions. This excludes allocator and thread overhead.

The first proposal passes 31 release tests and both full official conformance
suites, but its four-worker measurement was interrupted when another task
launched ONI. The timing driver now checks for live games before and after every
child and withheld the successful summary. Its partial samples show substantial
CPU overhead; no speedup is claimed. The original observations remain in the
[first receipt](conformance/results/linux-744825-pressure-tiles.json).

V2 enters the pool once per phase and skips immutable ineligible tasks before
gathering their footprints. It passes 32 release tests, including 576 comparisons
across 16 floating-point control combinations, and one/four-worker official
conformance: 224 cases and 8440 publications each. Its [measurement criteria and
current status](conformance/results/linux-744825-pressure-tiles-v2.json) remain
explicitly pending. Neither proposal has replaced production `1f56c9b2` or passed
headless/graphical game acceptance.

### Asynchronous executor and copy-cost premise

V3 replaces global level joins with a bounded grid-DAG coordinator. Completed
owned jobs unlock successors immediately. It passes 36 release tests and both
224-case/8440-publication official suites, including failure ownership recovery,
nested-pool fallback and all 16 floating-point control combinations.

The complete eight-pair, four-worker CPU16–19 run rejects this implementation:
fluid median wall time increases 48.67%, germ-flow 30.17%; CPU time increases
87.07% and 55.45%, respectively. All six scenes regress in both metrics, with
zero major faults. This candidate remains unadopted. See the
[full V3 receipt](conformance/results/linux-744825-pressure-tiles-v3.json).

A direct premise check uses an authored uniform 256x128 pressure-only fixture.
Serial pressure, copying/dispatch alone with arithmetic omitted, and the full
parallel path must all preserve the same mass, temperature, germ and flow words.
Two independent six-sample alternating runs agree: copy-only child CPU is
1.122x and 1.128x serial; copy-only phase wall time is 87.15% and 86.67% of
serial. Child CPU includes setup, warmup and validation; the phase wall timer
excludes them. Copy-only includes both data transport and dispatch, so their
individual contributions are not separated.

The result rejects another scheduler-only repair of the complete CellSOA-copy
representation on this fixture. The next design must remove cell-data copies,
then rerun correctness and timing; these numbers are not game FPS. The
[premise receipt](conformance/results/linux-744825-pressure-copy-cost.json) binds
both runs. `conformance/pressure_copy_cost.py` is the retained driver; build the
ignored unit probe by applying `pressure-copy-cost-probe.patch` after the V3
proposal in an isolated source tree. The production library remains `1f56c9b2`.

### Borrowed rows: cell copies removed, timing still fails to show a benefit

The next isolated candidate (`5c857230`) borrows disjoint row fragments directly
from the original SOA arrays using safe `split_at_mut`. There are no physics-cell
scratch copies or gather/commit phases. Fixed-size plans and slice headers stay
bounded; notification payload capacity is at most 48 KiB across eight workers,
excluding containers, stack and thread overhead.

It passes 33 release tests, including 576 serial/parallel comparisons across 16
floating-point control combinations, and both official one/four-worker suites:
224 cases and 8440 publications each. The model proofs do not establish compiled
kernel correctness; the source-bound tests remain necessary.

The eight-pair timing run stopped when another task launched ONI. Four completed
pairs in fluid and germ-flow show CPU-time increases of 81.14% and 54.57%, with
wall-time increases of 16.00% and 7.33%. Vacuum has only three completed pairs.
These are partial observations, not complete acceptance or whole-game results.
All retained paired final publications match the official reference, with zero
major faults. No successful timing summary was produced.

This candidate remains unadopted. Removing copies alone has not produced a useful
parallel executor; further work needs to separate scheduling, view construction
and kernel-access costs before changing another executor. The
[borrowed-row receipt](conformance/results/linux-744825-pressure-borrowed.json)
binds the source, tests, fixed criteria and retained samples. Notification
deduplication remains promising, with its capacity correction requiring fresh
native validation before headless then graphical game acceptance.

## Release optimization candidate

An isolated build enables Cargo release `lto = "thin"` and `codegen-units = 1`;
the portable target and simulation arithmetic source stay unchanged. Candidate
`f198e42c` passes all 28 release unit tests and the official one/four-worker
matrices: 224 cases and 8440 publications each. Its dynamic exports match the
production library. The file shrinks from 1368576 to 1170520 bytes (14.47%).
File size is not measured execution speed or resident memory. Its first timing
attempt stopped after 22 accepted observations when another task launched ONI.
The subsequent complete eight-pair CPU16–19/four-worker run rejects this profile:
fluid CPU increases 1.80%, germ-flow 2.00%, sealed 5.44% and vacuum 3.54%.
Thermal improves 10.37% and mixed-germ 6.08%, but the predeclared requirements
include fluid/germ-flow improvements and prohibit regressions above 3%.
All 96 samples retain exact final publications and zero major faults. These
mixed native-fixture results do not establish whole-game performance. Production
build settings are unchanged. See the
[reviewable profile patch](conformance/proposals/release-thin-lto.patch) and
[source-bound receipt](conformance/results/linux-744825-release-thin-lto.json).

### V2 源码落地与可复现发行构建（2026-10-21）

通知去重 V2 已从提案合入 `native/src/events.rs` 和 `frames_engine.rs`。
原实测候选 `4e677de1…` 已按无头后图形顺序通过指定三复制人存档的
正常存读档、重载后推进和五组完整 CPU/GPU 纹理对照，并检查实际画面。
记录见 `conformance/results/linux-744825-notification-dedup-v2-runtime.json`。

发行构建显式清空 `RUSTC_WRAPPER` 和 `RUSTC_WORKSPACE_WRAPPER`，
避免本机编译器包装程序影响发行文件身份。两个不同源码根目录与全新目标目录
均产出 `a1ef3400389fa2e8c0fcd0db703dc683df362d9b132eff2771530887b45e8f07`。
这是新的二进制身份，旧候选的运行验收和性能数字不能直接转移到它。
新文件的实机验收、整局对比、四实例同时运行及新版交付仍待完成。

### 混合场景热传导舍入修正（2026-10-21）

三个新增的气液固/真空混合场景均暴露了一个 f32 乘法顺序错误。
原版先计算绝热系数再乘导热率；端口此前交换了舍入次序。
恢复 ELF 0x54a02..0x54a0c 的顺序后，三个失败场景逐位一致。
新核心 `c301cc3e…` 单/四线程均通过 227 个用例、9103 次发布对照，
31 项 Rust 测试通过，两个源码根目录产物一致。
回执为 `conformance/results/linux-744825-mixed-thermal-order.json`。
这是兼容性修正，没有新增性能提升结论；旧通知去重基准仍仅适用于旧文件。

### 当前 c301cc3e 的复测边界（2026-10-21）

八轮交替、CPU16、单工作线程、六个 96×96 原生场景共 96 个样本，
最终发布均与原版一致，样本主缺页数为零。
真空场景 CPU 中位耗时 0.651505 → 0.470591 秒（减少 27.77%），
进程峰值 RSS 中位数 44352 → 33148 KiB（减少 25.26%）。
病菌流动场景中位耗时增加 5.39%，超过既定的 3% 回退上限，
因此本轮未通过完整性能门槛，不能作为已验收的全面提速结论。
两组病菌场景样本范围高度重叠（基线 0.5445–0.7742 秒，
候选 0.5444–0.7900 秒）；探索性双侧精确秩和检验 p≈0.959，
代码回退尚未得到证实。此统计检查不替代或放宽原验收门槛。
主机负载/频率未在采样期间记录，仍需单独剖析并核对限制因素。
具体回执见 `conformance/results/linux-744825-mixed-thermal-performance.json`。
这些数字不代表整局 FPS 或整局内存收益。
