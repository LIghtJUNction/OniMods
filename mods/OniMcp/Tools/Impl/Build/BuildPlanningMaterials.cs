using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static MaterialSelection SelectElements(BuildingDef def, string material, int worldId)
        {
            string requested = material?.Trim();
            bool auto = string.IsNullOrWhiteSpace(requested)
                || requested.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("default", StringComparison.OrdinalIgnoreCase);

            var available = AvailableMaterials(def, worldId, includeUnavailable: false).ToList();
            if (auto)
            {
                var recipe = ConstructionRecipeAdapter.Read(def.Mass, def.MaterialCategory);
                var autoCandidates = AvailableMaterials(def, worldId, includeUnavailable: true);
                if (recipe.Known)
                {
                    var slotMaterials = ConstructionRecipeAdapter.SelectAvailable(
                        recipe,
                        autoCandidates,
                        (item, category) => MaterialMatchesAnyCategory(item.Tag, ParseMaterialCategoryExpression(category).ToList()),
                        item => item.AvailableKg > 0f,
                        item => item.ReachableLooseKg > 0f ? 1000000000f + item.ReachableLooseKg : item.AvailableKg);
                    if (slotMaterials == null)
                        return MaterialSelection.Invalid("No locally available material for every native construction slot.",
                            requested, available, autoCandidates.Take(20).ToList(), recipe,
                            reasonCode: "insufficient_material");
                    var underSupplied = Enumerable.Range(0, recipe.Slots.Count)
                        .GroupBy(index => slotMaterials[index].Tag.Name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault(group => slotMaterials[group.First()].AvailableKg
                            + 0.001f < group.Sum(index => recipe.Slots[index].RequiredKg));
                    if (underSupplied != null)
                    {
                        var shortMaterial = slotMaterials[underSupplied.First()];
                        return MaterialSelection.Invalid(
                            "Local material shortage for '" + shortMaterial.Tag.Name + "': requires "
                                + Math.Round(underSupplied.Sum(index => recipe.Slots[index].RequiredKg), 3)
                                + " kg, local total is " + Math.Round(shortMaterial.AvailableKg, 3) + " kg.",
                            requested, available, autoCandidates.Take(20).ToList(), recipe, slotMaterials, slotMaterials[0],
                            reasonCode: "insufficient_material");
                    }
                    var elements = slotMaterials.Select(item => item.Tag).ToList();
                    return ValidatedMaterialSelection(elements, "auto", requested, slotMaterials[0], available, recipe, slotMaterials);
                }

                return MaterialSelection.Invalid("Native construction recipe is unknown; cannot select a complete material set.",
                    requested, available, autoCandidates.Take(20).ToList(), recipe,
                    reasonCode: "invalid_material_requirement");
            }

            var requestedCategory = MaterialCategoryTags(def).FirstOrDefault(category => EqualsIgnoreCase(category.Name, requested));
            if (requestedCategory.IsValid)
            {
                var categoryMatch = AvailableMaterials(def, worldId, includeUnavailable: true)
                    .Where(item => MaterialMatchesCategory(item.Tag, requestedCategory))
                    .OrderByDescending(item => item.ReachableLooseKg)
                    .ThenByDescending(item => item.AvailableKg)
                    .FirstOrDefault();
                if (categoryMatch != null)
                {
                    var recipe = ConstructionRecipeAdapter.Read(def.Mass, def.MaterialCategory);
                    var slotMaterials = new List<BuildMaterialInfo>();
                    var elements = new List<Tag>();
                    if (recipe.Known)
                    {
                        foreach (var slot in recipe.Slots)
                        {
                            var categories = ParseMaterialCategoryExpression(slot.Category).ToList();
                            if (!MaterialMatchesAnyCategory(categoryMatch.Tag, categories))
                                return MaterialSelection.Invalid("Requested category cannot satisfy construction slot " + slot.Index + ".",
                                    requested, available, AvailableMaterials(def, worldId, true).Take(20).ToList());
                            elements.Add(categoryMatch.Tag);
                            slotMaterials.Add(categoryMatch);
                        }
                        return ValidatedMaterialSelection(elements, "category:" + requestedCategory.Name, requested,
                            categoryMatch, available, recipe, slotMaterials);
                    }
                    return MaterialSelection.Invalid("Native construction recipe is invalid or incomplete.",
                        requested, available, new List<BuildMaterialInfo> { categoryMatch }, recipe,
                        reasonCode: "invalid_material_requirement");
                }
            }

            var match = AvailableMaterials(def, worldId, includeUnavailable: true)
                .FirstOrDefault(item => EqualsIgnoreCase(item.Tag.Name, requested)
                    || EqualsIgnoreCase(item.Name, requested)
);
            if (match == null)
            {
                Tag requestedTag = new Tag(requested);
                Tag elementTag = requestedTag;
                if (ElementLoader.GetElement(elementTag) == null)
                {
                    var prefab = Assets.GetPrefab(requestedTag);
                    var primary = prefab != null ? prefab.GetComponent<PrimaryElement>() : null;
                    if (primary != null)
                        elementTag = new Tag(primary.ElementID.ToString());
                }
                var categories = MaterialCategoryTags(def).ToList();
                if (elementTag.IsValid && ElementLoader.GetElement(elementTag) != null
                    && (categories.Count == 0 || MaterialMatchesAnyCategory(elementTag, categories)))
                    match = MaterialInfoForTag(def, worldId, elementTag);
            }
            var candidates = AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList();
            if (match == null || !match.ValidForBuilding)
            {
                return MaterialSelection.Invalid(
                    $"Material '{requested}' is not valid for {def.PrefabID}",
                    requested,
                    available,
                    candidates);
            }

            var selectedRecipe = ConstructionRecipeAdapter.Read(def.Mass, def.MaterialCategory);
            if (selectedRecipe.Known)
            {
                var elements = new List<Tag>();
                var slotMaterials = new List<BuildMaterialInfo>();
                foreach (var slot in selectedRecipe.Slots)
                {
                    var categories = ParseMaterialCategoryExpression(slot.Category).ToList();
                    if (!MaterialMatchesAnyCategory(match.Tag, categories))
                        return MaterialSelection.Invalid(
                            "Material '" + requested + "' does not satisfy native construction slot " + slot.Index + " (" + slot.Category + ")",
                            requested, available, candidates);
                    elements.Add(match.Tag);
                    slotMaterials.Add(match);
                }
                return ValidatedMaterialSelection(elements, "explicit", requested, match, available, selectedRecipe, slotMaterials);
            }
            return ValidatedMaterialSelection(new List<Tag> { match.Tag }, "explicit", requested, match, available,
                selectedRecipe, new List<BuildMaterialInfo> { match });
        }

        private static MaterialSelection ValidatedMaterialSelection(
            List<Tag> elements,
            string mode,
            string requested,
            BuildMaterialInfo selected,
            List<BuildMaterialInfo> available,
            ConstructionRecipe recipe = null,
            List<BuildMaterialInfo> slotMaterials = null)
        {
            if (elements == null || elements.Count == 0)
                return MaterialSelection.Invalid("No material selected.", requested, available, new List<BuildMaterialInfo>());

            Tag primaryTag = elements[0];
            if (!primaryTag.IsValid || ElementLoader.GetElement(elements[0]) == null)
                return MaterialSelection.Invalid(
                    $"Material '{primaryTag.Name}' is not a valid build material.",
                    requested,
                    available,
                    (available ?? new List<BuildMaterialInfo>()).Take(20).ToList());

            var result = MaterialSelection.Success(elements, mode, requested, selected, available, recipe, slotMaterials);
            result.RequireMassForPlacements(1);
            return result;
        }

        private static List<BuildMaterialInfo> AvailableMaterials(BuildingDef def, int worldId, bool includeUnavailable)
        {
            var candidates = CandidateMaterialTags(def, worldId)
                .Where(tag => tag.IsValid)
                .Distinct()
                .Select(tag =>
                {
                    return MaterialInfoForTag(def, worldId, tag);
                })
                .Where(item => item.ValidForBuilding)
                .Where(item => includeUnavailable || item.AvailableKg > 0f)
                .OrderByDescending(item => item.ReachableLooseKg)
                .ThenByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag.Name)
                .ToList();

            return candidates;
        }

        private static BuildMaterialInfo MaterialInfoForTag(BuildingDef def, int worldId, Tag tag)
        {
            var categories = MaterialCategoryTags(def).ToList();
            var matches = categories.Where(category => MaterialMatchesCategory(tag, category)).ToList();
            var snapshot = GetBuildStockSnapshot(worldId);
            var stock = snapshot.For(tag);
            return new BuildMaterialInfo
            {
                Tag = tag,
                Name = tag.ProperNameStripLink(),
                // WorldInventory.TotalAmount includes Pickupables; use max to tolerate a
                // same-frame index refresh delay without double-counting the component scan.
                AvailableKg = Math.Max(stock.LocalWorldInventoryKg, stock.LooseKg),
                ReachableLooseKg = stock.ReachableLooseKg,
                LooseKg = stock.LooseKg,
                StoredKg = stock.StoredKg,
                UnverifiedLocalKg = ConstructionReadinessPolicy.LocalUnverifiedStock(
                    stock.LocalWorldInventoryKg, stock.LooseKg, stock.ReachableLooseKg, stock.StoredKg),
                RelatedForeignKg = ConstructionReadinessPolicy.ForeignInventoryContext(
                    stock.RelatedWorldInventoryKg, stock.LocalWorldInventoryKg),
                LocalWorldInventoryKg = stock.LocalWorldInventoryKg,
                RelatedWorldInventoryKg = stock.RelatedWorldInventoryKg,
                HasActiveNavigator = snapshot.ActiveNavigatorCount > 0,
                ValidForBuilding = categories.Count == 0 || matches.Count > 0,
                Categories = matches
            };
        }

        private static float RequiredMaterialKg(BuildingDef def)
        {
            if (def == null)
                return 0f;

            try
            {
                return ConstructionRecipeAdapter.Read(def.Mass, def.MaterialCategory).TotalMassKg;
            }
            catch
            {
                // Keep material diagnostics best-effort across ONI builds.
            }

            return 0f;
        }

        private static IEnumerable<Tag> CandidateMaterialTags(BuildingDef def, int worldId)
        {
            foreach (var tag in DefaultBuildElements(def))
                yield return tag;

            foreach (var category in MaterialCategoryTags(def))
            {
                if (DiscoveredResources.Instance != null)
                {
                    IEnumerable<Tag> discovered = null;
                    try
                    {
                        discovered = DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category);
                    }
                    catch
                    {
                        discovered = null;
                    }

                    if (discovered != null)
                    {
                        foreach (var tag in discovered)
                            yield return tag;
                    }
                }
            }

            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                yield break;
            foreach (var tag in GetBuildStockSnapshot(worldId).Tags)
            {
                if (MaterialMatchesAnyCategory(tag, categories))
                    yield return tag;
            }
        }

        private static IEnumerable<Tag> MatchingMaterialCategories(BuildingDef def, Tag material)
        {
            var categories = MaterialCategoryTags(def).ToList();
            foreach (var category in categories)
            {
                if (MaterialMatchesCategory(material, category))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> MaterialCategoryTags(BuildingDef def)
        {
            if (def.MaterialCategory == null)
                yield break;
            foreach (string categoryName in def.MaterialCategory)
            {
                foreach (var category in ParseMaterialCategoryExpression(categoryName))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> ParseMaterialCategoryExpression(string categoryExpression)
        {
            if (string.IsNullOrWhiteSpace(categoryExpression))
                yield break;

            char[] separators = { '&', '|', ',', ';' };
            foreach (var part in categoryExpression.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var category = new Tag(part.Trim());
                if (category.IsValid)
                    yield return category;
            }
        }

        private static List<Tag> DefaultBuildElements(BuildingDef def)
        {
            var defaults = def.DefaultElements() ?? new List<Tag>();
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                return defaults.Where(tag => tag.IsValid).Distinct().ToList();

            return defaults
                .Where(tag => MaterialMatchesAnyCategory(tag, categories))
                .Distinct()
                .ToList();
        }

        private static bool MaterialMatchesAnyCategory(Tag material, List<Tag> categories)
        {
            return material.IsValid && categories.Any(category => MaterialMatchesCategory(material, category));
        }

        private static bool MaterialMatchesCategory(Tag material, Tag category)
        {
            if (!material.IsValid || !category.IsValid)
                return false;
            if (material == category)
                return true;

            var element = ElementLoader.GetElement(material);
            if (element != null && (element.GetMaterialCategoryTag() == category || element.HasTag(category)))
                return true;

            var prefab = Assets.GetPrefab(material);
            var kpid = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            return kpid != null && kpid.HasTag(category);
        }

        private sealed class BuildMaterialInfo
        {
            public Tag Tag;
            public string Name;
            public float AvailableKg;
            public float LocalWorldInventoryKg;
            public float RelatedWorldInventoryKg;
            public float ReachableLooseKg;
            public float LooseKg;
            public float StoredKg;
            public float UnverifiedLocalKg;
            public float RelatedForeignKg;
            public bool HasActiveNavigator;
            public bool ValidForBuilding;
            public List<Tag> Categories = new List<Tag>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["availableKg"] = Math.Round(ToolUtil.SafeFloat(ReachableLooseKg), 3),
                    ["localInventoryKg"] = Math.Round(ToolUtil.SafeFloat(AvailableKg), 3),
                ["localWorldInventoryKg"] = Math.Round(ToolUtil.SafeFloat(LocalWorldInventoryKg), 3),
                    ["relatedWorldInventoryKgContextOnly"] = Math.Round(ToolUtil.SafeFloat(RelatedWorldInventoryKg), 3),
                    ["foreignWorldInventoryKgContextOnly"] = Math.Round(ToolUtil.SafeFloat(RelatedForeignKg), 3),
                    ["reachableLooseKg"] = Math.Round(ToolUtil.SafeFloat(ReachableLooseKg), 3),
                    ["looseKg"] = Math.Round(ToolUtil.SafeFloat(LooseKg), 3),
                    ["unverifiedLocalStockKg"] = Math.Round(ToolUtil.SafeFloat(UnverifiedLocalKg), 3),
                    ["storedKgNotCountedAsReachable"] = Math.Round(ToolUtil.SafeFloat(StoredKg), 3),
                    ["hasActiveNavigator"] = HasActiveNavigator,
                    ["validForBuilding"] = ValidForBuilding,
                    ["categories"] = Categories.Select(tag => tag.Name).OrderBy(name => name).ToList()
                };
            }
        }

    }
}
