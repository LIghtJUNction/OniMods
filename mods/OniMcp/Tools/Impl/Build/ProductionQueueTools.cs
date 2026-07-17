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
        public static McpTool BatchSetQueue()
        {
            return new McpTool
            {
                Name = "production_queue_batch_set",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "fabricator_queue_batch_set", "crafting_queue_batch_set" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "batch", "craft" },
                Description = "兼容入口：请优先使用 building_control domain=production action=batch。批量设置同一制作站的多个配方队列。items 支持短字段 r/m/c，defaults 可共享 mode/count，适合一次规划多个制作订单",
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

        public static McpTool ControlQueue()
        {
            return new McpTool
            {
                Name = "production_queue_control",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "fabricator_queue_control", "crafting_queue_control" },
                Tags = new List<string> { "production", "fabricator", "recipe", "queue", "batch", "craft" },
                Description = "生产队列聚合工具：action=list_fabricators/list_recipes/set/batch/set_mutant_seeds/mutant_seed_list/mutant_seed_set；读取制作站和配方，设置队列或突变种子策略。",
                Parameters = ProductionQueueControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list_fabricators").Trim().ToLowerInvariant();
                    if (action == "list" || action == "list_fabricators" || action == "fabricators")
                        return ListFabricators().Handler(args);
                    if (action == "list_recipes" || action == "recipes")
                        return ListRecipes().Handler(args);
                    if (action == "set")
                        return SetQueue().Handler(args);
                    if (action == "batch")
                        return BatchSetQueue().Handler(args);
                    if (action == "set_mutant_seeds" || action == "mutant_seeds")
                        return SetMutantSeeds().Handler(args);
                    if (action == "mutant_seed_list" || action == "list_mutant_seeds")
                        return SpecialUserMenuActionTools.ListMutantSeedControls().Handler(args);
                    if (action == "mutant_seed_set" || action == "set_mutant_seed_control")
                        return SpecialUserMenuActionTools.SetMutantSeedControl().Handler(args);
                    return CallToolResult.Error("action must be one of list_fabricators, list_recipes, set, batch, set_mutant_seeds, mutant_seed_list, mutant_seed_set");
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
                Hidden = true,
                Aliases = new List<string> { "fabricator_mutant_seeds_set", "production_seed_mutations_set" },
                Tags = new List<string> { "production", "fabricator", "seeds", "mutations", "spaced-out" },
                Description = "兼容入口：请优先使用 building_control domain=production action=set_mutant_seeds。设置制作站是否拒收突变种子，对应 DLC 制作站用户菜单的接受/拒绝突变种子开关",
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

    }
}
