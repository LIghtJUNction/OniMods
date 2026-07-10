using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class AgentProgramTools
    {
        private const string ToolName = "agent_program_execute";
        private const int DefaultMaxSteps = 100;
        private const int HardMaxSteps = 200;
        private const int DefaultMaxLoopIterations = 10;
        private const int HardMaxLoopIterations = 25;
        private const int MaxChildToolCalls = 50;
        private const int MaxStoredResultChars = 4000;

        public static McpTool ExecuteProgram()
        {
            return new McpTool
            {
                Name = ToolName,
                Group = "tools",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "agent_script_run", "agent_flow_execute", "agent_program_run" },
                Tags = new List<string> { "agent", "program", "script", "flow", "if", "while", "loop", "control", "脚本", "流程" },
                Description = "执行受限 agent 流程 DSL：支持变量、if/while/repeat、break/continue/return，并可按条件调用已注册 MCP 工具",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["program"] = new McpToolParameter
                    {
                        Type = "object",
                        Description = "程序对象或 JSON 字符串。steps 可使用 call、if、while、repeat、break、continue 和 return；call 通过 tool 与 args 调用已注册 MCP 工具。",
                        Required = true
                    },
                    ["dryRun"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "只做结构与工具名验证，不执行任何子调用，默认 false",
                        Required = false
                    },
                    ["maxSteps"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最大执行语句数，防止死循环，默认 100，最大 200",
                        Required = false
                    },
                    ["maxLoopIterations"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "单个 while/repeat 最大迭代次数，默认 10，最大 25",
                        Required = false
                    },
                    ["trace"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否返回详细执行 trace，默认 false；trace 中的子调用结果会被截断",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    JToken program;
                    string error;
                    if (!TryReadProgram(args["program"], out program, out error))
                        return CallToolResult.Error(error);

                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    int maxSteps = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxSteps") ?? DefaultMaxSteps, HardMaxSteps));
                    int maxLoopIterations = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxLoopIterations") ?? DefaultMaxLoopIterations, HardMaxLoopIterations));
                    bool includeTrace = ToolUtil.GetBool(args, "trace", false);

                    var runner = new AgentProgramRunner(dryRun, maxSteps, maxLoopIterations, includeTrace);
                    var payload = runner.Run(program);
                    string text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings);
                    bool ok = payload.ContainsKey("ok") && (bool)payload["ok"];
                    return ok ? CallToolResult.Text(text) : CallToolResult.Error(text);
                }
            };
        }

        private static bool TryReadProgram(JToken token, out JToken program, out string error)
        {
            program = null;
            error = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                error = "program is required";
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                try
                {
                    program = JToken.Parse(token.ToString());
                    return true;
                }
                catch (Exception ex)
                {
                    error = "program string must be valid JSON: " + ex.Message;
                    return false;
                }
            }

            if (token.Type != JTokenType.Object && token.Type != JTokenType.Array)
            {
                error = "program must be an object, array, or JSON string";
                return false;
            }

            program = token.DeepClone();
            return true;
        }
        private enum FlowSignal
        {
            None,
            Break,
            Continue,
            Return
        }

        private sealed class AgentProgramException : Exception
        {
            public AgentProgramException(string message) : base(message)
            {
            }
        }
    }
}
