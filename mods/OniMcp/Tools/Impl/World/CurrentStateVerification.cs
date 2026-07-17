using Newtonsoft.Json.Linq;
using System;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        private static JObject BuildStabilityWorkflow()
        {
            return new JObject
            {
                ["goal"] = "Keep normal play low-token and avoid broad/high-frequency reads unless a failure appears.",
                ["normalLoop"] = new JArray
                {
                    "Use read_control domain=state action=current with default minimal profile.",
                    "Use one local zoom or one cell detail before editing; avoid scanning broad maps repeatedly.",
                    "After write actions, verify compact result first, then one returned cell or local zoom."
                },
                ["readLogsWhen"] = new JArray
                {
                    "tool result reports exception/error/unsafe diagnostic",
                    "tester reports crash, connection reset, or game stopped responding",
                    "build/order action returns inconsistent state after compact verification"
                },
                ["logRead"] = new JObject
                {
                    ["tool"] = "world_editor",
                    ["arguments"] = new JObject
                    {
                        ["command"] = "read",
                        ["path"] = "/active/diagnostics/logs.md",
                        ["logLimit"] = 220
                    },
                    ["why"] = "Tail-only stability check; do this on failures, not every normal loop."
                },
                ["avoid"] = new JArray
                {
                    "Do not call knowledge/database reads during live game testing unless explicitly needed.",
                    "Do not issue huge batch edits in one frame; prefer template/batched summaries and compact verification.",
                    "Do not retry Bilibili sends or MCP writes in a tight loop after an error."
                }
            };
        }

        private static JObject StarterVerificationAfterCall()
        {
            return new JObject
            {
                ["preferred"] = "Use room_template response.verificationPlan first; it already contains exact post-build reads.",
                ["publicBatchShape"] = new JObject
                {
                    ["tool"] = "server_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "batch",
                        ["action"] = "call_many",
                        ["responseMode"] = "summary",
                        ["maxTextChars"] = 1200,
                        ["calls"] = "copy response.verificationPlan items here"
                    }
                },
                ["mustCheck"] = new JArray
                {
                    "verify_outhouse_cell",
                    "verify_wash_basin_cell",
                    "verify_research_station_cell",
                    "debris_followup when returned"
                },
                ["onlyOnFailure"] = new JObject
                {
                    ["tool"] = "world_editor",
                    ["arguments"] = new JObject
                    {
                        ["command"] = "read",
                        ["path"] = "/active/diagnostics/logs.md",
                        ["logLimit"] = 220
                    },
                    ["why"] = "Logs are expensive/noisy; read them only when result reports error, crash, or tester failure."
                },
                ["avoid"] = "Do not verify by reading broad maps; use returned cell paths, then one compact zoom only if a core cell looks wrong."
            };
        }

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
