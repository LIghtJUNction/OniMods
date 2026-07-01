using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SurvivalPlanTools
    {
        public static McpTool ControlSurvivalPlan()
        {
            return new McpTool
            {
                Name = "survival_plan_control",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Tags = new List<string> { "survival", "diagnostics", "food", "oxygen", "long-run", "100-cycle" },
                Description = "低 token 生存分诊：action=plan，把 food/oxygen/stress/red-alert 诊断转成是否可长跑、阻塞原因和下一步 MCP 调用建议。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "plan 或 status，默认 plan", Required = false },
                    ["targetCycles"] = new McpToolParameter { Type = "integer", Description = "目标长跑周期数，默认 100", Required = false },
                    ["foodKcalPerDupe"] = new McpToolParameter { Type = "number", Description = "每个复制人的最低食物库存阈值，默认 2000 kcal", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    return CallToolResult.Text(JsonConvert.SerializeObject(BuildSurvivalPlan(args), McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> BuildSurvivalPlan(Newtonsoft.Json.Linq.JObject args)
        {
            int targetCycles = ToolUtil.GetInt(args, "targetCycles") ?? 100;
            float foodPerDupe = ToolUtil.GetFloat(args, "foodKcalPerDupe") ?? 2000f;
            int dupes = Components.LiveMinionIdentities.Count;
            float foodKcal = Components.Edibles.Items.Where(e => e != null).Sum(e => ToolUtil.SafeFloat(e.Calories) / 1000f);
            float maxStress = MaxStress();
            var buildings = CountBuildings();
            int oxygenProducers = CountMatching(buildings, "OxygenDiffuser", "MineralDeoxidizer", "Electrolyzer");
            int toilets = CountMatching(buildings, "Outhouse", "FlushToilet");
            int beds = CountMatching(buildings, "Bed", "LuxuryBed");
            var allReadyHarvestables = ReadyHarvestables().Take(40).ToList();
            var reachableHarvestables = allReadyHarvestables
                .Where(item => item.TryGetValue("reachable", out var value) && value is bool reachable && reachable)
                .Take(20)
                .ToList();
            var readyHarvestables = (reachableHarvestables.Count > 0 ? reachableHarvestables : allReadyHarvestables).Take(20).ToList();
            var harvestIds = reachableHarvestables.Select(item => item["id"]).Where(id => Convert.ToInt32(id) > 0).Take(8).ToList();
            var foodAccessPlan = BuildFoodAccessPlan(allReadyHarvestables, reachableHarvestables);
            float foodNeed = dupes * foodPerDupe;
            var blockers = new List<Dictionary<string, object>>();
            var nextCalls = new List<string>();

            if (dupes <= 0)
                AddBlocker(blockers, "critical", "dupes", "No live duplicants detected.");
            if (foodKcal < foodNeed)
            {
                AddBlocker(blockers, "critical", "food", $"Food stock {Math.Round(foodKcal, 1)} kcal below {Math.Round(foodNeed, 1)} kcal threshold for {dupes} dupes.");
                nextCalls.Add("read_control domain=resources action=food limit=10");
                nextCalls.Add("read_control domain=resources action=search_items query=FieldRation includeStored=false limit=20");
                nextCalls.Add("read_control domain=resources action=search_items query=Muckroot includeStored=false limit=20");
                nextCalls.Add("colony_control domain=bio kind=farming action=list_harvestables readyOnly=true limit=20");
                if (harvestIds.Count > 0)
                    nextCalls.Add("for each harvestAction.ids: colony_control domain=bio kind=farming action=set_harvestable id=<id> readyOnly=true");
                if (reachableHarvestables.Count == 0 && allReadyHarvestables.Count > 0)
                {
                    AddBlocker(blockers, "warning", "food_reachability", "Ready harvestables exist, but none are currently reachable by active duplicants.");
                    nextCalls.Add("dupes_control domain=info action=status_check worldId=0 limit=10");
                    if (foodAccessPlan != null && foodAccessPlan.ContainsKey("frontierDigAction"))
                        nextCalls.Add("execute accessPlan.frontierDigAction; it is dryRun=true");
                }
            }
            if (oxygenProducers == 0)
            {
                AddBlocker(blockers, "warning", "oxygen", "No oxygen producer building detected.");
                nextCalls.Add("building_control domain=planning action=parse_plan plan=\"藻类制氧机@基地\" worldId=0 limit=5");
                nextCalls.Add("building_control domain=planning action=build_area plan=\"藻类制氧机@基地\" worldId=0 dryRun=true limit=5");
            }
            if (toilets == 0)
                AddBlocker(blockers, "warning", "hygiene", "No toilet detected.");
            if (beds < dupes)
                AddBlocker(blockers, "info", "sleep", $"Beds {beds}/{dupes}.");
            if (maxStress >= 60f)
                AddBlocker(blockers, "warning", "stress", $"Max stress {Math.Round(maxStress, 1)}%.");

            nextCalls.Add("colony_control domain=diagnostic action=alerts limit=10");
            nextCalls.Add("python .agents/skills/oni-mcp-autonomous-iteration/scripts/survival_watch.py --target-cycles 100 --poll-seconds 20 --speed 3");

            bool hasCritical = blockers.Any(item => string.Equals(item["severity"]?.ToString(), "critical", StringComparison.OrdinalIgnoreCase));
            var constructionPlan = ExtractConstructionPlan(foodAccessPlan);
            var nextActions = BuildNextActions(constructionPlan);
            return new Dictionary<string, object>
            {
                ["targetCycles"] = targetCycles,
                ["canAttemptLongRun"] = !hasCritical,
                ["decision"] = hasCritical ? "fix_critical_before_100_cycle_run" : "long_run_allowed_with_monitoring",
                ["metrics"] = new Dictionary<string, object>
                {
                    ["cycle"] = GameUtil.GetCurrentCycle(),
                    ["dupes"] = dupes,
                    ["foodKcal"] = Math.Round(foodKcal, 1),
                    ["foodNeedKcal"] = Math.Round(foodNeed, 1),
                    ["maxStress"] = Math.Round(maxStress, 1),
                    ["oxygenProducers"] = oxygenProducers,
                    ["toilets"] = toilets,
                    ["beds"] = beds,
                    ["readyHarvestables"] = allReadyHarvestables.Count,
                    ["reachableHarvestables"] = reachableHarvestables.Count
                },
                ["blockers"] = blockers,
                ["reachability"] = new Dictionary<string, object>
                {
                    ["readyHarvestables"] = allReadyHarvestables.Count,
                    ["reachableHarvestables"] = reachableHarvestables.Count,
                    ["harvestActionAvailable"] = harvestIds.Count > 0,
                    ["candidatePolicy"] = reachableHarvestables.Count > 0 ? "reachable_only" : "report_unreachable_only"
                },
                ["accessPlan"] = foodAccessPlan,
                ["constructionPlan"] = constructionPlan,
                ["nextActions"] = nextActions,
                ["readyHarvestables"] = readyHarvestables.Take(6).ToList(),
                ["harvestActionAvailable"] = harvestIds.Count > 0,
                ["harvestAction"] = harvestIds.Count == 0 ? null : new Dictionary<string, object>
                {
                    ["tool"] = "colony_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "bio",
                        ["kind"] = "farming",
                        ["action"] = "set_harvestable",
                        ["readyOnly"] = true
                    },
                    ["idField"] = "id",
                    ["ids"] = harvestIds,
                    ["reachableOnly"] = reachableHarvestables.Count > 0,
                    ["reason"] = "critical_food"
                },
                ["nextCalls"] = nextCalls.Distinct().Take(12).ToList(),
                ["tokenHint"] = "Read canAttemptLongRun, decision, blockers, nextActions. Prefer structured nextActions over nextCalls; keep survival_watch as the final proof gate."
            };
        }

        private static void AddBlocker(List<Dictionary<string, object>> blockers, string severity, string category, string message)
        {
            blockers.Add(new Dictionary<string, object> { ["severity"] = severity, ["category"] = category, ["message"] = message });
        }

        private static Dictionary<string, int> CountBuildings()
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null)
                    continue;
                var pos = building.transform.GetPosition();
                string id = (building.Def?.PrefabID ?? building.name) + "|" + Math.Round(pos.x) + "|" + Math.Round(pos.y) + "|" + building.GetMyWorldId();
                if (!seen.Add(id))
                    continue;
                string prefab = building.Def?.PrefabID ?? building.name;
                counts[prefab] = counts.ContainsKey(prefab) ? counts[prefab] + 1 : 1;
            }
            return counts;
        }

        private static int CountMatching(Dictionary<string, int> counts, params string[] needles)
        {
            return counts.Where(kv => needles.Any(needle => kv.Key.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)).Sum(kv => kv.Value);
        }

        private static IEnumerable<Dictionary<string, object>> ReadyHarvestables()
        {
            foreach (var harvestable in Components.HarvestDesignatables.Items)
            {
                var go = harvestable?.gameObject;
                if (go == null || !harvestable.CanBeHarvested() || harvestable.MarkedForHarvest)
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                int cell = Grid.PosToCell(go);
                yield return new Dictionary<string, object>
                {
                    ["id"] = kpid?.InstanceID ?? 0,
                    ["name"] = ToolUtil.CleanName(go.GetProperName()),
                    ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                    ["x"] = Grid.CellToXY(cell).x,
                    ["y"] = Grid.CellToXY(cell).y,
["worldId"] = go.GetMyWorldId(),
["reachable"] = AnyNavigatorCanReach(cell, go.GetMyWorldId())
                };
            }
        }

        private static bool AnyNavigatorCanReach(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell))
                return false;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != worldId)
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null && SafeCanReach(navigator, cell))
                    return true;
            }
            return false;
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> BuildFoodAccessPlan(
            List<Dictionary<string, object>> allReadyHarvestables,
            List<Dictionary<string, object>> reachableHarvestables)
        {
            if (allReadyHarvestables.Count == 0 || reachableHarvestables.Count > 0)
                return null;

            var navigators = ActiveNavigators(-1);
            if (navigators.Count == 0)
                return new Dictionary<string, object> { ["status"] = "no_active_navigators" };

            var target = allReadyHarvestables
                .OrderBy(item => NearestNavigatorDistance(item, navigators))
                .First();
            int targetX = Convert.ToInt32(target["x"]);
            int targetY = Convert.ToInt32(target["y"]);
            int worldId = Convert.ToInt32(target["worldId"]);
            var frontier = FindFrontierDigCells(targetX, targetY, worldId, navigators, 6);
            var safeFrontier = frontier
                .Where(item => string.Equals(item["risk"]?.ToString(), "none", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var constructionPlan = safeFrontier.Count == 0
                ? SurvivalAccessConstructionPlanner.BuildLadderPlan(target, worldId, navigators, frontier)
                : null;

            var result = new Dictionary<string, object>
            {
                ["status"] = safeFrontier.Count > 0 ? "frontier_dig_available" : frontier.Count > 0 ? "risky_frontier_only" : "no_reachable_frontier_found",
                ["target"] = target,
                ["frontierDigCells"] = frontier,
                ["next"] = safeFrontier.Count > 0
                    ? "Dry-run the first frontier dig cell, then rerun survival plan after reachable area changes."
                    : constructionPlan != null
                    ? "Dry-run constructionPlan.buildAction before risky digging."
                    : frontier.Count > 0
                    ? "Only risky frontier dig cells remain; inspect compact map or place safer ladder/stair access before digging."
                    : "Read a compact map around target and current dupe reachable samples before placing stairs/ladders."
            };
            if (constructionPlan != null)
                result["constructionPlan"] = constructionPlan;

            if (safeFrontier.Count > 0)
            {
                int x = Convert.ToInt32(safeFrontier[0]["x"]);
                int y = Convert.ToInt32(safeFrontier[0]["y"]);
                result["frontierDigDryRun"] = "Use frontierDigAction.arguments exactly; dryRun=true is set.";
                result["frontierDigAction"] = new Dictionary<string, object>
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
                        ["detail"] = true
                    }
                };
            }

            return result;
        }

        private static List<Navigator> ActiveNavigators(int worldId)
        {
            var navigators = new List<Navigator>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || (worldId >= 0 && dupe.GetMyWorldId() != worldId))
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null)
                    navigators.Add(navigator);
            }
            return navigators;
        }

        private static int NearestNavigatorDistance(Dictionary<string, object> target, List<Navigator> navigators)
        {
            int tx = Convert.ToInt32(target["x"]);
            int ty = Convert.ToInt32(target["y"]);
            int worldId = Convert.ToInt32(target["worldId"]);
            int best = int.MaxValue;
            foreach (var navigator in navigators)
            {
                if (navigator == null || navigator.gameObject.GetMyWorldId() != worldId)
                    continue;
                int cell = Grid.PosToCell(navigator);
                if (!Grid.IsValidCell(cell))
                    continue;
                int distance = Math.Abs(Grid.CellColumn(cell) - tx) + Math.Abs(Grid.CellRow(cell) - ty);
                if (distance < best)
                    best = distance;
            }
            return best;
        }

        private static List<Dictionary<string, object>> FindFrontierDigCells(
            int targetX,
            int targetY,
            int worldId,
            List<Navigator> navigators,
            int limit)
        {
            var candidates = new List<Dictionary<string, object>>();
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
                minY = Math.Min(minY, Grid.CellRow(cell) - 4);
                maxY = Math.Max(maxY, Grid.CellRow(cell) + 4);
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (!Grid.Solid[cell] || Grid.Foundation[cell])
                        continue;
                    if (SupportsFloorBuilding(cell, worldId))
                        continue;
                    if (!TryReachableAdjacent(cell, navigators, out int workCell))
                        continue;

                    bool adjacentLiquid = HasAdjacentLiquid(cell);
                    int distance = Math.Abs(x - targetX) + Math.Abs(y - targetY);
                    int score = distance + (adjacentLiquid ? 100 : 0);
                    candidates.Add(new Dictionary<string, object>
                    {
                        ["x"] = x,
                        ["y"] = y,
                        ["distance"] = distance,
                        ["score"] = score,
                        ["risk"] = adjacentLiquid ? "adjacent_liquid" : "none",
                        ["supportRisk"] = "none",
                        ["workCell"] = new Dictionary<string, object>
                        {
                            ["x"] = Grid.CellColumn(workCell),
                            ["y"] = Grid.CellRow(workCell)
                        }
                    });
                }
            }

            return candidates.OrderBy(item => Convert.ToInt32(item["score"])).Take(limit).ToList();
        }

        private static bool TryReachableAdjacent(int cell, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            foreach (int candidate in AdjacentCells(cell))
            {
                if (!Grid.IsValidCell(candidate) || Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;
                foreach (var navigator in navigators)
                {
                    if (navigator != null && SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasAdjacentLiquid(int cell)
        {
            foreach (int candidate in AdjacentCells(cell))
            {
                if (!Grid.IsValidCell(candidate) || Grid.Solid[candidate])
                    continue;
                var element = Grid.Element[candidate];
                if (element != null && element.IsLiquid && Grid.Mass[candidate] > 1f)
                    return true;
            }
            return false;
        }

        private static bool SupportsFloorBuilding(int cell, int worldId)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.GetMyWorldId() != worldId)
                    continue;
                var def = building.Def;
                if (def == null || !string.Equals(def.BuildLocationRule.ToString(), "OnFloor", StringComparison.OrdinalIgnoreCase))
                    continue;
                var component = building.GetComponent<Building>();
                int anchorCell = component != null ? component.GetBottomLeftCell() : Grid.PosToCell(building);
                if (!Grid.IsValidCell(anchorCell))
                    continue;
                int anchorX = Grid.CellColumn(anchorCell);
                int supportY = Grid.CellRow(anchorCell) - 1;
                int width = Math.Max(1, def.WidthInCells);
                if (y == supportY && x >= anchorX && x < anchorX + width)
                    return true;
            }
            return false;
        }

        private static IEnumerable<int> AdjacentCells(int cell)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            yield return Grid.XYToCell(x + 1, y);
            yield return Grid.XYToCell(x - 1, y);
            yield return Grid.XYToCell(x, y + 1);
            yield return Grid.XYToCell(x, y - 1);
        }

        private static float MaxStress()
        {
            float value = 0f;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
                value = Math.Max(value, DupeAmountUtil.StressValue(dupe));
            return value;
        }
    }
}
