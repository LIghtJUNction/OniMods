---
name: oni-mcp-control
description: MCP control skill for Oxygen Not Included. Defines the control loop (sense → analyze → decide → act → verify), tool calling strategies, parameter selection, batch patterns, and error recovery. Use when controlling ONI through MCP tools.
---

# ONI MCP Control

## When to use

Use when the user wants to **control ONI via MCP tools**, not when they want game tips.

## Control Model

### The OODA Loop for ONI MCP

```
Observe (Sense)    → colony_control domain=snapshot action=get profile=minimal delta=true + targeted small reads → get state snapshot
Orient (Analyze)   → parse JSON, identify gaps/anomalies
Decide (Plan)      → select target state, choose tools
Act (Execute)      → call write/execute tools with confirm
Verify (Check)     → re-call read tools, compare before/after
```

Every control action must complete the full loop. Never act without prior observation, never stop without verification.

## Semantic Tool Policy

Use `navigation_control` only for camera movement, focus, overlays, and screenshots. Game actions use semantic aggregate tools and virtual-file edits.

Rules:
- Use semantic MCP tools with explicit task text for build, dig, cancel, sweep, mop, disinfect, harvest, and deconstruct actions.
- Legacy coordinate building tools remain compatibility/debug paths. Prefer public aggregate tools (`building_control`, `orders_control`, `world_editor`) and verify schemas through `server_control domain=catalog`.
- For wires, pipes, conveyors, logic wires, and travel tubes, use layered utility designation instead of generic building deconstruct: `orders_control domain=designation action=cut_conduits type=wire|liquid|gas|solid|logic|travel_tube ... confirm=true`.
- For normal buildings, prefer object-specific deconstruction: `orders_control domain=designation action=deconstruct id=<instanceId> confirm=true`.
- If only a cell is known and multiple objects share it, list or dry-run first, inspect returned candidates, then choose the exact `id` or explicit utility `type`.
- For virtual infrastructure edits such as `power.md`, prefer coordinate-stable explicit cells: `world_editor action=edit ... editCells=[{"x":85,"y":148,"value":"拆"}]`.

### Control Modes

| Mode | Trigger | Primary Tools | Output |
|------|---------|--------------|--------|
| **Diagnostic** | User asks "what's wrong" | `colony_control domain=diagnostic action=diagnostics`, `colony_control domain=diagnostic action=alerts`, `read_control domain=resources action=food`, `dupes_control domain=info action=needs`, `read_control domain=infrastructure action=power_summary`, `read_control domain=world action=thermal_overheat_risk` | Problem list + severity |
| **Planning** | User asks "what should I do" | `server_control domain=catalog action=guide`, compact reads, dry-run tools | Short plan with explicit validation steps |
| **Execution** | User says "do it" | `server_control domain=batch action=call_many`, individual write/execute tools | Execution result + task IDs |
| **Monitoring** | User wants status update | `colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts` | Snapshot diff |

Mode transitions: Diagnostic → Planning → Execution → Monitoring. If Execution fails, fall back to Diagnostic.

## Tool Selection Strategy

### Read vs Write vs Execute

- **Read** (`colony_*`, `dupes_*`, `read_control domain=resources/world/infrastructure`, `world_*`) → 获取状态，无副作用，可缓存
- **Write** (`dupes_control domain=priority action=set`, `colony_control domain=management kind=schedule action=set_block`, `building_control domain=storage action=set_filter`) → 修改配置，需 `confirm=true`（medium risk）
- **Execute** (`game_control domain=speed action=pause`, `navigation_control action=move`, `orders_dig`, `orders_control domain=designation action=deconstruct`) → 对游戏下达动作，dangerous 需 `confirm=true`

Mode is inferred from tool name: `get_*` / `list_*` → read; `set_*` / `configure_*` → write; `move_*` / `pause_*` / `focus_*` → execute.

### Resource vs Tool vs Prompt

- **Resource** (`oni://...`) → 固定快照，适合重复读取的参考数据（inventory, schedules, dupes list）
- **Tool** (`tools/call`) → 交互式，带参数，适合针对性查询和动作
- **Prompt** → 启动标准化工作流（`power_audit`, `rooms_overview`, `thermal_audit`）

Resource URIs are idempotent and cacheable. Tools with parameters are not. Prompts orchestrate multiple resources and tools in sequence.

### When to use which discovery tool

