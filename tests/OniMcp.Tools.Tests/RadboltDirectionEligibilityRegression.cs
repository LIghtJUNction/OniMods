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

        foreach (EightDirection expected in Enum.GetValues(typeof(EightDirection)))
        {
            EightDirection actual;
            Check(RadboltDirectionEligibilityPolicy.TryParseDirection(expected.ToString(), out actual)
                && actual == expected, "each named direction must remain accepted");
            Check(RadboltDirectionEligibilityPolicy.TryParseDirection(expected.ToString().ToLowerInvariant(), out actual)
                && actual == expected, "named directions remain case-insensitive");
            Check(RadboltDirectionEligibilityPolicy.TryParseDirection(((int)expected).ToString(), out actual)
                && actual == expected, "defined numeric direction values remain compatible");
        }
        foreach (string invalid in new[] { "999", "-1", "8", "Up, Right", "", null })
        {
            EightDirection ignored;
            Check(!RadboltDirectionEligibilityPolicy.TryParseDirection(invalid, out ignored),
                "invalid or combined direction values must be rejected: " + invalid);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// Values from the pinned ONI EightDirection source contract, not a game runtime.
internal enum EightDirection
{
    Up,
    UpLeft,
    Left,
    DownLeft,
    Down,
    DownRight,
    Right,
    UpRight
}
