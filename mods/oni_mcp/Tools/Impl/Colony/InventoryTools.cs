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
    public static partial class InventoryTools
    {
        public static McpTool ControlResources()
        {
            return new McpTool
            {
                Name = "resources_control",
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "inventory_control", "resource_management_control" },
                Tags = new List<string> { "resources", "inventory", "food", "items", "search", "pin", "notification" },
                Description = "资源统一入口：action=inventory/food/search_items/pins/set_pin。set_pin 写入需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "inventory、food、search_items、pins 或 set_pin", Required = true, EnumValues = new List<string> { "inventory", "food", "search_items", "pins", "set_pin" } },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "inventory/search_items/set_pin 时按资源、物品名、prefabId、元素或 tag 过滤/指定资源", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "search_items/pins 时按物品名、prefabId、元素、tag 或资源名模糊搜索", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "按世界 ID 过滤，留空返回全部世界或当前激活世界", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "inventory/search_items 时是否包含已储存在容器/复制人身上的物品", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "search_items 时仅返回地上散落物；等价于 includeStored=false", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只统计已揭示格子内资源，默认 true；调试可传 false", Required = false },
                    ["includeUnpinned"] = new McpToolParameter { Type = "boolean", Description = "pins 时是否包含未固定且未通知的已发现资源，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量；各 action 使用原工具默认值和上限", Required = false },
                    ["pinned"] = new McpToolParameter { Type = "boolean", Description = "set_pin 时是否固定在资源面板；不传则不修改", Required = false },
                    ["notify"] = new McpToolParameter { Type = "boolean", Description = "set_pin 时是否启用资源通知；不传则不修改", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "set_pin 必须为 true，确认修改资源面板开关", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "inventory":
                        case "food":
                        case "search_items":
                            return ReadResourcesControl().Handler(args);
                        case "pins":
                        case "pin_list":
                            {
                                var forwarded = args != null ? (JObject)args.DeepClone() : new JObject();
                                forwarded["action"] = "list";
                                return ControlResourcePin().Handler(forwarded);
                            }
                        case "set_pin":
                        case "pin_set":
                            {
                                var forwarded = args != null ? (JObject)args.DeepClone() : new JObject();
                                forwarded["action"] = "set";
                                return ControlResourcePin().Handler(forwarded);
                            }
                        default:
                            return CallToolResult.Error("action must be inventory, food, search_items, pins, or set_pin");
                    }
                }
            };
        }

        public static McpTool ReadResourcesControl()
        {
            return new McpTool
            {
                Name = "resources_read_control",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Tags = new List<string> { "resources", "inventory", "food", "items", "search", "资源", "库存", "食物", "搜索" },
                Description = "资源读取聚合入口：action=inventory 读资源库存；action=food 读食物库存；action=search_items 搜索地图可拾取物品",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "inventory、food 或 search_items", Required = true, EnumValues = new List<string> { "inventory", "food", "search_items" } },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "action=inventory/search_items 时按资源、物品名、prefabId 或元素过滤", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=search_items 时按物品名、prefabId、元素或 tag 模糊搜索", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "按世界 ID 过滤，留空返回全部世界", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "action=inventory/search_items 时是否包含已储存在容器/复制人身上的物品", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "action=search_items 时仅返回地上散落物；等价于 includeStored=false", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已揭示格子内资源，默认 true；调试可传 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量；各 action 使用原工具默认值和上限", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "inventory")
                        return GetInventory().Handler(args);
                    if (action == "food")
                        return GetFoodInventory().Handler(args);
                    if (action == "search_items")
                    {
                        var forwarded = args != null ? (JObject)args.DeepClone() : new JObject();
                        forwarded.Remove("action");
                        return SearchItems().Handler(forwarded);
                    }

                    return CallToolResult.Error("action must be inventory, food, or search_items");
                }
            };
        }

        public static McpTool GetInventory()
        {
            return new McpTool
            {
                Name = "resources_inventory",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_inventory" },
                Description = "兼容旧工具：请改用 read_control domain=resources action=inventory",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["resource"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "按资源、物品名或 prefabId 过滤，留空返回全部",
                        Required = false
                    },
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "按世界 ID 过滤，留空返回全部世界",
                        Required = false
                    },
                    ["includeStored"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否包含已储存在容器/复制人身上的物品，默认 true",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少组资源，默认 100，最大 500",
                        Required = false
                    },
                    ["visibleOnly"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否只统计已揭示格子内资源，默认 true；调试可传 false",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filter = (args["resource"] ?? args["query"])?.ToString()?.ToLowerInvariant();
                    int? worldId = TryGetInt(args, "worldId");
                    bool includeStored = TryGetBool(args, "includeStored", true);
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 100, 500);

                    var groups = new Dictionary<string, InventoryAggregate>();
                    int scanned = 0;

                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.gameObject == null) continue;
                        scanned++;

                        var primary = pickupable.PrimaryElement;
                        if (primary == null) continue;

                        bool stored = pickupable.storage != null || pickupable.KPrefabID.HasTag(GameTags.Stored);
                        if (!includeStored && stored) continue;

                        int cell = ToolUtil.PickupableCell(pickupable);
                        if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                            continue;
                        int itemWorldId = Grid.IsValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId();
                        if (worldId.HasValue && itemWorldId != worldId.Value) continue;

                        string prefabId = pickupable.KPrefabID?.PrefabTag.Name ?? pickupable.name;
                        string name = ToolUtil.CleanName(pickupable.GetProperName());
                        string elementId = primary.ElementID.ToString();

                        if (!string.IsNullOrEmpty(filter) &&
                            !prefabId.ToLowerInvariant().Contains(filter) &&
                            !name.ToLowerInvariant().Contains(filter) &&
                            !elementId.ToLowerInvariant().Contains(filter))
                            continue;

                        string key = prefabId + "|" + elementId;
                        InventoryAggregate aggregate;
                        if (!groups.TryGetValue(key, out aggregate))
                        {
                            aggregate = new InventoryAggregate
                            {
                                Name = name,
                                PrefabId = prefabId,
                                ElementId = elementId,
                                WorldIds = new HashSet<int>()
                            };
                            groups[key] = aggregate;
                        }

                        float mass = SafeFloat(primary.Mass);
                        aggregate.Count++;
                        aggregate.TotalMassKg += mass;
                        aggregate.TotalUnits += SafeFloat(primary.Units);
                        aggregate.StoredCount += stored ? 1 : 0;
                        aggregate.LooseCount += stored ? 0 : 1;
                        aggregate.WorldIds.Add(itemWorldId);

                        var edible = pickupable.GetComponent<Edible>();
                        if (edible != null)
                            aggregate.TotalCaloriesKcal += SafeFloat(edible.Calories) / 1000f;

                        if (Grid.IsValidCell(cell))
                        {
                            aggregate.SampleCell = cell;
                            aggregate.SampleX = Grid.CellColumn(cell);
                            aggregate.SampleY = Grid.CellRow(cell);
                        }
                    }

                    var items = groups.Values
                        .OrderByDescending(item => item.TotalMassKg)
                        .Take(limit)
                        .Select(item => item.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["scannedPickupables"] = scanned,
                        ["visibleOnly"] = visibleOnly,
                        ["totalGroups"] = groups.Count,
                        ["returned"] = items.Count,
                        ["items"] = items
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetFoodInventory()
        {
            return new McpTool
            {
                Name = "resources_food",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_food_inventory" },
                Description = "兼容旧工具：请改用 read_control domain=resources action=food",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "按世界 ID 过滤，留空返回全部世界",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种食物，默认 100，最大 500",
                        Required = false
                    },
                    ["visibleOnly"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否只统计已揭示格子内食物，默认 true；调试可传 false",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int? worldId = TryGetInt(args, "worldId");
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 100, 500);
                    var groups = new Dictionary<string, FoodAggregate>();
                    float totalCaloriesKcal = 0f;

                    foreach (var edible in Components.Edibles.Items)
                    {
                        if (edible == null || edible.gameObject == null) continue;

                        var pickupable = edible.GetComponent<Pickupable>();
                        var primary = edible.GetComponent<PrimaryElement>();
                        int cell = pickupable != null ? ToolUtil.PickupableCell(pickupable) : Grid.PosToCell(edible);
                        if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                            continue;
                        int itemWorldId = Grid.IsValidCell(cell) ? Grid.WorldIdx[cell] : edible.GetMyWorldId();
                        if (worldId.HasValue && itemWorldId != worldId.Value) continue;

                        string prefabId = edible.GetComponent<KPrefabID>()?.PrefabTag.Name ?? edible.name;
                        string name = ToolUtil.CleanName(edible.GetProperName());
                        string key = prefabId;

                        FoodAggregate aggregate;
                        if (!groups.TryGetValue(key, out aggregate))
                        {
                            aggregate = new FoodAggregate
                            {
                                Name = name,
                                PrefabId = prefabId,
                                Quality = edible.GetQuality(),
                                Morale = edible.GetMorale(),
                                WorldIds = new HashSet<int>()
                            };
                            groups[key] = aggregate;
                        }

                        float caloriesKcal = SafeFloat(edible.Calories) / 1000f;
                        totalCaloriesKcal += caloriesKcal;
                        aggregate.Count++;
                        aggregate.TotalCaloriesKcal += caloriesKcal;
                        aggregate.TotalMassKg += primary != null ? SafeFloat(primary.Mass) : 0f;
                        aggregate.StoredCount += pickupable != null && pickupable.storage != null ? 1 : 0;
                        aggregate.WorldIds.Add(itemWorldId);
                    }

                    var foods = groups.Values
                        .OrderByDescending(food => food.TotalCaloriesKcal)
                        .Take(limit)
                        .Select(food => food.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["totalCaloriesKcal"] = Math.Round(totalCaloriesKcal, 1),
                        ["visibleOnly"] = visibleOnly,
                        ["foodTypes"] = groups.Count,
                        ["returned"] = foods.Count,
                        ["foods"] = foods
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SearchItems()
        {
            return new McpTool
            {
                Name = "items_search",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "find_items", "resources_find_items", "pickupables_search", "map_items_search" },
                Tags = new List<string> { "items", "pickupables", "resources", "search", "map", "物品", "搜索", "全图" },
                Description = "兼容旧工具：请改用 read_control domain=resources action=search_items",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按物品名、prefabId、元素或 tag 模糊搜索；留空返回全部", Required = false },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "query 的别名，适合搜索 Dirt、Water、CopperOre 等资源 tag", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "按世界 ID 过滤；留空搜索全部世界", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "是否包含储物箱/复制人/建筑内物品，默认 true", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "仅返回地上散落物；等价于 includeStored=false", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已揭示格子内物品，默认 true；调试可传 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回实例数，默认 120，最大 1000", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string query = (args["query"] ?? args["resource"])?.ToString();
                    int? worldId = TryGetInt(args, "worldId");
                    bool looseOnly = TryGetBool(args, "looseOnly", false);
                    bool includeStored = !looseOnly && TryGetBool(args, "includeStored", true);
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 120, 1000);

                    int scanned = 0;
                    int matched = 0;
                    var results = new List<Dictionary<string, object>>();

                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.gameObject == null)
                            continue;
                        scanned++;

                        bool stored = pickupable.storage != null || (pickupable.KPrefabID != null && pickupable.KPrefabID.HasTag(GameTags.Stored));
                        if (!includeStored && stored)
                            continue;
                        int cell = ToolUtil.PickupableCell(pickupable);
                        if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                            continue;

                        var info = ItemSearchInfo(pickupable);
                        int itemWorldId = info.ContainsKey("worldId") && info["worldId"] != null ? Convert.ToInt32(info["worldId"]) : pickupable.GetMyWorldId();
                        if (worldId.HasValue && itemWorldId != worldId.Value)
                            continue;

                        if (!ItemMatches(info, query))
                            continue;

                        matched++;
                        if (results.Count < limit)
                            results.Add(info);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["worldId"] = worldId.HasValue ? (object)worldId.Value : null,
                        ["includeStored"] = includeStored,
                        ["visibleOnly"] = visibleOnly,
                        ["scannedPickupables"] = scanned,
                        ["matched"] = matched,
                        ["returned"] = results.Count,
                        ["truncated"] = Math.Max(0, matched - results.Count),
                        ["items"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static int? TryGetInt(JObject args, string key)
        {
            int value;
            return args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : (int?)null;
        }

        private static bool TryGetBool(JObject args, string key, bool defaultValue)
        {
            bool value;
            return args[key] != null && bool.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        private static int ClampLimit(JObject args, int defaultValue, int max)
        {
            int value;
            if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out value))
                return Math.Max(1, Math.Min(value, max));
            return defaultValue;
        }

        private static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static WorldInventory GetWorldInventory(JObject args, out int worldId, out string error)
        {
            worldId = -1;
            error = null;
            if (ClusterManager.Instance == null)
            {
                error = "ClusterManager not initialized";
                return null;
            }

            worldId = TryGetInt(args, "worldId") ?? ClusterManager.Instance.activeWorldId;
            var world = ClusterManager.Instance.GetWorld(worldId);
            if (world == null || world.worldInventory == null)
            {
                error = $"World inventory not found: {worldId}";
                return null;
            }

            return world.worldInventory;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(query) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }


    }
}