| Goal | Use |
|------|-----|
| "What tools exist?" | `server_control domain=catalog action=manifest` (full) or `server_control domain=catalog action=search detail=brief` (filtered) |
| "What can I do about X?" | `server_control domain=catalog action=guide goal=X` |
| "Am I missing coverage?" | `server_control domain=catalog action=coverage` |
| "Is this tool safe?" | `server_control domain=catalog action=static_audit` |

`server_control domain=catalog action=manifest` exposes the compact 8-tool core surface; `detail=full` expands the parameters for those public aggregate tools. Legacy fine-grained tools are hidden compatibility entrypoints and should not be preferred.

### Anti-Loop Guardrails

- Do not call the same read tool with identical arguments twice in a row. If the first result is insufficient, name the missing field and switch to a targeted tool, `server_control domain=catalog action=search`, or a user question.
- Do not substitute a nearby-sounding tool for a different job. Example: `dupes_control domain=info action=status_check` is for health/pathing, not names, attributes, or batch rename.
- Before using a tool name that is not visible in the current tool set, call `server_control domain=catalog action=search query=<action>` or `server_control domain=catalog action=manifest group=<category>` and follow the returned schema.
- Keep simple requests simple: a one-step configuration task should not trigger colony snapshots, maps, screenshots, or unrelated audits unless the result depends on them.
- After any zero-effect write/execute result (`marked=0`, no changed rows, missing target), do not repeat the same call. Read the result hint, re-read the smallest relevant context, then change tool or parameters.

## Observation: Camera, Views, Screenshots, Maps

Spatial ONI control starts with semantic search, virtual-file reads, or a compact structured map. Use camera tools only when a visual view or screenshot materially helps. Do not make raw coordinates the primary action interface.

### Camera Navigation

Use camera tools when the user asks to look somewhere, when you need a screenshot of a specific region, or when a visual overlay matters:

```
navigation_control action=get_view
  → read current position, zoom, activeWorldId, screen size

navigation_control action=focus_cell
  x, y, worldId?, zoom?
  → center a known grid cell

navigation_control action=focus_dupe
  id/name
  → follow a duplicant

navigation_control action=move
  mode: jump | pan
  x/y for jump, dx/dy for pan, zoom?
  → move to a coordinate or nudge the view

navigation_control action=set_active_world
  worldId
  → switch asteroid/rocket interior before reading or viewing that world
```

After moving the camera, call `navigation_control action=get_view` if exact view confirmation matters. For map-data tools, moving the camera is optional when you pass explicit `x1,y1,x2,y2`; it is required only for screenshots or default camera-centered map reads.

### View / Overlay Switching

Use `navigation_control action=switch_view` for visual overlays and optional screenshot capture:

```
navigation_control action=switch_view
  view: none | oxygen | power | gas_conduits | liquid_conduits | solid_conveyor | logic | temperature | heat_flow | materials | light | decor | rooms | priorities | disease | radiation | sound | suit | crop | harvest
  screenshot: true|false
```

Rules:
- Use `view=none` before a normal visual screenshot.
- Use `view=oxygen`, `temperature`, `rooms`, `decor`, `crop`, `harvest`, `priorities`, `disease`, etc. when the task depends on visual overlay colors/icons.
- For power, gas pipes, liquid pipes, conveyors, and logic wires, prefer `read_control domain=world action=area_snapshot` or `read_control domain=world action=text_map view=...` for coordinate-accurate planning; use `navigation_control action=switch_view` only to visually confirm.
- `navigation_control action=switch_view screenshot=true` is the shortest way to switch overlay and capture the current screen in one call.

### Screenshot Use

Use `navigation_control action=screenshot` only after the camera and overlay are set correctly. Screenshot is useful for:

- confirming the player-facing visual state
- room/decor/crop/harvest/priority/disease overlays
- UI state that is not represented by world maps
- comparing what the user sees with structured map data

Screenshot is weak for exact coordinates. If a decision affects digging, building, sweeping, mopping, wiring, piping, or deconstruction, read `read_control domain=world action=area_snapshot` or `read_control domain=world action=text_map` before acting.

### Semantic Build Discipline

For any build/dig/deconstruct action, observe first, then call the matching semantic aggregate directly.

