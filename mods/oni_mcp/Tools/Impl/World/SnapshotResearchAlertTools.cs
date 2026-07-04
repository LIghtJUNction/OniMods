using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
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
    }
}
