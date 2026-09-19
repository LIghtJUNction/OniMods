using System;

namespace OniMcp.Tools
{
    internal static class BuildMaterialSufficiencyPolicy
    {
        internal static bool IsSatisfied(float selectedAvailableKg, float requiredKg, bool freeBuildContext)
        {
            // Compatibility seam for the current behavior. The regression added with this
            // extraction demonstrates that a positive-but-insufficient amount is still
            // treated as executable until the policy is fixed.
            return true;
        }
    }
}
