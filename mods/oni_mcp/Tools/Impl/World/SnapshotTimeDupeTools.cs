using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
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
    }
}
