# ONI MCP Server

ONI MCP Server is an Oxygen Not Included mod that starts a local MCP Streamable HTTP server inside the game. It lets MCP-compatible AI clients read colony state, query game data, inspect the map, and perform explicit actions after player confirmation.

Default endpoint:

```text
http://localhost:8788/mcp/
```

Source code: https://github.com/LIghtJUNction/OniMods/

## What It Can Do

* Check oxygen, food, power, temperature, Duplicants, rooms, and alerts.
* Read live `oni://...` resources instead of relying only on screenshots.
* Run small confirmed actions such as pause, speed change, screenshots, schedules, door settings, storage filters, priorities, and Duplicant renaming.
* Read text maps, define areas, preview layouts, and assist with planning.
* Serve as an ONI + MCP + agent experiment platform.

## What It Is Not

* It does not make AI reliably play Oxygen Not Included on its own.
* It is not recommended to let AI perform large dig, deconstruct, sandbox, save, or load actions without confirmation.
* This is an MCP control surface, not a complete autonomous player.

## Installation

Subscribe to **ONI MCP Server**, enable it in the in-game **Mods** menu, then restart the game.

After loading or creating a colony, the mod starts the MCP server automatically. The server may start from the main menu, but colony-dependent tools only return useful data after a save is loaded.

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
      "url": "http://localhost:8788/mcp/"
    }
  }
}
```

For first use, start with a read-only prompt:

```text
Do not modify the save yet. Check colony status and list the three most urgent risks.
```

If token authentication is enabled, send either:

```text
Authorization: Bearer <token>
```

or:

```text
X-Oni-Mcp-Token: <token>
```

## Configuration

Use the in-game mod options screen or edit `OniMcpConfig.json`.

Common options:

* `Host`: default `localhost`; use `0.0.0.0` for LAN access.
* `Port`: default `8788`.
* `AuthEnabled`: enables token authentication.
* `AuthToken`: token value.
* `ScreenshotCleanupEnabled`: removes old temporary screenshots.
* `ScreenshotRetentionMinutes`: screenshot retention time.
* `ScreenshotMaxFiles`: maximum temporary screenshot count.

After changing host, port, or authentication settings, restart the game or save mod options to restart the server.

## Security Notes

Keep `Host=localhost` by default so only local AI clients can connect.

If you expose the server to LAN with `Host=0.0.0.0`, enable token authentication and use a strong token.

Let AI analyze first, then execute only after permission. Large dig, deconstruct, sandbox, save, and load actions should require extra confirmation. Save manually before long automated sessions.

## MCP Tools

The public tool surface currently uses 8 aggregate tools:

* `server_control`: health checks, manifests, tool search, guides.
* `read_control`: maps, colony state, resources, buildings, mechanics.
* `game_control`: pause, resume, speed, saves, sandbox actions.
* `navigation_control`: camera, overlays, screenshots, pointer, clicks, drags.
* `building_control`: build planning, materials, previews, storage, filters, production queues.
* `orders_control`: dig, sweep, mop, deconstruct, priorities, area orders, conduit and wire cuts.
* `dupes_control`: Duplicant status, details, priorities, commands, renaming, skills, hats.
* `colony_control`: snapshots, reports, diagnostics, notifications, schedules, diet, research, medical, farming, ranching.

Older fine-grained tools are kept as hidden compatibility entries. The runtime source of truth is:

```text
server_control domain=catalog action=manifest
```

or:

```text
oni://tools/manifest
```

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

Before `oni_mcp` reaches `1.0.0`, tool names, parameters, resource paths, and response fields may change incompatibly.

Derivative mods, plugins, scripts, and third-party clients should pin a specific version and use the runtime manifest as the compatibility source of truth.

## Credits

Developed and tested by **gpt5.5**, **Kimi k2.6**, and player **LIghtJUNction**. The project is open source and available for inspection, modification, and contribution.
