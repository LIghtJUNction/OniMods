using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        public static McpTool MopArea()
        {
            return new McpTool
            {
                Name = "orders_mop_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "mop_area", "liquids_mop_area", "water_mop_area", "mop_liquid_area", "mop_water_area" },
                Tags = new List<string> { "orders", "mop", "liquid", "water", "polluted-water", "floor", "spill", "地上的水", "拖地", "液体", "水" },
                Description = "在矩形区域内对地上的水、污水或其他可拖地液体格子下达拖地命令；不是清扫固体碎片。遵循游戏限制：下方必须有地面且液体质量不能超过可拖地上限",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拖地差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when mopping more than 100 cells");

                    var prefab = Assets.GetPrefab(new Tag("MopPlacer"));
                    if (prefab == null)
                        return CallToolResult.Error("MopPlacer prefab is not available");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int marked = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    var targetCells = new List<int>();
                    var executionSkipped = new Dictionary<string, int>();
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;
                            if (Grid.Solid[cell] || !Grid.Element[cell].IsLiquid)
                                continue;
                        if (Grid.Objects[cell, (int)ObjectLayer.MopPlacer] != null)
                        {
                            skipped++;
                            IncrementSkip(executionSkipped, "already_queued");
                            continue;
                        }
                            bool onFloor = Grid.IsValidCell(Grid.CellBelow(cell)) && Grid.Solid[Grid.CellBelow(cell)];
                            bool smallEnough = Grid.Mass[cell] <= MopTool.maxMopAmt;
                        if (!onFloor || !smallEnough)
                        {
                            skipped++;
                            IncrementSkip(executionSkipped, onFloor ? "too_much_liquid" : "no_floor");
                            results.Add(CellResult(cell, onFloor ? "skipped_too_much_liquid" : "skipped_no_floor"));
                            continue;
                        }

                            if (DebugHandler.InstantBuildMode)
                            {
                            Moppable.MopCell(cell, 1000000f, null);
                            marked++;
                            targetCells.Add(cell);
                            results.Add(CellResult(cell, "instant_mopped"));
                            continue;
                        }

                            var placer = Util.KInstantiate(prefab);
                            Grid.Objects[cell, (int)ObjectLayer.MopPlacer] = placer;
                            var position = Grid.CellToPosCBC(cell, Grid.SceneLayer.Move);
                            position.z -= 0.15f;
                            placer.transform.SetPosition(position);
                            placer.SetActive(true);
                        ApplyPriority(placer, args);
                        marked++;
                        targetCells.Add(cell);
                        results.Add(CellResult(cell, "marked"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["skipped"] = skipped,
                        ["execution"] = CellExecutionMetadata("mop", worldId, targetCells, executionSkipped, false, 200),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DisinfectArea()
        {
            return new McpTool
            {
                Name = "orders_disinfect_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "disinfect_area", "germs_disinfect_area" },
                Description = "兼容入口：请优先使用 orders_control domain=area action=disinfect。标记矩形区域内带病菌且支持消毒的对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "消毒差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when disinfecting more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var disinfectable = go.GetComponent<Disinfectable>();
                                var element = go.GetComponent<PrimaryElement>();
                                if (disinfectable == null || element == null || element.DiseaseCount <= 0)
                                    continue;

                                disinfectable.MarkForDisinfect();
                                ApplyPriority(go, args);
                                marked++;
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ScanLiquidCells(Dictionary<string, int> rect, int worldId, int sampleLimit)
        {
            int count = 0;
            var samples = new List<Dictionary<string, object>>();

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (Grid.Solid[cell] || !Grid.Element[cell].IsLiquid)
                        continue;

                    count++;
                    if (samples.Count < sampleLimit)
                        samples.Add(CellResult(cell, "liquid_in_sweep_area"));
                }
            }

            return new Dictionary<string, object>
            {
                ["count"] = count,
                ["samples"] = samples
            };
        }

        private static List<Dictionary<string, object>> SweepPreviewRisks(Dictionary<string, object> liquidScan)
        {
            var risks = new List<Dictionary<string, object>>();
            int liquidCount = liquidScan != null && liquidScan.ContainsKey("count") ? Convert.ToInt32(liquidScan["count"]) : 0;
            if (liquidCount > 0)
            {
                risks.Add(new Dictionary<string, object>
                {
                    ["type"] = "liquid_in_area",
                    ["severity"] = "info",
                    ["count"] = liquidCount,
                    ["samples"] = liquidScan.ContainsKey("samples") ? liquidScan["samples"] : null,
                    ["message"] = "Sweep ignores liquid cells; use orders_control domain=area action=mop for floor liquids."
                });
            }
            return risks;
        }
}
}
