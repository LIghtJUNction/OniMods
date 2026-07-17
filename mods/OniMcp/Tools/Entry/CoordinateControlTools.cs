using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class CoordinateControlTools
    {
        internal static McpTool ControlCoordinate()
        {
            return new McpTool
            {
                Name = "coordinate_control",
                Group = "coordinates",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "coordinate_gateway", "coordinate_tool" },
                Tags = new List<string> { "coordinates", "fallback", "gateway", "debug" },
                Description = "Coordinate auxiliary entrypoint. Only this public tool accepts raw x/y, rectangles, cells, points, and anchors. It forwards to targetTool with payload; wrapper actions like cell_ref/anchors_ref adapt coordinates without overriding payload.action.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["targetTool"] = new McpToolParameter { Type = "string", Description = "Underlying tool to forward to, e.g. read_control, navigation_control, building_control.", Required = true },
                    ["payload"] = new McpToolParameter { Type = "object", Description = "Argument object forwarded to targetTool. Top-level coordinate fields take precedence.", Required = false },
                    ["domain"] = new McpToolParameter { Type = "string", Description = "Optional target domain merged into payload.", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "Optional target action. Wrapper actions cell_ref/anchors_ref/points_ref are consumed by coordinate_control.", Required = false },
                    ["x"] = new McpToolParameter { Type = "number", Description = "Target X coordinate.", Required = false },
                    ["y"] = new McpToolParameter { Type = "number", Description = "Target Y coordinate.", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "Rectangle start X.", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "Rectangle start Y.", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "Rectangle end X.", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "Rectangle end Y.", Required = false },
                    ["dx"] = new McpToolParameter { Type = "number", Description = "Relative X offset.", Required = false },
                    ["dy"] = new McpToolParameter { Type = "number", Description = "Relative Y offset.", Required = false },
                    ["cell"] = new McpToolParameter { Type = "integer", Description = "Target cell index.", Required = false },
                    ["cells"] = new McpToolParameter { Type = "array", Description = "Cell index array.", Required = false },
                    ["points"] = new McpToolParameter { Type = "array", Description = "Coordinate path point array, e.g. [[x,y], ...] or [{x,y}, ...].", Required = false },
                    ["anchors"] = new McpToolParameter { Type = "array", Description = "Coordinate anchor array, e.g. [[x,y], ...] or [{x,y}, ...].", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Dangerous or write operations still follow target tool confirmation rules.", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview only when supported by target tool.", Required = false },
                    ["moveCamera"] = new McpToolParameter { Type = "boolean", Description = "Whether to move camera to coordinate center after execution.", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "Camera zoom level when moveCamera is true.", Required = false },
                    ["cameraMoveMode"] = new McpToolParameter { Type = "string", Description = "Camera move mode: snap or smooth. Default snap.", Required = false, EnumValues = new List<string> { "snap", "smooth" } }
                },
                Handler = args =>
                {
                    string targetTool = (args["targetTool"]?.ToString() ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(targetTool))
                        return CallToolResult.Error("targetTool is required");
                    if (OniToolRegistry.IsCoordinateTool(targetTool))
                        return CallToolResult.Error("coordinate_control cannot target itself");
                    if (!OniToolRegistry.TryGetOperation(targetTool, out var tool))
                        return CallToolResult.Error("targetTool not found: " + targetTool);

                    JObject forwarded = ReadPayload(args);
                    string wrapperAction = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    bool coordinateWrapper = IsCoordinateWrapperAction(wrapperAction);

                    MergeIfPresent(args, forwarded, "domain");
                    if (!coordinateWrapper)
                        MergeIfPresent(args, forwarded, "action");
                    MergeCoordinateFields(args, forwarded);
                    ApplyCoordinateWrapperDefaults(targetTool, forwarded, coordinateWrapper);

                    if (GetBool(args, "moveCamera"))
                        MoveCameraToCoordinateCenter(args);

                    return tool.Handler(forwarded);
                }
            };
        }

        private static JObject ReadPayload(JObject args)
        {
            if (args["payload"] is JObject obj)
                return (JObject)obj.DeepClone();
            if (args["payload"]?.Type == JTokenType.String)
            {
                try { return JObject.Parse(args["payload"].ToString()); }
                catch { }
            }
            return new JObject();
        }

        private static void MergeCoordinateFields(JObject source, JObject destination)
        {
            string[] keys =
            {
                "x", "y", "x1", "y1", "x2", "y2", "dx", "dy",
                "cell", "cells", "points", "anchors", "confirm", "dryRun"
            };
            foreach (string key in keys)
                MergeIfPresent(source, destination, key);
        }

        private static void MergeIfPresent(JObject source, JObject destination, string key)
        {
            if (source.TryGetValue(key, out var value) && value != null)
                destination[key] = value.DeepClone();
        }

        private static bool IsCoordinateWrapperAction(string action)
        {
            switch (action)
            {
                case "cell_ref":
                case "cells_ref":
                case "point_ref":
                case "points_ref":
                case "anchor_ref":
                case "anchors_ref":
                case "rect_ref":
                case "area_ref":
                case "forward":
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyCoordinateWrapperDefaults(string targetTool, JObject forwarded, bool coordinateWrapper)
        {
            if (!coordinateWrapper)
                return;

            string tool = (targetTool ?? string.Empty).Trim().ToLowerInvariant();
            bool hasAction = !string.IsNullOrWhiteSpace(forwarded["action"]?.ToString());
            bool hasDomain = !string.IsNullOrWhiteSpace(forwarded["domain"]?.ToString());

            if ((tool == "building_control" || tool == "buildings_control") && !hasAction
                && (forwarded["points"] != null || forwarded["anchors"] != null))
            {
                if (!hasDomain)
                    forwarded["domain"] = "planning";
                forwarded["action"] = "auto_connect";
            }

            if ((tool == "read_control" || tool == "world_editor") && !hasAction
                && (forwarded["x"] != null || forwarded["y"] != null || forwarded["cell"] != null))
            {
                if (!hasDomain)
                    forwarded["domain"] = "world";
                forwarded["action"] = "cell_info";
            }
        }

        private static bool GetBool(JObject args, string key)
        {
            if (!args.TryGetValue(key, out var token) || token == null)
                return false;
            if (token.Type == JTokenType.Boolean)
                return (bool)token;
            return bool.TryParse(token.ToString(), out var value) && value;
        }

        private static void MoveCameraToCoordinateCenter(JObject args)
        {
            float? centerX = ReadFloat(args, "x");
            float? centerY = ReadFloat(args, "y");

            if (!centerX.HasValue)
            {
                float? x1 = ReadFloat(args, "x1");
                float? x2 = ReadFloat(args, "x2");
                if (x1.HasValue && x2.HasValue)
                    centerX = (x1.Value + x2.Value) / 2f;
            }
            if (!centerY.HasValue)
            {
                float? y1 = ReadFloat(args, "y1");
                float? y2 = ReadFloat(args, "y2");
                if (y1.HasValue && y2.HasValue)
                    centerY = (y1.Value + y2.Value) / 2f;
            }
            if (!centerX.HasValue || !centerY.HasValue)
                return;

            float? zoom = ReadFloat(args, "zoom");
            string moveMode = (args["cameraMoveMode"]?.ToString() ?? "snap").Trim().ToLowerInvariant();
            OniMcp.Server.MainThreadBridge.Enqueue(() =>
            {
                var cameraController = CameraController.Instance;
                if (cameraController == null || Camera.main == null)
                    return;
                float finalZoom = zoom ?? Camera.main.orthographicSize;
                var targetPos = new Vector3(centerX.Value, centerY.Value, -100f);
                if (moveMode == "smooth")
                {
                    cameraController.CameraGoTo(targetPos, 0.5f, true);
                    Camera.main.orthographicSize = finalZoom;
                }
                else
                {
                    cameraController.SnapTo(targetPos, finalZoom);
                }
            });
        }

        private static float? ReadFloat(JObject args, string key)
        {
            if (args.TryGetValue(key, out var token) && float.TryParse(token.ToString(), out float value))
                return value;
            return null;
        }
    }
}
