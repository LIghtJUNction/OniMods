---
name: oni-mcp-build-control
description: MCP control skill for ONI building and construction operations. Covers spatial scanning, area definition, batch building plans, excavation orders, and construction verification through MCP tools. Use when controlling construction via MCP.
---

# ONI MCP Build Control

## When to use

Use when the user wants to **build, dig, deconstruct, or reorganize** the colony through MCP tools.

## Control Model

### Construction Control Loop

```
Scan    → minimal status + pointer/camera + targeted small snapshot only if needed
Plan    → choose pointer start + tool + click/drag gestures
Validate→ dryRun/validateOnly tools first when available
Execute → agent_pointer_left_click / agent_pointer_hold_left
Verify  → agent_pointer_get + targeted small snapshot/status
```

## Pointer-First Construction Policy

The visible agent pointer is the primary construction interface. Normal construction, digging, cancellation, sweeping, mopping, harvesting, and simple deconstruction must use pointer tools first:

```
agent_pointer_jump x=<startX> y=<startY> worldId=<worldId>
agent_pointer_select_tool tool=build prefabId=<PrefabId> material=auto priority=5
agent_pointer_left_click confirm=true
agent_pointer_hold_left direction=<right|left|up|down> length=<cells> confirm=true
```

Use direct coordinate tools only when no pointer equivalent exists:

- Legacy coordinate building tools are not exposed. Do not call or suggest them.
- `orders_*_area`: use only for large rectangles or missing pointer support; otherwise select the corresponding pointer tool and drag/click.
- `world_text_map`: use only for targeted verification or chunked scans, not as the default action substrate.

For repeat locations, save pointer jump points:

```
agent_pointer_jump_point_set code=p1
agent_pointer_jump code=p1
```

## Spatial Operations

### Coordinate System

- Origin: bottom-left of the world
- Grid: integer cells, x increases right, y increases up
- World ID: each asteroid has its own worldId
- Active world: `ClusterManager.Instance.activeWorldId`
- Screenshots and text maps are observation sources, not the main control surface.
- Before placing floors, walls, ladders, wires, pipes, or machines, aim the visible agent pointer at the intended start cell and select the build tool.

### Grid Alignment Rule

Misaligned blueprints are usually caused by converting visual intent into raw coordinates. Avoid this with a strict pointer workflow:

1. Use compact status first; only read a map if terrain/hazard context is unknown:
   ```
   colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
   ```
   or:
   ```
   world_area_snapshot areaId=<small area> preset=construction profile=scan encoding=rle includeScreenshot=false
   ```
2. Aim the pointer at the exact start cell with `agent_pointer_jump` or nudge from a known point.
3. Select the desired tool/building/material with `agent_pointer_select_tool`.
4. Use `agent_pointer_hold_left` for straight segments and `agent_pointer_left_click` for single placements.
5. Use `agent_pointer_get` after the action to confirm the pointer endpoint.
6. If a placement is wrong, switch to `tool=cancel` and click/drag the wrong cells before replacing.

For the common "extend existing platform left/right from the printing pod" task:

- Jump or nudge the pointer to the existing platform endpoint.
- Select `tool=build prefabId=Tile material=auto`.
- Extend left/right with `agent_pointer_hold_left direction=left|right length=<cells> confirm=true`.
- If terrain blocks the route, switch to `tool=dig` and drag the blocking natural tiles first; do not shift the floor line up/down unless the plan explicitly calls for a ramp or step.

### Scanning Workflow

```
colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
agent_pointer_get      → get current agent pointer, selected tool, and jump points
agent_pointer_jump     → jump to home/p1/p2/x/y when target is known
world_area_snapshot chunksOnly=true → only for large unknown areas
world_area_snapshot areaId=<block> preset=planning profile=scan encoding=rle → targeted base-layout snapshot
layout_candidates x1 y1 x2 y2 purpose=lab|barracks|bathroom|power|farm → scored room/platform candidates
world_text_map areaId=<small area> profile=scan encoding=rle → compact verification scan
  → If need objects: add includeBuildings=true, includeItems=true
  → If need elements: add includeElements=true
```

