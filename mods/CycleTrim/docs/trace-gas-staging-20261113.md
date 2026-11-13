# Trace-gas correctness and reusable staging — 2026-11-13

## Correctness

Gas projection formerly snapped changes smaller than `max(1e-7 kg, previous * 1e-5)`
back to the previous mass. For trace gas this could resurrect a computed zero or
suppress newly transported gas. Both serial and parallel projection now use only
the relative tolerance. Exact zero display slots become vacuum, or promote a
surviving secondary species. Positive trace mass is preserved; no arbitrary mass
purge was introduced. A rendered `0.0 µg` alone still does not prove exact zero.

The new trace-gas regression fails with the original threshold (exit 101) and
passes with the fix. Tests cover depletion, emergence and continued trace-gas
transport in both projection paths, plus zero-slot promotion.

## CPU gas work

The pending gas-transport optimization is included: reuse each cell's neighbor
velocities across species, and skip solid-cell work. The test-only frozen
transport oracle checks exact outputs, including nine-channel workloads. Earlier
same-process paired microbenchmarks measured about 5–14% lower local chain time
across dense/sparse and diffusion cases. This is not a whole-game FPS result.

## Allocation work

One per-device liquid workspace reuses packed input, masks, solid flags and CPU
projection storage. Finished work returns its workspace even when readback fails.
Gas/ liquid diffusion masks circulate between staging and the caller instead of
being dropped after every region. Capacity survives steps, not physics values;
obstacle flags and inputs are refreshed. The existing asynchronous liquid-submit /
gas-compute / liquid-finish order is unchanged.

The new reuse regression verifies stable warmed input/mask/projection storage and
alternating obstacle states. This removes repeated large-array allocations; it is
not evidence that total ONI memory fell by the same amount.

## Verification

- Native library: 218 passed, 16 ignored; includes CPU/GPU parity and liquid overlap regressions.
- Jlib: 2290 host assertions passed; shared native-loader API compiled and loaded in ONI.
- Loader host checks passed; no claim that the optional native-library smoke was run.
- Nested TOON camera-view regression: 19 Harness runtime tests passed.
- CycleTrim and OniMcp built/deployed through `onim dev`.
- Steam graphical test copy loaded with 15 duplicants; actual mapped native SHA256:
  `f239c29cc12635c5cb5a00045faa536c19ca0ae49e5e72a4255bf4138fbc6bf6`.

The first instrumented active series measured 13.16 median camera callbacks/s,
but used zoom 24.41 rather than the older zoom-8 baseline and observed about
820 MiB swap-in / 598 MiB swap-out. It does not establish an optimization gain.
Paused instrumentation also showed low cadence with no collections in those
windows: measured Game.Update/LateUpdate covered only a small part of the frame.
Unattributed time must not be labeled GPU time or GC time without further evidence.

Read-only memory counters showed about 2.8 GiB Mono used heap and 1.25 GiB Unity
allocated memory. These counters overlap and must not be added. A rendering-mode
diagnostic suffered stale telemetry and is not accepted as a quantitative result.

The sampler now records structured camera/world/viewport state before and after
measurement and rejects missing or changed views. Raw game logs and private saves
remain local. Whole-game performance attribution remains unverified.

A subsequent zoom-8 series measured 15.74 median camera callbacks/s but was
rejected because the camera changed and control overhead exceeded two seconds.
It is retained as a failed measurement, not evidence of improved FPS.
