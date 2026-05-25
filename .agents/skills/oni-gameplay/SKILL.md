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
Observe (Sense)    → colony_state_snapshot minimal/delta + targeted small reads → get state snapshot
Orient (Analyze)   → parse JSON, identify gaps/anomalies
Decide (Plan)      → select target state, choose tools
Act (Execute)      → call write/execute tools with confirm
Verify (Check)     → re-call read tools, compare before/after
```

Every control action must complete the full loop. Never act without prior observation, never stop without verification.

## Pointer-First Policy

All map actions must be driven through the visible agent pointer unless the tool is configuration-only or no pointer equivalent exists.

Default action workflow:

```
agent_pointer_jump or agent_pointer_aim_cell
agent_pointer_select_tool tool=<build|dig|cancel|sweep|mop|disinfect|harvest|deconstruct> prefabId? material? priority?
agent_pointer_left_click confirm=true
# or for straight lines:
agent_pointer_hold_left direction=<right|left|up|down> length=<cells> confirm=true
```

Rules:
- Legacy coordinate building tools are removed from the public MCP surface. Do not call or suggest them.
- Treat direct `orders_*_area` calls as compatibility/debug paths. Do not use them as the first choice for normal play.
- Use `agent_pointer_hold_left` for wires, pipes, ladders, tiles, straight digs, sweep lines, mop lines, cancel lines, and harvest lines.
- Use `agent_pointer_jump code=home|p1|p2` or `agent_pointer_jump x/y|dx/dy|direction+steps` for navigation. Use `agent_pointer_jump_point_set code=p1` to save repeat work locations.
- If a coordinate is known from a read tool, jump the pointer to it and act with the pointer; do not pass that coordinate directly to build/order tools.

### Control Modes

| Mode | Trigger | Primary Tools | Output |
|------|---------|--------------|--------|
| **Diagnostic** | User asks "what's wrong" | `colony_diagnostics`, `colony_alerts`, `resources_food`, `dupes_needs`, `power_summary`, `thermal_overheat_risk_scan` | Problem list + severity |
| **Planning** | User asks "what should I do" | `tools_guide`, compact reads, dry-run tools | Short plan with explicit validation steps |
| **Execution** | User says "do it" | `tools_call_many`, individual write/execute tools | Execution result + task IDs |
| **Monitoring** | User wants status update | `colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts` | Snapshot diff |

Mode transitions: Diagnostic → Planning → Execution → Monitoring. If Execution fails, fall back to Diagnostic.

## Tool Selection Strategy

### Read vs Write vs Execute

- **Read** (`colony_*`, `dupes_*`, `resources_*`, `world_*`, `power_summary`, `rooms_list`) → 获取状态，无副作用，可缓存
- **Write** (`set_personal_priority`, `set_schedule_block`, `set_storage_filter`) → 修改配置，需 `confirm=true`（medium risk）
- **Execute** (`game_pause`, `camera_move`, `orders_dig`, `buildings_deconstruct`) → 对游戏下达动作，dangerous 需 `confirm=true`

Mode is inferred from tool name: `get_*` / `list_*` → read; `set_*` / `configure_*` → write; `move_*` / `pause_*` / `focus_*` → execute.

### Resource vs Tool vs Prompt

- **Resource** (`oni://...`) → 固定快照，适合重复读取的参考数据（inventory, schedules, dupes list）
- **Tool** (`tools/call`) → 交互式，带参数，适合针对性查询和动作
- **Prompt** → 启动标准化工作流（`power_audit`, `rooms_overview`, `thermal_audit`）

Resource URIs are idempotent and cacheable. Tools with parameters are not. Prompts orchestrate multiple resources and tools in sequence.

### When to use which discovery tool

| Goal | Use |
|------|-----|
| "What tools exist?" | `tools_manifest` (full) or `tools_search detail=brief` (filtered) |
| "What can I do about X?" | `tools_guide goal=X` |
| "Am I missing coverage?" | `tools_player_action_coverage` |
| "Is this tool safe?" | `tools_static_audit` |

