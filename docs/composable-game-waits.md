# Composable game waits

`game_control domain=speed` keeps observation, predicates, and execution separate:

- `action=step_facts` reads native idle/work-schedule facts and pending dig/build counts without changing speed.
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
| `orders_complete` | At least one captured dig/build order existed, and none remain pending. |
| `cycle_end` | Cycle advanced since execution started. |
| `alert` | Current bad/threat alerts. Severe duplicant threats override every expression. |
| `alarm` | Current native automation alarm; optional case-insensitive `contains`. |
| `new_notification` | Notification absent from the execution baseline. |
| `notification` | Current notification; optional `contains` matches title/notifier, optional `type` is Good/Bad/BadMinor/Messages/Event. Both filters must match. |
| `printing_pod_ready` | Printing Pod changed from unavailable to available. |
| `research_complete` | Baseline research stopped or a current research-complete notification is present. |

Every leaf accepts `stableSeconds` (0–30 **game** seconds). Default is 2 for idle and 0 for other facts. A false observation resets that leaf's stability interval. Combination nodes evaluate every child on each observation. Expressions have at most 4 levels and 32 nodes; empty combinations, unknown facts/properties, invalid types, and simultaneous `until` / `condition` are rejected before releasing the pause or superseding a watcher.

## Population and tracking boundaries

Idle population is every live duplicant in `worldId` (default: the selected world). With `schedule=work`, each matching duplicant must have a native Work schedule block. `all` requires **every** member of the full population to satisfy both facts; off-work duplicants are not silently excluded. Zero duplicants never satisfy either quantifier. A null current chore is not classified as idle because it also occurs between chores. Native idle includes idle wandering. The two-second default filters brief idle transitions; the agent can change it.

Orders are captured once at execution start: existing native `Diggable` and `Constructable` objects in the selected world. `targetCells` optionally selects digs and intersecting building footprints across construction layers; without it, the whole selected world is captured. New orders and other chore kinds (delivery, research, sweeping, etc.) are excluded. Completion **or cancellation** removes an order from the pending set, so completion still needs a targeted game-state check when cancellation is possible.

`orderTracking` reports initial/remaining dig and build counts separately. `remainingTargetOrders` remains as a compatibility field and now counts those captured objects even when `targetCells` was omitted. Zero by itself does not prove any orders were captured; inspect `initialTrackedOrders`. `idleFacts` and compatibility `idleDuplicants` use the same native facts; the latter lists only idle duplicants on Work blocks.

Existing string `condition` inputs remain supported, including `dupe_idle` (any work-scheduled idle duplicant), `all_dupes_idle`, orders/cycle/notifications/research/printing/alarm aliases and keyword matching. `alarm:<name>` selects alarms; bare custom keywords and legacy `notify:` / `notification:` / `alert:` keywords search alarms first, then notification title/notifier, preserving an `alarm:` stop reason when an alarm matches. Legacy `any` combines orders, idle, and cycle; use explicit `until.any` for other combinations. Existing `wait=false`, `step_status`, and `step_cancel` remain available. No extra public tool is introduced.

Execution always retains game-time (1–600 seconds) and wall-time (0.5–30 seconds) caps; invalid/nonfinite limits are rejected. A severe threat pauses regardless of the requested expression. `step_facts` only observes; callers should keep the game paused while inspecting or planning.
