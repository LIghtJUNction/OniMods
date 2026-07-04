using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        private static JToken ReadReachabilityIfRequested(JObject args)
        {
            if (!ToolUtil.GetBool(args, "includeReachability", false)
                && !ToolUtil.GetBool(args, "includeReachableArea", false))
                return null;

            int radius = Math.Max(1, Math.Min(
                ToolUtil.GetInt(args, "reachabilityRadius")
                ?? ToolUtil.GetInt(args, "radius")
                ?? 12,
                80));
            int sampleLimit = Math.Max(0, Math.Min(
                ToolUtil.GetInt(args, "reachabilitySampleLimit")
                ?? ToolUtil.GetInt(args, "sampleLimit")
                ?? 12,
                80));

            var reachabilityArgs = new JObject
            {
                ["domain"] = "world",
                ["action"] = "reachable_area",
                ["radius"] = radius,
                ["sampleLimit"] = sampleLimit,
                ["includeSamples"] = ToolUtil.GetBool(args, "includeReachabilitySamples", true)
            };
            CopyOptional(args, reachabilityArgs, "name");
            CopyOptional(args, reachabilityArgs, "query");
            CopyOptional(args, reachabilityArgs, "target");
            CopyOptional(args, reachabilityArgs, "id");
            CopyOptional(args, reachabilityArgs, "worldId");

            CallToolResult result = WorldAnalysisTools.GetWorldReachableArea().Handler(reachabilityArgs);
            string text = result.Content?.Count > 0 ? result.Content[0].Text ?? string.Empty : string.Empty;
            if (result.IsError)
                return new JObject { ["ok"] = false, ["error"] = text };

            return TryParseJson(text) ?? text;
        }

        private static void CopyOptional(JObject source, JObject target, string key)
        {
            if (source?[key] != null)
                target[key] = source[key].DeepClone();
        }
    }
}
