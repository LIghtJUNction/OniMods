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
Scan    → camera_move + world_area_snapshot preset=planning / layout_candidates → understand space
Plan    → choose candidate rectangle + area_define + buildings_plan* → create build plan
Validate→ dryRun tools first; use plan_harness_validate only for formal plans
Execute → tools_call_many for simple work; plan_harness_execute only for existing formal plans
Verify  → world_text_map + buildings_search_defs → confirm completion
```

## Spatial Operations

### Coordinate System

- Origin: bottom-left of the world
- Grid: integer cells, x increases right, y increases up
- World ID: each asteroid has its own worldId
- Active world: `ClusterManager.Instance.activeWorldId`
- Screenshots are not a coordinate source. They are only visual confirmation.
- Before placing floors, walls, ladders, wires, pipes, or machines, anchor every planned coordinate to `world_area_snapshot`, `world_text_map`, `world_cell_info`, or an `areaId`.

### Grid Alignment Rule

Misaligned blueprints are usually caused by estimating coordinates from screenshots. Avoid this with a strict grid workflow:

1. Read a map first:
   ```
   world_text_map x1 y1 x2 y2 profile=standard encoding=plain includeBuildings=true includeElements=true
   ```
   or:
   ```
   world_area_snapshot x1 y1 x2 y2 preset=construction encoding=plain includeScreenshot=false
   ```
2. Identify the anchor row/column from map coordinates, not from the image. Existing platform tiles are `tile`; natural solids are `sol`; gases/liquids are `oxy/po2/co2/hyd/liq`; buildings are `bld`.
3. For a horizontal platform, use one constant `y` for the whole line: `l: [x1, y, x2, y]`.
4. For a vertical ladder/wall, use one constant `x` for the whole line: `l: [x, y1, x, y2]`.
5. Do not split one straight platform into multiple guessed segments unless all segments share the same exact `y` and touch/overlap intentionally.
6. Never place support tiles from a screenshot rectangle alone. If a screenshot suggests a target, convert it to a map read and derive exact cells first.
7. After `dryRun=true`, inspect `results`/`planned`/`valid` and the requested coordinates. If any line is off by one row/column, stop and re-read the map instead of executing.

For the common "extend existing platform left/right from the printing pod" task:

- Locate the existing platform row from `world_text_map`; it is the row containing a run of `tile` under/near the printing pod.
- Use world absolute coordinates from the map output for build calls.
- Extend left with `{ "p": "Tile", "l": [leftX, platformY, existingLeftX - 1, platformY] }`.
- Extend right with `{ "p": "Tile", "l": [existingRightX + 1, platformY, rightX, platformY] }`.
- If the terrain/ice blocks the route, queue `orders_dig_area` for the blocking natural tiles first; do not shift the floor line up/down to avoid terrain unless the plan explicitly calls for a ramp or step.

### Scanning Workflow

```
camera_get_view        → get current position + zoom + worldId
camera_move mode=jump x=targetX y=targetY zoom=1 → jump to area
world_area_snapshot x1 y1 x2 y2 preset=planning encoding=plain → default base-layout snapshot
  → Use preset=utilities for utility-only power+gas+liquid+shipping+logic overlays
layout_candidates x1 y1 x2 y2 purpose=lab|barracks|bathroom|power|farm → scored room/platform candidates
world_text_map x1 y1 x2 y2 profile=standard encoding=plain → readable terrain/object scan
  → If need objects: add includeBuildings=true, includeItems=true
  → If need elements: add includeElements=true
```

Prefer `world_area_snapshot preset=planning` for room/base layout. It returns base terrain, utility overlays, floor runs, dig runs, hazards, and candidate rectangles. Use `layout_candidates` before choosing room coordinates for labs, barracks, bathrooms, power rooms, or farms. Use `world_text_map` only for very small focused scans. Use screenshots only as visual confirmation; do not infer exact build coordinates from screenshots alone.

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
buildings_plan_rect areaId=bedroom_block ... → build within area
```

`area_define` 返回手工 `a*` areaId；`area_blocks` 返回自动地图块 `b*` areaId。两者都可以在任何接受 `areaId` 参数的工具中复用，替代 `x1/y1/x2/y2`。相邻块可临时写成 `areaId=b1+b2+b3`，或用 `area_merge` 生成新的 `a*`。拼接使用外接矩形，非相邻块会包含中间空隙。对块内施工或订单优先使用地图输出的世界绝对坐标。

