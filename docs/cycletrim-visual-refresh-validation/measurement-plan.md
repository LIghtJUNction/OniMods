# Conduit display refresh: validation plan

Baseline: `62067913a5aea4daa4a8991689c86aad261b1269` (PR #558).
This is an independent display addition. Concurrent animation/conveyor recovery
work is not duplicated or claimed as a result of this change.

## Mechanism and acceptance criteria

Only rebuild the gas/liquid conduit display every two or four rendered frames,
with a maximum age of 100 ms checked at the next invocation. Continue drawing
the completed meshes on intervening frames. Preserve the outer render method,
animation clock, audio, and all simulation calls. Full remains the default.

New simulation calls, network rebuilds, actual visible bounds, world, highlight,
thermal tint and style changes force rebuilding. Content overlays and audio
requests always use the original. Only the normal branch that completed both
mesh uploads/draws may authorize reuse. Empty returns, failures, release and
same-instance reentrancy must not reuse old or partially rebuilt geometry.

Before timing, require real-Harmony host regressions for those boundaries,
zero additional steady-state managed allocations, settings wiring, a strict
net48 build against the pinned 737790 references, and full-range diff checks.
These gates do not replace Steam-launched acceptance.

## Measurement protocol

- Use .NET SDK 10.0.401 / runtime 10.0.12, Release, Linux x64. Pin CPU 2 to avoid
  another workspace's CPU 0 recovery benchmark; the host is shared.
- Three warmups followed by seven alternating original/patched pairs for every
  workload. Keep all samples, report median/range and paired ratios.
- Measure actual patched fixture calls, including policy, input capture, mesh
  rebuild work and drawing, rather than only the decision helper.
- Stable 60 FPS workloads must reduce rebuilding by exactly 50% / 75% for
  Half / Quarter, retain every draw and every simulation invocation, and improve
  the medium/large synthetic workload's median total elapsed time.
- Retain tiny-work, changing-input, overlay, audio, frequent-simulation and
  low-FPS controls. Report any timing regression rather than tune thresholds
  to a hosted runner. Timings have no game-FPS interpretation.
- Exercise empty-view and failure/reentrancy controls as correctness tests.
  A redraw exception is propagated; it never causes a replay and duplicate draw.

## Runtime acceptance

Record candidate commit/DLL, actual game build, save, settings and other mods.
Check gas and liquid contents, both overlays, audio, zoom/pan, two asteroids,
construction/removal, pause/speed changes, save/load and cleanup. Compare frame
times on the same save with Full/Half/Quarter and record distributions.
FastTrack or conflicting rendering patches must retain their own original path.
