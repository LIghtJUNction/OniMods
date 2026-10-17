using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        internal static void RunCompactInventoryRegression()
        {
            var item = new InventoryAggregate
            {
                Name = "Copper Ore",
                PrefabId = "CopperOre",
                ElementId = "CopperOre",
                Count = 4,
                StoredCount = 3,
                LooseCount = 1,
                TotalMassKg = 10.25f,
                TotalUnits = 10.25f,
                TotalCaloriesKcal = 0f,
                WorldIds = new HashSet<int> { 0 },
                SampleCell = 10,
                SampleX = 3,
                SampleY = 7
            };
            var compact = item.ToCompactDictionary();
            var full = item.ToDictionary();
            RequireCompact((string)compact["name"] == "Copper Ore" && (string)compact["prefabId"] == "CopperOre", "compact identity changed");
            RequireCompact((int)compact["count"] == 4 && (int)compact["storedCount"] == 3 && (int)compact["looseCount"] == 1, "compact counts changed");
            RequireCompact(!compact.ContainsKey("elementId") && !compact.ContainsKey("totalUnits") && !compact.ContainsKey("totalCaloriesKcal")
                && !compact.ContainsKey("samplePosition") && !compact.ContainsKey("worldIds"), "compact omitted-field contract changed");
            RequireCompact(full.ContainsKey("elementId") && full.ContainsKey("totalUnits") && full.ContainsKey("samplePosition")
                && full.ContainsKey("worldIds"), "full inventory compatibility changed");
            item.ElementId = "Copper";
            item.TotalUnits = 7.5f;
            item.TotalCaloriesKcal = 25f;
            item.WorldIds = new HashSet<int> { 6, 2 };
            compact = item.ToCompactDictionary();
            RequireCompact((string)compact["elementId"] == "Copper", "distinct element ID lost");
            RequireCompact((double)compact["totalUnits"] == 7.5, "distinct units lost");
            RequireCompact((double)compact["totalCaloriesKcal"] == 25, "calories lost");
            RequireCompact(((IEnumerable<int>)compact["worldIds"]).SequenceEqual(new[] { 2, 6 }), "world IDs lost or unsorted");

            var food = new FoodAggregate
            {
                Name = "Meal Lice",
                PrefabId = "MealLice",
                Quality = 1,
                Morale = 0,
                Count = 2,
                StoredCount = 1,
                TotalCaloriesKcal = 1200f,
                TotalMassKg = 2f,
                WorldIds = new HashSet<int> { 0 }
            };
            RequireCompact(food.ToDictionary().ContainsKey("quality"), "food aggregate changed");
        }
        private static void RequireCompact(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
