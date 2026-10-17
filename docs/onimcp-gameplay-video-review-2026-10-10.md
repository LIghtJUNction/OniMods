# OniMcp gameplay video review — 2026-10-10

## Evidence and scope

The user supplied `1000040420.mp4` (4:25, 1280×720). Times below are positions in the edited video, not elapsed game time or tool timings. The video includes cuts and accelerated footage. Narration and chat text are not server traces. They cannot establish whether the model, network, a tool, or the game caused the delay. The deployed mod revision is not identified.

| Video time | Evidence | Improvement |
| --- | --- | --- |
| 00:25–00:55 | Narration describes repeated observe/decide/call cycles; opening orders are visible. | Return short observations by default. Do not repeat the complete tool guide on every status read. |
| 01:05–01:15 | Narration reports leaving the game to load an updated mod after issuing orders. | Keep a fixed candidate for a play session. Do not mix software update time with gameplay time. This change does not add live DLL replacement. |
| 01:52–02:07 | Chat shown at 01:58–02:02 says plants occupy roof cells and research/power equipment lacks material. | Inspect pending construction after a short run. Before placing more work, check exact cells, access and available materials. |
| 02:38–02:48 | The dig tooltip shows `Colony Lacks Hard Digging Skill`; narration explains the missing skill. | Read the actual dig/work status and skill panel. Raising priority does not give a duplicant the missing skill. |
| 02:52–02:58 | Falling material and an `Entombed` status are visible; narration later reports the room recognized as a great hall. | Check overhead loose terrain before digging. Verify the game's room classification and building condition after construction. |

The completed-building index in `WorldEditorBuildingFiles.cs` does not list pending construction. `WorldEditorActiveIndex.cs` previously called its full options, next-call guide and editable-file catalog on every read. These are source findings, independent of the unknown video deployment revision.

## Implemented read interface

Use the existing tool and virtual file. No new top-level tool is registered.

```json
{
  "task": "Check unfinished construction before issuing more orders",
  "command": "read",
  "path": "/active/index.md",
  "format": "progress",
  "query": "Tile",
  "limit": 20,
  "offset": 0
}
```

`query` is optional; omit it to include all visible pending construction in the active world. Keep the game paused and preserve the same query between pages. `limit` is 1–100, default 20. `offset` is a non-negative integer. Neither accepts silently rounded values or numeric strings. Use the returned next offset rather than guessing that a full page contains all work.

The report separates total pending blueprints, matches and returned rows. Rows carry the native instance ID, building prefab ID, cell and native main status. A single scene scan supplies the pending list; only the requested page reads native status labels. This is on demand, not a new per-frame scan or a scan for each map cell. It does not cache game objects across save/world changes.

The scope is **visible pending construction**, not all errands. Dig orders, worker skill requirements and every possible blocker are not claimed to be covered by the construction main status. Follow the exact-cell and reachability links when required. Missing status is `unknown`, not ready. A failed scan returns `observation=unavailable`, without an invented zero count. Zero pending blueprints does not prove completion: a plan can have been cancelled or never accepted.

The default index now keeps the current time, save, game state and active-world header, followed by short read options and verification guidance. `format=help` retains the file/command guide. `format=full` retains that guide plus expanded colony state. `includeState=true` remains available, including with the progress profile. Map `format=edit` is unchanged.

## Gameplay loop

Pause, observe, choose a small task, check its prerequisites, issue ordinary orders, run briefly, pause, and verify. A successful tool call is not proof that a duplicant finished the work. Do not repeatedly change priority or resend unchanged orders when a required skill, access path, plant removal or material is still missing.

For a room goal, verify completed buildings, the native room classification and any required operation. The progress report deliberately does not infer those conditions or mark the overall goal complete.

## Verification boundary

The Core executable links the production report and checks strict page arguments, filtered versus total counts, continuation, unknown status, failed observation, empty/out-of-range pages, overflow bounds, table escaping and explicitly truncated long status text. Existing Core tests remain in the same executable.

These tests do not execute Unity, the native construction scan or the native status API. The full mod must also compile against the repository's pinned reference assemblies. Neither host tests nor that historical reference build prove compatibility with the game shown in the video.

Before deployment, use a copied save and record the exact candidate revision and game build:

1. Read a visible unfinished Tile and Ladder. Check identity, position and main status against the game UI. Include a static status whose native data is null, where available.
2. Check filtering and multiple pages while paused. Complete and separately cancel a blueprint using normal gameplay, then read again; neither case alone must produce a task-complete claim.
3. Switch active worlds and load another save. No old or hidden object may remain in the report. Verify the read changes no orders, priorities, pause state or save data.
4. Compare default/help/full/progress profiles and `includeState=true`; verify the old map edit profile is unchanged.

No real ONI runtime validation is claimed by this report. CI results belong to the exact commit recorded in the pull request, not to a video with an unknown mod revision.

## Separate follow-up work

The broader planner should describe prerequisite work for plants, hard digging, worker access, loose overhead terrain and material delivery before proposing construction. That needs native-game tests; this read-only change does not add automatic prerequisite writes or bypass game rules. Map parsing and survival-only tool cleanup remain separate from this construction observation change.
