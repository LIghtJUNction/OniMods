---
name: oni-mcp-control
description: Control Oxygen Not Included through Oni MCP, including authorized autonomous play, bounded event-driven waits, colony inspection, construction and virtual-file edits. Reuse discovered interfaces and overlap static research with bounded gameplay; use a game-advisor skill for advice without control.
---

# ONI MCP Control

## Load once, then read by need

This is the control entry point. Read it once per session and reuse it until the instructions change.

- For autonomous play, reducing tool round trips, or researching while the game runs, read [interaction.md](references/interaction.md) once. It defines cached discovery, batching and bounded asynchronous waits.
- Before the first map/operation/management edit or off-screen framing, read one language of [world-editor.md](references/world-editor.md) / [world-editor.zh.md](references/world-editor.zh.md). Reuse the protocol afterward.
- For an unfamiliar action or parameter, read only the relevant section of [tool-reference.md](references/tool-reference.md) / [tool-reference.zh.md](references/tool-reference.zh.md).
- For construction blockers, deferred builds, task candidates, bionic needs or historical wait results, read the relevant section of [task feedback](../../../docs/gameplay-task-feedback.md).
- For module selection, use [colony strategy](../oni-mcp-colony-strategy/SKILL.md), then the current bottleneck's reference. Check the original design's startup conditions, version and DLC.
- Load [recording and archive notes](references/recording-and-archive.md) only when recording or archiving is explicitly requested.

Do not preload the reference library or inspect implementation source during ordinary gameplay. Enter source-level diagnosis only when a concrete error cannot be resolved from the public interface and these references.

## First connection and interface cache

Use `http://127.0.0.1:8788/mcp/`. Establish the MCP session and read `tools/list` once; reuse it until reconnection, protocol changes, a mod reload or a schema mismatch.

The legacy/default surface exposes seven tools: `benchmark`, `building_control`, `navigation_control`, `game_control`, `orders_control`, `server_control`, `world_editor`. Other protocol surfaces may advertise fewer. Call only advertised tools, including inside batches. Internal `colony_control`, `dupes_control`, `read_control`, `search_control` and `coordinate_control` are unavailable unless explicitly advertised.

Every tool call needs a nonempty `task` describing its purpose. Example after initialization:

```bash
rtk proxy mcporter call --http-url http://127.0.0.1:8788/mcp/ --allow-http --tool world_editor --args '{"command":"read","path":"/active/colony/status.oni","profile":"minimal","task":"检查基地当前状态"}' --timeout 45000 --output json
```

Direct HTTP calls need `Content-Type: application/json`, `Accept: application/json, text/event-stream`, the negotiated `Mcp-Protocol-Version`, and the session ID returned by initialization. Do not repeat the handshake for each action.

## Live control and authority

- Gameplay writes require the user's authorization. Advice or skill maintenance does not authorize launching or controlling the game.
- Pause before inspecting live state for a decision, changing priorities or issuing orders. Before resuming, prepare useful work and a finite `step_until` window.
- During an authorized bounded run, helpers may read static references and previously captured snapshots. Treat these snapshots as historical; pause and refresh the affected live region before committing a decision based on them.
- Keep one game controller. Helpers research or review; they do not independently pause, resume or mutate the same save.
- Follow marked boundaries and instructions about the view or coding. Launch ONI through Steam only. Use normal blueprints/chores, without spawning, instant construction or debug resources.
- Inspect exact cells and dry-run liquid-adjacent digs, trapped-duplicant rescues, broad edits and other irreversible work.
- Prefer Printing Pod care packages. Add duplicants only when explicitly requested; do not add them while food is unstable.
- Monitor livestream health only in an actual livestream task.

## Smallest useful read

Use a previous completion result if it already answers the question. Otherwise read one compact entry and follow only a flagged subsystem:

