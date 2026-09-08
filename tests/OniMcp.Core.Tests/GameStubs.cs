using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

// Only game tool factories and the Unity overlay are replaced. Tests execute the
// production registries, route tables, middleware and JSON-RPC serialization.
namespace OniMcp.Tools
{
    internal static class GameStubs
    {
        internal static int HandlerCalls;
        internal static bool FailNextFactory;

        internal static McpTool Tool(string name)
        {
            if (FailNextFactory)
            {
                FailNextFactory = false;
                throw new InvalidOperationException("factory failure");
            }

            return new McpTool
            {
                Name = name,
                Mode = "execute",
                Aliases = new List<string> { "legacy_" + name },
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string" }
                },
                Handler = arguments =>
                {
                    HandlerCalls++;
                    if ((string)arguments["query"] == "throw")
                        throw new InvalidOperationException("handler failure");
                    if ((string)arguments["query"] == "error")
                        return CallToolResult.Error("handler error");
                    if ((string)arguments["query"] == "null")
                        return null;
                    return CallToolResult.Text(new JObject
                    {
                        ["tool"] = name,
                        ["arguments"] = arguments.DeepClone()
                    }.ToString());
                }
            };
        }
    }

    internal static class ToolCallSpeechOverlay
    {
        internal static int Presentations;
        internal static void ShowNearPlayerMouse(string description) { Presentations++; }
        internal static void NotifyMissingDescription(string toolName) { }
    }

    internal static class CoreToolEnglishDescriptions { internal static McpTool Apply(McpTool tool) => tool; }
    internal static class ServerControlEntryTools { internal static McpTool ControlServer() => GameStubs.Tool("server_control"); }
    internal static class WorldEditorTools { internal static McpTool ControlWorldEditor() => GameStubs.Tool("world_editor"); }
    internal static class NavigationControlTools { internal static McpTool ControlNavigation() => GameStubs.Tool("navigation_control"); }
    internal static class BuildingControlTools { internal static McpTool ControlBuilding() => GameStubs.Tool("building_control"); }
    internal static class GameControlEntryTools { internal static McpTool ControlGame() => GameStubs.Tool("game_control"); }
    internal static class OrdersControlEntryTools { internal static McpTool ControlOrders() => GameStubs.Tool("orders_control"); }
    internal static class BenchmarkTools { internal static McpTool Benchmark() => GameStubs.Tool("benchmark"); }
    internal static class ColonyTools { internal static McpTool ControlColony() => GameStubs.Tool("colony_control"); }
    internal static class DuplicantTools { internal static McpTool ControlDupes() => GameStubs.Tool("dupes_control"); }
    internal static class ReadTools { internal static McpTool ControlRead() => GameStubs.Tool("read_control"); }
    internal static class SearchControlTools { internal static McpTool ControlSearch() => GameStubs.Tool("search_control"); }
    internal static class CoordinateControlTools { internal static McpTool ControlCoordinate() => GameStubs.Tool("coordinate_control"); }
}
