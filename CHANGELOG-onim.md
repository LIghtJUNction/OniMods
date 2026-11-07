# onim Changelog

## 0.1.12 - 2026-11-07

- Add native `--update-preview` support to `onim publish`, enabling seamless preview updates during Legacy ZIP publication without external scripts.
- Make `onim workshop` commands lenient to local game paths, enabling standalone Workshop inspection and metadata updates.
- Prevent Steam Cloud preview filename collisions with timestamped cloud identifiers.
- Include missing `WorkshopDependencyRunner.cs` and related dependency sources in the embedded publisher manifest.

## 0.1.11 - 2026-11-07

- Enforce prerequisite load order in `mods.json` so shared libraries like `LIghtJUNction.Jlib` load before consumer mods (`LIghtJUNction.CycleTrim`, `LIghtJUNction.OniMcp`).

## 0.1.10 - 2026-11-07

- Enforce one active installation per Mod `staticID` across Local, Dev, and Steam Workshop.
- Move verified duplicate directories into `mods/.onim-recovery` and back up `mods.json` before cleanup.
- Preserve another installed source when uninstalling only Local or Dev copies.
- Read generated Mod identity from the project file when the source `mod.yaml` is absent.
