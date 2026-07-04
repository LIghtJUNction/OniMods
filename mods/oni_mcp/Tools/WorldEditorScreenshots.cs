using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult Screenshot(JObject args)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            if (Camera.main == null)
                return CallToolResult.Error("Camera not available");

            var views = ParseScreenshotViews(args);
            int waitFrames = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "waitFrames") ?? 2, 30));
            bool allowSound = Bool(args, "allowSound");
            string filename = Text(args, "filename");
            var captures = new JArray();

            foreach (var view in views)
            {
                bool current = IsCurrentScreenshotView(view);
                var forwarded = new JObject
                {
                    ["domain"] = "camera",
                    ["action"] = current ? "screenshot" : "switch_view",
                    ["waitFrames"] = waitFrames,
                    ["allowSound"] = allowSound
                };
                if (!current)
                {
                    forwarded["view"] = NormalizeScreenshotView(view);
                    forwarded["screenshot"] = true;
                }

                string captureFileName = BuildScreenshotFileName(filename, view, views.Count);
                if (!string.IsNullOrWhiteSpace(captureFileName))
                    forwarded["filename"] = captureFileName;

                var result = NavigationControlTools.ControlNavigation().Handler(forwarded);
                captures.Add(BuildScreenshotCaptureResult(view, current, result));
            }

            return JsonResult(new JObject
            {
                ["ok"] = captures.All(item => !(bool)item["isError"]),
                ["mode"] = views.Count > 1 ? "multi_view_viewport_screenshot" : "viewport_screenshot",
                ["viewport"] = JObject.FromObject(CurrentViewportInfo()),
                ["requestedViews"] = new JArray(views),
                ["waitFrames"] = waitFrames,
                ["captures"] = captures,
                ["latestUrl"] = OniMcp.Config.OniMcpOptions.Current.ScreenshotLatestUrl,
                ["next"] = "Open the returned screenshot.url after readyAfterFrames has elapsed."
            });
        }

        private static List<string> ParseScreenshotViews(JObject args)
        {
            var result = new List<string>();
            var views = args?["views"];
            if (views is JArray array)
            {
                foreach (var item in array)
                {
                    var text = item?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                }
            }
            else if (views != null)
            {
                AddCommaSeparatedViews(result, views.ToString());
            }

            if (result.Count == 0)
                AddCommaSeparatedViews(result, Text(args, "view", "activeView"));
            if (result.Count == 0)
                result.Add("current");

            return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        }

        private static void AddCommaSeparatedViews(List<string> result, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            foreach (var part in value.Split(','))
            {
                var text = part.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text);
            }
        }

        private static bool IsCurrentScreenshotView(string view)
        {
            string normalized = (view ?? string.Empty).Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(normalized) || normalized == "current" || normalized == "active";
        }

        private static string NormalizeScreenshotView(string view)
        {
            string normalized = (view ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            return normalized == "default" ? "none" : normalized;
        }

        private static string BuildScreenshotFileName(string requested, string view, int count)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;
            if (count <= 1)
                return requested;

            string baseName = Path.GetFileNameWithoutExtension(requested);
            string extension = Path.GetExtension(requested);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";
            string suffix = NormalizeScreenshotFilePart(view);
            return $"{baseName}_{suffix}{extension}";
        }

        private static string NormalizeScreenshotFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "current";
            var chars = value.Trim().ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray();
            return new string(chars);
        }

        private static JObject BuildScreenshotCaptureResult(string view, bool current, CallToolResult result)
        {
            var item = new JObject
            {
                ["view"] = view,
                ["source"] = current ? "current_overlay" : "switched_overlay",
                ["isError"] = result == null || result.IsError
            };

            string text = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return item;

            try
            {
                var parsed = JObject.Parse(text);
                item["result"] = parsed;
                var screenshot = parsed["screenshot"] ?? parsed;
                item["screenshot"] = screenshot;
                item["url"] = screenshot["url"];
                item["path"] = screenshot["path"];
                item["readyAfterFrames"] = screenshot["readyAfterFrames"] ?? parsed["readyAfterFrames"];
            }
            catch
            {
                item["text"] = TrimText(text, 900);
            }
            return item;
        }

        private static Dictionary<string, object> CurrentViewportInfo()
        {
            var cam = Camera.main;
            var pos = cam.transform.position;
            float size = cam.orthographicSize;
            float aspect = cam.aspect;
            int xMin = Mathf.Clamp(Mathf.RoundToInt(pos.x - size * aspect), 0, Grid.WidthInCells - 1);
            int xMax = Mathf.Clamp(Mathf.RoundToInt(pos.x + size * aspect), 0, Grid.WidthInCells - 1);
            int yMin = Mathf.Clamp(Mathf.RoundToInt(pos.y - size), 0, Grid.HeightInCells - 1);
            int yMax = Mathf.Clamp(Mathf.RoundToInt(pos.y + size), 0, Grid.HeightInCells - 1);
            return new Dictionary<string, object>
            {
                ["x1"] = xMin,
                ["y1"] = yMin,
                ["x2"] = xMax,
                ["y2"] = yMax,
                ["width"] = xMax - xMin + 1,
                ["height"] = yMax - yMin + 1,
                ["activeWorldId"] = ClusterManager.Instance?.activeWorldId ?? -1,
                ["screen"] = new { width = Screen.width, height = Screen.height }
            };
        }

        private static string ReadScreenshotsIndexMarkdown()
        {
            string latest = WorldEditor.LatestScreenshotPath();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Screenshots");
            sb.AppendLine();
            sb.AppendLine("Use `world_editor` with `command=screenshot` to capture the current camera viewport.");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            sb.AppendLine("- `command=screenshot` captures the current overlay.");
            sb.AppendLine("- `command=screenshot views=[default,power,temperature,oxygen]` captures multiple overlays in one call.");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(latest))
                sb.AppendLine($"Latest: {WorldEditor.ScreenshotUrl(latest)}");
            else
                sb.AppendLine("Latest: none");
            return sb.ToString();
        }
    }
}
