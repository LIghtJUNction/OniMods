# Regression recovery measurement plan

Recorded before new performance measurements on 2026-10-30.

## Question and fixed acceptance criteria

Can structural reductions in lookup, wrapper and field-comparison work recover
the previous candidate's slow cases without changing results or removing the
workloads that exposed them? Correctness is required first. Report every fixed
workload, including cases that remain slower than the original fixture or the
previous candidate. Do not tune admission thresholds, sample counts, workloads,
or success criteria after observing results. Options remain independent.

The principal comparison is the current candidate against the prior candidate,
`7aa5003c23cd41a385664e684c3debf918a798a2`, rebuilt in an isolated worktree. Also
measure the unpatched fixture in each process so any remaining cost relative to
the original is visible. Historical timing medians are context only; they are
not denominators for this iteration's improvement claims.

### Protocol amendment before formal runs

The first exploratory one-pair processes exposed a runtime-stage confound:
`ReadWatts` was still Tier0 in both revisions, while Harmony-generated setters
were FullOpts. Original conveyor controls also differed markedly between short
isolated processes. Those exploratory samples and the diagnostic observations
are retained. The initial whole-suite benchmark entry points are additionally
run unchanged under default runtime settings as a cross-check.

For the formal interleaved comparison, set `DOTNET_TieredCompilation=0` equally
for both revisions. This fixes the compilation mode from process start instead
of allowing asynchronous promotion to fall into different timed batches. Keep
all original workload definitions, warmups, pairs and output checks unchanged.
Record this setting in `run.json`; do not present it as a game runtime setting
or combine its absolute timings with default-runtime sessions. This amendment
was made before formal timings, based on the observed compilation-stage
difference, not on a change to performance acceptance criteria.

## Revisions and environment

- Prior revision: exact detached worktree at `7aa5003`; preserved runnable host
  assembly and its dependencies are hashed before candidate compilation.
- Candidate: source and test hashes taken after all edits are frozen. If an
  implementation changes after a run, retain that run under its own source hash.
- .NET SDK 10.0.401, runtime 10.0.12, Linux x64, shared AMD EPYC 9V74 host.
- Pin every timed process to permitted CPU 0. Other task agents stop compiling
  and running tests during formal timings. This is not an exclusive host.
- The 737790 reference assemblies are verified against the repository manifest.
  The separate pinned method source declares 744825. Never execute game DLLs.

## Paired execution protocol

Use a small standalone reflection driver to call the **existing private
benchmark measurement methods** in each preserved host assembly. Driver setup,
argument construction, reflection calls and patch installation are outside the
measurement methods' timed regions. The original benchmark algorithms, inputs,
iteration counts and internal checks remain unchanged.

For each family/scenario/mode, run seven outer pairs. Even numbered pairs run
prior then candidate; odd numbered pairs run candidate then prior. Each process
contains one original-versus-patched pair in the same alternating order used by
the existing benchmark. A new process per pair isolates the two assembly
revisions and prevents their Harmony patches or static compatibility state from
interacting. This process protocol is shared by both revisions; absolute values
are not compared to old whole-suite timing sessions.

Warmups remain as originally implemented:

- Conveyor: three complete batches for each original/patched measurement.
- Animation: three original/patched warmup pairs of 5,000 updates, then 30,000
  timed updates per variant; first texture copy is outside timing.
- Power: twenty updates per variant, then one hundred timed updates.

## Fixed workloads

| Family | Scenario | Original parameters |
| --- | --- | --- |
| Conveyor | long-misses | 100 calls; 32 retained paths x 256 rails; 128 new rails |
| Conveyor | shared-rails | 100 calls; same sizes, 126 shared interior rails |
| Conveyor | early-hits | 1,000 calls; one 1,024-rail path; 128 new rails |
| Conveyor | single-query | 1,000 calls; one 1,024-rail path; one new rail |
| Conveyor | short-paths | 1,000 calls; 8 paths x 16 rails; 16 new rails |
| Conveyor | contained-path | 100,000 calls; 8 paths x 256 rails; 32 new rails |
| Animation | repeat-single | 256-symbol buffer; one same-value symbol per update |
| Animation | change-single | Same buffer; one changed symbol per update |
| Animation | repeat-range4 | Same buffer; four same-value symbols per update |
| Animation | repeat-range256 | Same buffer; all 256 symbols rewritten identically |
| Power | repeated-circuits | 512 requests/update, 32 unique circuits |
| Power | all-distinct-circuits | 512 requests/update, 512 unique circuits |

Measure animation both with the setter alone and with the identity/version
consumer performing the actual managed byte copy. Power retains its original
128 synthetic work iterations and 128 temporary allocation bytes per query.

## Required evidence and calculations

Retain compact raw samples with revision, scenario, pair/order, elapsed time,
allocations, checksums and semantic counters (direction reads, power query calls,
animation copies/bytes). Fail on mismatching outputs or counter contracts.
Report minimum, median and maximum timings and paired ratios. Prior/new ratios
use temporally adjacent outer pairs; original/patched ratios use each process's
inner pair. Also report the two original timings for each outer pair so host or
process variation is inspectable. Preserve all samples; do not trim outliers.

Run the full host regressions, existing settings/probe suite and managed net48
reference build after integration. Check the complete PR range with
`git diff --check`. Static checks, host behavior, compilation and real ONI
runtime acceptance remain separate evidence levels. These fixtures do not
establish game FPS, GPU performance or real workload frequency.
