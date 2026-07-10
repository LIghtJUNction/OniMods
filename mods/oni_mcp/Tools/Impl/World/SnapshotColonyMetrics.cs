using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
        private static FoodSnapshot BuildFood(int worldId, int limit, bool visibleOnly)
        {
            var groups = new Dictionary<string, FoodAggregate>();
            float totalKcal = 0f;
            foreach (var edible in Components.Edibles.Items)
            {
                if (edible == null || edible.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(edible);
                if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                    continue;
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
                if (Contains(prefabId, "OxygenDiffuser") || Contains(prefabId, "MineralDeoxidizer") || Contains(prefabId, "Electrolyzer")) snapshot.OxygenProducers++;
            }
            return snapshot;
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
    }
}