## Building Plan Tools

### Tool Selection

| Goal | Tool | When to use |
|------|------|-------------|
| Single building at position | `buildings_plan` | One-off placement |
| Rectangular fill (floor, wall) | `buildings_plan_rect` | Lines, rooms, platforms |
| Mixed batch (different buildings) | `buildings_plan_many` | Complex structures |

### Fast Path

For simple low-risk construction, do not create a `plan_harness`:

```
world_area_snapshot preset=planning encoding=plain
buildings_plan_many dryRun=true confirm=true ...
buildings_plan_many dryRun=false confirm=true ...
world_area_snapshot preset=construction encoding=plain
```

Use `plan_harness` only for broad, risky, multi-phase, user-marked, or resume-later plans.

### buildings_plan

Parameters:
- `prefabId`: building type identifier
- `x`, `y`: world grid position
- `worldId`: target world (default active)
- Optional: `orientation` (Neutral/R90/R180/R270/FlipH), `material`, `facade`, `priority` (1-9)
- Optional: `dryRun` / `validateOnly` to validate without placing
- Optional: `allowUnsupported` to bypass OnFloor support blocking; avoid unless intentionally placing unsupported blueprints
- `confirm`: must be `true`

Example:
```json
{
  "name": "buildings_plan",
  "arguments": {
    "prefabId": "Bed",
    "x": 12, "y": 3, "orientation": "Neutral",
    "confirm": true
  }
}
```

### buildings_plan_rect

Fills an entire rectangle with the same building. Hard limit: 200 cells.

Parameters:
- `prefabId`: building type
- `x1`, `y1`, `x2`, `y2`: world rectangle bounds (inclusive)
- `areaId`: optional, replaces x1/y1/x2/y2 with that area's absolute rectangle
- `worldId`, `material`, `facade`, `priority`
- `dryRun` / `validateOnly` validates without placing
- `allowUnsupported` bypasses OnFloor support blocking; avoid for normal play
- `confirm`: must be `true`

Example:
```json
{
  "name": "buildings_plan_rect",
  "arguments": {
    "prefabId": "Tile",
    "x1": 70, "y1": 132, "x2": 80, "y2": 132,
    "confirm": true
  }
}
```

### buildings_plan_many

Compact batch placement. Supports multiple location formats per item. Default `maxCells`: 500, hard max: 1000.

Top-level parameters:
- `items`: array of plan items
- `routes`: optional utility routes expanded into Wire/LogicWire/GasConduit/LiquidConduit/SolidConduit cells
- `worldId`/`w`, `material`/`m`, `facade`/`f`, `priority`/`pri`, `orientation`/`o`: defaults merged into each item
- `areaId`/`a`: optional area handle for whole-area item placement; item coordinates and routes otherwise use world absolute coordinates
- `detail`: return per-cell results (default false)
- `maxCells`: cell expansion limit
- `dryRun` / `validateOnly`: validate the whole batch without placing
- `allowUnsupported`: bypass OnFloor support blocking; avoid for normal play
- `confirm`: must be `true`

Item location formats (each item must have one):
- `x` + `y`: single cell
- `line` or `l`: `[x1, y1, x2, y2]` horizontal/vertical line only
- `path`, `points`, or `pts`: `[[x,y], ...]` orthogonal polyline; every segment must be horizontal or vertical
- `r`: `[x1, y1, x2, y2]` rectangle
- `cells` or `cs`: `[[x, y], ...]` cell list
- `areaId` or `a`: predefined area handle

Short fields accepted everywhere: `p` = `prefabId`, `w` = `worldId`, `m` = `material`, `f`/`fid` = `facade`/`facadeId`, `pri` = `priority`, `o` = `orientation`.

Use `line/l` for straight floors, ladders, wires, and pipes. Use `path/points` for L-shaped or multi-segment routes. Do not use `r` for a line unless the rectangle is intentionally one cell high or one cell wide; `r` fills the entire rectangle.

For `BuildLocationRule=OnFloor` buildings, the server checks support cells below the footprint. Missing support returns `Unsupported OnFloor building` with `missingSupportCells`. In `buildings_plan_many`, list support tiles before floor-bound buildings so the dry-run can validate against same-batch supports.

