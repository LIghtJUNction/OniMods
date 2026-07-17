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
    public static partial class RanchingTools
    {
        private const int MaxAreaCells = 1000;

        public static McpTool ControlRanching()
        {
            return new McpTool
            {
                Name = "ranching_control",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "critters_control", "creature_ranching_control" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "incubator", "eggs", "batch" },
                Description = "畜牧统一入口。kind=critters/dropoff/incubator；action 透传到对应旧 control。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "畜牧对象类型：critters、dropoff、incubator", Required = true, EnumValues = new List<string> { "critters", "dropoff", "incubator" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "critters 使用 critters；dropoff/incubator 支持 list/configure/batch", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId、过滤 tag、请求蛋或占用对象筛选", Required = false },
                    ["capturableOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=critters 时是否只返回当前可抓捕对象，默认 false", Required = false },
                    ["wrangledOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=critters 时是否只返回已捆绑对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量，默认 100，最大 500", Required = false },
                    ["capacity"] = new McpToolParameter { Type = "integer", Description = "kind=dropoff 时目标容量；留空不修改", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "kind=dropoff 的过滤修改模式：replace、add、remove、clear，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove", "clear" } },
                    ["critterTags"] = new McpToolParameter { Type = "array", Description = "kind=dropoff 的小动物 prefab/tag 列表，例如 Hatch、Drecko；mode=clear 可留空", Required = false },
                    ["incubatorAction"] = new McpToolParameter { Type = "string", Description = "kind=incubator 时 set、cancel、remove_occupant，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel", "remove_occupant" } },
                    ["eggTag"] = new McpToolParameter { Type = "string", Description = "kind=incubator 且 incubatorAction=set 时的蛋 prefab/tag", Required = false },
                    ["autoReplace"] = new McpToolParameter { Type = "boolean", Description = "kind=incubator 时是否连续孵化；留空不修改", Required = false },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=incubator batch + set 时只处理空孵化器，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层批量、清空过滤器、容量设为 0 或 remove_occupant 需要 true", Required = false }
                })),
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "critters":
                        case "critter":
                            {
                                var forwarded = args != null ? (JObject)args.DeepClone() : new JObject();
                                forwarded["action"] = "critters";
                                return ReadRanchingControl().Handler(forwarded);
                            }
                        case "dropoff":
                        case "dropoffs":
                            return ControlDropOff().Handler(args);
                        case "incubator":
                        case "incubators":
                            return ControlIncubator().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be critters, dropoff, or incubator");
                    }
                }
            };
        }

        public static McpTool ReadRanchingControl()
        {
            return new McpTool
            {
                Name = "ranching_read_control",
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "ranching_control", "critters_read_control" },
                Tags = new List<string> { "ranching", "critters", "creatures", "capture", "wrangle" },
                Description = "畜牧只读聚合工具：action=critters 列出地图上的小动物/可抓捕对象状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "critters", Required = true, EnumValues = new List<string> { "critters" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=critters 时按名称或 prefabId 筛选", Required = false },
                    ["capturableOnly"] = new McpToolParameter { Type = "boolean", Description = "action=critters 时是否只返回当前可抓捕对象，默认 false", Required = false },
                    ["wrangledOnly"] = new McpToolParameter { Type = "boolean", Description = "action=critters 时是否只返回已捆绑对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=critters 时最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "critters")
                        return ListCritters().Handler(args);
                    return CallToolResult.Error("action must be critters");
                }
            };
        }

        public static McpTool ListCritters()
        {
            return new McpTool
            {
                Name = "critters_list",
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "ranching_critters_list", "creatures_list" },
                Tags = new List<string> { "ranching", "critters", "creatures", "capture", "wrangle" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=bio bioDomain=ranching action=critters",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 筛选", Required = false },
                    ["capturableOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回当前可抓捕对象，默认 false", Required = false },
                    ["wrangledOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已捆绑对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool capturableOnly = ToolUtil.GetBool(args, "capturableOnly", false);
                    bool wrangledOnly = ToolUtil.GetBool(args, "wrangledOnly", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var critters = Components.Capturables.Items
                        .Where(item => item != null && item.gameObject != null)
                        .Where(item => ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId))
                        .Where(item => rect == null || CellInRect(Grid.PosToCell(item.gameObject), rect, worldId))
                        .Where(item => !capturableOnly || item.IsCapturable())
                        .Where(item => !wrangledOnly || (item.GetComponent<Baggable>()?.wrangled ?? false))
                        .Where(item => CritterMatches(item.gameObject, query))
                        .OrderBy(item => TargetName(item.gameObject))
                        .Take(limit)
                        .Select(CritterInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = critters.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["critters"] = critters
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListDropOffs()
        {
            return new McpTool
            {
                Name = "critters_dropoff_list",
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "ranching_dropoffs_list", "critter_dropoffs" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity" },
                Description = "兼容入口：列出小动物投放点/鱼类投放点。新调用请使用 colony_control domain=bio bioDomain=ranching kind=dropoff action=list。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId 或过滤 tag 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var items = Components.BuildingCompletes.Items
                        .Select(building => building?.GetComponent<CreatureDeliveryPoint>())
                        .Where(point => point != null && ToolUtil.GameObjectMatchesWorld(point.gameObject, worldId))
                        .Where(point => rect == null || CellInRect(Grid.PosToCell(point.gameObject), rect, worldId))
                        .Where(point => Matches(point, query))
                        .OrderBy(point => TargetName(point.gameObject))
                        .Take(limit)
                        .Select(DropOffInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["dropOffs"] = items
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlDropOff()
        {
            return new McpTool
            {
                Name = "critters_dropoff_control",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "critter_dropoff_control", "ranching_dropoff_control" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity", "batch" },
                Description = "统一读取/配置小动物投放点或鱼类投放点。action=list/configure/batch；configure 按 id/x/y 单点修改，batch 按 areaId 或矩形批量修改。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、configure、batch，默认 list", Required = false, EnumValues = new List<string> { "list", "configure", "batch" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId 或过滤 tag 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量，默认 100，最大 500", Required = false },
                    ["capacity"] = new McpToolParameter { Type = "integer", Description = "目标容量；留空不修改", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "过滤修改模式：replace、add、remove、clear，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove", "clear" } },
                    ["critterTags"] = new McpToolParameter { Type = "array", Description = "小动物 prefab/tag 列表，例如 Hatch、Drecko；mode=clear 可留空", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "batch、清空过滤器或容量设为 0 时必须为 true", Required = false }
                })),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListDropOffs().Handler(args);
                    if (action == "configure" || action == "set")
                        return ConfigureDropOff().Handler(args);
                    if (action == "batch")
                        return BatchConfigureDropOffs().Handler(args);
                    return CallToolResult.Error("action must be list, configure, or batch");
                }
            };
        }

        public static McpTool ListIncubators()
        {
            return new McpTool
            {
                Name = "incubators_list",
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "egg_incubators_list", "ranching_incubators_list" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle" },
                Description = "兼容入口：列出孵化器的蛋请求、占用蛋/幼体、进度和连续孵化设置。新调用请使用 colony_control domain=bio bioDomain=ranching kind=incubator action=list。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、请求蛋或当前占用对象筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var incubators = Components.BuildingCompletes.Items
                        .Select(building => building?.GetComponent<EggIncubator>())
                        .Where(incubator => incubator != null && ToolUtil.GameObjectMatchesWorld(incubator.gameObject, worldId))
                        .Where(incubator => rect == null || CellInRect(Grid.PosToCell(incubator.gameObject), rect, worldId))
                        .Where(incubator => IncubatorMatches(incubator, query))
                        .OrderBy(incubator => TargetName(incubator.gameObject))
                        .Take(limit)
                        .Select(IncubatorInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = incubators.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["incubators"] = incubators
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlIncubator()
        {
            return new McpTool
            {
                Name = "incubator_control",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "incubators_control", "egg_incubator_control", "ranching_incubator_control" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle", "batch" },
                Description = "统一读取/配置孵化器。action=list/configure/batch；incubatorAction=set/cancel/remove_occupant 控制蛋请求动作。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、configure、batch，默认 list", Required = false, EnumValues = new List<string> { "list", "configure", "batch" } },
                    ["incubatorAction"] = new McpToolParameter { Type = "string", Description = "set、cancel、remove_occupant，默认 set；旧单独工具仍使用 action 表示该值", Required = false, EnumValues = new List<string> { "set", "cancel", "remove_occupant" } },
                    ["eggTag"] = new McpToolParameter { Type = "string", Description = "蛋 prefab/tag；incubatorAction=set 时必填", Required = false },
                    ["autoReplace"] = new McpToolParameter { Type = "boolean", Description = "是否连续孵化；留空不修改", Required = false },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "batch + set 时只处理空孵化器，默认 true", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按孵化器、请求蛋或当前占用对象筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "batch 或 remove_occupant 必须为 true", Required = false }
                })),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListIncubators().Handler(args);
                    if (action == "configure" || action == "set" || action == "cancel" || action == "remove_occupant")
                        return ConfigureIncubator().Handler(IncubatorDispatchArgs(args, action));
                    if (action == "batch")
                        return BatchConfigureIncubators().Handler(IncubatorDispatchArgs(args, action));
                    return CallToolResult.Error("action must be list, configure, or batch");
                }
            };
        }

    }
}
