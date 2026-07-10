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
            result["techUnlocked"] = recipe.IsRequiredTechUnlocked();
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

        private static Dictionary<string, McpToolParameter> ProductionQueueControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list_fabricators、list_recipes、set、batch、set_mutant_seeds、mutant_seed_list 或 mutant_seed_set", Required = false, EnumValues = new List<string> { "list_fabricators", "list_recipes", "set", "batch", "set_mutant_seeds", "mutant_seed_list", "mutant_seed_set" } },
                ["recipeId"] = new McpToolParameter { Type = "string", Description = "action=list_recipes 时精确过滤；action=set 时为目标 ComplexRecipe.id", Required = false },
                ["categoryId"] = new McpToolParameter { Type = "string", Description = "action=list_recipes 时精确配方分类 id 过滤", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "读取时按建筑名、prefabId、配方、材料、产物或突变种子开关组件筛选", Required = false },
                ["fabricatorQuery"] = new McpToolParameter { Type = "string", Description = "action=list_recipes 时筛选制作站", Required = false },
                ["queuedOnly"] = new McpToolParameter { Type = "boolean", Description = "读取时是否只返回已有队列/订单项，默认 false", Required = false },
                ["includeRecipes"] = new McpToolParameter { Type = "boolean", Description = "action=list_fabricators 时是否附带每个制作站的配方摘要，默认 false", Required = false },
                ["includeLocked"] = new McpToolParameter { Type = "boolean", Description = "action=list_recipes 时是否包含科技未解锁配方，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "读取时最多返回数量", Required = false },
                ["mode"] = new McpToolParameter { Type = "string", Description = "action=set/batch 时队列模式：set、add、remove、infinite 或 clear，默认 set", Required = false, EnumValues = new List<string> { "set", "add", "remove", "infinite", "clear" } },
                ["count"] = new McpToolParameter { Type = "integer", Description = "action=set/batch 时 set/add/remove 的数量", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时队列操作数组。每项：recipeId/r，mode/m，count/c", Required = false },
                ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认队列参数", Required = false },
                ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                ["clearAll"] = new McpToolParameter { Type = "boolean", Description = "action=batch 时 true=先清空该制作站所有配方队列", Required = false },
                ["forbid"] = new McpToolParameter { Type = "boolean", Description = "action=set_mutant_seeds 时 true=拒收突变种子，false=接受突变种子", Required = false },
                ["accept"] = new McpToolParameter { Type = "boolean", Description = "action=mutant_seed_set 时 true=接受突变种子，false=拒收突变种子", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写队列或突变种子开关时必须为 true，避免误改生产状态", Required = false }
            }));
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
