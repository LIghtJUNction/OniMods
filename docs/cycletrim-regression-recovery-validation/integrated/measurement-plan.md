# Integrated-candidate measurement plan

Recorded before integrated builds and timings on 2026-10-30.

## Baseline and reason for a new stage

The remote branch advanced independently to
`62067913a5aea4daa4a8991689c86aad261b1269`, adding circuit-routing compatibility
guards, nested short-candidate invalidation, animation state shortcuts and three
animation workloads. The final candidate must include that work. This stage
therefore compares **exact 6206791** with the resolved integration; the earlier
7aa5003/checkpoint-3 measurements are historical pre-integration evidence.

The local checkpoint `fba163bffc70b4970dab05f23e97cee50ae3391d` and
`../pre-integration-source.patch` retain the earlier source changes. The archived
receipt is `../pre-integration-receipt.json`. Do not use old timings or assertions
as proof of the integrated candidate, and do not call the final power behavior
unchanged relative to 7aa5003: the 6206791 routing guards are retained.

## Fixed comparisons and workloads

Use the separate reflection driver in this directory; it supports the four
arguments of the new animation Scenario constructor. The existing private
Measure functions remain responsible for timed work. No threshold or iteration
count changes are made to improve the results.

Measure all 22 scenario/mode combinations:

- Six conveyor workloads: long-misses, shared-rails, early-hits, single-query,
  short-paths and contained-path, with the same inputs and counts as 6206791.
- Seven animation workloads, each in setter and consumer modes: repeat-single,
  change-single, repeat-range4, repeat-range256, change-range4, change-range256
  and change-last-range256. Each uses 256 symbols, 30,000 timed updates and one
  setter call per update. The changing-range workloads alternate the atlas
  index; change-last-range256 changes the last frame's last vector component.
- Two power workloads: 512 requests per update with either 32 repeated circuits
  or 512 distinct circuits, using the same 6206791 routing guards in both sides.

Conveyor retains three full-batch warmups per variant. Animation retains three
5,000-update warmup pairs, with the first managed copy excluded from timing.
Power retains 20 warmup updates and 100 measured updates per variant, its
original synthetic query work and temporary allocation.

## Execution and evidence

First run baseline and integrated correctness tests and reference compilation.
Then freeze sources and identify both runnable host assemblies with SHA256.

The principal controlled comparison uses seven temporally adjacent prior/new
process pairs for every workload, alternating process order. Each process uses
the original alternating original/patched order. Pin processes to CPU 0 and
set `DOTNET_TieredCompilation=0` equally for both revisions, preserving the
previously documented control for cold tier-promotion differences. No parallel
builds or tests run during timings; the shared host is not reserved exclusively.

Also run the unchanged whole benchmark entry points with default runtime
settings on both revisions. Keep those sessions separate from the controlled
process pairs and validate complete counts: 42 conveyor pairs, 98 animation
pairs (196 variant rows), and 14 power pairs per revision.

Preserve every sample and both original controls. Validate within-process and
cross-revision checksums, direction reads, power query counts, and animation
copy counts/bytes. Changing animation consumers must copy 30,000 times; repeated
consumers must copy zero times after their excluded first upload. Reject missing
or duplicate cases, modes, variants or pair indices. Report min/median/max
times, allocations, paired speedup distributions and how many of seven pairs
improved. No outlier trimming or retrospective workload selection is allowed.

The goal remains measured recovery without changed output or lost compatibility
guards. Residual losses and unchanged-control variability remain visible. No
Steam, Unity/Mono, real-save or game-frame-time claim is established here.
