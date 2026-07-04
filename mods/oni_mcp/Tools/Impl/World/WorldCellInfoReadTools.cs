using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool GetCellInfo()
        {
            return new McpTool
            {
                Name = "world_cell_info",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_cell_info" },
                Tags = new List<string> { "world", "cell", "grid", "terrain", "elements", "temperature", "disease", "map", "格子", "元素" },
                Hidden = true,
                Description = "兼容旧工具：请改用 read_control domain=world action=cell_info",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "格子 X 坐标",
                        Required = true
                    },
                    ["y"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "格子 Y 坐标",
                        Required = true
                    },
                        ["worldId"] = new McpToolParameter
                        {
                            Type = "integer",
                            Description = "目标世界 ID；提供后会校验格子属于该世界",
                            Required = false
                        },
                        ["includeReachability"] = new McpToolParameter
                        {
                            Type = "boolean",
                            Description = "是否附带复制人到该格子的可达性摘要，默认 false",
                            Required = false
                        },
                        ["radius"] = new McpToolParameter
                        {
                            Type = "integer",
                            Description = "可达性采样半径提示，默认 8，最大 20",
                            Required = false
                        }
                    },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int x;
                    int y;
                    if (!int.TryParse(args["x"]?.ToString(), out x) || !int.TryParse(args["y"]?.ToString(), out y))
                        return CallToolResult.Error("x and y are required integer coordinates");

                    if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
                        return CallToolResult.Error("Cell coordinates are outside the grid");

                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid cell");

                    int requestedWorldId = ToolUtil.GetInt(args, "worldId") ?? -1;
                    if (!ToolUtil.CellMatchesWorld(cell, requestedWorldId))
                        return CallToolResult.Error($"Cell ({x},{y}) is not in worldId={requestedWorldId}");

                    var element = Grid.Element[cell];
                    var result = new Dictionary<string, object>
                    {
                        ["cell"] = cell,
                        ["x"] = x,
                        ["y"] = y,
                        ["worldId"] = Grid.WorldIdx[cell],
                        ["isWorldValid"] = Grid.IsWorldValidCell(cell),
                        ["isVisible"] = Grid.IsVisible(cell),
                        ["element"] = element?.id.ToString() ?? "Unknown",
                        ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                        ["state"] = ToolUtil.GetElementState(element),
                        ["massKg"] = Math.Round(SafeFloat(Grid.Mass[cell]), 3),
                        ["temperatureK"] = Math.Round(SafeFloat(Grid.Temperature[cell]), 2),
                        ["temperatureC"] = Math.Round(SafeFloat(Grid.Temperature[cell]) - 273.15f, 2),
                        ["pressureKg"] = Math.Round(SafeFloat(Grid.Pressure[cell]), 3),
                        ["isSolid"] = Grid.Solid[cell],
                        ["isFoundation"] = Grid.Foundation[cell],
                        ["diseaseIdx"] = Grid.DiseaseIdx[cell],
                        ["diseaseCount"] = Grid.DiseaseCount[cell],
                        ["lightIntensity"] = Grid.LightIntensity[cell],
                        ["objectsByLayer"] = CellLayerObjects(cell),
                        ["buildings"] = CellBuildings(cell, requestedWorldId),
                        ["blueprints"] = CellBlueprints(cell, requestedWorldId),
                        ["pickupables"] = CellPickupables(cell, requestedWorldId),
                        ["pickupableSummary"] = CellPickupableSummary(cell, requestedWorldId),
                        ["dupes"] = CellDupes(cell, requestedWorldId),
                        ["plants"] = CellPlants(cell, requestedWorldId),
                        ["utilities"] = CellUtilities(cell),
                        ["buildability"] = CellBuildability(cell, requestedWorldId),
                        ["temperatureComfort"] = CellTemperatureComfort(cell),
                        ["progressiveDetail"] = CellProgressiveDetail(x, y),
                        ["reachability"] = CellReachabilitySummary(args, cell, requestedWorldId)
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
