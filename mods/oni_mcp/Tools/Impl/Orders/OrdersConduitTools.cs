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
        public static McpTool EmptyConduits()
        {
            return new McpTool
            {
                Name = "conduits_empty_area",
                Hidden = true,
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "empty_pipe_area", "orders_empty_pipe" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=empty_conduits。按区域标记气管、液管或运输轨道清空内容，对应游戏 Empty Pipe 工具",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "清空类型：all、gas、liquid、solid，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "gas", "liquid", "solid" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "清空差事优先级 1-9，默认 5", Required = false },
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
                        return CallToolResult.Error("confirm=true is required when emptying conduits in more than 100 cells");

                    var layers = GetEmptyLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("type must be all, gas, liquid or solid");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var layer in layers)
                            {
                                var go = Grid.Objects[cell, (int)layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var workable = go.GetComponent<IEmptyConduitWorkable>();
                                if (workable == null)
                                    continue;

                                if (DebugHandler.InstantBuildMode)
                                    workable.EmptyContents();
                                else
                                    workable.MarkForEmptying();
                                ApplyPriority(go, args);
                                marked++;
                                results.Add(ObjectResult(go, DebugHandler.InstantBuildMode ? "instant_emptied" : "marked"));
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "all" : args["type"].ToString(),
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CutConduits()
        {
            return new McpTool
            {
                Name = "conduits_cut",
                Hidden = true,
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders_cut_conduits", "cut_conduits" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=cut_conduits。按格子或矩形区域剪断管路/电线/运输轨道，实际下达拆除对应段的命令，需要 confirm=true",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格处理", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格处理", Required = false },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "剪断类型：auto=气/液/固体管路，all=管路+电线+自动化线+运输管，或指定 gas/liquid/solid/wire/logic/travel_tube；默认 auto",
                        Required = false,
                        EnumValues = new List<string> { "auto", "all", "gas", "liquid", "solid", "wire", "logic", "travel_tube" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拆除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for cutting conduits");
                    if (!HasRectInput(args) && (args["x"] == null || args["y"] == null))
                        return CallToolResult.Error("areaId, x/y or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 200)
                        return CallToolResult.Error("Refusing to cut more than 200 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var layers = GetCutLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("Invalid type; expected auto, all, gas, liquid, solid, wire, logic or travel_tube");
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int queued = 0;
                    int skipped = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var go in FindCuttableObjects(cell, layers))
                            {
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                string error;
                                if (QueueDeconstruct(go, args, out error))
                                {
                                    queued++;
                                    results.Add(CutResult(go, cell, "queued", null));
                                }
                                else
                                {
                                    skipped++;
                                    results.Add(CutResult(go, cell, "skipped", error));
                                }
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = queued,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "auto" : args["type"].ToString(),
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static List<ObjectLayer> GetCutLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "auto" || type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit });
            if (type == "auto" || type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit });
            if (type == "auto" || type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit });
            if (type == "auto" || type == "all" || type == "wire")
                layers.AddRange(new[] { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire });
            if (type == "auto" || type == "all" || type == "logic")
                layers.AddRange(new[] { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire });
            if (type == "all" || type == "travel_tube")
                layers.AddRange(new[] { ObjectLayer.TravelTubeTile, ObjectLayer.ReplacementTravelTube, ObjectLayer.Building });
            return layers;
        }

        private static List<ObjectLayer> GetEmptyLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile });
            if (type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile });
            if (type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile });
            return layers;
        }

        private static IEnumerable<GameObject> FindCuttableObjects(int cell, List<ObjectLayer> layers)
        {
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;
                if (layer == ObjectLayer.Building && go.GetComponent<TravelTube>() == null)
                    continue;
                yield return go;
            }
        }

        private static bool QueueDeconstruct(GameObject go, Newtonsoft.Json.Linq.JObject args, out string error)
        {
            var deconstructable = go.GetComponent<Deconstructable>();
            if (deconstructable == null)
            {
                error = "Target is not deconstructable";
                return false;
            }
            if (!deconstructable.allowDeconstruction && !DebugHandler.InstantBuildMode)
            {
                error = "Target does not allow deconstruction";
                return false;
            }

            deconstructable.QueueDeconstruction(userTriggered: true);
            ApplyPriority(go, args);
            error = null;
            return true;
        }

        private static Dictionary<string, object> CutResult(GameObject go, int cell, string status, string error)
        {
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            var result = new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? -1,
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell)
            };
            if (!string.IsNullOrEmpty(error))
                result["error"] = error;
            return result;
        }
}
}
