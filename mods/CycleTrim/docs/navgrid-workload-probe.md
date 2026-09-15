# NavGrid workload probe

This is a developer-only measurement path for issue #25. It is disabled unless the ONI process starts with `CYCLETRIM_NAVGRID_PROBE=1` (also accepts `true` or `yes`). Normal CycleTrim users pay no per-call probe cost because Harmony skips the patch at load time when the variable is absent.

The probe observes the real parameterless `NavGrid.UpdateGraph()` before expansion and aggregates only data that a future cheap dispatcher could know: unique dirty-cell count as stored by ONI, `updateRangeX`, `updateRangeY`, and seed bounding-box density. It does not replace `UpdateGraph`, alter `DirtyCells`, or select an optimization path.

Reports are cumulative histograms. Formatting/logging is deferred through `GameScheduler.ScheduleNextFrame`; the `UpdateGraph` prefix only scans the existing dirty list and updates fixed-size counters. Reports are requested after call 1, 64, 256, 1024, and then every 4x growth. Each report shows the most common dirty-count/range/density buckets rather than logging individual calls.

A valid capture must contain both the startup message saying `NavGrid.UpdateGraph() resolved` and a later `target reached; aggregate sampling started` message with a summary whose `calls` value is non-zero. If FastTrack's active `PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch` is detected, CycleTrim reports that sampling is disabled instead of mixing FastTrack-replaced traffic into the baseline.

For useful #25 evidence, capture several workload classes separately: an idle colony, a construction/deconstruction burst, doors or automation changing path topology, and critter-heavy activity. Record the ONI build and enabled mod set with each capture. The probe is for workload distribution and reachability only; its own instrumentation overhead must not be reported as an FPS or CPU speedup.