Rules:
- Start with `world_editor command=read`/`command=edit`, or `building_control domain=planning action=search_defs/materials/placement_candidates`.
- Treat each placement anchor as the `lowerLeftCell`; do not use screenshot center, tooltip position, or blueprint center as the anchor.
- For horizontal/vertical tiles and ladders, use `building_control domain=planning action=build_area` with explicit lower-left anchors. Use points only for wire, conduit, or rail utility routes.
- For furniture, machines, or any multi-cell footprint, use `build_area` with one lower-left anchor per placement.
- Preflight with `preview` or `dryRun=true`, then execute with `confirm=true`.
- Dig, sweep, mop, disinfect, cancel, harvest, capture, and deconstruct through `orders_control` area/designation actions; never simulate UI clicks.
- After execution, verify with a targeted virtual-file read, `area_snapshot`, or `text_map`.

### World Map Reads

Use `read_control domain=world action=area_snapshot` when virtual-file reads, camera context, and compact status do not provide enough spatial context:

```
read_control domain=world action=area_snapshot
  x1, y1, x2, y2, worldId?
  preset: terrain | construction | utilities | planning | all
  encoding: rle
  includeScreenshot: false
```

Presets:
- `terrain`: base terrain only
- `construction`: terrain + power overlay; good default for build/dig planning
- `utilities`: terrain + power/gas/liquid/conveyor/logic overlays
- `planning`: utilities + layout candidates/planning summary
- `all`: utilities + screenshot; only use when visual confirmation is also needed

Use `read_control domain=world action=text_map` for narrower or more controlled reads:

```
read_control domain=world action=text_map
  x1, y1, x2, y2, worldId?
  profile: standard | minimal | scan
  encoding: plain | rle | both
  view: base | temperature | power | gas_conduits | liquid_conduits | solid_conveyor | logic
  includeBuildings/includeDupes/includeItems/includeElements?
  detail: compact | full
```

Rules:
- Prefer `chunksOnly=true` for large areas, then read one returned block with `profile=scan encoding=rle`.
- For humans/debugging: `profile=standard encoding=plain includeElements=true`.
- Default for targeted verification: `profile=scan encoding=rle`.
- For very large low-token scans only: `profile=scan encoding=rle`.
- Treat the returned `areaId` as the handle for the exact area you read. Rows/columns also show relative `ry/rx`, but build/order tools should use world absolute coordinates from the map output.
- When editing the same area, convert any `rx/ry` notes back to world `x/y` using the map origin before calling build/order tools. Example: if origin is `(70,130)`, relative row `ry=2` becomes world `y=132`.
- Standard text rows use fixed-width tokens, not single-letter cells: `sol` natural solid, `tile` constructed tile, `oxy/po2/co2/hyd` gases, `liq` liquid, `bld/dup/itm/bp` overlays.
- For exact per-cell data: `detail=full` on a small rectangle.
- For utility coordinate checks: use `view=power`, `view=gas_conduits`, `view=liquid_conduits`, `view=solid_conveyor`, or `view=logic`; these are sparse overlay maps.
- For heat checks, use `view=temperature`; tokens are `frz/cold/mild/hot/xhot`.

### Recommended Observation Order

For a spatial task:

```
game_control domain=speed action=pause
colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
world_editor command=read path=/active/map/viewport.md when spatial context is needed
targeted read_control domain=world action=area_snapshot only if terrain/hazard context is missing
```

If the user gives an area or edit marker, use that area directly; do not waste time moving the camera before reading the map.

## Standard Control Sequences

### Sequence 1: Colony Health Check

```
colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
colony_control domain=snapshot action=get profile=brief only when minimal flags a concern
read_control domain=resources action=food → only when food detail is needed
read_control domain=infrastructure action=power_summary → generation vs load per circuit
read_control domain=world action=thermal_overheat_risk → overheat risk sorted by delta-T
read_control domain=infrastructure action=rooms → room coverage summary
→ Parse JSON, compare against thresholds, flag anomalies
```

Use `detail=compact` on all calls for a sub-5-second scan. Escalate to `detail=full` only when an anomaly is flagged.

### Sequence 2: Fast Execute A Simple Plan

Use this for low-risk, small-scope actions such as a short dig, mop, sweep, floor line, config batch, or dry-run-passed blueprint:

```
server_control domain=batch action=call_many
  dryRun: true
  responseMode: summary
  requireAllValid: true
  stopOnError: true
  items: [...]

server_control domain=batch action=call_many
  dryRun: false
  responseMode: summary
  requireAllValid: true
  stopOnError: true
  items: [...]

verify with colony_control domain=snapshot action=get, read_control domain=world action=area_snapshot, or targeted read
```

