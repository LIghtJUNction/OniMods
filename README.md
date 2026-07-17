# OniMods

[简体中文](README_ZH.md)

<p align="center">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/github/license/LIghtJUNction/OniMods?style=for-the-badge&logo=opensourceinitiative&logoColor=white" /></a>
  <a href="https://github.com/LIghtJUNction/OniMods/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/LIghtJUNction/OniMods?style=for-the-badge&logo=github" /></a>
  <a href="https://github.com/LIghtJUNction/OniMods/forks"><img alt="Forks" src="https://img.shields.io/github/forks/LIghtJUNction/OniMods?style=for-the-badge" /></a>
  <a href="https://github.com/LIghtJUNction/OniMods/issues"><img alt="Issues" src="https://img.shields.io/github/issues/LIghtJUNction/OniMods?style=for-the-badge&logo=github" /></a>
  <a href="https://github.com/LIghtJUNction/OniMods/commits/main"><img alt="Last Commit" src="https://img.shields.io/github/last-commit/LIghtJUNction/OniMods?style=for-the-badge" /></a>
  <a href="https://github.com/LIghtJUNction/OniMods/blob/main/LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue?style=for-the-badge" /></a>
  <a href="mods/OniMcp/README_EN.md"><img alt="ONI MCP docs" src="https://img.shields.io/badge/ONI%20MCP-Server%20Docs-4f46e5?style=for-the-badge" /></a>
</p>

<p align="center">
  <a href="mods/OniModTemplate/README.md"><img alt="Template" src="https://img.shields.io/badge/OniModTemplate-Template-0ea5e9?style=for-the-badge" /></a>
  <a href="Cargo.toml"><img alt="Rust" src="https://img.shields.io/badge/Rust%20CLI-onim-f46623?style=for-the-badge&logo=rust&logoColor=white" /></a>
  <a href="https://github.com/dotnet/sdk"><img alt=".NET SDK" src="https://img.shields.io/badge/.NET-8.0-5c2d91?style=for-the-badge&logo=dotnet&logoColor=white" /></a>
</p>

<p align="center">
  <a href="mods/OniMcp/README_EN.md"><img alt="ONI MCP Server — AI colony control for Oxygen Not Included" src="mods/OniMcp/preview.png" width="47%" /></a>
  <a href="mods/CycleTrim/README.md"><img alt="CycleTrim — performance optimization mod for Oxygen Not Included" src="mods/CycleTrim/preview.png" width="47%" /></a>
</p>

A large-scale modular repository for Oxygen Not Included mod development:

- `onim`: Rust-based ONI mod development CLI (init/build/install/publish workflow)
- `OniMcp`: MCP server mod exposing colony state and safe operations to MCP-compatible clients

> **Compatibility warning**: before `1.0.0`, the `OniMcp` API can still introduce breaking changes. If you build derivatives, plugins, scripts, or third-party clients, pin exact versions and use runtime manifests (e.g. `oni://tools/manifest`) as the compatibility source of truth.

## Table of Contents