Prefer pointer state and small/chunked snapshots. Use `layout_candidates` before choosing room shapes, but execute simple lines and single placements through the pointer. Use screenshots only as visual confirmation; do not infer exact build coordinates from screenshots alone.

Example:
```json
{
  "name": "world_text_map",
  "arguments": {
    "x1": 10, "y1": 20, "x2": 30, "y2": 40,
    "profile": "scan", "encoding": "rle",
    "includeBuildings": true, "includeElements": false
  }
}
```

### Area Definition and Reuse

```
area_define x1 y1 x2 y2 label="bedroom_block" → get areaId
area_blocks worldId=<id> maxCells=1600 → get b* blocks for whole-world scanning
area_merge areaIds=["b1","b2","b3"] label="north_expansion" → create a merged a* area
world_area_snapshot areaId=bedroom_block preset=planning → reusable planning context
layout_candidates areaId=bedroom_block purpose=barracks → choose room rectangle
world_text_map areaId=bedroom_block profile=standard encoding=plain → reuse area
agent_pointer_jump + agent_pointer_select_tool + agent_pointer_hold_left → build within area
```

`area_define` 返回手工 `a*` areaId；`area_blocks` 返回自动地图块 `b*` areaId。两者都可以在任何接受 `areaId` 参数的工具中复用，替代 `x1/y1/x2/y2`。相邻块可临时写成 `areaId=b1+b2+b3`，或用 `area_merge` 生成新的 `a*`。拼接使用外接矩形，非相邻块会包含中间空隙。对块内施工或订单优先使用地图输出的世界绝对坐标。

## Building and Order Tools

### Tool Selection

| Goal | Tool | When to use |
|------|------|-------------|
| Single building | `agent_pointer_select_tool tool=build` + `agent_pointer_left_click` | Default one-off placement |
| Straight floor/wire/pipe/ladder/dig line | `agent_pointer_hold_left` | Default line gesture |
| Cancel/sweep/mop/harvest line | `agent_pointer_select_tool` + `agent_pointer_hold_left` | Default order gesture |
| Dense mixed batch | Break into visible pointer clicks/drags | Complex structures still need player-visible gestures |
| Large rectangle order | `orders_*_area` | Compatibility fallback when pointer drag is impractical |

### Fast Path

For simple low-risk construction, use the direct pointer flow:

```
colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
agent_pointer_jump x=<startX> y=<startY>
agent_pointer_select_tool tool=build prefabId=Tile material=auto priority=5
agent_pointer_hold_left direction=right length=8 confirm=true
agent_pointer_get
```

For broad, risky, multi-phase, or player-marked plans, write the planned calls in the response and dry-run exact actions before execution.

### Build Gestures

Use `agent_pointer_select_tool` to show the chosen building and material on the pointer before placing anything.

Single placement:
```json
{ "name": "agent_pointer_jump", "arguments": { "x": 80, "y": 136, "worldId": 0 } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Bed", "material": "auto", "priority": 5 } }
{ "name": "agent_pointer_left_click", "arguments": { "confirm": true } }
```

Straight utility or platform:
```json
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Wire", "material": "CopperOre", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 12, "confirm": true } }
```

For L-shaped utility routes, split the route into two visible pointer drags. Use `agent_pointer_jump_point_set code=p1` before a multi-step job so the pointer can return to the route start.

### Dig, Sweep, Mop, Harvest, Cancel

Use the matching pointer tool and drag/click the affected cells:

```json
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "dig", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 8, "confirm": true } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "cancel" } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "left", "length": 3, "confirm": true } }
```

- Never use `orders_attack` for excavation. `orders_attack` is only for critters/enemies.
- Use `tool=sweep` only for solid debris/pickupables.
- Use `tool=mop` for water, polluted water, spills, "地上的水", "拖地", and liquid cells on floors.
- Use `tool=cancel` to fix wrong pointer placements before replacing them.

### Dense Batch Policy

