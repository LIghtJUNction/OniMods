using System;
using OniMcp.Tools;

internal static class BuildMaterialSufficiencyRegression
{
    internal static void Run()
    {
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(49.999f, 50f, false),
            "normal build material below the required mass must be rejected");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(50f, 50f, false),
            "exactly the required build mass must remain executable");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(75f, 50f, false),
            "material above the required build mass must remain executable");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(0f, 50f, true),
            "free-build contexts must bypass inventory mass gating");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(0f, 0f, false),
            "unknown/non-positive material requirements must not invent a blocker");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
