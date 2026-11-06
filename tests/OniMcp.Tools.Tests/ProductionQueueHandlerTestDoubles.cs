using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The batch handler, routing, alias/default merge, planning and queue policy are
// production sources. Game registries and setters are observable host boundaries;
// these fixtures do not model native RefreshQueue scheduling or Unity execution.
internal partial class ComplexFabricator : KMonoBehaviour
{
    internal readonly List<ComplexRecipe> Recipes = new List<ComplexRecipe>();
    internal readonly Dictionary<ComplexRecipe, int> Counts = new Dictionary<ComplexRecipe, int>();
    internal readonly List<KeyValuePair<ComplexRecipe, int>> Writes = new List<KeyValuePair<ComplexRecipe, int>>();
    internal bool ForbidMutantSeeds { get; set; }
    internal ComplexRecipe CurrentWorkingOrder { get; set; }
    internal ComplexRecipe NextOrder { get; set; }

    internal ComplexRecipe[] GetRecipes() => Recipes.ToArray();
    internal ComplexRecipe GetRecipe(string id) => Recipes.FirstOrDefault(recipe => recipe.id == id);
    internal int GetRecipeQueueCount(ComplexRecipe recipe) => Counts[recipe];
    internal void SetRecipeQueueCount(ComplexRecipe recipe, int count)
    {
        Writes.Add(new KeyValuePair<ComplexRecipe, int>(recipe, count));
        Counts[recipe] = count;
    }
}

internal static partial class Components
{
    internal static class ComplexFabricators
    {
        internal static readonly List<ComplexFabricator> Items = new List<ComplexFabricator>();
    }
}

namespace OniMcp.Tools
{
    public static partial class ProductionTools
    {
        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters) => parameters;
        private static Dictionary<string, McpToolParameter> ProductionQueueControlParams() => new Dictionary<string, McpToolParameter>();
        private static McpTool ListFabricators() => throw new NotSupportedException();
        private static McpTool ListRecipes() => throw new NotSupportedException();
        private static McpTool SetQueue() => throw new NotSupportedException();

        // Read-side presentation is deliberately small; tests inspect actual
        // setter calls and queue state instead of relying on this projection.
        private static object QueueSummary(ComplexFabricator fabricator) => fabricator.GetRecipes()
            .Select(recipe => new { recipeId = recipe.id, count = FormatQueueCount(fabricator.GetRecipeQueueCount(recipe)) }).ToList();
        private static object FormatQueueCount(int count) => count == ComplexFabricator.QUEUE_INFINITE ? (object)"infinite" : count;
        private static string SafeRecipeName(ComplexRecipe recipe, bool includeAmounts) => recipe.id;
        private static object TargetInfo(GameObject go) => new { id = go.GetComponent<KPrefabID>()?.InstanceID };

        private static bool HasLookupInput(JObject args) => args["id"] != null || args["x"] != null || args["y"] != null;
        private static bool HasRectInput(JObject args) => args["x1"] != null || args["y1"] != null;
        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId) => throw new NotSupportedException();
        private static bool Contains(string value, string query) => value != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        private static string TargetName(GameObject go) => go.name;
        private static string PrefabId(GameObject go) => go.GetComponent<KPrefabID>()?.PrefabTag.Name;
        private static bool RecipeMatches(ComplexRecipe recipe, string query) => Contains(recipe.id, query);
    }

    internal static class SpecialUserMenuActionTools
    {
        internal static McpTool ListMutantSeedControls() => throw new NotSupportedException();
        internal static McpTool SetMutantSeedControl() => throw new NotSupportedException();
    }
}
