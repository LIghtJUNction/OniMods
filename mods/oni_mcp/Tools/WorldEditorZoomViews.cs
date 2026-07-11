using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool _hasSynchronizedViewportBounds;
        private static int _synchronizedViewportXMin;
        private static int _synchronizedViewportYMin;
        private static int _synchronizedViewportXMax;
        private static int _synchronizedViewportYMax;

        private static CallToolResult Zoom(JObject args)
        {
            if (!TryReadZoomBounds(args, out int xMin, out int yMin, out int xMax, out int yMax, out string error))
                return CallToolResult.Error(error);

            var views = ResolveZoomViews(ParseZoomViews(args)).ToList();
            if (views.Count == 0)
                views = ResolveZoomViews(DefaultZoomViews()).ToList();

            string syncNote = SyncZoomCameraAndView(args, xMin, yMin, xMax, yMax, views);
            bool syncEachView = ToolUtil.GetBool(args, "syncView", true);
            string text = ReadZoomMarkdown(xMin, yMin, xMax, yMax, views.Select(view => view.Name), syncNote,
                ShouldCompactMap(args), syncEachView, ToolUtil.GetBool(args, "allowSound", false));
            return CallToolResult.Text(text);
        }

        private static bool TryParseZoomPath(string relative, out int xMin, out int yMin, out int xMax, out int yMax)
        {
            xMin = yMin = xMax = yMax = 0;
            const string prefix = "map/zoom_";
            const string suffix = ".md";
            if (string.IsNullOrEmpty(relative)
                || !relative.StartsWith(prefix, StringComparison.Ordinal)
                || !relative.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            string body = relative.Substring(prefix.Length, relative.Length - prefix.Length - suffix.Length);
            string[] parts = body.Split('_');
            if (parts.Length != 4
                || !int.TryParse(parts[0], out xMin)
                || !int.TryParse(parts[1], out yMin)
                || !int.TryParse(parts[2], out xMax)
                || !int.TryParse(parts[3], out yMax))
                return false;

            NormalizeZoomBounds(ref xMin, ref yMin, ref xMax, ref yMax);
            return true;
        }

        private static string ReadZoomMarkdown(int xMin, int yMin, int xMax, int yMax, IEnumerable<string> views,
            string syncNote = null, bool compact = true, bool syncEachView = false, bool allowSound = false)
        {
            NormalizeZoomBounds(ref xMin, ref yMin, ref xMax, ref yMax);
            var resolved = ResolveZoomViews(views).ToList();
            if (resolved.Count == 0)
                resolved = ResolveZoomViews(DefaultZoomViews()).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# Local Multi-View Map (X: " + xMin + "~" + xMax + ", Y: " + yMin + "~" + yMax + ")");
            sb.AppendLine();
            sb.AppendLine("- 范围: X=" + xMin + "~" + xMax + ", Y=" + yMin + "~" + yMax
                + " (" + (xMax - xMin + 1) + "x" + (yMax - yMin + 1) + ")");
            sb.AppendLine("- 视图: " + string.Join(", ", resolved.Select(item => item.Name).ToArray()));
            if (!string.IsNullOrWhiteSpace(syncNote))
                sb.AppendLine("- 直播视角: " + syncNote);
            sb.AppendLine("- 单格详情: `/active/map/cell_X_Y.md`");
            sb.AppendLine();

            foreach (var view in resolved)
            {
                if (syncEachView)
                    ApplyZoomOverlayMode(view.Mode, allowSound);
                sb.AppendLine(GetMapMd("局部放大 - " + view.Name, xMin, xMax, yMin, yMax, view.Mode, compact));
                if (syncEachView)
                    sb.AppendLine("- 游戏覆盖层同步: 已切换到 " + view.Name + "；该视图已在游戏中展示。");
                sb.AppendLine();
            }

            if (syncEachView && resolved.Count > 0)
                sb.AppendLine("- 最终游戏覆盖层: " + resolved[resolved.Count - 1].Name + "（与最后读取的文本视图一致）");

            return sb.ToString();
        }

        private static string SyncZoomCameraAndView(JObject args, int xMin, int yMin, int xMax, int yMax, List<ZoomView> views)
        {
            if (!ToolUtil.GetBool(args, "syncView", true))
                return "未同步(syncView=false)";

            string viewName = FirstZoomText(args, "activeView", "displayView", "view");
            ZoomView activeView;
            if (string.IsNullOrWhiteSpace(viewName) || !TryResolveZoomView(viewName, out activeView))
                activeView = views.Count > 0 ? views[0] : new ZoomView { Name = "default", Mode = OverlayModes.None.ID };

            bool focusCamera = ToolUtil.GetBool(args, "focusCamera", true);
            float zoom = 0f;
            if (focusCamera)
                zoom = SyncZoomCamera(args, xMin, yMin, xMax, yMax);

            ApplyZoomOverlayMode(activeView.Mode, ToolUtil.GetBool(args, "allowSound", false));
            string mode = ResolveFocusMode(args, xMax - xMin + 1, yMax - yMin + 1);
            string focus = focusCamera
                ? "相机已聚焦(" + mode + ", zoom=" + zoom.ToString("0.0") + "), "
                : string.Empty;
            return focus + "覆盖层=" + activeView.Name;
        }

        private static float SyncZoomCamera(JObject args, int xMin, int yMin, int xMax, int yMax)
        {
            var camera = CameraController.Instance;
            if (camera == null)
                return 0f;

            float centerX = (xMin + xMax) * 0.5f;
            float centerY = (yMin + yMax) * 0.5f;
            float zoom = ToolUtil.GetFloat(args, "cameraZoom")
                ?? ToolUtil.GetFloat(args, "zoom")
                ?? CalculateZoomForBounds(args, xMin, yMin, xMax, yMax);
            camera.SnapTo(new Vector3(centerX, centerY, -100f), zoom);
            _hasSynchronizedViewportBounds = true;
            _synchronizedViewportXMin = xMin;
            _synchronizedViewportYMin = yMin;
            _synchronizedViewportXMax = xMax;
            _synchronizedViewportYMax = yMax;
            return zoom;
        }

        private static bool TryGetSynchronizedViewportBounds(out int xMin, out int yMin, out int xMax, out int yMax)
        {
            xMin = yMin = xMax = yMax = 0;
            if (!_hasSynchronizedViewportBounds)
                return false;

            xMin = _synchronizedViewportXMin;
            yMin = _synchronizedViewportYMin;
            xMax = _synchronizedViewportXMax;
            yMax = _synchronizedViewportYMax;
            return true;
        }

        private static float CalculateZoomForBounds(JObject args, int xMin, int yMin, int xMax, int yMax)
        {
            int width = xMax - xMin + 1;
            int height = yMax - yMin + 1;
            string mode = ResolveFocusMode(args, width, height);
            float defaultPadding = mode == "detail" ? 0.75f : mode == "overview" ? 4f : 1.5f;
            float padding = Math.Max(0f, ToolUtil.GetFloat(args, "paddingCells") ?? defaultPadding);
            float aspect = Math.Max(0.5f, Screen.width / Math.Max(1f, (float)Screen.height));
            float vertical = height * 0.5f + padding;
            float horizontalAsVertical = (width * 0.5f + padding) / aspect;
            float raw = Math.Max(vertical, horizontalAsVertical);
            if (mode == "detail")
                raw *= 0.72f;
            else if (mode == "overview")
                raw *= 1.28f;

            float minZoom = ToolUtil.GetFloat(args, "minCameraZoom")
                ?? (mode == "detail" ? 2.8f : mode == "overview" ? 10f : 4f);
            float maxZoom = ToolUtil.GetFloat(args, "maxCameraZoom") ?? 80f;
            return Mathf.Clamp(raw, minZoom, Math.Max(minZoom, maxZoom));
        }

        private static string ResolveFocusMode(JObject args, int width, int height)
        {
            string requested = FirstZoomText(args, "focusMode", "cameraMode", "zoomMode");
            if (requested.Equals("detail", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("local", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("close", StringComparison.OrdinalIgnoreCase))
                return "detail";
            if (requested.Equals("overview", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("global", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("wide", StringComparison.OrdinalIgnoreCase))
                return "overview";

            int cells = Math.Max(1, width * height);
            return cells <= 360 ? "detail" : "overview";
        }

        private static bool TryReadMapFocusBounds(
            JObject args,
            out int xMin,
            out int yMin,
            out int xMax,
            out int yMax,
            out string error)
        {
            error = null;
            xMin = yMin = xMax = yMax = 0;
            int? x1 = ToolUtil.GetInt(args, "x1") ?? ToolUtil.GetInt(args, "xMin") ?? ToolUtil.GetInt(args, "left");
            int? y1 = ToolUtil.GetInt(args, "y1") ?? ToolUtil.GetInt(args, "yMin") ?? ToolUtil.GetInt(args, "bottom");
            int? x2 = ToolUtil.GetInt(args, "x2") ?? ToolUtil.GetInt(args, "xMax") ?? ToolUtil.GetInt(args, "right");
            int? y2 = ToolUtil.GetInt(args, "y2") ?? ToolUtil.GetInt(args, "yMax") ?? ToolUtil.GetInt(args, "top");
            bool hasRangePart = x1.HasValue || y1.HasValue || x2.HasValue || y2.HasValue;
            if (hasRangePart)
            {
                if (!x1.HasValue || !y1.HasValue || !x2.HasValue || !y2.HasValue)
                {
                    error = "viewport focus range requires x1,y1,x2,y2 together.";
                    return true;
                }

                xMin = x1.Value;
                yMin = y1.Value;
                xMax = x2.Value;
                yMax = y2.Value;
                NormalizeZoomBounds(ref xMin, ref yMin, ref xMax, ref yMax);
                return true;
            }

            int? x = ToolUtil.GetInt(args, "x") ?? ToolUtil.GetInt(args, "centerX");
            int? y = ToolUtil.GetInt(args, "y") ?? ToolUtil.GetInt(args, "centerY");
            if (!x.HasValue && !y.HasValue)
                return false;
            if (!x.HasValue || !y.HasValue)
            {
                error = "viewport focus point requires x and y together.";
                return true;
            }

            string mode = ResolveFocusMode(args, 1, 1);
            int radius = Math.Max(2, ToolUtil.GetInt(args, "radius") ?? (mode == "overview" ? 24 : 8));
            xMin = x.Value - radius;
            xMax = x.Value + radius;
            yMin = y.Value - radius;
            yMax = y.Value + radius;
            NormalizeZoomBounds(ref xMin, ref yMin, ref xMax, ref yMax);
            return true;
        }

        private static void ApplyZoomOverlayMode(HashedString mode, bool allowSound)
        {
            var overlay = OverlayScreen.Instance;
            if (overlay == null)
                return;

            overlay.ToggleOverlay(mode, allowSound);
            if (Game.Instance != null)
                Game.Instance.ForceOverlayUpdate(true);
        }

        private static bool TryReadZoomBounds(JObject args, out int xMin, out int yMin, out int xMax, out int yMax, out string error)
        {
            error = null;
            xMin = ToolUtil.GetInt(args, "x1") ?? ToolUtil.GetInt(args, "xMin") ?? ToolUtil.GetInt(args, "left") ?? int.MinValue;
            yMin = ToolUtil.GetInt(args, "y1") ?? ToolUtil.GetInt(args, "yMin") ?? ToolUtil.GetInt(args, "bottom") ?? int.MinValue;
            xMax = ToolUtil.GetInt(args, "x2") ?? ToolUtil.GetInt(args, "xMax") ?? ToolUtil.GetInt(args, "right") ?? int.MinValue;
            yMax = ToolUtil.GetInt(args, "y2") ?? ToolUtil.GetInt(args, "yMax") ?? ToolUtil.GetInt(args, "top") ?? int.MinValue;

            string path = NormalizePath(Text(args, "path"), _cwd);
            if ((xMin == int.MinValue || yMin == int.MinValue || xMax == int.MinValue || yMax == int.MinValue)
                && path.StartsWith("/active/", StringComparison.Ordinal)
                && TryParseZoomPath(SaveRelativePath(path), out int px1, out int py1, out int px2, out int py2))
            {
                xMin = px1;
                yMin = py1;
                xMax = px2;
                yMax = py2;
            }

            if (xMin == int.MinValue || yMin == int.MinValue || xMax == int.MinValue || yMax == int.MinValue)
            {
                error = "zoom requires x1,y1,x2,y2 or path /active/map/zoom_X1_Y1_X2_Y2.md";
                return false;
            }

            NormalizeZoomBounds(ref xMin, ref yMin, ref xMax, ref yMax);
            int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 900, 2500));
            int cells = (xMax - xMin + 1) * (yMax - yMin + 1);
            if (cells > maxCells)
            {
                error = "zoom range too large: " + cells + " cells, maxCells=" + maxCells
                    + ". Narrow the range or pass a higher maxCells up to 2500.";
                return false;
            }

            return true;
        }

        private static void NormalizeZoomBounds(ref int xMin, ref int yMin, ref int xMax, ref int yMax)
        {
            if (xMin > xMax)
            {
                int tmp = xMin;
                xMin = xMax;
                xMax = tmp;
            }
            if (yMin > yMax)
            {
                int tmp = yMin;
                yMin = yMax;
                yMax = tmp;
            }

            xMin = Mathf.Clamp(xMin, 0, Grid.WidthInCells - 1);
            xMax = Mathf.Clamp(xMax, 0, Grid.WidthInCells - 1);
            yMin = Mathf.Clamp(yMin, 0, Grid.HeightInCells - 1);
            yMax = Mathf.Clamp(yMax, 0, Grid.HeightInCells - 1);
        }

        private static IEnumerable<string> ParseZoomViews(JObject args)
        {
            JToken token = args?["views"] ?? args?["viewList"] ?? args?["view"];
            if (token is JArray array)
                return array.Select(item => item.ToString());

            string text = token?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return DefaultZoomViews();

            return text.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static IEnumerable<string> DefaultZoomViews()
        {
            return new[] { "default", "power", "oxygen", "temperature" };
        }

        private static IEnumerable<ZoomView> ResolveZoomViews(IEnumerable<string> views)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in views ?? DefaultZoomViews())
            {
                ZoomView view;
                if (!TryResolveZoomView(raw, out view))
                    continue;
                if (seen.Add(view.Name))
                    yield return view;
            }
        }

        private static bool TryResolveZoomView(string raw, out ZoomView view)
        {
            string name;
            HashedString mode;
            bool ok = TryResolveZoomView(raw, out name, out mode);
            view = new ZoomView { Name = name, Mode = mode };
            return ok;
        }

        private static bool TryResolveZoomView(string raw, out string name, out HashedString mode)
        {
            name = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
            switch (name)
            {
                case "":
                case "default":
                case "none":
                case "normal":
                    name = "default";
                    mode = OverlayModes.None.ID;
                    return true;
                case "oxygen":
                case "gas":
                    name = "oxygen";
                    mode = OverlayModes.Oxygen.ID;
                    return true;
                case "power":
                case "electric":
                case "electrical":
                    name = "power";
                    mode = OverlayModes.Power.ID;
                    return true;
                case "liquid":
                case "liquid_pipe":
                case "liquid_pipes":
                case "liquid_conduits":
                case "plumbing":
                    name = "liquid_conduits";
                    mode = OverlayModes.LiquidConduits.ID;
                    return true;
                case "gas_pipe":
                case "gas_pipes":
                case "gas_conduits":
                    name = "gas_conduits";
                    mode = OverlayModes.GasConduits.ID;
                    return true;
                case "logic":
                case "automation":
                    name = "logic";
                    mode = OverlayModes.Logic.ID;
                    return true;
                case "shipping":
                case "conveyor":
                case "solid_conveyor":
                    name = "solid_conveyor";
                    mode = OverlayModes.SolidConveyor.ID;
                    return true;
                case "temperature":
                case "temp":
                    name = "temperature";
                    mode = OverlayModes.Temperature.ID;
                    return true;
                case "materials":
                case "material":
                    name = "materials";
                    mode = OverlayModes.TileMode.ID;
                    return true;
                case "light":
                    mode = OverlayModes.Light.ID;
                    return true;
                case "decor":
                    mode = OverlayModes.Decor.ID;
                    return true;
                case "disease":
                case "germs":
                    name = "disease";
                    mode = OverlayModes.Disease.ID;
                    return true;
                case "radiation":
                    mode = OverlayModes.Radiation.ID;
                    return true;
                case "crop":
                case "farming":
                    name = "crop";
                    mode = OverlayModes.Crop.ID;
                    return true;
                case "harvest":
                    mode = OverlayModes.Harvest.ID;
                    return true;
                case "rooms":
                case "room":
                    name = "rooms";
                    mode = OverlayModes.Rooms.ID;
                    return true;
                default:
                    mode = default(HashedString);
                    return false;
            }
        }

        private static string FirstZoomText(JObject args, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = args?[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private struct ZoomView
        {
            public string Name;
            public HashedString Mode;
        }
    }
}
