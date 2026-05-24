---
name: oni-mcp-opening-setup
description: Use when starting a new Oxygen Not Included colony or when the user asks for opening setup/bootstrap configuration. Covers pausing before thinking, staggered schedules, attribute-based duplicant renaming with user-confirmed naming style, disabling auto-disinfect, scouting the starting asteroid, and planning early two-sided expansion plus a printing-pod laboratory.
---

# ONI MCP Opening Setup

## Trigger

Use this skill for new-game or early-cycle setup:

- opening configuration
- start-of-run bootstrap
- schedule setup
- duplicant renaming by attributes
- initial asteroid overview
- first dig/foundation/lab plan near the printing pod

## Hard Rule

Before analysis or planning, pause the game:

```
game_pause
```

Keep the game paused while reading state, planning, asking questions, and issuing setup commands. Only resume after the plan is complete and either:

- the user explicitly asks to continue, or
- the current task specifically includes "resume after setup".

If the user asks to think, inspect, plan, or configure, pause first.

## Required User Question

Before renaming duplicants, ask the user for the naming style.

Ask once, briefly. Examples:

- `职业前缀`: `Dig-Ada`, `Build-Meep`
- `中文岗位`: `挖掘-艾达`, `建造-米普`
- `短标签`: `Digger`, `Builder`, `Cook`
- User custom style

Do not apply `dupes_auto_rename apply=true` until the user answers. Preview is allowed:

```
dupes_auto_rename style=<candidate> apply=false
```

## Opening Flow

### 1. Pause And Snapshot

```
game_pause
colony_state_snapshot profile=standard includeAtmosphere=false
world_list
camera_get_view
```

Use `colony_state_snapshot` instead of separate status/dupe/food/research calls unless detail is missing.

### 2. Configure Staggered Schedules

Read current schedules:

```
schedule_list
```

Preview staggered shifts:

```
schedule_optimize apply=false
```

Apply only when the task is explicitly setup/configuration, or after confirming with the user:

```
schedule_optimize apply=true prefix="AI轮班"
schedule_list
```

The goal is early staggered shifts that reduce toilet, bed, and recreation congestion. Keep the default automatic shift count unless the user requested a specific number.

### 3. Rename Duplicants By Attributes

Read enough dupe context:

```
dupes_list
dupes_attributes
dupes_skills_list
```

Infer roles from attributes/interests:

- Digging/construction: excavation, construction, strength
- Research/operator: science, machinery
- Farming/ranching/cooking: agriculture, ranching, cuisine
- Supplier/tidier: athletics, strength, low specialization

Ask naming style before applying. Then either:

```
dupes_auto_rename style=<user style> apply=false
dupes_auto_rename style=<user style> apply=true
```

or use explicit:

```
dupes_rename id=<dupeId> newName=<name>
```

Verify with `dupes_list`.

### 4. Disable Auto-Disinfect

Do not issue broad disinfect orders in the opening.

Find auto-disinfect targets first:

```
user_menu_actions_list query=auto-disinfect category=care limit=100
```

For targets offering `disable_auto_disinfect`, batch press:

```
user_menu_actions_batch_press
  confirm=true
  defaults={ "actionKey": "disable_auto_disinfect" }
  items=[ { "id": ... }, ... ]
```

If no targets exist, record that auto-disinfect had nothing to disable.

### 5. Scout Starting Area And Asteroid Overview

Get map context around current camera / printing-pod area:

```
world_area_snapshot preset=utilities encoding=rle includeScreenshot=false
```

If the area is too small or the printing pod is not visible, expand around the observed starting coordinates with 40x30 to 60x40 cells, staying under `maxCells`.

Summarize for the user:

- active world / asteroid type
- starting biome signals
- nearby solids/liquids/gases
- immediate hazards
- food/plant/resource hints
- usable expansion directions
- whether early oxygen, water, or temperature looks urgent

Use text maps, not screenshots, for coordinate conclusions.

### 6. First Build/Expansion Plan

Default opening recommendation:

- Expand left and right from the printing pod.
- Queue conservative dig rectangles on both sides, avoiding liquids and hazardous pockets.
- Place foundations/platforms as straight lines using `buildings_plan_many` with `l`/`line`.
- Reserve the printing-pod-adjacent area as an early laboratory.
- Research/power setup must include floor/support first, then generator, battery, research station, and connected wire route.

Before placing or digging, dry-run:

```
buildings_plan_many dryRun=true confirm=true ...
```

For utility routes, use `routes` in `buildings_plan_many` so wires/pipes connect in the same call.

For excavation:

```
orders_dig_area confirm=true ...
```

Never use `orders_attack` for digging.

### 7. Record Plan

Use plan harness for the opening plan:

```
plan_harness_create objective="Opening setup and Cycle 1 expansion" riskTolerance=low requireVerification=true
plan_harness_record stage=plan summary="Opening setup plan" payload={...}
plan_harness_validate id=<planId>
```

If actions were executed, verify:

```
schedule_list
dupes_list
world_area_snapshot areaId=<area> preset=utilities encoding=rle
plan_harness_record stage=verification passed=true ...
```

## Execution Policy

This skill is allowed to execute setup actions when the user asked for opening setup/configuration:

- `game_pause`
- `schedule_optimize apply=true`
- `dupes_auto_rename apply=true` only after naming-style answer
- `user_menu_actions_batch_press` for `disable_auto_disinfect`
- `orders_dig_area`
- `buildings_plan_many`

Still use dry runs for construction before placement.

Do not resume the game until planning/configuration is complete and the user permits continuation.

## Final Response Shape

Keep it concise:

- paused status
- schedule setup result
- rename status or pending naming-style question
- auto-disinfect result
- asteroid overview
- expansion/lab plan summary
- whether game remains paused or was resumed by explicit request
