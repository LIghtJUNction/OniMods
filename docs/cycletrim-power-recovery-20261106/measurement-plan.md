# CycleTrim power-cache recovery: fixed measurement plan

Baseline: `0509cbe65cd8dc10a4918dfce83b5fb9d12d21be`.
Source checkpoint: `4d9cb1f2ee98067ab7df953c2a8950219beec291`.
Recorded 2026-11-06 before candidate performance results.

The change removes repeated identity hashing for one manager's small circuit IDs.
It does not change compatibility checks, game query counts, animation or conveyor
logic. Sparse/high-ID and additional-manager traffic keeps an identity dictionary.
A reference-free direct table grows from 16 to at most 4096 entries (32 KiB payload).
One sparse in-range high ID can allocate more than the previous tiny dictionary;
there is no universal memory-reduction claim.

## Correctness

Run the full existing baseline and candidate optimization executables. New tests
must additionally cover mixed identities, all exact float payloads exercised by
the deterministic seed, both storage paths, the shared capacity/overwrite rule,
nesting, invalidated callbacks, epoch rollover and released-manager collection.
Retain existing settings and net48/reference checks. Do not execute game binaries.

## Measurement

Build both exact revisions on the same runner with .NET SDK 10.0.401 and identical
Release/warnings flags. Pin the benchmark parent and inherited child affinity to
one allowed CPU. Keep the existing workloads, warmups and seven built-in pairs
unchanged. Installation/cold metadata remain excluded exactly as in the original
benchmark; lease creation/lookup/invalidation remain included.

Run seven adjacent old/new process pairs for each family, reversing process order
on every pair. With tiered compilation disabled equally, retain all 22 combinations
(power 2, conveyor 6, animation 14). Repeat the two power scenarios with the default
runtime. Each process contributes one patched-time median to cross-version ratios;
do not treat its seven inner samples as independent cross-version replications.
Validate checksums, callbacks, queries, copy counts and allocation data before
summarizing. Retain every raw process log and median/min/max; remove no outlier.

The source target is faster repeated AND all-distinct power queries against the
prior production helper: median old/new >1 and at least 5/7 improved outer pairs.
Report original-unpatched/new separately: improving on the old cache does not
establish parity with an unpatched all-distinct query. Report the prior broader
2x objective as unmet unless independently demonstrated; this checkpoint does not
replace that objective or justify new thresholds. Unchanged animation/conveyor
controls expose machine/JIT noise and receive no attributed improvement claim.

No Unity/Mono, save/load, actual GPU uploads or game FPS acceptance is implied.
The three experimental switches remain off; Steam-launched acceptance remains
required before merge/release. CI success alone is not a performance-gate pass.
