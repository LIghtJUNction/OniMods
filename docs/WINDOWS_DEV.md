# Windows Dev notes (`onim dev`)

## What broke on a real Windows install

1. **Mod install path**: `onim` wrote under `AppData\LocalLow\Klei\Oxygen Not Included\mods\Dev`, but the game loaded mods from `Documents\Klei\OxygenNotIncluded\mods` (see Player.log `Loaded assembly ...\Documents\Klei\OxygenNotIncluded\mods\...`).
2. **Expand-Archive spaces**: destination paths containing `Oxygen Not Included` were split into multiple PowerShell arguments when unquoted.
3. **Source tarball**: `tar -czf 'D:\...'` fails with Windows `tar`; the mod zip itself was fine.

## Fixes in this tree

- Prefer `Documents\Klei\OxygenNotIncluded` when its `mods` folder exists; fall back to LocalLow.
- Quote `Expand-Archive -LiteralPath` / `-DestinationPath` in `dev` and `install`.
- Use double-quoted tar paths and do not fail the mod package if the optional source tarball errors.
- Optional helper: `scripts/install_onim.bat` runs `vcvars64` then `cargo install --path .`.

## Verify

```bat
onim dev -m oni_mcp
:: restart Oxygen Not Included
:: enable Dev mod; disable Steam Workshop copy if same staticID
```
