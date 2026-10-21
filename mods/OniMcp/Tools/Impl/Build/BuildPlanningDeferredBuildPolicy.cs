namespace OniMcp.Tools
{
    internal static class BuildPlanningDeferredBuildPolicy
    {
        internal static bool HasPendingOrders(int marked, int alreadyMarked,
            int uprootMarked, int alreadyUprootMarked)
        {
            return marked > 0 || alreadyMarked > 0 || uprootMarked > 0 || alreadyUprootMarked > 0;
        }

        internal static bool AllTargetsQueued(int targetCount, int marked, int alreadyMarked,
            int uprootMarked, int alreadyUprootMarked, int failed, int skipped, bool limitReached)
        {
            int accepted = marked + alreadyMarked + uprootMarked + alreadyUprootMarked;
            return targetCount > 0 && accepted >= targetCount && failed == 0 && skipped == 0 && !limitReached;
        }

        internal static string CompletionStatus(bool planned, bool alreadyPresent, bool stillPending)
        {
            if (planned)
                return "blueprint_placed";
            if (alreadyPresent)
                return "already_present";
            if (stillPending)
                return "pending";
            return "failed";
        }

        internal static bool CanReuseHandle(bool exists, bool sameGame, bool sameRootWorld,
            bool sameWatchedWorld, bool sameWorldId, bool sameAnchor, bool samePrefab, bool sameOptions)
        {
            return exists && sameGame && sameRootWorld && sameWatchedWorld && sameWorldId
                && sameAnchor && samePrefab && sameOptions;
        }

        internal static bool CanCancel(bool isPending, bool dryRun, bool confirm)
        {
            return isPending && !dryRun && confirm;
        }

        internal static bool IsCancelPreview(bool dryRun)
        {
            return dryRun;
        }

        internal static bool IsUnfinished(string status)
        {
            return string.Equals(status, "pending", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "blueprint_placed", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
