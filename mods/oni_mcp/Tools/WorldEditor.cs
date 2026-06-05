using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class WorldEditor
    {
        public struct CellCoord
        {
            public readonly int x;
            public readonly int y;

            public CellCoord(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        public static int ResolveWorldId(JObject args, int fallbackWorldId = -1)
        {
            int? requested = GetInt(args, "worldId");
            if (requested.HasValue)
                return requested.Value;

            AreaHandle area;
            if (TryGetArea(args, out area))
                return area.WorldId;

            if (fallbackWorldId >= 0)
                return fallbackWorldId;

            return ClusterManager.Instance?.activeWorldId ?? -1;
        }

        public static Dictionary<string, int> ResolveRect(JObject args)
        {
            AreaHandle area;
            if (TryGetArea(args, out area))
            {
                bool relative = GetBool(args, "relative", false) || GetBool(args, "rel", false);
                if (relative && HasExplicitRect(args))
                    return ResolveRelativeRect(args, area);
                return area.Rect();
            }

            return NormalizeRect(
                GetInt(args, "x1") ?? GetInt(args, "x") ?? 0,
                GetInt(args, "y1") ?? GetInt(args, "y") ?? 0,
                GetInt(args, "x2") ?? GetInt(args, "x1") ?? GetInt(args, "x") ?? 0,
                GetInt(args, "y2") ?? GetInt(args, "y1") ?? GetInt(args, "y") ?? 0,
                0,
                0,
                Grid.WidthInCells - 1,
                Grid.HeightInCells - 1);
        }

        public static Dictionary<string, int> ResolveRectOrCamera(JObject args, int maxCells)
        {
            if (TryGetArea(args, out _) || HasExplicitRect(args))
                return ResolveRect(args);

            Vector3 camera = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            int side = Math.Max(8, (int)Math.Floor(Math.Sqrt(Math.Max(1, maxCells))));
            int half = side / 2;
            int centerX = Mathf.RoundToInt(camera.x);
            int centerY = Mathf.RoundToInt(camera.y);
            return NormalizeRect(
                centerX - half,
                centerY - half,
                centerX + half - 1,
                centerY + half - 1,
                0,
                0,
                Grid.WidthInCells - 1,
                Grid.HeightInCells - 1);
        }

        public static AreaHandle ResolveRelativeArea(JObject args)
        {
            if (!GetBool(args, "relative", false) && !GetBool(args, "rel", false))
                return null;

            AreaHandle area;
            return TryGetArea(args, out area) ? area : null;
        }

        public static CellCoord ToAbsoluteCell(int x, int y, AreaHandle area)
        {
            return area == null ? new CellCoord(x, y) : new CellCoord(area.X1 + x, area.Y1 + y);
        }

        public static Dictionary<string, object> AddRelativeInfo(Dictionary<string, object> result, JObject args, int x, int y)
        {
            AreaHandle area = ResolveRelativeArea(args);
            if (area == null)
                return result;

            result["areaId"] = area.Id;
            result["rx"] = x - area.X1;
            result["ry"] = y - area.Y1;
            result["origin"] = new[] { area.X1, area.Y1 };
            result["coordMode"] = "relative";
            return result;
        }

        public static Dictionary<string, object> SaveScreenshot(string fileName)
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
                ["url"] = ScreenshotUrl(path),
                ["latestUrl"] = OniMcpOptions.Current.ScreenshotLatestUrl,
                ["cycle"] = GameUtil.GetCurrentCycle(),
                ["screen"] = new { width = Screen.width, height = Screen.height },
                ["cleanup"] = cleanup
            };
        }

        public static Dictionary<string, object> CleanupTemporaryScreenshots(int reservedSlots = 0)
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

            if (!options.ScreenshotCleanupEnabled || !Directory.Exists(dir))
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

        private static bool TryGetArea(JObject args, out AreaHandle area)
        {
            area = null;
            string areaId = args?["areaId"]?.ToString();
            if (string.IsNullOrWhiteSpace(areaId))
                return false;
            if (!AreaHandleRegistry.TryGet(areaId, out area))
                throw new ArgumentException("Unknown areaId: " + areaId);
            return true;
        }

        private static bool HasExplicitRect(JObject args)
        {
            return args != null
                && (args["x1"] != null || args["y1"] != null || args["x2"] != null || args["y2"] != null || args["x"] != null || args["y"] != null);
        }

        private static Dictionary<string, int> ResolveRelativeRect(JObject args, AreaHandle area)
        {
            int rx1 = GetInt(args, "x1") ?? GetInt(args, "x") ?? 0;
            int ry1 = GetInt(args, "y1") ?? GetInt(args, "y") ?? 0;
            int rx2 = GetInt(args, "x2") ?? rx1;
            int ry2 = GetInt(args, "y2") ?? ry1;
            return NormalizeRect(area.X1 + rx1, area.Y1 + ry1, area.X1 + rx2, area.Y1 + ry2, area.X1, area.Y1, area.X2, area.Y2);
        }

        private static Dictionary<string, int> NormalizeRect(int x1, int y1, int x2, int y2, int minX, int minY, int maxX, int maxY)
        {
            if (x2 < x1) { int t = x1; x1 = x2; x2 = t; }
            if (y2 < y1) { int t = y1; y1 = y2; y2 = t; }
            return new Dictionary<string, int>
            {
                ["x1"] = Mathf.Clamp(x1, minX, maxX),
                ["y1"] = Mathf.Clamp(y1, minY, maxY),
                ["x2"] = Mathf.Clamp(x2, minX, maxX),
                ["y2"] = Mathf.Clamp(y2, minY, maxY)
            };
        }

        private static int? GetInt(JObject args, string key)
        {
            int value;
            return args != null && args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : (int?)null;
        }

        private static bool GetBool(JObject args, string key, bool defaultValue)
        {
            bool value;
            return args != null && args[key] != null && bool.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        public static string ScreenshotPathForFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            string safeName = Path.GetFileName(fileName);
            if (!safeName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return null;
            string path = Path.Combine(ScreenshotDirectory, safeName);
            return File.Exists(path) ? path : null;
        }

        public static string LatestScreenshotPath()
        {
            var errors = new List<string>();
            return ListScreenshotFiles(ScreenshotDirectory, errors)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }

        public static string ScreenshotUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            string fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
                return null;
            return OniMcpOptions.Current.ScreenshotBaseUrl + Uri.EscapeDataString(fileName);
        }

        private static string ScreenshotDirectory => Path.Combine(Path.GetTempPath(), "oni-mcp", "screenshots");

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
