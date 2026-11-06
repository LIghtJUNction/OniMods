using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class BuildMaterialSufficiencyPolicy
    {
        internal static bool IsSatisfied(
            ConstructionRecipe recipe, IList<string> materials, IList<float> localAvailableKg,
            int newPlacements, out string reasonCode, out string error)
        {
            reasonCode = "invalid_material_requirement";
            error = "Native construction recipe is invalid or incomplete; cannot verify material demand.";
            if (newPlacements <= 0 || recipe == null || !recipe.Known || recipe.Slots.Count == 0
                || materials == null || localAvailableKg == null
                || materials.Count != recipe.Slots.Count || localAvailableKg.Count != recipe.Slots.Count)
                return false;

            var required = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var available = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < recipe.Slots.Count; i++)
            {
                string material = materials[i];
                float mass = recipe.Slots[i].RequiredKg * newPlacements;
                if (string.IsNullOrWhiteSpace(material) || !Finite(mass) || mass <= 0f)
                    return false;
                required.TryGetValue(material, out float previous);
                float total = previous + mass;
                if (!Finite(total))
                    return false;
                required[material] = total;

                float local = localAvailableKg[i];
                if (!Finite(local) || local < 0f)
                {
                    reasonCode = "invalid_material_inventory";
                    error = "Local inventory for '" + material + "' is invalid; cannot authorize placement.";
                    return false;
                }
                // One stock pool can satisfy repeated slots only once. If snapshots
                // disagree, the smaller amount is the safe bound for this request.
                available[material] = available.TryGetValue(material, out float current)
                    ? Math.Min(current, local) : local;
            }

            foreach (var demand in required)
            {
                if (available[demand.Key] >= demand.Value)
                    continue;
                reasonCode = "insufficient_material";
                error = "Insufficient local build material: '" + demand.Key + "' has "
                    + Math.Round(available[demand.Key], 3) + " kg but " + Math.Round(demand.Value, 3)
                    + " kg is required for " + newPlacements + " new placement(s).";
                return false;
            }
            reasonCode = null;
            error = null;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
