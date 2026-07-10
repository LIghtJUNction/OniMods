using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TUNING;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SpaceStoryTools
    {
        public static McpTool ControlTelescope()
        {
            return new McpTool
            {
                Name = "telescope_control",
                Group = "space",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "telescope_open_starmap", "starmap_toggle_from_telescope" },
                Tags = new List<string> { "space", "telescope", "starmap", "ui", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=telescope action=open_starmap",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "open_starmap", Required = true, EnumValues = new List<string> { "open_starmap" } }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action != "open_starmap")
                        return CallToolResult.Error("action must be open_starmap");
                    if (ManagementMenu.Instance == null)
                        return CallToolResult.Error("ManagementMenu not initialized");
                    ManagementMenu.Instance.ToggleStarmap();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["analysis"] = AnalysisSummary(includeDestinations: false)
                    });
                }
            };
        }

        public static McpTool ListTemporalTears()
        {
            return new McpTool
            {
                Name = "temporal_tears_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "temporal_tear_status", "wormhole_status" },
                Tags = new List<string> { "story", "temporal-tear", "wormhole", "rocket", "cluster", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=temporal_tear action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭名、状态或位置筛选候选火箭", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回候选火箭数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    var tear = GetTemporalTear();
                    if (tear == null)
                        return CallToolResult.Error("TemporalTear not found");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var candidates = TemporalTearCandidates(tear)
                        .Select(ClustercraftInfo)
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["tear"] = TemporalTearInfo(tear),
                        ["returned"] = candidates.Count,
                        ["candidateCraft"] = candidates
                    });
                }
            };
        }

        public static McpTool ConsumeTemporalTearCraft()
        {
            return new McpTool
            {
                Name = "temporal_tear_consume_craft",
                Group = "story",
                Mode = "execute",
                Risk = "critical",
                Aliases = new List<string> { "temporal_tear_enter", "wormhole_consume_rocket" },
                Tags = new List<string> { "story", "temporal-tear", "wormhole", "rocket", "destructive", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=temporal_tear action=consume_craft",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["craftId"] = new McpToolParameter { Type = "integer", Description = "目标 Clustercraft InstanceID", Required = false },
                    ["craftName"] = new McpToolParameter { Type = "string", Description = "目标火箭名称；与 craftId 二选一", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true", Required = false },
                    ["confirmDestructive"] = new McpToolParameter { Type = "string", Description = "必须精确填写 consume craft", Required = false }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false) || args["confirmDestructive"]?.ToString() != "consume craft")
                        return CallToolResult.Error("confirm=true and confirmDestructive=\"consume craft\" are required; this destroys the craft, modules, and onboard dupes");
                    var tear = GetTemporalTear();
                    if (tear == null)
                        return CallToolResult.Error("TemporalTear not found");
                    if (!tear.IsOpen())
                        return CallToolResult.Error("TemporalTear is closed");
                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("craftId or craftName must match a Clustercraft at the TemporalTear location");
                    if (craft.Location != tear.Location)
                        return CallToolResult.Error("Clustercraft is not at the TemporalTear location");
                    if (craft.IsFlightInProgress())
                        return CallToolResult.Error("Clustercraft is still in flight and cannot be consumed");

                    var before = TemporalTearInfo(tear);
                    var craftBefore = ClustercraftInfo(craft);
                    tear.ConsumeCraft(craft);
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["action"] = "consume_craft",
                        ["before"] = before,
                        ["craft"] = craftBefore,
                        ["tear"] = TemporalTearInfo(tear)
                    });
                }
            };
        }

        public static McpTool ListProcessConditions()
        {
            return new McpTool
            {
                Name = "process_conditions_list",
                Group = "diagnostics",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "condition_list_side_screen", "rocket_process_conditions" },
                Tags = new List<string> { "conditions", "side-screen", "rocket", "diagnostics", "process" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=process_conditions action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑/火箭 InstanceID；省略时列出所有带条件对象", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false },
                    ["conditionType"] = new McpToolParameter { Type = "string", Description = "All、RocketFlight、RocketPrep、RocketStorage 或 RocketBoard，默认 All", Required = false, EnumValues = new List<string> { "All", "RocketFlight", "RocketPrep", "RocketStorage", "RocketBoard" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、条件文本或状态筛选", Required = false },
                    ["showHidden"] = new McpToolParameter { Type = "boolean", Description = "是否包含 ShowInUI=false 的条件，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回对象数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var type = ParseConditionType(args["conditionType"]?.ToString());
                    bool showHidden = ToolUtil.GetBool(args, "showHidden", false);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var target = FindConditionTarget(args);
                    IEnumerable<GameObject> targets = target == null ? ConditionTargets() : new[] { target };
                    var items = targets
                        .Select(go => ProcessConditionSetInfo(go, type, showHidden))
                        .Where(info => info != null)
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["conditionType"] = type.ToString(),
                        ["targets"] = items
                    });
                }
            };
        }

    }
}
