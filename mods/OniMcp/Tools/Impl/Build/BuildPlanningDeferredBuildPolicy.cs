using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class BuildPlanningDeferredBuildPolicy
    {
        internal static bool TryReadStatusBatch(JObject args, string action, out string[] ids, out string error)
        {
            ids = null;
            if (action != "status")
            {
                error = "pendingBuildIds supports status only. Cancellation requires a single pendingBuildId.";
                return false;
            }
            if (args["pendingBuildId"] != null)
            {
                error = "Use either pendingBuildId or pendingBuildIds.";
                return false;
            }
            var handles = args["pendingBuildIds"] as JArray;
            if (handles == null || handles.Count < 1 || handles.Count > 128)
            {
                error = "pendingBuildIds requires an array of 1 to 128 handle strings.";
                return false;
            }
            var parsed = new string[handles.Count];
            for (int i = 0; i < handles.Count; i++)
            {
                JToken handle = handles[i];
                if (handle.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)handle))
                {
                    error = "pendingBuildIds[" + i + "] must be a nonblank handle string.";
                    return false;
                }
                parsed[i] = (string)handle;
            }
            ids = parsed;
            error = null;
            return true;
        }

        internal static string RegisteredHandle(Dictionary<string, object> result)
        {
            if (!result.TryGetValue("pendingBuild", out object pending) || !(pending is bool accepted) || !accepted
                || !result.TryGetValue("pendingBuildId", out object handle))
                return null;
            string id = handle as string;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

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
