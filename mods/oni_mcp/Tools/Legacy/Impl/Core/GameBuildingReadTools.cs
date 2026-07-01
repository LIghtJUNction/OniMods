using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// 游戏控制相关 MCP Tools
    /// </summary>
    public static partial class GameControlTools
    {
        public static McpTool ControlBuildingsRead()
        {
            return new McpTool
            {
                Name = "buildings_read_control",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "buildings_control", "building_read_control" },
                Tags = new List<string> { "buildings", "read", "summary" },
                Description = "统一读取建筑列表和建筑类型摘要；action=list/summary。兼容 buildings_list 与 buildings_summary。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "读取动作：list=建筑明细列表，summary=按建筑类型聚合摘要",
                        Required = true
                    },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Bed, Tile 等），留空返回所有",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少个建筑/建筑类型，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    var action = args["action"]?.ToString()?.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                        case "buildings":
                            return GetBuildings().Handler(args);
                        case "summary":
                        case "aggregate":
                            return GetBuildingSummary().Handler(args);
                        default:
                            return CallToolResult.Error("action must be one of: list, summary");
                    }
                }
            };
        }

        public static McpTool GetBuildings()
        {
            return new McpTool
            {
                Name = "buildings_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "get_buildings" },
                Description = "兼容入口：请优先使用 read_control domain=buildings action=list。获取殖民地建筑列表，可按类型筛选",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Bed 等），留空返回所有",
                        Required = false
                    },
                    ["query"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "按建筑名或 prefabId 过滤；search/target/name 也会作为别名使用",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少个建筑，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filterType = args["type"]?.ToString()?.ToLower();
                    string query = args["query"]?.ToString();
                    if (string.IsNullOrWhiteSpace(query)) query = args["search"]?.ToString();
                    if (string.IsNullOrWhiteSpace(query)) query = args["target"]?.ToString();
                    if (string.IsNullOrWhiteSpace(query)) query = args["name"]?.ToString();
                    query = query?.ToLower();
                    if (string.IsNullOrWhiteSpace(filterType))
                        filterType = query;
                    int limit = 100;
                    if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out int parsedLimit))
                        limit = Math.Max(1, Math.Min(parsedLimit, 500));

                    var buildings = new List<Dictionary<string, object>>();
                    var seen = new HashSet<string>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null) continue;

                        var prefabName = building.name;
                        var def = building.Def;
                        var name = ToolUtil.CleanName(def?.Name ?? prefabName);
                        var position = building.transform?.position ?? Vector3.zero;
                        var x = Mathf.RoundToInt(position.x);
                        var y = Mathf.RoundToInt(position.y);
                        var worldId = building.GetMyWorldId();
                        var identity = (def?.PrefabID ?? prefabName) + "|" + x + "|" + y + "|" + worldId;
                        if (!seen.Add(identity)) continue;

                        if (!string.IsNullOrEmpty(filterType) &&
                            !name.ToLower().Contains(filterType) &&
                            !prefabName.ToLower().Contains(filterType))
                            continue;

                        var operational = building.GetComponent<Operational>();

                        buildings.Add(new Dictionary<string, object>
                        {
                            ["name"] = name,
                            ["prefabId"] = def?.PrefabID ?? "unknown",
                            ["position"] = new { x, y },
                            ["isOperational"] = operational?.IsOperational ?? false,
                            ["isActive"] = operational?.IsActive ?? false,
                            ["worldId"] = worldId
                        });
                    }

                    var limited = buildings.Take(limit).ToList();
                    var summary = new Dictionary<string, object>
                    {
                        ["total"] = buildings.Count,
                        ["returned"] = limited.Count,
                        ["query"] = query,
                        ["buildings"] = limited
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(summary, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetBuildingSummary()
        {
            return new McpTool
            {
                Name = "buildings_summary",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "get_building_summary" },
                Description = "兼容入口：请优先使用 read_control domain=buildings action=summary。按建筑类型聚合统计数量、运行状态和世界分布",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "建筑类型筛选（如 Generator, Pump, Tile 等），留空返回所有",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种建筑，默认 100，最大 500",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filterType = args["type"]?.ToString()?.ToLower();
                    int limit = 100;
                    if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out int parsedLimit))
                        limit = Math.Max(1, Math.Min(parsedLimit, 500));

                    var groups = new Dictionary<string, BuildingAggregate>();
                    var seen = new HashSet<string>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null) continue;

                        var def = building.Def;
                        var prefabId = def?.PrefabID ?? building.name;
                        var name = ToolUtil.CleanName(def?.Name ?? prefabId);
                        var position = building.transform?.position ?? Vector3.zero;
                        var x = Mathf.RoundToInt(position.x);
                        var y = Mathf.RoundToInt(position.y);
                        var worldId = building.GetMyWorldId();
                        var identity = prefabId + "|" + x + "|" + y + "|" + worldId;
                        if (!seen.Add(identity)) continue;

                        if (!string.IsNullOrEmpty(filterType) &&
                            !name.ToLower().Contains(filterType) &&
                            !prefabId.ToLower().Contains(filterType))
                            continue;

                        BuildingAggregate aggregate;
                        if (!groups.TryGetValue(prefabId, out aggregate))
                        {
                            aggregate = new BuildingAggregate
                            {
                                Name = name,
                                PrefabId = prefabId,
                                WorldIds = new HashSet<int>()
                            };
                            groups[prefabId] = aggregate;
                        }

                        var operational = building.GetComponent<Operational>();
                        aggregate.Count++;
                        aggregate.OperationalCount += operational != null && operational.IsOperational ? 1 : 0;
                        aggregate.ActiveCount += operational != null && operational.IsActive ? 1 : 0;
                        aggregate.WorldIds.Add(worldId);
                    }

                    var summaries = groups.Values
                        .OrderByDescending(group => group.Count)
                        .Take(limit)
                        .Select(group => group.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["totalTypes"] = groups.Count,
                        ["returned"] = summaries.Count,
                        ["buildings"] = summaries
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private class BuildingAggregate
        {
            public string Name;
            public string PrefabId;
            public int Count;
            public int OperationalCount;
            public int ActiveCount;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["count"] = Count,
                    ["operationalCount"] = OperationalCount,
                    ["activeCount"] = ActiveCount,
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };
            }
        }

    }
}
