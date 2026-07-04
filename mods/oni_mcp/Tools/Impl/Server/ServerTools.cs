using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ServerTools
    {
        private const string ToolName = "server_control";

        public static McpTool ControlServer()
        {
            return new McpTool
            {
                Name = ToolName,
                Group = "server",
                Mode = "read/execute",
                Risk = "medium",
                Aliases = new List<string> { "mcp_server_control", "server_diagnostics_control", "mcp_client_request_control", "tools_catalog_control", "tools_call_many", "agent_program_execute" },
                Description = "服务器/MCP 组合入口：domain=diagnostics action=status/capabilities/logs_tail；domain=client_request action=create_sampling/create_elicitation；domain=catalog action=manifest/search/guide/coverage/static_audit/surface_audit；domain=batch action=call_many 批量调用工具；domain=program action=execute 执行受限流程 DSL",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "diagnostics、client_request、catalog、batch、program 或 middleware，默认 diagnostics", Required = false, EnumValues = new List<string> { "diagnostics", "client_request", "catalog", "batch", "program", "middleware" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "diagnostics: status/capabilities/logs_tail；client_request: create_sampling/create_elicitation；catalog: manifest/search/guide/coverage/static_audit/surface_audit；batch: call_many；program: execute；middleware: queue/status/clear", Required = true },
                    ["file"] = new McpToolParameter { Type = "string", Description = "diagnostics logs_tail：current 或 previous", Required = false },
                    ["lines"] = new McpToolParameter { Type = "integer", Description = "diagnostics logs_tail：返回末尾行数，默认 120，最大 1000", Required = false },
                    ["filter"] = new McpToolParameter { Type = "string", Description = "diagnostics logs_tail：可选关键词过滤", Required = false },
                    ["surface"] = new McpToolParameter { Type = "string", Description = "catalog surface_audit：side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification", Required = false, EnumValues = new List<string> { "side_screen", "user_menu", "management", "tool_menu", "ui_menu", "global_control", "notification" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "catalog manifest/search/coverage/surface_audit 的关键词或目标意图", Required = false },
                    ["goal"] = new McpToolParameter { Type = "string", Description = "catalog guide 的玩家目标或操作意图", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "catalog manifest/search/coverage 的工具或操作分组过滤", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "catalog manifest/search 过滤 read/write/execute/any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "catalog manifest/search/static_audit 过滤 none/low/medium/dangerous/any", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "catalog coverage/surface_audit 状态过滤", Required = false, EnumValues = new List<string> { "all", "covered", "partial", "missing", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "catalog 返回细节：brief/compact/full，按 action 支持", Required = false },
                    ["includeResources"] = new McpToolParameter { Type = "boolean", Description = "catalog coverage 是否返回 resourceAnchors", Required = false },
                    ["includeHotkeys"] = new McpToolParameter { Type = "boolean", Description = "catalog coverage 是否返回游戏 Action 枚举热键覆盖摘要", Required = false },
                    ["includeNoAction"] = new McpToolParameter { Type = "boolean", Description = "catalog surface_audit surface=side_screen 是否返回纯显示/无玩家操作侧屏", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "catalog manifest/search/coverage 最多返回多少项", Required = false },
                    ["calls"] = new McpToolParameter { Type = "array", Description = "domain=batch action=call_many：要调用的工具数组，格式为 [{\"name\":\"tool_name\",\"arguments\":{...}}]，也兼容短字段 {t,a}；最多 20 个", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "domain=batch action=call_many：calls 的别名", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "domain=batch action=call_many：合并到每个子调用 arguments 的默认参数对象；子调用同名参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "domain=batch action=call_many：defaults 的别名", Required = false },
                    ["stopOnError"] = new McpToolParameter { Type = "boolean", Description = "domain=batch action=call_many：遇到错误后是否停止后续调用，默认 false", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "domain=batch action=call_many：只做结构、安全和工具存在性预检，不执行任何子调用，默认 false", Required = false },
                    ["requireAllValid"] = new McpToolParameter { Type = "boolean", Description = "domain=batch action=call_many：执行前要求所有子调用通过预检，默认 true", Required = false },
                    ["responseMode"] = new McpToolParameter { Type = "string", Description = "domain=batch action=call_many：full/summary/errors，默认 summary", Required = false, EnumValues = new List<string> { "full", "summary", "errors" } },
                    ["includeArguments"] = new McpToolParameter { Type = "boolean", Description = "domain=batch action=call_many：是否回显合并 defaults 后的 arguments，默认 false", Required = false },
                    ["maxTextChars"] = new McpToolParameter { Type = "integer", Description = "domain=batch action=call_many：summary/errors 模式下每项 text 最大字符数，默认 500，最大 4000", Required = false },
                    ["program"] = new McpToolParameter { Type = "object", Description = "domain=program action=execute：受限流程 DSL 对象、数组或 JSON 字符串，格式如 {vars:{...},steps:[...]}", Required = false },
                    ["maxSteps"] = new McpToolParameter { Type = "integer", Description = "domain=program action=execute：最大执行语句数，默认 200，最大 2000", Required = false },
                    ["maxLoopIterations"] = new McpToolParameter { Type = "integer", Description = "domain=program action=execute：单个 while/repeat 最大迭代次数，默认 100，最大 1000", Required = false },
                    ["trace"] = new McpToolParameter { Type = "boolean", Description = "domain=program action=execute：是否返回详细执行 trace，默认 true", Required = false },
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "client_request create_sampling：用户消息", Required = false },
                    ["systemPrompt"] = new McpToolParameter { Type = "string", Description = "client_request create_sampling：可选 system prompt", Required = false },
                    ["maxTokens"] = new McpToolParameter { Type = "integer", Description = "client_request create_sampling：最大 token 数，默认 1000", Required = false },
                    ["temperature"] = new McpToolParameter { Type = "number", Description = "client_request create_sampling：可选 temperature", Required = false },
                    ["includeContext"] = new McpToolParameter { Type = "string", Description = "client_request create_sampling：上下文范围", Required = false, EnumValues = new List<string> { "none", "thisServer", "allServers" } },
                    ["message"] = new McpToolParameter { Type = "string", Description = "client_request create_elicitation 或 middleware queue：展示给用户的问题、说明或下一次工具调用通知", Required = false },
                    ["level"] = new McpToolParameter { Type = "string", Description = "middleware queue：通知级别，默认 info", Required = false, EnumValues = new List<string> { "info", "warning", "error" } },
                    ["fieldName"] = new McpToolParameter { Type = "string", Description = "client_request create_elicitation：结构化响应字段名，默认 response", Required = false },
                    ["fieldDescription"] = new McpToolParameter { Type = "string", Description = "client_request create_elicitation：结构化响应字段说明", Required = false },
                    ["fieldType"] = new McpToolParameter { Type = "string", Description = "client_request create_elicitation：字段类型，默认 string", Required = false, EnumValues = new List<string> { "string", "boolean", "integer", "number" } },
                    ["required"] = new McpToolParameter { Type = "boolean", Description = "client_request create_elicitation：字段是否必填，默认 true", Required = false },
                    ["schema"] = new McpToolParameter { Type = "string", Description = "client_request create_elicitation：可选完整 JSON schema", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "diagnostics").Trim().ToLowerInvariant();
                    if (domain == "diagnostics" || domain == "diagnostic" || domain == "server")
                        return DiagnosticsControl().Handler(args);
                    if (domain == "client_request" || domain == "client" || domain == "request")
                        return ControlClientRequest().Handler(args);
                    if (domain == "catalog" || domain == "tools" || domain == "tool_catalog")
                    {
                        var forwarded = new JObject(args);
                        forwarded.Remove("domain");
                        return ToolCatalogTools.ControlToolCatalog().Handler(forwarded);
                    }
                    if (domain == "batch" || domain == "call_many" || domain == "many")
                        return ToolBatchTools.CallMany().Handler(args);
                    if (domain == "middleware" || domain == "tool_middleware" || domain == "notice")
                        return ToolCallMiddlewareControl(args);
                    if (domain == "program" || domain == "agent_program" || domain == "flow" || domain == "script")
                    {
                        string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                        if (action == "execute" || action == "run" || action == "start")
                            return AgentProgramTools.ExecuteProgram().Handler(args);
                        return CallToolResult.Error("domain=program action must be execute");
                    }
                    return CallToolResult.Error("domain must be diagnostics, client_request, catalog, batch, program, or middleware");
                }
            };
        }

        internal static bool IsServerControlDomainCall(string name, JObject arguments, params string[] domains)
        {
            McpTool tool;
            if (!OniToolRegistry.TryGetTool(name, out tool)
                || !string.Equals(tool.Name, ToolName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string domain = (arguments?["domain"]?.ToString() ?? "diagnostics").Trim().ToLowerInvariant();
            foreach (var candidate in domains)
            {
                if (string.Equals(domain, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static CallToolResult ToolCallMiddlewareControl(JObject args)
        {
            string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            Dictionary<string, object> payload;

            if (action == "queue" || action == "notify" || action == "notification")
            {
                string message = args["message"]?.ToString();
                if (string.IsNullOrWhiteSpace(message))
                    return CallToolResult.Error("message is required for domain=middleware action=queue");
                payload = ToolCallMiddleware.QueueNotification(message, args["level"]?.ToString());
                payload["queued"] = true;
                payload["delivery"] = "next tools/call response";
                return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
            }

            if (action == "status")
                return CallToolResult.Text(JsonConvert.SerializeObject(ToolCallMiddleware.Status(), McpJsonUtil.Settings));

            if (action == "clear")
                return CallToolResult.Text(JsonConvert.SerializeObject(ToolCallMiddleware.Clear(), McpJsonUtil.Settings));

            return CallToolResult.Error("domain=middleware action must be queue, status, or clear");
        }

        public static McpTool DiagnosticsControl()
        {
            return new McpTool
            {
                Name = "server_diagnostics_control",
                Hidden = true,
                Group = "server",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "server_diagnostics", "mcp_server_diagnostics" },
                Description = "兼容旧工具：请改用 server_control domain=diagnostics action=status/capabilities/logs_tail",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "诊断动作：status 返回 MCP 服务器状态；capabilities 返回客户端能力；logs_tail 读取最近游戏日志",
                        Required = true,
                        EnumValues = new List<string> { "status", "capabilities", "logs_tail" }
                    },
                    ["file"] = new McpToolParameter { Type = "string", Description = "action=logs_tail 时使用：日志文件 current 或 previous，默认 current", Required = false },
                    ["lines"] = new McpToolParameter { Type = "integer", Description = "action=logs_tail 时使用：返回末尾行数，默认 120，最大 1000", Required = false },
                    ["filter"] = new McpToolParameter { Type = "string", Description = "action=logs_tail 时使用：可选关键词过滤，不区分大小写", Required = false }
                },
                Handler = args =>
                {
                    string action = args["action"]?.ToString();
                    if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
                        return GetMcpStatus().Handler(args);
                    if (string.Equals(action, "capabilities", StringComparison.OrdinalIgnoreCase))
                        return GetClientCapabilities().Handler(args);
                    if (string.Equals(action, "logs_tail", StringComparison.OrdinalIgnoreCase))
                        return TailLogs().Handler(args);

                    return CallToolResult.Error("Invalid action. Expected status, capabilities, or logs_tail.");
                }
            };
        }


        public static McpTool TailLogs()
        {
            return new McpTool
            {
                Name = "logs_tail",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "tail_logs", "player_log" },
                Description = "兼容旧名；建议使用 server_diagnostics_control action=logs_tail",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["file"] = new McpToolParameter { Type = "string", Description = "日志文件：current 或 previous，默认 current", Required = false },
                    ["lines"] = new McpToolParameter { Type = "integer", Description = "返回末尾行数，默认 120，最大 1000", Required = false },
                    ["filter"] = new McpToolParameter { Type = "string", Description = "可选关键词过滤，不区分大小写", Required = false }
                },
                Handler = args =>
                {
                    string requested = args["file"]?.ToString();
                    string fileName = string.Equals(requested, "previous", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(requested, "prev", StringComparison.OrdinalIgnoreCase)
                            ? "Player-prev.log"
                            : "Player.log";

                    int lines = 120;
                    if (args["lines"] != null && int.TryParse(args["lines"].ToString(), out int parsed))
                        lines = Math.Max(1, Math.Min(parsed, 1000));

                    string filter = args["filter"]?.ToString();
                    string path = ResolveLogPath(fileName);
                    if (path == null || !File.Exists(path))
                        return CallToolResult.Error($"Log file not found: {fileName}");

                    var allLines = ReadAllLinesShared(path);
                    IEnumerable<string> selected = allLines;
                    if (!string.IsNullOrWhiteSpace(filter))
                    {
                        selected = selected.Where(line =>
                            line != null && line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                    }

                    var tail = selected.Skip(Math.Max(0, selected.Count() - lines)).ToList();
                    var result = new Dictionary<string, object>
                    {
                        ["path"] = path,
                        ["file"] = fileName,
                        ["lines"] = tail.Count,
                        ["filter"] = string.IsNullOrWhiteSpace(filter) ? null : filter,
                        ["text"] = string.Join("\n", tail.ToArray())
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetClientCapabilities()
        {
            return new McpTool
            {
                Name = "mcp_client_capabilities",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "client_capabilities", "server_client_capabilities" },
                Description = "兼容旧名；建议使用 server_diagnostics_control action=capabilities",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    var server = McpHttpServer.Instance;
                    object sessions = server == null ? (object)new List<object>() : server.GetSessionSummaries();
                    var result = new Dictionary<string, object>
                    {
                        ["loaded"] = server != null,
                        ["sessions"] = sessions
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CreateSamplingRequest()
        {
            return new McpTool
            {
                Name = "mcp_sampling_request_create",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sampling_request_create", "client_sampling_request" },
                Description = "兼容旧名；建议使用 mcp_client_request_control action=create_sampling",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "要让客户端模型生成或分析的用户消息", Required = true },
                    ["systemPrompt"] = new McpToolParameter { Type = "string", Description = "可选 system prompt", Required = false },
                    ["maxTokens"] = new McpToolParameter { Type = "integer", Description = "最大 token 数，默认 1000", Required = false },
                    ["temperature"] = new McpToolParameter { Type = "number", Description = "可选 temperature", Required = false },
                    ["includeContext"] = new McpToolParameter { Type = "string", Description = "上下文范围", Required = false, EnumValues = new List<string> { "none", "thisServer", "allServers" } }
                },
                Handler = args =>
                {
                    string prompt = args["prompt"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prompt))
                        return CallToolResult.Error("Missing prompt");

                    int maxTokens = ReadInt(args, "maxTokens", 1000, 1, 32000);
                    var @params = new JObject
                    {
                        ["messages"] = new JArray
                        {
                            new JObject
                            {
                                ["role"] = "user",
                                ["content"] = new JObject
                                {
                                    ["type"] = "text",
                                    ["text"] = prompt
                                }
                            }
                        },
                        ["maxTokens"] = maxTokens
                    };

                    string systemPrompt = args["systemPrompt"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(systemPrompt))
                        @params["systemPrompt"] = systemPrompt;

                    string includeContext = args["includeContext"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(includeContext))
                        @params["includeContext"] = includeContext;

                    double temperature;
                    if (args["temperature"] != null && double.TryParse(args["temperature"].ToString(), out temperature))
                        @params["temperature"] = temperature;

                    return CallToolResult.Text(JsonConvert.SerializeObject(ClientRequest("sampling/createMessage", @params), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlClientRequest()
        {
            return new McpTool
            {
                Name = "mcp_client_request_control",
                Hidden = true,
                Group = "server",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "client_request_control", "mcp_client_request" },
                Description = "兼容旧工具：请改用 server_control domain=client_request action=create_sampling/create_elicitation",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：create_sampling 或 create_elicitation", Required = true, EnumValues = new List<string> { "create_sampling", "create_elicitation" } },
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "create_sampling：要让客户端模型生成或分析的用户消息", Required = false },
                    ["systemPrompt"] = new McpToolParameter { Type = "string", Description = "create_sampling：可选 system prompt", Required = false },
                    ["maxTokens"] = new McpToolParameter { Type = "integer", Description = "create_sampling：最大 token 数，默认 1000", Required = false },
                    ["temperature"] = new McpToolParameter { Type = "number", Description = "create_sampling：可选 temperature", Required = false },
                    ["includeContext"] = new McpToolParameter { Type = "string", Description = "create_sampling：上下文范围", Required = false, EnumValues = new List<string> { "none", "thisServer", "allServers" } },
                    ["message"] = new McpToolParameter { Type = "string", Description = "create_elicitation：展示给用户的问题或说明", Required = false },
                    ["fieldName"] = new McpToolParameter { Type = "string", Description = "create_elicitation：结构化响应字段名，默认 response", Required = false },
                    ["fieldDescription"] = new McpToolParameter { Type = "string", Description = "create_elicitation：结构化响应字段说明", Required = false },
                    ["fieldType"] = new McpToolParameter { Type = "string", Description = "create_elicitation：字段类型，默认 string", Required = false, EnumValues = new List<string> { "string", "boolean", "integer", "number" } },
                    ["required"] = new McpToolParameter { Type = "boolean", Description = "create_elicitation：字段是否必填，默认 true", Required = false },
                    ["schema"] = new McpToolParameter { Type = "string", Description = "create_elicitation：可选完整 JSON schema；提供后覆盖 field* 参数", Required = false }
                },
                Handler = args =>
                {
                    string action = args["action"]?.ToString();
                    switch (action)
                    {
                        case "create_sampling":
                            return CreateSamplingRequest().Handler(args);
                        case "create_elicitation":
                            return CreateElicitationRequest().Handler(args);
                        default:
                            return CallToolResult.Error("Invalid action. Expected create_sampling or create_elicitation.");
                    }
                }
            };
        }

        public static McpTool CreateElicitationRequest()
        {
            return new McpTool
            {
                Name = "mcp_elicitation_request_create",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "elicitation_request_create", "client_elicitation_request" },
                Description = "兼容旧名；建议使用 mcp_client_request_control action=create_elicitation",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["message"] = new McpToolParameter { Type = "string", Description = "展示给用户的问题或说明", Required = true },
                    ["fieldName"] = new McpToolParameter { Type = "string", Description = "结构化响应字段名，默认 response", Required = false },
                    ["fieldDescription"] = new McpToolParameter { Type = "string", Description = "结构化响应字段说明", Required = false },
                    ["fieldType"] = new McpToolParameter { Type = "string", Description = "字段类型，默认 string", Required = false, EnumValues = new List<string> { "string", "boolean", "integer", "number" } },
                    ["required"] = new McpToolParameter { Type = "boolean", Description = "字段是否必填，默认 true", Required = false },
                    ["schema"] = new McpToolParameter { Type = "string", Description = "可选完整 JSON schema；提供后覆盖 field* 参数", Required = false }
                },
                Handler = args =>
                {
                    string message = args["message"]?.ToString();
                    if (string.IsNullOrWhiteSpace(message))
                        return CallToolResult.Error("Missing message");

                    JObject schema;
                    string schemaText = args["schema"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(schemaText))
                    {
                        try
                        {
                            schema = JObject.Parse(schemaText);
                        }
                        catch (Exception ex)
                        {
                            return CallToolResult.Error($"Invalid schema JSON: {ex.Message}");
                        }
                    }
                    else
                    {
                        string fieldName = string.IsNullOrWhiteSpace(args["fieldName"]?.ToString()) ? "response" : args["fieldName"].ToString();
                        string fieldType = string.IsNullOrWhiteSpace(args["fieldType"]?.ToString()) ? "string" : args["fieldType"].ToString();
                        bool required = args["required"] == null || string.Equals(args["required"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
                        schema = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                [fieldName] = new JObject
                                {
                                    ["type"] = fieldType,
                                    ["description"] = string.IsNullOrWhiteSpace(args["fieldDescription"]?.ToString()) ? message : args["fieldDescription"].ToString()
                                }
                            }
                        };
                        if (required)
                            schema["required"] = new JArray(fieldName);
                    }

                    var @params = new JObject
                    {
                        ["message"] = message,
                        ["requestedSchema"] = schema
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(ClientRequest("elicitation/create", @params), McpJsonUtil.Settings));
                }
            };
        }

        private static string ResolveLogPath(string fileName)
        {
            var candidates = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!string.IsNullOrEmpty(home))
            {
                candidates.Add(Path.Combine(home, ".config", "unity3d", "Klei", "Oxygen Not Included", fileName));
                candidates.Add(Path.Combine(home, "Library", "Application Support", "unity.Klei.Oxygen Not Included", fileName));
                candidates.Add(Path.Combine(home, "AppData", "LocalLow", "Klei", "Oxygen Not Included", fileName));
            }

            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, "Klei", "Oxygen Not Included", fileName));

            return candidates.FirstOrDefault(File.Exists);
        }

        private static JObject ClientRequest(string method, JObject @params)
        {
            return new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Guid.NewGuid().ToString("N"),
                ["method"] = method,
                ["params"] = @params,
                ["note"] = "This is a client-side MCP request object. OniMcp records client capabilities but does not push requests because GET SSE is disabled."
            };
        }

        private static int ReadInt(JObject args, string key, int fallback, int min, int max)
        {
            int value;
            if (args[key] != null && int.TryParse(args[key].ToString(), out value))
                return Math.Max(min, Math.Min(value, max));
            return fallback;
        }

        private static string[] ReadAllLinesShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd()
                    .Replace("\r\n", "\n")
                    .Split(new[] { '\n' }, StringSplitOptions.None);
            }
        }
    }
}
