# OniMods

[简体中文](README_ZH.md)

An Oxygen Not Included mod repository containing:

- `onim`: the built-in ONI mod development CLI in this repo, used to initialize, build, install, uninstall, and publish mods.
- `oni_mcp`: a mod that lets AI assistants read and operate an ONI colony through MCP.

> **Compatibility warning**: before `1.0.0`, the `oni_mcp` API may still introduce breaking changes. If you build derivatives, plugins, scripts, or third-party clients, pin an exact version and use the runtime `tools_manifest` / `oni://tools/manifest` as the source of truth for compatibility.

## Quick Links

| Project | Path | Description |
|------|------|------|
| `onim` | [src/](src/) | Rust-based mod development toolchain |
| `oni_mcp` | [mods/oni_mcp/](mods/oni_mcp/) | ONI MCP Server mod |
| Chinese README | [README_ZH.md](README_ZH.md) | Chinese project overview |
| `oni_mcp` Chinese docs | [mods/oni_mcp/README.md](mods/oni_mcp/README.md) | Chinese installation, connection, and feature guide |
| `oni_mcp` English docs | [mods/oni_mcp/README_EN.md](mods/oni_mcp/README_EN.md) | English installation and usage guide |

## ONI MCP Server

[ONI MCP Server docs](mods/oni_mcp/README_EN.md)

<details>
<summary>Expand Mod Overview</summary>

`oni_mcp` is an MCP server mod tailored for Oxygen Not Included. After installation, MCP-capable AI clients can use a local HTTP interface to read colony state, analyze situations, and, with authorization, perform selected in-game actions.

Right now it is best suited as an "AI colony advisor" and a "semi-automated operations assistant":

- Read colony state, resource inventory, duplicant needs, power, rooms, temperature, rockets, and more.
- Perform simple, explicit actions such as pausing the game, taking screenshots, adjusting schedules, changing building settings, setting door permissions, and renaming duplicants.
- Help agents learn the large MCP tool surface through tool grouping, tool search, and skill docs in `.agents/skills`.
- Provide risk levels and confirmation requirements for dangerous actions such as digging and deconstruction.

It is not a full autonomous gameplay agent. Complex world planning, long-term autonomous colony play, and high-quality tactical decision-making still require significant constraints, validation, and human supervision.

Related AI Skill reference: [zhuiyun.skill](https://github.com/LIghtJUNction/zhuiyun.skill)

</details>

<details>
<summary>Expand Development Notes</summary>

Idea note: implement a second "agent pointer" that always snaps to cell centers and renders on screen, so all AI actions can be executed through a visible pointer. Compared with issuing direct coordinate commands, this may be more stable and easier to observe and debug.

Short version: do not expect current AI systems to play a complex simulation game like ONI well over long autonomous sessions. A more realistic direction for now is letting AI read more game data, execute small explicit tasks, and assist with local planning after player confirmation.

</details>

## onim

`onim` is an ONI mod development toolchain covering the usual workflow from project initialization to Steam Workshop publishing.

## One-Line Start

```bash
cargo install --path .
onim setup
onim init MyMod
onim dev -m MyMod
```

## Common Workflow

```bash
# 1. Install the onim CLI
cargo install --path .

# 2. Interactive setup: detect game path, check dependencies, write config
onim setup

# 3. Create a new mod
onim init MyMod --author YourName --desc "Mod description"

# 4. Development iteration
onim dev -m MyMod          # build and install into the game Dev folder
onim build -m MyMod        # build only
onim info                  # inspect installed mods

# 5. Formal release
onim install -m MyMod      # Release build and install into the Local folder
onim publish -m MyMod      # upload to Steam Workshop

# 6. Cleanup
onim uninstall -m MyMod    # uninstall from the game directory
```

`-m <name>` selects the mod. If omitted, `onim` uses `default_mod` from [oni-mods.toml](oni-mods.toml).

Before the first build, create the local MSBuild config:

```bash
cp Directory.Build.props.example Directory.Build.props
# Then edit OniGamePath in Directory.Build.props to your local ONI install path.
```

You can also run `onim setup` to detect and write the config automatically.

## Command Reference

| Command | Purpose |
|------|------|
| `onim setup` | Initialize project config and detect game path/dependencies |
| `onim init <name>` | Create a new mod from the template |
| `onim build` | Build the mod, with `--release` for a Release build |
| `onim dev` | Build and install into the game `mods/Dev/` directory |
| `onim install` | Release build and install into `mods/Local/` |
| `onim uninstall` | Uninstall a mod, supports `--scope dev/local/all` |
| `onim info` | Show installed Dev, Local, and Steam mods |
| `onim publish` | Publish to the Steam Workshop, supports `--gui` |
| `onim list` | List mods from the config file |

## Repository Layout

```text
.
├── oni-mods.toml          # onim config file, stores default mod and mod list
├── Directory.Build.props.example  # local MSBuild config template
├── Directory.Build.props          # generated local config, not committed
├── Cargo.toml             # onim CLI
├── src/                   # onim source code
├── mods/                  # mod project directory
│   ├── OniModTemplate/    # mod template
│   └── oni_mcp/           # ONI MCP Server
└── oni/src/               # decompiled game source reference
```

## Dependencies

- [Rust](https://rustup.rs/): compile `onim`
- [.NET SDK](https://dotnet.microsoft.com/download): build mods
- `unzip`: extract build outputs during install
- `tar`: package source archives

`onim setup` checks dependencies automatically and suggests how to install missing ones.
