using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static void AddConstructionDiagnostics(Dictionary<string, object> result, string prefabId, int x, int y, int worldId)
        {
            if (result == null || string.IsNullOrWhiteSpace(prefabId))
                return;
            string resolved;
            string error;
            var def = ResolveBuildingDef(prefabId, out resolved, out error);
            if (def != null)
                result["constructionDiagnostics"] = ConstructionDiagnostics(def, x, y, worldId, result);
        }

        private static Dictionary<string, object> BatchConstructionDiagnostics(List<Dictionary<string, object>> results)
        {
            var slotEntries = new List<Dictionary<string, object>>();
            var includedResults = (results ?? new List<Dictionary<string, object>>())
                .Where(item => !GetBool(item, "alreadyPresent") && (GetBool(item, "planned") || GetBool(item, "pendingBuild"))).ToList();
            foreach (var result in includedResults)
            {
                var material = GetObject(GetObject(result, "constructionDiagnostics"), "material");
                if (material == null)
                    continue;
                var slots = material.TryGetValue("recipeSlots", out object rawSlots)
                    ? (rawSlots as IEnumerable<object>)?.OfType<Dictionary<string, object>>().ToList()
                    : null;
                if (slots != null && slots.Count > 0)
                {
                    foreach (var slot in slots)
                        slotEntries.Add(new Dictionary<string, object> { ["result"] = result, ["slot"] = slot });
                }
                else
                {
                    slotEntries.Add(new Dictionary<string, object>
                    {
                        ["result"] = result,
                        ["slot"] = new Dictionary<string, object>
                        {
                            ["material"] = ValueAt(material, "tag") ?? "unknown",
                            ["requiredKg"] = FloatAt(material, "requiredKg"),
                            ["reachableAvailableKg"] = FloatAt(material, "reachableAvailableKg"),
                            ["unverifiedLocalStockKg"] = FloatAt(material, "unverifiedLocalStockKg"),
                            ["hasActiveNavigator"] = BoolAt(material, "hasActiveNavigator"),
                            ["foreignWorldInventoryKgContextOnly"] = FloatAt(material, "foreignWorldInventoryKgContextOnly"),
                            ["requirementKnown"] = BoolAt(material, "requirementKnown")
                        }
                    });
                }
            }

            var groups = slotEntries
                .GroupBy(entry => ValueAt(GetObject(entry, "slot"), "material") ?? "unknown", StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    var firstSlot = GetObject(first, "slot");
                    float aggregateRequired = group.Sum(entry => FloatAt(GetObject(entry, "slot"), "requiredKg"));
                    float reachableKg = FloatAt(firstSlot, "reachableAvailableKg");
                    float unverifiedLocalKg = FloatAt(firstSlot, "unverifiedLocalStockKg");
                    bool hasNavigator = BoolAt(firstSlot, "hasActiveNavigator");
                    bool requirementKnown = group.All(entry => BoolAt(GetObject(entry, "slot"), "requirementKnown"));
                    int anchorCount = group.Select(entry => GetObject(entry, "result")).Distinct().Count();
                    var readiness = ConstructionReadinessPolicy.Material(
                        aggregateRequired, reachableKg, unverifiedLocalKg, hasNavigator,
                        requirementKnown && !string.Equals(group.Key, "unknown", StringComparison.OrdinalIgnoreCase));
                    return new Dictionary<string, object>
                    {
                        ["material"] = group.Key == "unknown" ? null : group.Key,
                        ["newBlueprintOrPendingAnchors"] = anchorCount,
                        ["requiredKgPerAnchor"] = null,
                        ["aggregateRequiredKg"] = requirementKnown ? (object)Math.Round(aggregateRequired, 3) : null,
                        ["reachableAvailableKgCountedOnce"] = Math.Round(reachableKg, 3),
                        ["aggregateShortageKg"] = readiness.ShortageKg.HasValue ? (object)Math.Round(readiness.ShortageKg.Value, 3) : null,
                        ["status"] = readiness.Status,
                        ["unverifiedLocalStockKg"] = Math.Round(unverifiedLocalKg, 3),
                        ["foreignWorldInventoryKgContextOnly"] = FloatAt(firstSlot, "foreignWorldInventoryKgContextOnly"),
                        ["stockReservedForThisBatch"] = false
                    };
                })
                .ToList();
            int included = includedResults.Count;
            int existing = (results ?? new List<Dictionary<string, object>>()).Count(item => GetBool(item, "alreadyPresent"));
            return new Dictionary<string, object>
            {
                ["resources"] = groups,
                ["newBlueprintOrPendingAnchors"] = included,
                ["alreadyPresentAnchorsExcluded"] = existing,
                ["note"] = "This batch calculation counts reachable stock once and does not reserve it. Existing blueprints/buildings are excluded; pending builds after dig are included."
            };
        }

        private static Dictionary<string, object> ConstructionDiagnostics(
            BuildingDef def,
            int x,
            int y,
            int worldId,
            Dictionary<string, object> result)
        {
            var materialSelection = ObjectAt(result, "materialSelection");
            var selected = ObjectAt(materialSelection, "selected");
            string materialTag = ValueAt(selected, "tag");
            if (string.IsNullOrWhiteSpace(materialTag))
                materialTag = StringListAt(materialSelection, "elements").FirstOrDefault();

            float requiredKg = FloatAt(materialSelection, "requiredKg");
            float reachableKg = FloatAt(selected, "reachableLooseKg");
            float localInventoryKg = FloatAt(selected, "localInventoryKg");
            float foreignInventoryKg = FloatAt(selected, "foreignWorldInventoryKgContextOnly");
            float relatedInventoryKg = FloatAt(selected, "relatedWorldInventoryKgContextOnly");
            float storedKg = FloatAt(selected, "storedKgNotCountedAsReachable");
            float unverifiedLocalKg = FloatAt(selected, "unverifiedLocalStockKg");
            bool hasNavigator = BoolAt(selected, "hasActiveNavigator");
            bool materialKnown = selected != null && !string.IsNullOrWhiteSpace(materialTag);
            string materialStatus = ValueAt(materialSelection, "localAvailabilityStatus") ?? "unknown";
            object shortageKg = materialSelection != null && materialSelection.TryGetValue("shortageKg", out object shortage) ? shortage : null;
            var work = WorkPositionDiagnostics(def, x, y, worldId);
            return new Dictionary<string, object>
            {
                ["material"] = new Dictionary<string, object>
                {
                    ["tag"] = materialKnown ? materialTag : null,
                    ["requirementKnown"] = materialSelection != null && materialSelection.ContainsKey("requirementKnown")
                        && materialSelection["requirementKnown"] != null && Convert.ToBoolean(materialSelection["requirementKnown"]),
                    ["requiredKg"] = requiredKg > 0f ? (object)Math.Round(requiredKg, 3) : null,
                    ["reachableAvailableKg"] = materialKnown ? (object)Math.Round(reachableKg, 3) : null,
                    ["shortageKg"] = shortageKg,
                    ["status"] = materialStatus,
                    ["localInventoryKg"] = materialKnown ? (object)Math.Round(localInventoryKg, 3) : null,
                    ["relatedWorldInventoryKgContextOnly"] = materialKnown ? (object)Math.Round(relatedInventoryKg, 3) : null,
                    ["foreignWorldInventoryKgContextOnly"] = materialKnown ? (object)Math.Round(foreignInventoryKg, 3) : null,
                    ["unverifiedLocalStockKg"] = materialKnown ? (object)Math.Round(unverifiedLocalKg, 3) : null,
                    ["storedKgNotCountedAsReachable"] = materialKnown ? (object)Math.Round(storedKg, 3) : null,
                    ["hasActiveNavigator"] = hasNavigator,
                    ["recipeSlots"] = materialSelection != null && materialSelection.TryGetValue("recipeSlots", out object recipeSlots)
                        ? recipeSlots : new List<object>(),
                    ["note"] = "Reachable availability counts loose matching material reachable by a living minion in this world. Local stored stock is unknown for fetchability; related-world stock is context only and never satisfies local requirements."
                },
                ["workPosition"] = work
            };
        }

        private static Dictionary<string, object> WorkPositionDiagnostics(BuildingDef def, int x, int y, int worldId)
        {
            var placement = BuildPlacementDetails(def, x, y, worldId);
            var footprint = placement?.Footprint?.Where(cell => cell.Valid && cell.InWorld).ToList()
                ?? new List<FootprintCell>();
            bool footprintKnown = placement != null && footprint.Count > 0
                && footprint.Count == placement.Footprint.Count;
            var navigators = ActiveBuildNavigators(worldId);
            var footprintCells = new HashSet<int>(footprint.Select(cell => cell.Cell));
            var candidates = new HashSet<int>();
            int[,] offsets = { { 0, 1 }, { 0, -1 }, { 1, 0 }, { -1, 0 } };
            foreach (var cell in footprint)
            {
                for (int i = 0; i < offsets.GetLength(0); i++)
                {
                    int neighbor = Grid.OffsetCell(cell.Cell, new CellOffset { x = offsets[i, 0], y = offsets[i, 1] });
                    if (!Grid.IsValidCell(neighbor) || !Grid.IsWorldValidCell(neighbor)
                        || Grid.WorldIdx[neighbor] != worldId || footprintCells.Contains(neighbor))
                        continue;
                    candidates.Add(neighbor);
                }
            }

            var reachable = new List<int>();
            foreach (int cell in candidates)
            {
                if (navigators.Any(navigator => SafeBuildCanReach(navigator, cell)))
                    reachable.Add(cell);
            }
            string status = ConstructionReadinessPolicy.WorkPosition(footprintKnown, navigators.Count > 0, candidates.Count, reachable.Count);
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["source"] = "native BuildingDef placement footprint perimeter checked with live minion Navigator.CanReach(cell)",
                ["footprintCells"] = footprint.Count,
                ["candidateApproachCells"] = candidates.Count,
                ["reachableApproachCells"] = reachable.Count,
                ["reachableWorkPositions"] = reachable.Take(20).Select(cell => new Dictionary<string, int>
                {
                    ["x"] = Grid.CellColumn(cell),
                    ["y"] = Grid.CellRow(cell),
                    ["cell"] = cell
                }).ToList(),
                ["livingNavigators"] = navigators.Count,
                ["note"] = status == "reachable"
                    ? "At least one native approach position around the complete footprint is currently reachable."
                    : status == "unreachable"
                        ? "Living minions exist, but none can currently reach the footprint approach positions."
                        : "Work-position reachability could not be established from the current world state."
            };
        }

        private static Dictionary<string, object> ObjectAt(Dictionary<string, object> value, string key)
        {
            return value != null && value.TryGetValue(key, out object nested)
                ? nested as Dictionary<string, object>
                : null;
        }

        private static string ValueAt(Dictionary<string, object> value, string key)
        {
            return value != null && value.TryGetValue(key, out object item) ? item?.ToString() : null;
        }

        private static float FloatAt(Dictionary<string, object> value, string key)
        {
            return float.TryParse(ValueAt(value, key), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
        }

        private static bool BoolAt(Dictionary<string, object> value, string key)
        {
            return bool.TryParse(ValueAt(value, key), out bool parsed) && parsed;
        }

        private static List<string> StringListAt(Dictionary<string, object> value, string key)
        {
            if (value == null || !value.TryGetValue(key, out object raw) || !(raw is IEnumerable<object> items))
                return new List<string>();
            return items.Where(item => item != null).Select(item => item.ToString()).ToList();
        }
    }
}
