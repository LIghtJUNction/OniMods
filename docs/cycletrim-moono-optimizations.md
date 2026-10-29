# CycleTrim: eliminating repeated metadata and overlay work

Date: 2026-10-29. Implementation baseline:
`d6097e20cf95bd0e34f802c1674a6abc1ba363be`.

The change reuses answers only while their inputs cannot have changed. It adds
two independent implementations to the existing CycleTrim mod, with executable
host tests and measurements. No simulation update frequency is reduced.

## Research and provenance

The mechanism reference is
[`tigin-backwards/oni-moono@2d4aca2aeebe2351752d24737230339e0abfbb5e`](https://github.com/tigin-backwards/oni-moono/tree/2d4aca2aeebe2351752d24737230339e0abfbb5e),
particularly [save metadata](https://github.com/tigin-backwards/oni-moono/blob/2d4aca2aeebe2351752d24737230339e0abfbb5e/src/OniMoono.GameTweaks/FastSaveTypeChecks.cs)
and [overlay queries](https://github.com/tigin-backwards/oni-moono/blob/2d4aca2aeebe2351752d24737230339e0abfbb5e/src/OniMoono.GameTweaks/FastOverlays.cs).
Its README reserves rights; no Moono source is copied or bundled. The implementations
here were written independently from the behavior requirements and API contracts.

Moono replaces the Unity layer and runs the game's managed code on CoreCLR. Its
[reported whole-game performance](https://github.com/tigin-backwards/oni-moono/blob/2d4aca2aeebe2351752d24737230339e0abfbb5e/README.md#performance)
does not establish a CycleTrim speedup. CycleTrim remains an in-game mod; this
change does not replace Unity, Mono, the renderer or the native simulation core.

Method-body review uses the repository's existing source pin,
`Kupie/ONI_Decomp@8d4ace10c2b6a84cee394653a91b990a5122e636`, declaring build 744825:

- [SaveLoadRoot](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/SaveLoadRoot.cs)
- [OverlayModes](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/OverlayModes.cs)
- [CircuitManager](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/CircuitManager.cs)
- [EnergyConsumer](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/EnergyConsumer.cs),
  [Generator](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/Generator.cs),
  [OverlayScreen](https://github.com/Kupie/ONI_Decomp/blob/8d4ace10c2b6a84cee394653a91b990a5122e636/Assembly-CSharp/OverlayScreen.cs).

Downloaded decompiled files and reference game assemblies remain transient and
are not part of this change.

## Implemented changes

### Save metadata — enabled by default

`SaveWithoutTransform` asks twice whether each component type declares
`SkipSaveFileSerialization`, and writes the component type's `ToString()` name.
Many instances share a type. `SaveTypeMetadataCache` remembers these immutable
answers with a `ConditionalWeakTable<Type, ...>`.

- Only actual CLR runtime types and the exact skip-save attribute with
  `inherit=false` use the cached attribute answer. Other attributes, inherited
  queries, custom reflection wrappers and mutable type builders use the original
  reflection operation, including its exceptions.
- Cache keys are type identities, so equal names from different assemblies do not
  alias. Values contain only a boolean and a string; neither component objects
  nor collectible types are kept alive by the values.
- Serialization calls, component order, counts, payloads, stream positioning and
  save format remain in the original method. The name remains `ToString()`, not
  a substitute such as `FullName`.
- The transpiler requires both attribute calls and the type-name call. A missing,
  extra or prefixed call leaves the entire instruction stream unchanged. Labels
  and exception boundaries are preserved when calls are replaced.

`OptimizeSaveMetadata` can disable this group before patch installation. Changing
the setting requires restarting ONI. Thread-safe metadata access does not make
the game's complete serialization procedure safe to run concurrently.

### Power overlay queries — opt-in

One power overlay update can ask the same circuit's demand for many buildings.
The first request still runs the original graph traversal; subsequent requests
for that manager and circuit can reuse its exact float result until the update
finishes. Regular gameplay and other screens continue to call the original method.

Further safeguards beyond the reference mechanism:

- Key by manager **reference identity** and circuit ID; use a separate scope on
  each thread and on every method invocation, even within the same rendered frame.
- Nested updates invalidate their parent before and after running. Exceptional
  exits clear the scope through a Harmony finalizer. Failed queries are not cached.
- Keep at most 4096 entries and one empty reusable container per thread. Cleared
  containers do not retain managers or previous game objects.
- Validate compatibility lazily, immediately before the first actual reuse.
  Updates with all-distinct circuits do not need reflection scans or Harmony
  inventory checks. A failed check clears and disables reuse for that update.
- Unknown concrete `IEnergyConsumer` or `IBridgedNetworkItem` implementations,
  unsafe-to-enumerate assemblies and dynamic assemblies retain original queries.
  Only the CLR's own anonymous DynamicMethod host is exempted by assembly identity.
- Scan the relevant assembly reference graph, including indirect dependencies;
  avoid enumerating unrelated framework types. New assembly loads invalidate
  existing scopes immediately. Check known interface callbacks and transformer
  getter dependencies for Harmony modifications before reuse.

`OptimizePowerOverlay` defaults to **false**. Query reuse has workload-dependent
cost: a view with distinct circuits receives no avoided traversals. This option
allows representative in-game A/B measurements before enabling it for a colony.

## Verification and measurements

The [machine-readable receipt and raw logs](cycletrim-moono-validation/receipt.json)
record the tested source hashes, commands, environment and complete paired samples.
The executable test project links production helpers and patches and installs
**real Harmony 2.4.2** on original synthetic fixtures. It does not execute game DLLs.

| Check | Observed result |
| --- | --- |
| Production save patch and helper | 375 assertions passed; 257 synthetic output byte comparisons |
| Production power patch and scope | 109 assertions passed using real Harmony |
| Existing options, initialization and probe regressions | Passed; 58 settings assertions |
| Full managed net48 reference build | Passed, 0 warnings and 0 errors |

**Synthetic measurements only.** Milliseconds below are batch medians; speedups
are medians of paired baseline/candidate ratios, which need not equal the ratio
of the two independently calculated timing medians.

| Workload | Baseline median | Candidate median | Paired speedup |
| --- | ---: | ---: | ---: |
| Save metadata, 200,000 iterations | 33.452 ms | 6.080 ms | 5.465× |
| Power, 32 circuits / 512 requests, 100 updates | 22.329 ms | 11.150 ms | 2.131× |
| Power, 512 distinct circuits, 100 updates | 22.623 ms | 28.830 ms | 0.765× |

All save samples allocated 0 bytes in both variants after warmup. In the repeated
power case, original graph-query calls fell from 51,200 to 3,200 (93.75% fewer),
and allocations per batch fell from 7,782,400 to 1,778,400 bytes. In the distinct
case, both made 51,200 calls and allocated 7,782,400 bytes: the remaining lookup
and scope overhead still made the candidate slower. **Power reuse stays opt-in.**

The first power candidate checked compatibility eagerly on every update. Its
distinct-circuit paired speedup was 0.682× with approximately 1.29 MB extra
allocations per 100 updates. The retained lazy design removes those extra
allocations and improves that control to 0.765×, but does not erase the regression.
Both generations' raw samples are retained; the first result is not hidden.


Save tests include 257 synthetic serialized-output comparisons, including a
runtime witness that the patched call actually fills the production cache;
attribute and inheritance differences; same-name types from different assemblies;
null and custom-reflection errors; weak lifetime; concurrent access; and zero
allocation on the fully warmed metadata path. The complete measured loop is
prewarmed to exclude .NET tiered PGO/OSR initialization from the allocation window.

Power tests include actual call reduction, option disable, signed zero and NaN
payloads, manager identity, separate updates and threads, nested/reentrant calls,
exceptions, null receivers, late Harmony patches, callback patches, capacity,
object release, and assembly loading during an update. The dynamic-assembly
fixture establishes conservative dynamic fallback; it is not an acceptance test
of every third-party mod combination.

The performance samples use one pinned, non-exclusive virtual CPU on an AMD EPYC
9V74 host, Linux x64, .NET SDK 10.0.401/runtime 10.0.12. They measure warm execution:
initial patch installation, initial metadata discovery and stated warmup are
outside timing. Timings are informational; noisy shared-runner timing is not a
correctness gate. All measured pairs require equal result checksums.

The save workload performs two attribute checks and a possible name read over
five types, 200,000 iterations, three warmups and nine alternating pairs. It
measures metadata lookup, **not complete save serialization**.

The power workload performs 100 updates with 512 targets per update, twenty warmup
updates and seven alternating pairs. It includes the real patch's scope and
compatibility overhead. The original fixture's query performs 128 deterministic
integer iterations and allocates a temporary 128-byte array; this is a declared
synthetic cost, not the actual ONI circuit algorithm. The high-reuse case has 32
circuits; the no-reuse control has 512 distinct circuits.

### Reproduce

```bash
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true
dotnet run --project tests/CycleTrim.PerformanceProbe.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -- --skip-synthetic-performance

# Use separate processes: a regression deliberately loads an unsupported type.
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -- --benchmark-save
dotnet run --project tests/CycleTrim.Optimizations.Tests -c Release \
  -p:ImportDirectoryBuildProps=false -- --benchmark-power

dotnet build mods/CycleTrim/CycleTrim.csproj -c Release \
  -p:OniManagedPath=/path/to/verified/reference-assemblies \
  -p:ImportDirectoryBuildProps=false -p:TreatWarningsAsErrors=true \
  -p:IncludeOniSim=false
```

The normal `scripts/check_mods.py` runner discovers the new test project, so the
existing Mod quality workflow runs its behavior regressions without workflow
changes. `IncludeOniSim=false` above only scopes this reference build to the
managed change; production native-core defaults are unchanged.

## Runtime evidence still required

The reference assemblies in `ci/oni-reference-assemblies.json` declare build
**737790**, whereas the separate method-body source declares **744825**. A successful
reference build establishes the pinned API surface, not a 744825 game execution.

No Steam game, real save, Unity/Mono run or FPS measurement was available here.
Before runtime acceptance, use a disposable copy of the same save and record the
game build, candidate commit, other mods and settings:

1. Run save/load/continue with metadata reuse off and on; compare component and
   payload behavior and confirm no serialization errors.
2. Compare the same power viewport with reuse off and on, then switch worlds,
   pan, construct/remove wires and change transformer connections. Verify displayed
   demand and refresh behavior, including repeated refreshes within one frame.
3. Check cold first-open cost and whether the optimization remains eligible under
   the actual Mono runtime and installed mod set; conservative fallback is allowed.
4. Collect alternating save-time/overlay-time and frame-time samples with identical
   diagnostics settings. Do not multiply the synthetic ratios into a game FPS claim.

Non-Harmony detours, direct reflective field changes, and code installing a patch
inside an already-running update without loading an assembly are outside the
complete guarantee. Ordinary late patch installation is detected before reuse in
the next update. Preserve the repository's runtime acceptance requirements in
`docs/autonomous-iteration.md`; this candidate is not a Workshop release.

## Further optimization candidates

| Candidate | Further improvement | Required comparison before adoption |
| --- | --- | --- |
| Conveyor path containment | Build an index only for a synchronous network rebuild; detect same-length in-place edits rather than trusting list reference/count | Original path identities, rail order, removals, loops, rebuilds and save/load |
| Scene-partition cleanup | Deduplicate removed handles with a set, then remove in descending original position | Handle reuse, node moves and exact swap-remove results |
| Pickup work offsets | Double-buffer dirty objects so a change during recomputation is retained for the next pass | All movement/terrain/tag invalidations, third-party behavior and worker safety |
| Animation symbol updates | Avoid writes only when all relevant float **bits** and other fields match | Signed zero, NaN payloads, tiny changes, index errors and version/upload behavior |

These remain proposals. They are not represented as implemented or measured by
the two optimizations in this candidate.
