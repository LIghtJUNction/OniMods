using System;
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
    public static class CameraTools
    {
        private static readonly List<string> OverlayViewNames = new List<string>
        {
            "none",
            "oxygen",
            "power",
            "gas_conduits",
            "liquid_conduits",
            "solid_conveyor",
            "logic",
            "temperature",
            "heat_flow",
            "thermal_conductivity",
            "materials",
            "light",
            "decor",
            "rooms",
            "priorities",
            "disease",
            "radiation",
            "sound",
            "suit",
            "crop",
            "harvest"
        };

        public static McpTool GetCameraView()
        {
            return new McpTool
            {
                Name = "camera_get_view",
                Group = "camera",
                Mode = "read",
                Risk = "none",
                Description = "获取当前相机位置、缩放、激活世界和屏幕尺寸",
                Handler = args =>
                {
                    var camera = CameraController.Instance;
                    if (camera == null || Camera.main == null)
                        return CallToolResult.Error("Camera not available");

                    var pos = camera.transform.GetPosition();
                    var result = new Dictionary<string, object>
                    {
                        ["position"] = new { x = Math.Round(pos.x, 2), y = Math.Round(pos.y, 2), z = Math.Round(pos.z, 2) },
                        ["orthographicSize"] = Math.Round(Camera.main.orthographicSize, 2),
                        ["activeWorldId"] = ClusterManager.Instance?.activeWorldId ?? -1,
                        ["screen"] = new { width = Screen.width, height = Screen.height }
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetActiveWorld()
        {
            return new McpTool
            {
                Name = "camera_set_active_world",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "world_set_active", "camera_switch_world", "view_world" },
                Tags = new List<string> { "camera", "world", "cluster", "rocket", "navigation" },
                Description = "切换当前激活世界，用于复现星图 View World、火箭内外部查看等玩家导航操作",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，可先读 oni://world/list", Required = true },
                    ["requireDiscovered"] = new McpToolParameter { Type = "boolean", Description = "是否要求目标世界已被发现，默认 true", Required = false },
                    ["lookAtSurface"] = new McpToolParameter { Type = "boolean", Description = "如果世界还未被复制人访问，是否调用 LookAtSurface 后切换，默认 true", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "切换后相机缩放，默认保持当前缩放或 10", Required = false }
                },
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");

                    int? worldId = ToolUtil.GetInt(args, "worldId");
                    if (!worldId.HasValue)
                        return CallToolResult.Error("worldId is required");

                    var world = ClusterManager.Instance.GetWorld(worldId.Value);
                    if (world == null)
                        return CallToolResult.Error($"World not found: {worldId.Value}");

                    bool requireDiscovered = ToolUtil.GetBool(args, "requireDiscovered", true);
                    if (requireDiscovered && !world.IsDiscovered)
                        return CallToolResult.Error($"World {worldId.Value} is not discovered");

                    bool lookAtSurface = ToolUtil.GetBool(args, "lookAtSurface", true);
                    if (lookAtSurface && !world.IsDupeVisited)
                        world.LookAtSurface();

                    int previousWorldId = ClusterManager.Instance.activeWorldId;
                    ClusterManager.Instance.SetActiveWorld(world.id);

                    var camera = CameraController.Instance;
                    if (camera != null)
                    {
                        float zoom = ToolUtil.GetFloat(args, "zoom") ?? (Camera.main != null ? Camera.main.orthographicSize : 10f);
                        var center = (world.minimumBounds + world.maximumBounds) * 0.5f;
                        camera.SnapTo(new Vector3(center.x, center.y, -100f), zoom);
                    }

                    ManagementMenu.Instance?.CloseAll();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["previousWorldId"] = previousWorldId,
                        ["activeWorldId"] = ClusterManager.Instance.activeWorldId,
                        ["worldName"] = world.GetProperName(),
                        ["isDiscovered"] = world.IsDiscovered,
                        ["isDupeVisited"] = world.IsDupeVisited,
                        ["isModuleInterior"] = world.IsModuleInterior
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetCameraView()
        {
            return new McpTool
            {
                Name = "camera_set_view",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "移动相机到指定世界坐标，并可设置缩放",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "number", Description = "目标 X 坐标", Required = true },
                    ["y"] = new McpToolParameter { Type = "number", Description = "目标 Y 坐标", Required = true },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "相机正交缩放，默认 10", Required = false },
                    ["snap"] = new McpToolParameter { Type = "boolean", Description = "是否立即跳转，默认 true", Required = false }
                },
                Handler = args =>
                {
                    var camera = CameraController.Instance;
                    if (camera == null)
                        return CallToolResult.Error("Camera not available");

                    float? x = ToolUtil.GetFloat(args, "x");
                    float? y = ToolUtil.GetFloat(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    float zoom = ToolUtil.GetFloat(args, "zoom") ?? 10f;
                    bool snap = ToolUtil.GetBool(args, "snap", true);
                    var pos = new Vector3(x.Value, y.Value, -100f);
                    if (snap)
                        camera.SnapTo(pos, zoom);
                    else
                        camera.CameraGoTo(pos, 2f, true);

                    return CallToolResult.Text($"Camera moved to ({x.Value}, {y.Value}) zoom={zoom}");
                }
            };
        }

        public static McpTool MoveCamera()
        {
            return new McpTool
            {
                Name = "camera_move",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "控制摄像头相对平移或绝对跳转",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "移动模式：pan=按 dx/dy 相对平移，jump=跳转到 x/y 世界坐标",
                        Required = true,
                        EnumValues = new List<string> { "pan", "jump" }
                    },
                    ["x"] = new McpToolParameter { Type = "number", Description = "jump 模式目标 X 坐标", Required = false },
                    ["y"] = new McpToolParameter { Type = "number", Description = "jump 模式目标 Y 坐标", Required = false },
                    ["dx"] = new McpToolParameter { Type = "number", Description = "pan 模式 X 方向偏移，默认 0", Required = false },
                    ["dy"] = new McpToolParameter { Type = "number", Description = "pan 模式 Y 方向偏移，默认 0", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "相机正交缩放；留空则保持当前缩放", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "平滑移动秒数，默认 0.5", Required = false },
                    ["snap"] = new McpToolParameter { Type = "boolean", Description = "是否立即跳转；默认 jump=true、pan=false", Required = false }
                },
                Handler = args =>
                {
                    var camera = CameraController.Instance;
                    if (camera == null || Camera.main == null)
                        return CallToolResult.Error("Camera not available");

                    string mode = (args["mode"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (mode != "pan" && mode != "jump")
                        return CallToolResult.Error("mode must be pan or jump");

                    var current = camera.transform.GetPosition();
                    Vector3 target;
                    if (mode == "jump")
                    {
                        float? x = ToolUtil.GetFloat(args, "x");
                        float? y = ToolUtil.GetFloat(args, "y");
                        if (!x.HasValue || !y.HasValue)
                            return CallToolResult.Error("x and y are required for jump mode");

                        target = new Vector3(x.Value, y.Value, -100f);
                    }
                    else
                    {
                        float dx = ToolUtil.GetFloat(args, "dx") ?? 0f;
                        float dy = ToolUtil.GetFloat(args, "dy") ?? 0f;
                        if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f)
                            return CallToolResult.Error("dx or dy is required for pan mode");

                        target = new Vector3(current.x + dx, current.y + dy, -100f);
                    }

                    float zoom = ToolUtil.GetFloat(args, "zoom") ?? Camera.main.orthographicSize;
                    bool snap = ToolUtil.GetBool(args, "snap", mode == "jump");
                    if (snap)
                    {
                        camera.SnapTo(target, zoom);
                    }
                    else
                    {
                        float duration = Math.Max(0.05f, ToolUtil.GetFloat(args, "duration") ?? 0.5f);
                        camera.CameraGoTo(target, duration, true);
                        Camera.main.orthographicSize = zoom;
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["mode"] = mode,
                        ["from"] = new { x = Math.Round(current.x, 2), y = Math.Round(current.y, 2), z = Math.Round(current.z, 2) },
                        ["target"] = new { x = Math.Round(target.x, 2), y = Math.Round(target.y, 2), z = Math.Round(target.z, 2) },
                        ["zoom"] = Math.Round(zoom, 2),
                        ["snap"] = snap
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SwitchView()
        {
            return new McpTool
            {
                Name = "camera_switch_view",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "切换 ONI 覆盖层视图，可选保存切换后的游戏截图",
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
                    overlay.ToggleOverlay(mode, allowSound);

                    var camera = CameraController.Instance;
                    var pos = camera != null ? camera.transform.GetPosition() : Vector3.zero;
                    var result = new Dictionary<string, object>
                    {
                        ["view"] = viewName,
                        ["activeOverlay"] = overlay.GetMode().ToString(),
                        ["activeWorldId"] = ClusterManager.Instance?.activeWorldId ?? -1,
                        ["camera"] = new
                        {
                            position = new { x = Math.Round(pos.x, 2), y = Math.Round(pos.y, 2), z = Math.Round(pos.z, 2) },
                            orthographicSize = Camera.main != null ? Math.Round(Camera.main.orthographicSize, 2) : 0
                        },
                        ["screen"] = new { width = Screen.width, height = Screen.height }
                    };

                    bool screenshot = ToolUtil.GetBool(args, "screenshot", true);
                    if (screenshot)
                        result["screenshot"] = WorldEditor.SaveScreenshot(args["filename"]?.ToString());

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FocusCell()
        {
            return new McpTool
            {
                Name = "camera_focus_cell",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "移动相机到指定地图格子",
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
                    return CallToolResult.Text($"Camera focused cell ({x.Value}, {y.Value}) worldId={worldId}");
                }
            };
        }

        public static McpTool FocusDupe()
        {
            return new McpTool
            {
                Name = "camera_focus_dupe",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "移动相机并跟随指定复制人",
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
                Group = "game",
                Mode = "execute",
                Risk = "low",
                Description = "保存当前游戏画面截图到系统临时目录的 oni-mcp/screenshots 并返回文件路径。注意：当需要分析地形、格子级元素分布、建筑布局或资源位置时，请优先使用 world_text_map（文本地图），它提供更精确的结构化数据；截图仅用于视觉确认、查看整体画面效果或文本地图无法覆盖的场景（如装饰度、植物生长阶段等视觉信息）。",
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

        public static Dictionary<string, object> CleanupTemporaryScreenshots()
        {
            return WorldEditor.CleanupTemporaryScreenshots();
        }

        private static bool TryResolveOverlayView(string requestedView, out string viewName, out HashedString mode)
        {
            viewName = NormalizeViewName(requestedView);
            switch (viewName)
            {
                case "none":
                case "normal":
                case "base":
                    viewName = "none";
                    mode = OverlayModes.None.ID;
                    return true;
                case "oxygen":
                case "gas":
                    viewName = "oxygen";
                    mode = OverlayModes.Oxygen.ID;
                    return true;
                case "power":
                case "electric":
                case "electrical":
                    viewName = "power";
                    mode = OverlayModes.Power.ID;
                    return true;
                case "gas_conduits":
                case "gas_pipe":
                case "gas_pipes":
                    viewName = "gas_conduits";
                    mode = OverlayModes.GasConduits.ID;
                    return true;
                case "liquid_conduits":
                case "liquid_pipe":
                case "liquid_pipes":
                case "plumbing":
                    viewName = "liquid_conduits";
                    mode = OverlayModes.LiquidConduits.ID;
                    return true;
                case "solid_conveyor":
                case "shipping":
                case "conveyor":
                    viewName = "solid_conveyor";
                    mode = OverlayModes.SolidConveyor.ID;
                    return true;
                case "logic":
                case "automation":
                    viewName = "logic";
                    mode = OverlayModes.Logic.ID;
                    return true;
                case "temperature":
                case "temp":
                    viewName = "temperature";
                    mode = OverlayModes.Temperature.ID;
                    return true;
                case "heat_flow":
                case "heatflow":
                    viewName = "heat_flow";
                    mode = OverlayModes.HeatFlow.ID;
                    return true;
                case "thermal_conductivity":
                case "conductivity":
                    viewName = "thermal_conductivity";
                    mode = OverlayModes.ThermalConductivity.ID;
                    return true;
                case "materials":
                case "material":
                case "tile":
                case "tiles":
                    viewName = "materials";
                    mode = OverlayModes.TileMode.ID;
                    return true;
                case "light":
                    viewName = "light";
                    mode = OverlayModes.Light.ID;
                    return true;
                case "decor":
                    viewName = "decor";
                    mode = OverlayModes.Decor.ID;
                    return true;
                case "rooms":
                case "room":
                    viewName = "rooms";
                    mode = OverlayModes.Rooms.ID;
                    return true;
                case "priorities":
                case "priority":
                    viewName = "priorities";
                    mode = OverlayModes.Priorities.ID;
                    return true;
                case "disease":
                case "germs":
                    viewName = "disease";
                    mode = OverlayModes.Disease.ID;
                    return true;
                case "radiation":
                case "rad":
                    viewName = "radiation";
                    mode = OverlayModes.Radiation.ID;
                    return true;
                case "sound":
                case "noise":
                    viewName = "sound";
                    mode = OverlayModes.Sound.ID;
                    return true;
                case "suit":
                case "exosuit":
                case "atmo_suit":
                    viewName = "suit";
                    mode = OverlayModes.Suit.ID;
                    return true;
                case "crop":
                case "farming":
                    viewName = "crop";
                    mode = OverlayModes.Crop.ID;
                    return true;
                case "harvest":
                    viewName = "harvest";
                    mode = OverlayModes.Harvest.ID;
                    return true;
                default:
                    mode = default(HashedString);
                    return false;
            }
        }

        private static string NormalizeViewName(string view)
        {
            return view.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }

        private static Dictionary<string, object> SaveScreenshot(string fileName)
        {
            var cleanup = CleanupTemporaryScreenshots(1);
            string dir = ScreenshotDirectory;
            Directory.CreateDirectory(dir);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"cycle_{GameUtil.GetCurrentCycle()}_{System.DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png";
            string path = Path.Combine(dir, Path.GetFileName(fileName));
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                path += ".png";

            ScreenCapture.CaptureScreenshot(path);
            return new Dictionary<string, object>
            {
                ["path"] = path,
                ["cycle"] = GameUtil.GetCurrentCycle(),
                ["screen"] = new { width = Screen.width, height = Screen.height },
                ["cleanup"] = cleanup
            };
        }

        private static string ScreenshotDirectory
        {
            get
            {
                return Path.Combine(Path.GetTempPath(), "oni-mcp", "screenshots");
            }
        }

        private static Dictionary<string, object> CleanupTemporaryScreenshots(int reservedSlots)
        {
            var options = OniMcpOptions.Current;
            string dir = ScreenshotDirectory;
            var result = new Dictionary<string, object>
            {
                ["path"] = dir,
                ["enabled"] = options.ScreenshotCleanupEnabled,
                ["retentionMinutes"] = options.ScreenshotRetentionMinutes,
                ["maxFiles"] = options.ScreenshotMaxFiles,
                ["deletedFiles"] = 0,
                ["remainingFiles"] = 0,
                ["errors"] = new List<string>()
            };

            if (!options.ScreenshotCleanupEnabled)
                return result;

            if (!Directory.Exists(dir))
                return result;

            var errors = (List<string>)result["errors"];
            int deleted = 0;
            System.DateTime cutoff = System.DateTime.UtcNow.AddMinutes(-options.ScreenshotRetentionMinutes);
            var files = ListScreenshotFiles(dir, errors);

            foreach (var file in files.Where(f => f.LastWriteTimeUtc < cutoff).ToList())
            {
                if (TryDeleteScreenshot(file, errors))
                    deleted++;
            }

            files = ListScreenshotFiles(dir, errors)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            int keep = Math.Max(0, options.ScreenshotMaxFiles - Math.Max(0, reservedSlots));
            foreach (var file in files.Skip(keep).ToList())
            {
                if (TryDeleteScreenshot(file, errors))
                    deleted++;
            }

            result["deletedFiles"] = deleted;
            result["remainingFiles"] = ListScreenshotFiles(dir, errors).Count;
            return result;
        }

        private static List<FileInfo> ListScreenshotFiles(string dir, List<string> errors)
        {
            try
            {
                return Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists)
                    .ToList();
            }
            catch (Exception ex)
            {
                AddCleanupError(errors, $"list screenshots failed: {ex.Message}");
                return new List<FileInfo>();
            }
        }

        private static bool TryDeleteScreenshot(FileInfo file, List<string> errors)
        {
            try
            {
                file.Delete();
                return true;
            }
            catch (Exception ex)
            {
                AddCleanupError(errors, $"{file.Name}: {ex.Message}");
                return false;
            }
        }

        private static void AddCleanupError(List<string> errors, string message)
        {
            if (errors.Count < 5)
                errors.Add(message);
        }
    }
}
