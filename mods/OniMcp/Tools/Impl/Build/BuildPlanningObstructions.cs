using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static List<Dictionary<string, object>> FindFootprintObstructions(
            PlacementDetails placement, GameObject ignored = null)
        {
            var result = new List<Dictionary<string, object>>();
            var placementDef = ResolveBuildingDefForPlacement(placement);
            var safetyFootprint = PlacementSafetyFootprint(placementDef, placement).ToList();
            var footprintCells = new HashSet<int>(safetyFootprint.Where(cell => cell.Valid).Select(cell => cell.Cell));
            bool utility = IsUtilityPrefab(placement.PrefabId);
            bool endpointBridge = UsesNativeBridgeEndpointRegistration(placementDef);

            foreach (var cellInfo in safetyFootprint)
            {
                if (!cellInfo.Valid)
                    continue;
                if (!utility && Grid.Solid[cellInfo.Cell])
                {
                    bool diggable = IsNaturalDiggableSolidCell(cellInfo.Cell, placement.WorldId);
                    bool alreadyMarked = Grid.Objects[cellInfo.Cell, (int)ObjectLayer.DigPlacer] != null;
                    result.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "solid_cell",
                        ["x"] = cellInfo.X,
                        ["y"] = cellInfo.Y,
                        ["cell"] = cellInfo.Cell,
                        ["diggable"] = diggable,
                        ["alreadyMarkedForDig"] = alreadyMarked,
                        ["reasonCode"] = "solid_cell",
                        ["reason"] = diggable
                            ? "target footprint contains natural solid terrain that can be marked for digging"
                            : "target footprint contains solid terrain or constructed tile that cannot be auto-dug"
                    });
                }

                foreach (var uproot in UprootableObstructionsAtCell(cellInfo.Cell, placement.WorldId))
                {
                    uproot["x"] = cellInfo.X;
                    uproot["y"] = cellInfo.Y;
                    uproot["cell"] = cellInfo.Cell;
                    result.Add(uproot);
                }
            }

            foreach (var conflict in FindUtilityLayerConflicts(placementDef, placement, ignored))
                result.Add(conflict);
            foreach (var conflict in FindBuildingLayerConflicts(placementDef, placement))
                result.Add(conflict);
            foreach (var conflict in FindLogicEndpointConflicts(placementDef, placement))
                result.Add(conflict);

            var seen = new HashSet<string>();
            foreach (var obstruction in ExistingBuildingFootprintObstructions(placement.WorldId, footprintCells))
            {
                string id = obstruction.ContainsKey("id") ? obstruction["id"]?.ToString() : "";
                if ((utility && !endpointBridge) || IsUtilityPrefab(id))
                    continue;
                string key = obstruction["kind"] + "|" + id + "|" + obstruction["objectX"] + "|" + obstruction["objectY"] + "|" + obstruction["x"] + "|" + obstruction["y"];
                if (seen.Add(key))
                    result.Add(obstruction);
            }

            return result;
        }

        private static BuildingDef ResolveBuildingDefForPlacement(PlacementDetails placement)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.PrefabId))
                return null;
            string resolvedPrefabId;
            string error;
            return ResolveBuildingDef(placement.PrefabId, out resolvedPrefabId, out error);
        }

        private static Dictionary<string, object> TryAutoDigObstructions(PlacementDetails placement, FootprintValidation footprintResult, JObject args, AutoDigContext context)
        {
            bool enabled = ToolUtil.GetBool(args, "autoDigObstructions", true);
            if (!enabled || placement == null || footprintResult == null)
                return null;
            if (footprintResult.InvalidCells.Count > 0)
                return null;

            foreach (var obstruction in footprintResult.Obstructions)
            {
                string kind = obstruction.ContainsKey("kind") ? obstruction["kind"]?.ToString() : null;
                if (EqualsIgnoreCase(kind, "solid_cell"))
                {
                    object diggableValue;
                    bool diggable = obstruction.TryGetValue("diggable", out diggableValue)
                        && diggableValue != null
                        && bool.TryParse(diggableValue.ToString(), out bool parsed)
                        && parsed;
                    if (!diggable)
                        return null;
                    continue;
                }

                if (EqualsIgnoreCase(kind, "uprootable"))
                {
                    bool uprootEnabled = ToolUtil.GetBool(args, "autoUprootObstructions", true);
                    object canUprootValue;
                    bool canUproot = obstruction.TryGetValue("canUproot", out canUprootValue)
                        && canUprootValue != null
                        && bool.TryParse(canUprootValue.ToString(), out bool parsed)
                        && parsed;
                    if (!uprootEnabled || !canUproot)
                        return null;
                    continue;
                }

                    return null;
            }

            var digTargets = placement.Footprint
                .Where(cell => IsNaturalDiggableSolidCell(cell.Cell, placement.WorldId))
                .ToList();
            var uprootTargets = placement.Footprint
                .SelectMany(cell => UprootablesAtCell(cell.Cell, placement.WorldId).Select(uprootable => new { cell, uprootable }))
                .GroupBy(item => item.uprootable.gameObject.GetInstanceID())
                .Select(group => group.First())
                .ToList();
            if (digTargets.Count == 0 && uprootTargets.Count == 0)
                return null;

            bool dryRun = IsDryRun(args);
            context = context ?? AutoDigContext.FromArgs(args);
            var targetResults = new List<Dictionary<string, object>>();
            int wouldMark = 0;
            int marked = 0;
            int alreadyMarked = 0;
            int skipped = 0;
            int failed = 0;
            double kgTotal = 0;

            if (!dryRun && DigTool.Instance == null)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["available"] = false,
                    ["dryRun"] = false,
                    ["needsRetryAfterDig"] = true,
                    ["targetCount"] = digTargets.Count + uprootTargets.Count,
                    ["error"] = "DigTool is not initialized; open a loaded colony UI before issuing automatic dig orders"
                };
            }

            int uprootWouldMark = 0;
            int uprootMarked = 0;
            int alreadyUprootMarked = 0;

            foreach (var target in digTargets)
            {
                bool already = Grid.Objects[target.Cell, (int)ObjectLayer.DigPlacer] != null;
                kgTotal += ToolUtil.SafeFloat(Grid.Mass[target.Cell]);

                if (already)
                {
                    alreadyMarked++;
                    targetResults.Add(AutoDigTarget(target, "already_marked"));
                    continue;
                }

                if (dryRun)
                {
                    wouldMark++;
                    targetResults.Add(AutoDigTarget(target, "would_dig"));
                    continue;
                }

                if (!context.TryReserve(target.Cell))
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target, context.LimitReached ? "limit_skipped" : "duplicate_skipped"));
                    continue;
                }

                if (DigTool.PlaceDig(target.Cell, context.NextDistance()) != null)
                {
                    marked++;
                    context.Marked++;
                    targetResults.Add(AutoDigTarget(target, "marked"));
                }
                else
                {
                    failed++;
                    targetResults.Add(AutoDigTarget(target, "failed"));
                }
            }

            foreach (var target in uprootTargets)
            {
                var uprootable = target.uprootable;
                var go = uprootable.gameObject;
                bool already = uprootable.IsMarkedForUproot;
                if (already)
                {
                    alreadyUprootMarked++;
                    targetResults.Add(AutoDigTarget(target.cell, "already_uproot_marked"));
                    continue;
                }

                if (dryRun)
                {
                    uprootWouldMark++;
                    targetResults.Add(AutoDigTarget(target.cell, "would_uproot"));
                    continue;
                }

                if (!uprootable.CanUproot())
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target.cell, "uproot_unavailable"));
                    continue;
                }

                if (!context.TryReserve(target.cell.Cell))
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target.cell, context.LimitReached ? "limit_skipped" : "duplicate_skipped"));
                    continue;
                }

                uprootable.MarkForUproot();
                SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
                uprootMarked++;
                context.Marked++;
                targetResults.Add(AutoDigTarget(target.cell, "uproot_marked"));
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["available"] = true,
                ["dryRun"] = dryRun,
                ["needsRetryAfterDig"] = false,
                ["action"] = dryRun ? "would_clear_obstructions_for_build" : "queued_clear_obstructions_for_build",
                ["targetCount"] = digTargets.Count + uprootTargets.Count,
                ["digTargets"] = digTargets.Count,
                ["uprootTargets"] = uprootTargets.Count,
                ["wouldMark"] = dryRun ? wouldMark : (object)null,
                ["uprootWouldMark"] = dryRun ? uprootWouldMark : (object)null,
                ["marked"] = marked,
                ["alreadyMarked"] = alreadyMarked,
                ["uprootMarked"] = uprootMarked,
                ["alreadyUprootMarked"] = alreadyUprootMarked,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["limitReached"] = context.LimitReached,
                ["maxCells"] = context.MaxCells,
                ["kgTotal"] = Math.Round(kgTotal, 3),
                ["targets"] = targetResults.Take(50).ToList(),
                ["truncatedTargets"] = Math.Max(0, targetResults.Count - 50),
                ["note"] = "Natural solid cells were marked for digging and uprootable plants were marked for removal; the build planner will still attempt to place the build blueprint on the same cells."
            };
        }

        private static IEnumerable<Dictionary<string, object>> UprootableObstructionsAtCell(int cell, int worldId)
        {
            foreach (var uprootable in UprootablesAtCell(cell, worldId))
            {
                var go = uprootable.gameObject;
                var kpid = go.GetComponent<KPrefabID>();
                yield return new Dictionary<string, object>
                {
                    ["kind"] = "uprootable",
                    ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                    ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                    ["name"] = ToolUtil.CleanName(go.GetProperName()),
                    ["canUproot"] = uprootable.CanUproot(),
                    ["alreadyMarkedForUproot"] = uprootable.IsMarkedForUproot,
                    ["reasonCode"] = "uprootable_plant",
                    ["reason"] = "target footprint contains a plant or uprootable object that can be marked for uproot"
                };
            }
        }

        private static IEnumerable<Uprootable> UprootablesAtCell(int cell, int worldId)
        {
            foreach (var uprootable in Components.Uprootables.Items)
            {
                var go = uprootable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                if (Grid.PosToCell(go) == cell)
                    yield return uprootable;
            }
        }

        private static bool IsNaturalDiggableSolidCell(int cell, int worldId)
        {
            return Grid.IsValidCell(cell)
                && Grid.IsVisible(cell)
                && ToolUtil.CellMatchesWorld(cell, worldId)
                && Grid.Solid[cell]
                && !Grid.Foundation[cell];
        }

        private static Dictionary<string, object> AutoDigTarget(FootprintCell cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["x"] = cell.X,
                ["y"] = cell.Y,
                ["cell"] = cell.Cell,
                ["worldId"] = cell.WorldId,
                ["status"] = status
            };
        }

        private static IEnumerable<Dictionary<string, object>> ExistingBuildingFootprintObstructions(int worldId, HashSet<int> footprintCells)
        {
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null || !ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                foreach (var item in ExistingObjectFootprint(building.gameObject, building.Def, "building", footprintCells))
                    yield return item;
            }

            foreach (var constructable in FindConstructables(worldId))
            {
                var go = constructable?.gameObject;
                if (go == null)
                    continue;
                var building = go.GetComponent<Building>();
                foreach (var item in ExistingObjectFootprint(go, building?.Def, "blueprint", footprintCells))
                    yield return item;
            }
        }
    }
}
