using System;
using OniMcp.Tools;

internal static class BuildMaterialSufficiencyRegression
{
    internal static void Run()
    {
        var def = new BuildingDef { Mass = new[] { 50f, 25f } };
        float required = BuildMaterialSufficiencyPolicy.RequiredPrimaryMass(def);
        Check(required == 50f, "material demand must use the actual primary recipe mass");
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(49.999f, required),
            "normal build material below the required mass must be rejected");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(50f, required),
            "exactly the required build mass must remain executable");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(75f, required),
            "material above the required build mass must remain executable");
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(75f, required * 2),
            "enough mass for one segment cannot authorize two new path cells");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(100f, required * 2),
            "exact aggregate path mass must remain executable");
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(0f, required),
            "known mass requirements cannot be bypassed by empty inventory");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(0f, 0f),
            "unknown/non-positive material requirements must not invent a blocker");
        Check(BuildMaterialSufficiencyPolicy.IsSatisfied(0f, -1f),
            "negative unknown requirements remain non-blocking");
        Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(-1f, required),
            "negative inventory cannot satisfy positive mass");
        Check(BuildMaterialSufficiencyPolicy.RequiredPrimaryMass(null) == 0f
            && BuildMaterialSufficiencyPolicy.RequiredPrimaryMass(new BuildingDef()) == 0f
            && BuildMaterialSufficiencyPolicy.RequiredPrimaryMass(new BuildingDef { Mass = new float[0] }) == 0f,
            "missing recipe metadata remains an unknown zero requirement");

        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            float invalidRequired = BuildMaterialSufficiencyPolicy.RequiredPrimaryMass(new BuildingDef { Mass = new[] { invalid } });
            Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(100f, invalidRequired),
                "non-finite recipe mass must not be sanitized into a valid zero requirement");
            Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(invalid, required),
                "non-finite inventory must not authorize placement");
            Check(!BuildMaterialSufficiencyPolicy.IsSatisfied(invalid, 0f),
                "non-finite inventory remains invalid with unknown recipe metadata");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
