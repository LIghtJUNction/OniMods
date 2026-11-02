# Fetch layout iteration protocol

This experiment continues the private OniHarness AI handoff against OniMods
`14c08591cbd0bf66cd0f53b37bfb498bdef4abf6`. The archived direct-destination layout
is not promoted: a callback can observe its partially collected results. The new
candidate uses private dense storage and publishes only after collection.

## Protocol history

The decisions below are the original plan recorded before round 1. That round's
floating-point freshness fixture and direct-oracle dispatch diagnostic were later
superseded. Round 2 used the corrected integer fixture and actual prior/V1/V2
Harmony prefixes in seven rotating/reversed three-way groups, with two warmup
batches per variant. This history note was added after evaluation; the revised
method, sources and results are recorded in [the iteration report](cycletrim-fetch-iteration.md)
and [round-2 receipt](cycletrim-fetch-iteration-validation/round2-metadata.json).

## Original decisions fixed before round 1

- Ordinary launches retain the established dictionary collector. The candidate
  requires an explicit layout-control launch argument and a paused-game request.
- Correctness is a gate: ordered identities, exact value bits, callback work,
  exceptions, nested invocations and reference release must match the baseline.
- Time the linked production prefix, buffer and layout controller with real
  Harmony; game/Unity APIs are synthetic. Consume every resulting candidate.
- Use .NET SDK 10.0.401 / runtime 10.0.12, Linux x64, CPU 0 affinity, workstation
  GC and tiered compilation disabled. Acquire the shared advisory benchmark lock.
- For each of ten workloads, run two warmup batches per layout, then seven
  alternating pairs. Keep every raw sample, allocation result and checksum.
- Summarize the median of each pair's baseline/candidate time ratio, together
  with the full range. A ratio above 1 is faster in that fixture. Use 1.05 and
  0.95 only as descriptive bands; do not select new thresholds from the results.
- Separately measure ordinary disabled dispatch against the legacy oracle. That
  comparison includes Harmony wrapper and code-generation differences; it is
  not an isolated estimate of control-field read cost.
- A host timing result cannot promote the candidate, establish cache behavior,
  or prove game FPS/throughput. Real acceptance requires an authorized Steam
  session, the pinned game/save/mod combination, positive native work, actual
  selected-layout calls and final pause. Both sides must use the same harness.

The archive's prior whole-game result was inconclusive. The current executor has
no Steam executable or registered Steam library, so this iteration's executable
scope is host behavior, reference compilation and synthetic timing.
