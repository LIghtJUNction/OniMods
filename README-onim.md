# onim

`onim` is a command-line toolkit for creating and developing Oxygen Not Included mods. It creates the project layout, builds the C# mod, installs development builds, and publishes to Steam Workshop directly via the headless Legacy ZIP publisher.

The whole path is three commands: `onim dev` tries a mod in game, `onim build` packs it, and `onim publish` sends it to Steam. Source: [OniMods](https://github.com/LIghtJUNction/OniMods).

## Install

```sh
cargo install onim --locked
```

Building a mod also requires the .NET SDK and an installed copy of Oxygen Not Included.

## Create a mod project from an empty directory

```sh
mkdir MyFirstMod
cd MyFirstMod
onim init MyFirstMod --author YourName --desc "My first ONI mod"
```

This creates the `onim.toml` project config, local MSBuild path files, a README and Git ignore rules, plus a starter mod under `mods/MyFirstMod/`. The mod name can be omitted to create the standard `OniModTemplate` project.

Before the first build, set `OniGamePath` in `Directory.Build.props` to your local game installation. Then run:

```sh
onim doctor
onim build
```

For an in-game development install, run `onim dev`. It builds the configured mod and enables its Dev copy in ONI. You can also create additional mods in this project with `onim init AnotherMod`.

Each build must produce a new `dist/<AssemblyName>.zip`. A successful compiler
exit without a new ZIP fails the command and restores the previous package.
Installation extracts and checks the DLL and `mod_info.yaml` before replacing
the installed copy. Extraction or Dev enablement failures preserve the previous
installation; an interrupted transaction is retained at the reported recovery
path. Identity-confirmed duplicates across Local, Dev and Workshop are moved to
`mods/.onim-recovery`; `mods.json` is backed up before deduplication.

## Commands

- `onim init [NAME]` creates a project in an empty directory or adds a mod to an existing `onim` project.
- `onim setup` detects the game and required tools for an existing OniMods workspace.
- `onim doctor` checks configured paths, tools and mod sources.
- `onim build` and `onim dev` build and install a development copy.
- `onim install` builds the configured project and installs it to `Local`.
  `onim install --release OWNER/REPO` installs a mod from the latest GitHub
  Release; `onim install --repo OWNER/REPO [--ref REF]` scans C# projects,
  builds their mods, and installs them. Use `-m NAME` to select one mod from a
  repository containing several.
- `onim workshop search QUERY`, `onim workshop install ID`, and
  `onim workshop uninstall ID` search, install, and remove Steam Workshop mods.
  Workshop install accepts ONI-compatible single-file Legacy ZIP items.
- `onim workshop page show ID` reads an owned Workshop page.
  `onim workshop page set ID` changes the English and Simplified Chinese title
  and description, replaces the full tag set, and sets visibility or the preview
  image. It does not upload a new ZIP.
- Install and uninstall operations enforce one active version per `staticID`
  across `Local`, `Dev`, and Workshop. Verified duplicate folders are moved to
  `mods/.onim-recovery`; `mods.json` is backed up before it is changed.
- `onim info`, `onim list` and `onim uninstall` inspect and manage configured mods.
- `onim publish` uploads directly to Steam Workshop via the headless Legacy ZIP publisher (or `--gui` for OniUploader); it never uses incompatible SteamCMD directory uploads.
  Useful flags: `--preflight` (read-only account/item check), `--dry-run`
  (build and write an immutable plan without uploading), `--create` (first
  upload, created Private and verified before `--visibility` applies),
  `--resume PLAN` (continue a journaled plan without repeating uncertain
  writes), `--update-preview`, `--expected-owner STEAMID` and `--auto-note`.

See the [OniMods repository](https://github.com/LIghtJUNction/OniMods) for the full guide, troubleshooting and source.

Native Legacy ZIP publishing through the game's Steam API ships in the released
CLI (current version `0.1.12`, see [CHANGELOG-onim.md](CHANGELOG-onim.md)).
Remaining acceptance items for the native path are tracked in
[native publishing progress](docs/onim-native-publishing.md).
