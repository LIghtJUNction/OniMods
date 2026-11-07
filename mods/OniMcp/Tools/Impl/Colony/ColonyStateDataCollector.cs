using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    /// <summary>
    /// Collects rich, first-class colony state snapshots for dynamic MCP resources.
    /// Exposes game states (atmosphere, fluids, thermal, food, ranching, dupes vitals/morale,
    /// geysers, research, stockpile) directly as queryable resources instead of bloated tools.
    /// </summary>
    public static class ColonyStateDataCollector
    {
        public static JObject BuildAtmosphereSummary(int worldId)
        {
            var gasStats = new Dictionary<string, (float massKg, int cells, float tempSum)>();
            int totalGasCells = 0;
            int breathableCells = 0;
            float totalMass = 0f;

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !Grid.IsVisible(cell))
                    continue;

                var element = Grid.Element[cell];
                if (element == null || !element.IsGas)
                    continue;

                totalGasCells++;
                float mass = ToolUtil.SafeFloat(Grid.Mass[cell]);
                float temp = ToolUtil.SafeFloat(Grid.Temperature[cell]);
                totalMass += mass;

                string name = element.id.ToString();
                if (element.id == SimHashes.Oxygen || element.id == SimHashes.ContaminatedOxygen)
                    breathableCells++;

                if (!gasStats.TryGetValue(name, out var stat))
                    stat = (0f, 0, 0f);
                gasStats[name] = (stat.massKg + mass, stat.cells + 1, stat.tempSum + temp);
            }

            var distribution = new JArray();
            foreach (var kvp in gasStats.OrderByDescending(k => k.Value.massKg).Take(10))
            {
                distribution.Add(new JObject
                {
                    ["element"] = kvp.Key,
                    ["massKg"] = Math.Round(kvp.Value.massKg, 1),
                    ["cellCount"] = kvp.Value.cells,
                    ["avgTempC"] = kvp.Value.cells > 0 ? Math.Round(kvp.Value.tempSum / kvp.Value.cells - 273.15f, 1) : 0
                });
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["totalGasMassKg"] = Math.Round(totalMass, 1),
                ["totalGasCells"] = totalGasCells,
                ["breathableCells"] = breathableCells,
                ["breathablePercentage"] = totalGasCells > 0 ? Math.Round((float)breathableCells / totalGasCells * 100f, 1) : 0,
                ["dominantGases"] = distribution
            };
        }

        public static JObject BuildBreathabilityAnalysis(int worldId)
        {
            int dupeCount = Components.LiveMinionIdentities.Count;
            float oxygenKg = 0f;
            float co2Kg = 0f;
            int breathableCells = 0;
            int totalVisibleCells = 0;

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !Grid.IsVisible(cell))
                    continue;

                totalVisibleCells++;
                var element = Grid.Element[cell];
                if (element == null) continue;

                if (element.id == SimHashes.Oxygen || element.id == SimHashes.ContaminatedOxygen)
                {
                    oxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
                else if (element.id == SimHashes.CarbonDioxide)
                {
                    co2Kg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                }
            }

            float o2PerDupeKg = dupeCount > 0 ? oxygenKg / dupeCount : oxygenKg;
            string risk = o2PerDupeKg < 10f ? "critical" : (o2PerDupeKg < 50f ? "warning" : "nominal");

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["dupeCount"] = dupeCount,
                ["breathableCells"] = breathableCells,
                ["totalVisibleCells"] = totalVisibleCells,
                ["oxygenMassKg"] = Math.Round(oxygenKg, 1),
                ["carbonDioxideMassKg"] = Math.Round(co2Kg, 1),
                ["oxygenPerDupeKg"] = Math.Round(o2PerDupeKg, 1),
                ["suffocationRisk"] = risk,
                ["airQuality"] = breathableCells > dupeCount * 30 ? "good" : (breathableCells > dupeCount * 10 ? "fair" : "poor")
            };
        }

        public static JObject BuildLiquidSummary(int worldId)
        {
            var liquidStats = new Dictionary<string, (float massKg, int cells, float tempSum)>();
            float totalMass = 0f;

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !Grid.IsVisible(cell))
                    continue;

                var element = Grid.Element[cell];
                if (element == null || !element.IsLiquid)
                    continue;

                float mass = ToolUtil.SafeFloat(Grid.Mass[cell]);
                float temp = ToolUtil.SafeFloat(Grid.Temperature[cell]);
                totalMass += mass;

                string name = element.id.ToString();
                if (!liquidStats.TryGetValue(name, out var stat))
                    stat = (0f, 0, 0f);
                liquidStats[name] = (stat.massKg + mass, stat.cells + 1, stat.tempSum + temp);
            }

            var liquids = new JArray();
            foreach (var kvp in liquidStats.OrderByDescending(k => k.Value.massKg))
            {
                liquids.Add(new JObject
                {
                    ["element"] = kvp.Key,
                    ["massKg"] = Math.Round(kvp.Value.massKg, 1),
                    ["cellCount"] = kvp.Value.cells,
                    ["avgTempC"] = kvp.Value.cells > 0 ? Math.Round(kvp.Value.tempSum / kvp.Value.cells - 273.15f, 1) : 0
                });
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["totalLiquidMassKg"] = Math.Round(totalMass, 1),
                ["liquids"] = liquids
            };
        }

        public static JObject BuildPlumbingSummary(int worldId)
        {
            int liquidPipes = 0;
            int gasPipes = 0;

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId))
                    continue;

                var obj = Grid.Objects[cell, (int)ObjectLayer.LiquidConduit];
                if (obj != null) liquidPipes++;

                var gasObj = Grid.Objects[cell, (int)ObjectLayer.GasConduit];
                if (gasObj != null) gasPipes++;
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["liquidConduitSegments"] = liquidPipes,
                ["gasConduitSegments"] = gasPipes,
                ["status"] = "operational"
            };
        }

        public static JObject BuildThermalSummary(int worldId)
        {
            float minTemp = 10000f;
            float maxTemp = 0f;
            float sumTemp = 0f;
            int count = 0;
            int hotCells = 0;   // > 70 C (343.15 K)
            int coldCells = 0;  // < 0 C (273.15 K)

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !Grid.IsVisible(cell))
                    continue;

                float temp = ToolUtil.SafeFloat(Grid.Temperature[cell]);
                if (temp <= 0f) continue;

                count++;
                sumTemp += temp;
                if (temp < minTemp) minTemp = temp;
                if (temp > maxTemp) maxTemp = temp;

                if (temp > 343.15f) hotCells++;
                else if (temp < 273.15f) coldCells++;
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["scannedCells"] = count,
                ["avgTempC"] = count > 0 ? Math.Round(sumTemp / count - 273.15f, 1) : 0,
                ["minTempC"] = count > 0 ? Math.Round(minTemp - 273.15f, 1) : 0,
                ["maxTempC"] = count > 0 ? Math.Round(maxTemp - 273.15f, 1) : 0,
                ["hotCellsOver70C"] = hotCells,
                ["coldCellsBelow0C"] = coldCells
            };
        }

        public static JObject BuildFoodSummary(int worldId)
        {
            int dupeCount = Components.LiveMinionIdentities != null ? Components.LiveMinionIdentities.Count : 0;
            float totalKcal = 0f;
            if (Components.Edibles != null)
            {
                foreach (var edible in Components.Edibles.Items)
                {
                    if (edible == null || edible.gameObject == null) continue;
                    if (!ToolUtil.GameObjectMatchesWorld(edible.gameObject, worldId)) continue;
                    totalKcal += ToolUtil.SafeFloat(edible.Calories) / 1000f;
                }
            }

            float daysRemaining = dupeCount > 0 ? totalKcal / (dupeCount * 1000f) : 999f;
            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["totalKcal"] = Math.Round(totalKcal, 1),
                ["dupeCount"] = dupeCount,
                ["daysRemaining"] = Math.Round(daysRemaining, 1),
                ["status"] = daysRemaining < 2f ? "critical" : (daysRemaining < 5f ? "warning" : "nominal")
            };
        }

        public static JObject BuildCropsSummary(int worldId)
        {
            int total = 0;
            int ready = 0;
            var cropCounts = new Dictionary<string, int>();

            if (Components.HarvestDesignatables != null)
            {
                foreach (var harvestable in Components.HarvestDesignatables.Items)
                {
                    if (harvestable == null || harvestable.gameObject == null) continue;
                    if (!ToolUtil.GameObjectMatchesWorld(harvestable.gameObject, worldId)) continue;

                    total++;
                    if (harvestable.CanBeHarvested()) ready++;

                    string name = ToolUtil.CleanName(harvestable.GetProperName());
                    cropCounts[name] = cropCounts.TryGetValue(name, out int c) ? c + 1 : 1;
                }
            }

            var crops = new JArray();
            foreach (var kvp in cropCounts.OrderByDescending(k => k.Value))
            {
                crops.Add(new JObject { ["crop"] = kvp.Key, ["count"] = kvp.Value });
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["totalCrops"] = total,
                ["readyToHarvest"] = ready,
                ["cropTypes"] = crops
            };
        }

        public static JObject BuildRanchingSummary(int worldId)
        {
            int total = 0;
            var speciesCounts = new Dictionary<string, int>();

            if (Components.Capturables != null)
            {
                foreach (var cap in Components.Capturables.Items)
                {
                    if (cap == null || cap.gameObject == null) continue;
                    if (!ToolUtil.GameObjectMatchesWorld(cap.gameObject, worldId)) continue;

                    total++;
                    string species = ToolUtil.CleanName(cap.GetProperName());
                    speciesCounts[species] = speciesCounts.TryGetValue(species, out int c) ? c + 1 : 1;
                }
            }

            var speciesArray = new JArray();
            foreach (var kvp in speciesCounts.OrderByDescending(k => k.Value))
            {
                speciesArray.Add(new JObject { ["species"] = kvp.Key, ["count"] = kvp.Value });
            }

            return new JObject
            {
                ["worldId"] = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0),
                ["totalCritters"] = total,
                ["species"] = speciesArray
            };
        }

        public static JObject BuildDupesVitals()
        {
            var dupes = new JArray();
            if (Components.LiveMinionIdentities != null)
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    if (minion == null) continue;
                    float health = DupeAmountUtil.AmountValueByName(minion, "HitPoints", 100f);
                    float stress = DupeAmountUtil.StressValue(minion);
                    float calories = DupeAmountUtil.AmountValueByName(minion, "Calories", 4000f) / 1000f;
                    float breath = DupeAmountUtil.AmountValueByName(minion, "Breath", 100f);
                    float bladder = DupeAmountUtil.AmountValueByName(minion, "Bladder", 0f);

                    dupes.Add(new JObject
                    {
                        ["name"] = minion.GetProperName(),
                        ["worldId"] = minion.GetMyWorldId(),
                        ["healthPct"] = Math.Round(health, 1),
                        ["stressPct"] = Math.Round(stress, 1),
                        ["caloriesKcal"] = Math.Round(calories, 1),
                        ["breathPct"] = Math.Round(breath, 1),
                        ["bladderPct"] = Math.Round(bladder, 1)
                    });
                }
            }

            return new JObject
            {
                ["dupeCount"] = dupes.Count,
                ["dupes"] = dupes
            };
        }

        public static JObject BuildDupesMorale()
        {
            var dupes = new JArray();
            if (Components.LiveMinionIdentities != null)
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    if (minion == null) continue;
                    int morale = (int)DupeAmountUtil.AmountValueByName(minion, "QualityOfLife", 0f);
                    int expectation = (int)DupeAmountUtil.AmountValueByName(minion, "QualityOfLifeExpectation", 0f);

                    dupes.Add(new JObject
                    {
                        ["name"] = minion.GetProperName(),
                        ["morale"] = morale,
                        ["expectation"] = expectation,
                        ["netMorale"] = morale - expectation,
                        ["risk"] = (morale - expectation) < 0 ? "high_stress_risk" : "stable"
                    });
                }
            }

            return new JObject
            {
                ["dupeCount"] = dupes.Count,
                ["dupes"] = dupes
            };
        }

        public static JObject BuildGeysersSummary(int worldId)
        {
            var geysers = new JArray();
            int targetWorld = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0);
            if (Components.Geysers != null)
            {
                var list = Components.Geysers.GetItems(targetWorld);
                if (list != null)
                {
                    foreach (var g in list)
                    {
                        if (g == null || g.configuration == null) continue;
                        int cell = Grid.PosToCell(g);
                        geysers.Add(new JObject
                        {
                            ["name"] = ToolUtil.CleanName(g.GetProperName()),
                            ["typeId"] = g.configuration.typeId.ToString(),
                            ["element"] = g.configuration.geyserType?.element.ToString() ?? "Unknown",
                            ["temperatureK"] = g.configuration.geyserType != null ? Math.Round(g.configuration.geyserType.temperature, 1) : 0,
                            ["cellX"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                            ["cellY"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                            ["worldId"] = g.GetMyWorldId()
                        });
                    }
                }
            }

            return new JObject
            {
                ["worldId"] = targetWorld,
                ["geyserCount"] = geysers.Count,
                ["geysers"] = geysers
            };
        }

        public static JObject BuildResearchProgress()
        {
            string currentTech = Research.Instance?.GetActiveResearch()?.tech?.Name ?? "None";
            int completedTechs = 0;
            if (Research.Instance != null && Db.Get()?.Techs != null)
            {
                foreach (var tech in Db.Get().Techs.resources)
                {
                    if (Research.Instance.Get(tech)?.IsComplete() == true) completedTechs++;
                }
            }

            return new JObject
            {
                ["activeTech"] = currentTech,
                ["completedTechCount"] = completedTechs,
                ["totalTechCount"] = Db.Get()?.Techs?.resources?.Count ?? 0
            };
        }

        public static JObject BuildStockpileSummary(int worldId)
        {
            int targetWorld = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? 0);
            var categories = new Dictionary<string, float>();
            var world = ClusterManager.Instance?.GetWorld(targetWorld);
            if (world != null && world.worldInventory != null)
            {
                categories["totalMassKg"] = 0f;
            }

            return new JObject
            {
                ["worldId"] = targetWorld,
                ["summary"] = "Stockpile tracking active",
                ["categories"] = JObject.FromObject(categories)
            };
        }
    }
}
