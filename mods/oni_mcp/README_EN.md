**Source Code:** https://github.com/LIghtJUNction/OniMods/

# ONI MCP Server

ONI MCP Server is an Oxygen Not Included mod. It starts a local MCP Streamable HTTP server inside the game, allowing MCP-capable AI clients to read colony state, query game data, inspect the map, and execute authorized game actions.

Default endpoint:

```text
http://localhost:8787/mcp/
```

> **API stability warning**: Before `oni_mcp` reaches `1.0.0`, tool names, parameters, resource paths, and response fields may change incompatibly. Derivative mods, plugins, scripts, and third-party clients should pin a specific version and use the runtime `tools_manifest` / `oni://tools/manifest` as the compatibility source of truth.

## What It Is Good For

- Colony advice: check oxygen, food, power, temperature, Duplicants, rooms, and alerts.
- Live game data: read `oni://...` resources instead of relying only on screenshots.
- Small operations: pause, change speed, take screenshots, adjust schedules, configure doors, set storage filters, change priorities, and rename Duplicants.
- Planning support: read text maps, define areas, generate layout candidates, and use a plan gate before execution.
- Agent experiments: expose tool search, manifests, resource templates, and risk levels for custom AI skills.

## What It Is Not

- It does not make current AI reliably autonomous at Oxygen Not Included. Long-term planning, priority management, and cascading failures are still difficult.
- It is not recommended to let AI run large dig, deconstruct, sandbox, save, or load operations without confirmation.
- This project provides the MCP server and game control surface. It is not a complete autonomous agent.

## Installation

### Steam Workshop

Subscribe to **ONI MCP Server**, enable it in the Mods menu, then restart the game.

### Local Install

Extract `OniMcp.zip` into the game's local mod directory, for example:

```text
mods/Local/OniMcp/
```

For development, install it into:

```text
mods/Dev/OniMcp/
```

With this repository's `onim` tool:

```bash
onim dev -m oni_mcp
```

## Launch

1. Start Oxygen Not Included.
2. Enable **ONI MCP Server** in the main menu **Mods** screen.
3. Load or create a colony.
4. The mod starts the MCP server at `http://localhost:8787/mcp/` by default.

The server starts as early as possible in the main menu. Tools that need colony state only return useful data after a save is loaded.

## Connect An AI Client

### Claude Desktop Example

Edit the Claude Desktop config file:

- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8787/mcp/"
    }
  }
}
```

Restart the client, then start with a read-only request:

```text
Do not modify the save yet. Check my colony status and list the three most urgent risks.
```

### Other MCP Clients

Any client that supports MCP Streamable HTTP can connect. Set the URL to:

```text
http://localhost:8787/mcp/
```

If token authentication is enabled, the client must send `Authorization: Bearer <token>` or `X-Oni-Mcp-Token: <token>`.

## Configuration

You can use the in-game mod options screen or edit `OniMcpConfig.json`. The mod first checks the mod directory; if no config exists there, it uses the game's persistent data directory.

```json
{
  "Host": "localhost",
  "Port": 8787,
  "AuthEnabled": false,
  "AuthToken": "",
  "GlobalAutoDisinfectDisabled": false,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `Host` | `localhost` | HTTP listen host. Use `0.0.0.0` for LAN access |
| `Port` | `8787` | MCP port, from `1024` to `65535` |
| `AuthEnabled` | `false` | Require token authentication |
| `AuthToken` | empty | Token value. Empty token disables authentication |
| `GlobalAutoDisinfectDisabled` | `false` | Keep global auto-disinfect disabled |
| `ScreenshotCleanupEnabled` | `true` | Remove old temporary screenshots |
| `ScreenshotRetentionMinutes` | `120` | Temporary screenshot retention in minutes |
| `ScreenshotMaxFiles` | `40` | Maximum temporary screenshot count |

After changing `Host`, `Port`, or authentication settings, restart the game or save the mod options to restart the server.

## Security Notes

- Keep `Host=localhost` by default so only local AI clients can connect.
- If you set `Host=0.0.0.0` for LAN access, enable `AuthEnabled` and use a strong token.
- Ask the AI to analyze first and execute only after permission. Large dig, deconstruct, sandbox, save, and load actions need extra care.
- Save manually before long automated sessions, and limit the AI to short run windows.

## MCP Capabilities

The current implementation includes 330+ tools, 120+ fixed resources, and 100+ resource templates. The exact list may change with the code; use the runtime `tools_manifest` tool or `oni://tools/manifest` resource as the source of truth.

### Core Tools

| Area | Example tools | Purpose |
|------|---------------|---------|
| Server and catalog | `server_status`, `tools_manifest`, `tools_search`, `tools_guide` | Check service state, search tools, route goals to tool chains |
| Game control | `game_pause`, `game_resume`, `game_set_speed`, `game_save` | Pause, resume, change speed, save |
| Camera and screenshots | `camera_move`, `camera_switch_view`, `game_screenshot` | Move camera, switch overlays, capture screenshots |
| World reading | `world_text_map`, `world_area_snapshot`, `world_cell_info` | Text maps, area snapshots, cell details |
| Area management | `area_define`, `area_get`, `area_blocks`, `area_merge` | Define and reuse map regions |
| Agent pointer | `agent_pointer_aim_cell`, `agent_pointer_user_mouse_get`, `agent_pointer_say`, `agent_pointer_left_click` | Execute click-based actions, read the user's mouse cell, and show pointer speech bubbles |
| Buildings and orders | `buildings_search_defs`, `buildings_materials`, `build_preview`, `build_area`, `orders_dig_area`, `orders_sweep_area` | Find buildings, choose materials, preview/batch-place blueprints, create dig and sweep orders |
| Duplicants | `dupes_status_check`, `dupes_detail`, `dupes_needs`, `dupes_priority_set`, `dupes_rename` | Check status, needs, priorities, and names |
| Management screens | `schedule_list`, `schedule_set_block`, `diet_status`, `resources_storage_set_filter` | Schedules, diet, storage filters, and management screen settings |
| Side screens | `filters_list`, `state_controls_list`, `automation_controls_list`, `lights_color_set` | Common building side-screen configuration |
| Rockets and space | `rockets_status`, `rocket_modules_list`, `rocket_crew_requests_list` | Rocket state, modules, crew, and space systems |
| Audit and coverage | `tools_player_action_coverage`, `tools_static_audit`, `side_screen_surfaces_audit` | Inspect tool coverage and gaps |
| Batch and planning | `tools_call_many`, `agent_program_execute`, `edit_mark_request_list` | Batch calls, conditional/loop flow scripts, and player edit marks |

For `agent_pointer_*`, `agentId` is a logical pointer name scoped to the current MCP session. If omitted, the session's default `agent` pointer is used. The same `agentId` in different client sessions does not share state; default labels include the client name and a short session prefix. Use `mcp_client_capabilities` to inspect current sessions and client info. Use `agent_pointer_clear` to delete a pointer and its jump points when it is no longer needed.

### Common Resources

| URI | Description |
|-----|-------------|
| `oni://colony/status` | Cycle, Duplicant count, speed, pause state |
| `oni://colony/diagnostics` | Oxygen, food, heat, and other diagnostics |
| `oni://colony/alerts` | Current alerts and notifications |
| `oni://colony/summary` | Action-oriented colony summary |
| `oni://resources/inventory` | Resource inventory |
| `oni://resources/food` | Food inventory and spoilage data |
| `oni://dupes` | Duplicant list |
| `oni://dupes/status-check` | Position, errands, needs, and stuck-risk signals |
| `oni://power/summary` | Power network and battery summary |
| `oni://rooms/list` | Room system state |
| `oni://thermal/overheat-risk` | Building overheat risks |
| `oni://world/text-map` | Text map |
| `oni://buildings/defs` | Buildable building definitions |
| `oni://tools/manifest` | Tool manifest |
| `oni://tools/guide` | Goal-oriented tool-chain guide |

### Built-In Prompts

- `colony_triage`: quick colony triage
- `next_cycle_plan`: next-cycle plan
- `inspect_area`: area inspection
- `dupe_care_review`: Duplicant care review
- `power_audit`: power audit
- `rooms_overview`: rooms overview
- `thermal_audit`: thermal management audit

## Recommended Workflow

1. Read state first: `oni://colony/status`, `oni://colony/diagnostics`, `oni://resources/food`, and `oni://dupes/status-check`.
2. Find tools: use `tools_search` or `oni://tools/guide` for the user's goal.
3. Work in small areas: define an area with `area_define`, then use `*_area` tools to avoid coordinate mistakes.
4. Plan before batches: use the plan gate for multi-step building, digging, or deconstruction.
5. Verify after changes: reread the relevant resources and confirm the game state changed as expected.

## Risk Levels

| Risk | Meaning |
|------|---------|
| `read` | Query only, no save mutation |
| `write` | Change settings, filters, priorities, or assignments |
| `execute` | Issue in-game actions or trigger UI behavior |
| `dangerous` | Digging, deconstruction, sandbox, irreversible, or large-area changes. Usually requires `confirm: true` |

## Troubleshooting

- AI cannot connect: make sure the mod is enabled, the game is running, and the port is `8787`.
- Tools return empty data: load a colony first; many game systems do not exist in the main menu.
- LAN access fails after setting `0.0.0.0`: check the system firewall and network isolation.
- Authentication returns 401: make sure the client sends `Authorization: Bearer <token>` or `X-Oni-Mcp-Token`.
- Port conflict: change `Port`, save options, then restart the server or restart the game.

## Development

Build:

```bash
onim build -m oni_mcp
```

Install for development:

```bash
onim dev -m oni_mcp
```

Project layout:

```text
mods/oni_mcp/
├── Core/                 # MCP protocol types
├── Server/               # HTTP server and Unity main-thread bridge
├── Tools/                # MCP tools, resources, and prompt registries
├── Config/               # Mod configuration
├── Support/              # Paths, logging, reflection, and JSON helpers
├── Localization/         # Localization strings
├── assets/               # Tool icons and resources
├── ModInfo.cs            # Mod entry point
└── OniMcp.csproj         # Project configuration and packaging logic
```

Typical new-tool flow:

1. Implement a method under `Tools/` that returns an `McpTool`.
2. Register it in `OniToolRegistry.Initialize()`.
3. If a read-only entry is useful, add an `oni://` resource or resource template in `OniResourceRegistry`.
4. Add confirmation parameters and an appropriate risk level for dangerous tools.
5. Use `tools_static_audit` or `tools_manifest` to verify runtime registration.

The release package includes `OniMcp.dll`, mod metadata, preview image, README files, and required assets.

## Credits

This mod was developed and tested by **gpt5.5**, **Kimi k2.6**, and player **LIghtJUNction**. The source code is fully open for inspection, modification, and contribution.
