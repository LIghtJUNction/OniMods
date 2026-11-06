using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static void ProcessDeferredBuilds(Game game)
        {
            if (game == null || !ReferenceEquals(Game.Instance, game))
            {
                ClearDeferredBuilds("cancelled_game_changed");
                return;
            }

            var active = DeferredBuilds.Where(item => item.Status == "pending" || item.Status == "blueprint_placed")
                .ToList();
            if (active.Count > 0)
            {
                int queueCount = active.Count;
                int start = deferredBuildCursor % queueCount;
                int count = Math.Min(8, queueCount);
                active = Enumerable.Range(0, count).Select(offset => active[(start + offset) % queueCount]).ToList();
                deferredBuildCursor = (start + count) % queueCount;
            }
            foreach (var item in active)
                ProcessDeferredBuild(item, game);

            PurgeDeferredBuildHistory();
        }

        private static void ProcessDeferredBuild(DeferredBuild item, Game game)
        {
            try
            {
                if (!ReferenceEquals(item.Game, game)
                    || !ReferenceEquals(item.RootWorld, DeferredRootWorld())
                    || !ReferenceEquals(item.WatchedWorld, DeferredWorld(item.WorldId)))
                {
                    FinishDeferredBuild(item, "cancelled_scene_changed", null);
                    return;
                }
                if (item.Status == "blueprint_placed")
                {
                    ObserveDeferredConstruction(item);
                    return;
                }
                if (Time.realtimeSinceStartup - item.CreatedAt > 1800f)
                {
                    FinishDeferredBuild(item, "timed_out", null);
                    return;
                }
                if (!DeferredTargetsReady(item, out string blockedReason))
                {
                    item.BlockedReason = blockedReason;
                    return;
                }
                if (!DeferredBuildHasReachableWorkCell(item, out blockedReason))
                {
                    item.BlockedReason = blockedReason;
                    return;
                }

                var retryArgs = (JObject)item.Args.DeepClone();
                var result = TryPlanOne(item.PrefabId, item.X, item.Y, retryArgs,
                    deferredBuildId: item.Id);
                AddConstructionDiagnostics(result, item.PrefabId, item.X, item.Y, item.WorldId);
                item.Attempts++;
                item.LastResult = result;
                item.UpdatedAt = Time.realtimeSinceStartup;
                string status = BuildPlanningDeferredBuildPolicy.CompletionStatus(
                    GetBool(result, "planned"), GetBool(result, "alreadyPresent"), GetBool(result, "pendingBuild"));
                if (status == "blueprint_placed")
                    FinishDeferredBuild(item, "blueprint_placed", result);
                else if (status == "already_present")
                    FinishDeferredBuild(item, GetBool(result, "alreadyBuilding") ? "built"
                        : GetBool(result, "alreadyBlueprint") ? "blueprint_placed" : "already_present", result);
                else if (status == "pending")
                    item.BlockedReason = "footprint_still_blocked";
                else
                    FinishDeferredBuild(item, "failed", result);
            }
            catch (Exception exception)
            {
                FinishDeferredBuild(item, "failed", new Dictionary<string, object>
                {
                    ["error"] = exception.GetType().Name + ": " + exception.Message,
                    ["remainingAction"] = BuildDeferredRemainingAction(item.Args, item.PrefabId, item.X, item.Y)
                });
            }
        }

        private static void ObserveDeferredConstruction(DeferredBuild item)
        {
            string resolvedPrefabId;
            string resolveError;
            var def = ResolveBuildingDef(item.PrefabId, out resolvedPrefabId, out resolveError);
            if (def == null)
            {
                FinishDeferredBuild(item, "failed", new Dictionary<string, object>
                {
                    ["error"] = resolveError ?? "Building definition became unavailable after blueprint placement.",
                    ["blueprintPlaced"] = true,
                    ["buildingCompleted"] = false
                });
                return;
            }
            var orientation = ParseOrientation(item.Args?["orientation"]?.ToString());
            var placement = BuildPlacementDetails(def, item.X, item.Y, item.WorldId, orientation);
            var existing = FindDeferredBuildOnFootprint(def, placement);
            string kind = existing != null && existing.TryGetValue("kind", out object rawKind) ? rawKind?.ToString() : null;
            if (string.Equals(kind, "building", StringComparison.OrdinalIgnoreCase))
            {
                FinishDeferredBuild(item, "built", new Dictionary<string, object>
                {
                    ["blueprintPlaced"] = true,
                    ["buildingCompleted"] = true,
                    ["existing"] = existing
                });
            }
            else if (!string.Equals(kind, "blueprint", StringComparison.OrdinalIgnoreCase))
            {
                FinishDeferredBuild(item, "blueprint_missing", new Dictionary<string, object>
                {
                    ["blueprintPlaced"] = false,
                    ["buildingCompleted"] = false,
                    ["note"] = "The queued blueprint is no longer present. It may have been cancelled or removed outside this tool."
                });
            }
        }

        private static Dictionary<string, object> FindDeferredBuildOnFootprint(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null)
                return null;
            var layers = new HashSet<int> { (int)def.ObjectLayer, (int)ObjectLayer.Building };
            foreach (var cell in placement.Footprint)
            {
                if (!cell.Valid || !Grid.IsValidCell(cell.Cell))
                    continue;
                foreach (int layer in layers)
                {
                    var go = Grid.Objects[cell.Cell, layer];
                    if (go == null)
                        continue;
                    var building = go.GetComponent<Building>();
                    string prefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                    if (!EqualsIgnoreCase(prefabId, def.PrefabID))
                        continue;
                    var actual = ActualPlacementDetails(go, def, placement.AnchorX, placement.AnchorY);
                    if (!GetBool(ComparePlacement(placement, actual), "valid"))
                        continue;
                    string kind = go.GetComponent<BuildingComplete>() != null ? "building"
                        : go.GetComponent<Constructable>() != null ? "blueprint" : null;
                    if (kind == null)
                        continue;
                    return new Dictionary<string, object>
                    {
                        ["kind"] = kind,
                        ["prefabId"] = prefabId,
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                        ["actualPlacement"] = actual
                    };
                }
            }
            return null;
        }

        private static bool DeferredTargetsReady(DeferredBuild item, out string reason)
        {
            reason = null;
            foreach (int cell in item.NaturalCells)
            {
                if (!Grid.IsValidCell(cell) || !Grid.Solid[cell])
                    continue;
                if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] == null)
                {
                    reason = "dig_order_missing_or_cancelled";
                    FinishDeferredBuild(item, "cancelled_obstruction_order_removed", null);
                    return false;
                }
                reason = "waiting_for_native_dig";
                return false;
            }
            foreach (int cell in item.UprootCells)
            {
                var targets = UprootablesAtCell(cell, item.WorldId).ToList();
                if (targets.Count == 0)
                    continue;
                if (targets.Any(target => target.IsMarkedForUproot))
                {
                    reason = "waiting_for_native_uproot";
                    return false;
                }
                reason = "uproot_order_missing_or_cancelled";
                FinishDeferredBuild(item, "cancelled_obstruction_order_removed", null);
                return false;
            }
            return true;
        }

        private static bool DeferredBuildHasReachableWorkCell(DeferredBuild item, out string reason)
        {
            string resolvedPrefabId;
            string resolveError;
            var def = ResolveBuildingDef(item.PrefabId, out resolvedPrefabId, out resolveError);
            if (def == null)
            {
                reason = "building_definition_unavailable";
                return false;
            }
            var work = WorkPositionDiagnostics(def, item.X, item.Y, item.WorldId);
            string status = work != null && work.TryGetValue("status", out object value) ? value?.ToString() : null;
            if (string.Equals(status, "reachable", StringComparison.OrdinalIgnoreCase))
            {
                reason = null;
                return true;
            }
            reason = status == "unreachable" ? "no_reachable_build_work_cell" : "build_work_position_unknown";
            return false;
        }

        private static void FinishDeferredBuild(DeferredBuild item, string status, Dictionary<string, object> result)
        {
            item.Status = status;
            item.UpdatedAt = Time.realtimeSinceStartup;
            item.LastResult = result;
            if (status == "built")
                item.RemainingAction = null;
            else if (status != "blueprint_placed" && status != "cancelled")
                item.RemainingAction = BuildDeferredRemainingAction(item.Args, item.PrefabId, item.X, item.Y);
        }

        private static void ClearDeferredBuilds(string status)
        {
            foreach (var item in DeferredBuilds.Where(item => item.Status == "pending" || item.Status == "blueprint_placed"))
                FinishDeferredBuild(item, status, null);
            DeferredBuilds.Clear();
            deferredBuildDriver = null;
        }

        private static void PurgeDeferredBuildHistory()
        {
            DeferredBuilds.RemoveAll(item => item.Status != "pending" && item.Status != "blueprint_placed"
                && Time.realtimeSinceStartup - item.UpdatedAt > 3600f);
            foreach (var old in DeferredBuilds.Where(item => item.Status != "pending" && item.Status != "blueprint_placed")
                .OrderByDescending(item => item.UpdatedAt).Skip(MaxDeferredBuilds).ToList())
                DeferredBuilds.Remove(old);
        }

        private static object DeferredRootWorld()
        {
            var cluster = ClusterManager.Instance;
            return cluster == null ? null : cluster.GetWorld(0);
        }

        private static object DeferredWorld(int worldId)
        {
            var cluster = ClusterManager.Instance;
            return cluster == null ? null : cluster.GetWorld(worldId);
        }

        private sealed class DeferredBuild
        {
            public string Id;
            public Game Game;
            public object RootWorld;
            public object WatchedWorld;
            public int WorldId;
            public string PrefabId;
            public int X;
            public int Y;
            public Dictionary<string, object> MaterialSelection;
            public JObject Args;
            public HashSet<int> NaturalCells;
            public HashSet<int> UprootCells;
            public float CreatedAt;
            public float UpdatedAt;
            public int Attempts;
            public string Status;
            public string BlockedReason;
            public Dictionary<string, object> LastResult;
            public Dictionary<string, object> RemainingAction;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["pendingBuildId"] = Id,
                    ["status"] = Status,
                    ["canCancel"] = Status == "pending",
                    ["blueprintPlaced"] = Status == "blueprint_placed" || Status == "built",
                    ["buildingCompleted"] = Status == "built",
                    ["persisted"] = false,
                    ["expiresOnLoad"] = true,
                    ["prefabId"] = PrefabId,
                    ["anchor"] = new Dictionary<string, object> { ["x"] = X, ["y"] = Y },
                    ["worldId"] = WorldId,
                    ["materialSelection"] = MaterialSelection,
                    ["createdAtRealtimeSeconds"] = CreatedAt,
                    ["updatedAtRealtimeSeconds"] = UpdatedAt,
                    ["attempts"] = Attempts,
                    ["blockedReason"] = BlockedReason,
                    ["lastResult"] = LastResult,
                    ["remainingAction"] = RemainingAction
                };
            }
        }

        private sealed class DeferredBuildDriver : MonoBehaviour
        {
            private Game target;
            private float nextPoll;

            public void Initialize(Game game)
            {
                target = game;
                nextPoll = Time.realtimeSinceStartup + 1f;
            }

            private void Update()
            {
                if (!DeferredBuilds.Any(item => item.Status == "pending" || item.Status == "blueprint_placed"))
                {
                    if (ReferenceEquals(deferredBuildDriver, this))
                        deferredBuildDriver = null;
                    Destroy(this);
                    return;
                }
                if (!ReferenceEquals(Game.Instance, target))
                {
                    ClearDeferredBuilds("cancelled_game_changed");
                    Destroy(this);
                    return;
                }
                if (Time.realtimeSinceStartup < nextPoll)
                    return;
                nextPoll = Time.realtimeSinceStartup + 1f;
                ProcessDeferredBuilds(target);
            }

            private void OnDestroy()
            {
                if (ReferenceEquals(deferredBuildDriver, this))
                {
                    deferredBuildDriver = null;
                    if (DeferredBuilds.Any(item => item.Status == "pending" || item.Status == "blueprint_placed"))
                        ClearDeferredBuilds("cancelled_driver_destroyed");
                }
            }
        }
    }
}
