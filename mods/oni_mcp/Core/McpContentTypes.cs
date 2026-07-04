using System.Collections.Generic;
using Newtonsoft.Json;

namespace OniMcp.Core
{
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
