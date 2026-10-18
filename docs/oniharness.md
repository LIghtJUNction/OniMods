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

OniHarness starts its own test workers and calls the installed native simulation
library directly. It does not launch ONI, Steam or Unity Player. The initial
preview has a verified Linux x64 adapter for build 744825 and exact file hashes.
Other native versions are unsupported until verified.

The examples cover gas mass, heat transfer, liquid movement, custom material
parameters, energy input and cell replacement. C# tests can exercise portable
mod helpers and selected installed game methods in a .NET host. Full colony
loading, duplicants/AI, building lifecycle and Harmony patch execution are not
supported. These tests do not replace in-game acceptance gates for OniMods.

See the subproject's [capability table](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/capabilities.md)
and [local verification receipt](https://github.com/LIghtJUNction/OniHarness/blob/main/docs/verification/0.1.0.json).
Game libraries, assets, saves and decompiled implementation are not distributed.
The new framework uses AUL 1.0-draft.1, with its own license and upstream identity.
