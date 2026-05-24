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
Scan    → camera_move + world_text_map → understand space
Plan    → area_define + buildings_plan* → create build plan
Validate→ plan_harness_validate → check params + confirm
Execute → plan_harness_execute or tools_call_many → issue orders
Verify  → world_text_map + buildings_search_defs → confirm completion
```

## Spatial Operations

### Coordinate System

- Origin: bottom-left of the world
- Grid: integer cells, x increases right, y increases up
- World ID: each asteroid has its own worldId
- Active world: `ClusterManager.Instance.activeWorldId`

### Scanning Workflow

```
camera_get_view        → get current position + zoom + worldId
camera_move mode=jump x=targetX y=targetY zoom=1 → jump to area
world_text_map x1 y1 x2 y2 profile=scan encoding=rle → minimal terrain scan
  → If need objects: add includeBuildings=true, includeItems=true
  → If need elements: add includeElements=true
```

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
world_text_map areaId=bedroom_block profile=scan → reuse area
buildings_plan_rect areaId=bedroom_block ... → build within area
```

`area_define` 返回的 areaId 可以在任何接受 `areaId` 参数的工具中复用，替代 `x1/y1/x2/y2`。

## Building Plan Tools

### Tool Selection

| Goal | Tool | When to use |
|------|------|-------------|
| Single building at position | `buildings_plan` | One-off placement |
| Rectangular fill (floor, wall) | `buildings_plan_rect` | Lines, rooms, platforms |
| Mixed batch (different buildings) | `buildings_plan_many` | Complex structures |

### buildings_plan

Parameters:
- `prefabId`: building type identifier
- `x`, `y`: grid position
- `worldId`: target world (default active)
- Optional: `orientation` (Neutral/R90/R180/R270/FlipH), `material`, `facade`, `priority` (1-9)
- `confirm`: must be `true`

Example:
```json
{
  "name": "buildings_plan",
  "arguments": {
    "prefabId": "Bed", "x": 12, "y": 22,
    "worldId": 0, "orientation": "Neutral",
    "confirm": true
  }
}
```

### buildings_plan_rect

Fills an entire rectangle with the same building. Hard limit: 200 cells.

Parameters:
- `prefabId`: building type
- `x1`, `y1`, `x2`, `y2`: rectangle bounds (inclusive)
- `areaId`: optional, replaces x1/y1/x2/y2
- `worldId`, `material`, `facade`, `priority`
- `confirm`: must be `true`

Example:
```json
{
  "name": "buildings_plan_rect",
  "arguments": {
    "prefabId": "Tile", "x1": 10, "y1": 20, "x2": 20, "y2": 20,
    "worldId": 0, "confirm": true
  }
}
```

### buildings_plan_many

Compact batch placement. Supports multiple location formats per item. Default `maxCells`: 500, hard max: 1000.

Top-level parameters:
- `items`: array of plan items
- `worldId`/`w`, `material`/`m`, `facade`/`f`, `priority`/`pri`, `orientation`/`o`: defaults merged into each item
- `detail`: return per-cell results (default false)
- `maxCells`: cell expansion limit
- `confirm`: must be `true`

Item location formats (each item must have one):
- `x` + `y`: single cell
- `r`: `[x1, y1, x2, y2]` rectangle
- `cells` or `cs`: `[[x, y], ...]` cell list
- `areaId` or `a`: predefined area handle

Short fields accepted everywhere: `p` = `prefabId`, `w` = `worldId`, `m` = `material`, `f`/`fid` = `facade`/`facadeId`, `pri` = `priority`, `o` = `orientation`.

Example with mixed formats:
```json
{
  "name": "buildings_plan_many",
  "arguments": {
    "defaults": { "worldId": 0, "orientation": "Neutral" },
    "items": [
      { "prefabId": "Tile", "x": 10, "y": 20 },
      { "p": "Tile", "r": [11, 20, 15, 20] },
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

### Sweeping

```
orders_sweep_area x1 y1 x2 y2 worldId confirm=true
```
- Marks debris for sweeping
- `confirm=true` required when area exceeds 100 cells

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
  world_text_map x1 y1 x2 y2 profile=scan
  → Identify existing terrain, buildings, items

Step 2: Clear (if needed)
  orders_dig_area x1 y1 x2 y2 confirm=true
  orders_sweep_area x1 y1 x2 y2 confirm=true
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
  plan_harness_create objective="Build room_A" areaId="room_A"
  plan_harness_record id=p0001 stage=plan summary="Room build plan"
    payload: { plannedCalls: [...] }
  plan_harness_validate id=p0001
  → Checks: tool exists? required params present? dangerous tools have confirm?

Step 7: Execute
  plan_harness_execute id=p0001 confirm=true
  → Or tools_call_many with dryRun=true first

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
      - { prefabId: Wire, r: [12, 20, 20, 20] }

Step 4: Execute
  tools_call_many
    defaults: { confirm: true }
    items: [ ... ]
```

## Validation and Safety

### Pre-Build Checks

1. **Space check**: `world_text_map` confirms area is clear or planned clearance is complete
2. **Building availability**: `buildings_search_defs query=...` confirms `prefabId` exists and is unlocked
3. **Confirm check**: dangerous tools (`orders_dig_area`, `buildings_deconstruct`, `plan_harness_execute`) require `confirm: true`
4. **Cell limits**: `buildings_plan_rect` refuses >200 cells; `buildings_plan_many` refuses >maxCells (default 500, max 1000); `tools_call_many` max 20 calls

### Post-Build Verification

1. `world_text_map includeBuildings=true` → buildings appear at planned coordinates
2. `buildings_search_defs` → verify `prefabId` if placement failed with "Building def not found"
3. `world_cell_info x y` → inspect a single cell for element/overlay details

## Error Recovery

### "Building already exists at position"
- Re-scan with `world_text_map includeBuildings=true`
- If existing building is wrong: `buildings_deconstruct` with `id` or `x`/`y`, then retry

### "Invalid or not visible cell"
- Cell is outside world bounds or in fog of war
- Use `camera_move mode=jump x=... y=...` to ensure area is revealed

### "Building def not found"
- Call `buildings_search_defs query=...` to find correct `prefabId`
- Common issue: using display name instead of internal `prefabId`

### "No valid material selected"
- The specified `material` tag is not valid for this building
- Omit `material` to use default, or check `buildings_search_defs` output for `materialCategories`/`defaultMaterials`

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
| label | string | null | Human-readable tag for `area_define` |
| maxCells | int | 500 | `buildings_plan_many` expansion limit |

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
      { "p": "Tile", "r": [10, 20, 20, 20] },
      { "p": "Ladder", "r": [10, 21, 10, 25] },
      { "p": "Bed", "x": 12, "y": 22 },
      { "p": "Bed", "x": 14, "y": 22 }
    ],
    "confirm": true
  }
}
```
