using System;
using System.Collections.Generic;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
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
    }
}
