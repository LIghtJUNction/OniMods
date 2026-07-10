using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class PowerAndRoomTools
    {
        public static McpTool InfrastructureReadControl()
        {
            return new McpTool
            {
                Name = "infrastructure_read_control",
                Group = "infrastructure",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "infrastructure_read" },
                Tags = new List<string> { "power", "electricity", "rooms", "infrastructure", "电力", "房间", "基础设施" },
                Description = "电力/房间只读聚合入口：action=power_summary/power_ports/ports/rooms；ports 返回电力、液管、气管、信号、运输端口。",
                Parameters = InfrastructureReadParams(),
                Handler = args =>
                {
                    args = args ?? new JObject();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("action");

                    switch (action)
                    {
                        case "power_summary":
                            return GetPowerSummary().Handler(forwarded);
                        case "power_ports":
                            return GetBuildingPowerPorts().Handler(forwarded);
                        case "ports":
                        case "utility_ports":
                        case "all_ports":
                            return InfrastructurePortReadTools.ReadPorts(forwarded);
                        case "rooms":
                            return ListRooms().Handler(forwarded);
                        default:
                            return CallToolResult.Error("Unsupported action. Use power_summary, power_ports, ports, or rooms.");
                    }
                }
            };
        }

        public static McpTool GetBuildingPowerPortsCompat()
        {
            var tool = GetBuildingPowerPorts();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=power_ports。";
            return tool;
        }

        public static McpTool GetPowerSummaryCompat()
        {
            var tool = GetPowerSummary();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=power_summary。";
            return tool;
        }

        public static McpTool ListRoomsCompat()
        {
            var tool = ListRooms();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=rooms。";
            return tool;
        }

        public static McpTool GetBuildingPowerPorts()
        {
            return new McpTool
            {
                Name = "building_power_ports",
                Group = "power",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "power_ports_list", "building_power_connection_points", "power_connection_points" },
                Tags = new List<string> { "power", "electricity", "ports", "connector", "wire", "电力", "接口", "接线" },
                Description = "列出指定区域内已建建筑和蓝图建筑的电力接口格：锚点、输入/输出端口、相对偏移和可接线状态，方便直接接线而不是猜文本地图",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选，例如 battery、generator、transformer、wire", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 120，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 120, 500);

                    var results = new List<Dictionary<string, object>>();
                    var seen = new HashSet<string>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (rect != null && !CellInRect(cell, rect, worldId))
                            continue;
                        if (!MatchesQuery(go, query))
                            continue;

                        var def = building.Def;
                        if (def == null)
                            continue;

                        bool hasInput = def.RequiresPowerInput;
                        bool hasOutput = def.RequiresPowerOutput;
                        if (!hasInput && !hasOutput)
                            continue;

                        results.Add(BuildingPowerPortInfo(go, def, hasInput, hasOutput, "built"));
                        seen.Add(BuildingPortKey(go, def));
                        if (results.Count >= limit)
                            break;
                    }

                    if (results.Count < limit)
                    {
                        foreach (var constructable in FindConstructables(worldId))
                        {
                            var go = constructable?.gameObject;
                            if (go == null)
                                continue;
                            int cell = Grid.PosToCell(go);
                            if (rect != null && !CellInRect(cell, rect, worldId))
                                continue;
                            if (!MatchesQuery(go, query))
                                continue;

                            var building = go.GetComponent<Building>();
                            var kpid = go.GetComponent<KPrefabID>();
                            var def = building?.Def ?? Assets.GetBuildingDef(kpid?.PrefabTag.Name ?? go.name);
                            if (def == null)
                                continue;
                            if (seen.Contains(BuildingPortKey(go, def)))
                                continue;

                            bool hasInput = def.RequiresPowerInput;
                            bool hasOutput = def.RequiresPowerOutput;
                            if (!hasInput && !hasOutput)
                                continue;

                            results.Add(BuildingPowerPortInfo(go, def, hasInput, hasOutput, "blueprint"));
                            if (results.Count >= limit)
                                break;
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["rect"] = rect,
                        ["buildings"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