For map actions, the `items` should use semantic `building_control` or `orders_control` calls with bounded areaId/query/anchors/points. Do not create separate planning records for trivial single-step actions. It adds latency without improving safety.

### Sequence 3: Execute A Complex Plan

Use this only for complex/risky/multi-phase/player-marked plans:

```
1. Write the planned tool calls in the response with key arguments.
2. Run exact dryRun/validateOnly calls where supported.
3. Inspect errors and revise the calls.
4. Execute only after the user has authorized the action.
5. Verify with targeted read tools.
```

If `validate` reports missing `confirm`, inject it into defaults or the corresponding `plannedCalls` entry and re-validate. If it reports coordinate anchoring issues, convert the plan to world absolute coordinates before any dry-run or execution. Never skip validation before execution.

### Sequence 4: Batch Configuration

```
server_control domain=batch action=call_many
  dryRun: true
  responseMode: summary
  defaults: { confirm: true }
  items:
    - { t: dupes_control, a: { domain: priority, action: set, id: 1, choreGroup: Dig, priority: 4 } }
    - { t: dupes_control, a: { domain: priority, action: set, id: 2, choreGroup: Build, priority: 4 } }
    - { t: colony_control, a: { domain: management, kind: schedule, action: set_block, schedule: "Default", hour: 3, group: Sleep } }
    ...
  requireAllValid: true
  stopOnError: true
```

If `dryRun` passes, re-call with `dryRun: false` to execute. `defaults` merges into every item. Use low-token shorthand (`t` / `a`) for large batches. Max 20 items per call. Keep `responseMode=summary` for normal batches; use `responseMode=errors` for retry loops and `responseMode=full` only when exact child payloads are needed.

### Sequence 5: Spatial Analysis + Action

```
game_control domain=speed action=pause → freeze state for complex or destructive spatial plans
read_control domain=world action=area_snapshot
  x1, y1, x2, y2
  preset: construction | utilities
  encoding: rle
  includeScreenshot: false
→ Preferred coordinate-accurate context for build/dig/power/pipe planning
read_control domain=world action=text_map
  x1, y1, x2, y2
  profile: standard
  encoding: plain
  includeElements: true
→ Use directly for human-readable terrain/object debugging
navigation_control action=focus_cell / navigation_control action=move
navigation_control action=switch_view
navigation_control action=screenshot
→ Optional visual confirmation only after structured maps
→ Define reusable area: read_control domain=area action=define → areaId when the same region will be reused
→ Plan: choose semantic target/areaId and building_control or orders_control action
→ Execute: build_area with one or more lower-left anchors, or the matching area/designation action after dry-run
→ Verify: read_control domain=world action=area_snapshot or read_control domain=world action=text_map over the same area
```

Use `read_control domain=world action=area_snapshot` for normal spatial planning because it keeps base terrain, objects, and utility overlays in one response. Use `read_control domain=world action=text_map profile=standard encoding=plain` by default; reserve `profile=scan encoding=rle` for very large low-token terrain scans. Use screenshots only for visual confirmation, room/decor/crop/UI overlay interpretation, or when text maps cannot express the visual state.

### Sequence 6: Duplicant Management

```
colony_control domain=read action=dupes             → all dupes with ID, name, world, stress
dupes_control domain=info action=detail → single dupe: attributes, traits, skills
dupes_control domain=info action=needs  → needs, stress sources, morale
dupes_orders_control domain=priority action=list  → current chore priorities
→ Identify gaps
→ Batch update: server_control domain=batch action=call_many with set_personal_priority items
→ Verify: dupes_orders_control domain=priority action=list
```

Always fetch `colony_control domain=read action=dupes` first to resolve name → ID mapping. Many dupe tools require numeric `id`, not name.

## Parameter Selection Rules

### worldId
- Default: current active world (omit or pass null)
- `-1`: all worlds (for summary scans across asteroid clusters)
- Specific ID: target that world only

### limit
- Default is usually 50/80
- Use lower for quick scans, higher for audits
- Never exceed max (usually 200–300)
- If result is truncated, use pagination via offset or narrower filters

