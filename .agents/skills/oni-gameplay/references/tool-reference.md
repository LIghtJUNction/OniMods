# ONI MCP Tool Reference

Read this reference when selecting tools, composing multi-tool workflows, choosing parameters, or mapping prompts and resources to aggregate calls.

## Contents

- [Discovery](#discovery)
- [Read, write, execute](#read-write-execute)
- [Standard workflows](#standard-workflows)
- [Parameter rules](#parameter-rules)
- [Batch and efficiency patterns](#batch-and-efficiency-patterns)
- [Tool categories](#tool-categories)
- [Prompt workflows](#prompt-workflows)
- [Resource URIs](#resource-uris)
- [Quick reference](#quick-reference)

## Discovery

| Goal | Call |
|---|---|
| Full public surface | `server_control domain=catalog action=manifest` |
| Filter by intent | `server_control domain=catalog action=search query=<intent> detail=brief` |
| Ask how to solve a goal | `server_control domain=catalog action=guide goal=<goal>` |
| Check coverage | `server_control domain=catalog action=coverage` |
| Audit safety metadata | `server_control domain=catalog action=static_audit` |

The manifest exposes the compact public aggregate surface. Treat legacy fine-grained names as compatibility entries, not preferred tools.

## Read, write, execute

- **Read:** state queries such as snapshots, duplicant details, resources, infrastructure, and maps. Retry-safe only while arguments and state are unchanged.
- **Write:** configuration such as schedules, priorities, filters, thresholds, names, and policies. Usually medium risk and requires confirmation.
- **Execute:** pause/resume, orders, deconstruction, construction, camera actions, saves, and other operations. Dangerous map mutations require confirmation.

Resources (`oni://...`) are cacheable snapshots. Tools are parameterized live operations. Prompts describe a standard chain but do not execute it.

## Standard workflows

### Colony health check

```text
colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts
colony_control domain=snapshot action=get profile=brief        # only when minimal flags concern
read_control domain=resources action=food                      # when food detail is needed
read_control domain=infrastructure action=power_summary        # when power is flagged
read_control domain=world action=thermal_overheat_risk         # when heat is flagged
read_control domain=infrastructure action=rooms                # when room coverage matters
```

### Simple low-risk execution

1. Read the target state.
2. Dry-run a bounded semantic call or virtual-file patch.
3. Execute with confirmation.
4. Verify with the matching targeted read.

Use this for a short dig, mop, sweep, floor line, setting change, or reviewed blueprint.

### Complex spatial plan

```text
game_control domain=speed action=pause
read_control domain=world action=area_snapshot preset=construction|utilities encoding=rle
read_control domain=world action=text_map profile=standard encoding=plain includeElements=true
# optional visual confirmation only
navigation_control action=switch_view view=<overlay> screenshot=true
# dry-run, execute, then reread the same rectangle
```

Use explicit map headers for exact virtual-file edits. Use `areaId` for repeated semantic operations.

### Duplicant management

```text
colony_control domain=read action=dupes
dupes_control domain=info action=detail id=<id>
dupes_control domain=info action=needs id=<id>
dupes_control domain=priority action=list id=<id>
# batch only the required changes
dupes_control domain=priority action=list id=<id>   # verify
```

Resolve name to numeric ID from the roster before tools that require `id`.

### Batch configuration

```text
server_control domain=batch action=call_many
  dryRun=true responseMode=summary requireAllValid=true stopOnError=true
  defaults={confirm:true}
  items=[
    {t:dupes_control,a:{domain:priority,action:set,id:1,choreGroup:Dig,priority:4}},
    {t:colony_control,a:{domain:management,kind:schedule,action:set_block,schedule:"Default",hour:3,group:Sleep}}
  ]
```

If validation passes, call again with `dryRun=false`, then verify each affected domain.

## Parameter rules

### `worldId`

- Omit for the active world.
- Use a specific ID for an asteroid or rocket interior.
- Use `-1` only when the action documents all-world behavior.

### `limit`, detail, and encoding

- Keep limits narrow; paginate or filter instead of requesting the maximum.
- Use `detail=brief|compact` for decisions, `full` only for exact diagnosis.
- Use `profile=standard encoding=plain` for readable targeted maps.
- Use `profile=scan encoding=rle` for large read-only scans.
- Use `format=edit compact=false` for map patches.
- Use batch `responseMode=summary` normally, `errors` for retry loops, `full` for exact child payloads.

### Coordinates and areas

- Raw rectangles are valid for read-only map inspection and syntax explicitly supported by typed operation files.
- Ordinary aggregate action tools use semantic query, target, plan, or `areaId` rather than raw coordinates.
- Coordinates are world cells with bottom-left origin. Preserve returned absolute coordinates; do not confuse `rx/ry` with `x/y`.
- Define reusable areas with `read_control domain=area action=define`; merge adjacent handles when needed and forget temporary custom handles afterward.

### Confirmation

- Read tools need no confirmation.
- Medium/dangerous mutations require `confirm=true` on the execution call.
- Dry-run first. Never add confirmation merely to silence validation before authorization.

## Batch and efficiency patterns

### Read before write

Read the exact schedule, policy, building, duplicant, or map state; calculate the delta locally; send only changed fields; reread the same object.

### Parallel independent reads

```text
server_control domain=batch action=call_many responseMode=summary items=[
  {t:colony_control,a:{domain:read,action:status}},
  {t:colony_control,a:{domain:diagnostic,action:diagnostics}},
  {t:read_control,a:{domain:resources,action:food}},
  {t:read_control,a:{domain:infrastructure,action:power_summary}}
]
```

Do not batch calls when a later call needs an ID or handle returned by an earlier one.

### Differential update

Read the full relevant list once, compute changes locally, batch only those changes, and verify with one reread.

### Cache guidance

Cache catalog results for a loaded runtime. Short-cache roster, inventory, schedules, room summaries, and stable status only when the game state is not advancing rapidly. Never cache writes, cell-level state, volatile alerts, active camera state, or power state during grid changes. Invalidate related reads after every mutation or speed change.

## Tool categories

| Category | Read | Configure/write | Execute |
|---|---|---|---|
| Colony | snapshot, status, diagnostics, alerts, reports | diagnostic settings, management | notification actions |
| Dupes | roster; detail, attributes, needs, status, priorities, skills | priority, skill, hat, rename, assignable | move or force action when explicitly supported |
| Schedules | management schedule list | create, set block, assign, optimize | — |
| Resources | inventory, food, item search, storage detail, diet status | pins, storage filters, diet policy | — |
| Buildings | list/summary, defs, materials, candidates, config list | preview, auto-connect, enabled/toggle/copy, visual config | designation/build plan through supported planning flow |
| Orders | priority list | priority set | dig, mop, sweep, disinfect, cancel, harvest, deconstruct, capture, empty/cut conduits |
| Infrastructure | power summary/ports, rooms, utility maps | supported configuration | utility designation through orders/building tools |
| World | worlds, cells, elements, maps, thermal risk, areas | define/merge/forget areas | map mutations through orders/build plans |
| Camera | get view | — | move, focus, overlay, screenshot |
| Game | time, saves, DLC/state reads | supported state/DLC settings | pause, resume, speed, save/load/quit |
| Server | manifest, search, guide, coverage, diagnostics | — | batch aggregate calls |
| Virtual files | pwd, ls, read, zoom, search, symbols | edit, blueprint | confirmed edit/batch |

## Prompt workflows

| Prompt | Trigger | Suggested chain |
|---|---|---|
| `colony_triage` | Quick health check | colony status → diagnostics → alerts → food |
| `next_cycle_plan` | Short planning horizon | colony summary → inventory → research → schedules → dupes |
| `inspect_area` | Spatial analysis | world text map → targeted area snapshot/details |
| `dupe_care_review` | Duplicant audit | dupes → schedules → detail/needs/attributes |
| `power_audit` | Power check | power summary → exact ports/configuration as needed |
| `rooms_overview` | Room coverage | rooms list → filter by type/size |
| `thermal_audit` | Heat risk | overheat risk → element or temperature-map detail |

Prompts describe the chain; call the resources/tools yourself.

## Resource URIs

| URI | Tool equivalent | Purpose |
|---|---|---|
| `oni://colony/status` | `colony_control domain=read action=status` | Baseline status |
| `oni://colony/diagnostics` | `colony_control domain=diagnostic action=diagnostics` | Diagnosed issues |
| `oni://colony/alerts` | `colony_control domain=diagnostic action=alerts` | Current alerts |
| `oni://colony/summary` | `colony_control domain=report action=summary` | Planning summary |
| `oni://resources/inventory` | `read_control domain=resources action=inventory` | Stock levels |
| `oni://resources/food` | `read_control domain=resources action=food` | Food and expiry |
| `oni://power/summary` | `read_control domain=infrastructure action=power_summary` | Circuit health |
| `oni://rooms/list` | `read_control domain=infrastructure action=rooms` | Room coverage |
| `oni://thermal/overheat-risk` | `read_control domain=world action=thermal_overheat_risk` | Heat ranking |
| `oni://world/elements` | `read_control domain=world action=element_summary` | Element mass/temp |
| `oni://world/text-map` | `read_control domain=world action=text_map` | Terrain and overlays |
| `oni://dupes` | `colony_control domain=read action=dupes` | Roster |
| `oni://schedules` | `colony_control domain=management kind=schedule action=list` | Schedules |
| `oni://research/status` | `colony_control domain=management kind=research action=status` | Research state |
| `oni://tools/manifest` | `server_control domain=catalog action=manifest` | Public tool catalog |
| `oni://tools/search` | `server_control domain=catalog action=search` | Filtered discovery |

Templates may accept query parameters, for example `oni://power/summary?worldId=2&includeDetails=true`.

## Quick reference

| Situation | First call | Follow-up | Verification |
|---|---|---|---|
| What is happening? | minimal snapshot | targeted flagged domain | minimal/brief snapshot |
| Fix power | power summary | power ports or config/build plan | power summary |
| Build something | search defs/materials | candidates plus semantic plan or exact map patch | reread map/buildings |
| Manage dupes | roster | detail/needs then bounded update | roster/detail |
| Check heat | overheat risk | element or temperature map | same risk area |
| Plan actions | catalog guide | search plus dry-run tools | relevant live reads |
| Batch config | batch dry-run | batch execution | affected domains |
| Find a tool | catalog search | guide or manifest | static audit when safety matters |
| Area operations | define/read area | bounded semantic action | area read |
| Camera navigation | get view | move/focus only if needed | get view |
| Check research | research status | research list | research status |
| Rockets | rocket status | rocket detail | rocket status |
| Storage | storage list/detail | set filter | storage detail |
| Automation | list automation | set supported automation surface | list automation |

## Error lookup

| Error | Next step |
|---|---|
| Missing/invalid parameter | Catalog search and follow current schema |
| Target not found | Refresh roster/building/object list |
| No targets reachable | Inspect duplicant status and exact path region; do not force rescue without authority |
| Confirm required | Dry-run, obtain/verify authorization, then confirm |
| Timeout | Check returned task state or reduce scope |
| Research lock in sandbox/instant build | Treat running DLL as stale; do not queue research for the test |
