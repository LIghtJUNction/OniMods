namespace OniMcp.Tools
{
    internal static class SandboxDryRunRoutingPolicy
    {
        internal static bool TryRejectUnsupported(string kind, string action, bool dryRun, out string error)
        {
            error = null;
            if (!dryRun)
                return false;

            kind = Normalize(kind);
            action = Normalize(action);

            if (kind == "read" || kind == "info")
                return false;
            if (kind == "map_designate" || kind == "designate" || kind == "search_designate" || kind == "map")
                return false;
            if (kind == "area" && action == "flood_fill")
                return false;

            if (string.IsNullOrEmpty(kind))
            {
                if (!IsSandboxModeAction(action))
                    return false;
                error = UnsupportedMessage("state", action);
                return true;
            }

            if (kind == "area" || kind == "entity" || kind == "entities")
            {
                error = UnsupportedMessage(kind, action);
                return true;
            }

            return false;
        }

        private static bool IsSandboxModeAction(string action)
        {
            return action == "set_sandbox_mode"
                || action == "sandbox_mode"
                || action == "sandbox_toggle"
                || action == "sandbox";
        }

        private static string UnsupportedMessage(string kind, string action)
        {
            string route = string.IsNullOrEmpty(action) ? kind : kind + "/" + action;
            return "dryRun=true is not supported for sandbox " + route
                + "; refusing to execute a route that can mutate the game. "
                + "Supported sandbox previews are kind=map_designate and kind=area action=flood_fill.";
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
