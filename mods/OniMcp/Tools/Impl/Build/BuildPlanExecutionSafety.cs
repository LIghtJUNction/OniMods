using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal const int MinimumAutoWriteBuildingScore = 700;
        internal const int MinimumAutoWriteBuildingGap = 80;
        internal const int MaximumPlanTermOrderBoost = 50;
        internal const int SuffixBuildingAliasScore = 600;

        internal static JObject BuildWriteResolutionArgs(JObject args)
        {
            var result = (JObject)args.DeepClone();
            result["limit"] = Math.Max(2, ToolUtil.GetInt(args, "limit") ?? 5);
            return result;
        }

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
