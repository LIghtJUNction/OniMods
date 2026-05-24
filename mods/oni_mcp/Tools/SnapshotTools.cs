using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static class SnapshotTools
    {
        public static McpTool GetColonyStateSnapshot()
        {
            return new McpTool
            {
                Name = "colony_state_snapshot",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "state_snapshot", "colony_snapshot" },
                Tags = new List<string> { "snapshot", "colony", "status", "diagnostics", "dupes", "food", "research", "performance", "快照", "状态" },
                Description = "【高效观察首选】一次返回低 token 殖民地状态快照，替代 game_time + colony_status + colony_diagnostics + colony_alerts + resources_food + dupes_list + research_status 的常规组合调用。默认不扫描全图大气；需要氧气统计时显式 includeAtmosphere=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["profile"] = new McpToolParameter { Type = "string", Description = "brief/standard/full；brief 最低 token，standard 默认，full 返回更多复制人和食物明细", Required = false, EnumValues = new List<string> { "brief", "standard", "full" } },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；传 -1 汇总全部可轻量统计项", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否包含复制人摘要，默认 true", Required = false },
                    ["includeFood"] = new McpToolParameter { Type = "boolean", Description = "是否包含食物摘要，默认 true", Required = false },
                    ["includeResearch"] = new McpToolParameter { Type = "boolean", Description = "是否包含研究状态，默认 true", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否包含关键建筑计数，默认 true", Required = false },
                    ["includeAlerts"] = new McpToolParameter { Type = "boolean", Description = "是否包含快照告警，默认 true", Required = false },
                    ["includeAtmosphere"] = new McpToolParameter { Type = "boolean", Description = "是否扫描可见大气格子；这是全图扫描，默认 false", Required = false },
                    ["dupeLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人明细数量，默认 brief=0 standard=12 full=50", Required = false },
                    ["foodLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回食物类型数量，默认 brief=0 standard=8 full=50", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string profile = NormalizeProfile(args["profile"]?.ToString());
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? 0);
                    bool includeDupes = ToolUtil.GetBool(args, "includeDupes", true);
                    bool includeFood = ToolUtil.GetBool(args, "includeFood", true);
                    bool includeResearch = ToolUtil.GetBool(args, "includeResearch", true);
                    bool includeBuildings = ToolUtil.GetBool(args, "includeBuildings", true);
                    bool includeAlerts = ToolUtil.GetBool(args, "includeAlerts", true);
                    bool includeAtmosphere = ToolUtil.GetBool(args, "includeAtmosphere", false);
                    int dupeLimit = Clamp(ToolUtil.GetInt(args, "dupeLimit") ?? DefaultDupeLimit(profile), 0, 100);
                    int foodLimit = Clamp(ToolUtil.GetInt(args, "foodLimit") ?? DefaultFoodLimit(profile), 0, 100);

                    var snapshot = new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["profile"] = profile,
                        ["worldId"] = worldId,
                        ["time"] = BuildTime(),
                        ["colony"] = BuildColony()
                    };

                    List<DupeSnapshot> dupes = includeDupes || includeAlerts ? BuildDupes(worldId) : new List<DupeSnapshot>();
                    FoodSnapshot food = includeFood || includeAlerts ? BuildFood(worldId, foodLimit) : null;
                    BuildingSnapshot buildings = includeBuildings || includeAlerts ? BuildBuildings(worldId) : null;
                    Dictionary<string, object> atmosphere = includeAtmosphere ? BuildAtmosphere(worldId) : null;

                    if (includeDupes)
                        snapshot["dupes"] = BuildDupeResult(dupes, dupeLimit, profile);
                    if (includeFood && food != null)
                        snapshot["food"] = food.ToDictionary();
                    if (includeBuildings && buildings != null)
                        snapshot["buildings"] = buildings.ToDictionary();
                    if (includeResearch)
                        snapshot["research"] = BuildResearch();
                    if (includeAtmosphere)
                        snapshot["atmosphere"] = atmosphere;
                    if (includeAlerts)
                        snapshot["alerts"] = BuildAlerts(dupes, food, buildings, atmosphere);

                    snapshot["cost"] = new Dictionary<string, object>
                    {
                        ["singleTool"] = true,
                        ["replaces"] = new[] { "game_time", "colony_status", "colony_diagnostics", "colony_alerts", "resources_food", "dupes_list", "research_status" },
                        ["fullGridScan"] = includeAtmosphere
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(snapshot, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> BuildTime()
        {
            float timeOfDay = GameClock.Instance?.GetCurrentCycleAsPercentage() ?? 0f;
            return new Dictionary<string, object>
            {
                ["cycle"] = GameUtil.GetCurrentCycle(),
                ["timeOfDayPercent"] = Math.Round(timeOfDay * 100f, 1),
                ["timeScale"] = Time.timeScale,
                ["isPaused"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.IsPaused : Time.timeScale == 0f,
                ["speed"] = SpeedControlScreen.Instance != null ? SpeedControlScreen.Instance.GetSpeed() + 1 : 0
            };
        }

        private static Dictionary<string, object> BuildColony()
        {
            var result = new Dictionary<string, object>
            {
                ["duplicantCount"] = Components.LiveMinionIdentities.Count,
                ["worldCount"] = ClusterManager.Instance?.worldCount ?? 0,
                ["activeWorldId"] = ClusterManager.Instance?.activeWorldId ?? -1
            };
            try
            {
                if (SaveLoader.Instance?.GameInfo != null)
                    result["saveName"] = SaveLoader.Instance.GameInfo.baseName;
            }
            catch { }
            return result;
        }

        private static List<DupeSnapshot> BuildDupes(int worldId)
        {
            var result = new List<DupeSnapshot>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null)
                    continue;
                int dupeWorld = dupe.GetMyWorldId();
                if (worldId >= 0 && dupeWorld != worldId)
                    continue;

                float stress = AmountValue(dupe, "Stress");
                float stamina = AmountValue(dupe, "Stamina");
                float calories = AmountValue(dupe, "Calories");
                var resume = dupe.GetComponent<MinionResume>();
                int cell = Grid.PosToCell(dupe);
                result.Add(new DupeSnapshot
                {
                    Id = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                    Name = ToolUtil.CleanName(dupe.GetProperName()),
                    WorldId = dupeWorld,
                    X = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                    Y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                    Stress = stress,
                    Stamina = stamina,
                    Calories = calories,
                    SkillPoints = resume?.AvailableSkillpoints ?? 0
                });
            }
            return result.OrderBy(item => item.Name).ToList();
        }

        private static float AmountValue(MinionIdentity dupe, string id)
        {
            var amounts = dupe?.GetComponent<Klei.AI.Amounts>();
            if (amounts == null)
                return 0f;
            foreach (var amount in amounts.ModifierList)
            {
                if (amount != null && amount.amount.Id == id)
                    return ToolUtil.SafeFloat(amount.value);
            }
            return 0f;
        }

        private static Dictionary<string, object> BuildDupeResult(List<DupeSnapshot> dupes, int limit, string profile)
        {
            var result = new Dictionary<string, object>
            {
                ["count"] = dupes.Count,
                ["maxStress"] = Math.Round(dupes.Count == 0 ? 0f : dupes.Max(item => item.Stress), 1),
                ["lowStamina"] = dupes.Count(item => item.Stamina > 0f && item.Stamina < 30f),
                ["skillPointsTotal"] = dupes.Sum(item => item.SkillPoints)
            };
            if (limit > 0)
            {
                result["items"] = dupes
                    .OrderByDescending(item => item.Stress)
                    .ThenBy(item => item.Name)
                    .Take(limit)
                    .Select(item => item.ToDictionary(profile == "full"))
                    .ToList();
            }
            return result;
        }

        private static FoodSnapshot BuildFood(int worldId, int limit)
        {
            var groups = new Dictionary<string, FoodAggregate>();
            float totalKcal = 0f;
            foreach (var edible in Components.Edibles.Items)
            {
                if (edible == null || edible.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(edible);
                int itemWorld = Grid.IsValidCell(cell) ? Grid.WorldIdx[cell] : edible.GetMyWorldId();
                if (worldId >= 0 && itemWorld != worldId)
                    continue;

                string id = edible.GetComponent<KPrefabID>()?.PrefabTag.Name ?? edible.name;
                FoodAggregate aggregate;
                if (!groups.TryGetValue(id, out aggregate))
                {
                    aggregate = new FoodAggregate
                    {
                        Id = id,
                        Name = ToolUtil.CleanName(edible.GetProperName()),
                        Quality = edible.GetQuality()
                    };
                    groups[id] = aggregate;
                }

                float kcal = ToolUtil.SafeFloat(edible.Calories) / 1000f;
                totalKcal += kcal;
                aggregate.Count++;
                aggregate.Kcal += kcal;
            }

            return new FoodSnapshot
            {
                TotalKcal = totalKcal,
                FoodTypes = groups.Count,
                Items = groups.Values
                    .OrderByDescending(item => item.Kcal)
                    .Take(Math.Max(0, limit))
                    .Select(item => item.ToDictionary())
                    .ToList()
            };
        }

        private static BuildingSnapshot BuildBuildings(int worldId)
        {
            var snapshot = new BuildingSnapshot();
            var seen = new HashSet<string>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null)
                    continue;
                int buildingWorld = building.GetMyWorldId();
                if (worldId >= 0 && buildingWorld != worldId)
                    continue;

                var def = building.Def;
                string prefabId = def?.PrefabID ?? building.name;
                var pos = building.transform.GetPosition();
                string key = prefabId + "|" + Math.Round(pos.x) + "|" + Math.Round(pos.y) + "|" + buildingWorld;
                if (!seen.Add(key))
                    continue;

                snapshot.Total++;
                if (Contains(prefabId, "Bed") || Contains(prefabId, "LuxuryBed")) snapshot.Beds++;
                if (Contains(prefabId, "Outhouse") || Contains(prefabId, "FlushToilet")) snapshot.Toilets++;
                if (Contains(prefabId, "WashBasin") || Contains(prefabId, "WashSink")) snapshot.WashStations++;
                if (Contains(prefabId, "ResearchCenter") || Contains(prefabId, "AdvancedResearchCenter")) snapshot.ResearchStations++;
                if (Contains(prefabId, "Battery")) snapshot.Batteries++;
                if (Contains(prefabId, "ManualGenerator") || Contains(prefabId, "Generator")) snapshot.Generators++;
                if (Contains(prefabId, "OxygenDiffuser") || Contains(prefabId, "Electrolyzer")) snapshot.OxygenProducers++;
            }
            return snapshot;
        }

        private static Dictionary<string, object> BuildResearch()
        {
            if (Research.Instance == null)
                return new Dictionary<string, object> { ["available"] = false };

            var active = Research.Instance.GetActiveResearch();
            var target = Research.Instance.GetTargetResearch();
            var queue = Research.Instance.GetResearchQueue();
            return new Dictionary<string, object>
            {
                ["available"] = true,
                ["active"] = active != null ? TechSummary(active.tech, includeProgress: true) : null,
                ["target"] = target != null ? TechSummary(target.tech, includeProgress: false) : null,
                ["queueCount"] = queue.Count,
                ["queue"] = queue.Take(5).Select(item => TechSummary(item.tech, includeProgress: false)).ToList()
            };
        }

        private static Dictionary<string, object> TechSummary(Tech tech, bool includeProgress)
        {
            if (tech == null)
                return null;
            var instance = Research.Instance?.Get(tech);
            var result = new Dictionary<string, object>
            {
                ["id"] = tech.Id,
                ["name"] = tech.Name,
                ["complete"] = instance?.IsComplete() ?? false
            };
            if (includeProgress && instance != null)
                result["progress"] = Math.Round(instance.GetTotalPercentageComplete() * 100.0, 1);
            return result;
        }

        private static Dictionary<string, object> BuildAtmosphere(int worldId)
        {
            float oxygenKg = 0f;
            float pollutedOxygenKg = 0f;
            int breathableCells = 0;
            int visibleCells = 0;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !Grid.IsVisible(cell))
                    continue;
                visibleCells++;
                var element = Grid.Element[cell];
                if (element == null)
                    continue;
                if (element.id == SimHashes.Oxygen)
                {
                    oxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
                else if (element.id == SimHashes.ContaminatedOxygen)
                {
                    pollutedOxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
            }

            return new Dictionary<string, object>
            {
                ["visibleCells"] = visibleCells,
                ["breathableCells"] = breathableCells,
                ["oxygenKg"] = Math.Round(oxygenKg, 1),
                ["pollutedOxygenKg"] = Math.Round(pollutedOxygenKg, 1)
            };
        }

        private static Dictionary<string, object> BuildAlerts(List<DupeSnapshot> dupes, FoodSnapshot food, BuildingSnapshot buildings, Dictionary<string, object> atmosphere)
        {
            int dupeCount = dupes.Count > 0 ? dupes.Count : Components.LiveMinionIdentities.Count;
            float foodKcal = food?.TotalKcal ?? 0f;
            var alerts = new List<Dictionary<string, object>>();

            AddAlert(alerts, food != null && foodKcal < dupeCount * 2000f, "critical", "food", $"Food low: {Math.Round(foodKcal, 1)} kcal for {dupeCount} dupes.");
            AddAlert(alerts, buildings != null && buildings.Beds < dupeCount, "warning", "sleep", $"Beds short: {buildings.Beds}/{dupeCount}.");
            AddAlert(alerts, buildings != null && buildings.Toilets == 0, "warning", "hygiene", "No toilet detected.");
            AddAlert(alerts, buildings != null && buildings.ResearchStations == 0, "info", "research", "No research station detected.");
            AddAlert(alerts, buildings != null && buildings.Batteries == 0, "info", "power", "No battery detected.");
            AddAlert(alerts, dupes.Count > 0 && dupes.Max(item => item.Stress) > 40f, "warning", "stress", $"Max stress {Math.Round(dupes.Max(item => item.Stress), 1)}.");

            if (atmosphere != null)
            {
                int breathableCells = Convert.ToInt32(atmosphere["breathableCells"]);
                AddAlert(alerts, breathableCells < dupeCount * 20, "warning", "oxygen", $"Visible breathable cells low: {breathableCells}.");
            }

            return new Dictionary<string, object>
            {
                ["count"] = alerts.Count,
                ["items"] = alerts
            };
        }

        private static void AddAlert(List<Dictionary<string, object>> alerts, bool condition, string severity, string category, string message)
        {
            if (!condition)
                return;
            alerts.Add(new Dictionary<string, object>
            {
                ["severity"] = severity,
                ["category"] = category,
                ["message"] = message
            });
        }

        private static string NormalizeProfile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "standard";
            string profile = value.Trim().ToLowerInvariant();
            if (profile == "brief" || profile == "standard" || profile == "full")
                return profile;
            return "standard";
        }

        private static int DefaultDupeLimit(string profile)
        {
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 12;
        }

        private static int DefaultFoodLimit(string profile)
        {
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 8;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class DupeSnapshot
        {
            public int Id;
            public string Name;
            public int WorldId;
            public int X;
            public int Y;
            public float Stress;
            public float Stamina;
            public float Calories;
            public int SkillPoints;

            public Dictionary<string, object> ToDictionary(bool full)
            {
                var result = new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["worldId"] = WorldId,
                    ["xy"] = new[] { X, Y },
                    ["stress"] = Math.Round(Stress, 1),
                    ["skillPoints"] = SkillPoints
                };
                if (full)
                {
                    result["stamina"] = Math.Round(Stamina, 1);
                    result["calories"] = Math.Round(Calories, 1);
                }
                return result;
            }
        }

        private sealed class FoodSnapshot
        {
            public float TotalKcal;
            public int FoodTypes;
            public List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["totalKcal"] = Math.Round(TotalKcal, 1),
                    ["foodTypes"] = FoodTypes,
                    ["items"] = Items
                };
            }
        }

        private sealed class FoodAggregate
        {
            public string Id;
            public string Name;
            public int Quality;
            public int Count;
            public float Kcal;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["quality"] = Quality,
                    ["count"] = Count,
                    ["kcal"] = Math.Round(Kcal, 1)
                };
            }
        }

        private sealed class BuildingSnapshot
        {
            public int Total;
            public int Beds;
            public int Toilets;
            public int WashStations;
            public int ResearchStations;
            public int Batteries;
            public int Generators;
            public int OxygenProducers;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["total"] = Total,
                    ["beds"] = Beds,
                    ["toilets"] = Toilets,
                    ["washStations"] = WashStations,
                    ["researchStations"] = ResearchStations,
                    ["batteries"] = Batteries,
                    ["generators"] = Generators,
                    ["oxygenProducers"] = OxygenProducers
                };
            }
        }
    }
}
