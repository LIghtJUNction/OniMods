# NavGrid workload probe

This is a developer-only measurement path for issue #25. It is disabled unless the ONI process starts with `CYCLETRIM_NAVGRID_PROBE=1` (also accepts `true` or `yes`). Normal CycleTrim users pay no per-call probe cost because Harmony skips the patch at load time when the variable is absent.

The probe observes the real parameterless `NavGrid.UpdateGraph()` before expansion and aggregates only data that a future cheap dispatcher could know: unique dirty-cell count as stored by ONI, `updateRangeX`, `updateRangeY`, and seed bounding-box density. It does not replace `UpdateGraph`, alter `DirtyCells`, or select an optimization path.

Reports are cumulative histograms. Formatting/logging is deferred through `UIScheduler.ScheduleNextFrame`; the `UpdateGraph` prefix only scans the existing dirty list and updates fixed-size counters. `UIScheduler` uses ONI's unscaled UI clock, so a report that was requested while the simulation was running can still flush after the game is paused. Reports are requested after call 1, 64, 256, 1024, and then every 4x growth. The normal report shows only the 12 most common dirty-count/range/density buckets so ordinary developer logs stay readable.

For quantitative captures, also start ONI with `CYCLETRIM_NAVGRID_PROBE_CAPTURE=1`. At each sparse report point CycleTrim then emits an additional `[CycleTrim][NavGridProbeCapture]` line containing every non-zero histogram bucket, not just the top 12. This can make the developer log line substantially larger, so the complete mode is opt-in and should only be enabled while gathering issue #25 evidence.

Analyze the resulting ONI log with:

```bash
python scripts/analyze_cycletrim_navgrid_probe.py /path/to/Player.log --pretty > navgrid-capture.json
```

The analyzer uses the latest probe startup in the log, requires the `NavGrid.UpdateGraph() resolved` and `target reached; aggregate sampling started` markers, rejects captures where FastTrack disabled baseline sampling, and verifies that the complete bucket counts sum exactly to the reported call count. A top-12-only or otherwise truncated report therefore fails instead of silently producing misleading workload fractions.

For a controlled workload window, first analyze the log and record the latest `calls` value, run the workload long enough to cross another sparse report point, then pause and wait for the unscaled UI scheduler to emit the deferred complete report before analyzing again:

```bash
python scripts/analyze_cycletrim_navgrid_probe.py /path/to/Player.log --after-calls 256 --pretty
```

`--after-calls` must match an existing complete capture in the current probe run and only accepts a later capture whose cumulative call count is larger. A restarted/reloaded probe, a missing baseline, or a window that never emitted a fresh deferred report fails closed instead of reusing old workload evidence. Pausing the simulation no longer requires a short resume solely to advance the report scheduler.

The JSON also reports `candidateGate` bounds for the current synthetic `NavGridAdaptiveGateBenchmark` rule (`dirty >= 20`, short range `>= 2`, long range `>= 4`). The runtime histogram deliberately keeps coarse buckets such as `dirty=16-23`, so a capture cannot always say exactly how many calls would satisfy that candidate rule. `eligibleCallsLowerBound` counts only buckets that are guaranteed to satisfy it, `eligibleCallsUpperBound` includes threshold-crossing buckets that might satisfy it, and `ambiguousCalls` is the difference. These are workload-coverage bounds, not a production routing decision or performance claim. The contract check keeps the analyzer thresholds synchronized with the benchmark constants.

A valid capture must contain both the startup message saying `NavGrid.UpdateGraph() resolved` and a later `target reached; aggregate sampling started` message with a summary whose `calls` value is non-zero. If FastTrack's active `PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch` is detected, CycleTrim reports that sampling is disabled instead of mixing FastTrack-replaced traffic into the baseline.

For useful #25 evidence, capture several workload classes separately: an idle colony, a construction/deconstruction burst, doors or automation changing path topology, and critter-heavy activity. Record the ONI build and enabled mod set with each capture. Use complete capture mode for any workload whose bucket fractions will be fed back into `NavGridAdaptiveGateBenchmark`; the normal top-12 summary is only for quick human inspection. The probe is for workload distribution and reachability only; its own instrumentation overhead must not be reported as an FPS or CPU speedup.
