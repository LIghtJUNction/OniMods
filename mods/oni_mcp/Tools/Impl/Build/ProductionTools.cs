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
    public static partial class ProductionTools
    {
        public static McpTool ListFabricators()
        {
            return new McpTool
            {
                Name = "production_fabricators_list",
                Hidden = true,
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "fabricators_list", "crafting_stations_list" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "craft", "refinery", "kitchen" },
                Description = "兼容入口：请优先使用 building_control domain=production action=list_fabricators。列出制作站/精炼/厨房等 ComplexFabricator 的配方队列、当前订单、下一订单和运行状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、当前/下一配方或已排队配方筛选", Required = false },
                    ["queuedOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回有排队或正在工作订单的制作站，默认 false", Required = false },
                    ["includeRecipes"] = new McpToolParameter { Type = "boolean", Description = "是否附带每个制作站的配方摘要，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool queuedOnly = ToolUtil.GetBool(args, "queuedOnly", false);
                    bool includeRecipes = ToolUtil.GetBool(args, "includeRecipes", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var results = Components.ComplexFabricators.Items
                        .Where(fabricator => MatchesFabricator(fabricator, rect, worldId, query))
                        .Where(fabricator => !queuedOnly || fabricator.HasAnyOrder || QueuedRecipeCount(fabricator) != 0)
                        .OrderBy(fabricator => TargetName(fabricator.gameObject))
                        .Take(limit)
                        .Select(fabricator => FabricatorInfo(fabricator, includeRecipes))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["queuedOnly"] = queuedOnly,
                        ["fabricators"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListRecipes()
        {
            return new McpTool
            {
                Name = "production_recipes_list",
                Hidden = true,
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "fabricator_recipes_list", "crafting_recipes_list" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "ingredients", "results" },
                Description = "兼容入口：请优先使用 building_control domain=production action=list_recipes。列出制作站可用 ComplexRecipe，包含 recipeId、分类、材料、产物、科技锁和当前队列数量；可按单个制作站、区域或关键词筛选",
                Parameters = LookupParams(RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["recipeId"] = new McpToolParameter { Type = "string", Description = "精确配方 id 过滤", Required = false },
                    ["categoryId"] = new McpToolParameter { Type = "string", Description = "精确配方分类 id 过滤", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按配方名、recipeId、categoryId、材料或产物筛选", Required = false },
                    ["queuedOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已排队配方，默认 false", Required = false },
                    ["includeLocked"] = new McpToolParameter { Type = "boolean", Description = "是否包含科技未解锁配方，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回配方数量，默认 200，最大 1000", Required = false }
                })),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string recipeId = args["recipeId"]?.ToString();
                    string categoryId = args["categoryId"]?.ToString();
                    string query = args["query"]?.ToString();
                    bool queuedOnly = ToolUtil.GetBool(args, "queuedOnly", false);
                    bool includeLocked = ToolUtil.GetBool(args, "includeLocked", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 200, 1000));

                    var recipes = new List<Dictionary<string, object>>();
                    foreach (var fabricator in FindFabricators(args))
                    {
                        foreach (var recipe in fabricator.GetRecipes())
                        {
                            if (!string.IsNullOrWhiteSpace(recipeId) && !string.Equals(recipe.id, recipeId.Trim(), StringComparison.Ordinal))
                                continue;
                            if (!string.IsNullOrWhiteSpace(categoryId) && !string.Equals(recipe.recipeCategoryID, categoryId.Trim(), StringComparison.Ordinal))
                                continue;
                            if (queuedOnly && fabricator.GetRecipeQueueCount(recipe) == 0)
                                continue;
                            if (!includeLocked && !recipe.IsRequiredTechUnlocked())
                                continue;
                            if (!RecipeMatches(recipe, query))
                                continue;

                            recipes.Add(RecipeInfo(fabricator, recipe, includeFabricator: true));
                            if (recipes.Count >= limit)
                                break;
                        }
                        if (recipes.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = recipes.Count,
                        ["queuedOnly"] = queuedOnly,
                        ["includeLocked"] = includeLocked,
                        ["recipes"] = recipes
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetQueue()
        {
            return new McpTool
            {
                Name = "production_queue_set",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "fabricator_queue_set", "crafting_queue_set" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "craft" },
                Description = "兼容入口：请优先使用 building_control domain=production action=set。设置制作站配方队列数量：set/add/remove/infinite/clear，对应制作站侧屏队列加减和无限制作",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["recipeId"] = new McpToolParameter { Type = "string", Description = "目标 ComplexRecipe.id，可先用 production_recipes_list 查询", Required = true },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "set、add、remove、infinite 或 clear，默认 set", Required = false, EnumValues = new List<string> { "set", "add", "remove", "infinite", "clear" } },
                    ["count"] = new McpToolParameter { Type = "integer", Description = "set/add/remove 的数量；set 默认 1，add/remove 默认 1", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，避免误改生产队列", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change a production queue");

                    var fabricator = FindFabricator(args);
                    if (fabricator == null)
                        return CallToolResult.Error("ComplexFabricator target not found");

                    string recipeId = args["recipeId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(recipeId))
                        return CallToolResult.Error("recipeId is required");

                    var recipe = fabricator.GetRecipe(recipeId.Trim());
                    if (recipe == null)
                        return CallToolResult.Error("recipeId is not available on this fabricator");

                    int before = fabricator.GetRecipeQueueCount(recipe);
                    string mode = (args["mode"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    int requested = Math.Max(0, ToolUtil.GetInt(args, "count") ?? 1);
                    int next;
                    if (!TryComputeQueueCount(before, mode, requested, out next))
                        return CallToolResult.Error("mode must be set, add, remove, infinite or clear");

                    fabricator.SetRecipeQueueCount(recipe, next);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["fabricator"] = TargetInfo(fabricator.gameObject),
                        ["recipe"] = RecipeInfo(fabricator, recipe, includeFabricator: false),
                        ["mode"] = mode,
                        ["before"] = FormatQueueCount(before),
                        ["after"] = FormatQueueCount(fabricator.GetRecipeQueueCount(recipe)),
                        ["changed"] = before != fabricator.GetRecipeQueueCount(recipe)
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
