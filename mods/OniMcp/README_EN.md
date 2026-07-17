# ONI MCP Server

[![ONI MCP Server preview](preview.png)](README.md)

ONI MCP Server is an Oxygen Not Included mod that exposes a local MCP service (`http://localhost:8788/mcp/`) with `oni://` resources for colony introspection and controlled actions, designed for safe AI/client interaction.

## Index

- [What It Is](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#what-it-is)
- [Install and Start](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#install-and-start)
- [Endpoint and Clients](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#endpoint-and-clients)
- [Settings and Security](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#settings-and-security)
- [Tool Groups](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#tool-groups)
- [Common Links and Resources](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#common-links-and-resources)
- [Compatibility and Stability](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#compatibility-and-stability)
- [Updates and Validation](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#updates-and-validation)
- [References and Credits](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md#references-and-credits)

## What It Is

### What It Does

- Read colony status, alerts, resources, and map-level data.
- Run small gameplay actions through aggregate tools (pause/speed/screenshots/scheduling/building/priority work).
- Provide an MCP-safe interaction path with explicit user confirmation.

### What It Does Not Do

- It is not a full autonomous player for long campaigns.
- High-risk operations (large dig/deconstruct/restart/load actions) should be explicit and confirmed by the client.

## Install and Start

1. Subscribe and enable **ONI MCP Server** in the in-game **Mods** menu.
2. Restart the game; service starts automatically in main menu/colony flow.
3. `oni://` resource tools depend on an active loaded colony; read-only checks can work earlier.

## Endpoint and Clients

- Browser endpoint: `http://localhost:8788/`
- MCP endpoint: `http://localhost:8788/mcp/`
- Client examples and payload guidance: [docs/api-developer-guide.md](../../docs/api-developer-guide.md)
- MCP resource references: [docs/mcp-tools-reference.md](../../docs/mcp-tools-reference.md)

## Settings and Security

- Config file: `OniMcpConfig.json`
- Default fields and load precedence are in: [mods/OniMcp/ModInfo.cs](ModInfo.cs)
- Default `AuthEnabled` is `false`; enable auth if exposing beyond local host.
- Restart MCP server via options button or full game restart after config updates.

## Tool Groups

- Manifest entry point: `oni://tools/manifest` or `server_control domain=catalog action=manifest`
- Default public aggregates:
  - `world_editor`: virtualized world access (`cd`, `ls`, `read`, `search`, `edit`)
  - `game_control`: gameplay state and control
  - `navigation_control`: camera, overlays, screenshots
  - `building_control`: planning, placement, and utility handling
  - `orders_control`: dig/sweep/mop/deconstruct orders
  - `server_control`: server state, screenshots, background tasks

## Common Links and Resources

- `oni://tools/manifest`
- `oni://tools/guide`
- `oni://colony/status`
- `oni://dupes/status-check`
- `oni://world/text-map`
- `oni://buildings/defs`
- `oni://resources/inventory`
- [scripts/verify_onimcp_tool_surface.py](../../scripts/verify_onimcp_tool_surface.py)
- [CHANGELOG.md](CHANGELOG.md)

## Compatibility and Stability

- Before `1.0.0`, tool names, parameters, and response fields can change.
- Third-party clients should pin versions and use runtime manifest as the compatibility source.

## Updates and Validation

- Changelog: [CHANGELOG.md](CHANGELOG.md)
- Verification: [scripts/verify_onimcp_tool_surface.py](../../scripts/verify_onimcp_tool_surface.py)

## References and Credits

- Repository: [LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)
- MCP ecosystem: [modelcontextprotocol](https://github.com/modelcontextprotocol)
- Related projects: [FastTrack](https://github.com/peterhaneve/FastTrack), [Harmony](https://github.com/pardeike/Harmony)
- Developed and tested by: gpt5.5, gpt5.6, glm5.2, Kimi k2.6, Kimi k3, Gemini 3.5 Flash, grok4.5, LIghtJUNction
