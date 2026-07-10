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
        public static McpTool ConfigureIncubator()
        {
            return new McpTool
            {
                Name = "incubator_configure",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "egg_incubator_set", "ranching_incubator_configure" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle" },
                Description = "兼容入口：配置孵化器蛋请求、连续孵化和移除占用对象。新调用请使用 colony_control domain=bio bioDomain=ranching kind=incubator action=configure。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["eggTag"] = new McpToolParameter { Type = "string", Description = "蛋 prefab/tag；action=set 时必填", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "set、cancel、remove_occupant，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel", "remove_occupant" } },
                    ["autoReplace"] = new McpToolParameter { Type = "boolean", Description = "是否连续孵化；留空不修改", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "remove_occupant 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var incubator = FindIncubator(args);
                    if (incubator == null)
                        return CallToolResult.Error("EggIncubator target not found");

                    if (args["autoReplace"] != null)
                        incubator.autoReplaceEntity = ToolUtil.GetBool(args, "autoReplace", false);

                    string action = (args["action"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    if (action == "cancel")
                    {
                        incubator.CancelActiveRequest();
                    }
                    else if (action == "remove_occupant")
                    {
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required for remove_occupant");
                        incubator.OrderRemoveOccupant();
                    }
                    else
                    {
                        string eggName = args["eggTag"]?.ToString();
                        if (string.IsNullOrWhiteSpace(eggName))
                            return CallToolResult.Error("eggTag is required for action=set");
                        var eggTag = TagManager.Create(eggName.Trim());
                        var eggPrefab = Assets.GetPrefab(eggTag);
                        if (eggPrefab == null || !eggPrefab.HasTag(GameTags.Egg))
                            return CallToolResult.Error("eggTag is not an egg prefab");
                        if (!incubator.HasDepositTag(eggTag) || !incubator.IsValidEntity(eggPrefab))
                            return CallToolResult.Error("Egg is not valid for this incubator");
                        incubator.CancelActiveRequest();
                        incubator.CreateOrder(eggTag, Tag.Invalid);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(IncubatorInfo(incubator), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ConfigureDropOff()
        {
            return new McpTool
            {
                Name = "critters_dropoff_configure",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "ranching_dropoff_set", "critter_dropoff_filter_set" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity" },
                Description = "兼容入口：配置小动物投放点/鱼类投放点的物种过滤器和容量上限。新调用请使用 colony_control domain=bio bioDomain=ranching kind=dropoff action=configure。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["capacity"] = new McpToolParameter { Type = "integer", Description = "目标容量；留空不修改", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "过滤修改模式：replace、add、remove、clear，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove", "clear" } },
                    ["critterTags"] = new McpToolParameter { Type = "array", Description = "小动物 prefab/tag 列表，例如 Hatch、Drecko；mode=clear 可留空", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "清空过滤器或容量设为 0 时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var point = FindDropOff(args);
                    if (point == null)
                        return CallToolResult.Error("CreatureDeliveryPoint target not found");

                    var error = ApplyDropOffConfig(point, args);
                    if (error != null)
                        return CallToolResult.Error(error);
                    return CallToolResult.Text(JsonConvert.SerializeObject(DropOffInfo(point), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool BatchConfigureDropOffs()
        {
            return new McpTool
            {
                Name = "critters_dropoff_batch_configure",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "ranching_dropoffs_batch_set", "critter_dropoffs_batch_configure" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity", "batch" },
                Description = "兼容入口：按区域批量配置小动物/鱼类投放点容量和物种过滤器。新调用请使用 colony_control domain=bio bioDomain=ranching kind=dropoff action=batch。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["capacity"] = new McpToolParameter { Type = "integer", Description = "目标容量；留空不修改", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "过滤修改模式：replace、add、remove、clear，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove", "clear" } },
                    ["critterTags"] = new McpToolParameter { Type = "array", Description = "小动物 prefab/tag 列表，例如 Hatch、Drecko；mode=clear 可留空", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按投放点名称、prefabId 或过滤 tag 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true；清空过滤器或容量设为 0 时也必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to batch configure critter drop-offs");

                    var rect = ToolUtil.GetRect(args);
                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    var changed = new List<Dictionary<string, object>>();
                    foreach (var point in Components.BuildingCompletes.Items
                                 .Select(building => building?.GetComponent<CreatureDeliveryPoint>())
                                 .Where(point => point != null && ToolUtil.GameObjectMatchesWorld(point.gameObject, worldId))
                                 .Where(point => CellInRect(Grid.PosToCell(point.gameObject), rect, worldId))
                                 .Where(point => Matches(point, query))
                                 .Take(limit))
                    {
                        var error = ApplyDropOffConfig(point, args);
                        if (error != null)
                            return CallToolResult.Error(error);
                        changed.Add(DropOffInfo(point));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = changed.Count,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["dropOffs"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool BatchConfigureIncubators()
        {
            return new McpTool
            {
                Name = "incubators_batch_configure",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "egg_incubators_batch_set", "ranching_incubators_batch_configure" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle", "batch" },
                Description = "兼容入口：按区域批量配置孵化器蛋请求、连续孵化或取消请求。新调用请使用 colony_control domain=bio bioDomain=ranching kind=incubator action=batch。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["eggTag"] = new McpToolParameter { Type = "string", Description = "蛋 prefab/tag；action=set 时必填", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "set、cancel、remove_occupant，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel", "remove_occupant" } },
                    ["autoReplace"] = new McpToolParameter { Type = "boolean", Description = "是否连续孵化；留空不修改", Required = false },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "action=set 时只处理空孵化器，默认 true", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按孵化器、请求蛋或当前占用对象筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true；remove_occupant 必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to batch configure incubators");

                    string action = (args["action"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    if (action != "set" && action != "cancel" && action != "remove_occupant")
                        return CallToolResult.Error("action must be set, cancel, or remove_occupant");
                    Tag eggTag = Tag.Invalid;
                    GameObject eggPrefab = null;
                    if (action == "set")
                    {
                        string eggName = args["eggTag"]?.ToString();
                        if (string.IsNullOrWhiteSpace(eggName))
                            return CallToolResult.Error("eggTag is required for action=set");
                        eggTag = TagManager.Create(eggName.Trim());
                        eggPrefab = Assets.GetPrefab(eggTag);
                        if (eggPrefab == null || !eggPrefab.HasTag(GameTags.Egg))
                            return CallToolResult.Error("eggTag is not an egg prefab");
                    }

                    var rect = ToolUtil.GetRect(args);
                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool emptyOnly = ToolUtil.GetBool(args, "emptyOnly", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    var changed = new List<Dictionary<string, object>>();
                    foreach (var incubator in Components.BuildingCompletes.Items
                                 .Select(building => building?.GetComponent<EggIncubator>())
                                 .Where(incubator => incubator != null && ToolUtil.GameObjectMatchesWorld(incubator.gameObject, worldId))
                                 .Where(incubator => CellInRect(Grid.PosToCell(incubator.gameObject), rect, worldId))
                                 .Where(incubator => IncubatorMatches(incubator, query))
                                 .Take(limit))
                    {
                        if (args["autoReplace"] != null)
                            incubator.autoReplaceEntity = ToolUtil.GetBool(args, "autoReplace", false);
                        if (action == "cancel")
                        {
                            incubator.CancelActiveRequest();
                        }
                        else if (action == "remove_occupant")
                        {
                            incubator.OrderRemoveOccupant();
                        }
                        else
                        {
                            if (emptyOnly && incubator.Occupant != null)
                                continue;
                            if (!incubator.HasDepositTag(eggTag) || !incubator.IsValidEntity(eggPrefab))
                                continue;
                            incubator.CancelActiveRequest();
                            incubator.CreateOrder(eggTag, Tag.Invalid);
                        }
                        changed.Add(IncubatorInfo(incubator));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["eggTag"] = eggTag.IsValid ? eggTag.Name : null,
                        ["changed"] = changed.Count,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["incubators"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static JObject IncubatorDispatchArgs(JObject args, string controlAction)
        {
            var dispatched = (JObject)args.DeepClone();
            string incubatorAction = args["incubatorAction"]?.ToString();
            if (string.IsNullOrWhiteSpace(incubatorAction))
            {
                incubatorAction = controlAction == "cancel" || controlAction == "remove_occupant"
                    ? controlAction
                    : "set";
            }
            dispatched["action"] = incubatorAction;
            return dispatched;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

    }
}