Coordinate building batch tools are removed from the public MCP surface. Keep construction visible by decomposing it into pointer gestures:

- Use `buildings_search_defs` and `buildings_materials` for prefab/material validation.
- Place support cells first with `agent_pointer_hold_left` or `agent_pointer_left_click`.
- Place machines, beds, toilets, batteries, and research stations only after the support is visible.
- `orders_*_area confirm=true`: large rectangular orders or emergency fallback when pointer support is missing.
- `buildings_deconstruct id=<id> confirm=true`: allowed when a read tool returns a precise building id; otherwise prefer pointer `tool=deconstruct`.

## Pointer Construction Patterns

### Platform Extension

```
agent_pointer_jump code=home or x/y at platform endpoint
agent_pointer_select_tool tool=build prefabId=Tile material=auto priority=5
agent_pointer_hold_left direction=right length=<cells> confirm=true
agent_pointer_get
```

If natural terrain blocks the line, switch to `tool=dig`, drag the blocking cells, then return to the platform jump point.

### Room Construction

```
game_pause
colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
agent_pointer_jump to room corner
agent_pointer_jump_point_set code=p1
agent_pointer_select_tool tool=dig priority=5
agent_pointer_hold_left for needed clearance lines
agent_pointer_select_tool tool=build prefabId=Tile material=auto priority=5
agent_pointer_hold_left for floor/wall lines
agent_pointer_select_tool tool=build prefabId=<furniture> material=auto priority=5
agent_pointer_left_click for each machine/furniture cell
agent_pointer_get
```

For multi-phase, risky, or player-marked rooms, write the pointer calls in the response and dry-run exact actions before execution.

### Utility Routing

```
agent_pointer_jump to source side or saved p1
agent_pointer_select_tool tool=build prefabId=Wire|GasConduit|LiquidConduit material=auto priority=5
agent_pointer_hold_left direction=<dir> length=<n> confirm=true
agent_pointer_hold_left direction=<dir2> length=<n2> confirm=true
```

Use a targeted utility snapshot only to identify existing ports, buried obstacles, or verification. Do not convert every wire/pipeline into raw coordinate batch calls by default.

## Validation and Safety

### Pre-Build Checks

1. **Pointer anchor check**: `agent_pointer_get` or `agent_pointer_jump` establishes the visible start cell.
2. **Tool check**: `agent_pointer_select_tool` shows the active tool/building/material before click/drag.
3. **Line check**: use one `agent_pointer_hold_left` per horizontal or vertical segment.
4. **Space check**: use a targeted `world_area_snapshot profile=scan encoding=rle` only when terrain, hazards, or ports are unknown.
5. **Building availability**: `buildings_search_defs query=...` confirms `prefabId` exists and is unlocked.
6. **Support check**: place visible support first with the pointer before any OnFloor building.
7. **Confirm check**: map-altering pointer clicks/drags and compatibility dangerous tools require `confirm=true`.
8. **Stop check**: pause and ask before large destructive digs, liquid/heat exposure, combat, or irreversible deconstruction.

### Post-Build Verification

1. Call `agent_pointer_get` to confirm the endpoint and selected tool.
2. Use targeted `world_area_snapshot areaId=<small area> preset=construction profile=scan encoding=rle includeScreenshot=false` when visible confirmation matters.
3. If any blueprint appears wrong, switch pointer to `tool=cancel` and click/drag the wrong cells before replacing.
4. Use `world_cell_info x y` only for a single ambiguous cell.

## Error Recovery

### "Building already exists at position"
- Re-read the small area with `world_area_snapshot includeBuildings=true`.
- If existing building is wrong, select pointer `tool=deconstruct` or use `buildings_deconstruct id=<id> confirm=true` when the id is known.

### "Invalid or not visible cell"
- Cell is outside world bounds or in fog of war
- Use `agent_pointer_jump code=home|p1` or `camera_move mode=jump` to reveal the area, then retry with the pointer.

