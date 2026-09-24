using System;
using OniMcp.Tools;

internal static class RadboltDirectionEligibilityRegression
{
    internal static void Run()
    {
        Check(!RadboltDirectionEligibilityPolicy.CanControl(false, false, false),
            "missing IHighEnergyParticleDirection must remain unavailable");
        Check(RadboltDirectionEligibilityPolicy.CanControl(true, false, false),
            "non-redirector direction implementations must remain controllable");
        Check(RadboltDirectionEligibilityPolicy.CanControl(true, true, true),
            "controllable redirectors must remain controllable");
        Check(!RadboltDirectionEligibilityPolicy.CanControl(true, true, false),
            "fixed redirectors must not expose or accept radbolt direction writes");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
