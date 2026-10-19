# Composable game waits

`game_control domain=speed` keeps observation, predicates, and execution separate:

- `action=step_facts` reads native idle/work-schedule facts, pending dig/build counts, and global `immigrantsAvailable` without changing speed.
- `until` describes one fact or combines conditions with `all` / `any`.
- `action=step_until` captures a baseline, runs within time limits, and pauses on a match, threat, external pause, observation failure, or timeout. The agent chooses the policy for each task.

For a colony where all three duplicants have finished available work:

```json
{
  "domain": "speed",
  "action": "step_until",
  "until": {"fact": "dupes_idle", "quantifier": "all", "schedule": "work", "stableSeconds": 2},
  "speed": 3,
  "maxGameSeconds": 60,
  "maxWallSeconds": 15
}
```

This is an example policy, not a server default. One idle duplicant may indicate blocked work, while everybody idle does not prove a building was successfully completed. Inspect the returned facts and the actual building state.

## Interpret the stop result

`alert` matches any alert present in the current observation, including an already-existing Bad notification; it is not limited to new or severe threats. Separately, `StepUntilWatcher` checks duplicant threats and forces a stop even when the requested expression does not contain `alert`. Read `stopReason`, `alerts`, and `notifications` to tell these cases apart. A very short return (including about 0.1 seconds) does not by itself show a `pauseCount` problem or prove that the requested work advanced.

`orders_complete` means the native dig/build orders and deferred-build workflows captured at start are no longer pending. Cancellation or a terminal workflow failure also removes pending work; inspect `orderTracking.deferredBuilds.states` and the target state before reporting success. A wait can make useful progress and then stop at its game-time or wall-time limit. The game is paused when `step_until` returns.

To require the captured orders to disappear **and** everyone to be idle, or to stop for an alarm:

```json
{
  "domain": "speed",
  "action": "step_until",
  "until": {
    "any": [
      {"all": [
        {"fact": "orders_complete"},
        {"fact": "dupes_idle", "quantifier": "all"}
      ]},
      {"fact": "alarm", "contains": "WaterFull"}
    ]
  },
  "maxGameSeconds": 90,
  "maxWallSeconds": 20
}
```

## Atomic predicates

| `fact` | Meaning / options |
| --- | --- |
| `dupes_idle` | Native `IdleChore` / Idle chore type; `quantifier=any` (default) or `all`; `schedule=work` (default) or `any`. |
| `orders_complete` | At least one native dig/build order or deferred-build workflow was captured, and none remain pending. |
| `cycle_end` | Cycle advanced since execution started. |
| `alert` | Current bad/threat alerts. Severe duplicant threats override every expression. |
| `alarm` | Current native automation alarm; optional case-insensitive `contains`. |
| `new_notification` | Notification absent from the execution baseline. |
| `notification` | Current notification; optional `contains` matches title/notifier, optional `type` is Good/Bad/BadMinor/Messages/Event. Both filters must match. |
| `printing_pod_ready` | Current native Immigration availability, including an offer already ready at step start. Independent of materialized reward cards. |
| `research_complete` | The native technology active at execution start changes from incomplete to complete, even if ONI immediately starts the next queued technology. |

Every leaf accepts `stableSeconds` (0–30 **game** seconds). Default is 2 for idle and 0 for other facts. A false observation resets that leaf's stability interval. Combination nodes evaluate every child on each observation. Expressions have at most 4 levels and 32 nodes; empty combinations, unknown facts/properties, invalid types, and simultaneous `until` / `condition` are rejected before releasing the pause or superseding a watcher.

A ready Printing Pod is checked before releasing the pause when the expression includes `printing_pod_ready`. The complete expression must match: readiness does not bypass `all` siblings or explicit stability windows. A default zero-second ready leaf in `any` can stop immediately, including `wait=false`; an explicit positive `stableSeconds` still requires game time. Expressions without this fact keep their existing start behavior. Legacy `print` / `printing_pod` / `printable` / `immigrant` aliases share this state semantics; legacy `any` still excludes printing.

`immigrantsAvailable` is returned by `step_facts`, `step_status`, and completion results. Treat it as the readiness fact. `list_rewards.rewardCount=0` only means no care-package choices are materialized yet; it does not mean the pod is unavailable. While paused, list the pod, explicitly `open_immigrants` with `confirm=true` if available but unmaterialized, then list and select only a care package. Readiness observation never opens UI or selects any reward.

## Population and tracking boundaries

Idle population is every live duplicant in `worldId` (default: the selected world). With `schedule=work`, each matching duplicant's current `ScheduleBlock.GroupId` must equal the native `Db.Get().ScheduleGroups.Worktime.Id`. This is a schedule-group identity, distinct from the `Work` block type or localized labels. Raw `scheduleGroupId` / `workScheduleGroupId` values are included in idle facts for verification. `all` requires **every** member of the full population to satisfy both facts; off-work duplicants are not silently excluded. Zero duplicants never satisfy either quantifier. A null current chore is not classified as idle because it also occurs between chores. Native idle includes idle wandering. The two-second default filters brief idle transitions; the agent can change it.

Orders are captured once at execution start: native `Diggable` and `Constructable` objects plus active deferred-build handles in the selected world. `targetCells` selects digs and intersecting building footprints, including deferred placements. The captured deferred handle remains pending through digging and the subsequently created blueprint until it reaches a terminal state. New requests and other chore kinds (delivery, research, sweeping, etc.) are excluded. Counts describe tracked native objects and workflows; phases can overlap, so they are not a unique count of duplicant chores. Completion, cancellation or failure removes pending work; zero still requires a targeted state check.

`researchTracking` reports `initialTechId`, `initiallyComplete`, and `completed` for the captured native technology. Clearing/changing the active research slot, old notifications, localized HUD titles, and reused notification IDs do not prove completion. If there was no active technology at execution start, this fact cannot match during that step.

`orderTracking` reports initial/remaining dig and build counts separately. `remainingTargetOrders` remains as a compatibility field and now counts those captured objects even when `targetCells` was omitted. Zero by itself does not prove any orders were captured; inspect `initialTrackedOrders`. `idleFacts` and compatibility `idleDuplicants` use the same native facts; the latter lists only idle duplicants on Work blocks.

Existing string `condition` inputs remain supported, including `dupe_idle` (any work-scheduled idle duplicant), `all_dupes_idle`, orders/cycle/notifications/research/printing/alarm aliases and keyword matching. `alarm:<name>` selects alarms; bare custom keywords and legacy `notify:` / `notification:` / `alert:` keywords search alarms first, then notification title/notifier, preserving an `alarm:` stop reason when an alarm matches. Legacy `any` combines orders, idle, and cycle; use explicit `until.any` for other combinations. Existing `wait=false`, `step_status`, and `step_cancel` remain available. No extra public tool is introduced.

Execution always retains game-time (1–600 seconds) and wall-time (0.5–30 seconds) caps; invalid/nonfinite limits are rejected. A severe threat pauses regardless of the requested expression. `step_facts` only observes; callers should keep the game paused while inspecting or planning.
