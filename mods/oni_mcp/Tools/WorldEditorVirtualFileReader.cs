using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const int VirtualMapLayerSize = 32;

        public static string ReadFileDirectly(string path)
        {
            bool isMd = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
            HashedString activeMode = OverlayScreen.Instance != null ? OverlayScreen.Instance.mode : OverlayModes.None.ID;
            string viewName = GetOverlayViewName(activeMode);
            bool isTempView = activeMode == OverlayModes.Temperature.ID;

            if (path == "/active/screenshots/index.md")
                return ReadScreenshotsIndexMarkdown();

            if (path == "/active/map/viewport.html" || path == "/active/map/viewport.md" || path == "/active/map/index.html" || path == "/active/map/index.md")
            {
                try
                {
                    if (Camera.main == null)
                        return "<h1>Error</h1><p>Camera not initialized or main camera is not available.</p>";

                    var cam = Camera.main;
                    var pos = cam.transform.position;
                    float size = cam.orthographicSize;
                    float aspect = cam.aspect;

                    int xMin = Mathf.Clamp(Mathf.RoundToInt(pos.x - size * aspect), 0, Grid.WidthInCells - 1);
                    int xMax = Mathf.Clamp(Mathf.RoundToInt(pos.x + size * aspect), 0, Grid.WidthInCells - 1);
                    int yMin = Mathf.Clamp(Mathf.RoundToInt(pos.y - size), 0, Grid.HeightInCells - 1);
                    int yMax = Mathf.Clamp(Mathf.RoundToInt(pos.y + size), 0, Grid.HeightInCells - 1);

                    int width = xMax - xMin + 1;

                    if (isMd)
                    {
                        return GetMapMd($"[视图: {viewName}] Camera Viewport Map (X: {xMin}~{xMax}, Y: {yMin}~{yMax})", xMin, xMax, yMin, yMax);
                    }

                    var sbCells = new StringBuilder();
                    for (int y = yMax; y >= yMin; y--)
                    {
                        for (int x = xMin; x <= xMax; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            string elemName = "Vacuum";
                            string color = "#1a202c";
                            float temp = 0f;
                            string bldName = "";
                            GameObject go = null;

                            if (Grid.IsValidCell(cell))
                            {
                                var elem = Grid.Element[cell];
                                temp = Grid.Temperature[cell];
                                if (elem != null)
                                    elemName = elem.id.ToString();

                                go = Grid.Objects[cell, (int)ObjectLayer.Building];
                                if (go != null)
                                {
                                    var cmp = go.GetComponent<BuildingComplete>();
                                    bldName = cmp != null ? cmp.name : go.name;
                                }

                                color = GetHtmlCellColor(cell, elem, go, activeMode, temp, bldName);
                            }

                            string tooltip = $"Cell: {cell} ({x}, {y})\nElement: {elemName}\nTemp: {temp - 273.15f:F1}°C";
                            if (!string.IsNullOrEmpty(bldName))
                            {
                                tooltip += $"\nBuilding: {bldName}";
                            }

                            sbCells.AppendFormat("<div class=\"cell\" style=\"background:{0};\" title=\"{1}\"></div>", color, WebUtility.HtmlEncode(tooltip));
                        }
                    }

                    string legendHtml = "";
                    if (activeMode == OverlayModes.Temperature.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#90cdf4;""></div>绝对零度 (&lt;-260°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#00b5d8;""></div>寒冷 (-260°C ~ -18°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#4299e1;""></div>冰冷 (-18°C ~ 0°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#48bb78;""></div>温和 (0°C ~ 20°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>温暖 (20°C ~ 35°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ed8936;""></div>炎热 (35°C ~ 100°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#e57373;""></div>灼热 (100°C ~ 1000°C)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#e53e3e;""></div>熔融 (&gt;1000°C)</div>";
                    }
                    else if (activeMode == OverlayModes.Power.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#dd6b20;""></div>电力设备 (Power Device)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>电线 (Wire)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                    }
                    else if (activeMode == OverlayModes.Oxygen.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#48bb78;""></div>易于呼吸 (Breathable, &gt;=600g)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>可呼吸 (Passable, 100g~600g)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ed8936;""></div>呼吸困难 (Difficult, &lt;100g)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#e53e3e;""></div>不可呼吸 (Unbreathable)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>固体方块 (Solid Block)</div>";
                    }
                    else if (activeMode == OverlayModes.LiquidConduits.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#3182ce;""></div>液体管道 (Liquid Pipe)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                    }
                    else if (activeMode == OverlayModes.GasConduits.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#38a169;""></div>气体管道 (Gas Pipe)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                    }
                    else if (activeMode == OverlayModes.Light.ID)
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#fffbeb;""></div>晒伤级强光 (Sunburn, &gt;=72500 Lux)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#fef08a;""></div>工作明亮 (Bright, 1000~72500 Lux)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#fde047;""></div>种植普通 (Normal, 200~1000 Lux)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ca8a04;""></div>微光 (Dim, &lt;200 Lux)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#111827;""></div>黑暗 (Dark, 0 Lux)</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>固体方块 (Solid Block)</div>";
                    }
                    else
                    {
                        legendHtml = @"
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#1a202c;""></div>Vacuum / Space</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>Building</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#8c5b30;""></div>Dirt</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#a88060;""></div>Sandstone</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#3182ce;""></div>Water</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#4a3728;""></div>Polluted Water</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#38a169;""></div>Algae</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#718096;""></div>Granite / Rock</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#e6fffa;""></div>Oxygen Gas</div>
                            <div class=""legend-item""><div class=""legend-color"" style=""background:#feb2b2;""></div>Carbon Dioxide</div>";
                    }

                    return GetMapHtml(
                        $"[视图: {viewName}] Camera Viewport Map (X: {xMin}~{xMax}, Y: {yMin}~{yMax})",
                        "/active/map/",
                        "← Back to Map",
                        "Currently showing visible grid based on camera view position. Move camera in game to update viewport area.",
                        width,
                        sbCells.ToString(),
                        legendHtml
                    );
                }
                catch (Exception ex)
                {
                    return $"<h1>Error rendering viewport map</h1><p>{ex.Message}</p>";
                }
            }

            if (path.StartsWith("/active/", StringComparison.Ordinal)
                && TryParseCellSnapshotPath(SaveRelativePath(path), out int cellX, out int cellY))
                return ReadCellSnapshotMarkdown(new JObject(), cellX, cellY);

            if (path.StartsWith("/active/", StringComparison.Ordinal)
                && TryParseZoomPath(SaveRelativePath(path), out int zoomX1, out int zoomY1, out int zoomX2, out int zoomY2))
                return ReadZoomMarkdown(zoomX1, zoomY1, zoomX2, zoomY2, DefaultZoomViews());

            if (path.StartsWith("/active/map/layers/layer_", StringComparison.Ordinal) && (path.EndsWith(".html", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal)))
            {
                try
                {
                    string filename = Path.GetFileNameWithoutExtension(path);
                    string parts = filename.Substring("layer_".Length);
                    string[] split = parts.Split('_');
                    if (split.Length == 2
                        && int.TryParse(split[0], out int relYMin)
                        && int.TryParse(split[1], out int relYMax))
                    {
                        int worldId = ClusterManager.Instance?.activeWorldId ?? 0;
                        var world = ClusterManager.Instance?.GetWorld(worldId);
                        if (world != null)
                        {
                            int height = world.WorldSize.y;
                            if (relYMin < 0
                                || relYMax < relYMin
                                || relYMax >= height
                                || relYMin % VirtualMapLayerSize != 0)
                            {
                                return "<h1>Invalid map layer</h1><p>Requested map layer must match a listed 32-cell layer within the active world bounds.</p>";
                            }

                            int expectedRelYMax = Math.Min(relYMin + VirtualMapLayerSize - 1, height - 1);
                            if (relYMax != expectedRelYMax)
                            {
                                return "<h1>Invalid map layer</h1><p>Requested map layer must match a listed 32-cell layer within the active world bounds.</p>";
                            }

                            int xMin = world.WorldOffset.x;
                            int xMax = world.WorldOffset.x + world.WorldSize.x - 1;
                            int yMin = world.WorldOffset.y + relYMin;
                            int yMax = world.WorldOffset.y + relYMax;
                            int width = world.WorldSize.x;

                            if (isMd)
                            {
                                return GetMapMd($"[视图: {viewName}] Map Layer Y = {relYMin} to {relYMax}", xMin, xMax, yMin, yMax);
                            }

                            var sbCells = new StringBuilder();
                            for (int y = yMax; y >= yMin; y--)
                            {
                                for (int x = xMin; x <= xMax; x++)
                                {
                                    int cell = Grid.XYToCell(x, y);
                                    string elemName = "Vacuum";
                                    string color = "#1a202c";
                                    float temp = 0f;
                                    string bldName = "";
                                    GameObject go = null;

                                    if (Grid.IsValidCell(cell))
                                    {
                                        var elem = Grid.Element[cell];
                                        temp = Grid.Temperature[cell];
                                        if (elem != null)
                                            elemName = elem.id.ToString();

                                        go = Grid.Objects[cell, (int)ObjectLayer.Building];
                                        if (go != null)
                                        {
                                            var cmp = go.GetComponent<BuildingComplete>();
                                            bldName = cmp != null ? cmp.name : go.name;
                                        }

                                        color = GetHtmlCellColor(cell, elem, go, activeMode, temp, bldName);
                                    }

                                    string tooltip = $"Cell: {cell} ({x}, {y})\nElement: {elemName}\nTemp: {temp - 273.15f:F1}°C";
                                    if (!string.IsNullOrEmpty(bldName))
                                    {
                                        tooltip += $"\nBuilding: {bldName}";
                                    }

                                    sbCells.AppendFormat("<div class=\"cell\" style=\"background:{0};\" title=\"{1}\"></div>", color, WebUtility.HtmlEncode(tooltip));
                                }
                            }

                            string legendHtml = "";
                            if (activeMode == OverlayModes.Temperature.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#90cdf4;""></div>绝对零度 (&lt;-260°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#00b5d8;""></div>寒冷 (-260°C ~ -18°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#4299e1;""></div>冰冷 (-18°C ~ 0°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#48bb78;""></div>温和 (0°C ~ 20°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>温暖 (20°C ~ 35°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ed8936;""></div>炎热 (35°C ~ 100°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#e57373;""></div>灼热 (100°C ~ 1000°C)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#e53e3e;""></div>熔融 (&gt;1000°C)</div>";
                            }
                            else if (activeMode == OverlayModes.Power.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#dd6b20;""></div>电力设备 (Power Device)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>电线 (Wire)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                            }
                            else if (activeMode == OverlayModes.Oxygen.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#48bb78;""></div>易于呼吸 (Breathable, &gt;=600g)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>可呼吸 (Passable, 100g~600g)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ed8936;""></div>呼吸困难 (Difficult, &lt;100g)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#e53e3e;""></div>不可呼吸 (Unbreathable)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>固体方块 (Solid Block)</div>";
                            }
                            else if (activeMode == OverlayModes.LiquidConduits.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#3182ce;""></div>液体管道 (Liquid Pipe)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                            }
                            else if (activeMode == OverlayModes.GasConduits.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#38a169;""></div>气体管道 (Gas Pipe)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>其他 (Others)</div>";
                            }
                            else if (activeMode == OverlayModes.Light.ID)
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#fffbeb;""></div>晒伤级强光 (Sunburn, &gt;=72500 Lux)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#fef08a;""></div>工作明亮 (Bright, 1000~72500 Lux)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#fde047;""></div>种植普通 (Normal, 200~1000 Lux)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ca8a04;""></div>微光 (Dim, &lt;200 Lux)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#111827;""></div>黑暗 (Dark, 0 Lux)</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#2d3748;""></div>固体方块 (Solid Block)</div>";
                            }
                            else
                            {
                                legendHtml = @"
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#1a202c;""></div>Vacuum / Space</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#ecc94b;""></div>Building</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#8c5b30;""></div>Dirt</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#a88060;""></div>Sandstone</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#3182ce;""></div>Water</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#4a3728;""></div>Polluted Water</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#38a169;""></div>Algae</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#718096;""></div>Granite / Rock</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#e6fffa;""></div>Oxygen Gas</div>
                                    <div class=""legend-item""><div class=""legend-color"" style=""background:#feb2b2;""></div>Carbon Dioxide</div>";
                            }

                            return GetMapHtml(
                                $"[视图: {viewName}] Map Layer Y = {relYMin} to {relYMax}",
                                "/active/map/layers/",
                                "← Back to Layers",
                                "Hover over cells to see coordinates, elements, and temperatures. Yellow cells represent buildings.",
                                width,
                                sbCells.ToString(),
                                legendHtml
                            );
                        }
                    }
                }
                catch (Exception ex)
                {
                    return $"<h1>Error rendering layer</h1><p>{ex.Message}</p>";
                }
            }
            return "<h1>File not found</h1>";
        }

    }
}
