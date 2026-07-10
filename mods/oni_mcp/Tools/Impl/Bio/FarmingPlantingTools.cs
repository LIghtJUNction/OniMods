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
                    // Live bug: PlanterBox.possibleDepositObjectTags is often [CropSeed], while
                    // HasDepositTag(BasicSingleHarvestPlantSeed) checks the seed prefab tag itself
                    // and returns false even when the seed carries CropSeed and IsValidEntity is true.
                    bool hasDeposit = SeedMatchesPlotDepositTags(plot, seedPrefab, seedTag);
                    bool validEntity = plot.IsValidEntity(seedPrefab);
                    if (!hasDeposit || !validEntity)
                    {
                        var plotInfo = PlotInfo(plot);
                        var seedInfo = SeedInfo(seed);
                        return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["error"] = "Seed is not valid for this planter direction/type",
                            ["hasDepositTag"] = hasDeposit,
                            ["isValidEntity"] = validEntity,
                            ["seed"] = seedInfo,
                            ["plot"] = plotInfo,
                            ["hint"] = "Use seed_catalog (cropSeedOnly=true). Deposit checks use seed tags (e.g. CropSeed), not only the seed prefab id."
                        }, McpJsonUtil.Settings));
                    }

                    var mutationTag = string.IsNullOrWhiteSpace(args["mutationTag"]?.ToString())
                        ? Tag.Invalid
                        : TagManager.Create(args["mutationTag"].ToString().Trim());
                    float worldInventoryAmount = AvailableSeedAmount(plot.gameObject, seedTag);
                    plot.CancelActiveRequest();
                    plot.CreateOrder(seedTag, mutationTag);
                    var after = PlotInfo(plot);
                    bool requestedPlantingMatches = RequestedPlantingMatches(plot, seedTag, mutationTag);
                    bool requestActive = plot.GetActiveRequest != null;
                    if (!requestedPlantingMatches)
                    {
                        return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["error"] = "CreateOrder did not establish the requested seed and active planting request",
                            ["seedTag"] = seedTag.Name,
                            ["mutationTag"] = mutationTag.IsValid ? mutationTag.Name : null,
                            ["requestedPlantingMatches"] = requestedPlantingMatches,
                            ["hasActiveRequest"] = requestActive,
                            ["hasDepositTag"] = hasDeposit,
                            ["isValidEntity"] = validEntity,
                            ["worldInventoryAmount"] = Math.Round(worldInventoryAmount, 3),
                            ["inventoryNote"] = "World inventory amount is informational only; it does not prove fetchability or explain CreateOrder outcome.",
                            ["seed"] = SeedInfo(seed),
                            ["plot"] = after,
                            ["hint"] = "Inspect plot.validPreview, requestedSeed, hasActiveRequest, occupant, and acceptedSeedTags; validate the exact seed against this plot before retrying."
                        }, McpJsonUtil.Settings));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(after, McpJsonUtil.Settings));
                }
            };
        }

    }
}
