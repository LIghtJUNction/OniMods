# Evidence stages and reproduction

## Current integrated stage

The current baseline is `62067913a5aea4daa4a8991689c86aad261b1269`. It includes
the independently submitted routing guards and nested-call fixes. Final
validation uses the separate tools and results under **`integrated/`**:

- [Measurement plan](integrated/measurement-plan.md)
- [Controlled paired samples](integrated/formal-controlled/samples.jsonl) and
  [summary](integrated/formal-controlled/summary.json)
- [Default-runtime cross-check](integrated/default-runtime/summary.json)
- [Current receipt](receipt.json)

Build the two host executables from exact 6206791 and the final candidate, using
the same SDK and options recorded in the receipt. Build the current driver:

```bash
dotnet build docs/cycletrim-regression-recovery-validation/integrated/BenchmarkDriver/BenchmarkDriver.csproj \
  -c Release -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
```

Then invoke `integrated/run_benchmarks.py run` with paths to that driver, the two
host DLLs, a new output directory and the recorded .NET executable. Use CPU 0,
seven pairs and `DOTNET_TieredCompilation=0` for the controlled protocol. Omitting
`--family` runs all 22 required scenario/mode combinations. The whole-program
default-runtime cross-check uses each host's original `--benchmark-conveyor`,
`--benchmark-animation` and `--benchmark-power` entry points, separately.
`integrated/default-runtime/run.py` runs all six complete CLI sessions serially,
clears tiering and JIT diagnostic overrides, captures each full log and validates
the expected sample counts. It accepts `--prior`, `--candidate`, `--output`,
`--dotnet` and `--cpu` arguments; use a new output directory for each run.

The current driver supports the four-argument animation Scenario constructor
and all seven animation workloads, including changing ranges and a change at
the last field of the final symbol. The current summary tools reject incomplete
workloads, pairs or differing semantic counters.

## Historical pre-integration stage

The tools directly in this directory (`BenchmarkDriver/`, `run_benchmarks.py`),
the top-level `formal-controlled/`, `default-runtime/` and `exploratory/` results
are intentionally preserved **pre-integration evidence**. Their baseline is
7aa5003 and their final local checkpoint is fba163b; they do not establish the
behavior or performance of the later integration.

The historical driver reflects a three-argument Scenario constructor and uses
four animation workloads. It is retained to reproduce that stage and must not
be used with 6206791 or the integrated host assembly. Its files and hashes have
not been replaced with the new driver.

[The historical receipt](pre-integration-receipt.json) identifies those source
and binary hashes. Apply `pre-integration-source.patch` with
`git apply --unidiff-zero` to exact 7aa5003 to reconstruct its nine changed source
and project files. Their hashes were checked against the historical receipt.
The fba163b checkpoint is local provenance, not a published GitHub commit.

All game-runtime acceptance boundaries remain in force. These artifacts contain
independently written fixtures, source patches and validation records; no game
binaries or decompiled implementation source are included.
