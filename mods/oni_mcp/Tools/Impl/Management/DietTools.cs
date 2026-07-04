using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class DietTools
    {
        public static McpTool ControlDiet()
        {
            return new McpTool
            {
                Name = "diet_control",
                Group = "diet",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "consumables_control" },
                Description = "饮食权限聚合工具：action=status 查看；action=set 设置单个食物；action=policy 按品质批量应用策略",
                Parameters = DietControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "status":
                        case "list":
                            return GetDietStatus().Handler(args);
                        case "set":
                            return SetDietFood().Handler(args);
                        case "policy":
                        case "apply_policy":
                            return ApplyDietPolicy().Handler(args);
                        default:
                            return CallToolResult.Error("action must be status, set, or policy");
                    }
                }
            };
        }

        public static McpTool GetDietStatus()
        {
            return new McpTool
            {
                Name = "diet_status",
                Group = "diet",
                Mode = "read",
                Risk = "none",
                Description = "兼容旧工具：请改用 colony_control domain=management kind=diet action=status",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空返回全部", Required = false },
                    ["includeAllFoods"] = new McpToolParameter { Type = "boolean", Description = "是否包含未库存的全部食物，默认 false", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只统计已揭示格子内库存食物，默认 true；调试可传 false", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeAllFoods = ToolUtil.GetBool(args, "includeAllFoods", false);
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", true);
                    var target = ToolUtil.FindDupe(args);
                    var dupes = target != null
                        ? new List<MinionIdentity> { target }
                        : Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).ToList();
                    var stocked = GetStockedFoods(visibleOnly);
                    var foodIds = includeAllFoods
                        ? GetAllFoodInfos().Select(food => food.Id).ToList()
                        : stocked.Keys.OrderBy(id => id).ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["visibleOnly"] = visibleOnly,
                        ["stockedFoods"] = stocked.Values.OrderByDescending(food => food.TotalCaloriesKcal).Select(food => food.ToDictionary()).ToList(),
                        ["duplicants"] = dupes.Select(dupe => DupeDietToDictionary(dupe, foodIds)).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetDietFood()
        {
            return new McpTool
            {
                Name = "diet_set",
                Group = "diet",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "set_diet_food" },
                Description = "兼容旧工具：请改用 colony_control domain=management kind=diet action=set",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["food"] = new McpToolParameter { Type = "string", Description = "食物 ID 或名称，例如 FieldRation / 营养棒", Required = true },
                    ["allow"] = new McpToolParameter { Type = "boolean", Description = "true 允许，false 禁用", Required = true },
                    ["allDupes"] = new McpToolParameter { Type = "boolean", Description = "是否应用到全部复制人，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string foodQuery = args["food"]?.ToString();
                    var food = ResolveFood(foodQuery);
                    if (food == null)
                        return CallToolResult.Error($"Food not found: {foodQuery}");

                    bool allow = ToolUtil.GetBool(args, "allow", true);
                    bool allDupes = ToolUtil.GetBool(args, "allDupes", false);
                    var dupes = SelectDupes(args, allDupes);
                    if (dupes.Count == 0)
                        return CallToolResult.Error("Duplicant not found");

                    var changes = ApplyFoodPermission(dupes, food.Id, allow);
                    var result = new Dictionary<string, object>
                    {
                        ["food"] = FoodInfoToDictionary(food),
                        ["allow"] = allow,
                        ["changes"] = changes
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ApplyDietPolicy()
        {
            return new McpTool
            {
                Name = "diet_policy",
                Group = "diet",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "apply_diet_policy" },
                Description = "兼容旧工具：请改用 colony_control domain=management kind=diet action=policy",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空配合 allDupes", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空配合 allDupes", Required = false },
                    ["minQuality"] = new McpToolParameter { Type = "integer", Description = "最低允许品质，默认 -1", Required = false },
                    ["onlyStocked"] = new McpToolParameter { Type = "boolean", Description = "是否只修改当前库存食物，默认 true", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只把已揭示格子内食物视为库存，默认 true；调试可传 false", Required = false },
                    ["allDupes"] = new McpToolParameter { Type = "boolean", Description = "是否应用到全部复制人，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int minQuality = ToolUtil.GetInt(args, "minQuality") ?? -1;
                    bool onlyStocked = ToolUtil.GetBool(args, "onlyStocked", true);
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", true);
                    bool allDupes = ToolUtil.GetBool(args, "allDupes", true);
                    var dupes = SelectDupes(args, allDupes);
                    if (dupes.Count == 0)
                        return CallToolResult.Error("Duplicant not found");

                    var stocked = GetStockedFoods(visibleOnly);
                    var foods = onlyStocked
                        ? GetAllFoodInfos().Where(food => stocked.ContainsKey(food.Id)).ToList()
                        : GetAllFoodInfos();

                    var changes = new List<Dictionary<string, object>>();
                    foreach (var food in foods)
                    {
                        bool allow = food.Quality >= minQuality;
                        changes.AddRange(ApplyFoodPermission(dupes, food.Id, allow));
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["minQuality"] = minQuality,
                        ["onlyStocked"] = onlyStocked,
                        ["visibleOnly"] = visibleOnly,
                        ["foodsChanged"] = foods.Count,
                        ["changes"] = changes
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> DietControlParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "status/list、set 或 policy/apply_policy", Required = true },
                ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；status/set/policy 可用", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；status/set/policy 可用", Required = false },
                ["includeAllFoods"] = new McpToolParameter { Type = "boolean", Description = "action=status 时是否包含未库存食物，默认 false", Required = false },
                ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "action=status/policy 时是否只统计已揭示格子内库存食物，默认 true；调试可传 false", Required = false },
                ["food"] = new McpToolParameter { Type = "string", Description = "action=set 时必填；食物 ID 或名称", Required = false },
                ["allow"] = new McpToolParameter { Type = "boolean", Description = "action=set 时必填；true 允许，false 禁用", Required = false },
                ["minQuality"] = new McpToolParameter { Type = "integer", Description = "action=policy 最低允许品质，默认 -1", Required = false },
                ["onlyStocked"] = new McpToolParameter { Type = "boolean", Description = "action=policy 是否只修改当前库存食物，默认 true", Required = false },
                ["allDupes"] = new McpToolParameter { Type = "boolean", Description = "action=set/policy 是否应用到全部复制人", Required = false }
            };
        }

        private static List<MinionIdentity> SelectDupes(Newtonsoft.Json.Linq.JObject args, bool allDupes)
        {
            if (allDupes)
                return Components.LiveMinionIdentities.Items.Where(item => item != null).ToList();

            var selected = ToolUtil.FindDupe(args);
            return selected != null ? new List<MinionIdentity> { selected } : new List<MinionIdentity>();
        }

        private static List<Dictionary<string, object>> ApplyFoodPermission(List<MinionIdentity> dupes, string foodId, bool allow)
        {
            var changes = new List<Dictionary<string, object>>();
            foreach (var dupe in dupes)
            {
                var consumer = dupe.GetComponent<ConsumableConsumer>();
                if (consumer == null)
                    continue;

                bool before = consumer.IsPermitted(foodId);
                consumer.SetPermitted(foodId, allow);
                changes.Add(new Dictionary<string, object>
                {
                    ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                    ["name"] = dupe.GetProperName(),
                    ["food"] = foodId,
                    ["before"] = before,
                    ["after"] = consumer.IsPermitted(foodId)
                });
            }
            return changes;
        }

        private static Dictionary<string, object> DupeDietToDictionary(MinionIdentity dupe, List<string> foodIds)
        {
            var consumer = dupe.GetComponent<ConsumableConsumer>();
            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["foods"] = foodIds.Select(foodId =>
                {
                    var food = EdiblesManager.GetFoodInfo(foodId);
                    return new Dictionary<string, object>
                    {
                        ["id"] = foodId,
                        ["name"] = food?.Name ?? foodId,
                        ["quality"] = food?.Quality ?? 0,
                        ["permitted"] = consumer?.IsPermitted(foodId) ?? false,
                        ["dietRestricted"] = consumer?.IsDietRestricted(foodId) ?? false
                    };
                }).ToList()
            };
        }

        private static List<EdiblesManager.FoodInfo> GetAllFoodInfos()
        {
            return EdiblesManager.GetAllFoodTypes()
                .Where(food => food != null && food.Display)
                .OrderBy(food => food.Quality)
                .ThenBy(food => food.Id)
                .ToList();
        }

        private static EdiblesManager.FoodInfo ResolveFood(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            string q = query.Trim();
            var foods = GetAllFoodInfos();
            var exact = foods.FirstOrDefault(food =>
                string.Equals(food.Id, q, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(food.Name, q, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var matches = foods.Where(food =>
                Contains(food.Id, q) ||
                Contains(food.Name, q)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static Dictionary<string, FoodStock> GetStockedFoods(bool visibleOnly)
        {
            var stocked = new Dictionary<string, FoodStock>();
            foreach (var edible in Components.Edibles.Items)
            {
                if (edible == null || edible.gameObject == null)
                    continue;
                var pickupable = edible.GetComponent<Pickupable>();
                int cell = pickupable != null ? ToolUtil.PickupableCell(pickupable) : Grid.PosToCell(edible);
                if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                    continue;

                string id = edible.GetComponent<KPrefabID>()?.PrefabTag.Name ?? edible.name;
                var info = EdiblesManager.GetFoodInfo(id);
                if (info == null)
                    continue;

                if (!stocked.TryGetValue(id, out var stock))
                {
                    stock = new FoodStock
                    {
                        Id = id,
                        Name = info.Name,
                        Quality = info.Quality
                    };
                    stocked[id] = stock;
                }
                stock.Count++;
                stock.TotalCaloriesKcal += SafeFloat(edible.Calories) / 1000f;
            }
            return stocked;
        }

        private static Dictionary<string, object> FoodInfoToDictionary(EdiblesManager.FoodInfo food)
        {
            return new Dictionary<string, object>
            {
                ["id"] = food.Id,
                ["name"] = food.Name,
                ["quality"] = food.Quality,
                ["caloriesPerUnit"] = Math.Round(food.CaloriesPerUnit / 1000f, 1),
                ["canRot"] = food.CanRot
            };
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private class FoodStock
        {
            public string Id;
            public string Name;
            public int Quality;
            public int Count;
            public float TotalCaloriesKcal;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["quality"] = Quality,
                    ["count"] = Count,
                    ["totalCaloriesKcal"] = Math.Round(TotalCaloriesKcal, 1)
                };
            }
        }
    }
}
