using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        private class InventoryAggregate
        {
            public string Name;
            public string PrefabId;
            public string ElementId;
            public int Count;
            public int StoredCount;
            public int LooseCount;
            public float TotalMassKg;
            public float TotalUnits;
            public float TotalCaloriesKcal;
            public int SampleCell = -1;
            public int SampleX;
            public int SampleY;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                var result = new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["elementId"] = ElementId,
                    ["count"] = Count,
                    ["storedCount"] = StoredCount,
                    ["looseCount"] = LooseCount,
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["totalUnits"] = Math.Round(TotalUnits, 3),
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };

                if (TotalCaloriesKcal > 0f)
                    result["totalCaloriesKcal"] = Math.Round(TotalCaloriesKcal, 1);

                if (SampleCell >= 0)
                    result["samplePosition"] = new { x = SampleX, y = SampleY };

                return result;
            }
        }

        private class FoodAggregate
        {
            public string Name;
            public string PrefabId;
            public int Quality;
            public int Morale;
            public int Count;
            public int StoredCount;
            public float TotalCaloriesKcal;
            public float TotalMassKg;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["quality"] = Quality,
                    ["morale"] = Morale,
                    ["count"] = Count,
                    ["storedCount"] = StoredCount,
                    ["totalCaloriesKcal"] = Math.Round(TotalCaloriesKcal, 1),
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };
            }
        }
    }
}
