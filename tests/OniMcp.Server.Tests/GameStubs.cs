// Only game/configuration boundaries are stubbed. Every Server implementation and
// protocol type is compiled from production source, including HttpListener transport.
using System;
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

public sealed class Game
{
    public static Game Instance { get; set; }
    public bool Loading { get; set; }
    public bool IsLoading() { return Loading; }
}

public static class Grid
{
    public static int CellCount { get; set; } = 1;
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
    public sealed class McpTool
    {
        public string Name { get; set; }
        public Func<JObject, CallToolResult> Handler { get; set; }
    }
    public static class ToolCallMiddleware
    {
        public const string TaskDescriptionParameter = "task";
        public static int Presentations;

        public static bool TryGetTaskDescription(JObject arguments, out string description)
        {
            var token = arguments?[TaskDescriptionParameter];
            description = token?.Type == JTokenType.String ? token.Value<string>()?.Trim() : null;
            return !string.IsNullOrWhiteSpace(description);
        }

        public static void PresentTaskDescription(string description)
        {
            Presentations++;
        }

        public static CallToolResult MissingTaskDescription(string toolName,
            List<Dictionary<string, object>> notifications)
        {
            return CallToolResult.Error("task is required: describe what you are doing before every tool call.");
        }
    }
    public static class OniToolRegistry
    {
        public static int Calls;
        public static Action<string, JObject> OnCall;
        public static int MiddlewareCalls;
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

        public static bool TryGetTool(string name, out McpTool tool)
        {
            tool = null;
            if (!ModernToolsEnabled || !string.Equals(name, "benchmark", StringComparison.Ordinal))
                return false;

            tool = new McpTool
            {
                Name = "benchmark",
                Handler = arguments =>
                {
                    Calls++;
                    LastName = "benchmark";
                    LastArguments = arguments;
                    return CallToolResult.Text("ok");
                }
            };
            return true;
        }

        public static bool HasCoordinateArguments(JToken token)
        {
            if (token == null)
                return false;
            if (token.Type == JTokenType.Object)
            {
                foreach (var property in ((JObject)token).Properties())
                {
                    if (IsCoordinateParameter(property.Name) || HasCoordinateArguments(property.Value))
                        return true;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    if (HasCoordinateArguments(item))
                        return true;
                }
            }
            return false;
        }

        public static bool IsCoordinateTool(string name)
        {
            return string.Equals(name, "coordinate_control", StringComparison.Ordinal)
                || string.Equals(name, "world_editor", StringComparison.Ordinal);
        }

        private static bool IsCoordinateParameter(string name)
        {
            switch (name)
            {
                case "x": case "y": case "x1": case "y1": case "x2": case "y2":
                case "dx": case "dy": case "cell": case "cells": case "points": case "anchors":
                    return true;
                default:
                    return false;
            }
        }

        public static CallToolResult CallTool(string name, JObject arguments)
        {
            MiddlewareCalls++;
            string taskDescription;
            if (ToolCallMiddleware.TryGetTaskDescription(arguments, out taskDescription))
                ToolCallMiddleware.PresentTaskDescription(taskDescription);
            Calls++;
            LastName = name;
            LastArguments = arguments;
            OnCall?.Invoke(name, arguments);
            return CallToolResult.Text("ok");
        }
    }
    public static class OniResourceRegistry
    {
        public static int ResourceReads;
        public static int CatalogReads;

        public static List<McpResourceInfo> GetResourceInfos() => new List<McpResourceInfo>
        {
            new McpResourceInfo { Uri = "oni://test", Name = "test", MimeType = "text/plain" },
            new McpResourceInfo { Uri = "oni://tools/manifest", Name = "server_control", MimeType = "application/json" },
            new McpResourceInfo { Uri = "oni://mcp/sessions", Name = "server_control", MimeType = "application/json" },
            new McpResourceInfo { Uri = "oni://测试", Name = "unicode-test", MimeType = "text/plain" },
            new McpResourceInfo { Uri = "oni://world/coordinate-screenshot", Name = "navigation_control", MimeType = "application/json" }
        };

        public static List<McpResourceTemplateInfo> GetResourceTemplateInfos() => new List<McpResourceTemplateInfo>
        {
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://template/{id}/data",
                Name = "template-test",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/coordinate-screenshot{?filename}",
                Name = "navigation_control",
                MimeType = "application/json"
            }
        };

        public static ReadResourceResult ReadResource(string uri)
        {
            if (uri != null && (uri.StartsWith("oni://tools/manifest", StringComparison.Ordinal)
                || uri == "oni://mcp/sessions"))
            {
                CatalogReads++;
                return new ReadResourceResult
                {
                    Contents = new List<TextResourceContent>
                    {
                        new TextResourceContent { Uri = uri, MimeType = "application/json", Text = "{\"catalog\":true}" }
                    }
                };
            }

            if (uri == "oni://world/coordinate-screenshot")
            {
                ResourceReads++;
                return new ReadResourceResult
                {
                    Contents = new List<TextResourceContent>
                    {
                        new TextResourceContent
                        {
                            Uri = uri,
                            MimeType = "application/json",
                            Text = "{\"queued\":true,\"sideEffect\":true}"
                        }
                    }
                };
            }

            if (uri == "oni://test" || uri == "oni://测试")
            {
                return new ReadResourceResult
                {
                    Contents = new List<TextResourceContent>
                    {
                        new TextResourceContent { Uri = uri, MimeType = "text/plain", Text = "test" }
                    }
                };
            }

            const string templatePrefix = "oni://template/";
            const string templateSuffix = "/data";
            if (uri != null && uri.StartsWith(templatePrefix) && uri.EndsWith(templateSuffix))
            {
                string id = uri.Substring(templatePrefix.Length,
                    uri.Length - templatePrefix.Length - templateSuffix.Length);
                if (id.Length > 0)
                {
                    return new ReadResourceResult
                    {
                        Contents = new List<TextResourceContent>
                        {
                            new TextResourceContent
                            {
                                Uri = uri,
                                MimeType = "application/json",
                                Text = "{\"id\":\"" + id + "\",\"templateTest\":true}"
                            }
                        }
                    };
                }
            }

            return null;
        }
    }
}
