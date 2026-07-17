# ONI MCP Server

[![ONI MCP Server preview](preview.png)](README_EN.md)

ONI MCP Server is an Oxygen Not Included mod. It starts a local MCP Streamable HTTP service inside the game so MCP-compatible AI clients can read colony state, query game data, inspect maps, and run explicit player-approved actions using a virtual filesystem-style interface.

Browser feedback page:

```text
http://localhost:8788/
```

MCP endpoint:

```text
http://localhost:8788/mcp/
```

Source code: https://github.com/LIghtJUNction/OniMods/

> **Steam Workshop Short Description:**
> An Oxygen Not Included Mod that runs a local Model Context Protocol (MCP) server inside the game. It allows AI assistants to connect, read colony status (oxygen, resources, duplicants), and safely execute player-approved actions (pause, change speed, schedules, building control) via a virtual filesystem-style world editor.

## What It Can Do

* Check oxygen, food, power, temperature, Duplicants, rooms, and alerts.
* Read live `oni://...` resources instead of relying only on screenshots.
* Navigate and edit the game world using virtual save folders and files via the `world_editor` (using SEARCH/REPLACE blocks).
* Run small confirmed actions such as pause, speed changes, screenshots, schedules, doors, storage filters, priorities, and Duplicant renaming.
* Serve as an ONI + MCP + agent experiment platform.

## What It Is Not

* It does not make AI reliably play Oxygen Not Included on its own.
* It is not recommended to let AI run large dig, deconstruct, sandbox, save, or load actions without confirmation.
* This is an MCP control surface, not a complete autonomous player.

## Installation

1. Subscribe to the mod.
2. Enable **ONI MCP Server** in the in-game **Mods** menu.
3. Restart the game.
4. After the main menu or a colony loads, the mod starts the local MCP service automatically.

Tools that depend on save state only return useful data after a colony is loaded. Protocol and configuration checks can work from the main menu.

## Configure Button

After the mod is enabled, the **ONI MCP Server** entry in the in-game **Mods** menu should show a **Configure** button. The button is provided by PLib Options and can change port, token, screenshot cleanup, and related settings. It also shows the current configuration file path.

If the Configure button is missing:

1. Confirm the mod is enabled and the game has been restarted.
2. Confirm the installed package contains the `OniMcp.dll` built with PLib included.
3. If the button is still missing, use v0.2.0 or newer; that version explicitly initializes the PLib Options ModsScreen patch.
4. Edit `OniMcpConfig.json` directly. The mod creates it automatically when it starts.

## Configuration File

The file name is always:

```text
OniMcpConfig.json
```

The preferred location is the Oxygen Not Included user data directory, not the Steam game installation directory:

* Windows: `Documents\Klei\OxygenNotIncluded\OniMcpConfig.json`
* Windows fallback: `Documents\Klei\Oxygen Not Included\OniMcpConfig.json`
* Linux: `~/.config/unity3d/Klei/Oxygen Not Included/OniMcpConfig.json`
* macOS: `~/Library/Application Support/unity.Klei.Oxygen Not Included/OniMcpConfig.json`

For backward compatibility, the mod also reads an existing `OniMcpConfig.json` from the mod installation directory. If neither location exists, the new version writes one to the user data directory on first load.

Common fields:

```json
{
  "Host": "localhost",
  "Port": 8788,
  "AuthEnabled": false,
  "AuthToken": "auto-generated-random-token",
  "SecurityMigrationVersion": 1,
  "GlobalAutoDisinfectDisabled": false,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

After changing the file, click **Restart MCP server** in the options panel or restart the game.
PLib 4.24 adds scroll bars when the options dialog exceeds its maximum height; expand `Status`, `Server`, `Security`, and `Screenshots` to reach every MCP setting.

## Save and Restart Through Steam

On Linux, `game_control domain=launch action=restart_load` synchronously saves the active save, fully exits ONI, and relaunches Steam AppID 457140. Preview with `dryRun=true`; execution requires `confirm=true`. It defaults to `resume=false`, so the new process loads the exact saved path and remains paused. The old process returns only `accepted/jobId/exactSavePath`; after relaunch, query `action=restart_status jobId=<id>` for `loaded` or `failed` instead of assuming one HTTP request survives the process restart.

## Token Authentication

Token verification is disabled by default:

```json
"AuthEnabled": false
```

On first launch, the mod generates a random `AuthToken` and writes it to `OniMcpConfig.json`, but verification remains off. Clients need either HTTP header only after **Require token** is manually enabled:

```text
Authorization: Bearer <AuthToken>
```

or:

```text
X-Oni-Mcp-Token: <AuthToken>
```

Upgrades from legacy configs perform one security migration that explicitly disables authentication; later manual enablement is preserved. For local-only use where the port is not exposed, keep the default disabled state:

```json
"AuthEnabled": false
```

If `Host` is changed to `0.0.0.0` for LAN access, keep token verification enabled and use a strong random token.

## Connect an MCP Client

Any client that supports MCP Streamable HTTP can connect to:

```text
http://localhost:8788/mcp/
```

Claude Desktop example:

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8788/mcp/",
      "headers": {
        "Authorization": "Bearer <AuthToken>"
      }
    }
  }
}
```

