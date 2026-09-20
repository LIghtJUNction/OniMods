using System;
using OniMcp.Tools;

internal static class PriorityWriteEligibilityRegression
{
    public static void Run()
    {
        string error;
        if (!PriorityWriteEligibilityPolicy.TryValidate(true, out error) || error != null)
            throw new InvalidOperationException("active priority target should remain writable");

        if (PriorityWriteEligibilityPolicy.TryValidate(false, out error)
            || !string.Equals(error, "Target is not currently prioritizable", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "inactive priority target must be rejected before single-target writes");
        }
    }
}
