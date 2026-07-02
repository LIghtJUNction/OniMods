using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static partial class SurvivalAccessConstructionPlanner
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
            var looseMaterials = FindReachableLooseBuildMaterials(worldId, navigators)
                .OrderBy(item => Convert.ToInt32(item["score"]))
                .ToList();
            var looseMaterial = looseMaterials.FirstOrDefault();
            var naturalMaterials = looseMaterial == null
                ? FindReachableNaturalBuildMaterials(worldId, navigators).Take(5).ToList()
                : new List<Dictionary<string, object>>();
            var naturalMaterial = naturalMaterials.FirstOrDefault();
            var deconstructMaterials = looseMaterial == null && naturalMaterial == null
                ? FindReachableDeconstructBuildMaterials(worldId, navigators, x, y1, y2).Take(5).ToList()
                : new List<Dictionary<string, object>>();
            var deconstructMaterial = deconstructMaterials.FirstOrDefault();

            var materialSource = looseMaterial ?? naturalMaterial ?? deconstructMaterial;
            string materialTag = materialSource != null ? materialSource["tag"].ToString() : "SiltStone";
            var buildAction = LadderBuildAction(materialTag, x, y1, y2, worldId);
            var result = new Dictionary<string, object>
            {
                ["status"] = StatusFor(looseMaterial, naturalMaterial, deconstructMaterial),
                ["reason"] = "risky_frontier_only",
                ["candidates"] = runs,
                ["materialStatus"] = MaterialStatusFor(looseMaterial, naturalMaterial, deconstructMaterial),
                ["materialCandidates"] = looseMaterials.Take(5).ToList(),
                ["materialDigCandidates"] = naturalMaterials,
                ["materialDeconstructCandidates"] = deconstructMaterials,
                ["buildBlockedBy"] = "material_not_available_until_prep_finishes",
                ["next"] = NextFor(looseMaterial, naturalMaterial, deconstructMaterial)
            };

            if (looseMaterial != null)
            {
                result["materialPrepAction"] = SweepMaterialAction(looseMaterial, worldId);
                result["buildAction"] = buildAction;
                return result;
            }

            if (naturalMaterial != null)
            {
                result["materialDigAction"] = DigMaterialAction(naturalMaterial, worldId);
                result["blockedActions"] = BlockedBuild(buildAction, "material_not_available_until_dig_debris_exists");
                return result;
            }

            if (deconstructMaterial != null)
            {
                result["materialDeconstructAction"] = DeconstructMaterialAction(deconstructMaterial, worldId);
                result["blockedActions"] = BlockedBuild(buildAction, "material_not_available_until_deconstruct_finishes");
                return result;
            }

            result["blockedActions"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["kind"] = "material_prep",
                    ["disabledReason"] = "no_safe_reachable_material_source",
                    ["action"] = SweepAreaAction(x, Math.Max(0, y1 - 1), x, y2, worldId)
                },
                new Dictionary<string, object>
                {
                    ["kind"] = "build_access",
                    ["disabledReason"] = "material_not_available_until_prep_finishes",
                    ["action"] = buildAction
                }
            };
            return result;
        }

        private static Dictionary<string, object> LadderBuildAction(
            string materialTag,
            int x,
            int y1,
            int y2,
            int worldId)
        {
            return new Dictionary<string, object>
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
        }

        private static string StatusFor(
            Dictionary<string, object> loose,
            Dictionary<string, object> natural,
            Dictionary<string, object> deconstruct)
        {
            if (loose != null) return "material_prep_required";
            if (natural != null) return "material_dig_required";
            if (deconstruct != null) return "material_deconstruct_required";
            return "material_search_required";
        }

        private static string MaterialStatusFor(
            Dictionary<string, object> loose,
            Dictionary<string, object> natural,
            Dictionary<string, object> deconstruct)
        {
            if (loose != null) return "reachable_loose_material_found";
            if (natural != null) return "reachable_natural_material_found";
            if (deconstruct != null) return "reachable_deconstruct_material_found";
            return "no_reachable_material_source_found";
        }

        private static string NextFor(
            Dictionary<string, object> loose,
            Dictionary<string, object> natural,
            Dictionary<string, object> deconstruct)
        {
            if (loose != null)
                return "Dry-run materialPrepAction; if valid, execute sweep, wait delivery/storage, rerun survival plan, then dry-run/build buildAction.";
            if (natural != null)
                return "Dry-run materialDigAction; if valid, execute dig, wait debris, rerun survival plan, then sweep/build.";
            if (deconstruct != null)
                return "Dry-run materialDeconstructAction; if valid, execute deconstruct, wait debris, rerun survival plan, then build.";
            return "No safe reachable loose, natural, or deconstructable ladder material found; inspect compact map before risky digging.";
        }

        private static object BlockedBuild(Dictionary<string, object> buildAction, string reason)
        {
            return new[]
            {
                new Dictionary<string, object>
                {
                    ["kind"] = "build_access",
                    ["disabledReason"] = reason,
                    ["action"] = buildAction
                }
            };
        }

        private static IEnumerable<Dictionary<string, object>> FindReachableOpenRuns(
            int targetX,
            int targetY,
            int worldId,
            List<Navigator> navigators,
            List<Dictionary<string, int>> referencePoints)
        {
            var bounds = BuildBounds(targetX, targetY, worldId, navigators);
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
            return new Bounds(Math.Max(0, minX), maxX, Math.Max(0, minY), maxY);
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
            int frontierDistance = referencePoints.Count == 0 ? 0 : referencePoints.Min(point =>
                Math.Abs(x - point["x"]) + Math.Abs(midY - point["y"]));
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y1"] = y1,
                ["y2"] = y2,
                ["height"] = y2 - y1 + 1,
                ["score"] = targetDistance + frontierDistance * 10,
                ["frontierDistance"] = frontierDistance,
                ["targetDistance"] = targetDistance,
                ["risk"] = "none",
                ["reachableSamples"] = reachable.Take(3).ToList()
            };
        }

        private static IEnumerable<Dictionary<string, int>> FrontierReferencePoints(List<Dictionary<string, object>> frontier)
        {
            foreach (var item in frontier ?? new List<Dictionary<string, object>>())
            {
                if (!item.ContainsKey("x") || !item.ContainsKey("y"))
                    continue;
                yield return new Dictionary<string, int>
                {
                    ["x"] = Convert.ToInt32(item["x"]),
                    ["y"] = Convert.ToInt32(item["y"])
                };
                if (item.TryGetValue("workCell", out var workCellValue) &&
                    workCellValue is Dictionary<string, object> workCell &&
                    workCell.ContainsKey("x") &&
                    workCell.ContainsKey("y"))
                {
                    yield return new Dictionary<string, int>
                    {
                        ["x"] = Convert.ToInt32(workCell["x"]),
                        ["y"] = Convert.ToInt32(workCell["y"])
                    };
                }
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
