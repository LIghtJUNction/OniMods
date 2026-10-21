# Gameplay task feedback and readiness

This reference describes current OniMcp response fields for construction, task
diagnosis, asynchronous waits, and duplicant needs. Treat them as live observations,
not guarantees that an order will finish. Keep the game paused while inspecting or
planning, use explicit wall-time limits for waits, and verify the target state after
any write or stop result.

## Construction materials and work positions

Read building materials with:

```text
building_control domain=planning action=materials prefabId=<prefabId> worldId=<worldId>
```

`worldId` is optional and defaults to the active world. The response contains
`materials[]`; each candidate includes `tag`, `validForBuilding`, `reachableLooseKg`,
`localInventoryKg`, `storedKgNotCountedAsReachable`, and related-world context. For
immediate construction readiness, use the selected material's `reachableLooseKg`
(`availableKg` is the same reachable-loose quantity in this response). Stored stock
and related-world inventory are not counted as reachable supply. On build previews,
inspect the returned top-level `materialSelection`, plus
`constructionDiagnostics.material` and `constructionDiagnostics.workPosition`:

- `materialSelection` reports `requirementKnown`, `requiredKg`,
  `localAvailabilityStatus`, `satisfied`, `shortageKg`, `recipeSlots[]`,
  `recipeUnknownReason`, and selected/candidate material details. Each recipe slot
  reports `index`, `category`, `material`, `requiredKg`, `reachableAvailableKg`,
  `localInventoryKg`, `foreignWorldInventoryKgContextOnly`,
  `unverifiedLocalStockKg`, `hasActiveNavigator`, `shortageKg`, and
  `localAvailabilityStatus`.
- Diagnostics material status is `sufficient`, `shortage`, or `unknown`. Unknown
  requirements, missing living navigators, or local stock whose fetchability was not
  verified keep the status unknown. Do not interpret null shortage as zero shortage.
- `material=auto` is invalid when local reachable inventory cannot cover the recipe's
  summed material slots. An explicitly selected material can still be placed with a
  reported shortage; it does not prove construction can proceed.
- Work-position status is `reachable`, `unreachable`, or `unknown`. It checks the
  current living navigators against approach cells around the complete footprint.
  It does not prove that a duplicant will choose the job or that future conditions
  remain unchanged.
- `batchConstructionDiagnostics.resources[]` includes `material`,
  `newBlueprintOrPendingAnchors`, `aggregateRequiredKg`,
  `reachableAvailableKgCountedOnce`, `aggregateShortageKg`, `status`,
  `unverifiedLocalStockKg`, `foreignWorldInventoryKgContextOnly`, and
  `stockReservedForThisBatch=false`. It groups by material and counts reachable stock
  once; already present buildings/blueprints are excluded and deferred-after-dig
  anchors are included. A batch shortage estimate is not a reservation or proof that
  every construction chore can proceed.

Exact construction edits still use the reviewed `/active/map/viewport.md` virtual
file protocol. Direct `building_control ... action=build_area` coordinate calls are
rejected outside that protocol.

## Pending build after natural-cell digging

Build planning defaults `autoDigObstructions=true`. When a committed plan finds
diggable natural solids in the footprint, it queues native dig orders and may return
`pendingBuild=true` with an opaque `pendingBuildId`; this means the blueprint is not
placed yet. Once the recorded obstruction orders clear and a reachable work position
is observed, the system retries placement. Check the handle with:

```text
building_control domain=planning action=pending_build pendingBuildId=<id> pendingBuildAction=status
```

For cancellation, first preview it with `pendingBuildAction=cancel` (the default is
`dryRun=true`). Apply only with `dryRun=false confirm=true`, and only when
`canCancel=true` and status is still pending. It leaves existing native dig and uproot
orders in place. Inspect `status`, `blockedReason`, `lastResult`, `blueprintPlaced`,
and `buildingCompleted`; a placed blueprint is still not a completed building.
`remainingAction` is the recovery information for partial or failed work.