- [Project Overview](#project-overview)
- [Supported Modules](#supported-modules)
- [Quick Start](#quick-start)
- [Quick Links](#quick-links)
- [Getting Started with onim](#getting-started-with-onim)
- [Repository Layout](#repository-layout)
- [Command Reference](#command-reference)
- [Development & Runtime Notes](#development--runtime-notes)
- [Compatibility](#compatibility)
- [Contributing](#contributing)
- [Release Notes](#release-notes)

## Project Overview

The repository is organized as a **two-part platform**:

1. **Dev Tooling Layer (`onim`)**  
   A developer experience layer for creating and shipping ONI mods.

2. **Mod Layer (`mods/`)**  
   Concrete mod implementations and templates with explicit boundaries.

## Supported Modules

| Module | Path | Scope |
|---|---|---|
| `onim` | [src/](src/) | Rust CLI for mod lifecycle management |
| `OniMcp` | [mods/OniMcp/](mods/OniMcp/) | ONI MCP server mod and tool surface |
| `CycleTrim` | [mods/CycleTrim/](mods/CycleTrim/) | Lightweight performance mod targeting measured simulation hot paths |
| `OniModTemplate` | [mods/OniModTemplate/](mods/OniModTemplate/) | Boilerplate template for new mod creation |
| Chinese Project Docs | [README_ZH.md](README_ZH.md) | Chinese overview and usage docs |
| MCP Runtime Docs | [docs/mcp-tools-reference.md](docs/mcp-tools-reference.md) | Current tool/resource reference |

## Quick Start

```bash
cargo install --path .
onim setup
onim init MyMod
onim dev -m MyMod
```

## Quick Links

- English ONI MCP docs: [mods/OniMcp/README_EN.md](mods/OniMcp/README_EN.md)
- Chinese ONI MCP docs: [mods/OniMcp/README.md](mods/OniMcp/README.md)
- ONI MCP server changelog: [mods/OniMcp/CHANGELOG.md](mods/OniMcp/CHANGELOG.md)
- Template changelog: [mods/OniModTemplate/CHANGELOG.md](mods/OniModTemplate/CHANGELOG.md)
- CycleTrim changelog: [mods/CycleTrim/CHANGELOG.md](mods/CycleTrim/CHANGELOG.md)

## Open Source References

- Community reference project: [zhuiyun.skill](https://github.com/LIghtJUNction/zhuiyun.skill)
- ONI mod ecosystem references: [PLib](https://github.com/peterhaneve/PLib)
- ONI ecosystem patching: [Harmony](https://github.com/pardeike/Harmony), [FastTrack](https://github.com/peterhaneve/FastTrack)

## ONI MCP Server

`OniMcp` is designed as a **safe, MCP-native operations layer** for Oxygen Not Included:

- **`world_editor`**: world-like text file editing workflow; apply SEARCH/REPLACE style edits to virtual save artifacts
- **`game_control`**: play speed, pause/resume, save/load orchestration
- **`navigation_control`**: camera, overlays, screenshot workflow
- **`building_control`**: building operations and utility routing
- **`orders_control`**: dig/sweep/clean/deconstruct flow control
- **`server_control`**: manifest, screenshot lifecycle, diagnostics, and session status

### ONI MCP Server Design

- Publicly visible surface is intentionally compact and stable for MCP clients.
- Internal compatibility entrypoints remain registered for legacy routing and older integrations.
- Operations are designed with explicit task framing and player confirmation in the operational flow.

See the full runtime docs in [mods/OniMcp/README_EN.md](mods/OniMcp/README_EN.md).

## onim

`onim` is the Rust CLI used by this repository to initialize, build, install, uninstall, inspect, and publish ONI mods.

### Typical Development Flow

1. Install dependencies and initialize local config with `onim setup`
2. Scaffold mod with `onim init`
3. Iterate quickly via `onim dev -m <mod>`
4. Build/publish through `onim build` and `onim publish`

## Repository Layout

```text
.
├── onim.toml                     # onim config (default mod + aliases)
├── Directory.Build.props.example  # .NET config template
├── Directory.Build.props          # generated local config (not committed)
├── Cargo.toml                    # onim CLI project
├── src/                          # onim source
├── mods/                         # mod workspace
│   ├── OniModTemplate/           # reusable mod template (ModInfo + Patches/)
│   ├── OniMcp/                  # ONI MCP server
│   │   ├── ModInfo.cs            # entry only
│   │   ├── Patches/ UI/ Config/ Core/ Server/ Support/
│   │   └── Tools/{Core,Entry,WorldEditor,Shared,Impl}/
│   └── CycleTrim/                # performance mod
│       ├── ModInfo.cs            # entry only
│       ├── Core/                 # shared non-patch logic
│       └── Patches/              # Harmony patches
├── docs/                         # reference docs
└── scripts/                      # helper scripts and verification tools
```

## Getting Started with onim

```bash
# 1) Install CLI from source
cargo install --path .

# 2) Discover game path and dependencies
onim setup

# 3) Create a mod
onim init MyMod --author YourName --desc "Your mod description"

# 4) Development cycle
onim dev -m MyMod         # build + install to Dev
onim info                 # inspect installed mods

# 5) Release cycle
onim install -m MyMod     # Release install to Local
onim publish -m MyMod     # publish to Steam Workshop

# 6) Cleanup
onim uninstall -m MyMod   # supports scope flags
```

If this is your first run, create your local build config before the first build:

```bash
cp Directory.Build.props.example Directory.Build.props
# edit OniGamePath in Directory.Build.props to your local ONI installation
```

`onim setup` can also generate this config automatically.

## Command Reference

| Command | Purpose |
|---|---|
| `onim setup` | initialize config and discover dependencies |
| `onim init <name>` | scaffold from template |
| `onim build` | build a mod (`--release` for release build) |
| `onim build --all` | build all configured mods |
| `onim dev` | build + install a mod to `mods/Dev` |
| `onim dev --all` | build + install all configured mods to `mods/Dev` |
| `onim install` | release build + install to `mods/Local` |
| `onim uninstall` | uninstall `dev/local/all` scoped mods |
| `onim info` | show installed Dev/Local/Steam modules |
| `onim publish` | publish to Steam Workshop |
| `onim list` | list known mods in config |

## Development & Runtime Notes

- Use semantic tasks and explicit short descriptions for all MCP calls.
- Prefer read-only verification for first-pass planning (status, safety checks, manifests).
- This project intentionally avoids pretending full autonomous gameplay can replace human oversight.

### Current direction

- Move game operation toward **virtual-file driven workflows**
- Keep explicit confirmation and bounded operations in critical actions
- Maintain strict safety checks and compatibility surfaces between tool versions

## Compatibility

- `OniMcp` is pre-1.0, so API surface can be unstable.
- For derivatives and integrations, pin exact versions and use runtime manifests as compatibility source.
- Use `oni://tools/manifest` for runtime verification.

## Contributing

1. Open issue for scope and design first
2. Keep PRs focused to one coherent change
3. Add/update docs/changelog links when behavior changes
4. Confirm local workflow (`onim setup`, relevant verify scripts) before merging

## Dependencies

- [Rust](https://rustup.rs/) to compile `onim`
- [.NET SDK](https://dotnet.microsoft.com/download) to build ONI mods
- `unzip` and `tar` for packaging/install helpers
- Steam Workshop + ONI local environment for integration checks

## Release Notes

- `OniMcp` releases are tracked in [mods/OniMcp/CHANGELOG.md](mods/OniMcp/CHANGELOG.md).
- `CycleTrim` history is tracked in [mods/CycleTrim/CHANGELOG.md](mods/CycleTrim/CHANGELOG.md).
- Template changelog is tracked in [mods/OniModTemplate/CHANGELOG.md](mods/OniModTemplate/CHANGELOG.md).

## License

This repository is licensed under the MIT License. See [LICENSE](LICENSE).
