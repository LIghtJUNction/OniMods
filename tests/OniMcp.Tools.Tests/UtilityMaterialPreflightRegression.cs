using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class UtilityMaterialPreflightRegression
{
    private static int assertions;

    internal static void Run()
    {
        var inventory = BuildMaterialSufficiencyRegression.Reset();
        var single = BuildMaterialSufficiencyRegression.Recipe(new[] { 50f }, new[] { "BuildableRaw" });
        single.WidthInCells = single.HeightInCells = 1;
        foreach (string kind in new[] { "building", "blueprint", "utility" })
        {
            BuildPlanningTools.ExistingMaterialsForTest.Clear();
            AddExisting(kind, 1, 1, "SandStone");
            foreach (string material in new[] { "SandStone", "auto", "default", "BuildableRaw" })
            {
                var result = BuildPlanningTools.TestExistingMaterialReuse(single, 1, 1, material);
                Check(result != null && (bool)result["valid"] && !(bool)result["blueprintPlaced"],
                    kind + " with zero inventory must be reused for " + material);
            }
            Check(BuildPlanningTools.TestExistingMaterialReuse(single, 1, 1, "CopperOre") == null,
                "different explicit material is not an idempotent retry");
            Check(BuildPlanningTools.TestExistingMaterialReuse(single, 1, 1, "SandStone", Orientation.R90) == null
                && BuildPlanningTools.TestExistingMaterialReuse(single, 2, 1, "SandStone") == null,
                "a different native placement identity must not borrow the reuse exemption");
            var reused = BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1), "SandStone", out var selection);
            Check(reused == null && selection == null, "fully reused path requires no material selection or new mass");
        }

        BuildPlanningTools.ExistingMaterialsForTest.Clear();
        inventory.Local["SandStone"] = 75f;
        inventory.Related["SandStone"] = 1000f;
        var shortage = BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1, 2), "SandStone", out var aggregate);
        Check(shortage != null && (string)shortage["reasonCode"] == "insufficient_material"
            && !(bool)shortage["committed"] && (int)shortage["planned"] == 0,
            "one segment's stock must reject the entire two-cell path before placement");
        Check(Convert.ToSingle(aggregate["requiredKg"]) == 100f && Equals(aggregate["satisfied"], false),
            "the response must report the aggregate new-cell demand");
        var powerFailure = BuildPlanningTools.TestPowerConnectionFailure(single, shortage);
        Check((string)powerFailure["status"] == "connection_failed" && (int)powerFailure["failed"] == 1
            && (int)powerFailure["planned"] == 0,
            "the real power auto-connect wrapper must propagate a material preflight rejection");
        var errorWithoutCount = new Dictionary<string, object>(shortage);
        errorWithoutCount.Remove("failed");
        Check((string)BuildPlanningTools.TestPowerConnectionFailure(single, errorWithoutCount)["status"] == "connection_failed",
            "an error response must never become connected_or_planned when failed is absent");
        inventory.Local["SandStone"] = 100f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1, 2, 2), "SandStone", out aggregate) == null
            && Convert.ToSingle(aggregate["requiredKg"]) == 100f,
            "exact mass covers all distinct new cells; repeated path cells are counted once");

        AddExisting("building", 1, 1, "SandStone");
        inventory.Local["SandStone"] = 50f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1, 2), "SandStone", out aggregate) == null
            && Convert.ToSingle(aggregate["requiredKg"]) == 50f,
            "existing matched buildings do not contribute new-cell demand");
        BuildPlanningTools.ExistingMaterialsForTest.Clear();
        AddExisting("utility", 1, 1, "SandStone");
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1, 2), "auto", out aggregate) == null
            && Convert.ToSingle(aggregate["requiredKg"]) == 50f,
            "auto reuse of utility cells also subtracts their material demand");
        BuildPlanningTools.ExistingMaterialsForTest.Clear();
        AddExisting("utility", 2, 1, "CopperOre");
        Check((string)BuildPlanningTools.TestUtilityMaterialPreflight(single, Path(1, 2), "SandStone", out aggregate)["reasonCode"]
            == "existing_material_mismatch", "material mismatch later in the path must reject before any new orders");

        BuildPlanningTools.ExistingMaterialsForTest.Clear();
        var mixed = BuildMaterialSufficiencyRegression.Recipe(new[] { 50f, 25f }, new[] { "BuildableRaw", "RefinedMetal" });
        inventory.Local["SandStone"] = 100f;
        inventory.Local["CopperOre"] = 49f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(mixed, Path(1, 2), "auto", out aggregate) != null,
            "every material slot must cover the entire path");
        inventory.Local["CopperOre"] = 50f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(mixed, Path(1, 2), "auto", out aggregate) == null,
            "separate material slots pass when each aggregated demand is met");
        var repeated = BuildMaterialSufficiencyRegression.Recipe(new[] { 50f, 25f }, new[] { "BuildableRaw", "BuildableRaw" });
        inventory.Local["SandStone"] = 149f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(repeated, Path(1, 2), "SandStone", out aggregate) != null,
            "a repeated material across recipe slots must also multiply by new-cell count");
        inventory.Local["SandStone"] = 150f;
        Check(BuildPlanningTools.TestUtilityMaterialPreflight(repeated, Path(1, 2), "SandStone", out aggregate) == null,
            "exact repeated-slot path demand remains executable");

        var invalid = BuildMaterialSufficiencyRegression.Recipe(new[] { float.NaN }, new[] { "BuildableRaw" });
        Check((string)BuildPlanningTools.TestUtilityMaterialPreflight(invalid, Path(1, 2), "SandStone", out aggregate)["reasonCode"]
            == "invalid_material_requirement" && Equals(aggregate["satisfied"], false),
            "invalid recipe demand must fail closed for the entire path");
        BuildPlanningTools.ExistingMaterialsForTest.Clear();
        Console.WriteLine("Utility material production preflight checks passed: " + assertions);
    }

    private static List<BuildPlanningTools.CellCoord> Path(params int[] xValues)
    {
        var path = new List<BuildPlanningTools.CellCoord>();
        foreach (int x in xValues) path.Add(new BuildPlanningTools.CellCoord(x, 1));
        return path;
    }

    private static void AddExisting(string kind, int x, int y, string material)
        => BuildPlanningTools.ExistingMaterialsForTest.Add(new BuildPlanningTools.ExistingMaterialFixture
        { Kind = kind, X = x, Y = y, PrefabId = "Wire", Material = material });

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Utility material preflight: " + message);
    }
}
