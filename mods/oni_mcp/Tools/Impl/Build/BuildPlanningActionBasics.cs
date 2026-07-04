using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        public static McpTool SearchBuildables()
        {
            return new McpTool
            {
                Name = "buildings_search_defs",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "兼容入口：请使用 building_control domain=planning action=search_defs",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "建筑 ID 或名称关键词", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "建造菜单分类/类别关键词，如 oxygen、plumbing、rocketry；大小写不敏感", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前未可用/未解锁的建筑定义，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    string category = args["category"]?.ToString();
                    bool includeUnavailable = ToolUtil.GetBool(args, "includeUnavailable", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 30, 100));
                    var defs = Assets.BuildingDefs
                        .Where(def => def != null && (includeUnavailable || IsUnlockedAndAvailable(def)))
                        .Where(def => string.IsNullOrWhiteSpace(category) || MatchesCategory(def, category))
                        .Where(def => string.IsNullOrWhiteSpace(query) || Matches(def, query))
                        .OrderBy(def => def.PrefabID)
                        .Take(limit)
                        .Select(BuildingDefToDictionary)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["category"] = string.IsNullOrWhiteSpace(category) ? null : category,
                        ["includeUnavailable"] = includeUnavailable,
                        ["returned"] = defs.Count,
                        ["buildings"] = defs
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ParseBuildPlan()
        {
            return new McpTool
            {
                Name = "build_parse_plan",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "build_parse_sequence", "building_plan_parse", "blueprint_parse" },
                Tags = new List<string> { "buildings", "planning", "parse", "sequence", "blueprint", "建造", "规划", "解析" },
                Description = "兼容入口：请使用 building_control domain=planning action=parse_plan。把自然语言/文字序列解析成 prefabId、material、anchor query 等建造参数。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["plan"] = new McpToolParameter { Type = "string", Description = "建造文字序列，例如 粉砂岩砖@氧气、Wire-小型冰箱、用铜矿造手动发电机在电池旁", Required = false },
                    ["blueprint"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                    ["sequence"] = new McpToolParameter { Type = "string", Description = "plan 的别名；也可传有序文字序列，如 粉砂岩砖-梯子-电线@电池，返回 sequenceItems 逐项分类", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "可选已知建筑 prefabId；传入后只解析材料/锚点", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "可选已知建材；传入后不从文字中猜测材料", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选已知锚点搜索词；传入后不从文字中猜测锚点", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "候选返回数量，默认 5，最大 20", Required = false }
                },
                Handler = args =>
                {
                    try
                    {
                        var resolution = ResolveBuildPlan(args);
                        return CallToolResult.Text(JsonConvert.SerializeObject(resolution.ToDictionary(), McpJsonUtil.Settings));
                    }
                    catch (Exception ex)
                    {
                        return CallToolResult.Error("parse_plan failed: " + ex);
                    }
                }
            };
        }

        public static McpTool ListBuildMaterials()
        {
            return new McpTool
            {
                Name = "buildings_materials",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "building_materials", "build_materials" },
                Tags = new List<string> { "buildings", "materials", "inventory", "available", "建造", "材料" },
                Description = "兼容入口：请使用 building_control domain=planning action=materials",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 Outhouse、Tile、Wire", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；会包含关联世界库存", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "是否同时返回已发现但当前库存为 0 的候选，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 200", Required = false }
                },
                Handler = args =>
                {
                    var planResolution = ResolveBuildPlan(args);
                    if (args["prefabId"] == null && !string.IsNullOrWhiteSpace(planResolution.PrefabId))
                        args["prefabId"] = planResolution.PrefabId;
                    if (args["material"] == null && !string.IsNullOrWhiteSpace(planResolution.Material))
                        args["material"] = planResolution.Material;
                    if (args["query"] == null && args["target"] == null && args["search"] == null && !string.IsNullOrWhiteSpace(planResolution.AnchorQuery))
                        args["query"] = planResolution.AnchorQuery;

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required");
                    string resolvedPrefabId;
                    string resolveError;
                    var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
                    if (def == null)
                        return CallToolResult.Error(resolveError);
                    prefabId = resolvedPrefabId;

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeUnavailable = ToolUtil.GetBool(args, "includeUnavailable", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 200));
                    var materials = AvailableMaterials(def, worldId, includeUnavailable)
                        .Take(limit)
                        .Select(item => item.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = def.PrefabID,
                        ["name"] = ToolUtil.CleanName(def.Name),
                        ["worldId"] = worldId,
                        ["materialCategories"] = def.MaterialCategory,
                        ["resolvedMaterialCategories"] = MaterialCategoryTags(def).Select(tag => tag.Name).ToList(),
                        ["defaultMaterials"] = DefaultBuildElements(def).Select(tag => tag.Name).ToList(),
                        ["autoMaterial"] = materials.Count > 0 ? materials[0]["tag"] : (object)"unavailable",
                        ["autoMaterialReason"] = materials.Count > 0 ? null : "no currently available material in this world/inventory",
                        ["returned"] = materials.Count,
                        ["materials"] = materials
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PreviewBuild()
        {
            return new McpTool
            {
                Name = "build_preview",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "build_validate", "building_preview" },
                Tags = new List<string> { "buildings", "preview", "footprint", "placement", "建造", "预检" },
                Description = "兼容入口：请使用 building_control domain=planning action=preview",
                Parameters = BuildPlacementParameters(includeConfirm: false, includeArea: false),
                Handler = args =>
                {
                    string error;
                    int x;
                    int y;
                    if (!TryResolvePoint(args, out x, out y, out error))
                        return CallToolResult.Error(error);

                    var previewArgs = (JObject)args.DeepClone();
                    previewArgs["dryRun"] = true;
                    var result = TryPlanOne(args["prefabId"]?.ToString(), x, y, previewArgs);
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
