using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal sealed class ConstructionRecipeSlot
    {
        public readonly int Index;
        public readonly float RequiredKg;
        public readonly string Category;

        public ConstructionRecipeSlot(int index, float requiredKg, string category)
        {
            Index = index;
            RequiredKg = requiredKg;
            Category = category;
        }
    }

    internal sealed class ConstructionRecipe
    {
        public readonly IReadOnlyList<ConstructionRecipeSlot> Slots;
        public readonly bool Known;
        public readonly float TotalMassKg;
        public readonly string UnknownReason;

        public ConstructionRecipe(IReadOnlyList<ConstructionRecipeSlot> slots, bool known, float totalMassKg, string unknownReason)
        {
            Slots = slots;
            Known = known;
            TotalMassKg = totalMassKg;
            UnknownReason = unknownReason;
        }
    }

    // Adapter over the native BuildingDef fields. Mass[i] and MaterialCategory[i]
    // describe the same construction material slot; do not infer from MassTier.
    internal static class ConstructionRecipeAdapter
    {
        public static ConstructionRecipe Read(float[] masses, string[] categories)
        {
            if (masses == null || categories == null || masses.Length == 0 || categories.Length != masses.Length)
                return new ConstructionRecipe(new List<ConstructionRecipeSlot>(), false, 0f, "Native BuildingDef.Mass and MaterialCategory slots are missing or mismatched.");

            var slots = new List<ConstructionRecipeSlot>(masses.Length);
            float total = 0f;
            for (int i = 0; i < masses.Length; i++)
            {
                float rawMass = masses[i];
                if (float.IsNaN(rawMass) || float.IsInfinity(rawMass) || rawMass <= 0f || string.IsNullOrWhiteSpace(categories[i]))
                    return new ConstructionRecipe(slots, false, 0f, "Native construction recipe contains an invalid material slot.");
                // BuildingDef.PostProcess creates each native Recipe.Ingredient with
                // (int)Mass[i], which truncates the field before the game uses it.
                float mass = (int)rawMass;
                if (mass <= 0f)
                    return new ConstructionRecipe(slots, false, 0f, "Native construction slot mass truncates to zero kilograms.");
                slots.Add(new ConstructionRecipeSlot(i, mass, categories[i]));
                total += mass;
            }

            return new ConstructionRecipe(slots, true, total, null);
        }

        public static List<T> SelectAvailable<T>(
            ConstructionRecipe recipe,
            IEnumerable<T> candidates,
            Func<T, string, bool> matchesSlot,
            Func<T, bool> isAvailable,
            Func<T, float> availableKg) where T : class
        {
            if (recipe == null || !recipe.Known)
                return null;

            var source = (candidates ?? Enumerable.Empty<T>()).ToList();
            var selected = new List<T>(recipe.Slots.Count);
            foreach (var slot in recipe.Slots)
            {
                var match = source.Where(item => matchesSlot(item, slot.Category) && isAvailable(item))
                    .OrderByDescending(availableKg)
                    .FirstOrDefault();
                if (match == null)
                    return null;
                selected.Add(match);
            }
            return selected;
        }
    }
}
