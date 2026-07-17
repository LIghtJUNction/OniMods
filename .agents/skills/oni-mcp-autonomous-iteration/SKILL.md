---
name: oni-mcp-autonomous-iteration
description: Use when iterating on the ONI MCP server so an agent can build with onim, deploy with onim dev, automatically launch/load a save, run batched runtime tests with a tester agent, and improve tool ergonomics toward long autonomous play.
---

# ONI MCP Autonomous Iteration

Use this workflow for end-to-end ONI MCP server improvement, especially when the goal is to make test agents control Oxygen Not Included with minimal manual steering.

## Operating Loop

1. Inspect current state first.
   - Run `git status --short`.
   - Check whether ONI is already running and whether `http://localhost:8788/mcp/` responds.
   - Read the current MCP catalog with `server_control domain=catalog action=manifest` or a targeted catalog search before assuming schemas.

2. Batch implementation before restarting ONI.
   - Prefer one implementation batch, then one `onim build`, one `onim dev`, one ONI restart, and one runtime test batch.
   - Do not restart for every small edit unless a stale DLL blocks testing.
   - Keep new files under 500 lines.
   - Use meaningful file names such as `GameLaunchTools.cs`, not numbered split files.

3. Build and deploy exactly.
   - Run `onim build`.
   - Run `git diff --check`.
   - Run `onim dev`.
   - Treat the deployed Dev mod DLL as stale until ONI is restarted or the runtime catalog proves the new schema loaded.

4. Launch/load the game through MCP.
   - Prefer `game_control domain=launch action=status limit=5`.
   - First call `game_control domain=launch action=start dryRun=true confirm=true index=0 resume=false`.
   - If dryRun is clean, call `game_control domain=launch action=start confirm=true index=0 resume=false`.
   - Verify with `colony_control domain=snapshot action=get profile=minimal`.
   - Keep the game paused during tests unless the test explicitly requires time passing.
   - For a full process restart on Linux, call `game_control domain=launch action=restart_load dryRun=true resume=false`, then repeat with `confirm=true`. The accepted response contains `jobId` and the exact saved path; after Steam relaunch, query `game_control domain=launch action=restart_status jobId=<id>` until `stage=loaded` or `stage=failed`.
   - `restart_load` is intentionally asynchronous across processes. Its relay carries only the old PID and the locally resolved absolute Steam executable, then launches Steam AppID 457140; it never carries the save path or MCP token.

5. Use low-token loop polling for long-run play.
   - Prefer `colony_control domain=snapshot action=get profile=minimal delta=true deltaKey=<loop> watch=stress,food_kcal,red_alert,alerts watchOnly=true`.
   - Treat `watch.alert=false`, `red_alert=false`, food above threshold, and low stress as the signal to continue.
   - Use `server_control domain=batch action=call_many responseMode=summary` for independent status reads.
   - In batch summaries, inspect `valid/failed/executed`, then child `summary.next`, `summary.tokenHint`, and count fields before asking for full output.

6. Run tester-agent feedback after deployment.
   - Give the tester only the task and current MCP endpoint assumptions, not your expected fixes.
   - Ask for a compact report: passed checks, blocking bugs, ergonomics problems, and highest-value next fixes.
   - Prefer read-only and `dryRun=true` tests first.
   - Include these areas when relevant: catalog schema, launch/status, snapshot, world search, sequence search, planning parse, build dryRun, orders dryRun metadata, power/wire auto-connect, and area handles.

7. Iterate from tester findings.
   - Fix issues that reduce one-call usability or agent clarity first.
   - Rebuild, redeploy, and rerun only the affected runtime checks.
   - Leave unrelated refactors for later unless they block autonomy.
   - Do not add lines to already oversized files unless the same batch also makes a meaningful semantic split.

## Runtime Checks

Prefer the bundled smoke script after MCP endpoint is online:

```bash
python .agents/skills/oni-mcp-autonomous-iteration/scripts/runtime_smoke.py
```

It verifies JSON-RPC initialize, `tools/list` schema, launch status, snapshot, planning parse, and world sequence search without requiring the tester agent to implement MCP session setup. It is a live smoke batch, not a pure read-only test; current calls are status/snapshot/parse/search checks and should not place orders.

For long-run survival validation, use:

```bash
python .agents/skills/oni-mcp-autonomous-iteration/scripts/survival_watch.py --target-cycles 100 --poll-seconds 20 --speed 3
```

Before a 100-cycle run, ask MCP for the survival plan:

```text
colony_control domain=survival action=plan targetCycles=100
```

Read `canAttemptLongRun`, `decision`, `blockers`, then execute `nextCalls` in order.

For quick validation without waiting a full cycle:

```bash
python .agents/skills/oni-mcp-autonomous-iteration/scripts/survival_watch.py --target-cycles 1 --max-seconds 10 --allow-partial --ignore-critical-diagnostics
```

