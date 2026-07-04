using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
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
    }
}