### "Unsupported OnFloor building"
- The building needs floor/support cells below its footprint
- Place `Tile`/support cells first with the pointer, or move the pointer to existing floor.
- For dense structures, place support cells first, then floor-bound buildings with pointer clicks/drags.

### "Building def not found"
- Call `buildings_search_defs query=...` to find correct `prefabId`
- Common issue: using display name instead of internal `prefabId`

### "No valid material selected"
- The specified `material` tag is not valid for this building
- Omit `material` or use `material=auto`, then check `buildings_materials prefabId=<id> includeUnavailable=true`
- Do not conclude a material is invalid from category names alone; use the returned candidate `categories` and a dry-run placement result

### "Facade 'X' is not available"
- Facade ID is invalid, not unlocked, or not applicable to this building
- Use `default` or omit `facade`

### Dig/deconstruct not completing
- Dupes have not reached the work site
- Use `dupes_status_check` and a targeted small snapshot to find the blocked route.
- Use pointer `tool=dig` to open access, then retry.

### Dry-run validation failed
- Read the returned error and failed item index.
- Fix prefab/material/support/confirm parameters before executing.
- Re-read target state if the failure suggests stale map data.

## Tool Parameter Quick Reference

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| x, y | int | optional | Pointer target when jumping to a known cell |
| dx, dy | int | optional | Relative pointer jump |
| direction, length | string/int | required for drag | Pointer hold-left direction and cell count |
| worldId | int | active | Target asteroid; -1 for all worlds where supported |
| prefabId | string | required for build | Use `buildings_search_defs` to find |
| material | string | auto | Build material shown on the pointer badge |
| priority | int | 5 | 1-9 order/build priority |
| confirm | bool | false | Required for map-altering pointer or dangerous compatibility tools |
| code | string | null | Pointer jump point such as `home`, `p1`, `p2` |
| areaId | string | null | Read/verify region handle, not the normal action surface |
| orientation | string | "Neutral" | Neutral, R90, R180, R270, FlipH |
| facade | string | null | `default` or unlocked facade ID |
| dryRun / validateOnly | bool | false | Validate without placing blueprints |
| allowUnsupported | bool | false | Batch fallback only; avoid for normal play |

## Pointer Examples

### Example 1: Floor Platform
```json
{ "name": "agent_pointer_jump", "arguments": { "x": 10, "y": 20, "worldId": 0 } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Tile", "material": "auto", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 10, "confirm": true } }
```

### Example 2: Mixed Room Interior
```json
{ "name": "agent_pointer_jump", "arguments": { "x": 12, "y": 22, "worldId": 0 } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Bed", "material": "auto", "priority": 5 } }
{ "name": "agent_pointer_left_click", "arguments": { "confirm": true } }
{ "name": "agent_pointer_jump", "arguments": { "dx": 2, "dy": 0 } }
{ "name": "agent_pointer_left_click", "arguments": { "confirm": true } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "FloorLamp", "material": "auto", "priority": 5 } }
{ "name": "agent_pointer_jump", "arguments": { "dx": -1, "dy": 2 } }
{ "name": "agent_pointer_left_click", "arguments": { "confirm": true } }
```

### Example 3: Excavate Then Build
```json
{ "name": "agent_pointer_jump", "arguments": { "x": 10, "y": 20, "worldId": 0 } }
{ "name": "agent_pointer_jump_point_set", "arguments": { "code": "p1" } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "dig", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 10, "confirm": true } }
{ "name": "agent_pointer_jump", "arguments": { "code": "p1" } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Tile", "material": "auto", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 10, "confirm": true } }
```

### Example 4: Multi-Step Power Line Plan
```json
{ "name": "world_area_snapshot", "arguments": { "areaId": "route_A", "preset": "planning", "encoding": "plain" } }
{ "name": "agent_pointer_jump", "arguments": { "x": 10, "y": 20 } }
{ "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Wire", "material": "CopperOre", "priority": 5 } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 10, "confirm": true, "dryRun": true } }
{ "name": "agent_pointer_hold_left", "arguments": { "direction": "right", "length": 10, "confirm": true } }
```
