using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Core
{
    // ============================================================
    // MCP Protocol Types - 基于 Model Context Protocol 规范
    // 使用 Newtonsoft.Json 序列化（游戏自带，无需额外依赖）
    // ============================================================

    /// <summary>
    /// JSON-RPC 2.0 请求
    /// </summary>
    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JObject Params { get; set; }

        public bool IsNotification => Id == null;
    }

    /// <summary>
    /// JSON-RPC 2.0 响应
    /// </summary>
    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public JsonRpcError Error { get; set; }

        public static JsonRpcResponse Success(object id, object result)
        {
            return new JsonRpcResponse { Id = id, Result = result };
        }

        public static JsonRpcResponse MakeError(object id, int code, string message, object data = null)
        {
            return new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError { Code = code, Message = message, Data = data }
            };
        }
    }

    public class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public object Data { get; set; }
    }

    /// <summary>
    /// MCP 错误码
    /// </summary>
    public static class McpErrorCode
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }

    // ============================================================
    // MCP 协议消息类型
    // ============================================================

    /// <summary>
    /// initialize 请求参数
    /// </summary>
    public class InitializeParams
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public ClientCapabilities Capabilities { get; set; }

        [JsonProperty("clientInfo")]
        public Implementation ClientInfo { get; set; }
    }

    public class ClientCapabilities
    {
        [JsonProperty("roots")]
        public object Roots { get; set; }

        [JsonProperty("sampling")]
        public object Sampling { get; set; }

        [JsonProperty("elicitation")]
        public object Elicitation { get; set; }

        [JsonProperty("tasks")]
        public object Tasks { get; set; }

        [JsonProperty("experimental")]
        public JObject Experimental { get; set; }
    }

    public class Implementation
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// initialize 响应结果
    /// </summary>
    public class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2025-11-25";

        [JsonProperty("capabilities")]
        public ServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public Implementation ServerInfo { get; set; }
    }

    public class ServerCapabilities
    {
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public ToolsCapability Tools { get; set; }

        [JsonProperty("prompts", NullValueHandling = NullValueHandling.Ignore)]
        public PromptsCapability Prompts { get; set; }

        [JsonProperty("resources", NullValueHandling = NullValueHandling.Ignore)]
        public ResourcesCapability Resources { get; set; }

        [JsonProperty("tasks", NullValueHandling = NullValueHandling.Ignore)]
        public TasksCapability Tasks { get; set; }

        [JsonProperty("experimental", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Experimental { get; set; }
    }

    public class ToolsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class PromptsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class ResourcesCapability
    {
        [JsonProperty("subscribe")]
        public bool Subscribe { get; set; }

        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    public class TasksCapability
    {
        [JsonProperty("list")]
        public object List { get; set; } = new JObject();

        [JsonProperty("cancel")]
        public object Cancel { get; set; } = new JObject();

        [JsonProperty("requests")]
        public JObject Requests { get; set; }
    }

    // ============================================================
    // Tool 相关类型
    // ============================================================

    /// <summary>
    /// Tool 列表响应
    /// </summary>
    public class ListToolsResult
    {
        [JsonProperty("tools")]
        public List<McpToolInfo> Tools { get; set; }
    }

    public class McpToolInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public InputSchema InputSchema { get; set; }

        [JsonProperty("execution", NullValueHandling = NullValueHandling.Ignore)]
        public ToolExecution Execution { get; set; }
    }

    public class ToolExecution
    {
        [JsonProperty("taskSupport")]
        public string TaskSupport { get; set; }
    }

    public class InputSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, SchemaProperty> Properties { get; set; }

        [JsonProperty("required")]
        public List<string> Required { get; set; }
    }

    public class SchemaProperty
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enum")]
        public List<object> Enum { get; set; }
    }

    /// <summary>
    /// Tool 调用请求
    /// </summary>
    public class CallToolParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public JObject Arguments { get; set; }

        [JsonProperty("task")]
        public TaskRequest Task { get; set; }
    }

    public class TaskRequest
    {
        [JsonProperty("ttl")]
        public int? Ttl { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("metadata")]
        public JObject Metadata { get; set; }
    }

    public class TaskIdParams
    {
        [JsonProperty("taskId")]
        public string TaskId { get; set; }
    }

    public class McpTaskInfo
    {
        [JsonProperty("taskId")]
        public string TaskId { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("statusMessage", NullValueHandling = NullValueHandling.Ignore)]
        public string StatusMessage { get; set; }

        [JsonProperty("createdAt")]
        public string CreatedAt { get; set; }

        [JsonProperty("lastUpdatedAt")]
        public string LastUpdatedAt { get; set; }

        [JsonProperty("ttl", NullValueHandling = NullValueHandling.Ignore)]
        public int? Ttl { get; set; }

        [JsonProperty("pollInterval", NullValueHandling = NullValueHandling.Ignore)]
        public int? PollInterval { get; set; }
    }

    public class CreateTaskResult
    {
        [JsonProperty("task")]
        public McpTaskInfo Task { get; set; }

        [JsonProperty("_meta", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Meta { get; set; }
    }

    public class ListTasksResult
    {
        [JsonProperty("tasks")]
        public List<McpTaskInfo> Tasks { get; set; }
    }

    public class TaskResult
    {
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("_meta")]
        public JObject Meta { get; set; }
    }

    // ============================================================
    // Prompt 相关类型
    // ============================================================

    public class ListPromptsResult
    {
        [JsonProperty("prompts")]
        public List<McpPromptInfo> Prompts { get; set; }
    }

    public class McpPromptInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("arguments", NullValueHandling = NullValueHandling.Ignore)]
        public List<McpPromptArgument> Arguments { get; set; }
    }

    public class McpPromptArgument
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }
    }

    public class GetPromptParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public Dictionary<string, string> Arguments { get; set; }
    }

    public class GetPromptResult
    {
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("messages")]
        public List<PromptMessage> Messages { get; set; }
    }

    public class PromptMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public ToolContent Content { get; set; }
    }

    // ============================================================
    // Resource 相关类型
    // ============================================================

    public class ListResourcesResult
    {
        [JsonProperty("resources")]
        public List<McpResourceInfo> Resources { get; set; }
    }

    public class ListResourceTemplatesResult
    {
        [JsonProperty("resourceTemplates")]
        public List<McpResourceTemplateInfo> ResourceTemplates { get; set; }
    }

    public class McpResourceInfo
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }

    public class McpResourceTemplateInfo
    {
        [JsonProperty("uriTemplate")]
        public string UriTemplate { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("title", NullValueHandling = NullValueHandling.Ignore)]
        public string Title { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }

    public class ReadResourceParams
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }

    public class ReadResourceResult
    {
        [JsonProperty("contents")]
        public List<TextResourceContent> Contents { get; set; }
    }

    public class TextResourceContent
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; } = "application/json";

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    /// <summary>
    /// Tool 调用结果
    /// </summary>
    public class CallToolResult
    {
        [JsonProperty("content")]
        public List<ToolContent> Content { get; set; }

        [JsonProperty("isError")]
        public bool IsError { get; set; }

        public static CallToolResult Text(string text)
        {
            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = text } }
            };
        }

        public static CallToolResult Error(string message)
        {
            return new CallToolResult
            {
                Content = new List<ToolContent> { new ToolContent { Text = message } },
                IsError = true
            };
        }
    }

    public class ToolContent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text")]
        public string Text { get; set; }
    }

}
