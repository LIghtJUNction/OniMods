using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static void SetPriority(GameObject go, int priority)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            int clamped = Math.Max(1, Math.Min(priority, 9));
            prioritizable.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, clamped));
        }

        private static Orientation ParseOrientation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Orientation.Neutral;

            Orientation orientation;
            return Enum.TryParse(value, true, out orientation) ? orientation : Orientation.Neutral;
        }

        private static bool Matches(BuildingDef def, string query)
        {
            string q = query.Trim();
            return Contains(def.PrefabID, q)
                || Contains(def.Name, q)
                || Contains(def.Desc, q)
                || BuildingCategories(def).Any(category => Contains(category, q))
                || def.SearchTerms.Any(term => Contains(term, q));
        }

        private static bool MatchesCategory(BuildingDef def, string category)
        {
            string q = category.Trim();
            return BuildingCategories(def).Any(value => Contains(value, q));
        }

        private static Dictionary<string, object> BuildingDefToDictionary(BuildingDef def)
        {
            int worldId = ClusterManager.Instance?.activeWorldId ?? -1;
            var availableMaterials = AvailableMaterials(def, worldId, includeUnavailable: false).ToList();
            var defaultElements = DefaultBuildElements(def);
            var availableTags = new HashSet<Tag>(availableMaterials.Select(m => m.Tag));
            var filteredDefaults = defaultElements
                .Where(tag => availableTags.Contains(tag))
                .ToList();
            if (filteredDefaults.Count == 0 && defaultElements.Count > 0)
                filteredDefaults = defaultElements;

            string recommendedMaterial = availableMaterials.Count > 0
                ? availableMaterials[0].Tag.Name
                : null;

            return new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["width"] = def.WidthInCells,
                ["height"] = def.HeightInCells,
                ["buildLocationRule"] = def.BuildLocationRule.ToString(),
                ["placement"] = BuildDefPlacementToDictionary(def),
                ["categories"] = BuildingCategories(def),
                ["materialCategories"] = def.MaterialCategory,
                ["resolvedMaterialCategories"] = MaterialCategoryTags(def).Select(tag => tag.Name).ToList(),
                ["defaultMaterials"] = filteredDefaults.Select(tag => tag.Name).ToList(),
                ["availableMaterials"] = availableMaterials.Take(20).Select(item => item.ToDictionary()).ToList(),
                ["recommendedMaterial"] = recommendedMaterial,
                ["autoMaterial"] = AutoMaterialValue(def, worldId),
                ["autoMaterialReason"] = AutoMaterialReason(def, worldId),
                ["facades"] = BuildingFacades(def),
                ["requiresPower"] = def.RequiresPowerInput,
                ["powerWatts"] = Math.Round(def.EnergyConsumptionWhenActive, 1),
                ["unlocked"] = IsTechUnlocked(def),
                ["availableNow"] = IsUnlockedAndAvailable(def)
            };
        }

private static bool IsUnlockedAndAvailable(BuildingDef def)
{
if (def == null)
return false;
try
{
return def.IsAvailable() && IsTechUnlocked(def);
}
catch
{
return false;
}
}

private static bool IsTechUnlocked(BuildingDef def)
{
if (def == null || Db.Get() == null || Db.Get().Techs == null)
return false;
try
{
return Db.Get().Techs.IsTechItemComplete(def.PrefabID);
}
catch
{
return false;
}
}

        private static object AutoMaterialValue(BuildingDef def, int worldId)
        {
            var material = AvailableMaterials(def, worldId, includeUnavailable: false).FirstOrDefault();
            return material != null ? (object)material.Tag.Name : "unavailable";
        }

        private static object AutoMaterialReason(BuildingDef def, int worldId)
        {
            if (!IsTechUnlocked(def))
                return "building_locked_by_research";
            if (!def.IsAvailable())
                return "building_not_available_in_current_context";
            var material = AvailableMaterials(def, worldId, includeUnavailable: false).FirstOrDefault();
            return material != null ? null : "no_currently_available_material";
        }

        private static List<string> BuildingCategories(BuildingDef def)
        {
            var categories = new List<string>();
            AddCategory(categories, ReadMemberString(def, "Category"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuCategory"));
            AddCategory(categories, ReadMemberString(def, "MenuCategory"));
            AddCategory(categories, ReadMemberString(def, "PlanScreenCategory"));
            AddCategory(categories, ReadMemberString(def, "Subcategory"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuSubcategory"));
            AddCategory(categories, ReadMemberString(def, "TechCategory"));

            if (def.MaterialCategory != null)
                foreach (var category in def.MaterialCategory)
                    AddCategory(categories, category);

            return categories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        }

        private static string ReadMemberString(BuildingDef def, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = def.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null)
                return MemberValueToString(property.GetValue(def, null));

            var field = type.GetField(name, flags);
            return field == null ? null : MemberValueToString(field.GetValue(def));
        }

        private static string MemberValueToString(object value)
        {
            if (value == null)
                return null;
            var tag = value as Tag?;
            if (tag.HasValue)
                return tag.Value.Name;
            return value.ToString();
        }

        private static void AddCategory(List<string> categories, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        private static List<Dictionary<string, object>> BuildingFacades(BuildingDef def)
        {
            var facades = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "DEFAULT_FACADE",
                    ["name"] = "Default",
                    ["unlocked"] = true,
                    ["default"] = true
                }
            };

            if (def.AvailableFacades == null)
                return facades;

            foreach (var facadeId in def.AvailableFacades)
            {
                var permit = Db.Get().Permits.TryGet(facadeId);
                bool unlocked = permit != null && permit.IsUnlocked();
                if (!unlocked)
                    continue;

                var facade = Db.GetBuildingFacades().TryGet(facadeId);
                facades.Add(new Dictionary<string, object>
                {
                    ["id"] = facadeId,
                    ["name"] = facade != null ? ToolUtil.CleanName(facade.Name) : facadeId,
                    ["unlocked"] = true,
                    ["default"] = false
                });
            }

            return facades;
        }

    }
}
