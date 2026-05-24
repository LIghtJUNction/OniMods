---
name: oni-mcp-plan-intake
description: Use when the user gives a natural-language ONI plan, uses the mod's in-game edit marker/planning tool, creates an edit_mark_request, gives a build order, coordinate scheme, phased colony plan, or asks the agent to read/understand/record a plan before acting. Guides the agent to call edit_mark_request_list first for player-created planning requests, then gather context, parse into plan_harness records, validate tool calls, and avoid executing unless explicitly authorized.
---

# ONI MCP Plan Intake

## Purpose

Use this skill when the user gives a plan or asks to turn text into an executable/validated ONI MCP plan.

The mod has a dedicated player planning intake tool: `edit_mark_request_list`. When the player uses the in-game MCP edit marker to box-select an area and type a request, that request is stored there with `prompt`, `areaId`, `rect`, `worldId`, `textMap`, and a workflow hint. The agent must read it before inventing a plan.

The job is to preserve the player's intent, gather just enough current game/tool context, record the plan in `plan_harness`, and validate it. Do not control the game unless the user explicitly asks to execute.

## Player Planning Tool

Primary tool:

```
edit_mark_request_list limit=5
```

Use it first whenever:

- the user says they made/marked/selected a plan in-game
- the user says "读取规划", "看我框选的规划", "按我游戏里标的来"
- an edit marker notification exists
- the prompt refers to "这个区域", "框选区域", "玩家规划", or "标记"

Each request contains:

- `id`: request id
- `prompt`: player-written plan/request
- `areaId`, `rect`, `worldId`: spatial anchor
- `textMap`: exact grid context; use this before screenshots
- `screenshotPath`: optional visual evidence only
- `workflow`: server-provided rule that the agent must plan first

After consuming a request, do not clear it until the plan has been recorded and the user no longer needs it. Use `edit_mark_request_clear id=<id>` only after successful intake or explicit user instruction.

## Default Flow

```
1. Read player planning requests first:
   edit_mark_request_list limit=5
   If a relevant request exists, use its prompt/areaId/rect/textMap as primary input.

2. Read current state:
   colony_state_snapshot profile=brief

3. Read existing plans:
   plan_harness_list limit=5
   plan_harness_get id=<relevant plan> if continuing an existing plan

4. Read tool context for the plan domain:
   tools_guide goal=<user plan summary>
   tools_search detail=brief query=<key action/building/order>

5. Read spatial context:
   If edit_mark request has textMap, use it first.
   If more detail is needed, call world_area_snapshot areaId=<request.areaId> preset=construction|utilities encoding=rle.

6. Create or update harness:
   plan_harness_create objective=<user intent> constraints=[...] riskTolerance=low requireVerification=true
   plan_harness_record stage=plan summary=<short summary> payload={ editMarkRequest?, userPlan, assumptions, plannedCalls? }

7. Parse and validate if calls are present:
   plan_harness_parse planText=<structured plan text> validate=true
   plan_harness_validate id=<planId>

8. Stop with a concise status:
   plan id, what was understood, missing info, validation issues, next safe action.
```

## Intake Rules

- Treat user/player text as authoritative intent, not as proof that coordinates/tools are valid.
- If `edit_mark_request_list` returns a relevant request, treat `request.prompt` as the primary plan text and `request.textMap` as the primary spatial context.
- Preserve raw user wording in `payload.userPlan`.
- Preserve raw edit marker request in `payload.editMarkRequest` or at least `{id, prompt, areaId, rect, worldId}`.
- Record assumptions explicitly in `payload.assumptions`.
- Prefer `colony_state_snapshot` over many separate status tools.
- Prefer `world_area_snapshot` over screenshots for map/coordinate planning.
- Use `tools_guide` and `tools_search` before choosing unfamiliar tool names.
- Use `plan_harness_parse` only for structured JSON or line-form tool calls. It is not a natural-language planner.
- If the user gives natural language only, write a structured plan record first; generate `plannedCalls` only after reading context.

## Execution Gate

Do not call these unless the user explicitly says to execute/apply/do it:

- `plan_harness_execute`
- `orders_*`
- `buildings_plan*`
- `game_resume`, `game_set_speed`
- any write/execute tool that changes game state

Allowed before explicit execution:

- read tools
- `edit_mark_request_list`
- `edit_mark_request_clear` only after successful intake or explicit instruction
- `plan_harness_create`
- `plan_harness_record`
- `plan_harness_parse`
- `plan_harness_validate`
- `buildings_plan* dryRun=true` only when validating candidate blueprints

## Planning Heuristics

For edit marker requests:
- Use `request.textMap.Text` first. It is generated from `world_text_map`.
- Use `request.areaId` for follow-up `world_area_snapshot` and later verification.
- Include `request.id` in the plan harness payload so the request can be traced.
- If multiple requests exist, pick the newest relevant one; if ambiguous, summarize candidates and ask which one to process.

For construction plans:
- Snapshot first with `preset=construction`.
- For power, pipes, automation, or conveyors use `preset=utilities`.
- Check support/floor needs before planned machinery/furniture.
- Use `buildings_plan_many dryRun=true` for candidate calls; put support tiles before floor-bound buildings.

For area orders:
- Digging uses `orders_dig_area`, never `orders_attack`.
- Sweeping uses `orders_sweep_area`.
- Cancel uses `orders_cancel_area`.
- Harvest uses `orders_harvest_area`.

For tool-call text, prefer JSON:

```json
{
  "plannedCalls": [
    { "name": "world_area_snapshot", "arguments": { "x1": 70, "y1": 134, "x2": 90, "y2": 145, "preset": "construction" } }
  ]
}
```

Compact line form is also valid:

```text
world_area_snapshot {"x1":70,"y1":134,"x2":90,"y2":145,"preset":"construction"}
buildings_plan_many {"dryRun":true,"confirm":true,"items":[{"p":"Tile","l":[74,135,79,135]}]}
```

## Final Response Shape

Keep the final response short:

- `editMarkRequest`: id if one was read
- `planId`: id if created/updated
- `understood`: one sentence
- `validated`: pass/fail/partial
- `issues`: only blocking issues
- `next`: the next safe tool or confirmation needed
