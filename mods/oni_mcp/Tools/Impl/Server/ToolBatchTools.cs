using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolBatchTools
    {
        public const string ToolName = "tools_call_many";

        public static McpTool CallMany()
        {
            return new McpTool
            {
                Name = ToolName,
                Group = "tools",
                Mode = "execute",
                Risk = "medium",
                Description = "万能批量工具：按顺序一次调用多个 ONI MCP 工具，默认返回紧凑摘要；支持 dryRun 预检、重复调用预警、requireAllValid 全量有效才执行和默认参数合并",
                Tags = new List<string> { "batch", "multi", "universal", "aggregate", "万能", "批量" },
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["calls"] = new McpToolParameter
                    {
                        Type = "array",
                        Description = "要调用的工具数组，格式为 [{\"name\":\"tool_name\",\"arguments\":{...}}]，也兼容短字段 {t,a}；最多 20 个",
                        Required = false
                    },
                    ["items"] = new McpToolParameter
                    {
                        Type = "array",
                        Description = "calls 的别名，支持同样的数组格式；适合与 domain batch 工具保持一致",
                        Required = false
                    },
                    ["defaults"] = new McpToolParameter
                    {
                        Type = "object",
                        Description = "合并到每个子调用 arguments 的默认参数对象；子调用同名参数优先",
                        Required = false
                    },
                    ["defaultArguments"] = new McpToolParameter
                    {
                        Type = "object",
                        Description = "defaults 的别名",
                        Required = false
                    },
                    ["stopOnError"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "遇到错误后是否停止后续调用，默认 false",
                        Required = false
                    },
                    ["dryRun"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "只做结构、安全和工具存在性预检，不执行任何子调用，默认 false",
                        Required = false
                    },
                    ["requireAllValid"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "执行前要求所有子调用通过预检，默认 true；false 时逐项执行并返回错误项",
                        Required = false
                    },
                    ["responseMode"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "返回模式：summary=每项只返回状态和截断文本，full=完整内容，errors=只返回错误项；默认 summary",
                        Required = false,
                        EnumValues = new List<string> { "full", "summary", "errors" }
                    },
                    ["includeArguments"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否在每项结果中回显合并 defaults 后的 arguments，默认 false；调试参数合并时再开启",
                        Required = false
                    },
                    ["maxTextChars"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "summary/errors 模式下每项 text 最大字符数，默认 500，最大 4000",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    var callsToken = args["calls"] ?? args["items"];
                    if (callsToken == null || callsToken.Type != JTokenType.Array)
                        return CallToolResult.Error("calls/items array is required");

                    var calls = (JArray)callsToken;
                    if (calls.Count == 0)
                        return CallToolResult.Error("calls must contain at least one tool call");
                    if (calls.Count > 20)
                        return CallToolResult.Error("calls cannot contain more than 20 tool calls");

                    bool stopOnError = ToolUtil.GetBool(args, "stopOnError", false);
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    bool requireAllValid = ToolUtil.GetBool(args, "requireAllValid", true);
                    string responseMode = NormalizeResponseMode(args["responseMode"]?.ToString());
                    bool includeArguments = ToolUtil.GetBool(args, "includeArguments", false);
                    int maxTextChars = Math.Max(80, Math.Min(ToolUtil.GetInt(args, "maxTextChars") ?? 500, 4000));
                    var defaults = args["defaults"] as JObject ?? args["defaultArguments"] as JObject ?? new JObject();
                    var preflightWarnings = DuplicateCallWarnings(calls, defaults);
                    var preflight = PreflightCalls(calls, defaults, includeArguments, preflightWarnings);
                    bool preflightValid = preflight.All(item => !(item.ContainsKey("isError") && (bool)item["isError"]));

                    if (dryRun || (requireAllValid && !preflightValid))
                    {
                        var validationPayload = new Dictionary<string, object>
                        {
                            ["dryRun"] = dryRun,
                            ["valid"] = preflightValid,
                            ["requested"] = calls.Count,
                            ["executed"] = 0,
                            ["succeeded"] = 0,
                        ["failed"] = preflight.Count(item => item.ContainsKey("isError") && (bool)item["isError"]),
                        ["warnings"] = preflightWarnings.Values.Distinct().ToList(),
                        ["requireAllValid"] = requireAllValid,
                        ["next"] = preflightValid
                            ? "Dry run passed. Re-run with dryRun=false, keeping confirm on dangerous child calls."
                            : "Fix failed child calls first; inspect results[*].text and missingRequired before executing.",
                        ["tokenHint"] = "Use valid/failed/warnings first; request responseMode=errors or includeArguments=true only when debugging.",
                        ["results"] = preflight
                    };
                        return new CallToolResult
                        {
                            Content = new List<ToolContent> { new ToolContent { Text = JsonConvert.SerializeObject(validationPayload, McpJsonUtil.Settings) } },
                            IsError = !preflightValid
                        };
                    }

                    var results = new List<Dictionary<string, object>>();
                    int succeeded = 0;
                    int failed = 0;
                    int attempted = 0;
                    int executed = 0;
                    bool stopped = false;

                    for (int i = 0; i < calls.Count; i++)
                    {
                        var result = ExecuteSingleCall(i, calls[i], defaults, responseMode, includeArguments, maxTextChars);
                        attempted++;
                        if (result.ContainsKey("executed") && (bool)result["executed"])
                            executed++;
                        bool isError = result.ContainsKey("isError") && (bool)result["isError"];
                        if (responseMode != "errors" || isError)
                            results.Add(result);

                        if (isError)
                        {
                            failed++;
                            if (stopOnError)
                            {
                                stopped = i < calls.Count - 1;
                                break;
                            }
                        }
                        else
                        {
                            succeeded++;
                        }
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["dryRun"] = false,
                        ["valid"] = failed == 0,
                        ["requested"] = calls.Count,
                        ["attempted"] = attempted,
                        ["executed"] = executed,
                        ["succeeded"] = succeeded,
                        ["failed"] = failed,
                        ["omitted"] = attempted - results.Count,
                        ["resultCount"] = results.Count,
                        ["stopped"] = stopped,
                    ["warnings"] = preflightWarnings.Values.Distinct().ToList(),
                    ["requireAllValid"] = requireAllValid,
                    ["responseMode"] = responseMode,
                    ["next"] = failed == 0
                        ? "Batch completed. Verify changed game state with a focused snapshot/search instead of repeating the same batch."
                        : "Inspect error results; fix inputs or run a dryRun with responseMode=errors before retrying.",
                    ["tokenHint"] = "Use resultCount/failed/executed first; prefer responseMode=summary or errors for autonomous loops.",
                    ["results"] = results
                };

                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        private static List<Dictionary<string, object>> PreflightCalls(JArray calls, JObject defaults, bool includeArguments, Dictionary<int, string> warnings)
        {
            var results = new List<Dictionary<string, object>>();
            for (int i = 0; i < calls.Count; i++)
                results.Add(PreflightSingleCall(i, calls[i], defaults, includeArguments, warnings));
            return results;
        }

        private static Dictionary<string, object> PreflightSingleCall(int index, JToken callToken, JObject defaults, bool includeArguments, Dictionary<int, string> warnings = null)
        {
            var call = callToken as JObject;
            if (call == null)
                return ErrorResult(index, null, null, "call entry must be an object");

            string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return ErrorResult(index, null, call, "tool name is required");

            string callId = call["id"]?.ToString() ?? call["key"]?.ToString();

            var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
            JObject arguments;
            if (argumentsToken == null || argumentsToken.Type == JTokenType.Null)
                arguments = new JObject();
            else if (argumentsToken.Type == JTokenType.Object)
                arguments = (JObject)argumentsToken.DeepClone();
            else
                return ErrorResult(index, name, call, "arguments must be an object");

            MergeDefaults(arguments, defaults);

            if (IsBatchCall(name, arguments))
                return ErrorResult(index, name, call, $"{ToolName} cannot call itself");

            McpTool tool;
            if (!OniToolRegistry.TryGetTool(name, out tool))
                return ErrorResult(index, name, call, $"Tool not found: {name}");

            if (RequiresBatchConfirm(tool, arguments))
                return ErrorResult(index, name, call, $"dangerous tool '{tool.Name}' requires arguments.confirm=true");

            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = name,
                ["canonicalName"] = tool.Name,
                ["group"] = tool.Group,
                ["mode"] = tool.Mode,
                ["risk"] = tool.Risk,
                ["executed"] = false,
                ["isError"] = false,
                ["text"] = "preflight ok"
            };
            if (!string.IsNullOrWhiteSpace(callId))
                result["id"] = callId;
            if (includeArguments)
                result["arguments"] = arguments;
            if (warnings != null && warnings.ContainsKey(index))
                result["warning"] = warnings[index];

            var missingRequired = MissingRequiredArguments(tool, arguments);
            if (missingRequired.Count > 0)
            {
                result["isError"] = true;
                result["text"] = "missing required arguments: " + string.Join(", ", missingRequired.ToArray());
                result["missingRequired"] = missingRequired;
            }

            return result;
        }

        private static List<string> MissingRequiredArguments(McpTool tool, JObject arguments)
        {
            if (tool.Parameters == null)
                return new List<string>();

            return tool.Parameters
                .Where(kv => kv.Value.Required && arguments[kv.Key] == null)
                .Select(kv => kv.Key)
                .OrderBy(name => name)
                .ToList();
        }

    }
}
