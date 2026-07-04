using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string ReadMapFileWithArgs(JObject args, string path)
        {
            string requestedView = FirstZoomText(args, "view", "activeView", "displayView");
            if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return ReadFileDirectly(path);
            if (string.IsNullOrWhiteSpace(requestedView))
            {
                if (!HasMapFormatArgs(args))
                    return ReadFileDirectly(path);
                requestedView = "default";
            }

            ZoomView view;
            if (!TryResolveZoomView(requestedView, out view))
                return "# " + path + "\n\nUnknown view: " + requestedView;

            string syncNote = string.Empty;
            if (ToolUtil.GetBool(args, "syncView", true))
            {
                if (TryReadMapFocusBounds(args, out int fxMin, out int fyMin, out int fxMax, out int fyMax, out string focusError))
                {
                    if (!string.IsNullOrWhiteSpace(focusError))
                        return "# " + path + "\n\n" + focusError;

                    syncNote = SyncZoomCameraAndView(args, fxMin, fyMin, fxMax, fyMax, new List<ZoomView> { view });
                }
                else
                {
                    ApplyZoomOverlayMode(view.Mode, ToolUtil.GetBool(args, "allowSound", false));
                }
            }

            if (!TryGetCameraBounds(out int xMin, out int xMax, out int yMin, out int yMax))
                return "# " + path + "\n\nCamera not initialized.";

            bool compact = ShouldCompactMap(args);
            string map = GetMapMd("[视图: " + view.Name + "] Camera Viewport Map (X: "
                + xMin + "~" + xMax + ", Y: " + yMin + "~" + yMax + ")",
                xMin, xMax, yMin, yMax, view.Mode, compact);
            if (string.IsNullOrWhiteSpace(syncNote))
                return map;
            return "- 直播视角: " + syncNote + "\n\n" + map;
        }

        private static bool ShouldCompactMap(JObject args)
        {
            string format = FirstZoomText(args, "format", "profile", "mode");
            if (!string.IsNullOrWhiteSpace(format)
                && (format.Equals("edit", StringComparison.OrdinalIgnoreCase)
                    || format.Equals("editing", StringComparison.OrdinalIgnoreCase)
                    || format.Equals("raw", StringComparison.OrdinalIgnoreCase)
                    || format.Equals("uncompressed", StringComparison.OrdinalIgnoreCase)))
                return false;
            return ToolUtil.GetBool(args, "compact", true);
        }

        private static bool HasMapFormatArgs(JObject args)
        {
            return args["compact"] != null
                || args["format"] != null
                || args["profile"] != null
                || args["view"] != null
                || args["activeView"] != null
                || args["displayView"] != null
                || args["x"] != null
                || args["y"] != null
                || args["x1"] != null
                || args["y1"] != null
                || args["x2"] != null
                || args["y2"] != null;
        }

        private static bool TryGetCameraBounds(out int xMin, out int xMax, out int yMin, out int yMax)
        {
            xMin = xMax = yMin = yMax = 0;
            if (Camera.main == null)
                return false;

            var cam = Camera.main;
            var pos = cam.transform.position;
            float size = cam.orthographicSize;
            float aspect = cam.aspect;
            xMin = Mathf.Clamp(Mathf.RoundToInt(pos.x - size * aspect), 0, Grid.WidthInCells - 1);
            xMax = Mathf.Clamp(Mathf.RoundToInt(pos.x + size * aspect), 0, Grid.WidthInCells - 1);
            yMin = Mathf.Clamp(Mathf.RoundToInt(pos.y - size), 0, Grid.HeightInCells - 1);
            yMax = Mathf.Clamp(Mathf.RoundToInt(pos.y + size), 0, Grid.HeightInCells - 1);
            return true;
        }
    }
}
