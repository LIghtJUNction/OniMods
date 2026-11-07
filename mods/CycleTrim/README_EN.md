# CycleTrim

A disruptive, experimental performance mod, still in early development. The direction is to rebuild the underlying algorithms and move as much computation as possible onto the GPU. Speed comes first, compatibility later. The hot-path cuts listed below are what the current build already does. They are not the final shape, and they are not a promise of vanilla behavior.

This mod comes from [OniMods](https://github.com/LIghtJUNction/OniMods). onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing: `onim dev` tries it in game, `onim build` packs it, and `onim publish` sends it to Steam.

## Index (summary page)

- [What it does](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#what-it-does)
- [Usage](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#usage)
- [Compatibility](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#compatibility)
- [Benchmark summary](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#benchmark-summary)
- [Detailed docs and logs](https://github.com/LIghtJUNction/OniMods/blob/main/mods/CycleTrim/README_EN.md#detailed-docs-and-notes)

## What it does

- Reuses fetch candidate lookup data for cheaper selection.
- Throttles expensive refresh work for busy duplicants only; idle duplicants remain responsive.
- Throttles stationary-critter navigation probing.
- Reuses immutable component type metadata during saves.
- Reuses repeated circuit demand queries within each power overlay update.
- Optionally samples gas/liquid conduit display mesh rebuilding while retaining simulation calls, every-frame drawing and audio.

## Usage

- Subscribe and enable as normal.
- No extra configuration is required in most cases.

**Save type metadata** is enabled by default and reuses adjacent type queries
within each save invocation. **Power overlay queries** is opt-in
because updates with few repeated circuits can cost more; both settings require
restarting ONI. Power query reuse falls back for unknown electricity interface implementations or
conflicting patches. The [implementation and validation notes](../../docs/cycletrim-moono-optimizations.md)
separate host tests and synthetic measurements from pending ONI runtime acceptance.
These are independent implementations informed by oni-moono's mechanisms, not its
engine/runtime replacement or its reported whole-game speedup.

**State machine lookups** is opt-in and requires restarting ONI. It reuses query
results while the state-machine list is unchanged. List changes invalidate the
answers; short lists and worker-thread queries keep the original path.

**Conduit animation refresh** defaults to Full. Half / Quarter rebuild the display
every 2 / 4 rendered frames when inputs are stable and continue drawing the completed
meshes between rebuilds. Simulation, camera, world and style changes refresh immediately;
a 100 ms age limit is checked on the next render call. Content overlays and audio
requests retain full updates. Restart is required; FastTrack or incompatible rendering
patches disable this group. Small workloads can cost more because of cache validation.
See the [implementation, complete measurements and acceptance scope](../../docs/cycletrim-visual-refresh.md).

## Compatibility

- Supports base game and DLC content.
- Behavior-safe by default for long-running colonies.
- CycleTrim no longer intercepts `SmartReservoir` automation signaling; vanilla repeated notifications are preserved for stock reservoirs and third-party buildings that reuse the component.

### OniSim native simulation core

OniSim is CycleTrim's experimental native simulation core, with source under [`OniSim/`](OniSim/). Windows, macOS and Linux packages include the native library, but runtime mapping is disabled by default and the official SimDLL remains active. Explicitly enable the new PLib option `EnableOniSimExperimental` and restart ONI to attempt mapping. The old `EnableOniSim` config key is intentionally not reused, so a previously persisted default-true value cannot silently re-enable takeover after upgrade. Mapping cannot be hot-reverted. `CYCLETRIM_ONISIM_ENABLE=0`, `false`, or `off` remains available as a startup override to disable it.

Use only with a disposable test save. All 62 official message IDs are now accounted for in source routing, but that is not proof of full semantic equivalence: gas-interaction rules are parsed and stored but are not called by the fixed-step pipeline. Limited save/load receipts apply only to their exact historical binaries, not automatically to the current source. DLC, other-Mod, graphical, long-duration, large-colony, and whole-game performance acceptance remain open; OniSim is not a complete replacement yet. The loader accepts only the statically inspected ONI `Assembly-CSharp` and Mono fingerprints; other game versions are refused with a log message.

Linux x86_64 builds include OniSim by default; use `IncludeOniSim=false` to create a package without it. Runtime mapping stays disabled unless `EnableOniSimExperimental` is explicitly enabled in CycleTrim's Mod settings and ONI is restarted. `CYCLETRIM_ONISIM_ENABLE=0` can force-disable it before startup; `1` permits loading only when the PLib option is enabled. The standalone OniSim.Mod entry requires the environment variable as its explicit opt-in. `CYCLETRIM_ONISIM_NATIVE_PATH` may override the bundled library with an absolute `.so` path; when unset, the loader uses `OniSim/liboni_sim_core.so` beside the Mod assembly.

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

The standalone Brain benchmark project has been retired. The results above are historical measurements; they are not results from the current source. Run the current host checks with `python3 scripts/check_mods.py --skip-synthetic-performance`.

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