`tools_manifest` exposes ~60 core tools by default; `tools_search detail=full` reveals the complete ~320 tool registry. Use `detail=brief` for low-token discovery.

## Observation: Camera, Views, Screenshots, Maps

Spatial ONI control starts with the visible agent pointer. Use text maps only as supporting context for unknown terrain, hazards, or verification. Do not make the model's primary action interface raw coordinates.

### Camera Navigation

Use camera tools when the user asks to look somewhere, when you need a screenshot of a specific region, or when a visual overlay matters:

```
camera_get_view
  → read current position, zoom, activeWorldId, screen size

camera_focus_cell
  x, y, worldId?, zoom?
  → center a known grid cell

camera_focus_dupe
  id/name
  → follow a duplicant

camera_move
  mode: jump | pan
  x/y for jump, dx/dy for pan, zoom?
  → move to a coordinate or nudge the view

camera_set_active_world
  worldId
  → switch asteroid/rocket interior before reading or viewing that world
```

After moving the camera, call `camera_get_view` if exact view confirmation matters. For map-data tools, moving the camera is optional when you pass explicit `x1,y1,x2,y2`; it is required only for screenshots or default camera-centered map reads.

### View / Overlay Switching

Use `camera_switch_view` for visual overlays and optional screenshot capture:

```
camera_switch_view
  view: none | oxygen | power | gas_conduits | liquid_conduits | solid_conveyor | logic | temperature | heat_flow | materials | light | decor | rooms | priorities | disease | radiation | sound | suit | crop | harvest
  screenshot: true|false
```

Rules:
- Use `view=none` before a normal visual screenshot.
- Use `view=oxygen`, `temperature`, `rooms`, `decor`, `crop`, `harvest`, `priorities`, `disease`, etc. when the task depends on visual overlay colors/icons.
- For power, gas pipes, liquid pipes, conveyors, and logic wires, prefer `world_area_snapshot` or `world_text_map view=...` for coordinate-accurate planning; use `camera_switch_view` only to visually confirm.
- `camera_switch_view screenshot=true` is the shortest way to switch overlay and capture the current screen in one call.

### Screenshot Use

Use `game_screenshot` only after the camera and overlay are set correctly. Screenshot is useful for:

- confirming the player-facing visual state
- room/decor/crop/harvest/priority/disease overlays
- UI state that is not represented by world maps
- comparing what the user sees with structured map data

Screenshot is weak for exact coordinates. If a decision affects digging, building, sweeping, mopping, wiring, piping, or deconstruction, read `world_area_snapshot` or `world_text_map` before acting.

### Pointer Build Discipline

For any build/dig/deconstruct action, screenshots and text maps are observation only. The actual command path should be pointer selection plus click/drag.

Rules:
- Before placing a line, aim the pointer at the start cell with `agent_pointer_jump` or `agent_pointer_aim_cell`.
- Select the current tool with `agent_pointer_select_tool`.
- Treat `buildings_search_defs.placement.anchor=lowerLeftCell` as the build anchor. Do not treat screenshot center, tooltip position, or blueprint center as the anchor.
- For horizontal/vertical 1x1 work (tiles, ladders, wires, conduits), use `agent_pointer_hold_left direction=... length=... confirm=true`.
- For furniture/machines or any footprint wider/taller than 1 cell, use `agent_pointer_left_click` once per lower-left anchor. `agent_pointer_hold_left` rejects these by default; pass `allowFootprintDrag=true` only when repeated footprint placement is intentional.
- Preflight uncertain multi-cell placements with `agent_pointer_left_click dryRun=true`, then execute with `confirm=true`.
- After execution, compare `placementCheck` with a targeted `world_area_snapshot`/`world_text_map`. If the blueprint is shifted, cancel it with the pointer's cancel tool before replacing.

### World Map Reads

Use `world_area_snapshot` only when pointer/camera state and compact status do not provide enough spatial context:

```
world_area_snapshot
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

Use `world_text_map` for narrower or more controlled reads:

```
world_text_map
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
game_pause
colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
agent_pointer_get
agent_pointer_jump/aim_cell if target is known
targeted world_area_snapshot only if terrain/hazard context is missing
```

If the user gives an area or edit marker, use that area directly; do not waste time moving the camera before reading the map.

## Standard Control Sequences

### Sequence 1: Colony Health Check

```
colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
colony_state_snapshot profile=brief only when minimal flags a concern
resources_food         → only when food detail is needed
power_summary          → generation vs load per circuit
thermal_overheat_risk_scan → overheat risk sorted by delta-T
rooms_list             → room coverage summary
→ Parse JSON, compare against thresholds, flag anomalies
```

Use `detail=compact` on all calls for a sub-5-second scan. Escalate to `detail=full` only when an anomaly is flagged.

### Sequence 2: Fast Execute A Simple Plan

Use this for low-risk, small-scope actions such as a short dig, mop, sweep, floor line, config batch, or dry-run-passed blueprint:

```
tools_call_many
  dryRun: true
  responseMode: summary
  requireAllValid: true
  stopOnError: true
  items: [...]

tools_call_many
  dryRun: false
  responseMode: summary
  requireAllValid: true
  stopOnError: true
  items: [...]

verify with colony_state_snapshot/world_area_snapshot/targeted read
```

For map actions, the `items` should usually be pointer tool calls, not coordinate order/build calls. Do not create separate planning records for trivial single-step actions. It adds latency without improving safety.

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
tools_call_many
  dryRun: true
  responseMode: summary
  defaults: { confirm: true }
  items:
    - { t: set_personal_priority, a: { id: 1, choreGroup: Dig, priority: 4 } }
    - { t: set_personal_priority, a: { id: 2, choreGroup: Build, priority: 4 } }
    - { t: set_schedule_block,   a: { scheduleId: 0, blockIndex: 3, activity: Sleep } }
    ...
  requireAllValid: true
  stopOnError: true
```

If `dryRun` passes, re-call with `dryRun: false` to execute. `defaults` merges into every item. Use low-token shorthand (`t` / `a`) for large batches. Max 20 items per call. Keep `responseMode=summary` for normal batches; use `responseMode=errors` for retry loops and `responseMode=full` only when exact child payloads are needed.

### Sequence 5: Spatial Analysis + Action

```
game_pause             → freeze state for complex or destructive spatial plans
world_area_snapshot
  x1, y1, x2, y2
  preset: construction | utilities
  encoding: rle
  includeScreenshot: false
→ Preferred coordinate-accurate context for build/dig/power/pipe planning
world_text_map
  x1, y1, x2, y2
  profile: standard
  encoding: plain
  includeElements: true
→ Use directly for human-readable terrain/object debugging
camera_focus_cell / camera_move
camera_switch_view
game_screenshot
→ Optional visual confirmation only after structured maps
→ Define reusable area: area_define → areaId when the same region will be reused
→ Plan: choose pointer start, build/order tool, and click/drag gestures
→ Execute: agent_pointer_left_click or agent_pointer_hold_left
→ Verify: world_area_snapshot or world_text_map over the same area
```

Use `world_area_snapshot` for normal spatial planning because it keeps base terrain, objects, and utility overlays in one response. Use `world_text_map profile=standard encoding=plain` by default; reserve `profile=scan encoding=rle` for very large low-token terrain scans. Use screenshots only for visual confirmation, room/decor/crop/UI overlay interpretation, or when text maps cannot express the visual state.

### Sequence 6: Duplicant Management

```
dupes_list             → all dupes with ID, name, world, stress
dupes_detail           → single dupe: attributes, traits, skills
dupes_needs            → needs, stress sources, morale
dupes_priorities_list  → current chore priorities
→ Identify gaps
→ Batch update: tools_call_many with set_personal_priority items
→ Verify: dupes_priorities_list
```

Always fetch `dupes_list` first to resolve name → ID mapping. Many dupe tools require numeric `id`, not name.

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
- `tools_call_many responseMode=summary` → compact per-call status without nested full payloads

