# Native differential fixtures

## 2026-10-21 source recheck and rejected scheduling experiment

The native source tree at `adc4b2e` rebuilds here as `55d5a236…` with Rust
1.99.1. All 43 Release Rust tests and 57 Python harness tests pass. Both one
and four workers pass all 287 official-library cases / 9,739 publications;
the parallel emitter case observes `OniSim-cell-0` through `OniSim-cell-3`.
See `results/linux-744825-native-recheck-20261021.json`. This is native fixture
evidence; no Steam/Unity client was launched in this recheck.
The earlier one/four-worker scaling gates remain without a passing result;
these correctness runs do not establish a four-worker performance improvement.

The frame benchmark now records missing or unreadable Linux PSI counters as
unknown, with the OS error, instead of aborting when a container does not expose
`/proc/pressure`. Required process memory and worker observations stay strict.

An experiment divided disease cleanup into equal row bands for the actual
worker count. All 96 timed 96×96 final states match the official library and
all samples observe four working cell threads. The original performance gates
fail: mixed-germ CPU rises 4.30% and wall time rises 5.69%. Production scheduling
is unchanged. The proposal and every sample are retained in
`proposals/disease-balanced-rows-20261021.patch` and
`results/linux-744825-balanced-disease-rows-20261021.json`.

## Earlier acceptance records

Earlier source-built native: `2772e526…`. The borrowed pressure-sweep source
passes 43 release Rust tests and 50 pure Python tests. Both 2772e526 and preceding
8bd415ce pass the expanded 287 cases / 9739 publications at one and four workers.
The 24 new flow-metadata fixtures retain exact signed-zero, subnormal, infinity
and quiet/signaling NaN words. The public-patch build and canonical build both
reproduce the measured native identity. See
[the current receipt](results/linux-744825-pressure-sweep-borrows.json).

Incremental native timing against 8bd415ce passes the fixed CPU/wall/RSS gates
at 96x96 and 192x192. This does not establish direct official-library timing,
whole-game performance, graphical acceptance or four simultaneous full games.
One genuine Steam headless normal save/load roundtrip has passed for the new
pair; actual worker names, graphics and complete runtime acceptance remain pending.
Sections below describe earlier
binaries and retain their original test counts and scope.

Runtime library audits now automatically record actual physics/pool thread names
and their scheduler runtimes inside the PID-incarnation checks. After a bounded
warmup step, use `verify_instances.py --require-cell-workers 4` with the other
usual audit arguments to enforce the exact observed pool names. Serial mode uses
`--require-cell-workers 0`; setting workers=1 does not create a one-thread pool.
Missing/unreadable/changing task sets fail rather than being recorded as zero.
The five new fake-proc/API-double tests are game-free evidence, not recovered
observations for the earlier stopped headless process.

Earlier source correction: the mixed-material thermal multiplication order now
matches the original instruction sequence. The then-current native `c301cc3e…`
passes 227 cases and 9103 publications with [one](results/linux-744825-workers1.json)
and [four](results/linux-744825-workers4.json) workers; all 31 release
unit tests also pass. The three new interleaved-phase soak fixtures fail against
the preceding binary and pass after correction. See
[the exact receipt](results/linux-744825-mixed-thermal-order.json).
Ordered Steam headless and graphical four-worker acceptance also passes for the
small survival fixture, including save/load, complete textures and actual frame
inspection. Exact ZIP/source delivery requires its separate verification receipt;
these runs do not establish broader compatibility or overall performance.
Older result sections below retain their original case counts and binary scope.

Four additional mixed-phase active-region soak fixtures now cover disjoint and
overlapping regions, both traversal orders at the 32-cell boundary, 24 frame
batches and four native save/reload rounds. Selected runs pass all four cases /
308 publications for c301cc3e and the private 9a04afbd inline proposal, each with
one and four workers. This supplements the earlier 227-case full-suite receipts;
at that time, a complete 231-case run had not yet been performed. See the
[multi-region supplement](results/linux-744825-multi-region-soak.json). These
remain authored native inputs, not realistic DLC-world/colony acceptance.

