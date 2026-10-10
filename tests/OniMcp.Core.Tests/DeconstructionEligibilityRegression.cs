using System;
using System.Reflection;
using OniMcp.Tools;

internal static class DeconstructionEligibilityRegressionEntry
{
    private static void Main()
    {
        RunRegression();

        MethodInfo existing = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing core regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunRegression()
    {
        AssertAllowed(
            DeconstructionEligibilityPolicy.Evaluate(true, true, false, false),
            "ordinary allowed Deconstructable must remain eligible");
        AssertRejected(
            DeconstructionEligibilityPolicy.Evaluate(true, false, false, false),
            "deconstruction_disabled",
            "protected Deconstructable must be rejected in normal mode");
        AssertAllowed(
            DeconstructionEligibilityPolicy.Evaluate(true, false, true, false),
            "InstantBuild must preserve the existing protected-target bypass");
        AssertAllowed(
            DeconstructionEligibilityPolicy.Evaluate(false, false, false, true),
            "supported utility target must remain eligible");
        AssertRejected(
            DeconstructionEligibilityPolicy.Evaluate(false, false, false, false),
            "not_deconstructable",
            "unrelated explicit-id target must not be previewed as queueable");
        AssertRejected(
            DeconstructionEligibilityPolicy.Evaluate(true, false, false, true),
            "deconstruction_disabled",
            "Deconstructable eligibility must keep precedence over utility fallback");
    }

    private static void AssertAllowed(DeconstructionEligibility result, string message)
    {
        if (!result.CanQueue || result.ReasonCode != null || result.Error != null)
            throw new InvalidOperationException(message);
    }

    private static void AssertRejected(DeconstructionEligibility result, string reasonCode, string message)
    {
        if (result.CanQueue || !string.Equals(result.ReasonCode, reasonCode, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(result.Error))
        {
            throw new InvalidOperationException(message);
        }
    }
}
