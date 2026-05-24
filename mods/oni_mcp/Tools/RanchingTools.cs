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
    public static class RanchingTools
    {
        private const int MaxAreaCells = 1000;

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
                Description = "列出地图上的小动物/可抓捕对象状态：是否可抓捕、已标记抓捕、是否已捆绑、位置和物种 tag",
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
                Aliases = new List<string> { "ranching_dropoffs_list", "critter_dropoffs" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity" },
                Description = "列出小动物投放点/鱼类投放点的过滤器、容量和当前计数",
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

        public static McpTool ListIncubators()
        {
            return new McpTool
            {
                Name = "incubators_list",
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "egg_incubators_list", "ranching_incubators_list" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle" },
                Description = "列出孵化器的蛋请求、占用蛋/幼体、进度和连续孵化设置",
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

        public static McpTool ConfigureIncubator()
        {
            return new McpTool
            {
                Name = "incubator_configure",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "egg_incubator_set", "ranching_incubator_configure" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle" },
                Description = "配置孵化器蛋请求、连续孵化和移除占用对象，对应孵化器侧屏",
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
                Aliases = new List<string> { "ranching_dropoff_set", "critter_dropoff_filter_set" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity" },
                Description = "配置小动物投放点/鱼类投放点的物种过滤器和容量上限，对应投放点侧屏",
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
                Aliases = new List<string> { "ranching_dropoffs_batch_set", "critter_dropoffs_batch_configure" },
                Tags = new List<string> { "ranching", "critters", "dropoff", "filters", "capacity", "batch" },
                Description = "按区域批量配置小动物/鱼类投放点容量和物种过滤器",
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
                Aliases = new List<string> { "egg_incubators_batch_set", "ranching_incubators_batch_configure" },
                Tags = new List<string> { "ranching", "eggs", "incubator", "receptacle", "batch" },
                Description = "按区域批量配置孵化器蛋请求、连续孵化或取消请求",
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

        private static CreatureDeliveryPoint FindDropOff(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                var point = go == null ? null : go.GetComponent<CreatureDeliveryPoint>();
                if (point == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return point;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return point;
            }
            return null;
        }

        private static string ApplyDropOffConfig(CreatureDeliveryPoint point, JObject args)
        {
            int? capacity = ToolUtil.GetInt(args, "capacity");
            if (capacity.HasValue)
            {
                if (capacity.Value == 0 && !ToolUtil.GetBool(args, "confirm", false))
                    return "confirm=true is required when setting capacity to 0";
                var tracker = point.GetComponent<BaggableCritterCapacityTracker>();
                if (tracker == null)
                    return "Target has no BaggableCritterCapacityTracker";
                float clamped = Mathf.Clamp(capacity.Value, 0, tracker.maximumCreatures);
                ((IUserControlledCapacity)tracker).UserMaxCapacity = clamped;
                tracker.RefreshCreatureCount();
            }

            string mode = (args["mode"]?.ToString() ?? "replace").Trim().ToLowerInvariant();
            var filter = point.GetComponent<TreeFilterable>();
            if (filter != null && (args["critterTags"] != null || mode == "clear"))
            {
                var tags = ParseTags(args["critterTags"]);
                if (mode == "clear")
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return "confirm=true is required when clearing critter filters";
                    filter.UpdateFilters(new HashSet<Tag>());
                }
                else
                {
                    var current = new HashSet<Tag>(filter.GetTags());
                    if (mode == "add")
                        current.UnionWith(tags);
                    else if (mode == "remove")
                        current.ExceptWith(tags);
                    else if (mode == "replace")
                        current = tags;
                    else
                        return "mode must be replace, add, remove, or clear";
                    filter.UpdateFilters(current);
                }
            }

            point.critterCapacity.RefreshCreatureCount();
            return null;
        }

        private static EggIncubator FindIncubator(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                var incubator = go == null ? null : go.GetComponent<EggIncubator>();
                if (incubator == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return incubator;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return incubator;
            }
            return null;
        }

        private static Dictionary<string, object> CritterInfo(Capturable capturable)
        {
            var go = capturable.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var baggable = go.GetComponent<Baggable>();
            var pickupable = go.GetComponent<Pickupable>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["isCapturable"] = capturable.IsCapturable(),
                ["markedForCapture"] = capturable.IsMarkedForCapture,
                ["wrangled"] = baggable?.wrangled ?? false,
                ["bagged"] = go.HasTag(GameTags.Creatures.Bagged),
                ["stored"] = go.HasTag(GameTags.Stored),
                ["storageId"] = pickupable?.storage?.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static Dictionary<string, object> DropOffInfo(CreatureDeliveryPoint point)
        {
            var go = point.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var filter = go.GetComponent<TreeFilterable>();
            var tracker = go.GetComponent<BaggableCritterCapacityTracker>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["logicEnabled"] = point.LogicEnabled(),
                ["capacity"] = tracker?.creatureLimit ?? 0,
                ["maximumCreatures"] = tracker?.maximumCreatures ?? 0,
                ["storedCreatureCount"] = tracker?.storedCreatureCount ?? 0,
                ["acceptedTags"] = filter == null ? new List<string>() : filter.GetTags().Select(tag => tag.Name).OrderBy(name => name).ToList()
            };
        }

        private static Dictionary<string, object> IncubatorInfo(EggIncubator incubator)
        {
            var go = incubator.gameObject;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["autoReplace"] = incubator.autoReplaceEntity,
                ["requestedEgg"] = incubator.requestedEntityTag.IsValid ? incubator.requestedEntityTag.Name : null,
                ["hasActiveRequest"] = incubator.GetActiveRequest != null,
                ["progress"] = Math.Round(incubator.GetProgress(), 3),
                ["occupant"] = incubator.Occupant == null ? null : ObjectInfo(incubator.Occupant),
                ["acceptedEggTags"] = incubator.possibleDepositObjectTags.Select(tag => tag.Name).OrderBy(name => name).ToList()
            };
        }

        private static Dictionary<string, object> ObjectInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static HashSet<Tag> ParseTags(JToken token)
        {
            var tags = new HashSet<Tag>();
            if (token == null)
                return tags;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in token)
                {
                    string value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        tags.Add(TagManager.Create(value.Trim()));
                }
            }
            else
            {
                foreach (string value in token.ToString().Split(new[] { ',', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    tags.Add(TagManager.Create(value.Trim()));
            }
            return tags;
        }

        private static bool Matches(CreatureDeliveryPoint point, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = point.gameObject;
            var filter = go.GetComponent<TreeFilterable>();
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q)
                || (filter != null && filter.GetTags().Any(tag => Contains(tag.Name, q)));
        }

        private static bool CritterMatches(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q);
        }

        private static bool IncubatorMatches(EggIncubator incubator, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = incubator.gameObject;
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q)
                || Contains(incubator.requestedEntityTag.Name, q)
                || Contains(incubator.Occupant?.GetProperName(), q)
                || Contains(incubator.Occupant?.GetComponent<KPrefabID>()?.PrefabTag.Name, q);
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static string TargetName(GameObject go)
        {
            return ToolUtil.CleanName(go.GetProperName());
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