These authored fixtures compare the installed ONI 744825 Linux simulator with
the Rust core. Each library/case runs in its own child process with a timeout.
This mode does not launch Unity or modify a colony. It neither redistributes nor
loads community simulator code. The official library must match the SHA-256
pinned in `host.py`; the candidate must expose project ABI version 1.

From the repository root, after building the candidate:

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/run.py \
  --reference "$HOME/.local/share/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Plugins/libSimDLL.so" \
  --candidate mods/CycleTrim/obj/Release/OniSimNative/release/liboni_sim_core.so \
  --workers 4 --output mods/CycleTrim/OniSim/conformance/artifacts/matrix-workers4
```

Repeat with `--workers 1` and a separate output directory. `--case NAME` selects
one fixture. Exit status is nonzero for any data difference, native diagnostic,
invalid publication, child crash/timeout, or missing required parallel workers.

`--jobs 2` runs at most two independent case pairs concurrently; each pair still
executes reference and candidate in separate processes, with its own files and
world. `--workers` sets each candidate's cell-worker budget. `jobs * workers`
must fit the current CPU affinity when jobs exceed one. The default is one job
to minimize memory. Result ordering stays deterministic; an interrupted/error
path cancels unstarted cases and drains the active cases under their existing
child timeouts. `comparison_seconds` measures this test loop, not solver speed.
This does not launch or validate multiple complete games. Do not run these
correctness jobs concurrently with a native timing benchmark.

Both official diagnostic IDs 0 and 1 fail a normal fixture. Two explicitly
negative conduit fixtures require the specified fatal id 1 report from Add or
Set. Only their reference children permit the pinned SIGINT assert to return,
and only the exact known assert plus the invalid-temperature report are accepted.
They stop after the fatal path; fallback state is not compared. A third negative
fixture calls the explicit DebugCrash export in isolated children of both roles,
requiring one SIGINT followed by the exact fatal assertion/file/line. Unexpected
reports still fail. These hooks are never invoked in colony acceptance.

## Compared data

Every observed publication, including empty batches, compares all public cells in the twelve grid/backwall arrays,
accumulated flow, all five CPU texture images, the frame count and all 29 ordered
event lists. Floats retain their exact IEEE words. Only ABI padding is excluded;
event ordering, small float differences and initial sentinel values are retained.
Roundtrip fixtures also compare the length and SHA-256 of the complete native
save image, reset the simulation session, load each implementation's own image,
and compare initialization and three further frames.

## Control the actual batch sequence

API call order alone does not fix which queued frames join an asynchronous
Step. Start/Prepare can release an empty Step. The native accumulated-flow
counter resets every fifteenth Step, including empty batches, so racing two
independent polling loops produced occasional valid differences at that boundary.
Those failed traces are retained; no field was excluded or given a tolerance.

The harness now observes a completed batch before queuing a new frame. It then
publishes the completed empty batch, waits for the queued frame, and publishes
that frame. Both snapshots and the exact message counts are compared. Rust's
`ONISIM_IsReady` only observes the mailbox under its mutex. For the pinned
reference only, the harness reads FrameSync's ready flag under its own mutex at
the verified addresses documented in `host.py`; it does not mutate physics or
release the handshake. This is a fingerprint-bound fixture observation, not a
portable official readiness API. Ordinary game scheduling is unchanged.

## Package the tested outputs

After an ordinary build and real-game acceptance, refresh documents/receipts by
running only the packaging target. A full build reruns ILRepack and can change
the managed DLL identity even when its source is unchanged.

```sh
rtk proxy dotnet msbuild mods/CycleTrim/CycleTrim.csproj -target:PackageMod \
  -p:Configuration=Release \
  -p:DistPath="$PWD/mods/CycleTrim/dist/"