Routes connect endpoints in the same call:
```json
{
  "name": "buildings_plan_many",
  "arguments": {
    "items": [
      { "p": "ManualGenerator", "x": 80, "y": 136 },
      { "p": "Battery", "x": 83, "y": 136 },
      { "p": "ResearchCenter", "x": 85, "y": 136 }
    ],
    "routes": [
      { "p": "Wire", "from": { "p": "ManualGenerator", "x": 80, "y": 136, "port": "powerOutput" }, "to": { "p": "ResearchCenter", "x": 85, "y": 136, "port": "powerInput" }, "viaY": 135 }
    ],
    "confirm": true,
    "dryRun": true
  }
}
```
Use `viaX` or `viaY` to control the L-shaped path. Route endpoints and via coordinates are world absolute coordinates. For pipe ports, prefer explicit `[x,y]` endpoints when exact port offsets matter.

Example with mixed formats:
```json
{
  "name": "buildings_plan_many",
  "arguments": {
    "defaults": { "worldId": 0, "orientation": "Neutral" },
    "items": [
      { "prefabId": "Tile", "x": 10, "y": 20 },
      { "p": "Tile", "l": [11, 20, 15, 20] },
      { "p": "Bed", "x": 12, "y": 22 },
      { "p": "Bed", "x": 14, "y": 22 }
    ],
    "confirm": true
  }
}
```

## Excavation and Clearance

### Digging

```
orders_dig_area x1 y1 x2 y2 worldId confirm=true
```
- Issues dig orders for all diggable natural tiles in rectangle
- `confirm=true` required (dangerous tool)
- Does not guarantee completion; dupes must execute
- Skips already-placed dig orders, foundations, and non-solid cells
- Never use `orders_attack` for excavation. `orders_attack` is only for critters/enemies and area attack requires a separate attack confirmation.

### Sweeping

```
orders_sweep_area x1 y1 x2 y2 worldId dryRun=true confirm=true
orders_sweep_area x1 y1 x2 y2 worldId confirm=true priority=5
```
- Marks solid debris/pickupables for sweeping to storage.
- Never use sweep for water, polluted water, or any liquid. For "地上的水", spills, or liquid cells on a floor, use `orders_mop_area`.
- `confirm=true` required when area exceeds 100 cells
- Use `dryRun=true` first if a sweep seems ineffective. Read `inRect`, `marked`, `targets`, and `skipped` to distinguish no debris, stored/equipped items, missing `Clearable`, or wrong world/coordinates.
- Sweep only creates errands; dupes still need reachable storage accepting the item.

### Mopping

```
orders_mop_area x1 y1 x2 y2 worldId confirm=true priority=5
```
- Marks water/polluted water/liquid cells on a floor for mopping.
- Use this for user wording like "地上的水", "拖地", "漏水", "液体", "water", "liquid", or "spill".
- Mop skips cells without floor support or liquid above the game's mop mass limit.
- If `orders_sweep_area` returns `liquidCellsInRect > 0` or a `mopHint`, switch to `orders_mop_area`; do not repeat sweep with the same area.

### Deconstruction

```
buildings_deconstruct id=X confirm=true   → single building by instance ID
buildings_deconstruct x=X y=Y confirm=true → building at coordinate
```
- `confirm=true` required (dangerous tool)
- Also accepts `areaId`, `prefabId`, `query` for lookup
- Bulk via `tools_call_many`:

```json
{
  "name": "tools_call_many",
  "arguments": {
    "items": [
      { "name": "buildings_deconstruct", "arguments": { "id": 1, "confirm": true } },
      { "name": "buildings_deconstruct", "arguments": { "id": 2, "confirm": true } }
    ]
  }
}
```

### Cancel Orders

```
orders_cancel_area x1 y1 x2 y2 worldId confirm=true
```
- Cancels player-placed orders: dig, build, deconstruct, sweep, harvest, attack, capture
- `confirm=true` required when area exceeds 100 cells

## Batch Construction Workflow

### Pattern: Room Construction

