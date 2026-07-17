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
        public static Dictionary<string, object> CleanupTemporaryScreenshots()
        {
            return WorldEditor.CleanupTemporaryScreenshots();
        }

        private static Dictionary<string, McpToolParameter> CameraControlParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter
                {
                    Type = "string",
                    Description = "操作：get_view、set_active_world、set_view、move、switch_view、focus_cell、focus_dupe、screenshot、coordinate_screenshot",
                    Required = true,
                    EnumValues = new List<string> { "get_view", "set_active_world", "set_view", "move", "switch_view", "focus_cell", "focus_dupe", "screenshot", "coordinate_screenshot" }
                },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，set_active_world 必填；其他 action 默认当前激活世界", Required = false },
                ["requireDiscovered"] = new McpToolParameter { Type = "boolean", Description = "set_active_world：是否要求目标世界已被发现，默认 true", Required = false },
                ["lookAtSurface"] = new McpToolParameter { Type = "boolean", Description = "set_active_world：世界未被访问时是否 LookAtSurface，默认 true", Required = false },
                ["x"] = new McpToolParameter { Type = "number", Description = "set_view/move jump 目标 X；focus_cell 格子 X；也可用于按坐标定位", Required = false },
                ["y"] = new McpToolParameter { Type = "number", Description = "set_view/move jump 目标 Y；focus_cell 格子 Y；也可用于按坐标定位", Required = false },
                ["zoom"] = new McpToolParameter { Type = "number", Description = "相机正交缩放；留空按各 action 默认值处理", Required = false },
                ["snap"] = new McpToolParameter { Type = "boolean", Description = "set_view/move：是否立即跳转", Required = false },
                ["mode"] = new McpToolParameter { Type = "string", Description = "move：pan=按 dx/dy 相对平移，jump=跳转到 x/y 世界坐标", Required = false, EnumValues = new List<string> { "pan", "jump" } },
                ["dx"] = new McpToolParameter { Type = "number", Description = "move pan：X 方向偏移，默认 0", Required = false },
                ["dy"] = new McpToolParameter { Type = "number", Description = "move pan：Y 方向偏移，默认 0", Required = false },
                ["duration"] = new McpToolParameter { Type = "number", Description = "move：平滑移动秒数，默认 0.5", Required = false },
                ["view"] = new McpToolParameter { Type = "string", Description = "switch_view/coordinate_screenshot 覆盖层视图", Required = false, EnumValues = OverlayViewNames },
                ["screenshot"] = new McpToolParameter { Type = "boolean", Description = "switch_view：是否保存切换后的截图，默认 true", Required = false },
                ["filename"] = new McpToolParameter { Type = "string", Description = "screenshot/switch_view/coordinate_screenshot：可选截图文件名", Required = false },
                ["waitFrames"] = new McpToolParameter { Type = "integer", Description = "switch_view/coordinate_screenshot：截图前等待的 Unity 帧数", Required = false },
                ["allowSound"] = new McpToolParameter { Type = "boolean", Description = "switch_view：是否播放视图切换音效，默认 false", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "focus_dupe：复制人 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "focus_dupe：复制人名称", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "coordinate_screenshot：区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "coordinate_screenshot：区域左下 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "coordinate_screenshot：区域左下 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "coordinate_screenshot：区域右上 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "coordinate_screenshot：区域右上 Y", Required = false },
                ["focusCamera"] = new McpToolParameter { Type = "boolean", Description = "coordinate_screenshot：是否自动移动相机覆盖区域，默认 true", Required = false },
                ["paddingCells"] = new McpToolParameter { Type = "number", Description = "coordinate_screenshot：自动对焦时四周留白格数，默认 1.5", Required = false },
                ["showGrid"] = new McpToolParameter { Type = "boolean", Description = "coordinate_screenshot：是否显示格线，默认 true", Required = false },
                ["showCoordinates"] = new McpToolParameter { Type = "boolean", Description = "coordinate_screenshot：是否在边缘显示 x/y 坐标，默认 true", Required = false },
                ["includeCellLabels"] = new McpToolParameter { Type = "boolean", Description = "coordinate_screenshot：是否在格心稀疏标注 x,y", Required = false },
                ["step"] = new McpToolParameter { Type = "integer", Description = "coordinate_screenshot：坐标标签步长", Required = false }
            };
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

        private sealed class OverlaySwitchRequest
        {
            public string Id;
            public string ViewName;
            public HashedString Mode;
            public bool AllowSound;
            public bool Screenshot;
            public string ScreenshotPath;
            public Dictionary<string, object> Cleanup;
            public int WaitFrames;
            public CoordinateGridOverlay.OverlayRequest CoordinateOverlay;
        }

        private sealed class OverlaySwitchQueue : MonoBehaviour
        {
            private static OverlaySwitchQueue instance;
            private readonly Queue<OverlaySwitchRequest> requests = new Queue<OverlaySwitchRequest>();
            private bool running;

            public static Dictionary<string, object> Enqueue(string viewName, HashedString mode, bool allowSound, bool screenshot, string fileName, int waitFrames, CoordinateGridOverlay.OverlayRequest coordinateOverlay = null)
            {
                EnsureInstance();
                var request = new OverlaySwitchRequest
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ViewName = viewName,
                    Mode = mode,
                    AllowSound = allowSound,
                    Screenshot = screenshot,
                    WaitFrames = waitFrames,
                    CoordinateOverlay = coordinateOverlay
                };
                if (screenshot)
                {
                    request.Cleanup = CleanupTemporaryScreenshots(1);
                    request.ScreenshotPath = PrepareScreenshotPath(fileName, viewName);
                }

                int queuePosition = instance.requests.Count + 1;
                instance.requests.Enqueue(request);
                if (!instance.running)
                    instance.StartCoroutine(instance.ProcessQueue());

                return new Dictionary<string, object>
                {
                    ["id"] = request.Id,
                    ["position"] = queuePosition,
                    ["running"] = instance.running,
                    ["screenshot"] = screenshot ? (object)new Dictionary<string, object>
                    {
                        ["path"] = request.ScreenshotPath,
                        ["url"] = WorldEditor.ScreenshotUrl(request.ScreenshotPath),
                        ["latestUrl"] = OniMcpOptions.Current.ScreenshotLatestUrl,
                        ["readyAfterFrames"] = waitFrames + 1,
                        ["cycle"] = GameUtil.GetCurrentCycle(),
                        ["screen"] = new { width = Screen.width, height = Screen.height },
                        ["cleanup"] = request.Cleanup
                    } : null
                };
            }

            private static void EnsureInstance()
            {
                if (instance != null)
                    return;

                var obj = new GameObject("OniMcp_OverlaySwitchQueue");
                UnityEngine.Object.DontDestroyOnLoad(obj);
                instance = obj.AddComponent<OverlaySwitchQueue>();
            }

            private IEnumerator ProcessQueue()
            {
                running = true;
                while (requests.Count > 0)
                {
                    var request = requests.Dequeue();
                    if (request.ViewName != "coordinate_grid")
                        ApplyOverlayMode(request.Mode, request.AllowSound);
                    if (request.CoordinateOverlay != null)
                        CoordinateGridOverlay.Show(request.CoordinateOverlay);

                    if (request.Screenshot)
                    {
                        for (int i = 0; i < request.WaitFrames; i++)
                            yield return null;
                        ScreenCapture.CaptureScreenshot(request.ScreenshotPath);
                        yield return null;
                    }
                    else
                    {
                        yield return null;
                    }
                }
                running = false;
            }
        }

        private static void ApplyOverlayMode(HashedString mode, bool allowSound)
        {
            var overlay = OverlayScreen.Instance;
            if (overlay == null)
                return;

            overlay.ToggleOverlay(mode, allowSound);
            if (Game.Instance != null)
                Game.Instance.ForceOverlayUpdate(true);
        }

        private static string PrepareScreenshotPath(string fileName, string viewName)
        {
            string dir = ScreenshotDirectory;
            Directory.CreateDirectory(dir);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"cycle_{GameUtil.GetCurrentCycle()}_{NormalizeScreenshotName(viewName)}_{System.DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.png";
            string path = Path.Combine(dir, Path.GetFileName(fileName));
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                path += ".png";
            return path;
        }

        private static string NormalizeScreenshotName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "overlay";
            var chars = value.Trim().ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray();
            return new string(chars);
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
