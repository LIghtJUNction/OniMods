using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

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
                var selected = available.FirstOrDefault();
                if (selected != null)
                    return MaterialSelection.Success(new List<Tag> { selected.Tag }, "auto", requested, selected, available);

                var defaults = DefaultBuildElements(def);
                if (defaults.Count > 0 && IsFreeBuildContext())
                    return MaterialSelection.Success(defaults, "default_no_inventory_in_debug", requested, null, available);

                return MaterialSelection.Invalid(
                    "No available build material in current world inventory",
                    requested,
                    available,
                    AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList());
            }

            var match = AvailableMaterials(def, worldId, includeUnavailable: true)
                .FirstOrDefault(item => EqualsIgnoreCase(item.Tag.Name, requested)
                    || EqualsIgnoreCase(item.Name, requested)
                    || Contains(item.Tag.Name, requested)
                    || Contains(item.Name, requested));
            var candidates = AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList();
            if (match == null || !match.ValidForBuilding)
            {
                return MaterialSelection.Invalid(
                    $"Material '{requested}' is not valid for {def.PrefabID}",
                    requested,
                    available,
                    candidates);
            }

            if (match.AvailableKg <= 0f && !IsFreeBuildContext())
            {
                return MaterialSelection.Invalid(
                    $"Material '{match.Tag.Name}' is valid for {def.PrefabID}, but none is currently available",
                    requested,
                    available,
                    candidates);
            }

            return MaterialSelection.Success(new List<Tag> { match.Tag }, "explicit", requested, match, available);
        }

        private static List<BuildMaterialInfo> AvailableMaterials(BuildingDef def, int worldId, bool includeUnavailable)
        {
            var categories = MaterialCategoryTags(def).ToList();
            var candidates = CandidateMaterialTags(def, worldId)
                .Where(tag => tag.IsValid)
                .Distinct()
                .Select(tag =>
                {
                    var matches = categories.Where(category => MaterialMatchesCategory(tag, category)).ToList();
                    return new BuildMaterialInfo
                    {
                        Tag = tag,
                        Name = tag.ProperNameStripLink(),
                        AvailableKg = AvailableAmount(worldId, tag),
                        ValidForBuilding = categories.Count == 0 || matches.Count > 0,
                        Categories = matches
                    };
                })
                .Where(item => item.ValidForBuilding)
                .Where(item => includeUnavailable || item.AvailableKg > 0f || IsFreeBuildContext())
                .OrderByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag.Name)
                .ToList();

            return candidates;
        }

        private static float RequiredMaterialKg(BuildingDef def)
        {
            if (def == null)
                return 0f;

            try
            {
                var buildingsType = typeof(BuildingDef).Assembly.GetType("TUNING+BUILDINGS") ?? Type.GetType("TUNING+BUILDINGS");
                var massField = buildingsType?.GetField("CONSTRUCTION_MASS_KG", BindingFlags.Public | BindingFlags.Static);
                var masses = massField?.GetValue(null) as float[];
                int tier = Mathf.RoundToInt(ToolUtil.SafeFloat(def.MassTier));
                if (masses != null && tier >= 0 && tier < masses.Length)
                    return Math.Max(0f, masses[tier]);
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

            foreach (var tag in InventoryMaterialTags(def, worldId))
                yield return tag;
        }

        private static IEnumerable<Tag> InventoryMaterialTags(BuildingDef def, int worldId)
        {
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                yield break;

            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;

                int itemWorldId = PickupableWorldId(pickupable);
                if (worldId >= 0 && itemWorldId != worldId)
                    continue;

                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                Tag prefabTag = kpid?.PrefabTag ?? Tag.Invalid;
                Tag elementTag = primary != null ? new Tag(primary.ElementID.ToString()) : Tag.Invalid;

                if (MaterialMatchesAnyCategory(prefabTag, categories))
                    yield return prefabTag;
                if (MaterialMatchesAnyCategory(elementTag, categories))
                    yield return elementTag;
                if (kpid != null && categories.Any(category => kpid.HasTag(category)))
                {
                    if (elementTag.IsValid)
                        yield return elementTag;
                    if (prefabTag.IsValid)
                        yield return prefabTag;
                }
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

        private static int PickupableWorldId(Pickupable pickupable)
        {
            int cell = pickupable.cachedCell;
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];
            return pickupable.GetMyWorldId();
        }

        private static float AvailableAmount(int worldId, Tag tag)
        {
            if (!tag.IsValid || ClusterManager.Instance == null)
                return 0f;
            var world = ClusterManager.Instance.GetWorld(worldId >= 0 ? worldId : ClusterManager.Instance.activeWorldId);
            if (world == null || world.worldInventory == null)
                return 0f;

            float amount = ToolUtil.SafeFloat(world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.KPrefabID == null)
                    continue;
                if (pickupable.storage != null || pickupable.KPrefabID.HasTag(GameTags.Stored))
                    continue;
                if (!pickupable.KPrefabID.HasTag(tag))
                    continue;
                if (PickupableWorldId(pickupable) != world.id)
                    continue;

                if (pickupable.PrimaryElement != null)
                    amount += ToolUtil.SafeFloat(pickupable.PrimaryElement.Mass);
            }
            return amount;
        }

        private static bool IsFreeBuildContext()
        {
            return DebugHandler.InstantBuildMode || (Game.Instance != null && Game.Instance.SandboxModeActive);
        }

        private static FacadeSelection ResolveFacade(BuildingDef def, string facade)
        {
            if (string.IsNullOrWhiteSpace(facade))
                return FacadeSelection.Default();

            var facadeId = facade.Trim();
            if (facadeId.Equals("default", StringComparison.OrdinalIgnoreCase) || facadeId == "DEFAULT_FACADE")
                return FacadeSelection.Default();

            if (def.AvailableFacades == null || !def.AvailableFacades.Contains(facadeId))
                return FacadeSelection.Invalid($"Facade '{facadeId}' is not available for {def.PrefabID}");

            var permit = Db.Get().Permits.TryGet(facadeId);
            if (permit == null)
                return FacadeSelection.Invalid($"Facade '{facadeId}' has no permit resource");

            if (!permit.IsUnlocked())
                return FacadeSelection.Invalid($"Facade '{facadeId}' is locked");

            return FacadeSelection.Custom(facadeId);
        }
        private struct FacadeSelection
        {
            public readonly bool Valid;
            public readonly string TryPlaceId;
            public readonly string ResponseId;
            public readonly string Error;

            private FacadeSelection(bool valid, string tryPlaceId, string responseId, string error)
            {
                Valid = valid;
                TryPlaceId = tryPlaceId;
                ResponseId = responseId;
                Error = error;
            }

            public static FacadeSelection Default()
            {
                return new FacadeSelection(true, null, "DEFAULT_FACADE", null);
            }

            public static FacadeSelection Custom(string facadeId)
            {
                return new FacadeSelection(true, facadeId, facadeId, null);
            }

            public static FacadeSelection Invalid(string error)
            {
                return new FacadeSelection(false, null, null, error);
            }
        }

        private sealed class BuildMaterialInfo
        {
            public Tag Tag;
            public string Name;
            public float AvailableKg;
            public bool ValidForBuilding;
            public List<Tag> Categories = new List<Tag>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["availableKg"] = Math.Round(ToolUtil.SafeFloat(AvailableKg), 3),
                    ["validForBuilding"] = ValidForBuilding,
                    ["categories"] = Categories.Select(tag => tag.Name).OrderBy(name => name).ToList()
                };
            }
        }

        private sealed class MaterialSelection
        {
            public bool Valid;
            public string Mode;
            public string Requested;
            public List<Tag> Elements = new List<Tag>();
            public BuildMaterialInfo Selected;
            public List<BuildMaterialInfo> Available = new List<BuildMaterialInfo>();
            public List<BuildMaterialInfo> Candidates = new List<BuildMaterialInfo>();
            public float RequiredKg;
            public string Error;

            public static MaterialSelection Success(List<Tag> elements, string mode, string requested, BuildMaterialInfo selected, List<BuildMaterialInfo> available)
            {
                return new MaterialSelection
                {
                    Valid = true,
                    Mode = mode,
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Elements = elements ?? new List<Tag>(),
                    Selected = selected,
                    Available = available ?? new List<BuildMaterialInfo>(),
                    RequiredKg = 0f
                };
            }

            public static MaterialSelection Invalid(string error, string requested, List<BuildMaterialInfo> available, List<BuildMaterialInfo> candidates)
            {
                return new MaterialSelection
                {
                    Valid = false,
                    Mode = "invalid",
                    Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                    Error = error,
                    Available = available ?? new List<BuildMaterialInfo>(),
                    Candidates = candidates ?? new List<BuildMaterialInfo>(),
                    RequiredKg = 0f
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                float selectedAvailableKg = Selected != null ? ToolUtil.SafeFloat(Selected.AvailableKg) : 0f;
                bool hasRequiredKg = RequiredKg > 0f;
                object satisfied = hasRequiredKg ? (object)(IsFreeBuildContext() || selectedAvailableKg >= RequiredKg) : null;
                float shortageKg = hasRequiredKg ? Math.Max(0f, RequiredKg - selectedAvailableKg) : 0f;

                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["mode"] = Mode,
                    ["requested"] = Requested,
                    ["requirementKnown"] = hasRequiredKg,
                    ["requiredKg"] = hasRequiredKg ? (object)Math.Round(ToolUtil.SafeFloat(RequiredKg), 3) : null,
                    ["selectedAvailableKg"] = Selected != null ? (object)Math.Round(selectedAvailableKg, 3) : null,
                    ["satisfied"] = satisfied,
                    ["shortageKg"] = hasRequiredKg ? (object)Math.Round(shortageKg, 3) : null,
                    ["selected"] = Selected != null ? Selected.ToDictionary() : null,
                    ["elements"] = Elements.Select(tag => tag.Name).ToList(),
                    ["availableMaterials"] = Available.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["candidateMaterials"] = Candidates.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["fallbackMaterial"] = Available.Count > 0 ? Available[0].Tag.Name : null,
                    ["next"] = Valid
                        ? "Material is usable."
                        : Available.Count > 0
                            ? "Retry with material=auto or material=" + Available[0].Tag.Name + " if exact material is not required."
                            : "No usable material is currently available; inspect inventory/material candidates before retrying.",
                    ["suggestion"] = Available.Count > 0 ? "Use material=auto or material=" + Available[0].Tag.Name : "No available material; inspect read_control domain=resources action=inventory/building_control domain=planning action=materials",
                    ["error"] = Error
                };
            }
        }
    }
}
