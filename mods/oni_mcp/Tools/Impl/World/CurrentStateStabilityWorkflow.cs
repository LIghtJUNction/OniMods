using Newtonsoft.Json.Linq;

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
    }
}
