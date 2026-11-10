# Current CycleTrim bottleneck evidence, 2026-11-10

The inspected game process loaded native SHA256
`bf783968d7e14ddf42ec4ab0c25bf35bff65338e6c0160abfef18c5169a7089c`
from Dev/CycleTrim. Camera size was 74.51, resolution 1219x1521. This is not
the earlier full-map, paused oxygen-overlay benchmark.

## Measurements

Thirteen sampled non-reused simulation batches (excluding initialization and
zero-time reuse) had median total 142.883 ms, range 91.210–172.376 ms.
Stage medians were simulation 131.723 ms, Grid publication 1.667 ms, texture
publication 2.817 ms, and species publication 8.088 ms. Stage medians do not
sum to the total median. These sampled batch durations are not displayed FPS.

A bounded OniMcp run lasted 6.235 wall seconds including control calls; the
rounded game clock moved from 28.7% to 29.7% of a cycle (approximately six
game seconds). CPU time was 3.00 s on the main thread, 2.49 s on onisim-physics,
and 1.61 s on UnityGfxDeviceW. Thus this sample does not show a large sustained
simulation-speed deficit, and it does not measure frame-time percentiles.
The game was paused after the sample and later exited normally. A subsequent
perf recording did not collect data because the process had exited. No crash
or successful call-stack profile is claimed.

Local evidence is in `/tmp/cycletrim-current-bottleneck/` and the current
Player.log. No new GPU tests or game restart were used for this diagnosis.

## Confirmed execution costs and next changes

1. `native/src/gpu_sim.rs::step_gas_impl` submits pressure projection, waits
   for `revalidate_velocity`, and only then submits transport and final
   readback. The region loop repeats this serially. Even a sampled 32x32,
   one-channel region spent several milliseconds in this pipeline. Investigate
   a GPU-side bounded transport/stability decision and one final readback;
   retain non-finite/output validation. Measure small-region CPU and GPU
   crossover rather than assuming every region benefits from GPU dispatch.
2. `multicomponent_flux::step_region_prepared` calls `staging.prepare` before
   its idle lookup. A hit therefore still pays scanning and dense packing.
   The exact comparison also includes temperature: a temperature-only change
   wakes mass transport. Split flow/thermal dirty state and move dirty-tile
   rejection before packing. Wake changed tiles and necessary neighbours,
   keeping all species subject to the same rules.
3. `lib.rs::prepare_callbacks` calls `worker.wait_ready`, so unfinished worker
   batches can block the game's result-publication call. This connects long
   simulation work to a possible frame stall; the exact stall contribution
   still needs a valid managed frame-time capture. Merely adding a worker
   thread does not remove this barrier.
4. Species publication still costs 4.473–15.008 ms in these samples. Reuse
   unchanged publication blocks and avoid rebuilding the whole species view
   for local changes, while preserving native-lease ownership guarantees.
5. Gas pipeline caching retains at most eight shape/channel combinations and
   96 MiB. Channel-count changes can create cache churn; this is a hypothesis
   requiring hit/miss and allocation counters, not a demonstrated dominant
   cost. Do not simply enlarge buffers on a memory-constrained host.

The first optimization experiment should remove the intermediate GPU round
trip while holding the physical scenario and camera fixed. Then compare
changed-tile packing/publication with the same wake-up scenarios. Report
median/p95/p99 frame times, simulation progress, GPU waits and memory pressure
separately. A 60 FPS result is not established by these measurements.
