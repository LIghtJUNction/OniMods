# onim Changelog

## 0.1.10 - 2026-11-07

- Enforce one active installation per Mod `staticID` across Local, Dev, and Steam Workshop.
- Move verified duplicate directories into `mods/.onim-recovery` and back up `mods.json` before cleanup.
- Preserve another installed source when uninstalling only Local or Dev copies.
- Read generated Mod identity from the project file when the source `mod.yaml` is absent.
