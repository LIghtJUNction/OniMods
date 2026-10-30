# Conduit display refresh evidence

The selected implementation is **inplace**, based on
`62067913a5aea4daa4a8991689c86aad261b1269` from PR #558. It is submitted on an
independent branch so concurrent animation/conveyor work can be reviewed separately.
Full remains the default. **The selected synthetic timing gate did not fully pass,
and Steam-launched acceptance has not been run. This is a review candidate.**

## Records

| Record | Meaning |
| --- | --- |
| `measurement-plan.md` | Criteria fixed before timing; no workload or threshold changes between implementations |
| `initial-benchmark.log` | First prefix/finalizer implementation; both medium timing gates failed |
| `dispatch-benchmark.log` | Ordinary dispatcher implementation; Half medium failed |
| `inplace-benchmark.log` | Selected implementation; Half medium failed |
| `aggregate-benchmark.log` | Discarded aggregate-reader experiment; Half medium failed |
| `benchmarks.json` | Recomputed distributions, every failed gate, latest and selected round explicitly distinguished |
| `host-build.log`, `host-tests.log` | Strict selected host build and all 4566 optimization assertions |
| `options-probe.log` | Settings and probe regressions, including 76 settings assertions |
| `reference-build.log` | Strict selected net48 managed build; 0 warnings/errors, `IncludeOniSim=false` |
| `reference-hashes.json` | All 13 pinned compile-reference hashes |
| `reference-surface.json` | 49 reflected signatures in the stripped 737790 reference; not method-body execution |
| `source-provenance.json` | Separate fixed 744825 method-body sources and official FastTrack sources |
| `variant-hashes.json` | Measured production-helper source and host DLL fingerprints for all four rounds |
| `negative-gate-tests.json` | Three deliberately incorrect gates rejected by the unchanged regression source |
| `receipt.json` | Selected source/evidence hashes, commands, outcomes and limitations |

The selected host DLL rebuilt after reverting the aggregate experiment is byte-for-byte
identical to the DLL measured in the inplace round. No favorable rerun of unchanged
selected code was substituted for that round.

`initial-warmup-failure.log` retains an early harness mistake: the zero-allocation
assertion was applied during warmup. The corrected harness retains warmup allocation
data and requires zero additional measured-thread allocation only in formal samples.
This stopped attempt produced no formal samples and is not counted as a fifth round.

The aggregate experiment was measured on the host before the parallel reference-build
failure was inspected. The game's global `Expression` type initially shadowed LINQ's
type. `aggregate-reference-name-collision.log` preserves the error;
`aggregate-qualified-reference-build.log` records the successful namespace-qualified
build. The unqualified measured and qualified source changes are both retained.
This experiment did not satisfy all premeasurement conditions and is not the selected
implementation or evidence for release readiness. Its slower results remain visible.

## Reproduce correctness and build

Use .NET SDK **10.0.401** / runtime **10.0.12**. Run from the repository root:

```bash
dotnet build tests/CycleTrim.Optimizations.Tests/CycleTrim.Optimizations.Tests.csproj \
  -c Release -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests/CycleTrim.PerformanceProbe.Tests.csproj \
  -c Release -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -- --skip-synthetic-performance

python3 scripts/fetch_oni_reference_assemblies.py \
  --manifest ci/oni-reference-assemblies.json --output /tmp/cycletrim-oni-managed
dotnet build mods/CycleTrim/CycleTrim.csproj -c Release \
  -p:OniManagedPath=/tmp/cycletrim-oni-managed -p:IncludeOniSim=false \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
```

The local selected build reused a successful restore with `--no-restore`; NuGet
auditing and the warning policy were not disabled for the production build. A prior
network-restricted restore was retried with network access. Reference binaries are
used only for compilation and static metadata inspection. None is executed or included
in these committed records.

## Reproduce measurement and summary

Run the benchmark in a fresh process, separate from compatibility tests that deliberately
install foreign patches. The recorded host was shared, with affinity restricted to CPU 2:

```bash
flock /tmp/oni-tiered-benchmark.lock taskset -c 2 dotnet \
  tests/CycleTrim.Optimizations.Tests/bin/Release/net10.0/CycleTrim.Optimizations.Tests.dll \
  --benchmark-conduit
python3 docs/cycletrim-visual-refresh-validation/summarize.py --selected-round inplace
```

The benchmark returns a nonzero exit code if any predeclared medium/large median-time
gate fails. The summarizer returns success when all retained data are internally
consistent; that success **does not turn timing failures into passes**. Its JSON reports
`selected_round_timing_gate_passed: false` for this submission.

Every full round has 12 scenarios, 3 warmup pairs and 7 measured pairs per scenario,
6000 frames per sample, and alternating original/candidate order. Four rounds contain
480 pairs including warmup, of which 336 pairs / 672 samples are formal measurements.
All formal measured-thread allocation counts are zero. Small, changing, audio, overlay,
low-FPS and frequent-simulation controls are retained. No whole-game FPS is measured.

## Reconstruct earlier candidates

Use a disposable checkout. The variant patches deliberately use zero context so blank
context lines are not confused with changed-line trailing whitespace. From the selected
source, reconstruct one variant at a time:

```bash
git apply --reverse --unidiff-zero docs/cycletrim-visual-refresh-validation/initial-to-selected.patch
# Or, in another clean copy:
git apply --reverse --unidiff-zero docs/cycletrim-visual-refresh-validation/dispatch-to-selected.patch
# Or apply the discarded experiment in another clean copy:
git apply --unidiff-zero docs/cycletrim-visual-refresh-validation/aggregate-measured.patch
```

`aggregate-fixed.patch` is the namespace-qualified, compileable discarded experiment;
no separate favorable timing sample is claimed for it. `variant-hashes.json` lets readers
check reconstructed helper and benchmark sources. Initial/dispatch tests acquired later
compatibility assertions after those timing runs; the benchmark source is unchanged
across all rounds, and each measured host binary has its own recorded fingerprint.

## Remaining acceptance

The fixed API references declare **737790**; the separately inspected method bodies
declare **744825**. Host Harmony and pinned Harmony are both **2.4.2**. Their matching
version does not establish Unity/Mono detour, live-delegate or native-resource behavior.

Before merging, satisfy the outstanding timing criterion or explicitly redesign and
revalidate the feature, then record Steam-launched tests with commit, DLL, game build,
save, mods and settings. Cover gas/liquid contents, overlays, audio, camera motion,
two worlds, construction/removal, pause/speed changes, save/load and cleanup. Measure
frame-time distributions on the same save under Full/Half/Quarter. Dependencies patched
only after installation still require the documented restart/fallback verification.
