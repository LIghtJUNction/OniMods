using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        public static McpTool DigArea()
        {
            return new McpTool
            {
                Name = "orders_dig_area",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Description = "兼容入口：请优先使用 orders_control domain=area action=dig。在矩形区域内对自然实体格子下达挖掘命令。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只预检并返回 preview，不实际下达挖掘，默认 false；dryRun 不要求 confirm", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "是否返回逐格坐标样本，默认 false；通常先看 execution 摘要", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "挖掘任务优先级 1~9，默认 5", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "preview.targets 最多返回数量，默认 300，最大 1000", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；dryRun=false 时必须为 true", Required = false },
                    ["previewToken"] = new McpToolParameter { Type = "string", Description = "dryRun 返回的预览令牌；提供后可省略重复参数", Required = false }
                }),
                Handler = args =>
                {
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    string previewToken = args["previewToken"]?.ToString();
                    if (!dryRun && !string.IsNullOrEmpty(previewToken))
                    {
                        var cachedArgs = PreviewTokenRegistry.Get(previewToken);
                        if (cachedArgs == null)
                            return CallToolResult.Error("Preview token expired or invalid; run dryRun=true first");
                        args = cachedArgs;
                        args["confirm"] = true;
                        dryRun = false;
                    }

                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for dig orders");
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    if (!dryRun && DigTool.Instance == null)
                        return CallToolResult.Error("DigTool is not initialized; open a loaded colony UI before issuing dig orders");

                    var rect = ToolUtil.GetRect(args);
                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool detail = ToolUtil.GetBool(args, "detail", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 300, 1000));
                    int marked = 0;
                    int existingUpdated = 0;
                    int priorityApplied = 0;
                    int priorityFailed = 0;
                    int requestedPriority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
                    int dist = 0;
                    double kgTotal = 0;
                    var targets = new List<Dictionary<string, object>>();
                    var targetCells = new List<int>();
                    var skipped = new Dictionary<string, int>();
                    var riskBuilder = new DigRiskBuilder();
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            {
                                IncrementSkip(skipped, "invalid_or_wrong_world");
                                continue;
                            }
                            if (!Grid.IsVisible(cell))
                            {
                                IncrementSkip(skipped, "not_visible");
                                continue;
                            }
                            if (!Grid.Solid[cell])
                            {
                                IncrementSkip(skipped, "not_solid");
                                continue;
                            }
                            if (Grid.Foundation[cell])
                            {
                                IncrementSkip(skipped, "foundation_or_constructed_tile");
                                continue;
                            }
                            var existingDig = Grid.Objects[cell, (int)ObjectLayer.DigPlacer];
                            if (existingDig != null)
                            {
                                targetCells.Add(cell);
                                if (detail && targets.Count < limit)
                                    targets.Add(DigTarget(cell, x, y, dryRun ? "would_update_priority" : "priority_updated"));
                                marked++;
                                if (!dryRun)
                                {
                                    ApplyPriority(existingDig, args);
                                    var existingPriority = existingDig.GetComponent<Prioritizable>();
                                    if (existingPriority != null && existingPriority.GetMasterPriority().priority_value == requestedPriority)
                                    {
                                        priorityApplied++;
                                        existingUpdated++;
                                    }
                                    else
                                    {
                                        priorityFailed++;
                                    }
                                }
                                continue;
                            }

                            kgTotal += ToolUtil.SafeFloat(Grid.Mass[cell]);
                            riskBuilder.ScanTarget(cell, x, y, worldId);
                            targetCells.Add(cell);
                            if (detail && targets.Count < limit)
                                targets.Add(DigTarget(cell, x, y, dryRun ? "would_dig" : "marked"));

                            if (dryRun)
                            {
                                marked++;
                                continue;
                            }

                            var digPlacer = DigTool.PlaceDig(cell, dist++);
                            if (digPlacer != null)
                            {
                                marked++;
                                ApplyPriority(digPlacer, args);
                                var prioritizable = digPlacer.GetComponent<Prioritizable>();
                                if (prioritizable != null && prioritizable.GetMasterPriority().priority_value == requestedPriority)
                                    priorityApplied++;
                                else
                                    priorityFailed++;
                            }
                        }
                    }

                    var execution = DigExecutionMetadata(rect, worldId, targetCells, skipped, detail, limit);
                    var responseDict = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["marked"] = marked,
                        ["existingUpdated"] = dryRun ? 0 : existingUpdated,
                        ["requestedPriority"] = requestedPriority,
                        ["priorityApplied"] = dryRun ? 0 : priorityApplied,
                        ["priorityFailed"] = dryRun ? 0 : priorityFailed,
                        ["priorityVerified"] = dryRun ? (object)null : priorityFailed == 0 && priorityApplied == marked,
                        ["wouldMark"] = dryRun ? marked : (object)null,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["execution"] = execution,
                        ["preview"] = new Dictionary<string, object>
                        {
                            ["targets"] = targets,
                            ["targetCount"] = marked,
                            ["kgTotal"] = Math.Round(kgTotal, 3),
                            ["risks"] = riskBuilder.ToList()
                        },
                        ["truncatedTargets"] = Math.Max(0, marked - targets.Count)
                    };
                    if (dryRun)
                        responseDict["previewToken"] = PreviewTokenRegistry.Register(args);
                    string response = JsonConvert.SerializeObject(responseDict, McpJsonUtil.Settings);
                    return !dryRun && priorityFailed > 0 ? CallToolResult.Error(response) : CallToolResult.Text(response);
                }
            };
        }

        private static Dictionary<string, object> DigTarget(int cell, int x, int y, string status)
        {
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["x"] = x,
                ["y"] = y,
                ["cell"] = cell,
                ["element"] = Grid.IsValidCell(cell) ? Grid.Element[cell].id.ToString() : "",
                ["kg"] = Grid.IsValidCell(cell) ? Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3) : 0,
                ["temperatureC"] = Grid.IsValidCell(cell) ? Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f, 1) : 0
            };
        }

        private static Dictionary<string, object> DigExecutionMetadata(
            Dictionary<string, int> rect,
            int worldId,
            List<int> targetCells,
            Dictionary<string, int> skipped,
            bool detail,
            int limit)
        {
            var navigators = ActiveNavigators(worldId);
            int reachableTargets = 0;
            var unreachableSamples = new List<Dictionary<string, object>>();
            var reachableSamples = new List<Dictionary<string, object>>();
            int sampleLimit = Math.Min(limit, 12);

            foreach (int cell in targetCells)
            {
                int workCell;
                bool reachable = TryFindReachableDigWorkCell(cell, worldId, navigators, out workCell);
                if (reachable)
                {
                    reachableTargets++;
                    if (detail && reachableSamples.Count < sampleLimit)
                        reachableSamples.Add(DigReachabilitySample(cell, workCell, "reachable"));
                }
                else if (detail && unreachableSamples.Count < sampleLimit)
                {
                    unreachableSamples.Add(DigReachabilitySample(cell, -1, "unreachable"));
                }
            }

            int unreachableTargets = Math.Max(0, targetCells.Count - reachableTargets);
            bool hasNavigators = navigators.Count > 0;
            string status = targetCells.Count == 0
                ? "no_diggable_targets"
                : !hasNavigators
                    ? "unknown_no_active_navigators"
                    : unreachableTargets == 0
                        ? "all_targets_reachable"
                        : reachableTargets == 0
                            ? "no_targets_reachable"
                            : "partially_reachable";

            var result = new Dictionary<string, object>
            {
                ["status"] = status,
                ["canIssueDig"] = targetCells.Count > 0,
                ["hasActiveNavigators"] = hasNavigators,
                ["navigatorCount"] = navigators.Count,
                ["diggableTargets"] = targetCells.Count,
                ["reachableTargets"] = reachableTargets,
                ["unreachableTargets"] = unreachableTargets,
                ["allTargetsReachable"] = hasNavigators && targetCells.Count > 0 && unreachableTargets == 0,
                ["anyTargetReachable"] = hasNavigators && reachableTargets > 0,
                ["skipped"] = skipped,
                ["tokenHint"] = "Use status/reachableTargets/unreachableTargets first; request detail=true only when samples are needed."
            };

            if (detail)
            {
                result["reachableSamples"] = reachableSamples;
                result["unreachableSamples"] = unreachableSamples;
                result["truncatedReachabilitySamples"] = Math.Max(0, targetCells.Count - reachableSamples.Count - unreachableSamples.Count);
            }

            return result;
        }

        private static Dictionary<string, object> CellExecutionMetadata(
            string action,
            int worldId,
            List<int> targetCells,
            Dictionary<string, int> skipped,
            bool detail,
            int limit)
        {
            var navigators = ActiveNavigators(worldId);
            int reachableTargets = 0;
            var reachableSamples = new List<Dictionary<string, object>>();
            var unreachableSamples = new List<Dictionary<string, object>>();
            int sampleLimit = Math.Min(limit, 12);

            foreach (int cell in targetCells)
            {
                int workCell;
                bool reachable = TryFindReachableWorkCell(cell, worldId, navigators, out workCell);
                if (reachable)
                {
                    reachableTargets++;
                    if (detail && reachableSamples.Count < sampleLimit)
                        reachableSamples.Add(ReachabilitySample(cell, workCell, "reachable"));
                }
                else if (detail && unreachableSamples.Count < sampleLimit)
                {
                    unreachableSamples.Add(ReachabilitySample(cell, -1, "unreachable"));
                }
            }

            int targetCount = targetCells.Count;
            int unreachableTargets = Math.Max(0, targetCount - reachableTargets);
            bool hasNavigators = navigators.Count > 0;
            string status = targetCount == 0
                ? "no_targets"
                : !hasNavigators
                    ? "unknown_no_active_navigators"
                    : unreachableTargets == 0
                        ? "all_targets_reachable"
                        : reachableTargets == 0
                            ? "no_targets_reachable"
                            : "partially_reachable";

            var result = new Dictionary<string, object>
            {
                ["action"] = action,
                ["status"] = status,
                ["hasActiveNavigators"] = hasNavigators,
                ["navigatorCount"] = navigators.Count,
                ["targetCount"] = targetCount,
                ["reachableTargets"] = reachableTargets,
                ["unreachableTargets"] = unreachableTargets,
                ["allTargetsReachable"] = hasNavigators && targetCount > 0 && unreachableTargets == 0,
                ["anyTargetReachable"] = hasNavigators && reachableTargets > 0,
                ["skipped"] = skipped ?? new Dictionary<string, int>(),
                ["tokenHint"] = "Use status/reachableTargets/unreachableTargets first; request detail=true only when samples are needed."
            };

            if (detail)
            {
                result["reachableSamples"] = reachableSamples;
                result["unreachableSamples"] = unreachableSamples;
                result["truncatedReachabilitySamples"] = Math.Max(0, targetCount - reachableSamples.Count - unreachableSamples.Count);
            }

            return result;
        }
}
}
