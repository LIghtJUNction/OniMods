using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class SurvivalAccessConstructionPlanner
    {
        public static Dictionary<string, object> BuildLadderPlan(
            Dictionary<string, object> target,
            int worldId,
            List<Navigator> navigators,
            List<Dictionary<string, object>> frontier)
        {
            if (target == null || navigators == null || navigators.Count == 0)
                return null;

            int targetX = Convert.ToInt32(target["x"]);
            int targetY = Convert.ToInt32(target["y"]);
            var referencePoints = FrontierReferencePoints(frontier).ToList();
            var runs = FindReachableOpenRuns(targetX, targetY, worldId, navigators, referencePoints)
                .OrderBy(item => Convert.ToInt32(item["score"]))
                .Take(4)
                .ToList();

            if (runs.Count == 0)
                return null;

            var first = runs[0];
            int x = Convert.ToInt32(first["x"]);
            int y1 = Convert.ToInt32(first["y1"]);
            int y2 = Convert.ToInt32(first["y2"]);
            var materials = FindReachableLooseBuildMaterials(worldId, navigators)
                .OrderBy(item => Convert.ToInt32(item["score"]))
                .ToList();
            var material = materials.FirstOrDefault();
            string materialTag = material != null ? material["tag"].ToString() : "SiltStone";
            var materialDigTargets = material == null
                ? FindReachableNaturalBuildMaterials(worldId, navigators).Take(5).ToList()
                : new List<Dictionary<string, object>>();
            var materialDig = materialDigTargets.FirstOrDefault();
            var materialPrepAction = material != null
                ? SweepMaterialAction(material, worldId)
                : SweepAreaAction(x, Math.Max(0, y1 - 1), x, y2, worldId);
            var materialDigAction = materialDig != null ? DigMaterialAction(materialDig, worldId) : null;

            var buildAction = new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "planning",
                    ["action"] = "build_area",
                    ["prefabId"] = "Ladder",
                    ["material"] = materialTag,
                    ["x1"] = x,
                    ["y1"] = y1,
                    ["x2"] = x,
                    ["y2"] = y2,
                    ["worldId"] = worldId,
                    ["dryRun"] = true,
                    ["limit"] = Math.Max(2, y2 - y1 + 1)
                }
            };
            var result = new Dictionary<string, object>
            {
                ["status"] = material != null ? "material_prep_required" : materialDig != null ? "material_dig_required" : "material_search_required",
                ["reason"] = "risky_frontier_only",
                ["candidates"] = runs,
                ["materialStatus"] = material != null ? "reachable_loose_material_found" : materialDig != null ? "reachable_natural_material_found" : "no_reachable_material_source_found",
                ["materialCandidates"] = materials.Take(5).ToList(),
                ["materialDigCandidates"] = materialDigTargets,
                ["buildBlockedBy"] = "material_not_available_until_prep_finishes",
                ["next"] = material != null
                    ? "Dry-run materialPrepAction; if valid execute sweep, wait for delivery/storage, rerun survival plan, then dry-run/build buildAction."
                    : materialDig != null
                        ? "Dry-run materialDigAction; if valid execute dig, wait for debris, rerun survival plan, then sweep/build."
                        : "No safe reachable loose or natural ladder material found; search_items SiltStone/SandStone before buildAction."
            };
            if (material != null)
            {
                result["materialPrepAction"] = materialPrepAction;
                result["buildAction"] = buildAction;
            }
            else if (materialDig != null)
            {
                result["materialDigAction"] = materialDigAction;
                result["blockedActions"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["kind"] = "build_access",
                        ["disabledReason"] = "material_not_available_until_dig_debris_exists",
                        ["action"] = buildAction
                    }
                };
            }
            else
            {
                result["blockedActions"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["kind"] = "material_prep",
                        ["disabledReason"] = "no_safe_reachable_loose_material",
                        ["action"] = materialPrepAction
                    },
                    new Dictionary<string, object>
                    {
                        ["kind"] = "build_access",
                        ["disabledReason"] = "material_not_available_until_prep_finishes",
                        ["action"] = buildAction
                    }
                };
            }
            return result;
        }

        private static Dictionary<string, object> SweepAreaAction(int x1, int y1, int x2, int y2, int worldId)
        {
            return new Dictionary<string, object>
            {
                ["tool"] = "orders_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "area",
                    ["action"] = "sweep",
                    ["x1"] = x1,
                    ["y1"] = y1,
                    ["x2"] = x2,
                    ["y2"] = y2,
                    ["worldId"] = worldId,
                    ["dryRun"] = true,
                    ["detail"] = true,
                    ["includeStored"] = false
                }
            };
        }

        private static Dictionary<string, object> SweepMaterialAction(Dictionary<string, object> material, int worldId)
        {
            int x = Convert.ToInt32(material["x"]);
            int y = Convert.ToInt32(material["y"]);
            return SweepAreaAction(x, y, x, y, worldId);
        }

        private static IEnumerable<Dictionary<string, object>> FindReachableLooseBuildMaterials(int worldId, List<Navigator> navigators)
        {
            string[] preferred = { "SiltStone", "SandStone", "IgneousRock", "Granite", "SedimentaryRock" };
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (pickupable.storage != null || pickupable.KPrefabID == null || pickupable.KPrefabID.HasTag(GameTags.Stored))
                    continue;

                var primary = pickupable.PrimaryElement;
                if (primary == null)
                    continue;
                string tag = primary.ElementID.ToString();
                int preference = Array.IndexOf(preferred, tag);
                if (preference < 0)
                    continue;

                int cell = pickupable.cachedCell;
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;
                if (IsLiquidRiskCell(cell))
                    continue;
                if (!navigators.Any(navigator => SafeCanReach(navigator, cell)))
                    continue;

                yield return new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["name"] = pickupable.GetProperName(),
                    ["x"] = Grid.CellColumn(cell),
                    ["y"] = Grid.CellRow(cell),
                    ["massKg"] = Math.Round(primary.Mass, 3),
                    ["cellElement"] = Grid.Element[cell]?.id.ToString(),
                    ["cellMassKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                    ["liquidRisk"] = false,
                    ["score"] = preference * 1000 - Math.Min(999, (int)Math.Round(primary.Mass))
                };
            }
        }

        private static IEnumerable<Dictionary<string, object>> FindReachableNaturalBuildMaterials(int worldId, List<Navigator> navigators)
        {
            string[] preferred = { "SiltStone", "SandStone", "IgneousRock", "Granite", "SedimentaryRock" };
            var bounds = NavigatorMaterialBounds(worldId, navigators);
            for (int y = bounds.MinY; y <= bounds.MaxY; y++)
            {
                for (int x = bounds.MinX; x <= bounds.MaxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (!Grid.Solid[cell] || Grid.Foundation[cell] || CellHasBuildingOrBlueprint(cell, worldId))
                        continue;
                    var element = Grid.Element[cell];
                    if (element == null || element.IsLiquid)
                        continue;
                    string tag = element.id.ToString();
                    int preference = Array.IndexOf(preferred, tag);
                    if (preference < 0)
                        continue;
                    int workCell;
                    if (!TryReachableMaterialWorkCell(cell, worldId, navigators, out workCell))
                        continue;
                    yield return new Dictionary<string, object>
                    {
                        ["tag"] = tag,
                        ["x"] = x,
                        ["y"] = y,
                        ["cell"] = cell,
                        ["worldId"] = worldId,
                        ["cellMassKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                        ["workCell"] = new Dictionary<string, object>
                        {
                            ["x"] = Grid.CellColumn(workCell),
                            ["y"] = Grid.CellRow(workCell)
                        },
                        ["score"] = preference * 1000 + NearestNavigatorDistance(cell, navigators)
                    };
                }
            }
        }

        private static Dictionary<string, object> DigMaterialAction(Dictionary<string, object> material, int worldId)
        {
            int x = Convert.ToInt32(material["x"]);
            int y = Convert.ToInt32(material["y"]);
            return new Dictionary<string, object>
            {
                ["tool"] = "orders_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "area",
                    ["action"] = "dig",
                    ["x1"] = x,
                    ["y1"] = y,
                    ["x2"] = x,
                    ["y2"] = y,
                    ["worldId"] = worldId,
                    ["dryRun"] = true,
                    ["detail"] = true,
                    ["limit"] = 10
                }
            };
        }

        private static bool TryReachableMaterialWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            int x = Grid.CellColumn(targetCell);
            int y = Grid.CellRow(targetCell);
            foreach (var offset in new[] { new[] { 0, 1 }, new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, -1 } })
            {
                int candidate = Grid.XYToCell(x + offset[0], y + offset[1]);
                if (!OpenForLadder(candidate, worldId))
                    continue;
                if (navigators.Any(navigator => SafeCanReach(navigator, candidate)))
                {
                    workCell = candidate;
                    return true;
                }
            }
            workCell = -1;
            return false;
        }

        private static Bounds NavigatorMaterialBounds(int worldId, List<Navigator> navigators)
        {
            int minX = int.MaxValue;
            int maxX = 0;
            int minY = int.MaxValue;
            int maxY = 0;
            foreach (var navigator in navigators)
            {
                if (navigator == null || navigator.gameObject.GetMyWorldId() != worldId)
                    continue;
                int cell = Grid.PosToCell(navigator);
                if (!Grid.IsValidCell(cell))
                    continue;
                minX = Math.Min(minX, Grid.CellColumn(cell) - 18);
                maxX = Math.Max(maxX, Grid.CellColumn(cell) + 18);
                minY = Math.Min(minY, Grid.CellRow(cell) - 12);
                maxY = Math.Max(maxY, Grid.CellRow(cell) + 12);
            }
            if (minX == int.MaxValue)
                return new Bounds(0, 0, 0, 0);
            return new Bounds(Math.Max(0, minX), maxX, Math.Max(0, minY), maxY);
        }

        private static int NearestNavigatorDistance(int cell, List<Navigator> navigators)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            int best = 9999;
            foreach (var navigator in navigators)
            {
                int ncell = navigator == null ? -1 : Grid.PosToCell(navigator);
                if (!Grid.IsValidCell(ncell))
                    continue;
                int distance = Math.Abs(x - Grid.CellColumn(ncell)) + Math.Abs(y - Grid.CellRow(ncell));
                if (distance < best)
                    best = distance;
            }
            return best;
        }

        private static bool IsLiquidRiskCell(int cell)
        {
            var element = Grid.Element[cell];
            return element != null && element.IsLiquid && Grid.Mass[cell] > 1f;
        }

        private static IEnumerable<Dictionary<string, object>> FindReachableOpenRuns(
            int targetX,
            int targetY,
            int worldId,
            List<Navigator> navigators,
            List<Dictionary<string, int>> referencePoints)
        {
            Bounds bounds = BuildBounds(targetX, targetY, worldId, navigators);
            for (int x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                int runStart = -1;
                for (int y = bounds.MinY; y <= bounds.MaxY + 1; y++)
                {
                    bool open = y <= bounds.MaxY && OpenForLadder(Grid.XYToCell(x, y), worldId);
                    if (open && runStart < 0)
                    {
                        runStart = y;
                        continue;
                    }

                    if ((open || runStart < 0) && y <= bounds.MaxY)
                        continue;

                    int runEnd = y - 1;
                    if (runEnd - runStart + 1 >= 2)
                    {
                        var reachable = ReachableCellsInRun(x, runStart, runEnd, navigators).ToList();
                        if (reachable.Count > 0)
                            yield return RunCandidate(x, runStart, runEnd, targetX, targetY, reachable, referencePoints);
                    }
                    runStart = open ? y : -1;
                }
            }
        }

        private static Bounds BuildBounds(int targetX, int targetY, int worldId, List<Navigator> navigators)
        {
            int minX = Math.Max(0, targetX - 40);
            int maxX = targetX + 40;
            int minY = Math.Max(0, targetY - 40);
            int maxY = targetY + 40;

            foreach (var navigator in navigators)
            {
                if (navigator == null || navigator.gameObject.GetMyWorldId() != worldId)
                    continue;
                int cell = Grid.PosToCell(navigator);
                if (!Grid.IsValidCell(cell))
                    continue;
                minX = Math.Min(minX, Grid.CellColumn(cell) - 4);
                maxX = Math.Max(maxX, Grid.CellColumn(cell) + 4);
                minY = Math.Min(minY, Grid.CellRow(cell) - 6);
                maxY = Math.Max(maxY, Grid.CellRow(cell) + 6);
            }

            return new Bounds(minX, maxX, minY, maxY);
        }

        private static bool OpenForLadder(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return false;
            if (Grid.Solid[cell] || Grid.Foundation[cell])
                return false;
            if (CellHasBuildingOrBlueprint(cell, worldId))
                return false;
            var element = Grid.Element[cell];
            return element == null || !element.IsLiquid || Grid.Mass[cell] <= 1f;
        }

        private static bool CellHasBuildingOrBlueprint(int cell, int worldId)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            foreach (var complete in Components.BuildingCompletes.Items)
            {
                if (complete == null || complete.GetMyWorldId() != worldId)
                    continue;
                if (FootprintContains(complete.gameObject, complete.Def, x, y))
                    return true;
            }

            foreach (var constructable in UnityEngine.Object.FindObjectsByType<Constructable>(UnityEngine.FindObjectsSortMode.None))
            {
                if (constructable == null || constructable.gameObject == null || constructable.gameObject.GetMyWorldId() != worldId)
                    continue;
                var kpid = constructable.gameObject.GetComponent<KPrefabID>();
                var def = kpid != null ? Assets.GetBuildingDef(kpid.PrefabTag.Name) : null;
                if (FootprintContains(constructable.gameObject, def, x, y))
                    return true;
            }
            return false;
        }

        private static bool FootprintContains(UnityEngine.GameObject go, BuildingDef def, int x, int y)
        {
            if (go == null || def == null)
                return false;
            var building = go.GetComponent<Building>();
            int anchor = building != null ? building.GetBottomLeftCell() : Grid.PosToCell(go);
            if (!Grid.IsValidCell(anchor))
                return false;
            int ax = Grid.CellColumn(anchor);
            int ay = Grid.CellRow(anchor);
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            return x >= ax && x < ax + width && y >= ay && y < ay + height;
        }

        private static IEnumerable<Dictionary<string, object>> ReachableCellsInRun(
            int x,
            int y1,
            int y2,
            List<Navigator> navigators)
        {
            for (int y = y1; y <= y2; y++)
            {
                int cell = Grid.XYToCell(x, y);
                if (navigators.Any(navigator => SafeCanReach(navigator, cell)))
                    yield return new Dictionary<string, object> { ["x"] = x, ["y"] = y };
            }
        }

        private static Dictionary<string, object> RunCandidate(
            int x,
            int y1,
            int y2,
            int targetX,
            int targetY,
            List<Dictionary<string, object>> reachable,
            List<Dictionary<string, int>> referencePoints)
        {
            int midY = (y1 + y2) / 2;
            int targetDistance = Math.Abs(x - targetX) + Math.Abs(midY - targetY);
            int frontierDistance = referencePoints.Count == 0
                ? targetDistance
                : referencePoints.Min(point => Math.Abs(x - point["x"]) + Math.Abs(midY - point["y"]));
            int score = frontierDistance * 10 + targetDistance;
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y1"] = y1,
                ["y2"] = y2,
                ["height"] = y2 - y1 + 1,
                ["score"] = score,
                ["frontierDistance"] = frontierDistance,
                ["targetDistance"] = targetDistance,
                ["risk"] = "none",
                ["reachableSamples"] = reachable.Take(3).ToList()
            };
        }

        private static IEnumerable<Dictionary<string, int>> FrontierReferencePoints(List<Dictionary<string, object>> frontier)
        {
            if (frontier == null)
                yield break;
            foreach (var item in frontier)
            {
                if (item == null || !item.ContainsKey("x") || !item.ContainsKey("y"))
                    continue;
                yield return new Dictionary<string, int>
                {
                    ["x"] = Convert.ToInt32(item["x"]),
                    ["y"] = Convert.ToInt32(item["y"])
                };
                if (item.TryGetValue("workCell", out var value) && value is Dictionary<string, object> workCell)
                {
                    yield return new Dictionary<string, int>
                    {
                        ["x"] = Convert.ToInt32(workCell["x"]),
                        ["y"] = Convert.ToInt32(workCell["y"])
                    };
                }
            }
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private readonly struct Bounds
        {
            public readonly int MinX;
            public readonly int MaxX;
            public readonly int MinY;
            public readonly int MaxY;

            public Bounds(int minX, int maxX, int minY, int maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }
        }
    }
}