```
Step 1: Scan
  world_text_map x1 y1 x2 y2 profile=standard encoding=plain
  → Identify existing terrain, buildings, items

Step 2: Clear (if needed)
  orders_dig_area x1 y1 x2 y2 confirm=true
  orders_sweep_area x1 y1 x2 y2 confirm=true  # solid debris only
  orders_mop_area x1 y1 x2 y2 confirm=true    # water/liquid only
  → Poll with world_text_map to check completion

Step 3: Define Area
  area_define x1 y1 x2 y2 label="room_A"

Step 4: Plan Walls/Floor
  buildings_plan_rect prefabId=Tile x1 y1 x2 y2 confirm=true
  → Or use buildings_plan_many with r (rect) items for mixed materials

Step 5: Plan Interior
  buildings_plan_many
    defaults: { worldId: 0 }
    items:
      - { prefabId: Bed, x: 12, y: 22 }
      - { prefabId: Bed, x: 14, y: 22 }

Step 6: Validate
  buildings_plan_many dryRun=true
    items ordered with Tile/support first, then furniture/machines
  plan_harness_create objective="Build room_A" areaId="room_A"
  plan_harness_record id=p0001 stage=plan summary="Room build plan"
    payload: { plannedCalls: [...] }
  plan_harness_validate id=p0001
  → Checks: tool exists? required params present? dangerous tools have confirm?

Step 7: Execute
  plan_harness_execute id=p0001 confirm=true
  → Or repeat the validated buildings_plan_many with dryRun=false

Step 8: Verify
  world_text_map areaId=room_A includeBuildings=true
  → Confirm buildings appear in planned positions
```

### Pattern: Utility Routing

```
Step 1: Path Planning
  world_text_map with includeBuildings=true
  → Identify existing pipes/wires from object list
  → Plan route coordinates avoiding obstacles

Step 2: Clear Path
  orders_dig_area for buried route segments confirm=true

Step 3: Plan Infrastructure
  buildings_plan_many
    defaults: { worldId: 0, orientation: "Neutral" }
    items:
      - { prefabId: Wire, x: 10, y: 20 }
      - { prefabId: Wire, x: 11, y: 20 }
      - { prefabId: Wire, line: [12, 20, 20, 20] }

Step 4: Execute
  tools_call_many
    defaults: { confirm: true }
    items: [ ... ]
```

## Validation and Safety

### Pre-Build Checks

1. **Map anchor check**: `world_text_map`/`world_area_snapshot` provides exact `x/y`; never use screenshot-only coordinates.
2. **Line alignment check**: horizontal lines have `y1 == y2`; vertical lines have `x1 == x2`; extensions of one platform share the same `platformY`.
3. **Space check**: `world_text_map` confirms area is clear or planned clearance is complete.
4. **Building availability**: `buildings_search_defs query=...` confirms `prefabId` exists and is unlocked.
5. **Support check**: run `buildings_plan* dryRun=true`; OnFloor buildings must have existing or same-batch prior support tiles.
6. **Dry-run coordinate check**: compare dry-run result coordinates against the intended map row/column before executing.
7. **Confirm check**: dangerous tools (`orders_dig_area`, `buildings_deconstruct`, `plan_harness_execute`) require `confirm: true`.
8. **Cell limits**: `buildings_plan_rect` refuses >200 cells; `buildings_plan_many` refuses >maxCells (default 500, max 1000); `tools_call_many` max 20 calls.

### Post-Build Verification

1. Re-read the same area with `world_text_map profile=standard encoding=plain includeBuildings=true`.
2. Confirm every planned floor/wall/ladder line appears on the intended constant `x` or `y`.
3. If any blueprint appears one row/column off, cancel the wrong cells with `orders_cancel_area` before placing replacements.
4. `buildings_search_defs` → verify `prefabId` if placement failed with "Building def not found".
5. `world_cell_info x y` → inspect a single cell for element/overlay details.

## Error Recovery

### "Building already exists at position"
- Re-scan with `world_text_map includeBuildings=true`
- If existing building is wrong: `buildings_deconstruct` with `id` or `x`/`y`, then retry

### "Invalid or not visible cell"
- Cell is outside world bounds or in fog of war
- Use `camera_move mode=jump x=... y=...` to ensure area is revealed

### "Unsupported OnFloor building"
- The building needs floor/support cells below its footprint
- Add `Tile`/support cells first, or move the building onto existing floor
- For `buildings_plan_many`, put support items earlier than beds, generators, batteries, research stations, toilets, and other floor-bound buildings

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
- `world_text_map` shows if terrain blocks access path
- `orders_dig_area` to clear access, then retry