rtk proxy python3 mods/CycleTrim/OniSim/conformance/build_source_package.py \
  --managed-path "$HOME/.local/share/Steam/steamapps/common/OxygenNotIncluded/OxygenNotIncluded_Data/Managed"
rtk proxy python3 mods/CycleTrim/OniSim/conformance/verify_package.py
```

The verifier rejects a different native library or managed DLL, stale fixture
sources/receipts/documents, failed acceptance and generated trees in the source
archive. Source packaging preserves `mods/CycleTrim` under a canonical root,
with the original LICENSE, LICENSE.MIT, LICENSING.md and UPSTREAM.toml, so native
Cargo license paths resolve correctly. `build_source_package.py` extracts the
exact source archive into a fresh directory, compiles C# and Rust against the
specified installed game references, and writes `dist/source-build-validation.json`.
It does not inherit ignored checkout build props. Repacking removes old build
and delivery receipts. The verifier requires the current archive's successful
clean-build receipt and writes `dist/delivery-validation.json` only after all
gates pass, including actual game acceptance for the same native/managed pair.

The 129x129 disease-emitter fixture crosses the actual parallel threshold and
requires cell workers when `--workers` exceeds one. Its overlapping species
exercise the Lean-proved diamond traversal through the real component dispatcher.
Worker names are recorded from `/proc/self/task` before shutdown.

`results/` contains compact, shareable receipts with library/harness/fixture hashes.
`artifacts/` contains generated raw snapshots, logs and debugger traces and is
ignored by Git and excluded from CycleTrim's source package. Hashes bind a result
to its tested artifact; rebuilding requires rerunning the comparison.

## Recorded run (2026-10-21)

Both [one worker](results/linux-744825-workers1-1f56c9b2.json) and
[four workers](results/linux-744825-workers4-1f56c9b2.json) passed all 224 cases and
compared 8440 publications each (221 positive contracts and three negative diagnostics). The large emitter fixture recorded exactly
`OniSim-cell-0` through `OniSim-cell-3` in the parallel run, and none in serial.
Candidate SHA-256:
`1f56c9b2f0dc47f0db3e24015e6d927492d61cf9c9ade4f17c6bcf3e6c897c33`.

The receipts also record all 62 catalogued message IDs actually sent successfully
(59 implemented handlers and three compatible enum-only no-ops). This is entry
coverage, not proof of every input variant. Added cases exercise replacement and
displacement, digging, property bits, disease commands, all radiation selectors,
allocation/clear/load, mixed fluids, unstable solids, diagonal falling and actual
pressure-damage events. Registration/overwrite/clear of interaction records is
compared through normal frames; those fixtures do not claim to execute the legacy
reaction helper. Linux system-info null returns and idempotent release are also
compared. Required events/array changes prevent the new physical cases from
passing without exercising their target behavior.

## Remaining acceptance work

The allocation-only case uses `fixed_clock.c`, compiled into its private artifact
directory and preloaded only into those fixture children. The original
AllocateCells takes a seed from `time()` at pinned ELF `0x3176b`; Rust uses
`SystemTime`. Both must consume the same fixed realtime instant (1900000000),
and the child checks that its allocation actually called the corresponding
clock. Monotonic clocks and timeout deadlines remain live. This addresses an
observed same-official-binary disagreement across wall-clock seeds, without
changing simulated cells, tolerances or the production implementation. The
source is hashed with the driver in each receipt. It is never preloaded into
ONI. `python3 test_runner.py` checks missing-library preflight and fixed realtime
with advancing monotonic time, probe build identity and texture receipt validation
(nine game-free tests). The comparator checks both sets of array/event keys and
distinguishes a missing field from an explicit null. It binds harness/library
hashes before execution, checks them again before issuing a summary, and removes
any previous summary before starting children.

Passing these cases establishes the specified fixture results. It does not
establish every element/interaction, component mode or malformed input,
historical colony file, multi-world topology, full game texture upload or long
colony stability. Authored native formats 0 through 15, the seven conduit exports,
component lifecycles, world rectangles, partial melting, sublimation and offgas
are exercised in specified fixtures; broader variants remain open. The current
previous null-backwall candidate (d45cb10e) has graphical and headless runtime receipts;
the earlier early-colony smoke applies to its recorded prior hash. Native host tests also
cover queue exhaustion, worker faults, lease abandonment and 30,720 ordered
frames across sixteen fresh worker lifecycles (28 tests total).
Expand these cases and real Steam-launched
acceptance runs before changing `gameReady` or `runtimeValidated`.

The three null-backwall cases exercise the official 0xffff absent marker,
change events, valid restoration, and serialization normalization. Their old
Rust failures are retained. The optional [texture probe](texture_probe/README.md)
compares native buffers, Unity CPU texture bytes and async GPU readback in an
explicit graphical test session; compilation alone is not a passed probe run.
The previous d45cb10e candidate's successful single-frame, five-buffer observation is
recorded in `results/linux-744825-graphics.json`, with restored test installation.
The generated three-duplicant survival colony was saved without overwrite.
An independent Steam headless run loaded its copy, advanced before saving,
reloaded at equal clock/dupe count, verified a valid cell at displayed precision,
and advanced again. See `results/linux-744825-runtime.json`; the first invalid
hardcoded sample is recorded as a failed check, followed by a deliberate second
reload using a live observed cell. Full compatibility remains pending.

Eight asymmetric thermal fixtures additionally cover insulation bytes 0, 127,
254 and 255, both hot/cold orientations, different heat capacities/surface areas,
and native save/reload. The 254/255 boundary selects the minimum-conductivity and
geometric-mean branches respectively; these small fixtures do not prove thermal
physics itself is parallelized.

Nine further topology/lifecycle fixtures cover odd grid sizes, overlapping and
reversed active regions, two native world rectangles freed/reinitialized on
either side, and eight successive save/reload cycles. The repeated lifecycle
case exposed a convection temperature clamp error also reproducible without
reloading. A separate two-water-cell fixture reproduces the f32 weighted mean
rounding below its cold endpoint; d45cb10e failed and 82460a88 passes. See
`results/linux-744825-convection-regression.json`. Current-hash graphical and
headless game acceptance must be repeated before updating the delivered package.

Four save-buffer fixtures also compare consecutive BeginSave replacements,
absent-backwall normalization and signed header origins. All replacement images
are copied before their pointers expire and compared by byte count/SHA-256.
Header-only probes do not claim that negative load offsets are valid. Driver
tests cover overwriting/reusing the same native buffer and releasing it after
invalid metadata or a copy exception. These run at synchronized publications;
they do not establish safety for unsynchronized concurrent game saves.
## Complete native frame timing

`benchmark_frames.py` compares a preserved baseline and a candidate in six
fixed 96x96 scenes: mixed gas/liquid flow, solid heat transfer, uniform gas,
mixed flow seeded with germs, two-species gas flow, and vacuum. The germ fixture must retain infection in
at least half of the grid cells after the timed batches. The two-species scene
also requires both species to remain present.
The vacuum fixture requires substance notifications for at least half the grid;
unchanged cell arrays alone do not establish that its notification path ran.
Each fresh process performs two warmup batches followed by six timed batches,
each requesting ten simulation seconds. Publication counts are checked; this is
requested simulation time, not an observed game clock. Full final publications
must match a separate pinned official run exactly, including ordered events.

```sh
rtk proxy python3 mods/CycleTrim/OniSim/conformance/benchmark_frames.py \
  --reference /path/to/libSimDLL.so --baseline /path/to/baseline.so \
  --candidate /path/to/candidate.so --workers 1 --samples 8 \
  --output mods/CycleTrim/OniSim/conformance/artifacts/frame-benchmark
