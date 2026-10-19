---
name: oni-mcp-control
description: Control Oxygen Not Included through Oni MCP with a strict observe-plan-act-verify loop. Use when reading a live colony, planning or executing gameplay actions, editing the virtual world files, managing duplicants or colony settings, inspecting maps and utilities, or recovering from MCP control errors; do not use for game-advice-only questions.
---

# ONI MCP Control

## Strategy reference routing

For colony progression or module selection, read [the colony strategy skill](../oni-mcp-colony-strategy/SKILL.md), then only the relevant topic reference. Cite the original article for the selected design and check its version, DLC and startup conditions. This supplements planning; it does not replace the world-editor protocol, authorize actions, or establish live-game compatibility.


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

Check `tools/list` before using a tool. The normal legacy/default **seven-tool core** contains `benchmark`, `building_control`, `navigation_control`, `game_control`, `orders_control`, `server_control`, and `world_editor`.

`colony_control`, `dupes_control`, `read_control`, and `search_control` are internal operations on this surface. **Do not call them directly or put them in a `server_control` batch** unless the current `tools/list` explicitly advertises them. A catalog entry or a typed operation-file listing does not make a hidden tool public.

Read live state with `world_editor command=read path=/active/colony/status.oni profile=minimal`. Read a small map with `world_editor command=read path=/active/map/viewport.md compact=true syncView=false focusCamera=false`; read an exact cell with `/active/map/cell_X_Y.md`. Use `world_editor command=symbols` for authoritative glyph lookup. Read `/active/ops/tools.md` before working with typed operation files.

`building_control` performs planning, materials, configuration, storage, and building actions. `orders_control` issues normal designations. `game_control` controls speed/save/game state; `navigation_control` controls view/screenshot; `server_control` provides diagnostics/catalog and batches of **advertised** tools.

`coordinate_control` is not public. Gameplay aggregate tools reject raw coordinates. Use semantic `areaId`/query/plan inputs, or exact coordinates inside supported `world_editor` maps and typed operation files. Camera-only `navigation_control` actions accept their advertised position, pan, and screenshot coordinates. Use `navigation_control action=set_zoom zoom=23` to zoom without changing the center; larger zoom values show a wider area.

For utilities, cut the exact layer with `orders_control domain=designation action=cut_conduits type=wire|liquid|gas|solid|logic|travel_tube ... confirm=true`. For normal buildings, prefer `orders_control domain=designation action=deconstruct id=<instanceId> confirm=true`. Preview ambiguous targets before writing.

## Mode selection

| User intent | First move | Result |
|---|---|---|
| Diagnose a problem | Minimal snapshot plus one targeted diagnostic read | Ranked issues and evidence |
| Ask what to do | Catalog guide plus bounded current-state reads | Short plan with verification |
| Execute an authorized action | Dry-run or preflight, then confirm | Applied result plus verification |
| Monitor | Minimal delta snapshot with narrow watches | State change only |

If execution fails, return to diagnosis. Do not repeat the same zero-effect call.

## Observation discipline

Start with one compact, publicly reachable live-state read:

```text
world_editor command=read path=/active/colony/status.oni profile=minimal
```

Escalate only the flagged area:

- food: `world_editor command=read path=/active/resources/food.oni`
- power: `world_editor command=read path=/active/infrastructure/power.md compact=true`
- rooms: `world_editor command=read path=/active/infrastructure/rooms.oni`
- heat: `world_editor command=read path=/active/map/viewport.md view=temperature compact=true syncView=false focusCamera=false`
- duplicants: `world_editor command=read path=/active/dupes/index.md`, then a named detail file
- exact terrain and utilities: `world_editor command=read path=/active/map/viewport.md compact=true syncView=false focusCamera=false`, or an exact cell file

`world_editor command=read path=/active/index.md includeState=true` is also available for a one-call world index and embedded colony status. Do not repeat an identical read without a state change; narrow the scope instead.

## Mandatory glyph lookup

Before interpreting a map glyph, generating a glyph from a building/element/entity name, or reading connection/overlay glyphs, announce in `commentary` that this skill requires an authoritative glyph lookup. Then call:

```text
world_editor command=symbols queries=["<all unknown glyphs or names>"] direction=auto
```

- Batch every unknown glyph/name from the current map in one call; do not issue one lookup per cell.
- Do not guess from Chinese characters, an old legend, or memory. Unknown results with `count=0` remain unknown.
- Use `view=<overlay>` to disambiguate context-sensitive overlay meanings.
- Reuse results already queried in the same runtime and same turn.

Examples:

```text
world_editor command=symbols queries=["砖","?","┼"] direction=code_to_meaning matchMode=auto view=logic
world_editor command=symbols queries=["砖块","氧气","研究站"] direction=meaning_to_code matchMode=auto
world_editor command=symbols queries=["零","■","晒","美","微","低","枯"] direction=auto perQueryLimit=20
```

## Spatial and camera discipline

- Prefer `world_editor command=search`, virtual-file maps, or exact cell files for decisions.
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

## Normal gameplay only

OniMcp creates ordinary blueprints and chores. Research, materials, support, visibility,
and native eligibility checks still apply. Sandbox routes, forced completion, spawning,
and direct terrain mutation are removed. Nested payloads cannot enable them.
The editor suppresses global debug instant-build during each request and restores its
previous state afterward. Builds refuse an active sandbox save rather than using its
free construction rules.

Read completed building parameters through `/active/buildings/index.md`, then open the
stable `/active/buildings/instances/<prefab>-<InstanceID>.md` file. Edit only canonical lines.

For gameplay, pause, read a small relevant region, preview one change, and submit native
orders. Let the game run briefly, pause again, and verify the same region. A blueprint,
chore, or `phase=orders_queued` is not a completed building or functioning network.

## Execution gates

- Preview risky or exact edits first: `dryRun=true` and no confirmation.
- Execute only with `dryRun=false confirm=true` after reviewing the translated plan.
- `server_control domain=batch action=call_many` supports up to 20 advertised tools. Never batch internal tool names in the seven-tool core. Use `requireAllValid=true` and `stopOnError=true` for coordinated writes.
- Do not batch dependent reads whose later arguments require an earlier result.
- Keep response payloads compact: `responseMode=summary` normally, `errors` for retry loops, `full` only for child details.
- A successful request can still be partial. Inspect `changedCells`, `applied`, `failed`, `partial`, `submittedCells`, `unsubmittedCells`, `phase`, and per-item results. Counts reported by child tools are actions, not necessarily map cells.

## Verification patterns

Choose verification before acting:

| Action | Verify with |
|---|---|
| Map build/dig/order | Re-read the exact map rectangle or matching layer |
| Power change | `/active/infrastructure/power.md` plus exact cell details if disconnected |
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
- Use one-shot `/active/colony/status.oni` minimal snapshots instead of assembling many broad reads.
- Batch independent reads; serialize dependent calls.
- Preserve low token use with compact details, narrow rectangles, and RLE only for read-only scans. Use expanded edit format for patches.