| Need | Public virtual file |
| --- | --- |
| Colony baseline | `/active/colony/status.oni profile=minimal` |
| Index plus embedded state | `/active/index.md includeState=true` |
| Local map | `/active/map/viewport.md compact=true syncView=false focusCamera=false` |
| Exact cell and hidden liquid | `/active/map/cell_X_Y.md` |
| Duplicants | `/active/dupes/index.oni`, then a named detail |
| Stock / food | `/active/resources/inventory.oni` / `/active/resources/food.oni` |
| Power / rooms | `/active/infrastructure/power.md` / `/active/infrastructure/rooms.oni` |
| Completed building | `/active/buildings/index.md`, then its listed `/active/buildings/instances/<prefab>-<InstanceID>.md` file |

These are `world_editor command=read path=...` calls; options shown beside a path are separate arguments. Do not reread every row on every stop. Invalidate relevant live facts after a write, time advance, load or world change. Static definitions and glyph mappings can remain cached when their metadata is unchanged.

## Map, glyph and construction rules

Before interpreting unknown map glyphs or names, state in commentary that authoritative lookup is required. Do not guess meanings from glyph shapes, Chinese characters or cached examples. Read unknown glyphs/names together with `world_editor command=symbols queries=[...] direction=auto`; include `view` for overlays. Reuse verified mappings across the session; zero matches and ambiguous matches remain unknown.

The same public tool supports both directions. Use these as query shapes, not as preverified meanings:

```text
world_editor command=symbols queries=["┼"] direction=code_to_meaning view=logic task="解释连接字码"
world_editor command=symbols queries=["氧气","研究站"] direction=meaning_to_code task="查询名称对应字码"
```

Use semantic IDs/queries/areas for aggregate tools. Exact edits use reviewed world-editor map tokens or typed operation files. Camera coordinates are for camera operations. Do not pan the camera just to edit off-screen; the map protocol supports non-camera framing. Larger `navigation_control action=set_zoom zoom=...` values show a wider area.

For a building, resolve only an uncached prefab, then use its placement dry-run to obtain materials, support, obstruction and reachability diagnostics together. Call the material diagnostic separately only if the preview leaves a relevant question unanswered. Natural cells can return a deferred build handle: follow its status rather than repeatedly reissuing digs and blueprints. A blueprint or `orders_queued` result does not prove construction.

Use `mop` for spills and `sweep` for debris. Never use `attack` to dig. Cut utility layers with `cut_conduits type=wire|liquid|gas|solid|logic|travel_tube`; deconstruct ordinary buildings by their observed instance ID.

## Writes, waits and recovery

- Preview exact or risky edits. Apply with `dryRun=false confirm=true` only when authorized and the preflight is understood.
- Batch up to 20 known, independent calls with `server_control domain=batch action=call_many requireAllValid=true stopOnError=true`. Prevalidation is not a transaction and does not replace per-call failures or partial-result checks.
- Keep `step_until wait=true` outside `call_many`: the batch does not await a child's deferred completion. See the interaction reference for overlapping research without polling.
- Default tool text is TOON; MCP envelopes and structured content remain JSON. Request compact summaries for routine results and full details for a specific unresolved error.
- Read `stopReason`, elapsed time, critical alerts, tracked-work states and Printing Pod readiness before the next action. Check the target once if the result does not prove its current state.
- On a schema error, search only that action with `server_control domain=catalog action=search query=... detail=brief`; reuse the correction.
- On a stale patch or zero-effect action, refresh the smallest affected target and fix the blocker. Do not repeat the same action or keep increasing priority without evidence.
- A short run can stop on an existing alert. It does not prove a native pause-counter fault; do not toggle the Spacebar to repair a guessed cause. Inspect the stop result and any confirmed native menu.
- On a timeout, inspect a returned request ID before retrying. Cancellation and workflow failure can remove pending work; zero remaining orders alone is not success.

Report the completed change, current blocker or meaningful event. Do not narrate every lookup or ask again for actions already covered by the user's authorization.
