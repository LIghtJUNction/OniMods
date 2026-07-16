---
name: oni-mcp-control
description: Control Oxygen Not Included through Oni MCP with a strict observe-plan-act-verify loop. Use when reading a live colony, planning or executing gameplay actions, editing the virtual world files, managing duplicants or colony settings, inspecting maps and utilities, or recovering from MCP control errors; do not use for game-advice-only questions.
---

# ONI MCP Control

## Reference routing

Keep this file loaded for every control task. Load only the reference needed for the current operation:

- Read [references/world-editor.md](references/world-editor.md) **before any** `world_editor` map edit, off-screen framing, operation-file command, management edit, plan, blueprint, or batch. It contains the exact virtual-file protocol and examples. 中文版: [references/world-editor.zh.md](references/world-editor.zh.md)
- Read [references/tool-reference.md](references/tool-reference.md) when selecting among aggregate tools, resources, prompts, parameters, tool categories, or standard multi-tool workflows. It contains the detailed tables and quick reference. 中文版: [references/tool-reference.zh.md](references/tool-reference.zh.md)

Do not duplicate those references into the working context unless the task requires them.

## Control contract

Run a complete control loop:

1. **Observe** the smallest relevant live state.
2. **Orient** around hazards, reachability, dependencies, and the user's scope.
3. **Decide** on a bounded target state and verification method.
4. **Act** through a semantic aggregate tool or reviewed virtual-file edit.
5. **Verify** with an independent targeted read.

Never act from stale assumptions. Never report success from a write response alone.

For autonomous play, keep the game paused while reading, planning, or issuing commands. Resume only for a short bounded window, pause again, and verify. Direct user instructions override viewer suggestions; gameplay safety overrides speed.

## Scope and authorization

- Distinguish advice from control. Read-only inspection is allowed for diagnosis; do not mutate the save unless the user authorizes gameplay action.
- Respect marked boundaries, reserved areas, and "do not move the view" or "do not code" instructions literally.
- Do not use sandbox/debug spawning or cheat resources during normal play.
- Printing Pod rewards default to items; do not select a new duplicant unless the user explicitly asks.
- For liquid-adjacent digs, trapped duplicants, broad rectangles, or irreversible edits, pause and inspect exact cells before acting.
- If livestream or OBS health fails during a live session, pause gameplay and restore the stream before continuing.

## Public tool policy

Use the compact aggregate surface:

- `colony_control`: colony snapshots, diagnostics, management, research, schedules.
- `dupes_control`: duplicant details, priorities, skills, equipment, commands.
- `read_control`: resources, buildings, infrastructure, world maps, reusable areas.
- `building_control`: planning, materials, configuration, storage, automation, special buildings.
- `orders_control`: dig, mop, sweep, disinfect, cancel, harvest, deconstruct, capture, conduit cuts.
- `game_control`: pause, speed, save, DLC, state, supported sandbox actions.
- `navigation_control`: camera, overlays, and screenshots only.
- `search_control`: semantic world/object search and discovery execution.
- `server_control`: discovery, diagnostics, and batching.
- `world_editor`: virtual-file reads and edits.

`coordinate_control` is not public. Ordinary aggregate tools reject raw coordinates. Use semantic `areaId`/query/plan inputs, or exact coordinates inside supported `world_editor` map and typed-operation files.

For utilities, cut the exact layer with:

`orders_control domain=designation action=cut_conduits type=wire|liquid|gas|solid|logic|travel_tube ... confirm=true`

For a normal building, prefer `orders_control domain=designation action=deconstruct id=<instanceId> confirm=true`. If a cell contains multiple candidates, list or dry-run first.

## Mode selection

| User intent | First move | Result |
|---|---|---|
| Diagnose a problem | Minimal snapshot plus one targeted diagnostic read | Ranked issues and evidence |
| Ask what to do | Catalog guide plus bounded current-state reads | Short plan with verification |
| Execute an authorized action | Dry-run or preflight, then confirm | Applied result plus verification |
| Monitor | Minimal delta snapshot with narrow watches | State change only |

If execution fails, return to diagnosis. Do not repeat the same zero-effect call.

## Observation discipline

Start compact:

```text
colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
```

Escalate only the flagged domain:

- food: `read_control domain=resources action=food`
- power: `read_control domain=infrastructure action=power_summary`
- rooms: `read_control domain=infrastructure action=rooms`
- heat: `read_control domain=world action=thermal_overheat_risk`
- duplicant health/pathing: `dupes_control domain=info action=status_check`
- exact terrain or utilities: virtual map or `read_control domain=world action=area_snapshot|text_map`

Do not call the same read with identical arguments twice in a row. State what is missing and switch to a narrower read or discovery call.

## Spatial and camera discipline

