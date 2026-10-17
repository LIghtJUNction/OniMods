using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult HandleWorldEditorScoped(JObject rawArgs)
        {
            return RunWithNormalGameplayScope(rawArgs, () => HandleWorldEditorCommand(rawArgs ?? new JObject()));
        }

        private static CallToolResult RunWithNormalGameplayScope(JObject args, Func<CallToolResult> action)
        {
            if (!GameplayRequestPolicy.Validate(args, out string error))
                return CallToolResult.Error(error);
            bool previous = DebugHandler.InstantBuildMode;
            try
            {
                // A player's global debug setting must not turn an MCP blueprint into a completed building.
                DebugHandler.InstantBuildMode = false;
                return action();
            }
            finally
            {
                DebugHandler.InstantBuildMode = previous;
            }
        }
    }
}
