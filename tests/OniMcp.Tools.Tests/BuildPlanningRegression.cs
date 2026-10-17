using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class BuildPlanningRegression
{
    internal static void Run()
    {
        ExistingMaterialReaderRegression.Run();
        Expect("locked", "Building is locked by research: FlushToilet", new JArray());
        Expect("locked", "Building is not researched", new List<object>());
        Expect("unavailableMaterial", "No material available", new JArray());
        Expect("invalidFloor", "Invalid footprint", new List<object>());
        Expect("invalidFloor", "Invalid footprint: outside selected world", new List<object> { new { cell = 42 } });
        Expect("invalidFloor", "Invalid footprint: occupied cell is invalid", new JArray(new JObject { ["cell"] = 42 }));
        Expect("failed", "TryPlace failed", new JArray());
        Expect("failed", "Building is unlocked; placement failed", null);
        Expect("failed", "TryPlace failed", new JObject());
        Expect("failed", "TryPlace failed", false);
        Expect("obstructed", "TryPlace failed", new JArray(new JObject { ["cell"] = 42 }));
        Expect("obstructed", "TryPlace failed", new List<object> { new { cell = 42 } });
        Expect("unsupported", "Unsupported placement", new List<object> { 42 });
        var locked = new Dictionary<string, object> { ["unlocked"] = new JValue(false), ["obstructions"] = new JArray() };
        Equal("locked", BuildPlanningTools.TestFailure("TryPlace failed", locked), "structured research lock");
        locked["reasonCode"] = "invalidFloor";
        Equal("invalidFloor", BuildPlanningTools.TestFailure("Unsupported placement", locked), "explicit reason wins");
        Equal("failed", BuildPlanningTools.TestFailure(null, null), "missing diagnostics");

        var aliases = BuildPlanningTools.TestBuildingAliases();
        Alias(aliases, "冲水马桶", "FlushToilet");
        Alias(aliases, "FLUSHTOILET", "FlushToilet");
        Alias(aliases, "basin", "WashBasin");
        Alias(aliases, "sink", "WashSink");
        Alias(aliases, "种植箱", "PlanterBox");
        Alias(aliases, "farmtile", "FarmTile");
        Alias(aliases, "水培砖", "HydroponicFarm");
        if (aliases.ContainsKey("花盆") || aliases.ContainsKey("水池"))
            throw new InvalidOperationException("Ambiguous decoration/pool names must not select a farm or sink");

        Assets.BuildingDefs.Clear();
        foreach (string id in new[] { "FlushToilet", "WashBasin", "WashSink", "PlanterBox", "FarmTile", "HydroponicFarm" })
            Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = id, Name = id });
        foreach (var pair in new[] { new[] { " basin ", "WashBasin" }, new[] { "SINK", "WashSink" }, new[] { "flushtoilet", "FlushToilet" }, new[] { "farmtile", "FarmTile" } })
            Equal(pair[1], BuildPlanningTools.TestResolvePrefab(pair[0]), "prefab alias " + pair[0]);
        Equal(null, BuildPlanningTools.TestResolvePrefab("missing building"), "unknown prefab stays unresolved");
        Assets.BuildingDefs.Clear();
        Console.WriteLine("Build planning regressions passed (classification and building aliases)");
    }

    private static void Alias(Dictionary<string, string> aliases, string term, string expected)
    {
        string actual;
        if (!aliases.TryGetValue(term, out actual))
            throw new InvalidOperationException("Missing building alias: " + term);
        Equal(expected, actual, "building alias " + term);
    }

    private static void Expect(string expected, string error, object obstructions)
    {
        Equal(expected, BuildPlanningTools.TestFailure(error,
            new Dictionary<string, object> { ["obstructions"] = obstructions }), error);
    }

    private static void Equal(string expected, string actual, string scenario)
    {
        if (expected != actual)
            throw new InvalidOperationException(scenario + ": expected " + expected + ", got " + actual);
    }
}

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static string TestFailure(string error, Dictionary<string, object> details) => ClassifyBuildFailure(error, details);
        internal static Dictionary<string, string> TestBuildingAliases() => PlanBuildingAliases();
        internal static string TestResolvePrefab(string name)
        {
            string resolved, error;
            return ResolveBuildingDef(name, out resolved, out error)?.PrefabID;
        }

        // Unused game-dependent collaborators of the linked production helpers.
        private static Dictionary<string, object> GetObject(Dictionary<string, object> details, string key) => throw new NotSupportedException();
        private static string NormalizePlanText(string value) => throw new NotSupportedException();
        private sealed class PlanMaterialCandidate
        {
            public string Tag { get; set; }
            public string Name { get; set; }
            public int Score { get; set; }
            public string Matched { get; set; }
            public float AvailableKg { get; set; }
            public bool ValidForBuilding { get; set; }
        }
    }
}
