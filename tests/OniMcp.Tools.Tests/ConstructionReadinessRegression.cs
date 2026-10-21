using System;
using OniMcp.Tools;

internal static class ConstructionReadinessRegression
{
    internal static void Run()
    {
        MaterialReadiness("sufficient", 0f,
            ConstructionReadinessPolicy.Material(100f, 433f, 0f, true, true),
            "reachable local stock covers recipe");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.Material(100f, 0f, 433f, true, true),
            "global inventory context cannot substitute for local reachable stock");
        MaterialReadiness("shortage", 100f,
            ConstructionReadinessPolicy.Material(100f, 0f, 0f, true, true),
            "observed empty local and world stock is a shortage");
        float foreignKg = ConstructionReadinessPolicy.ForeignInventoryContext(433f, 0f);
        float localUnknownKg = ConstructionReadinessPolicy.LocalUnverifiedStock(0f, 0f, 0f, 0f);
        Equal(433f, foreignKg, "foreign inventory stays context only");
        MaterialReadiness("shortage", 200f,
            ConstructionReadinessPolicy.Material(200f, 0f, localUnknownKg, true, true),
            "foreign 433 kg does not hide local shortage");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.Material(200f, 0f,
                ConstructionReadinessPolicy.LocalUnverifiedStock(0f, 100f, 0f, 0f), true, true),
            "local but unreachable loose stock remains unverified");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.Material(100f, 0f, 200f, true, true),
            "stored stock is not assumed fetchable");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.Material(100f, 100f, 0f, false, true),
            "no living navigator leaves local availability unknown");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.Material(0f, 100f, 0f, true, true),
            "unknown recipe requirement stays unknown");
        MaterialReadiness("shortage", 1300f,
            ConstructionReadinessPolicy.BatchMaterial(200f, 10, 700f, 0f, true, true),
            "batch demand sums recipe requirement while counting available stock once");
        MaterialReadiness("unknown", null,
            ConstructionReadinessPolicy.BatchMaterial(200f, 10, 700f, 500f, true, true),
            "batch with unverified local stock does not claim a definite shortage");

        Equal("reachable", ConstructionReadinessPolicy.WorkPosition(true, true, 4, 1), "reachable native approach");
        Equal("unreachable", ConstructionReadinessPolicy.WorkPosition(true, true, 4, 0), "all approach cells unreachable");
        Equal("unknown", ConstructionReadinessPolicy.WorkPosition(true, false, 4, 0), "no living navigator");
        Equal("unknown", ConstructionReadinessPolicy.WorkPosition(false, true, 0, 0), "missing native footprint");
        Console.WriteLine("Construction readiness regressions passed (local material and dynamic work-position states)");
    }

    private static void MaterialReadiness(string expectedStatus, float? expectedShortage, MaterialReadiness actual, string scenario)
    {
        Equal(expectedStatus, actual.Status, scenario + " status");
        if (expectedShortage.HasValue != actual.ShortageKg.HasValue
            || (expectedShortage.HasValue && Math.Abs(expectedShortage.Value - actual.ShortageKg.Value) > 0.001f))
            throw new InvalidOperationException(scenario + ": unexpected shortage");
    }

    private static void Equal(string expected, string actual, string scenario)
    {
        if (expected != actual)
            throw new InvalidOperationException(scenario + ": expected " + expected + ", got " + actual);
    }

    private static void Equal(float expected, float actual, string scenario)
    {
        if (Math.Abs(expected - actual) > 0.001f)
            throw new InvalidOperationException(scenario + ": expected " + expected + ", got " + actual);
    }
}
