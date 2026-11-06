# CycleTrim and OniHarness: first headless performance iteration

For current hotspot, thread/PSI, Python/FFI, syscall and matched-save A/B
procedures, see [CycleTrim measurement](cycletrim-measurement.md).

CycleTrim called `PatchAll` after the installed ONI loader's `OnLoad` had already
installed the same assembly. The second installation duplicated callbacks and
probe samples, and attempted to rewrite an already-rewritten queue-limit
instruction. Set the probe's owner before calling the native loader and let that
loader install once.

The linked production loader regression fails against the original source and
passes with the fix. The installed 744825 `UserMod2.OnLoad` contract was checked
locally. All 62 source/host checks passed; the actual-game run is separate evidence.

[Sanitized native measurement receipt](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/verification/performance-loop.json)
records the baseline/candidate source revisions, DLL fingerprints, environment,
per-sample CPU/native time and paging, and all seven probe targets. Both captures
used a copied three-duplicant development save, 1x speed, a 30 Hz update cap,
one warmup and five guarded three-second steps. Other existing enabled mods
were retained. No external Harmony owner was present on the measured targets.

The corrected run had no repeat queue-rewrite warning, all seven targets received
calls, every sampled step advanced native time and paused, graphics remained
Null with zero camera callbacks/DRM descriptors, and the original save hash was
unchanged. An intermediate run where native mod reinstall disabled CycleTrim is
excluded. Active-DLC enablement and actual probe output must be checked after a
local DLL update; the legacy base-game `enabled` flag alone is insufficient.

There is **no established whole-game speedup or memory saving** from this run.
Paging/residency varied, only one process per revision was measured, the colony
was small, and opt-in profiling adds overhead. The fixed 1x clock measures guarded
operation cost, not maximum simulation throughput. Probe cumulative windows
include paused main-loop work; the old duplicate samples are not equivalent work.
Do not add overlapping main/worker target durations into an overall CPU total.

OniHarness separately wakes control-file reads and status on transitions while
retaining one-second reconciliation and the game-owned stop deadline. Its first
one-second-step latency medians were 2.91 s and 1.80 s, but paging differed too
much for a causal CPU/simulation claim. The new tool records control-phase timing,
CPU, RSS, swap and major faults so subsequent iterations can distinguish waiting,
memory pressure and actual computation. Reducing temporary allocations and
repeated scans remains a measured hotspot task; these receipts do not measure
memory bandwidth or cache misses.

No Workshop upload or release version change is part of this source iteration.
Private logs/maps/saves stay local. AgentGC tracking could not start because its
shared database was over budget; no complete writer history is claimed.
