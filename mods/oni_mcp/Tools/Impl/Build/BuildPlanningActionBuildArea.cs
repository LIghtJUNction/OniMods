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
        public static McpTool BuildArea()
        {
            var parameters = BuildPlacementParameters(includeConfirm: true, includeArea: true);
            parameters["anchors"] = new McpToolParameter { Type = "array", Description = "可选 anchor 列表。每项可为 {x,y} 或 [x,y]；优先于 x/y 与 x1/y1/x2/y2", Required = false };
            parameters["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；无 anchors 时生成 anchor 网格", Required = false };
            parameters["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；无 anchors 时生成 anchor 网格", Required = false };
            parameters["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；无 anchors 时生成 anchor 网格", Required = false };
            parameters["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；无 anchors 时生成 anchor 网格", Required = false };
            parameters["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；relative=true 时 x/y 或 x1/y1/x2/y2 按 rx/ry 解释", Required = false };
            parameters["relative"] = new McpToolParameter { Type = "boolean", Description = "配合 areaId 使用相对坐标", Required = false };
            parameters["dense"] = new McpToolParameter { Type = "boolean", Description = "无 anchors 时是否每格都放一个 anchor。默认：1x1 建筑为 true，多格建筑按 footprint 尺寸步进", Required = false };
            parameters["stepX"] = new McpToolParameter { Type = "integer", Description = "无 anchors 时的 anchor X 步长；默认 1 或建筑宽度", Required = false };
            parameters["stepY"] = new McpToolParameter { Type = "integer", Description = "无 anchors 时的 anchor Y 步长；默认 1 或建筑高度", Required = false };
            parameters["maxAnchors"] = new McpToolParameter { Type = "integer", Description = "最多处理多少个 anchors，默认 100，最大 500", Required = false };
            parameters["maxCommitAnchors"] = new McpToolParameter { Type = "integer", Description = "confirm=true 时单次最多实际写入多少个 anchor，默认 32，最大 128；超出会返回 remainingAction 供下一次继续，避免一次性大量蓝图/挖掘冲击游戏渲染。", Required = false };
            parameters["allowPartial"] = new McpToolParameter { Type = "boolean", Description = "默认 false。实际建造前若任何 anchor 预检失败则整批拒绝；true 时跳过失败 anchor", Required = false };
            parameters["autoConnectPower"] = new McpToolParameter { Type = "boolean", Description = "耗电建筑默认 true：放置建筑蓝图时同步从最近已有电线/电源输出接线到电口；传 false 可关闭", Required = false };
            parameters["maxAutoConnectRadius"] = new McpToolParameter { Type = "integer", Description = "autoConnectPower 搜索最近已有电线/电源输出的半径，默认 80，最大 200", Required = false };

            return new McpTool
            {
                Name = "build_area",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "build_place_area", "build_blueprints" },
                Tags = new List<string> { "buildings", "batch", "area", "footprint", "建造", "批量" },
                Description = "按 anchor 列表或区域批量放置建造蓝图。执行前会预检 footprint、支撑、材料和明显碰撞；遇到可挖自然固体时默认先标记挖掘，dryRun=true 只返回预览。",
                Parameters = parameters,
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false) && !ToolUtil.GetBool(args, "dryRun", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");

                    var planResolution = ResolveBuildPlan(args);
                    if (args["prefabId"] == null && !string.IsNullOrWhiteSpace(planResolution.PrefabId))
                        args["prefabId"] = planResolution.PrefabId;
                    if (args["material"] == null && !string.IsNullOrWhiteSpace(planResolution.Material))
                        args["material"] = planResolution.Material;
                    if (args["query"] == null && args["target"] == null && args["search"] == null && !string.IsNullOrWhiteSpace(planResolution.AnchorQuery))
                        args["query"] = planResolution.AnchorQuery;

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required, or provide plan/blueprint/sequence with a recognizable building name");
                string resolvedPrefabId;
                string resolveError;
                var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
                if (def == null)
                    return CallToolResult.Error(resolveError);
                prefabId = resolvedPrefabId;
                args["prefabId"] = prefabId;

                JObject connectArgs;
                if (IsLinearUtilityPrefab(def.PrefabID) && TryBuildAutoConnectArgs(args, def.PrefabID, out connectArgs))
                    return AutoConnectUtility().Handler(connectArgs);

                string error;
                var anchors = ResolveAnchors(args, def, out error);
                    if (error != null)
                        return CallToolResult.Error(error);
                    var anchorResolution = BuildAnchorResolution(args, anchors);

                    int maxAnchors = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxAnchors") ?? 100, 500));
                    if (anchors.Count > maxAnchors)
                        return CallToolResult.Error($"Refusing to process {anchors.Count} anchors; maxAnchors={maxAnchors}");

                    bool dryRun = IsDryRun(args);
            bool allowPartial = ToolUtil.GetBool(args, "allowPartial", false);
            int maxCommitAnchors = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCommitAnchors") ?? 32, 128));
                    var preflightArgs = (JObject)args.DeepClone();
                    preflightArgs["dryRun"] = true;
                    preflightArgs["confirm"] = true;

                    var preflightSupport = new HashSet<int>();
                    var previews = new List<Dictionary<string, object>>();
                    var validAnchors = new List<CellCoord>();
                    var actionableAnchors = new List<CellCoord>();
                    var autoDigAnchors = new List<CellCoord>();
                    foreach (var anchor in anchors)
                    {
                        var preview = TryPlanOne(prefabId, anchor.x, anchor.y, preflightArgs, preflightSupport);
                        bool valid = preview.ContainsKey("valid") && (bool)preview["valid"];
                        if (valid)
                        {
                            validAnchors.Add(anchor);
                            actionableAnchors.Add(anchor);
                        }
                        else if (IsAutoDiggableFailure(preview))
                        {
                            autoDigAnchors.Add(anchor);
                            actionableAnchors.Add(anchor);
                        }
                        previews.Add(preview);
                    }

                    var failedPreviews = previews.Where(item => !(item.ContainsKey("valid") && (bool)item["valid"])).ToList();
                    var hardFailedPreviews = previews.Where(item => !(item.ContainsKey("valid") && (bool)item["valid"]) && !IsAutoDiggableFailure(item)).ToList();
                    if (dryRun || (hardFailedPreviews.Count > 0 && !allowPartial))
                    {
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["prefabId"] = prefabId,
                            ["dryRun"] = dryRun,
                            ["committed"] = false,
                        ["valid"] = failedPreviews.Count == 0,
                        ["actionable"] = hardFailedPreviews.Count == 0,
                        ["anchorResolution"] = anchorResolution,
                        ["planResolution"] = planResolution.ToDictionary(),
                        ["anchorCount"] = anchors.Count,
                            ["validAnchors"] = validAnchors.Count,
                        ["autoDiggableAnchors"] = autoDigAnchors.Count,
                        ["failed"] = failedPreviews.Count,
                        ["hardFailed"] = hardFailedPreviews.Count,
                        ["next"] = failedPreviews.Count == 0
                            ? "Dry run passed. Re-run with confirm=true to place blueprints."
                            : hardFailedPreviews.Count == 0
                                ? "Only auto-diggable obstructions remain. Re-run with confirm=true to queue required dig/uproot and place blueprints."
                                : "Fix hard failures first; inspect errors[0].reasonCode/details before retrying.",
                        ["tokenHint"] = "Use valid/actionable/anchorResolution/planResolution/errors[0].reasonCode first; read previews only when choosing an alternate anchor.",
                        ["errors"] = failedPreviews.Take(50).ToList(),
                        ["previews"] = previews
                    }, McpJsonUtil.Settings));
                    }

                    var actualSupport = new HashSet<int>();
                    var autoDigContext = AutoDigContext.FromArgs(args);
                    var results = new List<Dictionary<string, object>>();
            var remainingAnchors = new List<CellCoord>();
            var requestedExecutionAnchors = allowPartial ? actionableAnchors : anchors;
            var executionAnchors = requestedExecutionAnchors.Take(maxCommitAnchors).ToList();
            remainingAnchors.AddRange(requestedExecutionAnchors.Skip(maxCommitAnchors));
                    int planned = 0;
                    int alreadyPresent = 0;
                    int alreadyConnected = 0;
                    int succeeded = 0;
                    int failed = 0;
                    int autoDigQueued = 0;
                    int autoDigAlreadyMarked = 0;
            foreach (var anchor in executionAnchors)
                    {
                        var result = TryPlanOne(prefabId, anchor.x, anchor.y, args, actualSupport, autoDigContext);
                        bool wasPlanned = GetBool(result, "planned");
                        bool wasAlreadyPresent = GetBool(result, "alreadyPresent");
                        bool wasAlreadyConnected = GetBool(result, "alreadyConnected");
                        bool isAutoDig = IsAutoDigResult(result);
                        bool ok = wasPlanned || wasAlreadyPresent || wasAlreadyConnected || isAutoDig;
                        autoDigQueued += GetAutoDigInt(result, "marked");
                        autoDigAlreadyMarked += GetAutoDigInt(result, "alreadyMarked");
                        if (wasPlanned && !wasAlreadyPresent && !wasAlreadyConnected)
                            planned++;
                        if (wasAlreadyPresent)
                            alreadyPresent++;
                        if (wasAlreadyConnected)
                            alreadyConnected++;
                        if (ok)
                            succeeded++;
                        else if (!isAutoDig)
                        {
                            failed++;
                            remainingAnchors.Add(anchor);
                        }
                        if (!wasPlanned && !wasAlreadyPresent && !wasAlreadyConnected && isAutoDig)
                            remainingAnchors.Add(anchor);
                        results.Add(result);
                    }

            if (allowPartial && hardFailedPreviews.Count > 0)
                remainingAnchors.AddRange(anchors.Where(anchor => hardFailedPreviews.Any(item => SameAnchor(item, anchor))));

            bool throttled = requestedExecutionAnchors.Count > executionAnchors.Count;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = prefabId,
                        ["dryRun"] = false,
                        ["committed"] = planned > 0 || autoDigQueued > 0,
                        ["allowPartial"] = allowPartial,
                        ["anchorResolution"] = anchorResolution,
                        ["planResolution"] = planResolution.ToDictionary(),
                        ["anchorCount"] = anchors.Count,
                ["attempted"] = executionAnchors.Count,
                ["maxCommitAnchors"] = maxCommitAnchors,
                ["throttled"] = throttled,
                ["deferred"] = requestedExecutionAnchors.Count - executionAnchors.Count,
                        ["succeeded"] = succeeded,
                        ["planned"] = planned,
                        ["alreadyPresent"] = alreadyPresent,
                        ["alreadyConnected"] = alreadyConnected,
                        ["autoDigQueued"] = autoDigQueued,
                        ["autoDigAlreadyMarked"] = autoDigAlreadyMarked,
                        ["autoDigLimitReached"] = autoDigContext.LimitReached,
                        ["pendingBuildAfterDig"] = autoDigQueued + autoDigAlreadyMarked,
                        ["failed"] = failed + (allowPartial ? hardFailedPreviews.Count : 0),
                ["allRequestedSatisfied"] = !throttled && planned + alreadyPresent + alreadyConnected >= anchors.Count,
                        ["remainingAnchors"] = remainingAnchors.Select(anchor => AnchorDictionary(anchor.x, anchor.y, ToolUtil.ResolveWorldId(args))).ToList(),
                ["remainingAction"] = BuildRemainingBuildAreaAction(args, prefabId, remainingAnchors),
                ["next"] = throttled
                    ? "Batch was throttled for game stability. Execute remainingAction after the game processes this batch."
                    : remainingAnchors.Count > 0
                        ? "Some anchors remain blocked or pending auto-dig. Inspect remainingAction/preflightErrors."
                        : "Batch complete.",
                        ["preflightErrors"] = hardFailedPreviews.Take(50).ToList(),
                        ["results"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
