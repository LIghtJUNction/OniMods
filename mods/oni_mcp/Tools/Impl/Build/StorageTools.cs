using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class StorageTools
    {
        public static McpTool GetStorageList()
        {
            return new McpTool
            {
                Name = "resources_storage_list",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "storage_list" },
                Description = "兼容入口：请优先使用 building_control domain=storage action=list。列出储物箱/储液库/储气库等储存建筑，包含容量、已存质量和过滤器摘要",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["resource"] = new McpToolParameter { Type = "string", Description = "按储存过滤标签或建筑名筛选", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "按世界 ID 过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个储存建筑，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    string filter = args["resource"]?.ToString()?.ToLowerInvariant();
                    int? worldId = ToolUtil.GetInt(args, "worldId");
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var storages = GetStorageBuildings()
                        .Where(item => !worldId.HasValue || item.WorldId == worldId.Value)
                        .Where(item => string.IsNullOrEmpty(filter) || item.Matches(filter))
                        .Take(limit)
                        .Select(item => item.ToDictionary(includeItems: false))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["returned"] = storages.Count,
                        ["storages"] = storages
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetStorageDetail()
        {
            return new McpTool
            {
                Name = "resources_storage_detail",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "storage_detail" },
                Description = "兼容入口：请优先使用 building_control domain=storage action=detail。读取单个储存建筑的过滤标签、库存物品和容量信息",
                Parameters = StorageLookupParams(),
                Handler = args =>
                {
                    var target = FindStorage(args);
                    if (target == null)
                        return CallToolResult.Error("Storage building not found");
                    return CallToolResult.Text(JsonConvert.SerializeObject(target.ToDictionary(includeItems: true), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetStorageFilter()
        {
            return new McpTool
            {
                Name = "resources_storage_set_filter",
                Hidden = true,
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Description = "兼容入口：请优先使用 building_control domain=storage action=set_filter。设置储存建筑的资源过滤标签；默认替换当前过滤器，也可 add/remove",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "建筑 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 Y", Required = false },
                    ["tags"] = new McpToolParameter { Type = "array", Description = "资源 Tag 列表，如 Dirt、Algae、SandStone", Required = true },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "replace、add、remove，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove" } }
                },
                Handler = args =>
                {
                    var target = FindStorage(args);
                    if (target == null)
                        return CallToolResult.Error("Storage building not found");

                    var filterable = target.GameObject.GetComponent<TreeFilterable>();
                    if (filterable == null)
                        return CallToolResult.Error("Selected building does not expose TreeFilterable");

                    var tags = ParseTags(args["tags"]);
                    if (tags.Count == 0)
                        return CallToolResult.Error("tags must contain at least one resource tag");

                    string mode = (args["mode"]?.ToString() ?? "replace").ToLowerInvariant();
                    var next = new HashSet<Tag>(filterable.GetTags());
                    if (mode == "replace")
                        next.Clear();

                    foreach (var tag in tags)
                    {
                        if (mode == "remove")
                            next.Remove(tag);
                        else
                            next.Add(tag);
                    }

                    filterable.UpdateFilters(next);
                    return CallToolResult.Text(JsonConvert.SerializeObject(target.ToDictionary(includeItems: false), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlStorage()
        {
            return new McpTool
            {
                Name = "storage_control",
                Hidden = true,
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "resources_storage_control", "storage_filter_control" },
                Tags = new List<string> { "resources", "storage", "filter", "inventory" },
                Description = "储存建筑聚合工具：action=list/detail/set_filter；读取储存列表/详情，或修改储存过滤标签。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、detail 或 set_filter", Required = true, EnumValues = new List<string> { "list", "detail", "set_filter" } },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "action=list 时按储存过滤标签或建筑名筛选", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=list 时按世界 ID 过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=detail/set_filter 时的建筑 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=detail/set_filter 时的建筑所在格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=detail/set_filter 时的建筑所在格子 Y", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=detail/set_filter 时按本地化名或 prefabId 查找", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
                    ["tags"] = new McpToolParameter { Type = "array", Description = "action=set_filter 时资源 Tag 列表，如 Dirt、Algae、SandStone", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "action=set_filter 时为 replace、add、remove，默认 replace", Required = false, EnumValues = new List<string> { "replace", "add", "remove" } }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return GetStorageList().Handler(args);
                    if (action == "detail")
                        return GetStorageDetail().Handler(args);
                    if (action == "set_filter" || action == "set")
                        return SetStorageFilter().Handler(args);
                    return CallToolResult.Error("action must be list, detail, or set_filter");
                }
            };
        }

        public static McpTool ControlStorageSystem()
        {
            return new McpTool
            {
                Name = "storage_system_control",
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "storage_controls", "inventory_storage_control" },
                Tags = new List<string> { "resources", "storage", "filter", "inventory", "side-screen", "receptacle" },
                Description = "储存/过滤/实体插槽聚合工具：domain=storage/tile_selection/filter/receptacle，再用 action 参数执行对应子操作。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "storage、tile_selection、filter 或 receptacle", Required = true, EnumValues = new List<string> { "storage", "tile_selection", "filter", "receptacle" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子工具操作；storage=list/detail/set_filter，tile_selection=list/set/batch，filter=list/set，receptacle=list/request/cancel_request/remove_occupant/cancel_remove/batch", Required = false },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "domain=storage action=list 时按储存过滤标签或建筑名筛选", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "domain=tile_selection/filter/receptacle action=list 时按名称、prefabId 或 tag 筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "列表查询时是否返回可选项", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "列表查询最多返回数量", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标或区域 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标或区域 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID", Required = false },
                    ["tag"] = new McpToolParameter { Type = "string", Description = "domain=filter action=set kind=single 时的目标 tag/元素", Required = false },
                    ["tags"] = new McpToolParameter { Type = "array", Description = "domain=storage/filter 写入时的 tag 列表", Required = false },
                    ["itemTag"] = new McpToolParameter { Type = "string", Description = "domain=tile_selection action=set 时的目标物品 tag", Required = false },
                    ["entityTag"] = new McpToolParameter { Type = "string", Description = "domain=receptacle action=request 时的实体 tag", Required = false },
                    ["additionalTag"] = new McpToolParameter { Type = "string", Description = "domain=receptacle action=request 时的附加 tag", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "过滤写入模式：replace、add、remove 或 clear", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "清空单选或 StorageTile 目标", Required = false },
                    ["replaceExistingRequest"] = new McpToolParameter { Type = "boolean", Description = "domain=receptacle action=request 时是否替换现有请求，默认 true", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "批量操作条目", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "批量操作默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作确认；子工具要求时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        string legacyKind = (args["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                        if (legacyKind == "storage" || legacyKind == "building" || legacyKind == "buildings" ||
                            legacyKind == "tile_selection" || legacyKind == "tile" || legacyKind == "storage_tile" || legacyKind == "single_item" ||
                            legacyKind == "filter" || legacyKind == "filters" ||
                            legacyKind == "receptacle" || legacyKind == "receptacles" || legacyKind == "entity_slot" || legacyKind == "entity_slots")
                        {
                            domain = legacyKind;
                        }
                    }

                    switch (domain)
                    {
                        case "storage":
                        case "building":
                        case "buildings":
                            return ControlStorage().Handler(args);
                        case "tile_selection":
                        case "tile":
                        case "storage_tile":
                        case "single_item":
                            return ReceptacleTools.ControlStorageTileSelection().Handler(args);
                        case "filter":
                        case "filters":
                            return FilterTools.ControlFilter().Handler(args);
                        case "receptacle":
                        case "receptacles":
                        case "entity_slot":
                        case "entity_slots":
                            return ReceptacleTools.ControlReceptacle().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be storage, tile_selection, filter, or receptacle");
                    }
                }
            };
        }

        private static Dictionary<string, McpToolParameter> StorageLookupParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 Y", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "按本地化建筑名或 prefabId 查找，如 洗手盆 / WashBasin", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false }
            };
        }

        private static StorageInfo FindStorage(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            string query = args["query"]?.ToString() ?? args["name"]?.ToString() ?? args["resource"]?.ToString();
            string filter = string.IsNullOrWhiteSpace(query) ? null : query.Trim().ToLowerInvariant();

            return GetStorageBuildings().FirstOrDefault(item =>
                (id.HasValue && item.Id == id.Value) ||
                (cell.HasValue && item.Cell == cell.Value) ||
                (!string.IsNullOrEmpty(filter) && item.Matches(filter)));
        }

        private static List<StorageInfo> GetStorageBuildings()
        {
            var result = new List<StorageInfo>();
            var seen = new HashSet<int>();

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null) continue;
                var go = building.gameObject;
                var storage = go.GetComponent<Storage>();
                if (storage == null) continue;
                var id = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID();
                if (!seen.Add(id)) continue;

                var pos = go.transform.GetPosition();
                var cell = Grid.PosToCell(pos);
                var filterable = go.GetComponent<TreeFilterable>();
                result.Add(new StorageInfo
                {
                    Id = id,
                    GameObject = go,
                    Storage = storage,
                    Cell = cell,
                    X = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : Mathf.RoundToInt(pos.x),
                    Y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : Mathf.RoundToInt(pos.y),
                    WorldId = go.GetMyWorldId(),
                    Name = ToolUtil.CleanName(go.GetProperName()),
                    PrefabId = go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name,
                    AcceptedTags = filterable != null ? filterable.GetTags().Select(tag => tag.Name).OrderBy(tag => tag).ToList() : new List<string>()
                });
            }

            return result.OrderBy(item => item.PrefabId).ThenBy(item => item.Id).ToList();
        }

        private static List<Tag> ParseTags(Newtonsoft.Json.Linq.JToken token)
        {
            var tags = new List<Tag>();
            if (token == null)
                return tags;

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                foreach (var item in token)
                    AddTag(tags, item?.ToString());
            }
            else
            {
                foreach (var value in token.ToString().Split(','))
                    AddTag(tags, value);
            }

            return tags;
        }

        private static void AddTag(List<Tag> tags, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            tags.Add(new Tag(value.Trim()));
        }

        private class StorageInfo
        {
            public int Id;
            public GameObject GameObject;
            public Storage Storage;
            public int Cell;
            public int X;
            public int Y;
            public int WorldId;
            public string Name;
            public string PrefabId;
            public List<string> AcceptedTags;

            public bool Matches(string filter)
            {
                return Name.ToLowerInvariant().Contains(filter) ||
                       PrefabId.ToLowerInvariant().Contains(filter) ||
                       AcceptedTags.Any(tag => tag.ToLowerInvariant().Contains(filter));
            }

            public Dictionary<string, object> ToDictionary(bool includeItems)
            {
                var data = new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["position"] = new { x = X, y = Y },
                    ["worldId"] = WorldId,
                    ["capacityKg"] = Math.Round(ToolUtil.SafeFloat(Storage.capacityKg), 3),
                    ["massStoredKg"] = Math.Round(ToolUtil.SafeFloat(Storage.MassStored()), 3),
                    ["acceptedTags"] = AcceptedTags
                };

                var locker = GameObject.GetComponent<StorageLocker>();
                if (locker != null)
                    data["userMaxCapacityKg"] = Math.Round(ToolUtil.SafeFloat(locker.UserMaxCapacity), 3);

                if (includeItems)
                {
                    data["items"] = Storage.items
                        .Where(item => item != null)
                        .Select(item =>
                        {
                            var primary = item.GetComponent<PrimaryElement>();
                            var prefab = item.GetComponent<KPrefabID>();
                            return new Dictionary<string, object>
                            {
                                ["name"] = ToolUtil.CleanName(item.GetProperName()),
                                ["prefabId"] = prefab?.PrefabTag.Name ?? item.name,
                                ["massKg"] = Math.Round(primary != null ? ToolUtil.SafeFloat(primary.Mass) : 0f, 3)
                            };
                        })
                        .ToList();
                }

                return data;
            }
        }
    }
}
