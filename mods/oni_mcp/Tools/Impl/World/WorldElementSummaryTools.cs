using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool GetWorldElementSummary()
        {
            return new McpTool
            {
                Name = "world_element_summary",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_world_element_summary" },
                Tags = new List<string> { "world", "elements", "summary", "statistics", "mass", "temperature", "map", "元素", "统计" },
                Hidden = true,
                Description = "兼容旧工具：请改用 read_control domain=world action=element_summary",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "世界 ID，默认当前激活世界",
                        Required = false
                    },
                    ["state"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "元素状态过滤：all、gas、liquid、solid、vacuum，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "gas", "liquid", "solid", "vacuum" }
                    },
                    ["visibleOnly"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否只统计已揭示格子，默认 true",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种元素，默认 50，最大 200",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int worldId = TryGetInt(args, "worldId", ClusterManager.Instance?.activeWorldId ?? 0);
                    string stateFilter = (args["state"]?.ToString() ?? "all").ToLowerInvariant();
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 50, 200);

                    var groups = new Dictionary<string, ElementAggregate>();
                    int scannedCells = 0;
                    int matchedCells = 0;

                    for (int cell = 0; cell < Grid.CellCount; cell++)
                    {
                        if (!Grid.IsWorldValidCell(cell)) continue;
                        if (Grid.WorldIdx[cell] != worldId) continue;
                        if (visibleOnly && !Grid.IsVisible(cell)) continue;

                        scannedCells++;
                        var element = Grid.Element[cell];
                        string state = ToolUtil.GetElementState(element);
                        if (stateFilter != "all" && stateFilter != state)
                            continue;

                        matchedCells++;
                        string id = element?.id.ToString() ?? "Unknown";
                        ElementAggregate aggregate;
                        if (!groups.TryGetValue(id, out aggregate))
                        {
                            aggregate = new ElementAggregate
                            {
                                Id = id,
                                Name = ToolUtil.CleanName(element?.name ?? id),
                                State = state
                            };
                            groups[id] = aggregate;
                        }

                        float mass = SafeFloat(Grid.Mass[cell]);
                        float temp = SafeFloat(Grid.Temperature[cell]);
                        aggregate.CellCount++;
                        aggregate.TotalMassKg += mass;
                        aggregate.WeightedTemperatureK += temp * Math.Max(mass, 0.001f);
                        aggregate.TemperatureWeight += Math.Max(mass, 0.001f);
                    }

                    var elements = groups.Values
                        .OrderByDescending(item => item.TotalMassKg)
                        .Take(limit)
                        .Select(item => item.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["visibleOnly"] = visibleOnly,
                        ["state"] = stateFilter,
                        ["scannedCells"] = scannedCells,
                        ["matchedCells"] = matchedCells,
                        ["elementTypes"] = groups.Count,
                        ["returned"] = elements.Count,
                        ["elements"] = elements
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }
    }
}
