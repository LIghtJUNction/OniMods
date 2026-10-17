using System;
using System.Collections.Generic;

// Only native API data and predicates are substituted. Production policies are
// linked directly by the project; this fixture is not an ONI runtime claim.
internal partial class Automatable { }
internal sealed class CustomAutomatable : Automatable { }

internal partial class ComplexRecipe
{
    internal string id { get; set; } = "recipe";
    internal bool Unlocked { get; set; }
    internal bool FailUnlockRead { get; set; }
    internal bool IsRequiredTechOrPOIUnlocked()
    {
        if (FailUnlockRead)
            throw new InvalidOperationException("recovery must not read obsolete unlock data");
        return Unlocked;
    }
}

internal partial class ComplexFabricator
{
    // Fixed native source 744825 declares 99 and -1 respectively.
    internal const int MAX_QUEUE_SIZE = 99;
    internal const int QUEUE_INFINITE = -1;
}

internal partial class BuildingDef
{
    internal Recipe CraftRecipe { get; set; }
    internal IList<Tag> Defaults { get; set; }
    internal IList<Tag> DefaultElements() => Defaults;
}

internal sealed class Recipe
{
    internal List<Ingredient> Ingredients { get; } = new List<Ingredient>();
    internal sealed class Ingredient
    {
        internal Tag tag;
        internal float amount;
    }
}

internal static class MaterialSelector
{
    internal static readonly Dictionary<Tag, List<Tag>> Categories = new Dictionary<Tag, List<Tag>>();
    internal static List<Tag> GetValidMaterials(Tag category)
    {
        List<Tag> materials;
        return Categories.TryGetValue(category, out materials) ? materials : new List<Tag>();
    }
}
