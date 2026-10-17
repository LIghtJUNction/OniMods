using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string ReadMapFileWithArgs(JObject args, string path)
        {
            if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return ReadFileDirectly(path);

            return ReadMapRectangle(args, path, ResolveReadMapView(args));
        }

        private static string ReadMapLayerWithArgs(JObject args, string path)
        {
            const string prefix = "layer_";
            string name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException("Invalid map layer path: " + path);
            string[] parts = name.Substring(prefix.Length).Split('_');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int lower)
                || !int.TryParse(parts[1], out int upper))
                throw new ArgumentException("Invalid map layer path: " + path);

            int worldId = ClusterManager.Instance != null ? ClusterManager.Instance.activeWorldId : -1;
            var world = ClusterManager.Instance != null ? ClusterManager.Instance.GetWorld(worldId) : null;
            if (world == null)
                throw new ArgumentException("No active world is loaded.");
            if (lower < 0 || upper < lower || upper >= world.WorldSize.y
                || lower % VirtualMapLayerSize != 0
                || upper != Math.Min(lower + VirtualMapLayerSize - 1, world.WorldSize.y - 1))
                throw new ArgumentException("Map layer must match a listed 32-cell layer in the active world.");

            ZoomView view = ResolveReadMapView(args);
            if (ToolUtil.GetBool(args, "syncView", false))
                ApplyZoomOverlayMode(view.Mode, ToolUtil.GetBool(args, "allowSound", false));
            return GetMapMd("[视图: " + view.Name + "] " + path,
                world.WorldOffset.x, world.WorldOffset.x + world.WorldSize.x - 1,
                world.WorldOffset.y + lower, world.WorldOffset.y + upper,
                view.Mode, ShouldCompactMap(args));
        }

        private static ZoomView ResolveReadMapView(JObject args)
        {
            // Output format must not change the meaning of the map.
            string requestedView = FirstZoomText(args, "view", "activeView", "displayView");
            ZoomView view;
            if (string.IsNullOrWhiteSpace(requestedView))
            {
                HashedString mode = OverlayScreen.Instance != null ? OverlayScreen.Instance.mode : OverlayModes.None.ID;
                view = new ZoomView { Name = GetOverlayViewName(mode), Mode = mode };
            }
            else if (!TryResolveZoomView(requestedView, out view))
            {
                throw new ArgumentException("Unknown map view: " + requestedView);
            }

            return view;
        }

        private static string ReadMapRectangle(JObject args, string path, ZoomView view)
        {
            // Resolve the read rectangle before any optional camera changes.
            bool explicitBounds = TryReadMapFocusBounds(args, out int xMin, out int yMin,
                out int xMax, out int yMax, out string focusError);
            if (!string.IsNullOrWhiteSpace(focusError))
                throw new ArgumentException(focusError);
            if (!explicitBounds && !TryGetCameraBounds(out xMin, out xMax, out yMin, out yMax))
                throw new ArgumentException("Camera not initialized; supply explicit map bounds.");

            string syncNote = string.Empty;
            if (ToolUtil.GetBool(args, "syncView", false))
            {
                if (explicitBounds)
                {
                    // The rendered view is authoritative, including infrastructure files.
                    var syncArgs = (JObject)args.DeepClone();
                    syncArgs.Remove("view");
                    syncArgs.Remove("activeView");
                    syncArgs.Remove("displayView");
                    syncNote = SyncZoomCameraAndView(syncArgs, xMin, yMin, xMax, yMax, new List<ZoomView> { view });
                }
                else
                    ApplyZoomOverlayMode(view.Mode, ToolUtil.GetBool(args, "allowSound", false));
            }

            bool compact = ShouldCompactMap(args);
            string map = GetMapMd("[视图: " + view.Name + "] " + path + " (X: "
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
