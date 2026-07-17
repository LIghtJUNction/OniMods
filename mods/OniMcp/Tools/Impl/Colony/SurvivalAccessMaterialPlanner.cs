using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static partial class SurvivalAccessConstructionPlanner
    {
        private static readonly string[] PreferredBuildMaterials =
        {
            "SiltStone",
            "SandStone",
            "IgneousRock",
            "Granite",
            "SedimentaryRock"
        };

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

        private static Dictionary<string, object> DeconstructMaterialAction(Dictionary<string, object> material, int worldId)
        {
            int x = Convert.ToInt32(material["x"]);
            int y = Convert.ToInt32(material["y"]);
            return new Dictionary<string, object>
            {
                ["tool"] = "orders_control",
                ["arguments"] = new Dictionary<string, object>
                {
                    ["domain"] = "designation",
                    ["action"] = "deconstruct",
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = worldId,
                    ["dryRun"] = true
                }
            };
        }

        private static IEnumerable<Dictionary<string, object>> FindReachableLooseBuildMaterials(
            int worldId,
            List<Navigator> navigators)
        {
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (pickupable.storage != null ||
                    pickupable.KPrefabID == null ||
                    pickupable.KPrefabID.HasTag(GameTags.Stored))
                    continue;
                var primary = pickupable.PrimaryElement;
                if (primary == null)
                    continue;
                string tag = primary.ElementID.ToString();
                int preference = Array.IndexOf(PreferredBuildMaterials, tag);
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

        private static IEnumerable<Dictionary<string, object>> FindReachableNaturalBuildMaterials(
            int worldId,
            List<Navigator> navigators)
        {
            var bounds = NavigatorMaterialBounds(worldId, navigators);
            for (int y = bounds.MinY; y <= bounds.MaxY; y++)
            {
                for (int x = bounds.MinX; x <= bounds.MaxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (CellHasBuildingOrBlueprint(cell, worldId))
                        continue;
                    var element = Grid.Element[cell];
                    if (element == null || element.IsLiquid)
                        continue;
                    string tag = element.id.ToString();
                    int preference = Array.IndexOf(PreferredBuildMaterials, tag);
                    if (preference < 0)
                        continue;
                    if (!TryReachableMaterialWorkCell(cell, worldId, navigators, out int workCell))
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

        private static IEnumerable<Dictionary<string, object>> FindReachableDeconstructBuildMaterials(
            int worldId,
            List<Navigator> navigators,
            int ladderX,
            int ladderY1,
            int ladderY2)
        {
            foreach (var complete in Components.BuildingCompletes.Items)
            {
                if (complete == null || complete.gameObject == null || complete.GetMyWorldId() != worldId)
                    continue;
                var kpid = complete.GetComponent<KPrefabID>();
                string prefabId = kpid != null ? kpid.PrefabTag.Name : null;
                if (prefabId != "Tile")
                    continue;
                var primary = complete.GetComponent<PrimaryElement>();
                if (primary == null)
                    continue;
                string tag = primary.ElementID.ToString();
                int preference = Array.IndexOf(PreferredBuildMaterials, tag);
                if (preference < 0)
                    continue;

                int cell = Grid.PosToCell(complete);
                var building = complete.GetComponent<Building>();
                if (building != null)
                    cell = building.GetBottomLeftCell();
                if (!Grid.IsValidCell(cell) || IsLiquidRiskCell(cell))
                    continue;

                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                if (Math.Abs(x - ladderX) > 8 || y < ladderY1 - 2 || y > ladderY2 + 2)
                    continue;
                if (!navigators.Any(navigator => SafeCanReach(navigator, cell)))
                    continue;

                yield return new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["prefabId"] = prefabId,
                    ["name"] = complete.GetProperName(),
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = worldId,
                    ["source"] = "deconstruct_reachable_tile",
                    ["score"] = preference * 1000 + NearestNavigatorDistance(cell, navigators)
                };
            }
        }

        private static bool TryReachableMaterialWorkCell(
            int targetCell,
            int worldId,
            List<Navigator> navigators,
            out int workCell)
        {
            int x = Grid.CellColumn(targetCell);
            int y = Grid.CellRow(targetCell);
            int[][] offsets =
            {
                new[] { 0, 1 },
                new[] { 1, 0 },
                new[] { -1, 0 },
                new[] { 0, -1 }
            };
            foreach (var offset in offsets)
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
            return minX == int.MaxValue
                ? new Bounds(0, 0, 0, 0)
                : new Bounds(Math.Max(0, minX), Math.Max(0, maxX), Math.Max(0, minY), Math.Max(0, maxY));
        }

        private static bool OpenForLadder(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return false;
            if (CellHasBuildingOrBlueprint(cell, worldId))
                return false;
            var element = Grid.Element[cell];
            return element == null || !element.IsLiquid && !element.IsSolid;
        }

        private static bool CellHasBuildingOrBlueprint(int cell, int worldId)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            foreach (var complete in Components.BuildingCompletes.Items)
            {
                if (complete == null || complete.gameObject == null || complete.GetMyWorldId() != worldId)
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
    }
}
