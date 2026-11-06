# CycleTrim fetch experiment — 2026-10-30

The retained experiment replaces complete candidate records in the lookup dictionary
with integer positions into a private list. It reduces repeated dictionary writes
and bulk-copies the winners only when collection finishes. The measured benefit
depends on the workload; the candidate remains explicitly opt-in.

## Outcome

- Retained V1: private dense storage plus `Dictionary<PickupKey, int>`.
- Rejected V2: constructing `Pickup` only after a candidate wins did not produce a
  consistent improvement in direct paired measurements. Its complete source and
  failed-to-improve results remain reproducible.
- Corrected the synthetic API model: ONI build 744825 uses integer `freshness`,
  not the floating-point field used in the first host experiment.
- Hardened layout selection and evidence collection. The companion measurement
  change is [OniHarness #8](https://github.com/LIghtJUNction/OniHarness/pull/8),
  stacked on the existing verification-bootstrap PR #6.

The archive's previous small-colony run did not establish a whole-game speedup.
This executor has no Steam executable or registered Steam library; no game was
launched in this iteration. The executable results below are host behavior,
static game metadata, reference compilation and synthetic timing.

## Mechanism and preserved behavior

The previous collector stores `(key, complete Pickup)` in a dictionary. When a
new candidate wins, its value is written through the dictionary again. At the
end it enumerates every dictionary value into the destination list.

The retained candidate stores `(key, integer index)` and updates the private
list at that index. It uses `AddRange` to append the winners to `finalPickups`,
then invokes the same final comparator. Keys, navigation checks, path-cost
conversion, ranking and first-wins ties are unchanged.

Keeping the list private is required for observable behavior. Publishing winners
directly into `finalPickups` during collection lets eligibility/navigation
callbacks see partial results. A deliberately restored direct-publication
variant fails the new `callback observation Compact` regression even when final
results happen to match. The private list also preserves the existing exception
behavior, including results already published by an inner recursive invocation.

Both containers are cleared in `finally`. The existing per-thread single-slot
pool isolates nested invocations and releases extra rentals rather than retaining
an unbounded pool. Weak-reference tests check that successful and exceptional
calls do not retain candidate/owner references through the pool.

## Version and fixture correction

Production baseline: `14c08591cbd0bf66cd0f53b37bfb498bdef4abf6`.

The private supplied build-744825 `Assembly-CSharp.dll` has SHA-256
`6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1`.
Static metadata inspection establishes `freshness: Int32` and `foodQuality:
Int32` in both `FetchManager.Pickup` and `FetchManager.Fetchable`, with
`Pickup.PathCost: UInt16`. The assemblies were used for compilation and
metadata inspection, not executed as test implementations.

`round1.log` is retained as superseded evidence: its fixture modeled freshness
as `float`. It must not support a current-game data-shape claim or be divided
into a later timing to infer incremental speedup. The round-2 comparisons use
one corrected integer fixture for the prior production prefix, V1 and V2.

The first ordinary-path diagnostic also compared a direct oracle with a Harmony
prefix and allocated a sorting delegate in the oracle. It is not an estimate of
production dispatch cost. Round 2 replaces it with the actual prior prefix
through an equivalent Harmony wrapper and cached production sorting delegate.

## Final correctness and build checks

| Check | Executed outcome | What it establishes |
|---|---|---|
| Linked production fetch code, real Harmony, synthetic game/Unity APIs | 5,899 assertions / 1,462 three-way cases pass | Equivalence to the prior CycleTrim collector in these host scenarios |
| Wrong direct-publication variant | Fails `callback observation Compact` | Tests detect the identified intermediate-state regression |
| Layout control | 42 protocol assertions plus lifecycle regressions pass | Pause gating, nonce/PID/counters, bounded requests, rollback and cleanup in host fixtures |
| Full CycleTrim managed build against supplied 744825 references | 0 warnings, 0 errors; `IncludeOniSim=false` | Managed API compatibility and packaging, not native OniSim or game loading |
| Probe analyzer | 102 added numeric/compatibility cases pass; original implementation fails | Invalid booleans/nonfinite measurements are rejected |
| Scoped source contracts | Pass | Pool cleanup and probe integration text remains consistent |
| OniHarness #8 on #6 | 12 managed checks, 126 Python tests, package boundaries pass locally | Game-free measurement/control tooling, not ONI gameplay |

Fetch cases include key collisions, integer extrema, ushort conversion, strict
ties, callback work/order, callback mutations, failures and recovery, same/different
instance recursion, mode capture, thread isolation and reference release.

## Matched host measurements

The [original protocol](cycletrim-fetch-iteration-protocol.md) was fixed before
round 1. Round 2 corrected the API fixture and replaced the oracle diagnostic
with an actual-prefix three-way comparison; its complete method and sources are
recorded in the round-2 receipt. It ran on Linux x64 with .NET SDK
10.0.401/runtime 10.0.12, Harmony 2.4.2,
CPU 0 affinity, tiered compilation disabled and workstation GC. There were two
warmup batches per variant and seven groups with rotating/reversed order for
each of ten workloads. Each timed window includes gathering, sorting and
consuming every result; checksums, winner counts, eligibility/navigation work
and actual selected-layout counters are checked.

Ratio = prior elapsed time / retained-V1 elapsed time. Above 1 is faster in the
specified fixture. The last column directly compares V1 time / V2 time in the
same groups; below 1 favors retaining V1. These are medians of per-group ratios,
not ratios of separately summarized medians.

| Synthetic workload | Prior / retained V1, median ↑ | All seven ratios | V1 / rejected V2, median |
|---|---:|---:|---:|
| Empty input | 0.960× | 0.897–1.071× | 0.955× |
| 4 distinct candidates | 1.048× | 1.018–1.206× | 0.985× |
| 4,096 distinct candidates | 1.130× | 0.913–1.531× | 1.033× |
| Many duplicates, few improvements | 0.920× | 0.693–1.564× | 0.941× |
| Every duplicate improves the winner | 1.349× | 1.095–1.969× | 0.890× |
| Deliberate hash collisions | 1.250× | 1.074–1.467× | 1.004× |
| Most inputs ineligible | 1.021× | 0.682–1.106× | 0.900× |
| All inputs unreachable | 1.011× | 0.865–1.223× | 0.990× |
| Many candidates share cells | 0.931× | 0.810–1.162× | 1.024× |
| Unsorted distinct candidates | 1.133× | 1.048–1.246× | 0.963× |

All warmed prior/V1/V2 timing windows measured zero current-thread managed
allocations. This is not a retained-memory or whole-process-memory measurement.

The strongest repeatable positives in this run were changing winners, deliberate
collisions and unsorted distinct input. Stable duplicate-heavy and shared-cell
input had median regressions, with ranges crossing 1. Some distributions are wide on this shared host, so
small median differences are not decisive. No threshold classifier was added to
make the synthetic workloads choose a favored algorithm.

Round-2 prior / experiment-disabled V2 medians were 0.925× for empty input,
0.974× for four distinct candidates and 0.991× for 4,096 distinct candidates. Their ranges
were 0.791–1.332×, 0.912–1.038× and 0.889–1.046×. The diagnostic includes wrapper,
dispatch and code-generation differences. It does not prove zero added overhead
or an isolated volatile-read cost. This diagnostic used the frozen V2 wrapper;
it was not rerun after production reverted to V1. Real-game acceptance is still needed.

## Measurement protections

The layout controller is created only for `--cycletrim-layout-control=PATH`.
Ordinary launches retain the dictionary collector. Requests have the form
`baseline NONCE` or `compact NONCE`; a successful acknowledgment contains PID,
layout, nonce and cumulative calls for both layouts. Switching is allowed only
while a loaded game is paused. The collector captures its mode once per invocation.
Malformed/oversized requests preserve the current state. A failed acknowledgment
publication rolls back the mode, and initialization failure destroys its temporary
owner instead of leaving a partially enabled experiment.

OniHarness #8 reserves result names before stepping, compares PID plus process
start time, rejects nonfinite progress and decreasing counters, and retains
warmups and failed attempts. Its CPU observation ends after final pause
verification; both sides of a future comparison must use that same harness.
The probe analyzer separately rejects boolean integer fields and nonfinite
means while still accepting a valid negative heap-size delta after collection.

## Reproduction and evidence

- [Round-2 raw samples](cycletrim-fetch-iteration-validation/round2.log),
  [source/command receipt](cycletrim-fetch-iteration-validation/round2-metadata.json),
  [derived retained-candidate summary](cycletrim-fetch-iteration-validation/round2-summary.json).
- [Superseded round 1](cycletrim-fetch-iteration-validation/round1.log) and
  [its receipt](cycletrim-fetch-iteration-validation/round1-metadata.json).
- [Version and evidence boundaries](cycletrim-fetch-iteration-validation/provenance.json),
  [negative regression](cycletrim-fetch-iteration-validation/negative-direct-publication.log),
  [fetch tests](cycletrim-fetch-iteration-validation/fetch-tests.log),
  [probe tests](cycletrim-fetch-iteration-validation/probe-tests.log), and
  [744825 build log](cycletrim-fetch-iteration-validation/reference-build-744825.log).
- [Final validation commands](cycletrim-fetch-iteration-validation/final-validation-commands.json)
  bind the successful final checks to the retained V1 source hash. An earlier
  validation invocation incorrectly passed `-warnaserror` to the test executable;
  its [failure log](cycletrim-fetch-iteration-validation/final-check-invalid-invocation.log)
  and [command](cycletrim-fetch-iteration-validation/final-check-invalid-invocation.json)
  are retained separately from the corrected successful run.
- [Final source/evidence hashes](cycletrim-fetch-iteration-validation/final-file-hashes.json),
  [source-contract results](cycletrim-fetch-iteration-validation/source-contracts.json),
  and [copied ablation functional check](cycletrim-fetch-iteration-validation/ablation-functional.log).
- [Self-contained actual-prefix ablation](../benchmarks/CycleTrim.FetchAblation/)
  freezes the exact tested prior/V1/V2 sources. Its `Sources/FetchPickupCandidatePatch.cs`
  deliberately remains rejected V2; the shipping source under `mods/CycleTrim`
  is retained V1. Do not install the benchmark assembly as a game mod.

```sh
dotnet run --project tests/CycleTrim.Fetch.Tests -c Release
dotnet build benchmarks/CycleTrim.FetchAblation -c Release
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 taskset -c 0 dotnet \
  benchmarks/CycleTrim.FetchAblation/bin/Release/net10.0/DefaultDispatchAblation.dll --benchmark
python3 scripts/verify_cycletrim_fetch_pool_source.py
python3 scripts/verify_cycletrim_perf_probe_analyzer.py
```

The supplied archive's three selected chunks and 1,837 extracted source/evidence/
reference files were verified against their hashes. Full archive extraction and
`run.sh verify/doctor/build` were not completed as a full portable-game run.
Game binaries, save data, archive chunks and private game logs are excluded from
this change.

## Acceptance decision and next experiment

Keep the candidate experimental and do not merge/release it as a general speed
improvement. In an authorized Steam environment, use a disposable copy of the
same supplied save, the exact candidate build, the same other mods/settings,
and matched warmups. Verify the headless state (batch mode, Null graphics, zero
camera draws), each requested layout's PID/nonce and positive call deltas,
positive native simulation progress, and final pause for every sample.

Collect at least five alternating pairs. Reload the same initial save for each
side when measuring equal work; a continuing save is a separate experiment
whose evolving state must be recorded. Compare CPU per native simulation second
and hotspots, while retaining failed runs, paging and background-load evidence.
PMU data is optional evidence with its own coverage requirements; unavailable
counters are not zero cache misses. A representative larger save needs an
explicitly selected authorized fixture before making late-game claims.

## Claim review

| Question | Evidence/decision |
|---|---|
| What changed? | Dictionary values became indices; private publication preserves callback state. |
| Is the explanation reproducible? | Fixed sources, raw samples, commands, hashes and rejected variant are included. |
| Is the effect broad? | No; gains and regressions coexist, so promotion is rejected. |
| Is evaluation complete? | Host and reference checks pass; native game acceptance is outstanding. |
| Did the second design improve the first? | Not consistently in direct pairs; V2 was removed from production. |
