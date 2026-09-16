using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class SandboxFloodFillExecution
    {
        internal static string Validate(bool dryRun, bool gameInitialized, bool confirm, bool sandboxModeActive, bool force)
        {
            if (!gameInitialized)
                return "Game not initialized";
            if (dryRun)
                return null;
            if (!confirm)
                return "confirm=true is required";
            if (!sandboxModeActive && !force)
                return "Sandbox mode is not active; set force=true to override";
            return null;
        }

        internal static int Apply(IReadOnlyList<int> cells, bool dryRun, Action<int> apply)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (dryRun)
                return 0;

            for (int i = 0; i < cells.Count; i++)
                apply(cells[i]);
            return cells.Count;
        }
    }
}
