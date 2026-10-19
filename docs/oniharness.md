# OniHarness: independent headless testing project

[OniHarness](https://github.com/LIghtJUNction/OniHarness) is an independently
usable Git project, linked here as the `tools/OniHarness` submodule. The gitlink
pins a reviewed revision; it does not copy the harness into OniMods' source tree.

```sh
git submodule update --init tools/OniHarness
python3 tools/OniHarness/scripts/verify.py
```

The command above builds and runs the harness's own game-free checks. It does
not add a game dependency to OniMods' existing source contracts or host tests.

For explicitly requested local tests with your installed game:

```sh
python3 tools/OniHarness/scripts/verify.py --game-path /path/to/OxygenNotIncluded
```

For fixture tests, OniHarness starts its own test workers and calls the installed native simulation
library directly. It does not launch ONI, Steam or Unity Player. The initial
preview has a verified Linux x64 adapter for build 744825 and exact file hashes.
Other native versions are unsupported until verified.

The examples cover gas mass, heat transfer, liquid movement, custom material
parameters, energy input and cell replacement. C# tests can exercise portable
mod helpers and selected installed game methods in a .NET host. In those workers, full colony
loading, duplicants/AI, building lifecycle and Harmony patch execution are not
supported. These tests do not replace in-game acceptance gates for OniMods.

See the subproject's [capability table](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/capabilities.md)
and [local verification receipt](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/verification/0.1.0.json).
Game libraries, assets, saves and decompiled implementation are not distributed.
The new framework uses AUL 1.0-draft.1, with its own license and upstream identity.

## Agent skill and observed physics experiment

Use the project-local [oni-harness skill](../.agents/skills/oni-harness/SKILL.md).
It routes to the maintained skill in the submodule. Native fixture commands
remain game-free; starting the real game is a separate explicit workflow.

```sh
cd tools/OniHarness
python3 scripts/physics_playground.py --game-path /path/to/OxygenNotIncluded \
  --repeat 2 --output artifacts/physics-playground
```

The experiment waits for water to reach the basin, removes three partition
cells, observes flow into a second chamber, then adds heat to a measured water
cell. It records all sampled state and checks; reaching an observation limit
fails and prevents later actions. Open the output `index.html` to replay the
data; `--gif` also renders images when Pillow is already installed.

The skill and example are source-checkout additions after the v0.1.0 release.
No full colony gameplay capability is implied by this physics experiment.

## Real game, reduced rendering and optional text spectator

For actual colony/mod behavior, use the experimental [runtime workflow](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/runtime.md).
It starts ONI through Steam, keeps native simulation and offers an optional
rendering companion with a configurable update cap and game-owned stop deadline.
Normal mode restores original rendering settings. Compatibility and performance
must be checked per installed game, save and mod combination.

```sh
cd tools/OniHarness
python3 scripts/runtime.py launch --graphics reduced --interval 60 --update-rate 30
# After a disposable save copy is loaded and paused:
python3 scripts/runtime.py observe --view text
python3 scripts/runtime.py serve
```

Install the companion and enable OniMcp as described in the runtime document
before launching. The text spectator shows the same paused virtual-file map
returned to the agent, with observation time, supplied intent and action result.
It does not control the game or poll its state. `--view none` records without
printing a terminal map. No-graphics startup is explicitly experimental; a
successful process launch does not establish a working colony.

Runtime files are source-checkout additions after v0.1.0. Existing CLI/Core
release assets are unchanged. Keep actual game evidence distinct from native
fixtures and host/source checks; no full-feature compatibility is promised.
