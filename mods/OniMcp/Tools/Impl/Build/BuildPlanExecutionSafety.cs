using System;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal const int MinimumAutoWriteBuildingScore = 700;
        internal const int MinimumAutoWriteBuildingGap = 80;

        internal static bool IsAutoWriteBuildingResolutionSafe(
            string matchKind,
            int topScore,
            int secondScore,
            out string reason)
        {
            if (string.Equals(matchKind, "alias", StringComparison.OrdinalIgnoreCase)
                || string.Equals(matchKind, "prefabId", StringComparison.OrdinalIgnoreCase))
            {
                reason = null;
                return true;
            }

            if (topScore < MinimumAutoWriteBuildingScore)
            {
                reason = "low_confidence";
                return false;
            }

            if (secondScore > 0 && topScore - secondScore < MinimumAutoWriteBuildingGap)
            {
                reason = "near_tie";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
