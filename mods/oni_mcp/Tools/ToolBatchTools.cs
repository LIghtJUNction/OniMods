using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ToolBatchTools
    {
        public const string ToolName = "tools_call_many";

        public static McpTool CallMany()
        {
            return new McpTool
            {
                Name = ToolName,
                Group = "tools",
                Mode = "execute",
                Risk = "dangerous",
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

            if (string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase))
                return ErrorResult(index, name, call, $"{ToolName} cannot call itself");

            var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
            JObject arguments;
            if (argumentsToken == null || argumentsToken.Type == JTokenType.Null)
                arguments = new JObject();
            else if (argumentsToken.Type == JTokenType.Object)
                arguments = (JObject)argumentsToken.DeepClone();
            else
                return ErrorResult(index, name, call, "arguments must be an object");

            MergeDefaults(arguments, defaults);

            McpTool tool;
            if (!OniToolRegistry.TryGetTool(name, out tool))
                return ErrorResult(index, name, call, $"Tool not found: {name}");

            if (string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase)
                && !ToolUtil.GetBool(arguments, "confirm", false)
                && !HasPreviewTokenBypass(tool, arguments))
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

        private static Dictionary<string, object> ExecuteSingleCall(int index, JToken callToken, JObject defaults, string responseMode, bool includeArguments, int maxTextChars)
        {
            var call = callToken as JObject;
            if (call == null)
                return ErrorResult(index, null, null, "call entry must be an object");

            string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return ErrorResult(index, null, call, "tool name is required");

            var preflight = PreflightSingleCall(index, callToken, defaults, includeArguments);
            if (preflight.ContainsKey("isError") && (bool)preflight["isError"])
                return preflight;

            if (string.Equals(name, ToolName, StringComparison.OrdinalIgnoreCase))
                return ErrorResult(index, name, call, $"{ToolName} cannot call itself");

            var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
            JObject arguments;
            if (argumentsToken == null || argumentsToken.Type == JTokenType.Null)
            {
                arguments = new JObject();
            }
            else if (argumentsToken.Type == JTokenType.Object)
            {
                arguments = (JObject)argumentsToken.DeepClone();
            }
            else
            {
                return ErrorResult(index, name, call, "arguments must be an object");
            }

            MergeDefaults(arguments, defaults);

            McpTool tool;
            if (OniToolRegistry.TryGetTool(name, out tool)
                && string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase)
                && !ToolUtil.GetBool(arguments, "confirm", false)
                && !HasPreviewTokenBypass(tool, arguments))
            {
                return ErrorResult(index, name, call, $"dangerous tool '{tool.Name}' requires arguments.confirm=true");
            }

            var toolResult = OniToolRegistry.CallTool(name, arguments);
            string text = ExtractText(toolResult);
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = name,
                ["canonicalName"] = preflight.ContainsKey("canonicalName") ? preflight["canonicalName"] : name,
                ["group"] = preflight.ContainsKey("group") ? preflight["group"] : null,
                ["mode"] = preflight.ContainsKey("mode") ? preflight["mode"] : null,
                ["risk"] = preflight.ContainsKey("risk") ? preflight["risk"] : null,
                ["executed"] = true,
                ["isError"] = toolResult.IsError,
                ["text"] = responseMode == "summary" ? CompactSummary(text, maxTextChars) : Truncate(text, maxTextChars)
            };
            if (preflight.ContainsKey("id"))
                result["id"] = preflight["id"];
            if (includeArguments)
                result["arguments"] = arguments;
            if (responseMode == "summary")
                result["summary"] = CompactSummaryObject(text, maxTextChars);
            if (responseMode == "full")
            {
                result["content"] = ContentToDictionaries(toolResult.Content);
                result["text"] = text;
            }
            return result;
        }

        private static void MergeDefaults(JObject target, JObject defaults)
        {
            if (target == null || defaults == null)
                return;

            foreach (var property in defaults.Properties())
            {
                if (target[property.Name] != null)
                    continue;

                target[property.Name] = property.Value.DeepClone();
            }
        }

        private static Dictionary<int, string> DuplicateCallWarnings(JArray calls, JObject defaults)
        {
            var warnings = new Dictionary<int, string>();
            var seen = new Dictionary<string, int>();

            for (int i = 0; i < calls.Count; i++)
            {
                var call = calls[i] as JObject;
                if (call == null)
                    continue;

                string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
                JObject arguments = (argumentsToken as JObject) != null ? (JObject)argumentsToken.DeepClone() : new JObject();
                MergeDefaults(arguments, defaults);

                McpTool tool;
                string canonicalName = OniToolRegistry.TryGetTool(name, out tool) ? tool.Name : name;
                if (!IsWriteOrExecute(tool))
                    continue;

                string key = canonicalName + ":" + CanonicalJson(arguments);
                int previous;
                if (seen.TryGetValue(key, out previous))
                {
                    string message = $"duplicate write/execute call matches index {previous}; repeated same-area calls usually indicate wrong routing or stale state. Read the previous result before retrying.";
                    warnings[i] = message;
                }
                else
                {
                    seen[key] = i;
                }
            }

            return warnings;
        }

        private static bool IsWriteOrExecute(McpTool tool)
        {
            if (tool == null)
                return true;
            return string.Equals(tool.Mode, "write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tool.Mode, "execute", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasPreviewTokenBypass(McpTool tool, JObject arguments)
        {
            return tool?.Parameters != null
                && tool.Parameters.ContainsKey("previewToken")
                && !string.IsNullOrWhiteSpace(arguments?["previewToken"]?.ToString());
        }

        private static string CanonicalJson(JObject arguments)
        {
            if (arguments == null)
                return "{}";

            var ordered = new JObject();
            foreach (var property in arguments.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                ordered[property.Name] = property.Value.DeepClone();
            return ordered.ToString(Formatting.None);
        }

        private static Dictionary<string, object> ErrorResult(int index, string name, JObject call, string message)
        {
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["isError"] = true,
                ["content"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = message
                    }
                },
                ["text"] = message
            };

            if (!string.IsNullOrWhiteSpace(name))
                result["name"] = name;
            if (call != null)
                result["call"] = call;

            return result;
        }

        private static string NormalizeResponseMode(string value)
        {
            string mode = string.IsNullOrWhiteSpace(value) ? "summary" : value.Trim().ToLowerInvariant();
            if (mode == "compact")
                return "summary";
            if (mode == "full" || mode == "summary" || mode == "errors")
                return mode;
            return "summary";
        }

        private static List<Dictionary<string, object>> ContentToDictionaries(List<ToolContent> content)
        {
            if (content == null)
                return new List<Dictionary<string, object>>();

            return content
                .Select(item => new Dictionary<string, object>
                {
                    ["type"] = item.Type,
                    ["text"] = item.Text
                })
                .ToList();
        }

        private static string ExtractText(CallToolResult result)
        {
            if (result?.Content == null || result.Content.Count == 0)
                return "";

            return string.Join("\n", result.Content
                .Where(item => !string.IsNullOrEmpty(item.Text))
                .Select(item => item.Text)
                .ToArray());
        }

        private static string CompactSummary(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch
            {
                return Truncate(text, maxChars);
            }

            JObject source = token as JObject;
            if (source == null)
            {
                if (token is JArray arr)
                    return Truncate($"{{\"count\":{arr.Count}}}", maxChars);
                return Truncate(text, maxChars);
            }

            var summary = new JObject();
            foreach (var key in new[] { "ok", "valid", "planned", "error", "message", "count", "total" })
            {
                if (source[key] != null)
                    summary[key] = source[key];
            }

            bool looksLikeWorldMap = source["size"] != null || source["cells"] != null || source["objectCount"] != null || source["conflictCount"] != null;
            if (looksLikeWorldMap)
            {
                foreach (var key in new[] { "size", "cells", "objectCount", "conflictCount" })
                {
                    if (source[key] != null)
                        summary[key] = source[key];
                }
            }

            bool looksLikeBuilding = source["prefabId"] != null || source["anchor"] != null;
            if (looksLikeBuilding)
            {
                foreach (var key in new[] { "prefabId", "anchor" })
                {
                    if (source[key] != null)
                        summary[key] = source[key];
                }
            }

            string compact = summary.ToString(Formatting.None);
            return Truncate(compact, maxChars);
        }

        private static object CompactSummaryObject(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return new JObject();

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch
            {
                return new JObject { ["text"] = Truncate(text, maxChars) };
            }

            var source = token as JObject;
            if (source == null)
            {
                if (token is JArray arr)
                    return new JObject { ["count"] = arr.Count };
                return new JObject { ["text"] = Truncate(text, maxChars) };
            }

            var summary = new JObject();
            foreach (var key in new[]
            {
                "ok", "valid", "dryRun", "committed", "planned", "wouldMark", "marked", "changed",
                "queued", "triggeredObjects", "returned", "matched", "executed", "succeeded", "failed",
                "skipped", "count", "total", "areaId", "worldId", "rect", "size", "cells", "prefabId",
                "x", "y", "error", "message", "previewToken"
            })
            {
                if (source[key] != null && IsCompactScalarOrSmallArray(source[key]))
                    summary[key] = source[key].DeepClone();
            }

            foreach (var property in source.Properties())
            {
                if (summary[property.Name] != null)
                    continue;
                if (property.Value is JArray arr)
                    summary[property.Name + "Count"] = arr.Count;
                else if (property.Value is JObject obj && IsSummaryObjectName(property.Name))
                    summary[property.Name] = CompactNestedObject(obj);
            }

            if (!summary.HasValues)
                summary["keys"] = new JArray(source.Properties().Select(property => property.Name));
            return summary;
        }

        private static bool IsCompactScalarOrSmallArray(JToken token)
        {
            if (token == null)
                return false;
            if (token.Type != JTokenType.Array)
                return token.Type != JTokenType.Object;
            var array = (JArray)token;
            return array.Count <= 8 && array.All(item => item.Type != JTokenType.Object && item.Type != JTokenType.Array);
        }

        private static bool IsSummaryObjectName(string name)
        {
            return string.Equals(name, "summary", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "counts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skipped", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skipReasons", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject CompactNestedObject(JObject source)
        {
            var result = new JObject();
            foreach (var property in source.Properties())
            {
                if (IsCompactScalarOrSmallArray(property.Value))
                    result[property.Name] = property.Value.DeepClone();
                else if (property.Value is JArray arr)
                    result[property.Name + "Count"] = arr.Count;
            }
            return result;
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? "";
            return value.Substring(0, maxChars) + $"... [truncated {value.Length - maxChars} chars]";
        }
    }
}
