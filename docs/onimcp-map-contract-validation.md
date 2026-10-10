# OniMcp text maps and normal gameplay

## Implemented boundaries

Map reads use active-world, revealed cells only. Invalid, hidden, cross-world, and
missing data are not replaced with plausible values. Unknown connection data remains
`?`; a confirmed empty connection mask remains `*`. All 16 cardinal connection masks
preserve their geometry, including single-ended segments. Native physical connections,
not adjacent cells or shared circuit membership, provide the topology.

The supported view handlers are default, power, liquid pipes, gas pipes, automation,
conveyor rails, temperature, oxygen, light, decor, germs, radiation, tile materials,
crops, harvest, and rooms. Other views must not silently fall back to default.
Requested read bounds are independent of camera synchronization.

Edits require exact prefab identity or a unique exact display name. Material IDs are
not truncated to one character. Priorities outside 1–9 are errors. SEARCH treats `?`
and `*` literally; only `.*` is an explicit wildcard. Coordinate annotations must be
complete. Duplicate Y rows, mismatched row sets, unseen cells, stale contents, and
oversized patches are rejected. Existing whole-footprint and native placement checks
remain in force. A limit never truncates a building footprint.

The editor no longer supplies sandbox, spawn, terrain mutation, instant completion,
research bypass, free materials, or forced network repair. Normal orders and blueprint
placement remain. The normal execution scope suppresses debug instant-build for the
request and restores the pre-existing state. Building requests refuse sandbox saves.
Native research, material, support, visibility, and eligibility checks are retained.

## Small observation/action loop

Pause. Read a small relevant region in compact form. Use expanded edit format only for
the target patch. Preview one native operation and inspect rejection details. Submit it,
let the game run briefly, pause, and reread the same area. Never treat an accepted chore
or blueprint as a completed objective. Partial results require a fresh read before retry.

`reportedActions` records child action counts, not map-cell counts. `submittedCells`
records submitted work, not completed work. `phase=orders_queued`,
`gameCompleted=false`, and `requiresVerification=true` make the distinction explicit.
Physical utility connection readiness remains unknown until completed segments exist.

## Context size

The source schemas changed from 55 to 34 game-control parameters and from 73 to 51
world-editor parameters in this change. This is a parameter count, not a tokenizer or
runtime performance measurement. Compact maps omit verbose per-cell, connection, and
building-file tables; expanded reads still provide those details when needed.

## Validation levels

Executable host regressions compile the production token parser, cardinal read policy,
gameplay request policy, and public game-control router. Source checks additionally
verify native placement safety and absence of the removed mutation implementations.
Reference compilation checks the repository's pinned game API surface only.

No real ONI runtime was available during this change. Host tests and reference builds
do not establish correct rendering in every live overlay or successful in-game actions.
Record exact CI commit and run results in the pull request. The following runtime gate
remains required before treating this as fully accepted gameplay behavior.

## Required ONI runtime cases

For each supported view, compare the expanded text, the displayed game overlay, and
exact-cell native data on the same paused save. Record build, save, mods, bounds, and
view. Include hidden cells, world edges, missing data, and another loaded world's bounds.

For utilities, cover all cardinal masks, unknown managers, disconnected segments,
blueprints beside completed segments, crossing lines, bridges, and endpoints. Verify
that adjacency and shared network IDs never invent an edge.

For builds, cover adjacent identical structures, rotated multi-cell footprints, full
blueprint material recipes, same-prefab/different-material retries, missing research,
missing support, missing material, and occupied native endpoints. Preview must not
mutate; accepted work must be an ordinary blueprint or chore.

For edits, cover stale SEARCH content, repeated Y rows, mismatched coordinates, regex
limits, whole-footprint quota overflow, native rejection after a successful preview,
and partial child results. Re-read and verify actual completion after normal game time.

For control, exercise pause/read/preview/execute/resume/pause/re-read, including errors
and nested batches. Verify debug-mode restoration on all exit paths and rejection of
removed options both at the public route and inside operation payloads.