```

Run alone, without concurrent builds, conformance suites or game sessions.
Baseline/candidate order reverses between samples; the minimum is five samples.
The receipt binds libraries and harness files, preserves per-run logs and raw
snapshots, and reports process CPU medians/ranges plus wall time, major faults
and peak RSS for every sample. CPU timing includes worker execution and Python
message orchestration; wall time also includes readiness polling. Snapshot
conversion, initialization, shutdown and serialization are outside the timer.
New measurements additionally record Linux `VmHWM` and instantaneous RSS/PSS/swap
before converting the final snapshot. Use `VmHWM` for the current exec's RSS peak;
legacy `max_rss_kib` comes from `getrusage` and can retain a larger pre-exec parent
peak. Historical receipts lacking these fields cannot establish a memory saving
when that inherited floor obscures the child peak.
Pass `--cpu N` to pin each child and all of its threads to one allowed Linux
CPU; each sample records its effective affinity. Keep `--workers 1` for a
single-CPU run. Without this option the scheduler chooses cores. Frequency
control and host isolation are not configured by this tool.
For a parallel candidate, use e.g. `--cpus 16,17,18,19 --workers 4` after
checking that these CPUs are available on the test machine. This constrains the
child and its worker threads to a CPU set. `--cpu` and `--cpus` are mutually
exclusive; pinning four requested workers to one CPU can cause the native pool
to choose its serial fallback, so it does not validate parallel performance.
These are complete native frame fixtures, not realistic colony workloads or
whole-game FPS. Preserve failed and slower experiments when selecting changes.

## Zero heat-capacity and pressure boundaries

`pressure_cases.py` adds 36 authored fixtures: four isolated zero-capacity heat
exchanges, sixteen pressure/mass rounding boundaries, and sixteen deterministic
mixed-material scenes with large germ counts. The isolated cases exposed NaN
handling differences in the thermal equilibrium selection and final clamps.
The reference's MINSD/MAXSD and MINSS/MAXSS operand order must be preserved;
Rust's `clamp` and reversed unordered comparisons are not equivalent.

The [focused regression receipt](results/linux-744825-thermal-zero-capacity.json)
records two failures before the fix in otherwise identical frozen source trees.
The fixed candidate passes 224 cases and 8440 publications per run with one and
four workers requested, plus 28 native host tests. The small new fixtures do not
create cell workers; parallel-worker coverage comes from the existing larger
cases. This evidence binds the receipt's exact source/native hashes. It does not
approve independently changing sources, update the delivery package, establish
game performance, or replace Steam runtime acceptance.

The current native candidate uses Rust 1.99.1 / Edition 2024. The migration
receipt is [rust2024](results/linux-744825-rust2024.json); Load-plan memory
measurements are [separate](results/linux-744825-load-memory.json). All 25
exported names are preserved. Current native host tests number 28. Earlier
e4823a9f results remain in artifacts; a later thermal source change required
rebuilding and repeating every comparison on the final native identity above.
The final 215-case suite includes four zero-capacity thermal endpoint cases,
sixteen pressure-boundary cases and sixteen seeded mixed-pressure cases.

## Mixed-state soak coverage

Nine deterministic gas, liquid/headspace and solid-thermal scenes run 96 batches,
then four native save/reload cycles with continued steps. They combine asymmetric
materials, masses, temperatures and two disease species. Both one/four-worker
full suites pass 224 cases and 8440 publications. These small ABI scenes are not
long-running actual colonies. One earlier serial child timed out at the unchanged
20-second limit during concurrent suite runs; its failed receipt is retained.
An isolated recheck, 64 fresh children (maximum 0.215 seconds), and the isolated
full serial suite passed. The timeout root cause remains unproven; see
[the investigation](results/linux-744825-native-timeout-investigation.json).

Use `--baseline-role reference` to time the pinned official library as baseline;
the default baseline role is a previously built project candidate. The reference
role retains the official fingerprint check.