### detail / encoding / profile
- `detail=brief` or `detail=compact` → low token, quick decisions
- `detail=full` → deep analysis, more tokens
- `detail=summary` → balanced
- `profile=standard` + `encoding=plain` → default readable terrain/object map
- `profile=scan` + `encoding=rle` → very large low-token terrain scan only
- `format=json` → structured data for machine-readable planning and validation
- `server_control domain=batch action=call_many responseMode=summary` → compact per-call status without nested full payloads

### confirm
- `confirm: true` required for all medium/dangerous write tools
- `server_control domain=batch action=call_many` with `dryRun: true` pre-validates confirm requirements
- Use `building_control domain=planning action=materials/search_defs/placement_candidates/preview` before construction; execute placement through `build_area`, using one lower-left anchor for a single building.
- Never bypass confirm for dangerous tools (`orders_dig`, `orders_deconstruct`, `orders_sweep`)
- Do not repeat the same write/execute tool with identical coordinates after a zero-effect result. Read the result fields, re-read state if needed, then choose a different tool or corrected parameters.
- Use `orders_control domain=area action=sweep` only for solid debris/pickupables. Use `orders_control domain=area action=mop` for water, polluted water, spills, "地上的水", or other liquid cells on a floor.

### x1, y1, x2, y2 (area coordinates)
- Always specify in world cell coordinates
- `x1 < x2`, `y1 < y2` (origin is bottom-left)
- Large areas → high token cost; shrink the rectangle first, and use `encoding=rle` only when token budget matters more than readability

## Batch and Efficiency Patterns

### Pattern A: Read-Before-Write

Always read current state before modifying:

```
BAD:  directly set schedule values without first reading the current schedule
GOOD: colony_control domain=management kind=schedule action=list → identify target schedule name/hour/group
      colony_control domain=management kind=schedule action=set_block → colony_control domain=management kind=schedule action=list to verify
```

### Pattern B: Area-Based Operations

1. `read_control domain=area action=define` with `x1,y1,x2,y2,name` → get `a*` areaId for a hand-picked region
   or `read_control domain=area action=blocks worldId=<id>` → get `b*` areaIds for automatic world chunks
2. Reuse `areaId` in subsequent calls (`read_control domain=world action=text_map?areaId=xyz`)
   or temporarily join adjacent blocks with `areaId=b1+b2+b3`
3. For repeated use, call `read_control domain=area action=merge areaIds=[...]` to create one merged `a*` handle
4. Execute bulk work with the matching semantic area tool; use dry-run, bounded areaId, and returned risk details before confirm
5. Clean up temporary `a*` areas with `read_control domain=area action=forget` when no longer needed; keep `b*` blocks while scanning the world

### Pattern C: Differential Updates

