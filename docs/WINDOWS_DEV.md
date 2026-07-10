# Windows Dev notes (`onim dev`)

## What broke on a real Windows install

1. **Mod install path**: `onim` wrote under `AppData\LocalLow\Klei\Oxygen Not Included\mods\Dev`, but the game loaded mods from `Documents\Klei\OxygenNotIncluded\mods` (see Player.log `Loaded assembly ...\Documents\Klei\OxygenNotIncluded\mods\...`).
2. **Expand-Archive spaces**: destination paths containing `Oxygen Not Included` were split into multiple PowerShell arguments when unquoted.
3. **Source tarball**: `tar -czf 'D:\...'` fails with Windows `tar`; the mod zip itself was fine.

## Fixes in this tree

- Resolve the real Windows Documents Known Folder through `[Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)`, then install under `Klei\OxygenNotIncluded`; there is no `USERPROFILE\Documents` or LocalLow fallback.
- Use the shared archive helper for `dev` and `install`. On Windows it invokes PowerShell with `-NoProfile -NonInteractive` and safely single-quotes `Expand-Archive -LiteralPath` / `-DestinationPath`; other platforms pass paths directly to `unzip` without a shell.
- Use double-quoted tar paths and fail the build if the source tarball cannot be created.
- Optional helper: `scripts/install_onim.bat` uses `vswhere` to find the latest Visual Studio instance containing `Microsoft.VisualStudio.Component.VC.Tools.x86.x64`, runs `vcvars64`, then installs with Cargo. Set `ONIM_VCVARS64` to override the discovered `vcvars64.bat` path.

## Verify

```bat
onim dev -m oni_mcp
:: restart Oxygen Not Included
:: enable Dev mod; disable Steam Workshop copy if same staticID
```