If token verification is enabled, keep the `headers` block. Otherwise remove it.

For first use, start with a read-only prompt:

```text
Do not modify the save yet. Check colony status and list the three most urgent risks.
```

## Main Tools

The public tool surface is a compact set of aggregate entrypoints. The runtime manifest is the source of truth:

```text
server_control domain=catalog action=manifest
```

or read:

```text
oni://tools/manifest
```

Public aggregate tools:

* `world_editor`: Code-file style world editor. Saves are virtual folders; use commands `cd`, `ls`, `read`, `search` to inspect world files, and `edit` with SEARCH/REPLACE blocks to apply changes (digging, building, priorities, wires, pipes).
* `game_control`: Game state, speed, saves, UI, and other game-level actions.
* `navigation_control`: Camera, view, overlay, and screenshot operations.
* `building_control`: Building planning, placement, configuration, production, and utility connections.
* `orders_control`: Digging, sweeping, mopping, deconstruction, and other designations.
* `server_control`: Server health checks, tool catalogs, screenshot utility, background tasks, and manifest.

Other aggregate entrypoints, including `colony_control`, `dupes_control`, `read_control`, and `search_control`, remain registered for internal virtual-file routing and compatibility clients, but are not returned by the default `tools/list` response.

`coordinate_control` is not part of the current public runtime, and ordinary aggregate tools reject raw coordinates. For exact orders, read `/active/ops/tools.md` and edit `/active/ops/orders.md`; for exact construction, read and edit map tokens in `/active/map/viewport.md`. Select only currently public typed files/tools, ignoring the hidden `coordinate_control` and `/active/ops/coordinate.md` compatibility entries.

## Common Resources

* `oni://colony/status`
* `oni://colony/diagnostics`
* `oni://colony/alerts`
* `oni://colony/summary`
* `oni://resources/inventory`
* `oni://resources/food`
* `oni://dupes`
* `oni://dupes/status-check`
* `oni://power/summary`
* `oni://rooms/list`
* `oni://world/text-map`
* `oni://buildings/defs`
* `oni://tools/manifest`
* `oni://tools/guide`

## API Stability

Before `oni_mcp` reaches `1.0.0`, tool names, parameters, resource paths, and response fields may change incompatibly. Derivative mods, plugins, scripts, and third-party clients should pin a specific version and prefer the runtime manifest as the compatibility source of truth.

## Credits

Developed and tested by **gpt5.5**, **gpt5.6**, **glm5.2**, **Kimi k2.6**, **Kimi k3**, **Gemini 3.5 Flash**, **grok4.5**, and player **LIghtJUNction**. The project is open source and available for inspection, modification, and contribution.

## Changelog

Full history: [CHANGELOG.md](CHANGELOG.md)

## References

- Repository: [github.com/LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)
- API docs: [docs/mcp-tools-reference.md](../../docs/mcp-tools-reference.md)
- Developer guide: [docs/api-developer-guide.md](../../docs/api-developer-guide.md)
- Runtime entry point: [mods/oni_mcp/ModInfo.cs](ModInfo.cs)
- Surface-contract verification script: [scripts/verify_oni_mcp_tool_surface.py](../../scripts/verify_oni_mcp_tool_surface.py)

## Related Projects / Friend Links

- Model Context Protocol: [modelcontextprotocol](https://github.com/modelcontextprotocol)
- ONI MCP reference implementation (this repo): [OniMods](https://github.com/LIghtJUNction/OniMods)
- FastTrack performance patching example for ONI: [FastTrack](https://github.com/peterhaneve/FastTrack)
- Harmony patching framework: [Harmony](https://github.com/pardeike/Harmony)
