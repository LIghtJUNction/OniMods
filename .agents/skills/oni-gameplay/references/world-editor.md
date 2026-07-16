# World Editor Protocol

Read this reference before using `world_editor` for map edits, off-screen framing, operation files, management files, plans, blueprints, or batches.

## Contents

- [Mental model](#mental-model)
- [Virtual paths](#virtual-paths)
- [Universal patch protocol](#universal-patch-protocol)
- [Exact map rectangle protocol](#exact-map-rectangle-protocol)
- [Map token edits](#map-token-edits)
- [Typed operation files](#typed-operation-files)
- [Management, plan, and blueprint files](#management-plan-and-blueprint-files)
- [World-editor batch](#world-editor-batch)
- [Examples](#examples)
- [Troubleshooting](#troubleshooting)

## Mental model

`world_editor` exposes live game state as a virtual directory. A read renders current state; an edit submits a textual patch that preflight translates into game operations.

```text
world_editor command=pwd|cd|ls|read|zoom|grep|symbols|search|edit|blueprint|batch
  path=<virtual path> content=<patch>? dryRun=? confirm=? allowPartial=?
```

Convenience forwards also support game speed, camera/view/overlay, and screenshots. Prefer the dedicated aggregate tools unless the operation belongs naturally in the virtual-file workflow.

## Virtual paths

| Path | Purpose | Edit form |
|---|---|---|
| `/active/index.md` | Read-only colony index and optional first-call state | read only |
| `/active/map/viewport.md` | Terrain, objects, and orders in a framed rectangle | map tokens |
| `/active/map/index.md` | Alias of the editable viewport map | map tokens |
| `/active/map/layers/layer_<yMin>_<yMax>.md` | World-height slice rendered with the current active overlay | map tokens, except connection glyphs |
| `/active/map/symbols/glyphs.md` | Token legend | read only |
| `/active/ops/tools.md` | Current public typed operation files and tools | read only |
| `/active/ops/orders.md` | Dig, mop, sweep, disinfect, harvest, cancel, deconstruct, attack, capture | one command |
| `/active/ops/build.md` | Semantic coordinate-free build plans | one command |
| `/active/ops/{game,colony,read,search,dupes,navigation,server,...}.md` | Typed tool calls | one command |
| `/active/management/{schedule,priorities,dupes,food,skills,research}.md` | Panel snapshots plus edit commands | one command |
| `/active/dupes/<name>.md` | Per-duplicant detail | supported field edits such as `Name:` |
| `/active/buildings/plans.oni` | Building plan text | plan patch |
| `/active/infrastructure/*.oni` | Utility connection plans | plan patch |
| `/active/infrastructure/*.md` | Infrastructure map views | exact map preflight; connection glyph edits are refused |

Only `/active/` is mutable. Other save slots are historical or unloaded views.

## Universal patch protocol

The `content` parameter contains one or more marker blocks. Marker lines are indented here so repository diff checks do not mistake the documentation for an unresolved merge; the parser accepts marker substrings.

```text
  <<<<<<< SEARCH
<exact current text>
  =======
<replacement text>
  >>>>>>> REPLACE
```

Execution gate:

- `dryRun=true`, missing `confirm`, or `confirm=false`: preview only.
- `dryRun=false confirm=true`: execute translated operations.

Rules:

- Prefer one block. Multiple mutating blocks require outer `allowPartial=true`; game mutations cannot be transactionally rolled back.
- All blocks preflight before mutation. Any failure aborts with `phase=preflight` unless the explicitly allowed partial model applies.
- Patches are text-only. Do not send `editCells` or `editLines` coordinate payloads.
- Read again after execution. Never reuse a stale patch.

## Exact map rectangle protocol

Map rows use three explicit X-coordinate headers plus `Y=<n>:` rows:

```text
百位X: ...
十位X: ...
个位X: ...
Y=166: ...
Y=165: ...
```

### Preferred explicit mode

Include **all three** `百位X`, `十位X`, and `个位X` headers in SEARCH, followed by the relevant Y rows. Current preflight derives the exact rectangle and rereads that rectangle from source without moving, focusing, or depending on the current camera. It forces `format=edit compact=false` internally before matching.

This exact-source behavior applies to `/active/map/viewport.md`, its `/active/map/index.md` alias, `/active/map/layers/layer_<yMin>_<yMax>.md`, and infrastructure Markdown maps. It does not make the read-only colony index at `/active/index.md` editable.

If SEARCH attempts any explicit X header but the set is incomplete, malformed, inconsistent, or otherwise invalid, preflight fails closed. It does not silently fall back to viewport-relative coordinates.

Only a patch with **no X headers at all** may use legacy viewport-relative unique-row matching. Use that mode only for a tiny, unambiguous row already visible in the current viewport.

### Off-screen read-only framing

Read an off-screen rectangle without moving the camera:

```text
world_editor command=zoom path=/active/map/viewport.md
  x1=81 y1=42 x2=85 y2=42
  syncView=false focusCamera=false format=edit compact=false
```

`zoom` need not persist. Copy the returned three X headers and required Y rows directly into the later patch. Exact-header preflight rereads those world coordinates independently of the camera.

Do not use `navigation_control focus_cell` just to make a patch addressable.

### Expanded rows only

For authoring patches, request `format=edit compact=false`. Read-only maps may use RLE (`粉x3`), but compressed rows are error-prone for token-count edits. REPLACE must preserve the exact cell count of every row.

## Map token edits

Workflow:

1. Read or off-screen zoom with `format=edit compact=false`.
2. Copy all three X headers and the smallest complete set of Y rows into SEARCH.
3. Repeat them in REPLACE, changing only intended cells.
4. Preview with `dryRun=true`.
5. Inspect translated operations, materials, support, conflicts, and changed-cell count.
6. Reread if state changed; otherwise execute a fresh call with `dryRun=false confirm=true`.
7. Reread the exact rectangle.

Token forms:

- Build order: `建筑名:优先级`, optionally `#材料字`, such as `梯子:7#粉`.
- Bare building names represent existing objects, not construction orders.
- Orders: `挖`, `拆`, `擦`, `扫`, `毒`, `杀`, `收`, `消`, `捕`, optionally `:优先级`.
- Connection-glyph edits on map layers and infrastructure Markdown are refused because `auto_connect` may modify cells outside the validated snapshot. Make utility changes with an explicit `/active/infrastructure/*.oni` plan or an `/active/ops/build.md` `auto_connect` command.
- SEARCH wildcards `?` or `*` match one token; `/regex/` or `~regex` match one token by regex. REPLACE `?`, `*`, or `.*` keeps the original token.

Multi-cell buildings must include the complete footprint in one replacement block. Include support cells and enough neighboring context to keep the match unique.

Default write budget is 512 changed cells; configurable `maxWriteCells`/`maxCells` has a hard cap of 2500. On `partial=true`, reread and create a fresh patch for `remainingCells`.

## Typed operation files

Read `/active/ops/tools.md`, then read the selected operation file. Each file documents its default tool, schema, and commented examples.

Rules:

- Submit exactly one executable command per edit.
- Use `call tool=<name> key=value ...`; typed files may omit `tool=`.
- An empty SEARCH block adds a fresh command.
- The outer `task` is inherited and must describe the operation.
- Do not call `world_editor` recursively from an operation file.
- Do not use hidden `coordinate_control` or `/active/ops/coordinate.md`.
- Raw coordinates are allowed only in syntax explicitly documented by that typed file.

Common `/active/ops/orders.md` shortcuts:

```text
挖 土@(83,146):7
擦 x1=90 y1=140 x2=94 y2=142 priority=6
扫 areaId=base_floor priority=6
拆 建筑@(90,141):7
捕 小动物@(101,130):7 dryRun=true
```

Use `擦`/mop for liquids and `扫`/sweep for debris.

## Management, plan, and blueprint files

### Management Markdown

Tables are read-only snapshots. Change state only through one uncommented line under `## Edit Commands`.

| File | Typical commands |
|---|---|
| `schedule.md` | `set_block`, `assign_dupe`, `create_schedule` |
| `priorities.md` | `priority`, `priority_settings` |
| `dupes.md` | `rename` |
| `food.md` | `food`, `food_policy` |
| `skills.md` | `learn_skill` |
| `research.md` | `research`, `clear_research` |

Append `?format=json` only when machine-readable state is necessary.

### Plan files

- `/active/buildings/plans.oni`: preview routes to `parse_plan`; confirmed execution routes to `building_control planning build_area`.
- `/active/infrastructure/*.oni`: explicit utility plans route to `auto_connect`.
- `/active/ops/build.md`: submit one explicit `auto_connect` command when the utility change is better expressed as a typed operation.

### Blueprints

`world_editor command=blueprint name=<name>` supports list, read, create, delete, and use. Blueprint Markdown uses the same patch protocol.

## World-editor batch

`world_editor command=batch` accepts up to 20 `steps`/`items` containing world-editor, game-control, or navigation-control argument objects.

- At most one potentially mutating step is allowed, and it must be last.
- Earlier steps must be read-only.
- `stopOnError` defaults to true.
- Nested world-editor batches are rejected.
- Outer dry-run and confirmation policy is inherited.

Use batch when the final mutation depends only on static arguments already known before the batch. Do not pretend a later step can consume an earlier step's dynamic result.

## Examples

### Exact off-screen ladder patch

First run the read-only zoom above. Copy its real headers and rows. Marker indentation is intentional and accepted by the parser.

```text
world_editor command=edit path=/active/map/viewport.md task="搭梯子" dryRun=true
content="""
  <<<<<<< SEARCH
百位X: 0 0 0 0 0
十位X: 8 8 8 8 8
个位X: 1 2 3 4 5
Y=42: 粉 粉 空 空 水
  =======
百位X: 0 0 0 0 0
十位X: 8 8 8 8 8
个位X: 1 2 3 4 5
Y=42: 粉 粉 梯子:7 空 水
  >>>>>>> REPLACE
"""
```

Review, then submit the same freshly generated patch with `dryRun=false confirm=true`.

### Fresh dig command

```text
world_editor command=edit path=/active/ops/orders.md task="挖掘取材料" dryRun=true
content="""
  <<<<<<< SEARCH
  =======
挖 土@(83,146):7
  >>>>>>> REPLACE
"""
```

### Rename a duplicant

```text
world_editor command=edit path=/active/management/dupes.md task="重命名复制人" dryRun=false confirm=true
content="""
  <<<<<<< SEARCH
# rename name="Dig" newName="矿工"
  =======
rename name="Dig" newName="矿工"
  >>>>>>> REPLACE
"""
```

## Troubleshooting

| Symptom | Response |
|---|---|
| `SEARCH did not match` | Reread exact source and regenerate; do not resend stale text |
| Explicit-header error | Supply all three valid X headers from one read, or remove all headers for a truly viewport-relative unique match |
| Ambiguous row | Add X headers and more Y-row context |
| Wrong coordinate offset | Stop computing offsets; use explicit headers and exact-source preflight |
| Compressed token mismatch | Reread with `format=edit compact=false` |
| Multi-cell preview failure | Include the entire footprint and support context in one block |
| `partial=true` | Reread and patch only remaining cells |
| Camera moved unexpectedly | Use off-screen zoom with `syncView=false focusCamera=false`; never focus solely for editing |
| Sandbox reports research lock | Loaded DLL is stale; do not queue research for the test |