### confirm
- `confirm: true` required for all medium/dangerous write tools
- `tools_call_many` with `dryRun: true` pre-validates confirm requirements
- Use `buildings_materials` and `buildings_search_defs` before construction; build placement itself must go through the visible pointer.
- Never bypass confirm for dangerous tools (`orders_dig`, `orders_deconstruct`, `orders_sweep`)
- Do not repeat the same write/execute tool with identical coordinates after a zero-effect result. Read the result fields, re-read state if needed, then choose a different tool or corrected parameters.
- Use `orders_sweep_area` only for solid debris/pickupables. Use `orders_mop_area` for water, polluted water, spills, "地上的水", or other liquid cells on a floor.

### x1, y1, x2, y2 (area coordinates)
- Always specify in world cell coordinates
- `x1 < x2`, `y1 < y2` (origin is bottom-left)
- Large areas → high token cost; shrink the rectangle first, and use `encoding=rle` only when token budget matters more than readability

## Batch and Efficiency Patterns

### Pattern A: Read-Before-Write

Always read current state before modifying:

```
BAD:  directly call set_schedule_block with new values
GOOD: schedules_list → identify target scheduleId/blockIndex
      set_schedule_block → schedules_list to verify
```

### Pattern B: Area-Based Operations

1. `area_define` with `x1,y1,x2,y2,name` → get `a*` areaId for a hand-picked region
   or `area_blocks worldId=<id>` → get `b*` areaIds for automatic world chunks
2. Reuse `areaId` in subsequent calls (`world_text_map?areaId=xyz`)
   or temporarily join adjacent blocks with `areaId=b1+b2+b3`
3. For repeated use, call `area_merge areaIds=[...]` to create one merged `a*` handle
4. Bulk operations within an area should be decomposed into visible pointer clicks/drags; use direct area tools only when no pointer equivalent exists
5. Clean up temporary `a*` areas with `area_forget` when no longer needed; keep `b*` blocks while scanning the world

### Pattern C: Differential Updates

