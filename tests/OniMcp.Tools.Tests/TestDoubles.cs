using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

// Only game/registry boundaries are substituted. The batch, DSL, response types,
// and both regex entry points are compiled directly from production source.
namespace OniMcp.Tools
{
    public sealed class McpTool
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Mode { get; set; }
        public string Risk { get; set; }
        public string Description { get; set; }
        public List<string> Aliases { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, McpToolParameter> Parameters { get; set; }
        public Func<JObject, CallToolResult> Handler { get; set; }
    }

    public sealed class McpToolParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public List<string> EnumValues { get; set; }
    }

    public static class OniToolRegistry
    {
        internal static readonly Dictionary<string, McpTool> Tools = new Dictionary<string, McpTool>(StringComparer.OrdinalIgnoreCase);
        public static bool TryGetTool(string name, out McpTool tool) => Tools.TryGetValue(name, out tool);
        public static CallToolResult CallTool(string name, JObject arguments) => Tools[name].Handler(arguments);
    }

    public static class ToolUtil
    {
        public static bool GetBool(JObject args, string name, bool fallback) => args[name]?.Value<bool>() ?? fallback;
        public static int? GetInt(JObject args, string name) => args[name]?.Value<int?>();
    }

    public static class ServerTools
    {
        internal static bool IsServerControlDomainCall(string name, JObject args, params string[] domains)
        {
            McpTool tool;
            return OniToolRegistry.TryGetTool(name, out tool) && tool.Name == "server_control"
                && domains.Contains((args?["domain"]?.ToString() ?? "diagnostics").Trim().ToLowerInvariant());
        }
    }

    public static partial class WorldEditorTools
    {
        private const string _cwd = "/active/";
        internal static string ReadText;
        internal static CallToolResult RunGrep(JObject args) => Grep(args);
        internal static bool MatchToken(string actual, string pattern) => SearchTokenMatches(actual, pattern);
        private static string NormalizePath(string path, string cwd) => string.IsNullOrEmpty(path) ? cwd : path;
        private static string Text(JObject args, params string[] keys) => keys.Select(key => args[key]?.ToString()).FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? "";
        private static JObject CopyPayload(JObject args) => (JObject)args.DeepClone();
        private static CallToolResult Read(JObject args) => CallToolResult.Text(ReadText);
        private static CallToolResult SearchGlyphs(JObject args) => throw new NotSupportedException();
        private static List<JObject> BuildSymbolRows() => throw new NotSupportedException();
        private static char GetUniqueChar(string id, string name) => throw new NotSupportedException();
        private static string MapTokenPart(string token) => token;
        private static readonly Dictionary<string, char> UniqueCharMap = new Dictionary<string, char>();
        private sealed class MapEditCell
        {
            public int X { get; set; }
            public int Y { get; set; }
        }
    }
}

internal enum SimHashes { TestElement }
internal static class Assets
{
    internal static readonly List<TestBuildingDef> BuildingDefs = new List<TestBuildingDef>();
}
internal sealed class TestBuildingDef
{
    public string PrefabID { get; set; }
    public string Name { get; set; }
}
