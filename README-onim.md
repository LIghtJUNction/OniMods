# onim

`onim` is a command-line toolkit for creating and developing Oxygen Not Included mods. It creates the project layout, builds the C# mod, installs development builds, and prepares Workshop publishing through the OniUploader workflow.

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

## Commands

- `onim init [NAME]` creates a project in an empty directory or adds a mod to an existing `onim` project.
- `onim setup` detects the game and required tools for an existing OniMods workspace.
- `onim doctor` checks configured paths, tools and mod sources.
- `onim build` and `onim dev` build and install a development copy.
- `onim install` installs a Release build to the game's Local mods directory. It
  refuses to proceed while the same mod still exists in `Dev` so ONI cannot load
  two copies with the same static ID; remove or move the Dev copy first.
- `onim info`, `onim list` and `onim uninstall` inspect and manage configured mods.
- `onim publish` prepares the VDF for OniUploader; it does not use SteamCMD directory uploads.

See the [OniMods repository](https://github.com/LIghtJUNction/OniMods) for the full guide, troubleshooting and source.
