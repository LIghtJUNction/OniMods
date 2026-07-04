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
        public static McpTool HarvestArea()
        {
            return new McpTool
            {
                Name = "orders_harvest_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "harvest_area", "plants_harvest_area" },
                Description = "兼容入口：请优先使用 orders_control domain=area action=harvest。标记、取消或设置区域内植物/可收获对象的收获命令；mode=mark 仅标记当前可收获，when_ready 设置成熟即收获",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "mark、when_ready、cancel；默认 mark", Required = false, EnumValues = new List<string> { "mark", "when_ready", "cancel" } },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "mark 模式是否只处理当前可收获对象，默认 true", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只返回会处理/跳过的目标和 skipReasons，不实际修改；dryRun 不要求 confirm", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false },
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

                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (!dryRun && cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing harvest orders in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string mode = (args["mode"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    bool readyOnly = ToolUtil.GetBool(args, "readyOnly", true);
                    int changed = 0;
                    int skipped = 0;
                    int matched = 0;
                    int notReady = 0;
                    var results = new List<Dictionary<string, object>>();
                    var targetCells = new List<int>();
                    var executionSkipped = new Dictionary<string, int>();

                    foreach (var harvestable in Components.HarvestDesignatables.Items)
                    {
                        var go = harvestable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (!CellInRect(cell, rect, worldId))
                            continue;

                        matched++;
                        if (mode == "cancel")
                        {
                            if (!dryRun)
                            {
                                go.Trigger(CancelEvent);
                                harvestable.SetHarvestWhenReady(false);
                            }
                            changed++;
                            targetCells.Add(cell);
                            results.Add(ObjectResult(go, dryRun ? "would_cancel" : "cancelled"));
                        }
                        else if (mode == "when_ready")
                        {
                            if (!dryRun)
                                harvestable.SetHarvestWhenReady(true);
                            changed++;
                            targetCells.Add(cell);
                            results.Add(ObjectResult(go, dryRun ? "would_when_ready" : "when_ready"));
                        }
                        else
                        {
                            bool canHarvest = harvestable.CanBeHarvested();
                            if (readyOnly && !canHarvest)
                            {
                                notReady++;
                                skipped++;
                                IncrementSkip(executionSkipped, "not_ready");
                                results.Add(ObjectResult(go, "skipped_not_ready"));
                                continue;
                            }
                            if (!dryRun)
                                harvestable.MarkForHarvest();
                            changed++;
                            targetCells.Add(cell);
                            results.Add(ObjectResult(go, dryRun ? "would_mark" : "marked"));
                        }
                    }

                    var responseDict = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["mode"] = mode == "when_ready" || mode == "cancel" ? mode : "mark",
                        ["changed"] = changed,
                        ["matched"] = matched,
                        ["skipped"] = skipped,
                        ["skipReasons"] = HarvestSkipReasons(matched, notReady, skipped),
                        ["execution"] = CellExecutionMetadata("harvest", worldId, targetCells, executionSkipped, false, 200),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    };
                    if (dryRun)
                        responseDict["previewToken"] = PreviewTokenRegistry.Register(args);
                    return CallToolResult.Text(JsonConvert.SerializeObject(responseDict, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CaptureCritters()
        {
            return new McpTool
            {
                Name = "critters_capture",
                Hidden = true,
                Group = "ranching",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "orders_capture", "critters_wrangle" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=capture。按对象或区域标记/取消抓捕小动物；action=release 可释放已捆绑小动物",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；提供后优先于区域", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark、cancel、release；默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel", "release" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "抓捕差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true；release 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    if (action == "release" && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for releasing critters");

                    int? id = ToolUtil.GetInt(args, "id");
                    if (!id.HasValue && !HasRectInput(args))
                        return CallToolResult.Error("id, areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (!id.HasValue && cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing capture orders in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var setting = ParsePrioritySetting(args);
                    int changed = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();

                    foreach (var capturable in Components.Capturables.Items)
                    {
                        var go = capturable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        var kpid = go.GetComponent<KPrefabID>();
                        if (id.HasValue)
                        {
                            if (kpid == null || kpid.InstanceID != id.Value)
                                continue;
                        }
                        else
                        {
                            int cell = Grid.PosToCell(go);
                            if (!CellInRect(cell, rect, worldId))
                                continue;
                        }

                        if (action == "release")
                        {
                            var baggable = go.GetComponent<Baggable>();
                            if (baggable == null || !baggable.wrangled)
                            {
                                skipped++;
                                results.Add(ObjectResult(go, "skipped_not_wrangled"));
                                continue;
                            }
                            baggable.Free();
                            changed++;
                            results.Add(ObjectResult(go, "released"));
                        }
                        else
                        {
                            bool mark = action != "cancel";
                            if (mark && !capturable.IsCapturable())
                            {
                                skipped++;
                                results.Add(ObjectResult(go, "skipped_not_capturable"));
                                continue;
                            }
                            capturable.MarkForCapture(mark, setting, updateMarkedPriority: true);
                            changed++;
                            results.Add(ObjectResult(go, mark ? "marked" : "cancelled"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action == "cancel" || action == "release" ? action : "mark",
                        ["changed"] = changed,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = id.HasValue ? null : rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static List<FactionAlignment> FindAttackTargets(Newtonsoft.Json.Linq.JObject args)
        {
            var results = new List<FactionAlignment>();
            int? id = ToolUtil.GetInt(args, "id");
            int worldId = ToolUtil.ResolveWorldId(args);

            if (id.HasValue)
            {
                foreach (var target in Components.FactionAlignments.Items)
                {
                    var go = target?.gameObject;
                    if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                    var kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id.Value)
                    {
                        results.Add(target);
                        break;
                    }
                }
                return results;
            }

            bool hasArea = HasRectInput(args);
            if (!hasArea && (args["x"] == null || args["y"] == null))
                return results;

            var rect = ToolUtil.GetRect(args);
            foreach (var target in Components.FactionAlignments.Items)
            {
                var go = target?.gameObject;
                if (go == null || !target.canBePlayerTargeted || !target.IsAlignmentActive()) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;

                int cell = Grid.PosToCell(go);
                if (!Grid.IsValidCell(cell)) continue;
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                if (x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"])
                    results.Add(target);
            }

            return results;
        }

        private static Dictionary<string, object> TargetResult(GameObject go, FactionAlignment target, string status)
        {
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["faction"] = target.Alignment.ToString(),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static List<string> HarvestSkipReasons(int matched, int notReady, int skipped)
        {
            var reasons = new List<string>();
            if (matched == 0)
                reasons.Add("no_harvestable_plants_in_area");
            if (notReady > 0)
                reasons.Add("no_mature_plants_in_area");
            if (skipped > notReady)
                reasons.Add("some_targets_skipped");
            return reasons;
        }

        private static void AddSweepTarget(List<Dictionary<string, object>> targets, bool detail, int limit, Pickupable pickupable, int cell, string status)
        {
            if (!detail || targets.Count >= limit || pickupable == null)
                return;

            var go = pickupable.gameObject;
            var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
            var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
            bool valid = Grid.IsValidCell(cell);
            targets.Add(new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = valid ? Grid.CellColumn(cell) : -1,
                ["y"] = valid ? Grid.CellRow(cell) : -1,
                ["worldId"] = valid && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : pickupable.GetMyWorldId(),
                ["massKg"] = primary != null ? Math.Round(ToolUtil.SafeFloat(primary.Mass), 3) : 0,
                ["element"] = primary != null ? primary.ElementID.ToString() : null,
                ["stored"] = pickupable.storage != null || (kpid != null && kpid.HasTag(GameTags.Stored)),
                ["equipped"] = kpid != null && kpid.HasTag(GameTags.Equipped),
                ["hasClearable"] = pickupable.GetComponent<Clearable>() != null
            });
        }
}
}
