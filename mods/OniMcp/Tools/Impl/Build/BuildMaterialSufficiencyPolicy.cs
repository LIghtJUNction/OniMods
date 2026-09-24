using System;

namespace OniMcp.Tools
{
    internal static class BuildMaterialSufficiencyPolicy
    {
        internal static bool IsSatisfied(float selectedAvailableKg, float requiredKg, bool freeBuildContext)
        {
            if (freeBuildContext || requiredKg <= 0f)
                return true;

            if (float.IsNaN(requiredKg) || float.IsInfinity(requiredKg)
                || float.IsNaN(selectedAvailableKg) || float.IsInfinity(selectedAvailableKg))
                return false;

            return Math.Max(0f, selectedAvailableKg) >= requiredKg;
        }
    }
}
