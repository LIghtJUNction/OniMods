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
    public static class ServerTools
    {
        public static McpTool GetMcpStatus()
        {
            return new McpTool
            {
                Name = "server_status",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_mcp_status" },
                Description = "获取 ONI MCP 服务器状态、监听地址、默认暴露工具数量和完整注册工具数量",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    var server = McpHttpServer.Instance;
                    var status = new Dictionary<string, object>
                    {
                        ["loaded"] = server != null,
                        ["endpoint"] = server?.EndpointUrl,
                        ["port"] = server?.Port ?? 0,
                        ["configPath"] = OniMcpOptions.ConfigPath,
                        ["toolCount"] = OniToolRegistry.GetTools().Count,
                        ["listedToolCount"] = OniToolRegistry.GetDefaultToolInfoCount(),
                        ["toolsListMode"] = "core",
                        ["discovery"] = "tools/list returns core routing tools; use tools_search detail=full or tools_manifest for all registered tools. Hidden tools remain callable by name."
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(status, McpJsonUtil.Settings));
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
                Aliases = new List<string> { "tail_logs", "player_log" },
                Description = "查看 ONI Player.log 或 Player-prev.log 的末尾内容，可按关键词过滤",
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
                Aliases = new List<string> { "client_capabilities", "server_client_capabilities" },
                Description = "列出当前 MCP 会话声明的客户端能力，包括 sampling、elicitation 和 tasks",
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
                Aliases = new List<string> { "sampling_request_create", "client_sampling_request" },
                Description = "生成 sampling/createMessage 客户端请求对象；需要 MCP 客户端声明 sampling 后由客户端侧执行",
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

        public static McpTool CreateElicitationRequest()
        {
            return new McpTool
            {
                Name = "mcp_elicitation_request_create",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "elicitation_request_create", "client_elicitation_request" },
                Description = "生成 elicitation/create 客户端请求对象，用于向用户索取结构化确认或输入",
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
