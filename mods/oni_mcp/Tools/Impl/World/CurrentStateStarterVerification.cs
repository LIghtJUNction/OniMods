using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
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
    }
}
