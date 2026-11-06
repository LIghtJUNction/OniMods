using System;
using OniMcp.Tools;

internal static class ConstructionRecipeAdapterRegression
{
    public static void Run()
    {
        var building = new BuildingDef
        {
            // This mirrors the native Assembly-CSharp BuildingDef fields: a float[]
            // Mass and a parallel string[] MaterialCategory.
            Mass = new[] { 100f, 200f },
            MaterialCategory = new[] { "BuildableRaw", "RefinedMetal" },
            MassTier = 2f
        };

        var recipe = ConstructionRecipeAdapter.Read(building.Mass, building.MaterialCategory);
        Expect(recipe.Known && recipe.Slots.Count == 2, "native parallel slots should be recognized");
        Expect(recipe.TotalMassKg == 300f, "recipe total should sum native Mass slots, independent of MassTier");
        Expect(recipe.Slots[0].RequiredKg == 100f && recipe.Slots[0].Category == "BuildableRaw", "first category maps to first mass slot");
        Expect(recipe.Slots[1].RequiredKg == 200f && recipe.Slots[1].Category == "RefinedMetal", "second category maps to second mass slot");
        Expect(!ConstructionRecipeAdapter.Read(new[] { 200f }, new[] { "" }).Known, "empty native category must keep recipe unknown");
        Expect(!ConstructionRecipeAdapter.Read(new[] { 200f }, new[] { "BuildableRaw", "RefinedMetal" }).Known, "mismatched slot arrays must keep recipe unknown");
        var noStock = ConstructionRecipeAdapter.SelectAvailable(
            recipe,
            new[] { new Candidate("BuildableRaw", 0f), new Candidate("RefinedMetal", 0f) },
            (candidate, category) => candidate.Category == category,
            candidate => candidate.AvailableKg > 0f,
            candidate => candidate.AvailableKg);
        Expect(noStock == null, "default auto selection must not claim a no-stock material is usable");
        var stock = ConstructionRecipeAdapter.SelectAvailable(
            recipe,
            new[] { new Candidate("BuildableRaw", 150f), new Candidate("RefinedMetal", 250f) },
            (candidate, category) => candidate.Category == category,
            candidate => candidate.AvailableKg > 0f,
            candidate => candidate.AvailableKg);
        Expect(stock != null && stock.Count == 2 && stock[0].Category == "BuildableRaw" && stock[1].Category == "RefinedMetal",
            "default selection should map available candidates to each native material slot");
        Console.WriteLine("Construction recipe adapter regressions passed (native BuildingDef mass/category slots)");
    }

    private sealed class Candidate
    {
        public readonly string Category;
        public readonly float AvailableKg;

        public Candidate(string category, float availableKg)
        {
            Category = category;
            AvailableKg = availableKg;
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Construction recipe adapter regression failed: " + message);
    }
}
