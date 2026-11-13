# CycleTrim critical-path investigation — 2026-11-14

## Current result

No new whole-game speedup is established. The earlier gas-density optimization
is already pushed as `4dbb6f46`; it was not substituted for a whole-frame result.
This round investigated the ordinary graphical game and added lower-overhead
engine/pause observations for the next investigation. No new production physics
optimization or Workshop upload was made.

All game tests used the existing disposable 15-duplicant save copy. The mapped
native binary was Workshop 0.4.22's `6fe05e25424bf824de2531111acfc8910d633a9ade1c590351e4e2f4a06250dd`.
Private saves, full logs, raw addresses and profiles remain local.

## Evidence and limits

- With full diagnostics and a competing lmm-api Rust build, a 12.058-second paused
  sample recorded 103,885 main-thread major faults. Global swap counters increased
  by 243,279 pages in / 566,970 pages out. Thread wait samples included page waits.
  These counters establish paging pressure, not a useful FPS comparison. Swap is
  mixed zram/disk: it must not all be described as physical disk I/O.
- After that Rust process ended, ordinary mode without the 299-method diagnostics
  recorded 4.8236 seconds of main-thread blocking at futex waits in one 12-second
  kernel trace. That particular trace did not show page-I/O blocking. It does not
  prove GPU execution took 4.8236 seconds or that paging is permanently solved.
- A separate wait-address trace recorded 1.5966 seconds inside waits whose address
  was woken by UnityGfxDeviceW, and 2.5691 seconds at another address without a
  captured user-space waker. These are futex syscall durations, not pure off-CPU
  time. The second address's cause remains unresolved. Do not sum these separate
  windows or infer a GPU bottleneck from them.
- A separate ordinary paused process sample measured main CPU 7.65 seconds over
  13.875 wall seconds, including 0.64 seconds of kernel CPU, and 4,094 major faults.
  Paging, view changes and instrument overhead prevent claiming an optimized FPS.
- glibc malloc_info reported 955,224,064 bytes of arena space and 25,144,721 free
  bytes. This limits the immediate potential of trimming glibc free pages; it does
  not identify Unity custom pools, managed live objects or GPU allocation owners.
- A smoke command reported success after only 0.375 game seconds of progress in
  its requested ten seconds. A later attempt remained natively paused. These are
  not accepted running performance samples. The new running-window observer now
  rejects observed early pauses instead of accepting any positive clock delta.

## Retained implementation and verification

OniHarness adds opt-in native Unity counters (`--engine-timings`), explicit
availability/null values, bounded recorder history and disposal, plus read-only
native pause count / companion deadline ownership. No new Harmony hook is used
for these counters. The original watchdog duration and acknowledgment guard were
not loosened.

- Installed-reference Runtime Release build: passed, zero warnings/errors.
- Runtime Python checks: 22 passed, including early-pause rejection, ignoring a
  stale pre-resume pause, rejecting absent fresh running data and opt-in flags.
- Installed Runtime DLL SHA256: `8cc56de4f94f322a86b2e42d46ad8b0cc5973118032f8fc4f631e301e0cf91a7` (file deployment verified; not loaded-game acceptance).
- New engine-counter and pause-field game acceptance: **not run**. The operator
  prohibited further launches; the in-progress owned launch was stopped.
- No game process remains. Only the known pre-test mod-loader flag was restored
  after that owned interrupted startup; other mod settings were preserved.

## Next session

Respect the no-launch instruction until the user permits another launch. Once
permitted, load the test copy paused, verify the loaded Runtime DLL hash, inspect
counter availability and pause ownership, then capture a genuinely running short
window. Rank actual engine main/render/presentation/simulation times before
choosing the next production optimization. Inspect the unresolved futex owner;
do not continue gas micro-optimizations merely because they are easy to measure.

Local receipts: `/tmp/cycletrim-criticalpath-20261114/`,
`/tmp/cycletrim-criticalpath-clean-20261114/`,
`/tmp/cycletrim-final-runtime-tests-20261114.log`, and
`/tmp/cycletrim-engine-timing-build-20261114.log`.
