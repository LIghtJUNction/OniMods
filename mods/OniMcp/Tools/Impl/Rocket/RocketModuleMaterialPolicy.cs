using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class RocketModuleMaterialPolicy
    {
        internal static Dictionary<string, object> Resolve(BuildingDef def, string requested,
            Func<Tag, float> availableAmount, bool allowInsufficient, out IList<Tag> materials)
        {
            materials = string.IsNullOrWhiteSpace(requested)
                ? def.DefaultElements()
                : requested.Split(',').Select(value => new Tag(value.Trim())).ToList();
            var ingredients = def.CraftRecipe?.Ingredients;
            if (ingredients == null || materials == null || materials.Count != ingredients.Count)
                return Failure("invalid_material_count", "materials must contain exactly one tag per recipe slot");

            var required = new Dictionary<Tag, double>();
            for (int slot = 0; slot < ingredients.Count; slot++)
            {
                var material = materials[slot];
                var ingredient = ingredients[slot];
                if (!material.IsValid || !MaterialSelector.GetValidMaterials(ingredient.tag).Contains(material))
                {
                    var error = Failure("invalid_material_for_recipe", "material is not valid for this recipe slot");
                    error["slot"] = slot;
                    error["material"] = material.Name;
                    error["category"] = ingredient.tag.Name;
                    return error;
                }
                if (float.IsNaN(ingredient.amount) || float.IsInfinity(ingredient.amount) || ingredient.amount < 0)
                    return Failure("invalid_material_requirement", "recipe material requirement must be finite and nonnegative");
                double previous;
                required.TryGetValue(material, out previous);
                required[material] = previous + ingredient.amount;
            }

            if (!allowInsufficient)
            {
                foreach (var pair in required)
                {
                    float available = availableAmount(pair.Key);
                    if (float.IsNaN(available) || float.IsInfinity(available) || available < pair.Value)
                    {
                        var error = Failure("insufficient_material", "not enough material in the target world's inventory");
                        error["material"] = pair.Key.Name;
                        error["requiredKg"] = pair.Value;
                        error["availableKg"] = float.IsNaN(available) || float.IsInfinity(available)
                            ? (object)null : available;
                        return error;
                    }
                }
            }
            return null;
        }

        private static Dictionary<string, object> Failure(string code, string message)
        {
            return new Dictionary<string, object>
            {
                ["ok"] = false,
                ["reasonCode"] = code,
                ["error"] = message
            };
        }
    }
}
