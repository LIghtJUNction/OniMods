# Twofold performance target — declared before the next measurements

The user requires a comprehensive improvement of at least **2.0×**. This is an
acceptance target, not an observed result. The prior conduit candidate is
`bd6d01ac0960210d2a1c47db77a9911462de571c`; related conveyor/animation recovery is
`09c496c46b4879cbdce4a8a9056b3f85f9c5c0d3`. The next candidate must preserve both
histories and their unsuccessful measurements.

## Primary result: actual game work

Whole-game acceptance requires a Steam-launched ONI run on the same hardware,
game build, starting save, mod set, simulation speed, graphics mode and update
cap. Record the candidate commit and DLL hashes. Reload a disposable copy of
the same starting save before each run; verify positive native progress,
required mod/probe markers, unchanged game results and final pause.

Choose the representative save and scenarios before recording times. Use at
least seven adjacent baseline/candidate pairs after identical warmup, alternating
the order of the two versions. Retain every result and failure. Report paired
ratios, medians, ranges and tail latency for each chosen scenario. A comprehensive
2× claim requires at least 2.0× median native work per wall second in **every**
predeclared representative scenario, with no material correctness or frame-time
regression. List any untested scenarios explicitly; a single save cannot prove
universal performance.

The pinned OniHarness `measure_steps.py` measures guarded wall-time steps and
CPU cost per native simulation second. Its client startup/lease/pause latency
is not maximum-throughput evidence. Report these separately; neither a faster
control acknowledgment nor a lower render frequency establishes simulation
speedup. A graphics-disabled run cannot establish a conduit rendering gain.

## Secondary results: production helpers in host fixtures

Keep every existing benchmark workload, original control, admission rule,
correctness assertion and timing criterion. Do not lower thresholds, increase
synthetic work to obtain a favorable ratio, trim slow samples or replace a
failed run with an unchanged rerun. Record implementation changes between
exploratory rounds and freeze source/DLL hashes before formal measurements.

For conduit sampling, retain all 12 legacy workloads and its 3 warmup plus 7
alternating formal pairs per workload, 6,000 frames per sample. Report both
original/candidate and, in a separate controlled comparison, prior/candidate
ratios. Include all draws, simulation/audio calls, payload checksums and
allocations. Half sampling retains half of rebuilds plus all draws and checks;
sampling alone cannot guarantee a 2× total speedup even for an expensive build.
Forced-rebuild cases retain the original work and remain negative controls.

For conveyor changes, retain the long-miss, shared-rail, short-path, early-hit,
single-query and containment-return controls. Compare against the original
fixture and the exact 09c496c helper. In particular preserve observable predicate
callbacks, list mutation, exception order, nesting and all 2,059 differential
cases. Removing allocation must not silently bypass another mod's method body.

Run timing experiments serially on a fixed allowed CPU with a shared lock.
Use the same runtime flags for both sides, alternating adjacent processes for
cross-version comparisons. Keep source, host, compile-reference and Unity/Mono
evidence separate. Synthetic 2× results are local results, not whole-game
acceptance.

## Current runtime preflight

The current execution environment has no `steam` executable, no explicitly
provided installed-game path and no installation in the two standard Steam
locations checked. Google Drive's `OniHarness-AI_HANDOFF.md` was read; its archive
requires an authorized Steam session and contains no account credentials.
No game binary was downloaded or executed. Actual-game acceptance is **not run**.

The new iteration also repairs the prior reference-CI failure: the added `Game`
lifecycle patch needs an explicitly reviewed signatures-only baseline entry.
This is an API-set addition, not permission to change the pinned reference
assemblies or weaken the API drift check.
