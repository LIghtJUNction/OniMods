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
    }
}
