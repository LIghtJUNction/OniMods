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
        public static List<McpToolInfo> GetToolInfos() => new List<McpToolInfo>();
        public static CallToolResult CallTool(string name, JObject arguments)
        {
            Calls++;
            return CallToolResult.Text("ok");
        }
    }
    public static class OniResourceRegistry
    {
        public static List<McpResourceInfo> GetResourceInfos() => new List<McpResourceInfo>();
        public static List<McpResourceTemplateInfo> GetResourceTemplateInfos() => new List<McpResourceTemplateInfo>();
        public static ReadResourceResult ReadResource(string uri) => null;
    }
}
