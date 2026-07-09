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
    public static class FarmingTools
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

        public static McpTool ListSeedCatalog()
        {
            return new McpTool
            {
                Name = "farming_seed_catalog",
                Group = "farming",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "plantable_seeds_list", "seeds_catalog" },
                Tags = new List<string> { "farming", "plants", "seeds", "catalog" },
                Description = "兼容入口：请优先使用 farming_planting_control action=seed_catalog。列出 PlantableSeed prefab 及其标签事实；planterBoxCandidate 仅表示 CropSeed 候选，最终兼容性必须由具体 plot 的 IsValidEntity/预览状态验证。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按种子、植物或显示名筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false },
                    ["cropSeedOnly"] = new McpToolParameter { Type = "boolean", Description = "仅返回带 CropSeed 标签的候选种子，默认 false；不代表与任意具体种植槽兼容", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    bool cropSeedOnly = ToolUtil.GetBool(args, "cropSeedOnly", false);
                    var seeds = Assets.Prefabs
                        .Where(prefab => prefab != null && prefab.gameObject != null)
                        .Select(prefab => prefab.GetComponent<PlantableSeed>())
                        .Where(seed => seed != null)
                        .Where(seed => SeedMatches(seed, query))
                        .Where(seed => !cropSeedOnly || HasCropSeedTag(seed))
                        .OrderBy(seed => TargetName(seed.gameObject))
                        .Take(limit)
                        .Select(SeedInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = seeds.Count,
                        ["cropSeedOnly"] = cropSeedOnly,
                        ["hint"] = "cropSeedOnly/planterBoxCandidate only report the CropSeed tag. Validate the chosen seed against the concrete plot with set_planting diagnostics or a plot preview.",
                        ["seeds"] = seeds
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetPlanting()
        {
            return new McpTool
            {
                Name = "farming_planting_set",
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "planter_set_seed", "plant_seed_select" },
                Tags = new List<string> { "farming", "plants", "planters", "seeds" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=set。设置或取消种植箱/农砖的种子请求；可选 removeOccupant=true 先铲除当前植物",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["seedTag"] = new McpToolParameter { Type = "string", Description = "种子 prefab/tag，例如 BasicPlantSeed；action=set 时必填", Required = false },
                    ["mutationTag"] = new McpToolParameter { Type = "string", Description = "植物突变/亚种 tag；未使用突变时留空", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "set 或 cancel，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel" } },
                    ["removeOccupant"] = new McpToolParameter { Type = "boolean", Description = "若已有植物，是否先调用 OrderRemoveOccupant 铲除，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "removeOccupant=true 时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var plot = FindPlot(args);
                    if (plot == null)
                        return CallToolResult.Error("PlantablePlot target not found");

                    string action = (args["action"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    if (action == "cancel")
                    {
                        plot.CancelActiveRequest();
                        return CallToolResult.Text(JsonConvert.SerializeObject(PlotInfo(plot), McpJsonUtil.Settings));
                    }

                    bool removeOccupant = ToolUtil.GetBool(args, "removeOccupant", false);
                    if (plot.Occupant != null)
                    {
                        if (!removeOccupant)
                            return CallToolResult.Error("Plot already has an occupant; pass removeOccupant=true and confirm=true to uproot first");
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required when removeOccupant=true");
                        plot.OrderRemoveOccupant();
                    }

                    string seedName = args["seedTag"]?.ToString();
                    if (string.IsNullOrWhiteSpace(seedName))
                        return CallToolResult.Error("seedTag is required for action=set");
                    var seedTag = TagManager.Create(seedName.Trim());
                    var seedPrefab = Assets.GetPrefab(seedTag);
                    var seed = seedPrefab == null ? null : seedPrefab.GetComponent<PlantableSeed>();
                    if (seed == null)
                        return CallToolResult.Error("seedTag is not a PlantableSeed prefab");
                    if (!plot.HasDepositTag(seedTag) || !plot.IsValidEntity(seedPrefab))
                        return CallToolResult.Error("Seed is not valid for this planter direction/type");

                    var mutationTag = string.IsNullOrWhiteSpace(args["mutationTag"]?.ToString())
                        ? Tag.Invalid
                        : TagManager.Create(args["mutationTag"].ToString().Trim());
                    plot.CancelActiveRequest();
                    plot.CreateOrder(seedTag, mutationTag);

                    return CallToolResult.Text(JsonConvert.SerializeObject(PlotInfo(plot), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool BatchSetPlanting()
        {
            return new McpTool
            {
                Name = "farming_planting_batch_set",
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "planters_batch_set_seed", "plant_seed_area_set" },
                Tags = new List<string> { "farming", "plants", "planters", "seeds", "batch" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=batch。按区域批量设置或取消种植请求；适合一次给多块农砖/种植箱安排同一种种子",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["seedTag"] = new McpToolParameter { Type = "string", Description = "种子 prefab/tag，例如 BasicPlantSeed；action=set 时必填", Required = false },
                    ["mutationTag"] = new McpToolParameter { Type = "string", Description = "植物突变/亚种 tag；未使用突变时留空", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "set 或 cancel，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel" } },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "只处理没有当前植物的种植槽，默认 true", Required = false },
                    ["removeOccupant"] = new McpToolParameter { Type = "boolean", Description = "若已有植物，是否先调用 OrderRemoveOccupant 铲除，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按种植槽名称、prefabId、当前植物或请求种子筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true；区域超过 100 格或 removeOccupant=true 时也必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to batch change planting requests");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing planting requests in more than 100 cells");

                    string action = (args["action"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    bool isSet = action == "set";
                    if (!isSet && action != "cancel")
                        return CallToolResult.Error("action must be set or cancel");

                    Tag seedTag = Tag.Invalid;
                    GameObject seedPrefab = null;
                    if (isSet)
                    {
                        string seedName = args["seedTag"]?.ToString();
                        if (string.IsNullOrWhiteSpace(seedName))
                            return CallToolResult.Error("seedTag is required for action=set");
                        seedTag = TagManager.Create(seedName.Trim());
                        seedPrefab = Assets.GetPrefab(seedTag);
                        if (seedPrefab == null || seedPrefab.GetComponent<PlantableSeed>() == null)
                            return CallToolResult.Error("seedTag is not a PlantableSeed prefab");
                    }

                    var mutationTag = string.IsNullOrWhiteSpace(args["mutationTag"]?.ToString())
                        ? Tag.Invalid
                        : TagManager.Create(args["mutationTag"].ToString().Trim());
                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool emptyOnly = ToolUtil.GetBool(args, "emptyOnly", true);
                    bool removeOccupant = ToolUtil.GetBool(args, "removeOccupant", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    var changed = new List<Dictionary<string, object>>();
                    foreach (var plot in Components.BuildingCompletes.Items
                                 .Select(building => building?.GetComponent<PlantablePlot>())
                                 .Where(plot => plot != null && ToolUtil.GameObjectMatchesWorld(plot.gameObject, worldId))
                                 .Where(plot => CellInRect(Grid.PosToCell(plot.gameObject), rect, worldId))
                                 .Where(plot => PlantingMatches(plot, query))
                                 .Take(limit))
                    {
                        if (!isSet)
                        {
                            plot.CancelActiveRequest();
                            changed.Add(PlotInfo(plot));
                            continue;
                        }

                        if (!plot.HasDepositTag(seedTag) || !plot.IsValidEntity(seedPrefab))
                            continue;
                        if (plot.Occupant != null)
                        {
                            if (emptyOnly)
                                continue;
                            if (!removeOccupant)
                                continue;
                            plot.OrderRemoveOccupant();
                        }
                        plot.CancelActiveRequest();
                        plot.CreateOrder(seedTag, mutationTag);
                        changed.Add(PlotInfo(plot));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["seedTag"] = seedTag.IsValid ? seedTag.Name : null,
                        ["changed"] = changed.Count,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["plots"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool UprootArea()
        {
            return new McpTool
            {
                Name = "plants_uproot_area",
                Group = "farming",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "farming_uproot_area", "plants_cancel_uproot" },
                Tags = new List<string> { "farming", "plants", "uproot", "harvest" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=uproot。按区域标记或取消铲除植物",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark 或 cancel，默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "铲除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true；mark 操作建议传 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing uproot orders in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool mark = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant() != "cancel";
                    int changed = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var uprootable in Components.Uprootables.Items)
                    {
                        var go = uprootable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (!CellInRect(cell, rect, worldId))
                            continue;

                        if (mark)
                        {
                            if (!uprootable.CanUproot())
                                continue;
                            uprootable.MarkForUproot();
                            ApplyPriority(go, args);
                            results.Add(TargetInfo(go, "marked"));
                        }
                        else
                        {
                            if (!uprootable.IsMarkedForUproot)
                                continue;
                            uprootable.ForceCancelUproot();
                            go.Trigger(CancelEvent);
                            results.Add(TargetInfo(go, "cancelled"));
                        }
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = mark ? "mark" : "cancel",
                        ["changed"] = changed,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false },
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

        private static PlantablePlot FindPlot(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                var plot = go == null ? null : go.GetComponent<PlantablePlot>();
                if (plot == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return plot;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return plot;
            }
            return null;
        }

        private static HarvestDesignatable FindHarvestable(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var harvestable in Components.HarvestDesignatables.Items)
            {
                var go = harvestable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return harvestable;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return harvestable;
            }
            return null;
        }

        private static Dictionary<string, object> PlotInfo(PlantablePlot plot)
        {
            var result = TargetInfo(plot.gameObject, null);
            result["requestedSeed"] = plot.requestedEntityTag.IsValid ? plot.requestedEntityTag.Name : null;
            result["requestedMutation"] = plot.requestedEntityAdditionalFilterTag.IsValid ? plot.requestedEntityAdditionalFilterTag.Name : null;
            result["hasActiveRequest"] = plot.GetActiveRequest != null;
            result["validPreview"] = plot.ValidPlant;
            result["acceptsFertilizer"] = plot.AcceptsFertilizer;
            result["acceptsIrrigation"] = plot.AcceptsIrrigation;
            result["occupant"] = plot.Occupant == null ? null : TargetInfo(plot.Occupant, null);
            result["acceptedSeedTags"] = plot.possibleDepositObjectTags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            return result;
        }

        private static Dictionary<string, object> HarvestableInfo(HarvestDesignatable harvestable)
        {
            var result = TargetInfo(harvestable.gameObject, null);
            result["canBeHarvested"] = harvestable.CanBeHarvested();
            result["markedForHarvest"] = harvestable.MarkedForHarvest;
            result["harvestWhenReady"] = harvestable.HarvestWhenReady;
            result["inPlanterBox"] = harvestable.InPlanterBox;
            var harvestableComponent = harvestable.GetComponent<Harvestable>();
            if (harvestableComponent != null)
                result["harvestableComponent"] = harvestableComponent.GetType().Name;
            return result;
        }

        private static Dictionary<string, object> SeedInfo(PlantableSeed seed)
        {
            var kpid = seed.GetComponent<KPrefabID>();
            var seedGo = seed.gameObject;
            var seedTag = kpid?.PrefabTag ?? Tag.Invalid;
            // Tags agents need when matching list_planting.acceptedSeedTags (e.g. CropSeed).
            var depositTags = new List<string>();
            if (kpid != null)
            {
                foreach (var tag in kpid.Tags)
                {
                    if (tag.IsValid && !string.IsNullOrEmpty(tag.Name))
                        depositTags.Add(tag.Name);
                }
                depositTags = depositTags.Distinct().OrderBy(name => name).ToList();
            }

            // Live play: agents saw acceptedSeedTags=["CropSeed"] but seed_catalog only
            // returned seedTag/plantId, so they tried BasicSingleHarvestPlantSeed and got
            // "Seed is not valid for this planter direction/type" with no catalog guidance.
            return new Dictionary<string, object>
            {
                ["seedTag"] = seedTag.IsValid ? seedTag.Name : seedGo.name,
                ["name"] = TargetName(seedGo),
                ["plantId"] = seed.PlantID.Name,
                ["previewId"] = seed.PreviewID.Name,
                ["direction"] = seed.Direction.ToString(),
                ["replantGroundTag"] = seed.replantGroundTag.IsValid ? seed.replantGroundTag.Name : null,
                ["seedTags"] = depositTags,
                ["hasCropSeedTag"] = depositTags.Any(tag => tag == "CropSeed"),
                ["isCropSeed"] = depositTags.Any(tag => tag == "CropSeed"),
                ["planterBoxCandidate"] = depositTags.Any(tag => tag == "CropSeed")
            };
        }

        private static bool PlantingMatches(PlantablePlot plot, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(TargetName(plot.gameObject), q)
                || Contains(plot.gameObject.GetComponent<KPrefabID>()?.PrefabTag.Name ?? plot.gameObject.name, q)
                || Contains(plot.requestedEntityTag.Name, q)
                || Contains(plot.Occupant?.GetProperName(), q)
                || Contains(plot.Occupant?.GetComponent<KPrefabID>()?.PrefabTag.Name, q);
        }

        private static bool HarvestableMatches(HarvestDesignatable harvestable, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = harvestable.gameObject;
            return Contains(TargetName(go), q)
                || Contains(go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name, q);
        }

        private static bool SeedMatches(PlantableSeed seed, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var kpid = seed.GetComponent<KPrefabID>();
            return Contains(TargetName(seed.gameObject), q)
                || Contains(kpid?.PrefabTag.Name ?? seed.gameObject.name, q)
                || Contains(seed.PlantID.Name, q)
                || Contains(seed.PreviewID.Name, q)
                || (kpid != null && kpid.Tags.Any(tag => Contains(tag.Name, q)));
        }

        private static bool HasCropSeedTag(PlantableSeed seed)
        {
            var kpid = seed?.GetComponent<KPrefabID>();
            return kpid != null && kpid.Tags.Any(tag => tag.Name == "CropSeed");
        }

        private static void ApplyPriority(GameObject go, JObject args)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;
            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            prioritizable.SetMasterPriority(new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority));
        }

        private static Dictionary<string, object> TargetInfo(GameObject go, string status)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = TargetName(go),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
            if (!string.IsNullOrEmpty(status))
                result["status"] = status;
            return result;
        }

        private static string TargetName(GameObject go)
        {
            return ToolUtil.CleanName(go.GetProperName());
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

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
