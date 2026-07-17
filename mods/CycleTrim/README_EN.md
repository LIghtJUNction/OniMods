# CycleTrim

A lightweight Oxygen Not Included performance mod. CycleTrim optimizes selected high-frequency simulation paths to lower overhead while keeping gameplay behavior aligned with vanilla.

## Index (summary page)

- [What it does](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#what-it-does)
- [Usage](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#usage)
- [Compatibility](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#compatibility)
- [Benchmark summary](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#benchmark-summary)
- [Detailed docs and logs](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#detailed-docs-and-notes)

## What it does

- Reduces redundant smart reservoir/gas reservoir signal traffic.
- Reuses fetch candidate lookup data for cheaper selection.
- Throttles expensive refresh work for busy duplicants only; idle duplicants remain responsive.
- Throttles stationary-critter navigation probing.

## Usage

- Subscribe and enable as normal.
- No extra configuration is required in most cases.

## Compatibility

- Supports base game and DLC content.
- Behavior-safe by default for long-running colonies.

## Benchmark summary

| Scenario | Metric | Before -> After | Improvement |
|---|---|---|---|
| Busy duplicant throttle | FPS median (CPU hot-path scene) | `70.6 → 85.1` | `+20.6%` |
| Busy duplicant throttle | Brain total time | `5.087s -> 2.449s` | `-51.9%` |
| Stationary critter navigation throttle | FPS median | `81.23 -> 109.28` | `+34.5%` |
| Stationary critter navigation throttle | Navigator hot-path total | `826.852ms -> 47.970ms` | `-94.2%` |
| UpdatePickups cache | Hit rate (median) | `24.37%` | `24.37% of lookups hit cache` |
| UpdatePickups cache | Average cost | `41.279us -> 35.708us` | `-13.5%` |
| UpdatePickups cache | Estimated hits per second (30s window) | `~6.29 /s` | `cache-hit reuse` |

### Synthetic function-level benchmark for Brain scheduling (integrated, ordinary Creatures only)

The Brain rate cap is integrated into the production Harmony patch, but it applies only to the exact vanilla `CreatureBrainGroup`. Duplicants and custom Brain groups keep vanilla scheduling. Priority Brains keep the vanilla queue-selection and immediate-execution path and are not charged against the ordinary Creature allowance.

> **This is a standalone synthetic function benchmark, not an in-game FPS test, and it cannot be converted directly into whole-colony performance.**

- Environment: standalone `.NET 10` Release benchmark; ONI is not launched.
- Scenario: simulated `240` render FPS for `30` seconds, with `512` deterministic CPU-work iterations per normal Brain call.
- Method: `2` warmups and `7` paired, alternating baseline/candidate samples; medians are reported, and every sample verifies stable call counts and checksums.
- Call accounting: total calls fell from `43,200` to `19,200` (`-55.56%`). Ordinary Creature calls fell from `36,000` to `12,000` (`-66.67%`), while all `7,200` Dupe calls remained vanilla.
- Representative local result: elapsed time fell from `255.580ms` to `113.289ms` (`-55.67%`, `2.26x`).
- Scope: excludes priority workload, real Sensors/PathProber cost, and in-game Harmony scheduling overhead; it does not represent actual FPS.
- Safety difference: the current game DLL uses a dynamic `i != brains.Count` boundary. CycleTrim also re-reads Count so Brains added during an update can join the scan, but terminates when the list shrinks behind the scanned position to avoid the vanilla condition's potential failure to converge.

### AsyncPathProber deduplication and backpressure

- Supports only the exact vanilla `CreaturePathFinderAbilities`; Minion, Robot, and custom abilities fall back to vanilla because their dynamic permission/equipment semantics cannot be proven complete.
- A cache entry exists only after `Navigator.TakeResult` returns successfully. Cleared queues, unregisters, unapplied/stale results, and exceptions never complete an entry. Eight consecutive skips force a refresh.
- Startup verifies that Creature sentinel recycling is still an empty override; if a game update changes that contract, the entire Async optimization disables itself instead of running unsafely.
- Queue quota follows worker and in-flight counts within `1..4`; one worker uses quota 2 while idle and 1 while busy.
- 10,000-request synthetic matrix (**not in-game FPS**): requested hit rates `0/25/50/75/90%` produced `10000/7701/5501/3301/2000` PathProbe executions and avoided `0/2299/4499/6699/8000` clones, a theoretical probe-work reduction of `0/22.99/44.99/66.99/80.00%`. Bounded fallback makes actual reduction lower than nominal hits.

Reproduction commands:

```bash
dotnet run --no-restore -c Release --project benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj
dotnet run --no-build --no-restore -c Release --project benchmarks/CycleTrim.BrainBenchmarks/CycleTrim.BrainBenchmarks.csproj -- --benchmark
```

You can use the `benchmark` skill to let an agent run the full benchmark automatically and return normalized JSON results.

Full scenario notes are in [dist/CycleTrim/README.md](../../dist/CycleTrim/README.md) for reproducible detail.

## Source layout

```text
mods/CycleTrim/
├── ModInfo.cs          # KMod entry
├── Core/               # non-patch core logic (e.g. BrainRateCap)
├── Patches/            # Harmony patches
├── docs/               # Steam descriptions
└── CycleTrim.csproj
```

Same monorepo convention as the other mods: keep only the entrypoint and project/metadata files at the mod root; patches live under `Patches/`, shared logic under `Core/`.

## Detailed docs and notes

- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Runtime verification script: [verify_cycletrim_target_contract.py](../../scripts/verify_cycletrim_target_contract.py)