1. Read full list (`dupes_list`, `schedules_list`, `buildings_summary`)
2. Compute delta locally (don't diff on server)
3. Send only changes via `tools_call_many`

### Pattern D: Parallel Reads

Use `tools_call_many` for independent read calls to reduce round-trips:

```
tools_call_many
  responseMode: summary
  items:
    - { t: colony_status }
    - { t: colony_diagnostics }
    - { t: resources_food }
    - { t: power_summary }
```

Do NOT batch interdependent calls where a later call needs an id or result from an earlier call.

### Pattern E: One-Shot State Snapshot

Use `colony_state_snapshot profile=brief|standard` as the default first read. It replaces the common `game_time + colony_status + colony_diagnostics + colony_alerts + resources_food + dupes_list + research_status` bundle. Keep `includeAtmosphere=false` unless oxygen totals are needed, because atmosphere requires a full grid scan.

### Pattern F: Pointer Build First

For construction, choose prefab/material with read tools, aim the pointer at the exact start cell, then use `agent_pointer_left_click` or `agent_pointer_hold_left`. `BuildLocationRule=OnFloor` buildings must have floor/support cells below them; place visible support tiles first with the pointer before machines, beds, toilets, batteries, and research stations.

## Error Recovery

### Tool returns error
1. Check error message for missing param / invalid value / type mismatch
2. Re-read state if stale data suspected (IDs may have shifted)
3. Retry with corrected params
4. If persistent, fall back to `tools_search` to find alternative tool

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
- **none**: read-only, cacheable, retry-safe (`colony_status`, `dupes_list`, `world_text_map`)
- **low**: minor state change (`camera_move`, `game_pause`, `set_light_color`)
- **medium**: config changes (`set_personal_priority`, `set_schedule_block`, `set_threshold`) — confirm recommended
- **dangerous**: map-altering (`orders_dig`, `orders_deconstruct`, `orders_sweep`, `orders_cut_conduits`) — confirm required
- `orders_sweep_area` is solid debris/storage cleanup only; liquid cleanup is `orders_mop_area`.

Risk is inferred from tool name by the server. `InferRisk` logic: `deconstruct` / `dig` → dangerous; `set_` / `assign` / `sweep` / `launch` → medium; `pause` / `resume` / `focus` → low; everything else → none.

### Pre-Execution Checklist
- [ ] Read current state
- [ ] Validate parameters (type, range, existence)
- [ ] Check confirm requirement for write/execute tools
- [ ] Use `dryRun` or `validateOnly` if available
- [ ] For construction, confirm prefab/material and support cells before pointer click/drag
- [ ] Define rollback read tools for verification
- [ ] Ensure game is paused (`game_pause`) for complex multi-step operations

## State Caching Strategy

### What to cache (short TTL)
- `tools_manifest` / `tools_search` result → static after init
- `colony_status` → 5 seconds (cycle/time change slowly)
- `resources_inventory` → 10 seconds
- `dupes_list` → 10 seconds
- `buildings_summary` → 15 seconds
- `rooms_list` → 30 seconds

### What NOT to cache
- Any write/execute result
- `world_cell_info` (can change every tick)
- `camera_view` (player may have moved)
- `colony_alerts` (volatile)
- `power_summary` during grid changes

### Cache invalidation
- Any write/execute tool call → invalidate related read caches
- Game pause/resume/speed change → invalidate time-sensitive caches
- `orders_dig` / `orders_deconstruct` → invalidate `world_text_map` and `buildings_summary`

## Tool Categories at a Glance

| Category | Read | Write | Execute |
|----------|------|-------|---------|
| **colony** | `colony_status`, `colony_diagnostics`, `colony_alerts`, `colony_report`, `colony_summary`, `diagnostic_settings_list`, `notifications_list` | `set_diagnostic_settings` | `click_notification`, `dismiss_notification` |
| **dupes** | `dupes_list`, `dupes_detail`, `dupes_attributes`, `dupes_needs`, `dupes_priorities_list`, `dupes_skills`, `equipment`, `direct_commands`, `todos` | `set_personal_priority`, `batchSetPersonalPriorities`, `learn_skill`, `set_hat`, `rename_dupe`, `set_assignable`, `set_assignable_slot_item` | `move_dupe`, `move_dupes_batch` |
| **schedules** | `schedules_list` | `create_schedule`, `set_schedule_block`, `assign_dupe_schedule`, `optimize_schedules` | — |
| **resources** | `resources_inventory`, `resources_food`, `resources_pins`, `storage_list`, `storage_detail`, `diet_status` | `set_resource_pin`, `set_storage_filter`, `set_diet_food`, `apply_diet_policy` | — |
| **buildings** | `buildings_list`, `buildings_summary`, `buildings_search_defs`, `buildings_config_list`, `artables_list`, `lights_list`, `pixel_packs_list` | `set_building_enabled`, `configure_manual_delivery`, `copy_settings`, `set_artable_stage`, `set_light_color`, `set_pixel_pack_color` | — |
| **orders** | `priorities_list` | `set_building_priority`, `set_priority_area`, `set_building_toggle` | `agent_pointer_left_click`, `agent_pointer_hold_left`, `buildings_deconstruct`, `orders_sweep_area`, `orders_dig_area`, `orders_mop_area`, `orders_disinfect_area`, `orders_cancel_area`, `orders_harvest_area`, `critters_capture`, `conduits_empty_area`, `conduits_cut` |
| **power** | `power_summary` | — | — |
| **rooms** | `rooms_list` | — | — |
| **world** | `world_list`, `world_cell_info`, `world_element_summary`, `world_text_map`, `thermal_overheat_risk_scan` | `area_define`, `area_forget` | — |
| **camera** | `camera_get_view` | — | `camera_move`, `camera_set_view`, `camera_switch_view`, `camera_focus_cell`, `camera_focus_dupe`, `game_screenshot` |
| **game** | `game_time`, `list_saves`, `list_dlc_activation` | `set_sandbox_mode`, `activate_dlc_for_save` | `game_pause`, `game_resume`, `set_game_speed`, `save_game`, `load_save`, `quit_game` |
| **tools** | `tools_manifest`, `tools_search`, `tools_guide`, `tools_player_action_coverage`, `tools_static_audit` | `edit_mark_request_create`, `edit_mark_request_clear` | `tools_call_many` |
| **server** | `server_status`, `mcp_client_capabilities`, `logs_tail` | — | — |

## Prompt Workflows

| Prompt | Trigger | Internal Tool Chain |
|--------|---------|---------------------|
| `colony_triage` | Quick health check | `oni://colony/status` → `oni://colony/diagnostics` → `oni://colony/alerts` → `oni://resources/food` |
| `next_cycle_plan` | Action planning | `oni://colony/summary` → `oni://resources/inventory` → `oni://research/status` → `oni://schedules` → `oni://dupes` |
| `inspect_area` | Spatial analysis | `oni://world/text-map` plain map → optionally `world_text_map` with `includeBuildings=true` |
| `dupe_care_review` | Duplicant audit | `oni://dupes` → `oni://schedules` → `dupes_detail` / `dupes_needs` / `dupes_attributes` |
| `power_audit` | Power system check | `oni://power/summary` → optionally `buildings_config_list` filtered for power |
| `rooms_overview` | Room coverage check | `oni://rooms/list` → filter by type/size |
| `thermal_audit` | Overheat risk scan | `oni://thermal/overheat-risk` → optionally `world_element_summary` |

Prompts return a structured text that tells you which resources and tools to call. They do not execute directly — you must make the calls.

## Resource URI Reference

| URI | Tool Equivalent | Use Case |
|-----|-----------------|----------|
| `oni://colony/status` | `colony_status` | Baseline snapshot |
| `oni://colony/diagnostics` | `colony_diagnostics` | Alert summary |
| `oni://colony/alerts` | `colony_alerts` | Notification list |
| `oni://colony/summary` | `colony_summary` | Planning input |
| `oni://resources/inventory` | `resources_inventory` | Stock levels |
| `oni://resources/food` | `resources_food` | Food with expiry |
| `oni://power/summary` | `power_summary` | Circuit health |
| `oni://rooms/list` | `rooms_list` | Room coverage |
| `oni://thermal/overheat-risk` | `thermal_overheat_risk_scan` | Heat risk ranking |
| `oni://world/elements` | `world_element_summary` | Element mass/temp |
| `oni://world/text-map` | `world_text_map` | Terrain scan |
| `oni://dupes` | `dupes_list` | Duplicant roster |
| `oni://schedules` | `schedules_list` | Schedule definitions |
| `oni://research/status` | `research_status` | Tech progress |
| `oni://tools/manifest` | `tools_manifest` | Tool catalog |
| `oni://tools/search` | `tools_search` | Filtered discovery |
Resource templates accept query params: `oni://power/summary?worldId=2&includeDetails=true`, `oni://thermal/overheat-risk?marginC=20&limit=50`.

## Quick Reference

| Situation | First Tool | Second Tool | Verification |
|-----------|-----------|-------------|--------------|
| "What's happening?" | `colony_diagnostics` | `colony_alerts` | `colony_status` |
| "Fix power" | `power_summary` | `buildings_config_list` (power subset) | `power_summary` |
| "Build something" | `buildings_search_defs` | `agent_pointer_select_tool` + `agent_pointer_left_click`/`agent_pointer_hold_left` | `world_text_map` |
| "Manage dupes" | `dupes_list` | `dupes_detail` / `dupes_needs` | `dupes_list` |
| "Check heat" | `thermal_overheat_risk_scan` | `world_element_summary` | `thermal_overheat_risk_scan` |
| "Plan actions" | `tools_guide` | `tools_search` + dryRun-capable tools | relevant read tools |
| "Batch config" | `tools_call_many` (dryRun) | `tools_call_many` (execute) | relevant read tools |
| "Find a tool" | `tools_search` | `tools_guide` | `tools_static_audit` |
| "Area ops" | `area_define` | `world_text_map` (with areaId) | `area_get` |
| "Camera nav" | `camera_get_view` | `camera_move` / `camera_focus_cell` | `camera_get_view` |
| "Check research" | `research_status` | `list_research` | `research_status` |
| "Check rockets" | `rockets_status` | `rockets_detail` | `rockets_status` |
| "Storage config" | `storage_list` | `storage_detail` | `set_storage_filter` |
| "Automation setup" | `automation_controls_list` | `set_automatable_control` | `automation_controls_list` |