It initializes MCP directly, ensures save is loaded, sets speed, polls `colony_control domain=snapshot action=get profile=minimal`, reports compact cycle/alert metadata, pauses end unless `--no-pause-at-end` passed. reports `alertLevel`, diagnostic alerts, `diagnosticsIgnored`, and `pausedAtEnd`; hard failures are no dupes, max stress 100%, red alert mode, critical/error alert levels, or critical diagnostics unless `--ignore-critical-diagnostics` is passed for script smoke testing.

Use these checks as a minimum smoke suite after a launch-related or planning-related change:

```text
server_control domain=diagnostics action=status detail=brief
game_control domain=launch action=status limit=5
game_control domain=launch action=start dryRun=true confirm=true index=0 resume=false
colony_control domain=snapshot action=get profile=minimal
building_control domain=planning action=parse_plan plan="粉砂岩砖@氧气" worldId=0
building_control domain=planning action=parse_plan plan="用粉砂岩建造砖块，锚点氧气" worldId=0
building_control domain=planning action=parse_plan plan="Build two sandstone tiles near the base" worldId=0
read_control domain=world action=search pattern="粉砂岩-泥土-氧气" direction=both matchMode=smart worldId=0 limit=3
building_control domain=planning action=build_area plan="粉砂岩砖@氧气" worldId=0 dryRun=true limit=3
server_control domain=batch action=call_many dryRun=true responseMode=summary calls=[...]
```

Expected planning behavior:

- `粉砂岩砖@氧气` resolves to `prefabId=Tile`, `material=SiltStone`, `query=氧气`.
- `Build two sandstone tiles near the base` resolves to `prefabId=Tile`, `material=SandStone`, `query=base`.
- Natural anchor forms such as `锚点氧气`, `靠近电池`, `目标厕所`, and `@氧气` should produce an anchor query.
- A dryRun failure is acceptable when it reports the true game reason, such as unavailable material, unreachable target, obstruction, or missing support.

## ONI Process Handling

Use the bundled Steam-only launcher helper:

```bash
bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
```

If a running ONI PID already has a healthy MCP endpoint, it returns immediately without touching `unity.lock` or Steam. Otherwise it clears stale `unity.lock`, requires the Steam client, and launches fixed AppID 457140 through the Steam URI only; the AppID is not configurable. If MCP is offline while old ONI PIDs still exist, it waits for the full old PID set to exit before sending the URI; after the request, success requires a live PID not present before launch plus a healthy `http://localhost:8788/mcp/`. It prints compact diagnostics on failure and never falls back to launching the ONI binary directly. Useful overrides:

```bash
ONI_MCP_WAIT_SECONDS=360 bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
ONI_OLD_PROCESS_EXIT_WAIT_SECONDS=60 bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
```

Use Steam launch only:

```bash
pidof OxygenNotIncluded | xargs -r kill
rm -f ~/.local/share/Steam/steamapps/common/OxygenNotIncluded/unity.lock
steam steam://run/457140 >/tmp/oni-steam-launch.log 2>&1 &
```

Wait for both the process and MCP endpoint:

```bash
pidof OxygenNotIncluded
curl -fsS --max-time 2 http://localhost:8788/mcp/
```

Avoid `pgrep -f OxygenNotIncluded` in kill commands because it can match the shell command itself.

## Tester Report Format

Ask the tester to return:

```markdown
## Result
- Overall: pass/fail
- Loaded save: yes/no
- Paused after launch: yes/no

## Passed
- ...

## Bugs
- Severity, tool call, observed result, expected result

## Ergonomics
- Places where the agent still needed coordinates, repeated calls, or hidden knowledge

## Next Fixes
- Ordered list of fixes by autonomy impact
```

## Long-Run Goal

Optimize toward a tester agent surviving 100 cycles with low manual steering:

- Prefer semantic search and reusable area handles over raw coordinates.
- Every action dryRun should explain reachability, material availability, risk, and next action.
- Every write/action path that creates dupe work must include priority handling. Set priority directly when supported, or return compact `priorityAction`/`nextActions` using existing order/priority endpoints. Critical survival work such as access ladders, food, oxygen, toilets, reachable material digs, prerequisite sweeps, and rescue paths should default high priority, usually 7, unless user specified another value.
- Tool schemas should expose all supported parameters at the aggregate entrypoint.
- One-call search/action paths should return compact metadata that lets the next agent decide without reading large maps.
- Printing pod rewards must use existing `building_control domain=side_surface surface=facility kind=printing_pod`, never a new public tool. Use `action=list_rewards`, then `action=claim rewardIndex=N dryRun=true`, then `confirm=true` only when explicitly consuming the reward. After claiming survival-critical material/food/oxygen, immediately plan or return sweep/storage/build priority follow-up so the reward actually helps the colony.