- Prefer semantic search, virtual-file maps, `area_snapshot`, or `text_map` for exact decisions.
- Use `navigation_control` only when the user asks to look somewhere or a visual overlay/screenshot materially helps.
- Do not move or focus the camera merely to edit an off-screen rectangle. Use the non-camera framing workflow in `references/world-editor.md`.
- After an intentional camera move, call `navigation_control action=get_view` when exact confirmation matters.
- Use `navigation_control action=switch_view screenshot=true` for a visual overlay capture; use `view=none` for a normal screenshot.
- Screenshots are asynchronous. Honor `readyAfterFrames` before fetching the HTTP URL. Prefer `coordinate_screenshot` when later reasoning references cells.
- Structured maps, not screenshots, are authoritative for digging, building, piping, wiring, and deconstruction coordinates.

## Build and order discipline

For construction:

1. Resolve the prefab with `building_control domain=planning action=search_defs`.
2. Resolve valid materials with `building_control domain=planning action=materials`.
3. Check support, obstruction, and placement constraints with `placement_candidates` or a map-edit dry-run.
4. Use semantic `areaId`/plan placement, or exact map tokens through `world_editor`.
5. Re-read the target area.

For dig, sweep, mop, disinfect, cancel, harvest, capture, deconstruct, and utility cuts, use `orders_control` or `/active/ops/orders.md`; never simulate UI clicks.

Use sweep only for debris/pickupables. Use mop for liquid cells and spills.

## Sandbox and instant-build behavior

Current source treats a building as research-available when either `Game.Instance.SandboxModeActive` or `DebugHandler.InstantBuildMode` is active. Normal gameplay still requires completed research.

If an already-running sandbox/instant-build runtime reports `Building is locked by research`, treat the loaded DLL as stale. Do **not** queue research merely to satisfy a sandbox construction test. Report that the runtime needs a later safe reload; never restart the game unless explicitly authorized.

## Execution gates

- Preview risky or exact edits first: `dryRun=true` and no confirmation.
- Execute only with `dryRun=false confirm=true` after reviewing the translated plan.
- `server_control domain=batch action=call_many` supports up to 20 items. Use `requireAllValid=true` and `stopOnError=true` for coordinated writes.
- Do not batch dependent reads whose later arguments require an earlier result.
- Keep response payloads compact: `responseMode=summary` normally, `errors` for retry loops, `full` only for child details.
- A successful request can still be partial. Inspect `changedCells`, `applied`, `failed`, `partial`, `remainingCells`, and per-item results.

## Verification patterns

Choose verification before acting:

| Action | Verify with |
|---|---|
| Map build/dig/order | Re-read the exact map rectangle or matching layer |
| Power change | `power_summary` plus exact `power_ports` if disconnected |
| Schedule or policy | Re-list the same management object |
| Duplicant update | Re-read the duplicant or priority/skill list |
| Building configuration | Re-list configuration for that building ID |
| Camera/overlay | `get_view` or screenshot after `readyAfterFrames` |

Verify the effect, not merely the existence of a chore or blueprint, unless the user explicitly says the designation itself is the test target.

## Error recovery

### Schema or tool mismatch

1. Read the exact error.
2. Search the current catalog: `server_control domain=catalog action=search query=<intent> detail=brief`.
3. Follow the returned schema; do not invent a nearby tool name.

### Zero-effect or stale write

1. Do not repeat it.
2. Re-read the smallest target state.
3. Resolve changed IDs, coordinates, reachability, materials, or support.
4. Submit a corrected dry-run.

### Map preflight mismatch

Re-read and regenerate the patch. For explicit rectangles, use the three-header protocol in `references/world-editor.md`; do not fall back to guessed viewport offsets.

### Timeout

Check task state if a task ID was returned. Otherwise narrow the area, limit, or response detail before retrying.

### Confirmation rejection

Add `confirm=true` only after the user has authorized the mutation and the dry-run is acceptable.

## Risk checklist

Before a medium or dangerous action, confirm:

- [ ] Current target state was read.
- [ ] User authorization covers the mutation.
- [ ] Exact IDs, world, rectangle, support, and utility layer are correct.
- [ ] Broad or hazardous work was dry-run.
- [ ] `confirm=true` is present only on the execution call.
- [ ] A targeted verification read is ready.
- [ ] Complex spatial work is paused.

## Efficiency rules

- Cache catalog results for the loaded runtime, but invalidate assumptions after a reload.
- Invalidate related state after every write/execute call.
- Reuse bounded `areaId` handles for repeated reads and actions; forget temporary custom areas afterward.
- Use one-shot minimal snapshots instead of assembling many broad reads.
- Batch independent reads; serialize dependent calls.
- Preserve low token use with compact details, narrow rectangles, and RLE only for read-only scans. Use expanded edit format for patches.