### Plan harness gates failed
- `plan_harness_execute` requires prior `plan` + `feedback` + `verification` stages
- Record each stage with `plan_harness_record` before execute
- Use `overrideGate=true` only for emergency bypass

## Tool Parameter Quick Reference

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| x, y | int | required | Grid coordinates |
| x1, y1, x2, y2 | int | required* | Rectangle bounds (inclusive); *omitted if areaId provided |
| worldId | int | active | Target asteroid; -1 for all worlds where supported |
| prefabId | string | required | Use `buildings_search_defs` to find |
| confirm | bool | false | Required for dangerous tools |
| areaId | string | null | Reuse predefined area from `area_define` |
| orientation | string | "Neutral" | Neutral, R90, R180, R270, FlipH |
| material | string | null | Default material if omitted |
| facade | string | null | `default` or unlocked facade ID |
| priority | int | 5 | 1-9 build order priority |
| dryRun / validateOnly | bool | false | Validate without placing blueprints |
| allowUnsupported | bool | false | Bypass OnFloor support validation; avoid for normal play |
| label | string | null | Human-readable tag for `area_define` |
| maxCells | int | 500 | `buildings_plan_many` expansion limit |
| line / l | array | null | Straight horizontal/vertical segment `[x1,y1,x2,y2]` |
| path / points / pts | array | null | Orthogonal polyline `[[x,y],...]` |

## Batch Composition Examples

### Example 1: Floor Platform
```json
{
  "calls": [
    {
      "name": "buildings_plan_rect",
      "arguments": {
        "prefabId": "Tile",
        "x1": 10, "y1": 20, "x2": 20, "y2": 20,
        "worldId": 0,
        "confirm": true
      }
    }
  ]
}
```

### Example 2: Mixed Room Interior
```json
{
  "calls": [
    {
      "name": "buildings_plan_many",
      "arguments": {
        "defaults": { "worldId": 0 },
        "items": [
          { "prefabId": "Bed", "x": 12, "y": 22 },
          { "prefabId": "Bed", "x": 14, "y": 22 },
          { "prefabId": "FloorLamp", "x": 13, "y": 24 }
        ],
        "confirm": true
      }
    }
  ]
}
```

### Example 3: Excavate Then Build
```json
{
  "calls": [
    {
      "name": "orders_dig_area",
      "arguments": {
        "x1": 10, "y1": 20, "x2": 20, "y2": 30,
        "confirm": true
      }
    },
    {
      "name": "orders_sweep_area",
      "arguments": {
        "x1": 10, "y1": 20, "x2": 20, "y2": 30,
        "confirm": true
      }
    },
    {
      "name": "buildings_plan_rect",
      "arguments": {
        "prefabId": "Tile",
        "x1": 10, "y1": 20, "x2": 20, "y2": 20,
        "confirm": true
      }
    }
  ]
}
```

### Example 4: Plan Harness Full Flow
```json
// Create
{ "name": "plan_harness_create", "arguments": { "objective": "Build power line", "areaId": "route_A" } }

// Record plan
{ "name": "plan_harness_record", "arguments": { "id": "p0001", "stage": "plan", "summary": "Wire route", "payload": { "plannedCalls": [{"name":"buildings_plan_many","arguments":{"items":[{"p":"Wire","r":[10,20,20,20]}],"confirm":true}}] } } }

// Record feedback
{ "name": "plan_harness_record", "arguments": { "id": "p0001", "stage": "feedback", "summary": "Area scanned, path clear" } }

// Record verification
{ "name": "plan_harness_record", "arguments": { "id": "p0001", "stage": "verification", "summary": "Params valid", "passed": true } }

// Validate
{ "name": "plan_harness_validate", "arguments": { "id": "p0001" } }

// Execute
{ "name": "plan_harness_execute", "arguments": { "id": "p0001", "confirm": true } }
```

### Example 5: Compact Short-Field Batch
```json
{
  "name": "buildings_plan_many",
  "arguments": {
    "defaults": { "w": 0, "o": "Neutral" },
    "items": [
      { "p": "Tile", "l": [10, 20, 20, 20] },
      { "p": "Ladder", "l": [10, 21, 10, 25] },
      { "p": "Bed", "x": 12, "y": 22 },
      { "p": "Bed", "x": 14, "y": 22 }
    ],
    "confirm": true
  }
}
```
