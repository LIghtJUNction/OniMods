# Actual-prefix fetch ablation

This self-contained host experiment freezes the exact prior production prefix,
private-buffer candidate V1 and rejected lazy-construction V2 tested in round 2.
Every target uses real Harmony. Only game/Unity APIs are synthetic fixtures;
freshness uses the actual build-744825 integer field shape.

`sources.json` records source hashes and the two target/class renames needed for
independent prior/V1 methods. `Sources/FetchPickupCandidatePatch.cs` intentionally
contains V2, while the shipping file under `mods/CycleTrim` retains V1. These
snapshots are experimental evidence, not a second implementation to ship or edit
when changing the mod.

The measured fixture's terminal blank line is preserved to keep its recorded
SHA-256 exact. A path-specific Git attribute disables only the blank-at-EOF
diagnostic for that frozen file; the ordinary test source is formatted normally.

```sh
dotnet build benchmarks/CycleTrim.FetchAblation -c Release
DOTNET_TieredCompilation=0 DOTNET_gcServer=0 taskset -c 0 dotnet \
  benchmarks/CycleTrim.FetchAblation/bin/Release/net10.0/DefaultDispatchAblation.dll --benchmark
```

Without `--benchmark`, the executable performs 30 equality/work comparisons.
Timing uses two warmup batches, seven rotating/reversed three-way groups per
workload and seven alternating default-dispatch groups. It reports every sample,
allocation count, complete-output checksum and callback/counter work. Ratios are
pairwise; no direct oracle call is used as a timing baseline. A default-path
comparison includes wrapper/dispatch/code-generation differences, not just the
cost of reading the experiment flag.

See [the iteration report](../../docs/cycletrim-fetch-iteration.md) for raw logs,
selection decisions and limits. None of these results establishes Unity/Mono
performance, cache-event behavior or game FPS.
