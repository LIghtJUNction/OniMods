using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// Invokes a tool for the stateless modern read-only transport without
    /// consuming legacy process-wide middleware notifications.
    ///
    /// The caller is responsible for applying the modern exact-name allowlist
    /// before entering this helper. Task visibility and coordinate safety stay
    /// aligned with normal tool calls; only notification ownership is isolated.
    /// </summary>
    internal static class StatelessReadOnlyToolInvoker
    {
        internal static CallToolResult Call(string name, JObject arguments)
        {
            if (string.IsNullOrWhiteSpace(name))
                return CallToolResult.Error("Tool name is required.");
            if (!OniToolRegistry.TryGetTool(name, out var tool))
                return CallToolResult.Error($"Tool not found: {name}");

            var noNotifications = new List<Dictionary<string, object>>();
            try
            {
                if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out var taskDescription))
                    return ToolCallMiddleware.MissingTaskDescription(tool.Name, noNotifications);

                ToolCallMiddleware.PresentTaskDescription(taskDescription);

                if (!OniToolRegistry.IsCoordinateTool(tool.Name) && OniToolRegistry.HasCoordinateArguments(arguments))
                {
                    return CallToolResult.Error(
                        "Coordinate arguments are only supported by coordinate_control; use semantic query/target/areaId inputs for this tool.");
                }

                return tool.Handler(arguments ?? new JObject());
            }
            catch (Exception ex)
            {
                return CallToolResult.Error($"Tool execution error: {ex.Message}");
            }
        }
    }
}
