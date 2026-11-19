# onim Changelog

## Unreleased

- Read and extract mod ZIPs in process. `onim setup` and `onim doctor` no longer require an external `unzip` program.
- Check ZIP headers and entry paths consistently. Reject traversal, duplicate or ambiguous paths, symbolic links and special files; prevent existing files or filesystem directory aliases from being overwritten or silently merged.
- Install source build directories directly through the existing staging and rollback transaction, avoiding a temporary ZIP compression and extraction round trip. Preserve Unicode filenames, empty directories and file permissions.
- Reuse Project Z's published `zhex 0.1.3` for SHA-256 hexadecimal encoding with a fixed output buffer. Hash values and immutable Workshop publication plans keep their existing format.
- Add 13 archive regressions and use real ZIPs in the CLI installation fixtures, covering failed extraction, activation rollback and preservation of the previous installation.

## 0.1.13 - 2026-11-08

- Support animated GIF previews for Legacy Workshop page updates.

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