Pending-build records are memory-only (`persisted=false`, `expiresOnLoad=true`). They
are scoped to the current `Game`, root world, and requested target world; they are not
keyed to `activeWorld` or a save-path string. Unloading/replacing the game or target
world can end the handle. Check it before unloading, and do not claim it survives a
save/load transition.

## Diagnose a task target

When `orders_control` is available, request target-specific diagnostics with its
target `InstanceID`:

```text
orders_control domain=task action=diagnose id=<targetInstanceId> taskType=<optionalNativeChoreType>
```

The result reports `activeChores` from current chore drivers plus
`observedTaskGroups` with `eligibleObserved` and `blockedObserved` entries. Eligibility
and queue rank come from each consumer's most recent cached
`ChoreConsumer.GetLastPreconditionSnapshot`; the game supplies no timestamp. Therefore
the snapshot age is unknown, coverage is partial, and its ranks are only comparable
within that duplicant's observed list. A missing match is not proof that no candidate
exists or that every duplicant is ineligible. Treat this as diagnostic evidence, then
recheck the target and task state before changing priorities or issuing another order.
`limit` defaults to 20 and is capped at 100; check `eligibleTruncated`,
`blockedTruncated`, `activeTruncated`, and `groupsTruncated` before treating returned
lists as complete. Coverage and unobserved eligibility remain unknown/partial.

## Asynchronous `step_until` lifecycle

Pass a finite game-time cap and a wall-clock cap on each call. `maxWallSeconds` accepts
0.5–30 seconds (default 15); `maxGameSeconds` accepts 1–600 seconds (default 60).
`wait=false` starts a watcher and returns an opaque `requestId` with running status:

```text
game_control domain=speed action=step_until until={"fact":"orders_complete"} wait=false maxGameSeconds=60 maxWallSeconds=15
game_control domain=speed action=step_status requestId=<requestId>
game_control domain=speed action=step_cancel requestId=<requestId>
```

`step_status` with an ID returns that active or retained terminal result; without one,
it reports the active request or latest retained result. Completed results include
`completedAtUtc` and `resultAgeSeconds`; a cached result is historical, not a fresh
state read. Prefer a request ID to avoid confusing a previous run with current state.
`step_cancel` with the active ID targets only that request; if it has already completed,
the cached result is returned.
Without an ID it cancels the current request, or pauses if none is active. A new
`step_until` supersedes the current watcher. The terminal cache keeps at most the
eight newest results for the current `Game` and root-world identity; it is in-memory,
not a durable job store. The watcher also pins the requested target-world identity to
detect that world's unload. Active-world switches and save metadata/path strings do
not define this lifecycle. A result may stop on a match, critical threat, timeout,
external pause, observation failure, cancellation, or scene unload: read `stopReason`,
`isPaused`, alerts and order facts, then verify the actual objective. See
[composable game waits](composable-game-waits.md) for predicate semantics.

## Duplicant needs and bionic power

When the current tool catalog exposes `dupes_control`, use a targeted read such as
`dupes_control domain=info action=needs id=<instanceId>` or
`dupes_control domain=info action=status_check id=<instanceId> includeDetails=true detailMode=full`.
The response may include `amounts`, `availability`, and, for bionic duplicants,
`bionicPower`. Availability values distinguish `known`, `unknown`, and
`notApplicable`; `null` values or empty `amounts` are not zero and do not establish
safety. Do not call an internal tool name when it is absent from the current
`tools/list`; report unavailable fields as unknown and use the public world-editor
files for the state they actually expose.

`bionicPower` may report `state`, `chargeJ`, `capacityJ`, `online`, charged/depleted
powerbank counts, `sparePowerbankDamage`, nearby loose-bank counts/damage, and water
exposure fields. Unknown/null fields remain unknown. The loose-powerbank scan covers
a six-cell radius around that duplicant; a negative result is limited to that scan,
not a colony-wide absence claim.

These schemas are source-level behavior only. Host tests or reference builds do not
establish behavior in a running ONI session, save, mod combination, or FPS change.
