---
name: oni-mcp-environment-summary
description: Use when the user asks for a quick summary of nearby ONI environment/resources, starting-area overview, surrounding resources, nearby hazards, expansion directions, or "what is around me". Guides the agent to use low-token read-only MCP tools, especially world_area_snapshot, and return a concise actionable environment/resource摘要 without executing game actions.
---

# ONI MCP Environment Summary

## Purpose

Quickly summarize the area around the current view, selected area, printing pod, or user-provided coordinates. This skill is read-only: do not dig, build, sweep, harvest, or change settings.

## Fast Path

Use the fewest reads that answer the question:

```
colony_state_snapshot profile=brief includeAtmosphere=false
camera_get_view
world_area_snapshot preset=utilities encoding=rle includeScreenshot=false
```

If the user provided `areaId` or coordinates, pass them to `world_area_snapshot`.

If a player edit marker request exists or the user says "框选区域/标记区域/玩家规划", read it first:

```
edit_mark_request_list limit=5
world_area_snapshot areaId=<request.areaId> preset=utilities encoding=rle
```

## When To Add Detail

Only add these if the fast path is insufficient:

- `world_element_summary state=solid|liquid|gas` for world-scale mass/temperature context.
- `resources_inventory limit=30` for known stockpiles/debris inventory.
- `resources_food limit=20` for food count and calories.
- `farming_harvestables_list x1/y1/x2/y2 readyOnly=false limit=80` for nearby wild plants/food.
- `world_text_map profile=scan encoding=rle` for a larger terrain-only scan when `world_area_snapshot` area is too small.

Avoid screenshots unless the user asks for visual confirmation.

## Map Reading Rules

Read `maps.base` first:

- `S`: natural solid tiles; candidate digging/resources
- `T`: constructed tile/foundation
- `L`: liquid; note water/polluted water/brine if element details are present
- `O/P/C/H`: oxygen/polluted oxygen/carbon dioxide/hydrogen
- `B`: building
- `D`: duplicant
- `i`: loose item/debris
- `?`: unknown/unrevealed or outside-world

Read overlays after base:

- `maps.power`: wires and power devices
- `maps.gas_conduits`, `maps.liquid_conduits`: existing pipe routes
- `maps.logic`: automation
- `maps.solid_conveyor`: shipping rails

Use coordinates from text maps. Do not infer exact cells from screenshots.

## Summary Format

Return a concise Chinese summary with these headings:

```
周围环境摘要
地形/空间:
资源:
气体/液体:
可采集/食物:
危险/限制:
扩张方向:
建议下一步:
```

Keep each heading to 1-3 bullets. Mention coordinates only when they help action.

## Triage Heuristics

Prioritize what matters in early ONI:

- breathable space and CO2 pooling
- nearby water or problematic liquids
- algae/oxylite/organic solids/metal ore availability
- wild food/harvestables and seed opportunities
- heat, vacuum, slime/polluted oxygen, hostile critters, unreachable pockets
- left/right/up/down expansion quality
- whether printing-pod-adjacent space can become lab/core base

When uncertain, say what is unknown and recommend one focused follow-up scan instead of broad tool spam.

## Safety

Allowed:

- read-only tools
- `edit_mark_request_list`
- `world_area_snapshot`
- `world_text_map`
- inventory/farming read tools

Not allowed:

- `orders_*`
- `buildings_plan*`
- `game_resume`
- config/write/execute tools

If the user asks for actions after the summary, switch to planning/build-control skills and dry-run before executing.
