using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static string ExistingMaterialTag(Dictionary<string, object> existing)
        {
            object value;
            return existing != null
                && existing.TryGetValue("material", out value)
                && value != null
                ? value.ToString()
                : null;
        }

        private static bool ExistingMaterialRequestSatisfied(
            BuildingDef def,
            Dictionary<string, object> existing,
            string requested)
        {
            if (!BuildPlanningExistingMaterialPolicy.IsExplicitRequest(requested))
                return true;

            string existingTag = ExistingMaterialTag(existing);
            if (string.IsNullOrWhiteSpace(existingTag))
                return false;

            var materialTag = new Tag(existingTag);
            string existingName = materialTag.IsValid ? materialTag.ProperNameStripLink() : null;
            string request = requested.Trim();
            var requestedCategory = MaterialCategoryTags(def)
                .FirstOrDefault(category => EqualsIgnoreCase(category.Name, request));
            bool categoryMatches = requestedCategory.IsValid
                && materialTag.IsValid
                && MaterialMatchesCategory(materialTag, requestedCategory);

            return BuildPlanningExistingMaterialPolicy.RequestMatchesExisting(
                requested,
                existingTag,
                existingName,
                categoryMatches);
        }

        private static Dictionary<string, object> ExistingMaterialMismatchResult(
            string prefabId,
            int x,
            int y,
            Dictionary<string, object> existing,
            MaterialSelection materialResult,
            string requested)
        {
            if (!BuildPlanningExistingMaterialPolicy.IsExplicitRequest(requested))
                return null;

            string existingTag = ExistingMaterialTag(existing);
            Tag selected = materialResult != null && materialResult.Elements != null && materialResult.Elements.Count > 0
                ? materialResult.Elements[0]
                : Tag.Invalid;
            string selectedTag = selected.IsValid ? selected.Name : null;
            if (BuildPlanningExistingMaterialPolicy.SelectedMaterialMatchesExisting(selectedTag, existingTag))
                return null;

            string error = string.IsNullOrWhiteSpace(existingTag)
                ? "Existing placement material could not be verified for this explicit material request."
                : "Existing placement uses material '" + existingTag + "', not requested material '" + selectedTag + "'.";

            return ErrorResult(prefabId, x, y, error, new Dictionary<string, object>
            {
                ["reasonCode"] = "existing_material_mismatch",
                ["requestedMaterial"] = requested,
                ["resolvedMaterial"] = selectedTag,
                ["existingMaterial"] = existingTag,
                ["existing"] = existing,
                ["materialSelection"] = materialResult?.ToDictionary()
            });
        }
    }
}
