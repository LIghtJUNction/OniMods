using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class PlanningHarnessTools
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, PlanSession> Sessions = new Dictionary<string, PlanSession>();
        private static int nextId = 1;

        public static McpTool CreatePlan()
        {
            return new McpTool
            {
                Name = "plan_harness_create",
                Group = "planning",
                Mode = "write",
                Risk = "low",
                Aliases = new List<string> { "plan_create", "planning_create" },
                Tags = new List<string> { "plan", "harness", "feedback", "verify", "constraints", "规划", "验证" },
                Description = "创建一个规划 harness 会话，显式记录目标、约束、计划、反馈、验证和实施门禁",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["objective"] = new McpToolParameter { Type = "string", Description = "计划目标，例如 stabilize oxygen in barracks", Required = true },
                    ["constraints"] = new McpToolParameter { Type = "array", Description = "硬约束数组，例如 no dangerous tools without confirm、avoid dupe death", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选关联区域句柄", Required = false },
                    ["riskTolerance"] = new McpToolParameter { Type = "string", Description = "风险偏好 low、medium、high，默认 low", Required = false, EnumValues = new List<string> { "low", "medium", "high" } },
                    ["requireVerification"] = new McpToolParameter { Type = "boolean", Description = "实施前是否必须记录 passed=true 的 verification，默认 true", Required = false }
                },
                Handler = args =>
                {
                    string objective = Normalize(args["objective"]?.ToString(), 1000);
                    if (string.IsNullOrEmpty(objective))
                        return CallToolResult.Error("objective is required");

                    var session = new PlanSession
                    {
                        Id = "p" + nextId++.ToString("D4"),
                        Objective = objective,
                        AreaId = Normalize(args["areaId"]?.ToString(), 80),
                        RiskTolerance = NormalizeRisk(args["riskTolerance"]?.ToString()),
                        RequireVerification = ToolUtil.GetBool(args, "requireVerification", true),
                        CreatedAt = System.DateTime.UtcNow,
                        UpdatedAt = System.DateTime.UtcNow,
                        Constraints = ReadStringArray(args["constraints"])
                    };
                    session.Events.Add(new PlanEvent
                    {
                        Stage = "constraint",
                        Summary = "Initial constraints",
                        Payload = new JObject
                        {
                            ["constraints"] = new JArray(session.Constraints),
                            ["riskTolerance"] = session.RiskTolerance,
                            ["requireVerification"] = session.RequireVerification
                        },
                        CreatedAt = session.CreatedAt
                    });

                    lock (Lock)
                    {
                        Sessions[session.Id] = session;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(session.ToDictionary(includeEvents: true), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetPlan()
        {
            return new McpTool
            {
                Name = "plan_harness_get",
                Group = "planning",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "plan_get", "planning_get" },
                Description = "读取规划 harness 会话详情，包括阶段记录和下一步门禁",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "计划 ID，例如 p0001", Required = true }
                },
                Handler = args =>
                {
                    PlanSession session;
                    if (!TryGet(args["id"]?.ToString(), out session))
                        return CallToolResult.Error("plan id not found");
                    return CallToolResult.Text(JsonConvert.SerializeObject(session.ToDictionary(includeEvents: true), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListPlans()
        {
            return new McpTool
            {
                Name = "plan_harness_list",
                Group = "planning",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "plans", "planning_list" },
                Description = "列出规划 harness 会话，最近更新优先",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 20, 100));
                    List<Dictionary<string, object>> plans;
                    lock (Lock)
                    {
                        plans = Sessions.Values
                            .OrderByDescending(session => session.UpdatedAt)
                            .Take(limit)
                            .Select(session => session.ToDictionary(includeEvents: false))
                            .ToList();
                    }
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = plans.Count,
                        ["plans"] = plans
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ParsePlanText()
        {
            return new McpTool
            {
                Name = "plan_harness_parse",
                Group = "planning",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "plan_parse", "planning_parse", "planned_calls_parse" },
                Tags = new List<string> { "plan", "parse", "text", "plannedCalls", "feedback", "规划", "解析" },
                Description = "把文本规划解析为可校验/执行的 plannedCalls。支持 JSON/代码块/每行 tool_name {json}；解析失败返回 parseErrors 和示例，供 agent 修正。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["planText"] = new McpToolParameter { Type = "string", Description = "规划文本。推荐直接给 JSON：{\"plannedCalls\":[{\"name\":\"orders_dig_area\",\"arguments\":{...}}]}", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "可选默认参数，会随解析结果返回并参与校验", Required = false },
                    ["validate"] = new McpToolParameter { Type = "boolean", Description = "是否同时校验工具存在、必填参数和危险 confirm，默认 true", Required = false }
                },
                Handler = args =>
                {
                    string planText = Normalize(args["planText"]?.ToString(), 20000);
                    if (string.IsNullOrWhiteSpace(planText))
                        return CallToolResult.Error(ParseFailure("planText is required", null).ToString(Formatting.None));

                    var parse = ParsePlannedCalls(planText);
                    JObject defaults = MergeParsedDefaults(parse.Defaults, args["defaults"] as JObject);
                    var payload = new JObject
                    {
                        ["ok"] = parse.Ok,
                        ["source"] = parse.Source,
                        ["plannedCalls"] = parse.PlannedCalls ?? new JArray(),
                        ["defaults"] = defaults
                    };

                    if (!parse.Ok)
                    {
                        payload["parseErrors"] = new JArray(parse.Errors.Select(error => new JObject { ["message"] = error }));
                        payload["expectedFormats"] = ExpectedPlanFormats();
                        return new CallToolResult
                        {
                            Content = new List<ToolContent> { new ToolContent { Text = payload.ToString(Formatting.None) } },
                            IsError = true
                        };
                    }

                    if (ToolUtil.GetBool(args, "validate", true))
                    {
                        var issues = new List<Dictionary<string, object>>();
                        var warnings = new List<Dictionary<string, object>>();
                        ValidateCallsArray(new PlanEvent
                        {
                            Stage = "plan",
                            Summary = "Parsed plan text",
                            Payload = new JObject
                            {
                                ["plannedCalls"] = parse.PlannedCalls,
                                ["defaults"] = payload["defaults"].DeepClone()
                            }
                        }, "plannedCalls", OniToolRegistry.GetTools().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase), issues, warnings);

                        payload["valid"] = issues.Count == 0;
                        payload["issues"] = JArray.FromObject(issues);
                        payload["warnings"] = JArray.FromObject(warnings);
                    }

                    return CallToolResult.Text(payload.ToString(Formatting.None));
                }
            };
        }

        public static McpTool RecordPlanStage()
        {
            return new McpTool
            {
                Name = "plan_harness_record",
                Group = "planning",
                Mode = "write",
                Risk = "low",
                Aliases = new List<string> { "plan_record", "planning_record" },
                Tags = new List<string> { "plan", "feedback", "verification", "implementation", "constraints" },
                Description = "向规划 harness 追加阶段记录：plan、feedback、verification、implementation、constraint；plan 可传 payload.plannedCalls/calls/items 或 planText，planText 解析失败会返回结构化错误",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "计划 ID，例如 p0001", Required = true },
                    ["stage"] = new McpToolParameter { Type = "string", Description = "阶段：plan、feedback、verification、implementation、constraint", Required = true, EnumValues = new List<string> { "plan", "feedback", "verification", "implementation", "constraint" } },
                    ["summary"] = new McpToolParameter { Type = "string", Description = "阶段摘要", Required = true },
                    ["planText"] = new McpToolParameter { Type = "string", Description = "stage=plan 时可传文本规划；支持 JSON/代码块/每行 tool_name {json}，会解析成 plannedCalls，失败则返回 parseErrors", Required = false },
                    ["payload"] = new McpToolParameter { Type = "object", Description = "结构化阶段数据，例如 plannedCalls/calls/items、defaults、feedback、checks、implementedCalls、constraints", Required = false },
                    ["passed"] = new McpToolParameter { Type = "boolean", Description = "verification 阶段是否通过；implementation 阶段可记录执行是否成功", Required = false },
                    ["overrideGate"] = new McpToolParameter { Type = "boolean", Description = "允许绕过未验证实施门禁，默认 false", Required = false }
                },
                Handler = args =>
                {
                    PlanSession session;
                    if (!TryGet(args["id"]?.ToString(), out session))
                        return CallToolResult.Error("plan id not found");

                    string stage = NormalizeStage(args["stage"]?.ToString());
                    if (stage == null)
                        return CallToolResult.Error("stage must be plan, feedback, verification, implementation or constraint");

                    string summary = Normalize(args["summary"]?.ToString(), 2000);
                    if (string.IsNullOrEmpty(summary))
                        return CallToolResult.Error("summary is required");

                    bool overrideGate = ToolUtil.GetBool(args, "overrideGate", false);
                    if (stage == "implementation" && session.RequireVerification && !session.HasPassedVerification && !overrideGate)
                        return CallToolResult.Error("implementation is gated: record a verification stage with passed=true first, or set overrideGate=true");

                    var payload = args["payload"] as JObject ?? new JObject();
                    string planText = Normalize(args["planText"]?.ToString(), 20000);
                    if (stage == "plan" && !string.IsNullOrWhiteSpace(planText) && CallsArray(payload, "plannedCalls") == null)
                    {
                        var parse = ParsePlannedCalls(planText);
                        payload["planText"] = planText;
                        payload["planTextParse"] = parse.ToJObject();
                        if (!parse.Ok)
                            return new CallToolResult
                            {
                                Content = new List<ToolContent> { new ToolContent { Text = ParseFailure("planText could not be parsed into plannedCalls", parse).ToString(Formatting.None) } },
                                IsError = true
                            };
                        if (payload["defaults"] == null && payload["defaultArguments"] == null && parse.Defaults != null && parse.Defaults.Count > 0)
                            payload["defaults"] = parse.Defaults.DeepClone();
                        payload["plannedCalls"] = parse.PlannedCalls;
                    }

                    bool? passed = args["passed"] != null ? (bool?)ToolUtil.GetBool(args, "passed", false) : null;
                    var now = System.DateTime.UtcNow;
                    var ev = new PlanEvent
                    {
                        Stage = stage,
                        Summary = summary,
                        Payload = payload,
                        Passed = passed,
                        CreatedAt = now
                    };

                    lock (Lock)
                    {
                        session.Events.Add(ev);
                        session.UpdatedAt = now;
                        if (stage == "constraint")
                            MergeConstraints(session, payload);
                        if (stage == "verification" && passed.HasValue && passed.Value)
                            session.HasPassedVerification = true;
                        if (stage == "implementation")
                            session.HasImplementation = true;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(session.ToDictionary(includeEvents: true), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ValidatePlan()
        {
            return new McpTool
            {
                Name = "plan_harness_validate",
                Group = "planning",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "plan_validate", "planning_validate" },
                Tags = new List<string> { "plan", "harness", "validate", "gate", "constraints" },
                Description = "验证规划 harness 是否满足计划-反馈-验证-实施门禁，并检查 plannedCalls/calls/items 与 defaults 中工具存在性、必填参数和危险工具 confirm 参数",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "计划 ID，例如 p0001", Required = true },
                    ["requireImplementation"] = new McpToolParameter { Type = "boolean", Description = "是否要求已经记录 implementation 阶段，默认 false", Required = false }
                },
                Handler = args =>
                {
                    PlanSession session;
                    if (!TryGet(args["id"]?.ToString(), out session))
                        return CallToolResult.Error("plan id not found");

                    var issues = new List<Dictionary<string, object>>();
                    var warnings = new List<Dictionary<string, object>>();
                    bool requireImplementation = ToolUtil.GetBool(args, "requireImplementation", false);
                    ValidateSession(session, requireImplementation, issues, warnings);
                    var summary = session.ToDictionary(includeEvents: false);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = session.Id,
                        ["valid"] = issues.Count == 0,
                        ["nextRequiredStage"] = summary["nextRequiredStage"],
                        ["requireImplementation"] = requireImplementation,
                        ["issueCount"] = issues.Count,
                        ["warningCount"] = warnings.Count,
                        ["issues"] = issues,
                        ["warnings"] = warnings,
                        ["summary"] = summary
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ExecutePlan()
        {
            return new McpTool
            {
                Name = "plan_harness_execute",
                Group = "planning",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "plan_execute", "planning_execute" },
                Tags = new List<string> { "plan", "harness", "execute", "implementation", "batch", "gate" },
                Description = "执行规划 harness 中最近 plan 阶段的 plannedCalls/calls/items；通过反馈/验证/约束门禁后才会调用工具，并自动记录 implementation",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "计划 ID，例如 p0001", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true；确认执行 plannedCalls", Required = true },
                    ["stopOnError"] = new McpToolParameter { Type = "boolean", Description = "遇到错误后是否停止后续调用，默认 true", Required = false },
                    ["responseMode"] = new McpToolParameter { Type = "string", Description = "返回模式：summary 或 full，默认 summary", Required = false, EnumValues = new List<string> { "summary", "full" } },
                    ["overrideGate"] = new McpToolParameter { Type = "boolean", Description = "允许绕过反馈/验证门禁，默认 false；仍不会绕过子工具 confirm 参数", Required = false }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to execute a plan");

                    PlanSession session;
                    if (!TryGet(args["id"]?.ToString(), out session))
                        return CallToolResult.Error("plan id not found");

                    bool overrideGate = ToolUtil.GetBool(args, "overrideGate", false);
                    var issues = new List<Dictionary<string, object>>();
                    var warnings = new List<Dictionary<string, object>>();
                    ValidateSession(session, requireImplementation: false, issues: issues, warnings: warnings);
                    if (!overrideGate && issues.Count > 0)
                    {
                        return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["executed"] = false,
                            ["error"] = "plan gates failed",
                            ["issues"] = issues,
                            ["warnings"] = warnings,
                            ["nextRequiredStage"] = session.ToDictionary(includeEvents: false)["nextRequiredStage"]
                        }, McpJsonUtil.Settings));
                    }

                    var latestPlan = LatestPlanWithCalls(session);
                    JArray plannedCalls = CallsArray(latestPlan, "plannedCalls");
                    if (plannedCalls == null || plannedCalls.Count == 0)
                        return CallToolResult.Error("no plannedCalls found in the latest plan stage");
                    JObject defaults = CallDefaults(latestPlan);

                    bool stopOnError = ToolUtil.GetBool(args, "stopOnError", true);
                    string responseMode = (args["responseMode"]?.ToString() ?? "summary").ToLowerInvariant() == "full" ? "full" : "summary";
                    var execution = ExecuteCalls(plannedCalls, defaults, stopOnError, responseMode);
                    bool allSucceeded = (int)execution["failed"] == 0;
                    var now = System.DateTime.UtcNow;
                    var ev = new PlanEvent
                    {
                        Stage = "implementation",
                        Summary = allSucceeded ? "Executed planned calls" : "Executed planned calls with failures",
                        Payload = new JObject
                        {
                            ["implementedCalls"] = plannedCalls.DeepClone(),
                            ["defaults"] = defaults.DeepClone(),
                            ["execution"] = JObject.FromObject(execution),
                            ["overrideGate"] = overrideGate
                        },
                        Passed = allSucceeded,
                        CreatedAt = now
                    };

                    lock (Lock)
                    {
                        session.Events.Add(ev);
                        session.UpdatedAt = now;
                        session.HasImplementation = true;
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["executed"] = true,
                        ["id"] = session.Id,
                        ["passed"] = allSucceeded,
                        ["execution"] = execution,
                        ["plan"] = session.ToDictionary(includeEvents: false)
                    };
                    return new CallToolResult
                    {
                        Content = new List<ToolContent> { new ToolContent { Text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings) } },
                        IsError = !allSucceeded
                    };
                }
            };
        }

        private static bool TryGet(string id, out PlanSession session)
        {
            session = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;
            lock (Lock)
            {
                return Sessions.TryGetValue(id.Trim(), out session);
            }
        }

        private static void MergeConstraints(PlanSession session, JObject payload)
        {
            var constraints = ReadStringArray(payload["constraints"] as JArray);
            foreach (string constraint in constraints)
            {
                if (!session.Constraints.Contains(constraint))
                    session.Constraints.Add(constraint);
            }
        }

        private static List<string> ReadStringArray(JToken token)
        {
            var result = new List<string>();
            var array = token as JArray;
            if (array == null)
                return result;
            foreach (var item in array)
            {
                string value = Normalize(item?.ToString(), 500);
                if (!string.IsNullOrEmpty(value))
                    result.Add(value);
            }
            return result;
        }

        private static List<string> ReadStringArray(object token)
        {
            return ReadStringArray(token as JToken);
        }

        private static void ValidateCallPayloads(PlanSession session, List<Dictionary<string, object>> issues, List<Dictionary<string, object>> warnings)
        {
            var tools = OniToolRegistry.GetTools().ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var ev in session.Events)
            {
                ValidateCallsArray(ev, "plannedCalls", tools, issues, warnings);
                ValidateCallsArray(ev, "implementedCalls", tools, issues, warnings);
            }
        }

        private static void ValidateCallsArray(PlanEvent ev, string key, Dictionary<string, McpTool> tools, List<Dictionary<string, object>> issues, List<Dictionary<string, object>> warnings)
        {
            var calls = CallsArray(ev, key);
            if (calls == null)
                return;
            var defaults = CallDefaults(ev);

            for (int i = 0; i < calls.Count; i++)
            {
                var call = calls[i] as JObject;
                if (call == null)
                {
                    issues.Add(Issue("invalid_call", $"{ev.Stage}.{key}[{i}] must be an object"));
                    continue;
                }

                string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    issues.Add(Issue("invalid_call", $"{ev.Stage}.{key}[{i}] missing tool name"));
                    continue;
                }

                McpTool tool;
                if (!OniToolRegistry.TryGetTool(name.Trim(), out tool))
                {
                    issues.Add(Issue("unknown_tool", $"{ev.Stage}.{key}[{i}] references unknown tool '{name}'"));
                    continue;
                }

                var arguments = CloneArguments(call["arguments"] ?? call["args"] ?? call["a"]);
                if (arguments == null)
                {
                    issues.Add(Issue("invalid_call", $"{ev.Stage}.{key}[{i}] arguments must be an object"));
                    continue;
                }
                MergeDefaults(arguments, defaults);

                var missingRequired = MissingRequiredArguments(tool, arguments);
                if (missingRequired.Count > 0)
                    issues.Add(Issue("missing_required_arguments", $"{ev.Stage}.{key}[{i}] tool '{tool.Name}' missing required arguments: {string.Join(", ", missingRequired.ToArray())}"));

                if (string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase) && !ToolUtil.GetBool(arguments, "confirm", false))
                    issues.Add(Issue("dangerous_without_confirm", $"{ev.Stage}.{key}[{i}] dangerous tool '{tool.Name}' must include arguments.confirm=true"));
                else if (string.Equals(tool.Risk, "medium", StringComparison.OrdinalIgnoreCase) && tool.Parameters.ContainsKey("confirm") && !ToolUtil.GetBool(arguments, "confirm", false))
                    warnings.Add(Issue("medium_without_confirm", $"{ev.Stage}.{key}[{i}] medium-risk tool '{tool.Name}' usually requires arguments.confirm=true"));
            }
        }

        private static JArray CallsArray(PlanEvent ev, string key)
        {
            if (ev?.Payload == null)
                return null;

            return CallsArray(ev.Payload, key);
        }

        private static JArray CallsArray(JObject payload, string key)
        {
            if (payload == null)
                return null;

            if (payload[key] is JArray direct)
                return direct;

            if (key == "plannedCalls")
                return payload["calls"] as JArray ?? payload["items"] as JArray;

            return null;
        }

        private static JObject CallDefaults(PlanEvent ev)
        {
            return ev?.Payload?["defaults"] as JObject
                ?? ev?.Payload?["defaultArguments"] as JObject
                ?? new JObject();
        }

        private static PlanParseResult ParsePlannedCalls(string planText)
        {
            var result = new PlanParseResult();
            string text = Normalize(planText, 20000);
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add("plan text is empty");
                return result;
            }

            foreach (string candidate in CandidatePlanTexts(text))
            {
                var parsed = TryParseJsonPlan(candidate);
                if (parsed.Ok)
                    return parsed;
                result.Errors.AddRange(parsed.Errors);
            }

            var lineParsed = TryParseLinePlan(text);
            if (lineParsed.Ok)
                return lineParsed;
            result.Errors.AddRange(lineParsed.Errors);

            result.Errors = result.Errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct()
                .Take(8)
                .ToList();
            if (result.Errors.Count == 0)
                result.Errors.Add("no planned calls found");
            return result;
        }

        private static IEnumerable<string> CandidatePlanTexts(string text)
        {
            yield return text.Trim();

            foreach (Match match in Regex.Matches(text, "```(?:json|JSON)?\\s*([\\s\\S]*?)```"))
            {
                string body = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(body))
                    yield return body;
            }

            int objectStart = text.IndexOf('{');
            int objectEnd = text.LastIndexOf('}');
            if (objectStart >= 0 && objectEnd > objectStart)
                yield return text.Substring(objectStart, objectEnd - objectStart + 1).Trim();

            int arrayStart = text.IndexOf('[');
            int arrayEnd = text.LastIndexOf(']');
            if (arrayStart >= 0 && arrayEnd > arrayStart)
                yield return text.Substring(arrayStart, arrayEnd - arrayStart + 1).Trim();
        }

        private static PlanParseResult TryParseJsonPlan(string text)
        {
            var result = new PlanParseResult { Source = "json" };
            try
            {
                var token = JToken.Parse(text);
                JArray calls = null;
                JObject defaults = null;

                if (token is JArray array)
                {
                    calls = array;
                }
                else if (token is JObject obj)
                {
                    calls = obj["plannedCalls"] as JArray
                        ?? obj["calls"] as JArray
                        ?? obj["items"] as JArray;
                    defaults = obj["defaults"] as JObject ?? obj["defaultArguments"] as JObject;
                }

                if (calls == null || calls.Count == 0)
                {
                    result.Errors.Add("JSON parsed, but no plannedCalls/calls/items array was found");
                    return result;
                }

                result.PlannedCalls = NormalizeParsedCalls(calls, result.Errors);
                result.Defaults = defaults ?? new JObject();
                result.Ok = result.Errors.Count == 0;
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add("JSON parse failed: " + ex.Message);
                return result;
            }
        }

        private static PlanParseResult TryParseLinePlan(string text)
        {
            var result = new PlanParseResult { Source = "line" };
            var calls = new JArray();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripListPrefix(lines[i]).Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                int brace = line.IndexOf('{');
                if (brace <= 0)
                    continue;

                string name = line.Substring(0, brace).Trim();
                name = name.Trim('-', '*', '`', ':').Trim();
                if (name.Contains(" "))
                    name = name.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

                string json = line.Substring(brace).Trim();
                try
                {
                    var args = JObject.Parse(json);
                    calls.Add(new JObject
                    {
                        ["name"] = name,
                        ["arguments"] = args
                    });
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"line {i + 1}: arguments JSON parse failed: {ex.Message}");
                }
            }

            if (calls.Count == 0)
            {
                result.Errors.Add("line format found no calls; expected lines like: orders_dig_area {\"x1\":1,\"y1\":2,\"x2\":3,\"y2\":4,\"confirm\":true}");
                return result;
            }

            result.PlannedCalls = NormalizeParsedCalls(calls, result.Errors);
            result.Ok = result.Errors.Count == 0;
            return result;
        }

        private static string StripListPrefix(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "";
            return Regex.Replace(line, "^\\s*(?:[-*]|\\d+[.)])\\s*", "");
        }

        private static JArray NormalizeParsedCalls(JArray calls, List<string> errors)
        {
            var normalized = new JArray();
            for (int i = 0; i < calls.Count; i++)
            {
                var call = calls[i] as JObject;
                if (call == null)
                {
                    errors.Add($"plannedCalls[{i}] must be an object");
                    continue;
                }

                string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"plannedCalls[{i}] missing name/tool/t");
                    continue;
                }

                var args = CloneArguments(call["arguments"] ?? call["args"] ?? call["a"]);
                if (args == null)
                {
                    errors.Add($"plannedCalls[{i}] arguments/args/a must be an object");
                    continue;
                }

                normalized.Add(new JObject
                {
                    ["name"] = name.Trim(),
                    ["arguments"] = args
                });
            }
            return normalized;
        }

        private static JObject ParseFailure(string message, PlanParseResult parse)
        {
            var payload = new JObject
            {
                ["ok"] = false,
                ["error"] = message,
                ["expectedFormats"] = ExpectedPlanFormats()
            };
            if (parse != null)
            {
                payload["source"] = parse.Source;
                payload["parseErrors"] = new JArray(parse.Errors.Select(error => new JObject { ["message"] = error }));
            }
            return payload;
        }

        private static JObject MergeParsedDefaults(JObject parsedDefaults, JObject explicitDefaults)
        {
            var result = parsedDefaults != null ? (JObject)parsedDefaults.DeepClone() : new JObject();
            if (explicitDefaults == null)
                return result;

            foreach (var property in explicitDefaults.Properties())
                result[property.Name] = property.Value.DeepClone();
            return result;
        }

        private static JArray ExpectedPlanFormats()
        {
            return new JArray
            {
                new JObject
                {
                    ["kind"] = "json_object",
                    ["example"] = "{\"plannedCalls\":[{\"name\":\"orders_dig_area\",\"arguments\":{\"worldId\":0,\"x1\":101,\"y1\":253,\"x2\":106,\"y2\":256,\"confirm\":true}}]}"
                },
                new JObject
                {
                    ["kind"] = "compact_json",
                    ["example"] = "{\"items\":[{\"t\":\"orders_dig_area\",\"a\":{\"worldId\":0,\"x1\":101,\"y1\":253,\"x2\":106,\"y2\":256,\"confirm\":true}}]}"
                },
                new JObject
                {
                    ["kind"] = "line",
                    ["example"] = "orders_dig_area {\"worldId\":0,\"x1\":101,\"y1\":253,\"x2\":106,\"y2\":256,\"confirm\":true}"
                }
            };
        }

        private static JObject CloneArguments(JToken argumentsToken)
        {
            if (argumentsToken == null || argumentsToken.Type == JTokenType.Null)
                return new JObject();
            return argumentsToken.Type == JTokenType.Object ? (JObject)argumentsToken.DeepClone() : null;
        }

        private static void MergeDefaults(JObject target, JObject defaults)
        {
            if (target == null || defaults == null)
                return;

            foreach (var property in defaults.Properties())
            {
                if (target[property.Name] == null)
                    target[property.Name] = property.Value.DeepClone();
            }
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

        private static void ValidateSession(PlanSession session, bool requireImplementation, List<Dictionary<string, object>> issues, List<Dictionary<string, object>> warnings)
        {
            bool hasPlan = session.Events.Any(ev => ev.Stage == "plan");
            bool hasFeedback = session.Events.Any(ev => ev.Stage == "feedback");
            bool hasVerification = session.Events.Any(ev => ev.Stage == "verification");

            if (!hasPlan)
                issues.Add(Issue("missing_stage", "plan stage is required"));
            if (!hasFeedback)
                issues.Add(Issue("missing_stage", "feedback stage is required before implementation"));
            if (session.RequireVerification && !session.HasPassedVerification)
                issues.Add(Issue("verification_gate", "passed verification is required before implementation"));
            if (requireImplementation && !session.HasImplementation)
                issues.Add(Issue("missing_stage", "implementation stage is required"));
            if (!hasVerification)
                warnings.Add(Issue("missing_stage", "no verification stage has been recorded"));
            if (session.Constraints.Count == 0)
                warnings.Add(Issue("missing_constraints", "no explicit constraints recorded"));

            ValidateCallPayloads(session, issues, warnings);
        }

        private static PlanEvent LatestPlanWithCalls(PlanSession session)
        {
            return session.Events.LastOrDefault(ev => ev.Stage == "plan" && CallsArray(ev, "plannedCalls") is JArray);
        }

        private static Dictionary<string, object> ExecuteCalls(JArray calls, JObject defaults, bool stopOnError, string responseMode)
        {
            var results = new List<Dictionary<string, object>>();
            int succeeded = 0;
            int failed = 0;
            bool stopped = false;

            for (int i = 0; i < calls.Count; i++)
            {
                var result = ExecuteSinglePlannedCall(i, calls[i], defaults, responseMode);
                bool isError = result.ContainsKey("isError") && (bool)result["isError"];
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

            return new Dictionary<string, object>
            {
                ["requested"] = calls.Count,
                ["executed"] = results.Count,
                ["succeeded"] = succeeded,
                ["failed"] = failed,
                ["stopped"] = stopped,
                ["responseMode"] = responseMode,
                ["results"] = results
            };
        }

        private static Dictionary<string, object> ExecuteSinglePlannedCall(int index, JToken callToken, JObject defaults, string responseMode)
        {
            var call = callToken as JObject;
            if (call == null)
                return PlannedCallError(index, null, "plannedCalls entry must be an object");

            string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return PlannedCallError(index, null, "plannedCalls entry missing tool name");

            if (string.Equals(name, "plan_harness_execute", StringComparison.OrdinalIgnoreCase))
                return PlannedCallError(index, name, "plan_harness_execute cannot call itself");

            var arguments = CloneArguments(call["arguments"] ?? call["args"] ?? call["a"]);
            if (arguments == null)
                return PlannedCallError(index, name, "plannedCalls arguments must be an object");
            MergeDefaults(arguments, defaults);

            McpTool tool;
            if (OniToolRegistry.TryGetTool(name, out tool)
                && string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase)
                && !ToolUtil.GetBool(arguments, "confirm", false))
            {
                return PlannedCallError(index, name, $"dangerous tool '{tool.Name}' requires arguments.confirm=true");
            }

            var toolResult = OniToolRegistry.CallTool(name, arguments);
            string text = ExtractText(toolResult);
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = name,
                ["isError"] = toolResult.IsError,
                ["text"] = responseMode == "full" ? text : Truncate(text, 600)
            };
            if (responseMode == "full")
                result["content"] = ContentToDictionaries(toolResult.Content);
            return result;
        }

        private static Dictionary<string, object> PlannedCallError(int index, string name, string message)
        {
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["isError"] = true,
                ["text"] = message
            };
            if (!string.IsNullOrWhiteSpace(name))
                result["name"] = name;
            return result;
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

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? "";
            return value.Substring(0, maxChars) + $"... [truncated {value.Length - maxChars} chars]";
        }

        private static Dictionary<string, object> Issue(string code, string message)
        {
            return new Dictionary<string, object>
            {
                ["code"] = code,
                ["message"] = message
            };
        }

        private static string Normalize(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            string trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }

        private static string NormalizeRisk(string value)
        {
            string risk = Normalize(value, 20).ToLowerInvariant();
            return risk == "medium" || risk == "high" ? risk : "low";
        }

        private static string NormalizeStage(string value)
        {
            string stage = Normalize(value, 40).ToLowerInvariant();
            switch (stage)
            {
                case "plan":
                case "feedback":
                case "verification":
                case "implementation":
                case "constraint":
                    return stage;
                default:
                    return null;
            }
        }

        private class PlanSession
        {
            public string Id { get; set; }
            public string Objective { get; set; }
            public string AreaId { get; set; }
            public string RiskTolerance { get; set; }
            public bool RequireVerification { get; set; }
            public bool HasPassedVerification { get; set; }
            public bool HasImplementation { get; set; }
            public System.DateTime CreatedAt { get; set; }
            public System.DateTime UpdatedAt { get; set; }
            public List<string> Constraints { get; set; } = new List<string>();
            public List<PlanEvent> Events { get; set; } = new List<PlanEvent>();

            public Dictionary<string, object> ToDictionary(bool includeEvents)
            {
                var result = new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["uri"] = "oni://plans/" + Id,
                    ["objective"] = Objective,
                    ["areaId"] = string.IsNullOrEmpty(AreaId) ? null : AreaId,
                    ["riskTolerance"] = RiskTolerance,
                    ["requireVerification"] = RequireVerification,
                    ["hasPassedVerification"] = HasPassedVerification,
                    ["hasImplementation"] = HasImplementation,
                    ["nextRequiredStage"] = NextRequiredStage(),
                    ["constraints"] = Constraints,
                    ["createdAt"] = CreatedAt.ToString("o"),
                    ["updatedAt"] = UpdatedAt.ToString("o"),
                    ["eventCount"] = Events.Count
                };
                if (includeEvents)
                    result["events"] = Events.Select(item => item.ToDictionary()).ToList();
                return result;
            }

            private string NextRequiredStage()
            {
                bool hasPlan = Events.Any(ev => ev.Stage == "plan");
                bool hasFeedback = Events.Any(ev => ev.Stage == "feedback");
                if (!hasPlan)
                    return "plan";
                if (!hasFeedback)
                    return "feedback";
                if (RequireVerification && !HasPassedVerification)
                    return "verification";
                if (!HasImplementation)
                    return "implementation";
                return "verify_outcome";
            }
        }

        private class PlanEvent
        {
            public string Stage { get; set; }
            public string Summary { get; set; }
            public JObject Payload { get; set; }
            public bool? Passed { get; set; }
            public System.DateTime CreatedAt { get; set; }

            public Dictionary<string, object> ToDictionary()
            {
                var result = new Dictionary<string, object>
                {
                    ["stage"] = Stage,
                    ["summary"] = Summary,
                    ["payload"] = Payload,
                    ["createdAt"] = CreatedAt.ToString("o")
                };
                if (Passed.HasValue)
                    result["passed"] = Passed.Value;
                return result;
            }
        }

        private class PlanParseResult
        {
            public bool Ok { get; set; }
            public string Source { get; set; } = "none";
            public JArray PlannedCalls { get; set; }
            public JObject Defaults { get; set; } = new JObject();
            public List<string> Errors { get; set; } = new List<string>();

            public JObject ToJObject()
            {
                return new JObject
                {
                    ["ok"] = Ok,
                    ["source"] = Source,
                    ["plannedCalls"] = PlannedCalls ?? new JArray(),
                    ["defaults"] = Defaults ?? new JObject(),
                    ["errors"] = new JArray(Errors)
                };
            }
        }
    }
}
