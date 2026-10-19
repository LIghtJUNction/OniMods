using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        private static List<StepDupeFact> ReadStepDupeFacts(int worldId)
        {
            var result = new List<StepDupeFact>();
            if (Components.LiveMinionIdentities == null) return result;
            string workGroupId = Db.Get().ScheduleGroups.Worktime.Id;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || !ToolUtil.GameObjectMatchesWorld(dupe.gameObject, worldId)) continue;
                var chore = dupe.GetComponent<ChoreDriver>()?.GetCurrentChore();
                // A null chore also occurs during handoff. Only native idle work is an idle fact.
                bool idle = chore is IdleChore || (chore != null && chore.choreType == Db.Get().ChoreTypes.Idle);
                result.Add(new StepDupeFact
                {
                    Name = dupe.GetProperName(),
                    Idle = idle,
                    ScheduleGroupId = dupe.GetComponent<Schedulable>()?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId,
                    WorkScheduleGroupId = workGroupId
                });
            }
            return result;
        }

        private sealed class StepOrderTracker
        {
            private readonly List<Diggable> _digs = new List<Diggable>();
            private readonly List<Constructable> _builds = new List<Constructable>();
            internal int InitialDigOrders { get; private set; }
            internal int InitialBuildOrders { get; private set; }
            internal int InitialCount => InitialDigOrders + InitialBuildOrders;
            internal int RemainingDigOrders => _digs.Count(d => d != null);
            internal int RemainingBuildOrders => _builds.Count(b => b != null);
            internal int RemainingCount => RemainingDigOrders + RemainingBuildOrders;

            internal StepOrderTracker(int worldId, HashSet<int> targetCells)
            {
                bool targeted = targetCells != null && targetCells.Count > 0;
                if (Components.Diggables != null)
                {
                    foreach (var dig in Components.Diggables.Items)
                    {
                        if (dig == null || !ToolUtil.GameObjectMatchesWorld(dig.gameObject, worldId)) continue;
                        if (!targeted || targetCells.Contains(Grid.PosToCell(dig))) _digs.Add(dig);
                    }
                }
                // Capture once, not a scene scan on each frame. This includes every construction layer.
                foreach (var build in UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None))
                {
                    if (build == null || !ToolUtil.GameObjectMatchesWorld(build.gameObject, worldId)) continue;
                    bool matches = !targeted || targetCells.Contains(Grid.PosToCell(build));
                    var building = build.GetComponent<Building>();
                    if (!matches && building?.Def != null)
                    {
                        building.Def.RunOnArea(Grid.PosToCell(build), building.Orientation, cell =>
                        {
                            if (targetCells.Contains(cell)) matches = true;
                        });
                    }
                    if (matches) _builds.Add(build);
                }
                InitialDigOrders = _digs.Count;
                InitialBuildOrders = _builds.Count;
            }

            internal Dictionary<string, object> ToJson() => new Dictionary<string, object>
            {
                ["kinds"] = new[] { "dig", "build" },
                ["initialDigOrders"] = InitialDigOrders,
                ["initialBuildOrders"] = InitialBuildOrders,
                ["initialTrackedOrders"] = InitialCount,
                ["remainingDigOrders"] = RemainingDigOrders,
                ["remainingBuildOrders"] = RemainingBuildOrders,
                ["remainingTrackedOrders"] = RemainingCount,
                ["semantics"] = "Captured orders still present; completed and cancelled orders both leave the pending set. New orders and other chore kinds are excluded."
            };
        }

        private static Dictionary<string, object> StepDupeFactsJson(List<StepDupeFact> dupes) => new Dictionary<string, object>
        {
            ["population"] = "live duplicants in selected world",
            ["count"] = dupes.Count,
            ["nativeIdleCount"] = dupes.Count(d => d.Idle),
            ["workIdleCount"] = dupes.Count(d => d.Idle && d.WorkScheduled),
            ["duplicants"] = dupes.Select(d => new Dictionary<string, object>
            {
                ["name"] = d.Name, ["idle"] = d.Idle, ["workScheduled"] = d.WorkScheduled,
                ["scheduleGroupId"] = d.ScheduleGroupId, ["workScheduleGroupId"] = d.WorkScheduleGroupId
            }).ToList()
        };
    }
}
