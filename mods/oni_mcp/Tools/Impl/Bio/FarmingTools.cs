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
    public static partial class FarmingTools
    {
        private const int CancelEvent = 2127324410;

        public static McpTool ListPlanting()
        {
            return new McpTool
            {
                Name = "farming_planting_list",
                Group = "farming",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "planters_list", "plants_list" },
                Tags = new List<string> { "farming", "plants", "planters", "seeds", "harvest" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=list。列出种植箱/农砖等 PlantablePlot 的种植请求、当前植物和可接受种子类型",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、请求种子或当前植物筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var plots = Components.BuildingCompletes.Items
                        .Select(building => building?.GetComponent<PlantablePlot>())
                        .Where(plot => plot != null && ToolUtil.GameObjectMatchesWorld(plot.gameObject, worldId))
                        .Where(plot => rect == null || CellInRect(Grid.PosToCell(plot.gameObject), rect, worldId))
                        .Where(plot => PlantingMatches(plot, query))
                        .OrderBy(plot => TargetName(plot.gameObject))
                        .Take(limit)
                        .Select(PlotInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = plots.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["plots"] = plots
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlPlanting()
        {
            return new McpTool
            {
                Name = "farming_planting_control",
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "planting_control", "planter_seed_control", "farm_plot_control" },
                Tags = new List<string> { "farming", "plants", "planters", "seeds", "harvest", "uproot", "batch" },
                Description = "统一读取/设置种植槽、种子目录、收获标记和区域铲除。action=list_planting/seed_catalog/list_harvestables/set_harvestable/set_planting/batch_set_planting/uproot；兼容 list/set/batch。",
                Parameters = RectParams(LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list_planting/list、seed_catalog/list_seeds/seeds、list_harvestables、set_harvestable、set_planting/set/plant、batch_set_planting/batch、uproot", Required = true, EnumValues = new List<string> { "list_planting", "list", "seed_catalog", "list_seeds", "seeds", "list_harvestables", "set_harvestable", "set_planting", "set", "plant", "batch_set_planting", "batch", "uproot" } },
                    ["seedTag"] = new McpToolParameter { Type = "string", Description = "set_planting/batch_set_planting 且 plantingAction=set 时的种子 prefab/tag，例如 BasicPlantSeed", Required = false },
                    ["mutationTag"] = new McpToolParameter { Type = "string", Description = "植物突变/亚种 tag；未使用突变时留空", Required = false },
                    ["plantingAction"] = new McpToolParameter { Type = "string", Description = "种植请求动作：set 或 cancel；避免与外层 action 冲突，也兼容 requestAction", Required = false, EnumValues = new List<string> { "set", "cancel" } },
                    ["requestAction"] = new McpToolParameter { Type = "string", Description = "plantingAction 的别名：set 或 cancel", Required = false, EnumValues = new List<string> { "set", "cancel" } },
                    ["harvestAction"] = new McpToolParameter { Type = "string", Description = "set_harvestable 的动作：mark、when_ready、cancel，默认 mark", Required = false, EnumValues = new List<string> { "mark", "when_ready", "cancel" } },
                    ["uprootAction"] = new McpToolParameter { Type = "string", Description = "uproot 的动作：mark 或 cancel，默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "list_harvestables 时只返回可收获对象；set_harvestable mark 时是否要求当前可收获", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "uproot 时铲除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "uproot 时是否设为红色最高优先级，默认 false", Required = false },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "action=batch 时只处理没有当前植物的种植槽，默认 true", Required = false },
                    ["removeOccupant"] = new McpToolParameter { Type = "boolean", Description = "若已有植物，是否先调用 OrderRemoveOccupant 铲除，默认 false", Required = false },
                    ["cropSeedOnly"] = new McpToolParameter { Type = "boolean", Description = "seed_catalog 时仅返回带 CropSeed 标签的候选种子；最终兼容性仍需具体 plot 验证", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按种植槽/植物名称、prefabId、当前植物或请求种子筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "list/batch 最多返回或处理数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "set 中 removeOccupant=true 时需要；batch 必须为 true；大区域 uproot 时必须为 true", Required = false }
                })),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list" || action == "list_planting")
                        return ListPlanting().Handler(args);
                    if (action == "seed_catalog" || action == "list_seeds" || action == "seeds")
                        return ListSeedCatalog().Handler(args);
                    if (action == "list_harvestables")
                        return ListHarvestables().Handler(args);

                    var delegated = (JObject)args.DeepClone();
                    if (action == "set_harvestable")
                    {
                        string harvestAction = args["harvestAction"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(harvestAction))
                            delegated["action"] = harvestAction.Trim();
                        else
                            delegated.Remove("action");
                        return SetHarvestable().Handler(delegated);
                    }
                    if (action == "uproot")
                    {
                        string uprootAction = args["uprootAction"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(uprootAction))
                            delegated["action"] = uprootAction.Trim();
                        else
                            delegated.Remove("action");
                        return UprootArea().Handler(delegated);
                    }

                    string plantingAction = args["plantingAction"]?.ToString() ?? args["requestAction"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(plantingAction))
                        delegated["action"] = plantingAction.Trim();
                    else
                        delegated.Remove("action");

                    if (action == "set" || action == "set_planting" || action == "plant")
                        return SetPlanting().Handler(delegated);
                    if (action == "batch" || action == "batch_set_planting")
                        return BatchSetPlanting().Handler(delegated);
                    return CallToolResult.Error("action must be one of list_planting, seed_catalog, list_harvestables, set_harvestable, set_planting, batch_set_planting, uproot");
                }
            };
        }

        public static McpTool ListHarvestables()
        {
            return new McpTool
            {
                Name = "farming_harvestables_list",
                Group = "farming",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "plants_harvestables_list", "harvestables_list" },
                Tags = new List<string> { "farming", "plants", "harvest", "when-ready" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=list_harvestables。列出可收获/可设置自动收获的植物和作物状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 筛选", Required = false },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回当前可收获对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool readyOnly = ToolUtil.GetBool(args, "readyOnly", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var harvestables = Components.HarvestDesignatables.Items
                        .Where(item => item != null && item.gameObject != null)
                        .Where(item => ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId))
                        .Where(item => rect == null || CellInRect(Grid.PosToCell(item.gameObject), rect, worldId))
                        .Where(item => !readyOnly || item.CanBeHarvested())
                        .Where(item => HarvestableMatches(item, query))
                        .OrderBy(item => TargetName(item.gameObject))
                        .Take(limit)
                        .Select(HarvestableInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = harvestables.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["readyOnly"] = readyOnly,
                        ["harvestables"] = harvestables
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetHarvestable()
        {
            return new McpTool
            {
                Name = "farming_harvestable_set",
                Group = "farming",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "plant_harvest_set", "harvestable_set" },
                Tags = new List<string> { "farming", "plants", "harvest", "when-ready" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=set_harvestable。按单个对象设置收获状态：mark、when_ready、cancel",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark、when_ready、cancel，默认 mark", Required = false, EnumValues = new List<string> { "mark", "when_ready", "cancel" } },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "action=mark 时是否要求当前可收获，默认 true", Required = false }
                }),
                Handler = args =>
                {
                    var harvestable = FindHarvestable(args);
                    if (harvestable == null)
                        return CallToolResult.Error("HarvestDesignatable target not found");

                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    if (action == "cancel")
                    {
                        harvestable.gameObject.Trigger(CancelEvent);
                        harvestable.SetHarvestWhenReady(false);
                    }
                    else if (action == "when_ready")
                    {
                        harvestable.SetHarvestWhenReady(true);
                    }
                    else
                    {
                        bool readyOnly = ToolUtil.GetBool(args, "readyOnly", true);
                        if (readyOnly && !harvestable.CanBeHarvested())
                            return CallToolResult.Error("Target is not ready to harvest; pass readyOnly=false or use action=when_ready");
                        harvestable.MarkForHarvest();
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(HarvestableInfo(harvestable), McpJsonUtil.Settings));
                }
            };
        }

    }
}
