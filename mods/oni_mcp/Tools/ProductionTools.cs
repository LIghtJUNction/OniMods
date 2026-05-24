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
    public static class ProductionTools
    {
        public static McpTool ListFabricators()
        {
            return new McpTool
            {
                Name = "production_fabricators_list",
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "fabricators_list", "crafting_stations_list" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "craft", "refinery", "kitchen" },
                Description = "列出制作站/精炼/厨房等 ComplexFabricator 的配方队列、当前订单、下一订单和运行状态",
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
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "fabricator_recipes_list", "crafting_recipes_list" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "ingredients", "results" },
                Description = "列出制作站可用 ComplexRecipe，包含 recipeId、分类、材料、产物、科技锁和当前队列数量；可按单个制作站、区域或关键词筛选",
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
                            if (!includeLocked && !recipe.IsRequiredTechOrPOIUnlocked())
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
                Aliases = new List<string> { "fabricator_queue_set", "crafting_queue_set" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "craft" },
                Description = "设置制作站配方队列数量：set/add/remove/infinite/clear，对应制作站侧屏队列加减和无限制作",
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

        public static McpTool BatchSetQueue()
        {
            return new McpTool
            {
                Name = "production_queue_batch_set",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "fabricator_queue_batch_set", "crafting_queue_batch_set" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "batch", "craft" },
                Description = "批量设置同一制作站的多个配方队列。items 支持短字段 r/m/c，defaults 可共享 mode/count，适合一次规划多个制作订单",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "队列操作数组。每项：recipeId/r，mode/m=set|add|remove|infinite|clear，count/c", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认队列参数；支持 mode/m、count/c，子项参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["clearAll"] = new McpToolParameter { Type = "boolean", Description = "true=先清空该制作站所有配方队列", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，避免误改生产队列", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change production queues");

                    var fabricator = FindFabricator(args);
                    if (fabricator == null)
                        return CallToolResult.Error("ComplexFabricator target not found");

                    var items = args["items"] as JArray ?? new JArray();
                    bool clearAll = ToolUtil.GetBool(args, "clearAll", false);
                    if (!clearAll && items.Count == 0)
                        return CallToolResult.Error("items must contain at least one operation unless clearAll=true");

                    var defaults = args["defaults"] as JObject ?? args["defaultArguments"] as JObject;
                    var before = QueueSummary(fabricator);
                    var changes = new List<Dictionary<string, object>>();
                    if (clearAll)
                    {
                        foreach (var recipe in fabricator.GetRecipes())
                        {
                            int oldCount = fabricator.GetRecipeQueueCount(recipe);
                            if (oldCount == 0)
                                continue;
                            fabricator.SetRecipeQueueCount(recipe, 0);
                            changes.Add(new Dictionary<string, object>
                            {
                                ["recipeId"] = recipe.id,
                                ["mode"] = "clear",
                                ["before"] = FormatQueueCount(oldCount),
                                ["after"] = 0
                            });
                        }
                    }

                    foreach (var token in items)
                    {
                        var rawItem = token as JObject;
                        if (rawItem == null)
                            return CallToolResult.Error("Each items entry must be an object");

                        var item = MergeQueueDefaults(rawItem, defaults);
                        string recipeId = item["recipeId"]?.ToString();
                        if (string.IsNullOrWhiteSpace(recipeId))
                            return CallToolResult.Error("Each item requires recipeId or r");
                        var recipe = fabricator.GetRecipe(recipeId.Trim());
                        if (recipe == null)
                            return CallToolResult.Error("recipeId is not available on this fabricator: " + recipeId);

                        string mode = (item["mode"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                        int requested = Math.Max(0, ToolUtil.GetInt(item, "count") ?? 1);
                        int oldCount = fabricator.GetRecipeQueueCount(recipe);
                        int next;
                        if (!TryComputeQueueCount(oldCount, mode, requested, out next))
                            return CallToolResult.Error("mode must be set, add, remove, infinite or clear");

                        fabricator.SetRecipeQueueCount(recipe, next);
                        changes.Add(new Dictionary<string, object>
                        {
                            ["recipeId"] = recipe.id,
                            ["name"] = SafeRecipeName(recipe, includeAmounts: false),
                            ["mode"] = mode,
                            ["requested"] = requested,
                            ["before"] = FormatQueueCount(oldCount),
                            ["after"] = FormatQueueCount(fabricator.GetRecipeQueueCount(recipe)),
                            ["changed"] = oldCount != fabricator.GetRecipeQueueCount(recipe)
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["fabricator"] = TargetInfo(fabricator.gameObject),
                        ["before"] = before,
                        ["changes"] = changes,
                        ["queue"] = QueueSummary(fabricator)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetMutantSeeds()
        {
            return new McpTool
            {
                Name = "production_mutant_seeds_set",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "fabricator_mutant_seeds_set", "production_seed_mutations_set" },
                Tags = new List<string> { "production", "fabricator", "seeds", "mutations", "spaced-out" },
                Description = "设置制作站是否拒收突变种子，对应 DLC 制作站用户菜单的接受/拒绝突变种子开关",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["forbid"] = new McpToolParameter { Type = "boolean", Description = "true=拒收突变种子，false=接受突变种子", Required = true }
                }),
                Handler = args =>
                {
                    var fabricator = FindFabricator(args);
                    if (fabricator == null)
                        return CallToolResult.Error("ComplexFabricator target not found");

                    bool forbid = ToolUtil.GetBool(args, "forbid", false);
                    bool before = fabricator.ForbidMutantSeeds;
                    fabricator.ForbidMutantSeeds = forbid;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["fabricator"] = TargetInfo(fabricator.gameObject),
                        ["before"] = before,
                        ["forbidMutantSeeds"] = fabricator.ForbidMutantSeeds,
                        ["changed"] = before != fabricator.ForbidMutantSeeds
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static IEnumerable<ComplexFabricator> FindFabricators(JObject args)
        {
            bool hasTarget = HasLookupInput(args);
            if (hasTarget)
            {
                var target = FindFabricator(args);
                if (target != null)
                    yield return target;
                yield break;
            }

            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["fabricatorQuery"]?.ToString();
            foreach (var fabricator in Components.ComplexFabricators.Items)
            {
                if (MatchesFabricator(fabricator, rect, worldId, query))
                    yield return fabricator;
            }
        }

        private static ComplexFabricator FindFabricator(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var fabricator in Components.ComplexFabricators.Items)
            {
                var go = fabricator?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return fabricator;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return fabricator;
            }

            return null;
        }

        private static JObject MergeQueueDefaults(JObject item, JObject defaults)
        {
            var result = new JObject();
            CopyQueueAliases(defaults, result, overwrite: false);
            CopyNonQueueAliases(defaults, result, overwrite: false);
            CopyQueueAliases(item, result, overwrite: true);
            CopyNonQueueAliases(item, result, overwrite: true);
            return result;
        }

        private static void CopyQueueAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "recipeId", "r", overwrite);
            CopyAlias(source, target, "mode", "m", overwrite);
            CopyAlias(source, target, "count", "c", overwrite);
        }

        private static void CopyAlias(JObject source, JObject target, string longKey, string shortKey, bool overwrite)
        {
            var token = source[longKey] ?? source[shortKey];
            if (token != null && (overwrite || target[longKey] == null))
                target[longKey] = token.DeepClone();
        }

        private static void CopyNonQueueAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            foreach (var property in source.Properties())
            {
                if (IsQueueAlias(property.Name))
                    continue;
                if (overwrite || target[property.Name] == null)
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        private static bool IsQueueAlias(string name)
        {
            return string.Equals(name, "recipeId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "r", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "mode", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "m", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "count", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "c", StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesFabricator(ComplexFabricator fabricator, Dictionary<string, int> rect, int worldId, string query)
        {
            var go = fabricator?.gameObject;
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            if (rect != null && !CellInRect(cell, rect, worldId))
                return false;
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            if (Contains(TargetName(go), q) || Contains(PrefabId(go), q))
                return true;
            if (fabricator.CurrentWorkingOrder != null && RecipeMatches(fabricator.CurrentWorkingOrder, q))
                return true;
            if (fabricator.NextOrder != null && RecipeMatches(fabricator.NextOrder, q))
                return true;
            return fabricator.GetRecipes().Any(recipe => fabricator.GetRecipeQueueCount(recipe) != 0 && RecipeMatches(recipe, q));
        }

        private static Dictionary<string, object> FabricatorInfo(ComplexFabricator fabricator, bool includeRecipes)
        {
            var go = fabricator.gameObject;
            var result = TargetInfo(go);
            var operational = go.GetComponent<Operational>();
            result["operational"] = operational == null ? null : (object)new Dictionary<string, object>
            {
                ["isOperational"] = operational.IsOperational,
                ["isActive"] = operational.IsActive
            };
            result["hasAnyOrder"] = fabricator.HasAnyOrder;
            result["waitingForWorker"] = fabricator.WaitingForWorker;
            result["hasWorker"] = fabricator.HasWorker;
            result["duplicantOperated"] = fabricator.duplicantOperated;
            result["forbidMutantSeeds"] = fabricator.ForbidMutantSeeds;
            result["orderProgress"] = ToolUtil.SafeFloat(fabricator.OrderProgress);
            result["recipeCount"] = fabricator.GetRecipes().Length;
            result["currentRecipe"] = RecipeSummary(fabricator.CurrentWorkingOrder);
            result["nextRecipe"] = RecipeSummary(fabricator.NextOrder);
            result["queue"] = QueueSummary(fabricator);
            if (includeRecipes)
                result["recipes"] = fabricator.GetRecipes().Select(recipe => RecipeInfo(fabricator, recipe, includeFabricator: false)).ToList();
            return result;
        }

        private static Dictionary<string, object> RecipeInfo(ComplexFabricator fabricator, ComplexRecipe recipe, bool includeFabricator)
        {
            var result = RecipeSummary(recipe);
            result["queueCount"] = FormatQueueCount(fabricator.GetRecipeQueueCount(recipe));
            result["isQueued"] = fabricator.GetRecipeQueueCount(recipe) != 0;
            result["isCurrent"] = fabricator.CurrentWorkingOrder != null && fabricator.CurrentWorkingOrder.id == recipe.id;
            result["isNext"] = fabricator.NextOrder != null && fabricator.NextOrder.id == recipe.id;
            result["timeSeconds"] = ToolUtil.SafeFloat(recipe.time);
            result["requiredTech"] = recipe.requiredTech;
            result["requiresTechUnlock"] = recipe.RequiresTechUnlock();
            result["techOrPoiUnlocked"] = recipe.IsRequiredTechOrPOIUnlocked();
            result["requiresAllIngredientsDiscovered"] = recipe.RequiresAllIngredientsDiscovered;
            result["consumedHEP"] = recipe.consumedHEP;
            result["producedHEP"] = recipe.producedHEP;
            result["ingredients"] = recipe.ingredients.Select(RecipeElementInfo).ToList();
            result["results"] = recipe.results.Select(RecipeElementInfo).ToList();
            if (includeFabricator)
                result["fabricator"] = TargetInfo(fabricator.gameObject);
            return result;
        }

        private static Dictionary<string, object> RecipeSummary(ComplexRecipe recipe)
        {
            if (recipe == null)
                return null;

            return new Dictionary<string, object>
            {
                ["recipeId"] = recipe.id,
                ["categoryId"] = recipe.recipeCategoryID,
                ["name"] = SafeRecipeName(recipe, includeAmounts: false),
                ["nameWithAmounts"] = SafeRecipeName(recipe, includeAmounts: true),
                ["description"] = recipe.description,
                ["firstResult"] = recipe.FirstResult.Name
            };
        }

        private static Dictionary<string, object> RecipeElementInfo(ComplexRecipe.RecipeElement element)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = element.material.Name,
                ["name"] = SafeProperName(element.material),
                ["amount"] = ToolUtil.SafeFloat(element.amount),
                ["temperatureOperation"] = element.temperatureOperation.ToString(),
                ["storeElement"] = element.storeElement,
                ["inheritElement"] = element.inheritElement,
                ["facadeId"] = element.facadeID,
                ["doNotConsume"] = element.doNotConsume
            };
        }

        private static Dictionary<string, object> QueueSummary(ComplexFabricator fabricator)
        {
            var queued = fabricator.GetRecipes()
                .Where(recipe => fabricator.GetRecipeQueueCount(recipe) != 0)
                .Select(recipe => new Dictionary<string, object>
                {
                    ["recipeId"] = recipe.id,
                    ["categoryId"] = recipe.recipeCategoryID,
                    ["name"] = SafeRecipeName(recipe, includeAmounts: false),
                    ["count"] = FormatQueueCount(fabricator.GetRecipeQueueCount(recipe))
                })
                .ToList();

            return new Dictionary<string, object>
            {
                ["queuedRecipeCount"] = queued.Count,
                ["totalQueued"] = FormatQueueCount(QueuedRecipeCount(fabricator)),
                ["recipes"] = queued
            };
        }

        private static int QueuedRecipeCount(ComplexFabricator fabricator)
        {
            int total = 0;
            foreach (var recipe in fabricator.GetRecipes())
            {
                int count = fabricator.GetRecipeQueueCount(recipe);
                if (count == ComplexFabricator.QUEUE_INFINITE)
                    return ComplexFabricator.QUEUE_INFINITE;
                total += count;
            }
            return total;
        }

        private static object FormatQueueCount(int count)
        {
            if (count == ComplexFabricator.QUEUE_INFINITE)
                return "infinite";
            return count;
        }

        private static bool TryComputeQueueCount(int before, string mode, int requested, out int next)
        {
            switch (mode)
            {
                case "clear":
                    next = 0;
                    return true;
                case "infinite":
                    next = ComplexFabricator.QUEUE_INFINITE;
                    return true;
                case "add":
                    next = before == ComplexFabricator.QUEUE_INFINITE
                        ? ComplexFabricator.QUEUE_INFINITE
                        : Mathf.Clamp(before + Math.Max(1, requested), 0, ComplexFabricator.MAX_QUEUE_SIZE);
                    return true;
                case "remove":
                    next = before == ComplexFabricator.QUEUE_INFINITE
                        ? Mathf.Max(0, ComplexFabricator.MAX_QUEUE_SIZE - Math.Max(1, requested))
                        : Mathf.Clamp(before - Math.Max(1, requested), 0, ComplexFabricator.MAX_QUEUE_SIZE);
                    return true;
                case "set":
                    next = Mathf.Clamp(requested, 0, ComplexFabricator.MAX_QUEUE_SIZE);
                    return true;
                default:
                    next = before;
                    return false;
            }
        }

        private static bool RecipeMatches(ComplexRecipe recipe, string query)
        {
            if (recipe == null)
                return false;
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            return Contains(recipe.id, q)
                || Contains(recipe.recipeCategoryID, q)
                || Contains(SafeRecipeName(recipe, includeAmounts: false), q)
                || recipe.ingredients.Any(element => ElementMatches(element, q))
                || recipe.results.Any(element => ElementMatches(element, q));
        }

        private static bool ElementMatches(ComplexRecipe.RecipeElement element, string query)
        {
            return Contains(element.material.Name, query) || Contains(SafeProperName(element.material), query);
        }

        private static string SafeRecipeName(ComplexRecipe recipe, bool includeAmounts)
        {
            try
            {
                return ToolUtil.CleanName(recipe.GetUIName(includeAmounts));
            }
            catch
            {
                return recipe.id;
            }
        }

        private static string SafeProperName(Tag tag)
        {
            try
            {
                return ToolUtil.CleanName(tag.ProperName());
            }
            catch
            {
                return tag.Name;
            }
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = PrefabId(go),
                ["name"] = TargetName(go),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static string PrefabId(GameObject go)
        {
            var building = go.GetComponent<Building>();
            return building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
        }

        private static string TargetName(GameObject go)
        {
            return ToolUtil.CleanName(go.GetProperName());
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标制作站 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标制作站格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标制作站格子 Y", Required = false },
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

        private static bool HasLookupInput(JObject args)
        {
            return args["id"] != null || (args["x"] != null && args["y"] != null);
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

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