1. Read full list (`colony_control domain=read action=dupes`, `colony_control domain=management kind=schedule action=list`, `read_control domain=buildings action=summary`)
2. Compute delta locally (don't diff on server)
3. Send only changes via `server_control domain=batch action=call_many`

### Pattern D: Parallel Reads

Use `server_control domain=batch action=call_many` for independent read calls to reduce round-trips:

```
server_control domain=batch action=call_many
  responseMode: summary
  items:
    - { t: colony_control, a: { domain: read, action: status } }
    - { t: colony_control, a: { domain: diagnostic, action: diagnostics } }
    - { t: read_control, a: { domain: resources, action: food } }
    - { t: read_control, a: { domain: infrastructure, action: power_summary } }
```

Do NOT batch interdependent calls where a later call needs an id or result from an earlier call.

### Pattern E: One-Shot State Snapshot

Use `colony_control domain=snapshot action=get profile=brief|standard` as the default first read. It replaces the common `game_control domain=speed action=time + colony_control domain=read action=status + colony_control domain=diagnostic action=diagnostics + colony_control domain=diagnostic action=alerts + read_control domain=resources action=food + colony_control domain=read action=dupes + colony_control domain=management kind=research action=status` bundle. Keep `includeAtmosphere=false` unless oxygen totals are needed, because atmosphere requires a full grid scan.

### Pattern F: Semantic Build First

For construction, choose prefab/material with `search_defs` and `materials`, find candidates, preview one candidate with explicit x/y, then use `build_area` with lower-left anchors. `BuildLocationRule=OnFloor` buildings must have floor/support cells below them; place support tiles first before machines, beds, toilets, batteries, and research stations.

## Error Recovery

### Tool returns error
1. Check error message for missing param / invalid value / type mismatch
2. Re-read state if stale data suspected (IDs may have shifted)
3. Retry with corrected params
4. If persistent, fall back to `server_control domain=catalog action=search` to find alternative tool

### Request timeout (10s)
1. Check if task was created via `tasks/list` or server polling
2. If task exists and is "working", poll `tasks/get`
3. If no task, retry the call
4. If consistently timing out, reduce scope (smaller `limit`, narrower query, smaller area)

### confirm rejected
1. Dangerous tool was called without `confirm=true`
2. Add `confirm:true` and retry
3. If batch rejected, identify which item failed and retry subset

### Stale data / ID mismatch
1. Game state changes between read and write (dupes die, buildings deconstructed)
2. Always re-read target list if write fails with "not found"
3. Use name-based lookup as fallback when ID is unstable

## Risk Management

### Tool Risk Levels
- **none**: read-only, cacheable, retry-safe (`colony_control domain=read action=status`, `colony_control domain=read action=dupes`, `read_control domain=world action=text_map`)
- **low**: minor state change (`navigation_control action=move`, `game_control domain=speed action=pause`, `building_control domain=config action=visual kind=light visualAction=set_color`)
- **medium**: config changes (`dupes_control domain=priority action=set`, `colony_control domain=management kind=schedule action=set_block`, `building_control domain=config action=set_threshold`) — confirm recommended
- **dangerous**: map-altering (`orders_dig`, `orders_deconstruct`, `orders_sweep`, `orders_cut_conduits`) — confirm required
- `orders_control domain=area action=sweep` is solid debris/storage cleanup only; liquid cleanup is `orders_control domain=area action=mop`.

Risk is inferred from tool name by the server. `InferRisk` logic: `deconstruct` / `dig` → dangerous; `set_` / `assign` / `sweep` / `launch` → medium; `pause` / `resume` / `focus` → low; everything else → none.

### Pre-Execution Checklist
- [ ] Read current state
- [ ] Validate parameters (type, range, existence)
- [ ] Check confirm requirement for write/execute tools
- [ ] Use `dryRun` or `validateOnly` if available
- [ ] For construction, confirm prefab/material, support cells, and lower-left anchors before `build_area`
- [ ] Define rollback read tools for verification
- [ ] Ensure game is paused (`game_control domain=speed action=pause`) for complex multi-step operations

## State Caching Strategy

### What to cache (short TTL)
- `server_control domain=catalog action=manifest` / `server_control domain=catalog action=search` result → static after init
- `colony_control domain=read action=status` → 5 seconds (cycle/time change slowly)
- `read_control domain=resources action=inventory` → 10 seconds
- `colony_control domain=read action=dupes` → 10 seconds
- `read_control domain=buildings action=summary` → 15 seconds
- `read_control domain=infrastructure action=rooms` → 30 seconds

### What NOT to cache
- Any write/execute result
- `read_control domain=world action=cell_info` (can change every tick)
- `camera_view` (player may have moved)
- `colony_control domain=diagnostic action=alerts` (volatile)
- `read_control domain=infrastructure action=power_summary` during grid changes

### Cache invalidation
- Any write/execute tool call → invalidate related read caches
- Game pause/resume/speed change → invalidate time-sensitive caches
- `orders_dig` / `orders_deconstruct` → invalidate `read_control domain=world action=text_map` and `read_control domain=buildings action=summary`

## Tool Categories at a Glance

| Category | Read | Write | Execute |
|----------|------|-------|---------|
| **colony** | `colony_control domain=read action=status`, `colony_control domain=diagnostic action=diagnostics`, `colony_control domain=diagnostic action=alerts`, `colony_control domain=report action=report`, `colony_control domain=report action=summary`, `colony_control domain=diagnostic action=list_settings`, `colony_control domain=notification action=list` | `colony_control domain=diagnostic action=set_settings` | `colony_control domain=notification action=click/dismiss` |
| **dupes** | `colony_control domain=read action=dupes`, `dupes_control domain=info action=detail/attributes/needs/status_check`, `dupes_control domain=priority action=list`, `dupes_control domain=skill action=list`, `dupes_control domain=side_screen action=equipment/direct_commands/todos` | `dupes_control domain=priority action=set/batch_set`, `dupes_control domain=skill action=learn`, `dupes_control domain=hat action=set`, `dupes_control domain=command action=rename`, `dupes_control domain=command action=auto_rename`, `dupes_control domain=assignable action=set/set_slot_item` | `dupes_control domain=command action=move_to/move_batch_to/force_action` |
| **schedules** | `colony_control domain=management kind=schedule action=list` | `colony_control domain=management kind=schedule action=create/set_block/assign_dupe/optimize` | — |
| **resources** | `read_control domain=resources action=inventory/food/search_items`, `read_control domain=resources action=pins`, `building_control domain=storage action=list/detail`, `colony_control domain=management kind=diet action=status` | `read_control domain=resources action=set_pin`, `building_control domain=storage action=set_filter`, `colony_control domain=management kind=diet action=set/policy` | — |
| **buildings** | `read_control domain=buildings action=list/summary`, `building_control domain=planning action=search_defs/materials/placement_candidates`, `building_control domain=config action=list`, `building_control domain=special kind=artable action=list`, `building_control domain=config action=visual kind=light visualAction=list`, `building_control domain=config action=visual kind=pixel_pack visualAction=list` | `building_control domain=planning action=preview/auto_connect`, `building_control domain=config action=set_enabled/set_toggle/copy_settings`, `orders_control domain=designation action=manual_delivery`, `building_control domain=special kind=artable action=set_stage`, `building_control domain=config action=visual kind=light visualAction=set_color`, `building_control domain=config action=visual kind=pixel_pack visualAction=set_color` | — |
| **orders** | `orders_control domain=priority action=list` | `orders_control domain=priority action=set_building`, `orders_control domain=priority action=set_area` | `orders_control domain=designation action=deconstruct`, `orders_control domain=area action=sweep`, `orders_control domain=area action=dig`, `orders_control domain=area action=mop`, `orders_control domain=area action=disinfect`, `orders_control domain=area action=cancel`, `orders_control domain=area action=harvest`, `orders_control domain=designation action=capture`, `orders_control domain=designation action=empty_conduits`, `orders_control domain=designation action=cut_conduits` |
| **power** | `read_control domain=infrastructure action=power_summary/power_ports` | — | — |
| **rooms** | `read_control domain=infrastructure action=rooms` | — | — |
| **world** | `colony_control domain=read action=worlds`, `read_control domain=world action=cell_info`, `read_control domain=world action=element_summary`, `read_control domain=world action=text_map`, `read_control domain=world action=thermal_overheat_risk` | `read_control domain=area action=define`, `read_control domain=area action=forget` | — |
| **camera** | `navigation_control action=get_view` | — | `navigation_control action=move/set_view/switch_view/focus_cell/focus_dupe/screenshot` |
| **game** | `game_control domain=speed action=time`, `game_control domain=save action=list`, `game_control domain=dlc action=list` | `game_control domain=state action=set_sandbox_mode`, `game_control domain=dlc action=activate` | `game_control domain=speed action=pause/resume/set_speed`, `game_control domain=save action=save/load/quit` |
| **tools** | `server_control domain=catalog action=manifest`, `server_control domain=catalog action=search`, `server_control domain=catalog action=guide`, `server_control domain=catalog action=coverage`, `server_control domain=catalog action=static_audit` | `game_control domain=ui uiDomain=edit_mark action=create/clear` | `server_control domain=batch action=call_many` |
| **server** | `server_control domain=diagnostics action=status/capabilities/logs_tail`, `server_control domain=client_request` | — | — |

## Prompt Workflows

| Prompt | Trigger | Internal Tool Chain |
|--------|---------|---------------------|
| `colony_triage` | Quick health check | `oni://colony/status` → `oni://colony/diagnostics` → `oni://colony/alerts` → `oni://resources/food` |
| `next_cycle_plan` | Action planning | `oni://colony/summary` → `oni://resources/inventory` → `oni://research/status` → `oni://schedules` → `oni://dupes` |
| `inspect_area` | Spatial analysis | `oni://world/text-map` plain map → optionally `read_control domain=world action=text_map` with `includeBuildings=true` |
| `dupe_care_review` | Duplicant audit | `oni://dupes` → `oni://schedules` → `dupes_control domain=info action=detail/needs/attributes` |
| `power_audit` | Power system check | `oni://power/summary` → optionally `building_control domain=config action=list` filtered for power |
| `rooms_overview` | Room coverage check | `oni://rooms/list` → filter by type/size |
| `thermal_audit` | Overheat risk scan | `oni://thermal/overheat-risk` → optionally `read_control domain=world action=element_summary` |

Prompts return a structured text that tells you which resources and tools to call. They do not execute directly — you must make the calls.

## Resource URI Reference

| URI | Tool Equivalent | Use Case |
|-----|-----------------|----------|
| `oni://colony/status` | `colony_control domain=read action=status` | Baseline snapshot |
| `oni://colony/diagnostics` | `colony_control domain=diagnostic action=diagnostics` | Alert summary |
| `oni://colony/alerts` | `colony_control domain=diagnostic action=alerts` | Notification list |
| `oni://colony/summary` | `colony_control domain=report action=summary` | Planning input |
| `oni://resources/inventory` | `read_control domain=resources action=inventory` | Stock levels |
| `oni://resources/food` | `read_control domain=resources action=food` | Food with expiry |
| `oni://power/summary` | `read_control domain=infrastructure action=power_summary` | Circuit health |
| `oni://rooms/list` | `read_control domain=infrastructure action=rooms` | Room coverage |
| `oni://thermal/overheat-risk` | `read_control domain=world action=thermal_overheat_risk` | Heat risk ranking |
| `oni://world/elements` | `read_control domain=world action=element_summary` | Element mass/temp |
| `oni://world/text-map` | `read_control domain=world action=text_map` | Terrain scan |
| `oni://dupes` | `colony_control domain=read action=dupes` | Duplicant roster |
| `oni://schedules` | `colony_control domain=management kind=schedule action=list` | Schedule definitions |
| `oni://research/status` | `colony_control domain=management kind=research action=status` | Tech progress |
| `oni://tools/manifest` | `server_control domain=catalog action=manifest` | Tool catalog |
| `oni://tools/search` | `server_control domain=catalog action=search` | Filtered discovery |
Resource templates accept query params: `oni://power/summary?worldId=2&includeDetails=true`, `oni://thermal/overheat-risk?marginC=20&limit=50`.

## Quick Reference

| Situation | First Tool | Second Tool | Verification |
|-----------|-----------|-------------|--------------|
| "What's happening?" | `colony_control domain=diagnostic action=diagnostics` | `colony_control domain=diagnostic action=alerts` | `colony_control domain=read action=status` |
| "Fix power" | `read_control domain=infrastructure action=power_summary` | `building_control domain=config action=list` (power subset) | `read_control domain=infrastructure action=power_summary` |
| "Build something" | `building_control domain=planning action=search_defs/materials` | `building_control domain=planning action=placement_candidates/preview/build_area` | `read_control domain=world action=text_map` |
| "Manage dupes" | `colony_control domain=read action=dupes` | `dupes_control domain=info action=detail/needs` | `colony_control domain=read action=dupes` |
| "Check heat" | `read_control domain=world action=thermal_overheat_risk` | `read_control domain=world action=element_summary` | `read_control domain=world action=thermal_overheat_risk` |
| "Plan actions" | `server_control domain=catalog action=guide` | `server_control domain=catalog action=search` + dryRun-capable tools | relevant read tools |
| "Batch config" | `server_control domain=batch action=call_many` (dryRun) | `server_control domain=batch action=call_many` (execute) | relevant read tools |
| "Find a tool" | `server_control domain=catalog action=search` | `server_control domain=catalog action=guide` | `server_control domain=catalog action=static_audit` |
| "Area ops" | `read_control domain=area action=define` | `read_control domain=world action=text_map` (with areaId) | `read_control domain=area action=get` |
| "Camera nav" | `navigation_control action=get_view` | `navigation_control action=move/focus_cell` | `navigation_control action=get_view` |
| "Check research" | `colony_control domain=management kind=research action=status` | `colony_control domain=management kind=research action=list` | `colony_control domain=management kind=research action=status` |
| "Check rockets" | `building_control domain=rocket rocketDomain=ops action=status` | `building_control domain=rocket rocketDomain=ops action=detail` | `building_control domain=rocket rocketDomain=ops action=status` |
| "Storage config" | `building_control domain=storage action=list` | `building_control domain=storage action=detail` | `building_control domain=storage action=set_filter` |
| "Automation setup" | `building_control domain=config action=list_automation` | `building_control domain=side_surface surface=automation kind=automatable action=set` | `building_control domain=config action=list_automation` |
