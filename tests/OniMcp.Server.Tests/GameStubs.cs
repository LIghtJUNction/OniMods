// Only game/configuration boundaries are stubbed. Every Server implementation and
// protocol type is compiled from production source, including HttpListener transport.
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace UnityEngine
{
    public class MonoBehaviour
    {
        protected object gameObject { get; } = new object();
        protected static void Destroy(object value) { }
        protected static void DontDestroyOnLoad(object value) { }
    }
}

namespace OniMcp.Support
{
    public static class OniMcpLog
    {
        public static void Debug(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { System.Console.Error.WriteLine(message); }
    }
}

namespace OniMcp.Config
{
    public sealed class OniMcpOptions
    {
        public static OniMcpOptions Current { get; private set; } = new OniMcpOptions();
        public static void Save(OniMcpOptions options) { Current = options; }
        public int SecurityMigrationVersion { get; set; }
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; }
        public bool AuthEnabled { get; set; }
        public string AuthToken { get; set; }
        public bool GlobalAutoDisinfectDisabled { get; set; }
        public bool ScreenshotCleanupEnabled { get; set; }
        public int ScreenshotRetentionMinutes { get; set; }
        public int ScreenshotMaxFiles { get; set; }
        public string EndpointUrl => "http://" + Host + ":" + Port + "/mcp/";
        public IEnumerable<string> ListenPrefixes => new[] { "http://" + Host + ":" + Port + "/" };
    }
}

namespace OniMcp.Tools
{
    public static class GameRestartCoordinator { public static void EnsureIntentConsumerStarted() { } }
    public static class CameraTools { public static void CleanupTemporaryScreenshots() { } }
    public static class WorldEditorTools
    {
        public static string ReadFileDirectly(string path) => "test";
        public static object BuildBrowserListing(string path, string endpoint, string version) => new JObject();
    }
    public static class WorldEditor
    {
        public static string LatestScreenshotPath() => null;
        public static string ScreenshotPathForFile(string file) => null;
    }
    public static class OniToolRegistry
    {
        public static int Calls;
        public static string LastName;
        public static JObject LastArguments;
        public static bool ModernToolsEnabled;
        public static bool InvalidModernHeaderSchema;

        public static List<McpToolInfo> GetToolInfos()
        {
            if (!ModernToolsEnabled)
                return new List<McpToolInfo>();

            var benchmarkProperties = new Dictionary<string, SchemaProperty>
            {
                ["task"] = new SchemaProperty { Type = "string", Description = "Visible task description" },
                ["region"] = new SchemaProperty { Type = "string", McpHeader = "Region" },
                ["enabled"] = new SchemaProperty { Type = "boolean", McpHeader = "Enabled" },
                ["limit"] = new SchemaProperty { Type = "integer", McpHeader = "Limit" },
                ["options"] = new SchemaProperty
                {
                    Type = "object",
                    Properties = new Dictionary<string, SchemaProperty>
                    {
                        ["scope"] = new SchemaProperty { Type = "string", McpHeader = "Scope" }
                    }
                }
            };
            if (InvalidModernHeaderSchema)
                benchmarkProperties["ratio"] = new SchemaProperty { Type = "number", McpHeader = "Ratio" };

            return new List<McpToolInfo>
            {
                new McpToolInfo
                {
                    Name = "world_editor",
                    Description = "write-capable stub",
                    Execution = new ToolExecution { TaskSupport = "optional" },
                    InputSchema = new InputSchema { Properties = new Dictionary<string, SchemaProperty>() }
                },
                new McpToolInfo
                {
                    Name = "benchmark",
                    Description = "read-only benchmark stub",
                    Execution = new ToolExecution { TaskSupport = "optional" },
                    InputSchema = new InputSchema
                    {
                        Properties = benchmarkProperties,
                        Required = new List<string> { "task" }
                    }
                }
            };
        }

        public static CallToolResult CallTool(string name, JObject arguments)
        {
            Calls++;
            LastName = name;
            LastArguments = arguments;
            return CallToolResult.Text("ok");
        }
    }
    public static class OniResourceRegistry
    {
        public static List<McpResourceInfo> GetResourceInfos() => new List<McpResourceInfo>
        {
            new McpResourceInfo { Uri = "oni://test", Name = "test", MimeType = "text/plain" },
            new McpResourceInfo { Uri = "oni://测试", Name = "unicode-test", MimeType = "text/plain" }
        };
        public static List<McpResourceTemplateInfo> GetResourceTemplateInfos() => new List<McpResourceTemplateInfo>();
        public static ReadResourceResult ReadResource(string uri) => uri == "oni://test" || uri == "oni://测试"
            ? new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent { Uri = uri, MimeType = "text/plain", Text = "test" }
                }
            }
            : null;
    }
}
