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
        private static readonly SnapshotDeltaCache DeltaCache = new SnapshotDeltaCache();

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
                    ["profile"] = new McpToolParameter { Type = "string", Description = "minimal/brief/standard/full；minimal 只返回循环、暂停、告警级别、核心指标和一句摘要", Required = false, EnumValues = new List<string> { "minimal", "brief", "standard", "full" } },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；传 -1 汇总全部可轻量统计项", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否包含复制人摘要，默认 true", Required = false },
                    ["includeFood"] = new McpToolParameter { Type = "boolean", Description = "是否包含食物摘要，默认 true", Required = false },
                    ["includeResearch"] = new McpToolParameter { Type = "boolean", Description = "是否包含研究状态，默认 true", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否包含关键建筑计数，默认 true", Required = false },
                    ["includeAlerts"] = new McpToolParameter { Type = "boolean", Description = "是否包含快照告警，默认 true", Required = false },
                    ["includeAtmosphere"] = new McpToolParameter { Type = "boolean", Description = "是否扫描可见大气格子；这是全图扫描，默认 false", Required = false },
                    ["dupeLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人明细数量，默认 brief=0 standard=12 full=50", Required = false },
                    ["foodLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回食物类型数量，默认 brief=0 standard=8 full=50", Required = false },
                    ["delta"] = new McpToolParameter { Type = "boolean", Description = "只返回相对同 session/同 deltaKey 上次调用的变化；首次或 resetDelta=true 返回 baseline", Required = false },
                    ["deltaKey"] = new McpToolParameter { Type = "string", Description = "可选 delta 缓存槽；同一 agent 可为不同观察循环保留不同 baseline", Required = false },
                    ["resetDelta"] = new McpToolParameter { Type = "boolean", Description = "清除本次 delta baseline 并把当前结果作为新 baseline", Required = false },
                    ["watch"] = new McpToolParameter { Type = "array", Description = "关注指标数组或逗号字符串，如 stress,food_kcal,red_alert,alerts,oxygen；提供后返回 watch 块", Required = false },
                    ["watchOnly"] = new McpToolParameter { Type = "boolean", Description = "只返回 watch 指标、告警级别和摘要，适合循环轮询", Required = false },
                    ["thresholds"] = new McpToolParameter { Type = "object", Description = "关注阈值，例如 {\"stress\":\">60\",\"food_kcal\":\"<20000\"}；也兼容 threshold", Required = false }
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
                    bool minimal = profile == "minimal";
                    var watchKeys = ParseWatch(args["watch"]);
                    bool watchOnly = ToolUtil.GetBool(args, "watchOnly", false);
                    bool needAtmosphere = includeAtmosphere || WatchNeedsAtmosphere(watchKeys);

                    List<DupeSnapshot> dupes = (includeDupes || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildDupes(worldId) : new List<DupeSnapshot>();
                    FoodSnapshot food = (includeFood || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildFood(worldId, foodLimit) : null;
                    BuildingSnapshot buildings = (includeBuildings || includeAlerts || minimal || watchOnly || watchKeys.Count > 0) ? BuildBuildings(worldId) : null;
                    Dictionary<string, object> atmosphere = needAtmosphere ? BuildAtmosphere(worldId) : null;
                    var redAlert = BuildRedAlert(worldId);
                    var alerts = includeAlerts || minimal || watchOnly || watchKeys.Count > 0
                        ? BuildAlerts(dupes, food, buildings, atmosphere, redAlert)
                        : new Dictionary<string, object> { ["count"] = 0, ["items"] = new List<Dictionary<string, object>>() };
                    var metrics = BuildMetrics(dupes, food, buildings, atmosphere, redAlert, alerts);
                    var time = BuildTime();
                    var colony = BuildColony();

                    Dictionary<string, object> snapshot = minimal
                        ? BuildMinimalSnapshot(profile, worldId, time, colony, metrics)
                        : new Dictionary<string, object>
                        {
                            ["v"] = 2,
                            ["profile"] = profile,
                            ["worldId"] = worldId,
                            ["time"] = time,
                            ["colony"] = colony
                        };

                    if (!minimal && includeDupes)
                        snapshot["dupes"] = BuildDupeResult(dupes, dupeLimit, profile);
                    if (!minimal && includeFood && food != null)
                        snapshot["food"] = food.ToDictionary();
                    if (!minimal && includeBuildings && buildings != null)
                        snapshot["buildings"] = buildings.ToDictionary();
                    if (!minimal && includeResearch)
                        snapshot["research"] = BuildResearch();
                    if (!minimal && needAtmosphere)
                        snapshot["atmosphere"] = atmosphere;
                    if (!minimal && includeAlerts)
                        snapshot["alerts"] = alerts;

                    if (watchKeys.Count > 0)
                        snapshot["watch"] = BuildWatch(watchKeys, metrics, args["thresholds"] as JObject ?? args["threshold"] as JObject);
                    if (watchOnly)
                        snapshot = BuildWatchOnlySnapshot(profile, worldId, time, metrics, snapshot.ContainsKey("watch") ? snapshot["watch"] : BuildWatch(DefaultWatchKeys(), metrics, args["thresholds"] as JObject ?? args["threshold"] as JObject));

                    snapshot["cost"] = new Dictionary<string, object>
                    {
                        ["singleTool"] = true,
                        ["deltaAvailable"] = true,
                        ["watchOnly"] = watchOnly,
                        ["replaces"] = new[] { "game_time", "colony_status", "colony_diagnostics", "colony_alerts", "resources_food", "dupes_list", "research_status" },
                        ["fullGridScan"] = needAtmosphere
                    };
                    if (needAtmosphere)
                        ((Dictionary<string, object>)snapshot["cost"])["atmosphereNote"] = "Atmosphere is a full visible-grid aggregate and can mask local pockets; use world_area_snapshot on suspected rooms for local oxygen/temperature.";

                    if (ToolUtil.GetBool(args, "delta", false))
                    {
                        string deltaKey = BuildDeltaKey(args, profile, worldId, watchKeys, watchOnly);
                        var delta = DeltaCache.Apply(ToolSessionContext.SessionId, deltaKey, snapshot, ToolUtil.GetBool(args, "resetDelta", false));
                        return CallToolResult.Text(JsonConvert.SerializeObject(delta, McpJsonUtil.Settings));
                    }

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

                float stress = DupeAmountUtil.StressValue(dupe);
                float stamina = DupeAmountUtil.AmountValueByName(dupe, "Stamina");
                float calories = DupeAmountUtil.AmountValueByName(dupe, "Calories");
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
                aggregate.AddLocation(edible.gameObject, cell, kcal);
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

        private static Dictionary<string, object> BuildMinimalSnapshot(string profile, int worldId, Dictionary<string, object> time, Dictionary<string, object> colony, Dictionary<string, object> metrics)
        {
            return new Dictionary<string, object>
            {
                ["v"] = 2,
                ["profile"] = profile,
                ["worldId"] = worldId,
                ["cycle"] = time["cycle"],
                ["paused"] = time["isPaused"],
                ["speed"] = time["speed"],
                ["alertLevel"] = metrics["alertLevel"],
                ["summary"] = BuildSummary(metrics),
                ["metrics"] = new Dictionary<string, object>
                {
                    ["dupes"] = metrics["dupes"],
                    ["maxStress"] = metrics["stress"],
                    ["foodKcal"] = metrics["food_kcal"],
                    ["redAlert"] = metrics["red_alert"],
                    ["alerts"] = metrics["alerts"]
                },
                ["colony"] = new Dictionary<string, object>
                {
                    ["activeWorldId"] = colony["activeWorldId"],
                    ["worldCount"] = colony["worldCount"]
                }
            };
        }

        private static Dictionary<string, object> BuildWatchOnlySnapshot(string profile, int worldId, Dictionary<string, object> time, Dictionary<string, object> metrics, object watch)
        {
            return new Dictionary<string, object>
            {
                ["v"] = 2,
                ["profile"] = profile,
                ["worldId"] = worldId,
                ["cycle"] = time["cycle"],
                ["paused"] = time["isPaused"],
                ["alertLevel"] = metrics["alertLevel"],
                ["summary"] = BuildSummary(metrics),
                ["watch"] = watch
            };
        }

        private static Dictionary<string, object> BuildMetrics(List<DupeSnapshot> dupes, FoodSnapshot food, BuildingSnapshot buildings, Dictionary<string, object> atmosphere, Dictionary<string, object> redAlert, Dictionary<string, object> alerts)
        {
            int alertCount = Convert.ToInt32(alerts["count"]);
            bool red = redAlert.ContainsKey("isRedAlert") && Convert.ToBoolean(redAlert["isRedAlert"]);
            bool yellow = redAlert.ContainsKey("isYellowAlert") && Convert.ToBoolean(redAlert["isYellowAlert"]);
            string alertLevel = red || HasSeverity(alerts, "critical") ? "red" : (yellow || HasSeverity(alerts, "warning") ? "yellow" : (alertCount > 0 ? "info" : "green"));
            float maxStress = dupes.Count == 0 ? 0f : dupes.Max(item => item.Stress);

            var result = new Dictionary<string, object>
            {
                ["dupes"] = dupes.Count > 0 ? dupes.Count : Components.LiveMinionIdentities.Count,
                ["stress"] = Math.Round(maxStress, 1),
                ["stressed_dupes"] = dupes.Count(item => item.Stress >= 40f),
                ["low_stamina"] = dupes.Count(item => item.Stamina > 0f && item.Stamina < 30f),
                ["skill_points"] = dupes.Sum(item => item.SkillPoints),
                ["food_kcal"] = Math.Round(food?.TotalKcal ?? 0f, 1),
                ["food_types"] = food?.FoodTypes ?? 0,
                ["red_alert"] = red,
                ["yellow_alert"] = yellow,
                ["alerts"] = alertCount,
                ["alertLevel"] = alertLevel
            };

            if (buildings != null)
            {
                result["beds"] = buildings.Beds;
                result["toilets"] = buildings.Toilets;
                result["research_stations"] = buildings.ResearchStations;
                result["batteries"] = buildings.Batteries;
                result["oxygen_producers"] = buildings.OxygenProducers;
            }
            if (atmosphere != null)
            {
                result["oxygen_kg"] = atmosphere["oxygenKg"];
                result["polluted_oxygen_kg"] = atmosphere["pollutedOxygenKg"];
                result["breathable_cells"] = atmosphere["breathableCells"];
            }
            return result;
        }

        private static Dictionary<string, object> BuildRedAlert(int worldId)
        {
            var result = new Dictionary<string, object>
            {
                ["available"] = false,
                ["redAlertToggledOn"] = false,
                ["isRedAlert"] = false,
                ["isYellowAlert"] = false,
                ["isOn"] = false
            };
            if (ClusterManager.Instance == null)
                return result;
            var world = ClusterManager.Instance.GetWorld(worldId >= 0 ? worldId : ClusterManager.Instance.activeWorldId);
            var alert = world?.AlertManager;
            if (alert == null)
                return result;
            result["available"] = true;
            result["redAlertToggledOn"] = alert.IsRedAlertToggledOn();
            result["isRedAlert"] = alert.IsRedAlert();
            result["isYellowAlert"] = alert.IsYellowAlert();
            result["isOn"] = alert.IsOn();
            return result;
        }

        private static bool HasSeverity(Dictionary<string, object> alerts, string severity)
        {
            var items = alerts["items"] as List<Dictionary<string, object>>;
            return items != null && items.Any(item => string.Equals(item["severity"]?.ToString(), severity, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildSummary(Dictionary<string, object> metrics)
        {
            return $"{metrics["dupes"]} dupes, max stress {metrics["stress"]}%, food {metrics["food_kcal"]} kcal, alert {metrics["alertLevel"]}";
        }

        private static List<string> ParseWatch(JToken token)
        {
            var result = new List<string>();
            var array = token as JArray;
            if (array != null)
            {
                foreach (var item in array)
                    AddWatchKey(result, item?.ToString());
                return result;
            }
            AddWatchKey(result, token?.ToString());
            return result;
        }

        private static List<string> DefaultWatchKeys()
        {
            return new List<string> { "stress", "food_kcal", "red_alert", "alerts" };
        }

        private static void AddWatchKey(List<string> result, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            foreach (string raw in value.Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = NormalizeMetricKey(raw);
                if (key == "all")
                {
                    foreach (string defaultKey in DefaultWatchKeys())
                        if (!result.Contains(defaultKey))
                            result.Add(defaultKey);
                    continue;
                }
                if (!result.Contains(key))
                    result.Add(key);
            }
        }

        private static Dictionary<string, object> BuildWatch(List<string> watchKeys, Dictionary<string, object> metrics, JObject thresholds)
        {
            var values = new Dictionary<string, object>();
            var triggered = new List<Dictionary<string, object>>();
            foreach (string raw in watchKeys)
            {
                string key = NormalizeMetricKey(raw);
                object value;
                if (!metrics.TryGetValue(key, out value))
                    continue;
                values[key] = value;
                string threshold = thresholds?[raw]?.ToString() ?? thresholds?[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(threshold) && ThresholdTriggered(value, threshold))
                {
                    triggered.Add(new Dictionary<string, object>
                    {
                        ["metric"] = key,
                        ["value"] = value,
                        ["threshold"] = threshold
                    });
                }
            }
            return new Dictionary<string, object>
            {
                ["values"] = values,
                ["triggered"] = triggered,
                ["alert"] = triggered.Count > 0
            };
        }

        private static bool WatchNeedsAtmosphere(List<string> watchKeys)
        {
            return watchKeys.Any(key =>
            {
                string metric = NormalizeMetricKey(key);
                return metric == "oxygen_kg" || metric == "polluted_oxygen_kg" || metric == "breathable_cells";
            });
        }

        private static string NormalizeMetricKey(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant().Replace("-", "_");
            switch (key)
            {
                case "food":
                case "food_kcalories":
                case "kcal":
                    return "food_kcal";
                case "max_stress":
                    return "stress";
                case "red":
                case "redalert":
                    return "red_alert";
                case "alert":
                case "alert_count":
                    return "alerts";
                case "oxygen":
                    return "oxygen_kg";
                default:
                    return key;
            }
        }

        private static bool ThresholdTriggered(object value, string expression)
        {
            double number;
            if (!TryNumeric(value, out number))
                return false;
            string text = expression.Trim().Replace("%", "");
            string op = null;
            if (text.StartsWith(">=", StringComparison.Ordinal)) op = ">=";
            else if (text.StartsWith("<=", StringComparison.Ordinal)) op = "<=";
            else if (text.StartsWith(">", StringComparison.Ordinal)) op = ">";
            else if (text.StartsWith("<", StringComparison.Ordinal)) op = "<";
            else if (text.StartsWith("=", StringComparison.Ordinal)) op = "=";
            string rhs = op == null ? text : text.Substring(op.Length).Trim();
            double threshold;
            if (!double.TryParse(rhs, out threshold))
                return false;
            switch (op ?? ">=")
            {
                case ">": return number > threshold;
                case "<": return number < threshold;
                case "<=": return number <= threshold;
                case "=": return Math.Abs(number - threshold) < 0.0001;
                default: return number >= threshold;
            }
        }

        private static bool TryNumeric(object value, out double number)
        {
            number = 0;
            if (value == null)
                return false;
            if (value is bool)
            {
                number = (bool)value ? 1 : 0;
                return true;
            }
            return double.TryParse(value.ToString(), out number);
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

        private static Dictionary<string, object> BuildAlerts(List<DupeSnapshot> dupes, FoodSnapshot food, BuildingSnapshot buildings, Dictionary<string, object> atmosphere, Dictionary<string, object> redAlert)
        {
            int dupeCount = dupes.Count > 0 ? dupes.Count : Components.LiveMinionIdentities.Count;
            float foodKcal = food?.TotalKcal ?? 0f;
            var alerts = new List<Dictionary<string, object>>();

            AddAlert(alerts, redAlert != null && redAlert.ContainsKey("isRedAlert") && Convert.ToBoolean(redAlert["isRedAlert"]), "critical", "red_alert", "Red alert is active.");
            AddAlert(alerts, redAlert != null && redAlert.ContainsKey("isYellowAlert") && Convert.ToBoolean(redAlert["isYellowAlert"]), "warning", "yellow_alert", "Yellow alert is active.");
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
            if (profile == "minimal" || profile == "brief" || profile == "standard" || profile == "full")
                return profile;
            if (profile == "mini" || profile == "tiny")
                return "minimal";
            return "standard";
        }

        private static int DefaultDupeLimit(string profile)
        {
            if (profile == "minimal") return 0;
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 12;
        }

        private static int DefaultFoodLimit(string profile)
        {
            if (profile == "minimal") return 0;
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 8;
        }

        private static string BuildDeltaKey(JObject args, string profile, int worldId, List<string> watchKeys, bool watchOnly)
        {
            string explicitKey = args["deltaKey"]?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitKey))
                return explicitKey.Trim();
            string watch = watchKeys.Count == 0 ? "" : string.Join(",", watchKeys.OrderBy(item => item).ToArray());
            return string.Join("|", new[]
            {
                "colony_state_snapshot",
                profile,
                "w=" + worldId,
                "watchOnly=" + watchOnly,
                "watch=" + watch,
                "dupes=" + ToolUtil.GetBool(args, "includeDupes", true),
                "food=" + ToolUtil.GetBool(args, "includeFood", true),
                "research=" + ToolUtil.GetBool(args, "includeResearch", true),
                "buildings=" + ToolUtil.GetBool(args, "includeBuildings", true),
                "alerts=" + ToolUtil.GetBool(args, "includeAlerts", true),
                "atmo=" + ToolUtil.GetBool(args, "includeAtmosphere", false)
            });
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
            public List<Dictionary<string, object>> Locations = new List<Dictionary<string, object>>();

            public void AddLocation(GameObject go, int cell, float kcal)
            {
                if (Locations.Count >= 8)
                    return;
                int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
                int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
                Locations.Add(new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["cell"] = cell,
                    ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? (object)Grid.WorldIdx[cell] : null,
                    ["kcal"] = Math.Round(kcal, 1),
                    ["name"] = go != null ? ToolUtil.CleanName(go.GetProperName()) : null
                });
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["quality"] = Quality,
                    ["count"] = Count,
                    ["kcal"] = Math.Round(Kcal, 1),
                    ["locationSamples"] = Locations,
                    ["truncatedLocations"] = Math.Max(0, Count - Locations.Count)
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

        private sealed class SnapshotDeltaCache
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, JToken> snapshots = new Dictionary<string, JToken>(StringComparer.Ordinal);
            private readonly JsonSerializer serializer = JsonSerializer.Create(McpJsonUtil.Settings);

            public Dictionary<string, object> Apply(string sessionId, string deltaKey, Dictionary<string, object> current, bool reset)
            {
                string key = (string.IsNullOrWhiteSpace(sessionId) ? "global" : sessionId.Trim()) + ":" + deltaKey;
                var token = JToken.FromObject(current, serializer);
                object cycle = CurrentCycle(current);
                lock (sync)
                {
                    JToken previous;
                    if (reset || !snapshots.TryGetValue(key, out previous))
                    {
                        snapshots[key] = token.DeepClone();
                        return new Dictionary<string, object>
                        {
                            ["v"] = 1,
                            ["delta"] = true,
                            ["baseline"] = true,
                            ["unchanged"] = false,
                            ["cycle"] = cycle,
                            ["snapshot"] = current
                        };
                    }

                    JToken diff = Diff(previous, token);
                    snapshots[key] = token.DeepClone();
                    if (diff == null)
                    {
                        return new Dictionary<string, object>
                        {
                            ["v"] = 1,
                            ["delta"] = true,
                            ["unchanged"] = true,
                            ["cycle"] = cycle
                        };
                    }
                    return new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["delta"] = true,
                        ["unchanged"] = false,
                        ["cycle"] = cycle,
                        ["changed"] = diff
                    };
                }
            }

            private static object CurrentCycle(Dictionary<string, object> current)
            {
                if (current.ContainsKey("cycle"))
                    return current["cycle"];
                var time = current.ContainsKey("time") ? current["time"] as Dictionary<string, object> : null;
                return time != null && time.ContainsKey("cycle") ? time["cycle"] : null;
            }

            private static JToken Diff(JToken previous, JToken current)
            {
                if (JToken.DeepEquals(previous, current))
                    return null;
                var prevObj = previous as JObject;
                var curObj = current as JObject;
                if (prevObj == null || curObj == null)
                    return current.DeepClone();

                var result = new JObject();
                foreach (var property in curObj.Properties())
                {
                    JToken previousChild = prevObj[property.Name];
                    JToken childDiff = previousChild == null ? property.Value.DeepClone() : Diff(previousChild, property.Value);
                    if (childDiff != null)
                        result[property.Name] = childDiff;
                }
                return result.HasValues ? result : null;
            }
        }
    }
}
