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
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "storage_list" },
                Description = "列出储物箱/储液库/储气库等储存建筑，包含容量、已存质量和过滤器摘要",
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
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "storage_detail" },
                Description = "读取单个储存建筑的过滤标签、库存物品和容量信息",
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
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Description = "设置储存建筑的资源过滤标签；默认替换当前过滤器，也可 add/remove",
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

        private static Dictionary<string, McpToolParameter> StorageLookupParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "建筑所在格子 Y", Required = false }
            };
        }

        private static StorageInfo FindStorage(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;

            return GetStorageBuildings().FirstOrDefault(item =>
                (id.HasValue && item.Id == id.Value) ||
                (cell.HasValue && item.Cell == cell.Value));
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
