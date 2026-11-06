using System;
using System.Collections.Generic;
using OniMcp.Tools;
using UnityEngine;

internal static class BuildMaterialSufficiencyRegression
{
    private static int assertions;

    internal static void Run()
    {
        var inventory = Reset();
        var single = Recipe(new[] { 50f }, new[] { "BuildableRaw" });
        inventory.Local["SandStone"] = 49.999f;
        inventory.Related["SandStone"] = 500f;
        var shortage = Select(single, "SandStone");
        Check(!(bool)shortage["valid"], "related-world stock cannot authorize a locally short explicit placement");
        Check((string)shortage["reasonCode"] == "insufficient_material" && Equals(shortage["satisfied"], false),
            "a rejected shortage must be explicit in the serialized material result");
        Check(Convert.ToSingle(shortage["selectedLocalInventoryKg"]) < 50f
            && Convert.ToSingle(shortage["selectedForeignWorldInventoryKgContextOnly"]) > 450f,
            "foreign inventory remains context only");
        foreach (float amount in new[] { 50f, 75f })
        {
            inventory.Local["SandStone"] = amount;
            Check((bool)Select(single, "SandStone")["valid"], "exact and excess local mass must be accepted");
        }
        foreach (string material in new[] { "auto", "default", "BuildableRaw" })
        {
            inventory.Local["SandStone"] = 49.999f;
            var boundary = Select(single, material);
            Check(!(bool)boundary["valid"] && Equals(boundary["satisfied"], false)
                && (string)boundary["reasonCode"] == "insufficient_material",
                "rounded diagnostics and selection mode must not admit sub-required mass: " + material);
            inventory.Local["SandStone"] = 50f;
            Check((bool)Select(single, material)["valid"],
                "exact local mass must pass through the unified gate for " + material);
        }
        inventory.Local["SandStone"] = 0f;
        Check(!(bool)Select(single, "SandStone")["valid"], "foreign-only explicit stock must fail");
        Check(!(bool)Select(single, "auto")["valid"], "foreign-only auto stock must fail");

        inventory.Local["SandStone"] = 50f;
        var repeated = Recipe(new[] { 50f, 25f }, new[] { "BuildableRaw", "BuildableRaw" });
        Check(!(bool)Select(repeated, "SandStone")["valid"], "repeated material slots must count local stock once");
        inventory.Local["SandStone"] = 75f;
        Check((bool)Select(repeated, "SandStone")["valid"], "the complete repeated-slot mass must pass");

        var mixed = Recipe(new[] { 50f, 25f }, new[] { "BuildableRaw", "RefinedMetal" });
        inventory.Local["CopperOre"] = 24f;
        inventory.Related["CopperOre"] = 100f;
        Check(!(bool)Select(mixed, "auto")["valid"], "every native material slot must be supplied locally");
        inventory.Local["CopperOre"] = 25f;
        var complete = Select(mixed, "auto");
        Check((bool)complete["valid"] && ((List<Dictionary<string, object>>)complete["recipeSlots"]).Count == 2,
            "different material slots retain their native recipe identities");

        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            foreach (string material in new[] { "SandStone", "auto", "BuildableRaw" })
            {
                var result = Select(Recipe(new[] { invalid }, new[] { "BuildableRaw" }), material);
                Check(!(bool)result["valid"] && Equals(result["satisfied"], false)
                    && (string)result["reasonCode"] == "invalid_material_requirement",
                    "non-finite native demand must be invalid and unsatisfied for " + material);
                Check(result["requiredKg"] == null && (string)result["localAvailabilityStatus"] == "invalid",
                    "invalid demand must not look like an unknown executable requirement");
            }
        }
        VerifyInvalidStockAndAggregates();
        UtilityMaterialPreflightRegression.Run();
        Reset();
        Console.WriteLine("Build material production regression checks passed: " + assertions);
    }

    private static void VerifyInvalidStockAndAggregates()
    {
        var recipe = ConstructionRecipeAdapter.Read(new[] { 50f }, new[] { "BuildableRaw" });
        foreach (float stock in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(recipe, new[] { "SandStone" },
                new[] { stock }, 1, out string reason, out string error)
                && reason == "invalid_material_inventory" && !string.IsNullOrEmpty(error),
                "the final material gate must reject non-finite or negative stock");
        }
        foreach (int count in new[] { 0, -1 })
        {
            Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(recipe, new[] { "SandStone" },
                new[] { 100f }, count, out string reason, out string error)
                && reason == "invalid_material_requirement",
                "new placement demand must be positive");
        }
        var repeated = ConstructionRecipeAdapter.Read(new[] { 50f, 25f }, new[] { "BuildableRaw", "BuildableRaw" });
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(repeated, new[] { "SandStone", "SandStone" },
            new[] { 100f, 74.999f }, 1, out string repeatedReason, out string repeatedError)
            && repeatedReason == "insufficient_material",
            "disagreeing snapshots of one material pool must use the smaller available amount");
        var huge = new ConstructionRecipe(new[] { new ConstructionRecipeSlot(0, float.MaxValue, "BuildableRaw") },
            true, float.MaxValue, null);
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(huge, new[] { "SandStone" },
            new[] { float.MaxValue }, 2, out string overflowReason, out string overflowError)
            && overflowReason == "invalid_material_requirement",
            "overflowed aggregate demand must never authorize a placement");
    }

    internal static WorldInventory Reset()
    {
        BuildPlanningTools.ResetMaterialStockForTest();
        Components.Pickupables.Items.Clear();
        Components.LiveMinionIdentities.Items.Clear();
        ElementLoader.Elements.Clear();
        var raw = new Element();
        raw.Categories.Add(new Tag("BuildableRaw"));
        ElementLoader.Elements["SandStone"] = raw;
        var metal = new Element();
        metal.Categories.Add(new Tag("RefinedMetal"));
        ElementLoader.Elements["CopperOre"] = metal;
        var inventory = new WorldInventory();
        ClusterManager.Instance = new ClusterManager();
        ClusterManager.Instance.Worlds[0] = new TestCameraWorld { worldInventory = inventory };
        return inventory;
    }

    internal static BuildingDef Recipe(float[] mass, string[] categories)
        => new BuildingDef { PrefabID = "Wire", Name = "Wire", Mass = mass, MaterialCategory = categories };

    private static Dictionary<string, object> Select(BuildingDef def, string material)
    {
        Time.frameCount++;
        return BuildPlanningTools.TestMaterialSelection(def, material, 0);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Build material production regression: " + message);
    }
}
