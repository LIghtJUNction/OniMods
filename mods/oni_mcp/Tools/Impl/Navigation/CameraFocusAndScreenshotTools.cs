using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Config;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class CameraTools
    {
        public static McpTool SwitchView()
        {
            return new McpTool
            {
                Name = "camera_switch_view",
                Hidden = true,
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "兼容入口：请优先使用 navigation_control action=switch_view",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["view"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "目标视图：none、oxygen、power、gas_conduits、liquid_conduits、solid_conveyor、logic、temperature、heat_flow、thermal_conductivity、materials、light、decor、rooms、priorities、disease、radiation、sound、suit、crop、harvest",
                        Required = true,
                        EnumValues = OverlayViewNames
                    },
                    ["screenshot"] = new McpToolParameter { Type = "boolean", Description = "是否保存切换后的截图，默认 true", Required = false },
                    ["filename"] = new McpToolParameter { Type = "string", Description = "截图文件名，仅 screenshot=true 时使用", Required = false },
                    ["waitFrames"] = new McpToolParameter { Type = "integer", Description = "截图前等待的 Unity 帧数，默认 2，确保覆盖层完成渲染", Required = false },
                    ["allowSound"] = new McpToolParameter { Type = "boolean", Description = "是否播放游戏视图切换音效，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var overlay = OverlayScreen.Instance;
                    if (overlay == null)
                        return CallToolResult.Error("OverlayScreen not available");

                    string requestedView = args["view"]?.ToString();
                    if (string.IsNullOrEmpty(requestedView))
                        return CallToolResult.Error("view is required");

                    if (!TryResolveOverlayView(requestedView, out var viewName, out var mode))
                    {
                        return CallToolResult.Error($"Unknown view: {requestedView}. Available views: {string.Join(", ", OverlayViewNames)}");
                    }

                    bool allowSound = ToolUtil.GetBool(args, "allowSound", false);
                    bool screenshot = ToolUtil.GetBool(args, "screenshot", true);
                    int waitFrames = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "waitFrames") ?? 2, 30));
                    var queued = OverlaySwitchQueue.Enqueue(
                        viewName,
                        mode,
                        allowSound,
                        screenshot,
                        args["filename"]?.ToString(),
                        waitFrames);

                    var camera = CameraController.Instance;
                    var pos = camera != null ? camera.transform.GetPosition() : Vector3.zero;
                    var result = new Dictionary<string, object>
                    {
                        ["view"] = viewName,
                        ["requestedOverlay"] = mode.ToString(),
                        ["activeOverlayAtEnqueue"] = overlay.GetMode().ToString(),
                        ["queued"] = true,
                        ["queue"] = queued,
                        ["activeWorldId"] = ClusterManager.Instance?.activeWorldId ?? -1,
                        ["camera"] = new
                        {
                            position = new { x = Math.Round(pos.x, 2), y = Math.Round(pos.y, 2), z = Math.Round(pos.z, 2) },
                            orthographicSize = Camera.main != null ? Math.Round(Camera.main.orthographicSize, 2) : 0
                        },
                        ["screen"] = new { width = Screen.width, height = Screen.height }
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FocusCell()
        {
            return new McpTool
            {
                Name = "camera_focus_cell",
                Hidden = true,
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "兼容入口：请优先使用 navigation_control action=focus_cell",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "格子 X 坐标", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "格子 Y 坐标", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "相机正交缩放，默认 8", Required = false }
                },
                Handler = args =>
                {
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int cell = Grid.XYToCell(x.Value, y.Value);
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Cell ({x.Value},{y.Value}) is not in worldId={worldId}");

                    float zoom = ToolUtil.GetFloat(args, "zoom") ?? 8f;
                    CameraController.Instance?.SnapTo(new Vector3(x.Value + 0.5f, y.Value + 0.5f, -100f), zoom);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["focused"] = new { x = x.Value, y = y.Value },
                        ["worldId"] = worldId,
                        ["zoom"] = Math.Round(zoom, 2)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FocusDupe()
        {
            return new McpTool
            {
                Name = "camera_focus_dupe",
                Hidden = true,
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "兼容入口：请优先使用 navigation_control action=focus_dupe",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    CameraController.Instance?.SetFollowTarget(dupe.transform);
                    return CallToolResult.Text($"Camera following {dupe.GetProperName()}");
                }
            };
        }

        public static McpTool TakeScreenshot()
        {
            return new McpTool
            {
                Name = "game_screenshot",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Description = "兼容入口：请优先使用 navigation_control action=screenshot。需要格子坐标时使用 navigation_control action=coordinate_screenshot。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["filename"] = new McpToolParameter { Type = "string", Description = "可选文件名，默认自动按周期和时间生成", Required = false }
                },
                Handler = args =>
                {
                    var result = WorldEditor.SaveScreenshot(args["filename"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool TakeCoordinateScreenshot()
        {
            return new McpTool
            {
                Name = "camera_coordinate_screenshot",
                Hidden = true,
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "world_coordinate_screenshot", "map_coordinate_screenshot", "coordinate_screenshot" },
                Tags = new List<string> { "camera", "screenshot", "coordinates", "grid", "vision", "map", "截图", "坐标" },
                Description = "兼容入口：请优先使用 navigation_control action=coordinate_screenshot。给视觉模型优先用坐标截图：图片里直接有 x/y 坐标、格线和 HTTP URL。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界或 areaId 绑定世界", Required = false },
                    ["view"] = new McpToolParameter { Type = "string", Description = "截图前切换覆盖层：none、oxygen、power、gas_conduits、liquid_conduits、solid_conveyor、logic、temperature 等；默认不切换", Required = false, EnumValues = OverlayViewNames },
                    ["focusCamera"] = new McpToolParameter { Type = "boolean", Description = "是否自动移动相机覆盖该区域，默认 true", Required = false },
                    ["paddingCells"] = new McpToolParameter { Type = "number", Description = "自动对焦时四周留白格数，默认 1.5", Required = false },
                    ["showGrid"] = new McpToolParameter { Type = "boolean", Description = "是否显示格线，默认 true", Required = false },
                    ["showCoordinates"] = new McpToolParameter { Type = "boolean", Description = "是否在边缘显示 x/y 坐标，默认 true", Required = false },
                    ["includeCellLabels"] = new McpToolParameter { Type = "boolean", Description = "是否在格心稀疏标注 x,y，默认按区域大小自动开启/关闭", Required = false },
                    ["step"] = new McpToolParameter { Type = "integer", Description = "坐标标签步长，默认按区域大小自动选择", Required = false },
                    ["waitFrames"] = new McpToolParameter { Type = "integer", Description = "截图前等待帧数，默认 3，最大 30", Required = false },
                    ["filename"] = new McpToolParameter { Type = "string", Description = "可选截图文件名", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (Camera.main == null || CameraController.Instance == null)
                        return CallToolResult.Error("Camera not available");

                    int maxCells = 3600;
                    var rect = WorldEditor.ResolveRectOrCamera(args, maxCells);
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (cells > maxCells)
                        return CallToolResult.Error($"Area too large for coordinate screenshot: {width}x{height}={cells}, maxCells={maxCells}");

                    int worldId = WorldEditor.ResolveWorldId(args);
                    int centerCell = Grid.XYToCell((rect["x1"] + rect["x2"]) / 2, (rect["y1"] + rect["y2"]) / 2);
                    if (Grid.IsValidCell(centerCell) && !ToolUtil.CellMatchesWorld(centerCell, worldId))
                        return CallToolResult.Error($"Target rectangle center is not in worldId={worldId}");

                    string requestedView = args["view"]?.ToString();
                    string viewName = "current";
                    if (!string.IsNullOrWhiteSpace(requestedView))
                    {
                        if (!TryResolveOverlayView(requestedView, out viewName, out var mode))
                            return CallToolResult.Error($"Unknown view: {requestedView}. Available views: {string.Join(", ", OverlayViewNames)}");
                        ApplyOverlayMode(mode, false);
                    }

                    bool focusCamera = ToolUtil.GetBool(args, "focusCamera", true);
                    float padding = Math.Max(0f, ToolUtil.GetFloat(args, "paddingCells") ?? 1.5f);
                    if (focusCamera)
                    {
                        float centerX = (rect["x1"] + rect["x2"]) * 0.5f;
                        float centerY = (rect["y1"] + rect["y2"]) * 0.5f;
                        float aspect = Math.Max(0.5f, Screen.width / Math.Max(1f, (float)Screen.height));
                        float vertical = (height * 0.5f) + padding;
                        float horizontalAsVertical = ((width * 0.5f) + padding) / aspect;
                        float zoom = Math.Max(4f, Math.Max(vertical, horizontalAsVertical));
                        CameraController.Instance.SnapTo(new Vector3(centerX, centerY, -100f), zoom);
                    }

                    int autoStep = cells <= 180 ? 1 : cells <= 900 ? 2 : 5;
                    int step = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "step") ?? autoStep, 25));
                    bool includeCellLabels = ToolUtil.GetBool(args, "includeCellLabels", cells <= 180);
                    int waitFrames = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "waitFrames") ?? 3, 30));

                    var coordinateOverlay = new CoordinateGridOverlay.OverlayRequest
                    {
                        X1 = rect["x1"],
                        Y1 = rect["y1"],
                        X2 = rect["x2"],
                        Y2 = rect["y2"],
                        Step = step,
                        MajorEvery = Math.Max(step, 10),
                        ShowGrid = ToolUtil.GetBool(args, "showGrid", true),
                        ShowCoordinates = ToolUtil.GetBool(args, "showCoordinates", true),
                        IncludeCellLabels = includeCellLabels,
                        VisibleSeconds = Math.Max(1f, waitFrames / 30f + 1.5f)
                    };

                    string fileName = args["filename"]?.ToString();
                    if (string.IsNullOrWhiteSpace(fileName))
                        fileName = $"cycle_{GameUtil.GetCurrentCycle()}_coord_{rect["x1"]}_{rect["y1"]}_{rect["x2"]}_{rect["y2"]}_{System.DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png";
                    var screenshot = OverlaySwitchQueue.Enqueue("coordinate_grid", OverlayScreen.Instance?.GetMode() ?? OverlayModes.None.ID, false, true, fileName, waitFrames, coordinateOverlay);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = true,
                        ["tool"] = "camera_coordinate_screenshot",
                        ["view"] = viewName,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["size"] = new { width, height, cells },
                        ["coordinateOverlay"] = new
                        {
                            step,
                            showGrid = ToolUtil.GetBool(args, "showGrid", true),
                            showCoordinates = ToolUtil.GetBool(args, "showCoordinates", true),
                            includeCellLabels,
                            coordinateSystem = "ONI world absolute x/y; grid lines bound cell edges; labels are world cell coordinates"
                        },
                        ["screenshot"] = screenshot["screenshot"],
                        ["next"] = "Open screenshot.url/latestUrl directly; use visible grid labels to infer exact cell x/y, then call world_cell_info for selected cells."
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
