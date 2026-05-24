**Source Code:** https://github.com/LIghtJUNction/OniMods/

# ONI MCP Server

Let your AI assistant connect directly to your Oxygen Not Included colony. Once this mod is installed, any MCP-compatible AI (Claude, Cursor, or any other MCP client) can read game state, analyze your situation, and even issue construction and scheduling commands through a local HTTP interface.

> No coding knowledge required. Just install the mod, launch the game, configure your AI client, and your assistant can "see" and "manage" your colony.

## What This Mod Does

### 🤖 AI Colony Advisor
- **Colony Health Check**: AI automatically scans oxygen, food, power, and temperature, identifies risks, and offers advice
- **Power Audit**: Detects power shortfalls, battery status, and circuit loads to prevent blackouts
- **Thermal Management**: Scans for overheating buildings and warns you before equipment fails
- **Room Planning**: Checks if morale rooms are complete and identifies missing room types

### 🎮 Voice/Text Commands
- Tell the AI "pause the game and take a screenshot" → AI calls `game_pause` + `take_screenshot`
- Say "set my digger Duplicant's digging priority to max" → AI calls `dupes_priority_set`
- Say "build two beds at (100, 200)" → AI calls `buildings_plan`
- Say "check rocket status" → AI calls `rockets_status`

### 📊 Real-Time Data Panel
Through `oni://...` resource URIs, the AI can read live game data:
- Colony status, diagnostics, and alerts
- Resource inventory and food reserves
- Duplicant roster and needs
- Power system summary
- Room system status
- Building overheat risks

### 🛡️ Safety Controls
- All mutating operations have risk levels clearly marked
- Dangerous actions (digging, deconstruction) require explicit confirmation
- Supports Plan Harness workflow: AI proposes a plan, you review and confirm, then it executes

## Installation

### Prerequisites
- Oxygen Not Included (Steam version)
- Install the mod: extract `OniMcp.zip` into the game's `mods/` folder, or subscribe via Steam Workshop

### Launch
1. Launch Oxygen Not Included
2. From the main menu, go to **Mods** → enable **ONI MCP Server**
3. Load or create a colony
4. The MCP server starts automatically at `http://localhost:8787/mcp/`

### Connect Your AI

#### Claude Desktop Setup
Edit `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8787/mcp/"
    }
  }
}
```

Restart Claude Desktop, then type "check my colony status" in chat to get started.

#### Other MCP Clients
Any client that supports MCP Streamable HTTP transport can connect. Just set the URL to `http://localhost:8787/mcp/`.

## Mod Configuration

Create or edit `OniMcpConfig.json` (in the mod directory or the game's persistent data folder):

```json
{
  "Host": "localhost",
  "Port": 8787,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

- `Host`: Defaults to `localhost`. Set to `0.0.0.0` for LAN access
- `Port`: Defaults to `8787`
- `ScreenshotCleanupEnabled`: Auto-cleanup AI screenshots
- `ScreenshotRetentionMinutes`: How long screenshots are kept
- `ScreenshotMaxFiles`: Maximum number of screenshots to retain

Changes take effect after restarting the game.

## Feature Details

### Tools

About **320 tools** cover every game system, grouped as follows:

| System | Representative Tools | Capabilities |
|--------|----------------------|--------------|
| Colony | `colony_status`, `colony_diagnostics`, `colony_alerts` | Status, diagnostics, alerts |
| Duplicants | `dupes_list`, `dupes_detail`, `dupes_needs`, `dupes_attributes` | List, details, needs, attributes |
| Power | `power_summary` | Power system summary |
| Rooms | `rooms_list` | Room system status |
| Thermal | `thermal_overheat_risk_scan` | Overheat risk scan |
| Buildings | `buildings_search_defs`, `buildings_plan`, `buildings_deconstruct` | Search, plan, deconstruct |
| Orders | `orders_dig`, `orders_sweep`, `orders_harvest` | Digging, sweeping, harvesting |
| Rockets | `rockets_list`, `rockets_status`, `rockets_request_launch` | List, status, launch |
| Farming | `farming_planting`, `farming_harvestables` | Planting, harvestables |
| Research | `research_status`, `set_research` | Status, set |
| Schedule | `schedule_list`, `schedule_set_block` | List, set block |
| Resources | `resources_inventory`, `resources_food` | Inventory, food |
| Camera | `camera_move`, `camera_switch_view`, `take_screenshot` | Move, switch overlay, screenshot |
| World | `world_cell_info`, `world_text_map` | Cell info, text map |
| Game Control | `game_pause`, `game_set_speed`, `save_game` | Pause, speed, save |
| Meta Tools | `tools_call_many`, `plan_harness_create` | Batch calls, plan harness |

### Prompts

7 built-in scenario prompts to kick off standard workflows:

- `colony_triage` — Colony Health Check
- `power_audit` — Power Audit
- `rooms_overview` — Rooms Overview
- `thermal_audit` — Thermal Audit
- `next_cycle_plan` — Next Cycle Plan
- `inspect_area` — Area Map Analysis
- `dupe_care_review` — Duplicant Care Review

### Resources

Read live game state via `oni://` URIs:

- `oni://colony/status` — Colony status
- `oni://power/summary` — Power summary
- `oni://rooms/list` — Room list
- `oni://thermal/overheat-risk` — Overheat risk
- `oni://resources/inventory` — Resource inventory
- `oni://dupes` — Duplicant list
- `oni://rockets/status` — Rocket status
- `oni://world/text-map` — Text map
- And 100+ more resources...

### Risk Levels

- **read** (Read-only): Query state without modifying the save
- **write** (Write): Modify settings, priorities, filters
- **execute** (Execute): Issue action commands
- **dangerous** (Dangerous): Digging, deconstruction, and other large-scale changes; requires `confirm: true`

## Technical Details

- **Transport**: Streamable HTTP (JSON-RPC 2.0), default port 8787
- **Serialization**: Newtonsoft.Json (bundled with the game)
- **HTTP Server**: System.Net.HttpListener (built into .NET Framework)
- **Code Injection**: Harmony (bundled with the game)
- **All game API calls** execute on the Unity main thread for stability

## Building

```bash
onim build -m oni_mcp
```

The release package only contains `OniMcp.dll`, metadata, preview images, and resource files — no extra runtime libraries needed.

## Development

Project structure:

```
mods/oni_mcp/
├── Core/           - MCP protocol type definitions
├── Server/         - HTTP server + main thread bridge
├── Tools/          - Game action tool implementations
├── ModInfo.cs      - Mod entry point
└── OniMcp.csproj   - Project config
```

To add a new tool: create a new `McpTool` under `Tools/` and register it in `OniToolRegistry.Initialize()`.

## Credits & Testing

This mod was co-developed and tested by **gpt5.5** (approx. 400M tokens consumed), **Kimi k2.6**, and player **LIghtJUNction**. The full source code is open — feel free to inspect and contribute.
