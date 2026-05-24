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
        public static McpTool GetDietStatus()
        {
            return new McpTool
            {
                Name = "diet_status",
                Group = "diet",
                Mode = "read",
                Risk = "none",
                Description = "查看复制人的饮食权限和当前食物库存",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空返回全部", Required = false },
                    ["includeAllFoods"] = new McpToolParameter { Type = "boolean", Description = "是否包含未库存的全部食物，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeAllFoods = ToolUtil.GetBool(args, "includeAllFoods", false);
                    var target = ToolUtil.FindDupe(args);
                    var dupes = target != null
                        ? new List<MinionIdentity> { target }
                        : Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).ToList();
                    var stocked = GetStockedFoods();
                    var foodIds = includeAllFoods
                        ? GetAllFoodInfos().Select(food => food.Id).ToList()
                        : stocked.Keys.OrderBy(id => id).ToList();

                    var result = new Dictionary<string, object>
                    {
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
                Description = "允许或禁用某个复制人食用指定食物。可用 allDupes=true 应用到全部复制人",
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
                Description = "按食物品质批量配置饮食。常用：minQuality=-1 允许全部基础食物，minQuality=0 禁用营养棒等低品质食物",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空配合 allDupes", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空配合 allDupes", Required = false },
                    ["minQuality"] = new McpToolParameter { Type = "integer", Description = "最低允许品质，默认 -1", Required = false },
                    ["onlyStocked"] = new McpToolParameter { Type = "boolean", Description = "是否只修改当前库存食物，默认 true", Required = false },
                    ["allDupes"] = new McpToolParameter { Type = "boolean", Description = "是否应用到全部复制人，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int minQuality = ToolUtil.GetInt(args, "minQuality") ?? -1;
                    bool onlyStocked = ToolUtil.GetBool(args, "onlyStocked", true);
                    bool allDupes = ToolUtil.GetBool(args, "allDupes", true);
                    var dupes = SelectDupes(args, allDupes);
                    if (dupes.Count == 0)
                        return CallToolResult.Error("Duplicant not found");

                    var stocked = GetStockedFoods();
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
                        ["foodsChanged"] = foods.Count,
                        ["changes"] = changes
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
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

        private static Dictionary<string, FoodStock> GetStockedFoods()
        {
            var stocked = new Dictionary<string, FoodStock>();
            foreach (var edible in Components.Edibles.Items)
            {
                if (edible == null || edible.gameObject == null)
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
