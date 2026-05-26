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
    public static class InventoryTools
    {
        public static McpTool GetInventory()
        {
            return new McpTool
            {
                Name = "resources_inventory",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_inventory" },
                Description = "按资源/物品聚合殖民地可拾取库存，包含质量、数量、储存状态和位置统计",
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
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string filter = args["resource"]?.ToString()?.ToLowerInvariant();
                    int? worldId = TryGetInt(args, "worldId");
                    bool includeStored = TryGetBool(args, "includeStored", true);
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

                        int cell = pickupable.cachedCell;
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
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_food_inventory" },
                Description = "获取食物库存，按食物类型聚合卡路里、质量、品质和储存状态",
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
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int? worldId = TryGetInt(args, "worldId");
                    int limit = ClampLimit(args, 100, 500);
                    var groups = new Dictionary<string, FoodAggregate>();
                    float totalCaloriesKcal = 0f;

                    foreach (var edible in Components.Edibles.Items)
                    {
                        if (edible == null || edible.gameObject == null) continue;

                        var pickupable = edible.GetComponent<Pickupable>();
                        var primary = edible.GetComponent<PrimaryElement>();
                        int cell = pickupable != null ? pickupable.cachedCell : Grid.PosToCell(edible);
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
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "find_items", "resources_find_items", "pickupables_search", "map_items_search" },
                Tags = new List<string> { "items", "pickupables", "resources", "search", "map", "物品", "搜索", "全图" },
                Description = "全地图搜索可拾取物品/资源实例，按名称、prefabId 或元素过滤，返回坐标、质量、储存状态和容器信息",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按物品名、prefabId、元素或 tag 模糊搜索；留空返回全部", Required = false },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "query 的别名，适合搜索 Dirt、Water、CopperOre 等资源 tag", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "按世界 ID 过滤；留空搜索全部世界", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "是否包含储物箱/复制人/建筑内物品，默认 true", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "仅返回地上散落物；等价于 includeStored=false", Required = false },
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
                        ["scannedPickupables"] = scanned,
                        ["matched"] = matched,
                        ["returned"] = results.Count,
                        ["truncated"] = Math.Max(0, matched - results.Count),
                        ["items"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListResourcePins()
        {
            return new McpTool
            {
                Name = "resources_pins_list",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "resources_pins", "resources_notifications_list", "resource_pins_list" },
                Tags = new List<string> { "resources", "inventory", "pin", "notification", "allresources" },
                Description = "读取 AllResourcesScreen 资源行的固定显示和通知开关状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按资源 tag 或名称过滤", Required = false },
                    ["includeUnpinned"] = new McpToolParameter { Type = "boolean", Description = "是否包含未固定且未通知的已发现资源，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    var inventory = GetWorldInventory(args, out var worldId, out var error);
                    if (inventory == null)
                        return CallToolResult.Error(error);

                    string query = args["query"]?.ToString();
                    bool includeUnpinned = TryGetBool(args, "includeUnpinned", false);
                    int limit = ClampLimit(args, 100, 500);
                    var tags = ResourcePinTags(inventory, includeUnpinned)
                        .Where(tag => MatchesTag(tag, query))
                        .OrderBy(tag => tag.ProperNameStripLink())
                        .Take(limit)
                        .Select(tag => ResourcePinInfo(inventory, tag))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["includeUnpinned"] = includeUnpinned,
                        ["returned"] = tags.Count,
                        ["resources"] = tags
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetResourcePin()
        {
            return new McpTool
            {
                Name = "resources_pin_set",
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "set_resource_pin", "resources_notification_set", "resource_pin_set" },
                Tags = new List<string> { "resources", "inventory", "pin", "notification", "allresources" },
                Description = "设置 AllResourcesScreen 资源行的固定显示和通知开关；需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["resource"] = new McpToolParameter { Type = "string", Description = "资源 tag、prefabId 或名称，例如 Water、Oxygen、Dirt", Required = true },
                    ["pinned"] = new McpToolParameter { Type = "boolean", Description = "是否固定在资源面板；不传则不修改", Required = false },
                    ["notify"] = new McpToolParameter { Type = "boolean", Description = "是否启用资源通知；不传则不修改", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改资源面板开关", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change resource pin/notification settings");

                    var inventory = GetWorldInventory(args, out var worldId, out var error);
                    if (inventory == null)
                        return CallToolResult.Error(error);

                    string resource = args["resource"]?.ToString();
                    var tag = ResolveResourceTag(inventory, resource);
                    if (!tag.IsValid)
                        return CallToolResult.Error("Resource tag not found");

                    var before = ResourcePinInfo(inventory, tag);
                    if (args["pinned"] != null)
                        SetTagPresence(inventory.pinnedResources, tag, ToolUtil.GetBool(args, "pinned", false));
                    if (args["notify"] != null)
                        SetTagPresence(inventory.notifyResources, tag, ToolUtil.GetBool(args, "notify", false));

                    if (PinnedResourcesPanel.Instance != null)
                        PinnedResourcesPanel.Instance.Refresh();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["resource"] = tag.Name,
                        ["before"] = before,
                        ["after"] = ResourcePinInfo(inventory, tag)
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static int? TryGetInt(JObject args, string key)
        {
            int value;
            return args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : (int?)null;
        }

        private static Dictionary<string, object> ItemSearchInfo(Pickupable pickupable)
        {
            var go = pickupable.gameObject;
            var kpid = pickupable.KPrefabID ?? go.GetComponent<KPrefabID>();
            var primary = pickupable.PrimaryElement ?? go.GetComponent<PrimaryElement>();
            var edible = go.GetComponent<Edible>();
            var storage = pickupable.storage;
            int cell = pickupable.cachedCell;
            if (!Grid.IsValidCell(cell))
                cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId();
            bool stored = storage != null || (kpid != null && kpid.HasTag(GameTags.Stored));

            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(pickupable.GetProperName()),
                ["elementId"] = primary != null ? primary.ElementID.ToString() : null,
                ["massKg"] = primary != null ? (object)Math.Round(SafeFloat(primary.Mass), 3) : null,
                ["units"] = primary != null ? (object)Math.Round(SafeFloat(primary.Units), 3) : null,
                ["cell"] = Grid.IsValidCell(cell) ? (object)cell : null,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["stored"] = stored,
                ["storage"] = storage != null ? StorageInfo(storage) : null
            };

            if (edible != null)
            {
                result["caloriesKcal"] = Math.Round(SafeFloat(edible.Calories) / 1000f, 1);
                result["foodQuality"] = edible.GetQuality();
            }

            return result.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static Dictionary<string, object> StorageInfo(Storage storage)
        {
            var go = storage?.gameObject;
            if (go == null)
                return null;
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["cell"] = Grid.IsValidCell(cell) ? (object)cell : null,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            }.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static bool ItemMatches(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            return Contains(Value(info, "prefabId"), q)
                || Contains(Value(info, "name"), q)
                || Contains(Value(info, "elementId"), q);
        }

        private static string Value(Dictionary<string, object> info, string key)
        {
            object value;
            return info != null && info.TryGetValue(key, out value) && value != null ? value.ToString() : null;
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

        private static IEnumerable<Tag> ResourcePinTags(WorldInventory inventory, bool includeUnpinned)
        {
            var tags = new HashSet<Tag>();
            if (inventory.pinnedResources != null)
                foreach (var tag in inventory.pinnedResources)
                    tags.Add(tag);
            if (inventory.notifyResources != null)
                foreach (var tag in inventory.notifyResources)
                    tags.Add(tag);

            if (includeUnpinned && DiscoveredResources.Instance != null)
            {
                foreach (var category in GameTags.MaterialCategories)
                    foreach (var tag in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                        tags.Add(tag);
                foreach (var category in GameTags.CalorieCategories)
                    foreach (var tag in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                        tags.Add(tag);
                foreach (var category in GameTags.UnitCategories)
                    foreach (var tag in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                        tags.Add(tag);
            }

            return tags.Where(tag => tag.IsValid);
        }

        private static Dictionary<string, object> ResourcePinInfo(WorldInventory inventory, Tag tag)
        {
            var result = new Dictionary<string, object>
            {
                ["tag"] = tag.Name,
                ["name"] = tag.ProperNameStripLink(),
                ["pinned"] = inventory.pinnedResources != null && inventory.pinnedResources.Contains(tag),
                ["notify"] = inventory.notifyResources != null && inventory.notifyResources.Contains(tag)
            };

            return result;
        }

        private static Tag ResolveResourceTag(WorldInventory inventory, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Tag.Invalid;

            string q = query.Trim();
            var candidates = ResourcePinTags(inventory, includeUnpinned: true)
                .Concat(Components.Pickupables.Items
                    .Where(item => item != null && item.KPrefabID != null)
                    .Select(item => item.KPrefabID.PrefabTag))
                .Where(tag => tag.IsValid)
                .Distinct()
                .ToList();

            var exact = candidates.FirstOrDefault(tag => EqualsIgnoreCase(tag.Name, q) || EqualsIgnoreCase(tag.ProperNameStripLink(), q));
            if (exact.IsValid)
                return exact;

            return candidates.FirstOrDefault(tag => Contains(tag.Name, q) || Contains(tag.ProperNameStripLink(), q));
        }

        private static bool MatchesTag(Tag tag, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || Contains(tag.Name, query)
                || Contains(tag.ProperNameStripLink(), query);
        }

        private static void SetTagPresence(List<Tag> tags, Tag tag, bool enabled)
        {
            if (tags == null)
                return;

            if (enabled)
            {
                if (!tags.Contains(tag))
                    tags.Add(tag);
            }
            else
            {
                tags.Remove(tag);
            }
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(query) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }

        private class InventoryAggregate
        {
            public string Name;
            public string PrefabId;
            public string ElementId;
            public int Count;
            public int StoredCount;
            public int LooseCount;
            public float TotalMassKg;
            public float TotalUnits;
            public float TotalCaloriesKcal;
            public int SampleCell = -1;
            public int SampleX;
            public int SampleY;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                var result = new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["elementId"] = ElementId,
                    ["count"] = Count,
                    ["storedCount"] = StoredCount,
                    ["looseCount"] = LooseCount,
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["totalUnits"] = Math.Round(TotalUnits, 3),
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };

                if (TotalCaloriesKcal > 0f)
                    result["totalCaloriesKcal"] = Math.Round(TotalCaloriesKcal, 1);

                if (SampleCell >= 0)
                    result["samplePosition"] = new { x = SampleX, y = SampleY };

                return result;
            }
        }

        private class FoodAggregate
        {
            public string Name;
            public string PrefabId;
            public int Quality;
            public int Morale;
            public int Count;
            public int StoredCount;
            public float TotalCaloriesKcal;
            public float TotalMassKg;
            public HashSet<int> WorldIds;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["quality"] = Quality,
                    ["morale"] = Morale,
                    ["count"] = Count,
                    ["storedCount"] = StoredCount,
                    ["totalCaloriesKcal"] = Math.Round(TotalCaloriesKcal, 1),
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["worldIds"] = WorldIds.OrderBy(id => id).ToList()
                };
            }
        }
    }
}
