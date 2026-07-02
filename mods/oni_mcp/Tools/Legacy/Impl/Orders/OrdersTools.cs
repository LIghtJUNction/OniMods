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
        private const int CancelEvent = 2127324410;

        public static McpTool ControlOrders()
        {
            return new McpTool
            {
                Name = "orders_control",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders", "orders_unified_control", "orders_action_control", "map_orders_control" },
                Tags = new List<string> { "orders", "priority", "area", "designation", "dig", "sweep", "mop", "deconstruct" },
                Description = "订单聚合工具：domain=priority/area/designation。优先用 action + query/target/search/id/areaId 定位和执行；x/y 坐标仅作精确 fallback。用一个入口设置优先级、下达区域订单或执行指定/取消类操作；原有确认限制保持不变。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "订单领域：priority、area 或 designation；省略时按 action 自动推断", Required = false, EnumValues = new List<string> { "priority", "area", "designation" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=priority: list/set_building/set_area；domain=area: dig/sweep/mop/disinfect/cancel/harvest；domain=designation: deconstruct/attack/capture/empty_conduits/cut_conduits/manual_delivery", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "单目标对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "单目标或矩形起点 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "单目标或矩形起点 Y", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "管线/剪断类型；用于 empty_conduits/cut_conduits", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "差事优先级 1-9", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "harvest/attack/capture/manual_delivery 等动作的子模式", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "priority list/set_area 的名称或 prefabId 筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "priority list/set_area 是否包含不可设置优先级对象", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "sweep 是否纳入已存储对象", Required = false },
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "cancel 是否取消攻击标记", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "cancel 是否取消抓捕标记", Required = false },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "harvest mark 是否只处理当前可收获对象", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "dig/sweep/harvest 预览不执行", Required = false },
                    ["previewToken"] = new McpToolParameter { Type = "string", Description = "dryRun 返回的预览令牌", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "attack 允许标记友方/协助阵营目标", Required = false },
                    ["attackAreaConfirm"] = new McpToolParameter { Type = "string", Description = "attack 区域 mark 二次确认，必须精确为 attack area", Required = false },
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery 暂停/恢复手动补料", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 目标储量上限 kg", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 补料阈值 kg", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 单次搬运最小质量 kg", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery 立即请求一次搬运", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "dig/sweep 返回逐项坐标样本；默认 false，优先读取 execution/preview 摘要", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "读取/诊断最多返回数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作或大区域确认；沿用具体动作原规则", Required = false }
                }),
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(domain))
                        domain = InferOrdersDomain((args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant());
                    if (domain == "priority" || domain == "priorities")
                        return ControlPriority().Handler(args);
                    if (domain == "area" || domain == "area_action")
                        return AreaAction().Handler(args);
                    if (domain == "designation" || domain == "designate")
                        return DesignationControl().Handler(args);
                    return CallToolResult.Error("domain must be priority, area, or designation");
                }
            };
        }

        public static McpTool ListPriorities()
        {
            return new McpTool
            {
                Name = "priorities_list",
                Hidden = true,
                Group = "orders",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "orders_priorities_list" },
                Description = "兼容入口：请优先使用 orders_control domain=priority action=list。列出可设置优先级的对象，可按区域、世界和名称筛选",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = GetWorldFilter(args, hasRect);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var items = new List<Dictionary<string, object>>();
                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        items.Add(PriorityTargetToDictionary(prioritizable));
                        if (items.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["rect"] = rect,
                        ["priorities"] = items
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingPriority()
        {
            return new McpTool
            {
                Name = "buildings_set_priority",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Description = "兼容入口：请优先使用 orders_control domain=priority action=set_building。设置建筑或可优先级对象的差事优先级",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var prioritizable = go.GetComponent<Prioritizable>();
                    if (prioritizable == null)
                        return CallToolResult.Error("Target is not prioritizable");

                    int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
                    bool top = ToolUtil.GetBool(args, "topPriority", false);
                    var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
                    prioritizable.SetMasterPriority(setting);
                    return CallToolResult.Text($"Set priority for {go.GetProperName()} to {(top ? "topPriority" : priority.ToString())}");
                }
            };
        }

        public static McpTool SetPriorityArea()
        {
            return new McpTool
            {
                Name = "priorities_set_area",
                Hidden = true,
                Group = "orders",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "orders_set_priority_area", "set_priority_area" },
                Description = "兼容入口：请优先使用 orders_control domain=priority action=set_area。批量设置矩形区域内可优先级对象的差事优先级",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多修改数量，默认 200，最大 1000", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing priorities in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 200, 1000));
                    var setting = ParsePrioritySetting(args);
                    var changed = new List<Dictionary<string, object>>();
                    int matched = 0;

                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        matched++;
                        if (changed.Count >= limit)
                            continue;

                        prioritizable.SetMasterPriority(setting);
                        changed.Add(PriorityTargetToDictionary(prioritizable));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["matched"] = matched,
                        ["changed"] = changed.Count,
                        ["skippedByLimit"] = Math.Max(0, matched - changed.Count),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["priorityClass"] = setting.priority_class.ToString(),
                        ["priority"] = setting.priority_value,
                        ["targets"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlPriority()
        {
            return new McpTool
            {
                Name = "priority_control",
                Group = "orders",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "orders_priority_control", "prioritize_control" },
                Tags = new List<string> { "orders", "priority", "prioritize", "buildings", "area" },
                Description = "优先级聚合工具：action=list/set_building/set_area；读取可设置优先级对象，或设置单体/区域优先级。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、set_building 或 set_area", Required = true, EnumValues = new List<string> { "list", "set_building", "set_area" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标格子 Y", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "action=set_building/set_area 时的优先级 1-9", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "action=set_building/set_area 时是否设为红色最高优先级，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list/set_area 时按名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "action=list/set_area 时是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list/set_area 时最多返回或修改数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set_area 且区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListPriorities().Handler(args);
                    if (action == "set_building" || action == "set")
                        return SetBuildingPriority().Handler(args);
                    if (action == "set_area" || action == "area")
                        return SetPriorityArea().Handler(args);
                    return CallToolResult.Error("action must be list, set_building, or set_area");
                }
            };
        }

        private static string InferOrdersDomain(string action)
        {
            switch (action)
            {
                case "dig":
                case "sweep":
                case "mop":
                case "disinfect":
                case "cancel":
                case "harvest":
                    return "area";
                case "list":
                case "set":
                case "set_building":
                case "set_area":
                case "area":
                    return "priority";
                case "deconstruct":
                case "deconstruct_building":
                case "attack":
                case "capture":
                case "wrangle":
                case "empty_conduits":
                case "empty_pipe":
                case "cut_conduits":
                case "cut":
                case "manual_delivery":
                case "delivery":
                    return "designation";
                default:
                    return "";
            }
        }

        public static McpTool SweepArea()
        {
            return new McpTool
            {
                Name = "orders_sweep_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Description = "兼容入口：请优先使用 orders_control domain=area action=sweep。把矩形区域内固体散落物/碎片标记为清扫到仓库；不处理水、污水或任何液体。",
                Aliases = new List<string> { "sweep_area", "clear_debris_area", "solid_debris_sweep_area" },
                Tags = new List<string> { "orders", "sweep", "clear", "debris", "solid", "pickupable", "storage", "not-liquid", "固体", "碎片", "散落物", "清扫到仓库" },
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "清扫差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只扫描并返回会标记/跳过的对象，不实际下达清扫，默认 false；dryRun 不要求 confirm", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "是否把已存储对象纳入诊断；默认 false，通常不应清扫已存储对象", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "是否返回逐项目标坐标样本，默认 false；通常先看摘要字段", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "逐项诊断最多返回数量，默认 120，最大 500", Required = false },
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
                        return CallToolResult.Error("confirm=true is required when sweeping more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeStored = ToolUtil.GetBool(args, "includeStored", false);
                    bool detail = ToolUtil.GetBool(args, "detail", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 120, 500));
                    int marked = 0;
                    double kgTotal = 0;
                    int scanned = 0;
                    int inRect = 0;
                    int skippedStored = 0;
                    int skippedEquipped = 0;
                    int skippedNoCell = 0;
                    int skippedNoClearable = 0;
                    var liquidScan = ScanLiquidCells(rect, worldId, 12);
                    var targets = new List<Dictionary<string, object>>();
                    var targetCells = new List<int>();
                    var executionSkipped = new Dictionary<string, int>();

                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.gameObject == null)
                            continue;

                        scanned++;
                        int cell = SweepCell(pickupable);
                        if (!Grid.IsValidCell(cell))
                        {
                            skippedNoCell++;
                            IncrementSkip(executionSkipped, "invalid_cell");
                            AddSweepTarget(targets, detail, limit, pickupable, cell, "skipped_invalid_cell");
                            continue;
                        }
                        if (!CellInRect(cell, rect, worldId))
                            continue;

                        inRect++;
                        var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                        if (kpid != null && kpid.HasTag(GameTags.Equipped))
                        {
                            skippedEquipped++;
                            IncrementSkip(executionSkipped, "equipped");
                            AddSweepTarget(targets, detail, limit, pickupable, cell, "skipped_equipped");
                            continue;
                        }
                        if (!includeStored && kpid != null && (kpid.HasTag(GameTags.Stored) || pickupable.storage != null))
                        {
                            skippedStored++;
                            IncrementSkip(executionSkipped, "stored");
                            AddSweepTarget(targets, detail, limit, pickupable, cell, "skipped_stored");
                            continue;
                        }

                        var clearable = pickupable.GetComponent<Clearable>();
                        if (clearable == null)
                        {
                            skippedNoClearable++;
                            IncrementSkip(executionSkipped, "no_clearable");
                            AddSweepTarget(targets, detail, limit, pickupable, cell, "skipped_no_clearable");
                            continue;
                        }

                        var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                        if (primary != null)
                            kgTotal += ToolUtil.SafeFloat(primary.Mass);

                        if (!dryRun)
                        {
                            clearable.MarkForClear();
                            ApplyPriority(pickupable.gameObject, args);
                        }
                        marked++;
                        targetCells.Add(cell);
                        AddSweepTarget(targets, detail, limit, pickupable, cell, dryRun ? "would_mark" : "marked");
                    }

                    var responseDict = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["scannedPickupables"] = scanned,
                        ["inRect"] = inRect,
                        ["marked"] = marked,
                        ["skipped"] = new Dictionary<string, object>
                        {
                            ["invalidCell"] = skippedNoCell,
                            ["stored"] = skippedStored,
                            ["equipped"] = skippedEquipped,
                            ["noClearable"] = skippedNoClearable
                        },
                        ["execution"] = CellExecutionMetadata("sweep", worldId, targetCells, executionSkipped, detail, limit),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["preview"] = new Dictionary<string, object>
                        {
                            ["targets"] = targets.Where(item => item["status"]?.ToString() == "would_mark" || item["status"]?.ToString() == "marked").ToList(),
                            ["kgTotal"] = Math.Round(kgTotal, 3),
                            ["risks"] = SweepPreviewRisks(liquidScan)
                        },
                        ["targets"] = targets,
                        ["truncatedTargets"] = detail ? Math.Max(0, inRect - targets.Count) : 0,
                        ["liquidCellsInRect"] = liquidScan["count"],
                        ["mopHint"] = (int)liquidScan["count"] > 0 ? "This area contains liquid cells. Sweep only handles solid pickupables/debris; use orders_control domain=area action=mop for water/liquid on floor." : null,
                        ["liquidSamples"] = liquidScan["samples"],
                        ["note"] = marked == 0
                            ? "No sweep errands were marked. Check targets/skipped; sweep only handles solid pickupables/debris, not water/liquid. For floor liquids use orders_control domain=area action=mop. Sweep also requires reachable storage accepting the item before dupes will haul it."
                            : "Sweep marks solid debris only; it never mops water/liquid. Dupes still need reachable storage accepting the item."
                    };
                    if (dryRun)
                        responseDict["previewToken"] = PreviewTokenRegistry.Register(args);
                    return CallToolResult.Text(JsonConvert.SerializeObject(responseDict, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AreaAction()
        {
            return new McpTool
            {
                Name = "orders_area_action",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders_area", "area_order", "area_designate" },
                Tags = new List<string> { "orders", "area", "dig", "sweep", "mop", "disinfect", "cancel", "harvest" },
                Description = "统一区域命令入口。action=dig/sweep/mop/disinfect/cancel/harvest；复用各具体订单参数，危险或大区域仍需 confirm=true。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "区域命令：dig、sweep、mop、disinfect、cancel、harvest", Required = true, EnumValues = new List<string> { "dig", "sweep", "mop", "disinfect", "cancel", "harvest" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "支持该参数的命令使用：差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "支持该参数的命令使用：是否设为红色最高优先级，默认 false", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "dig/sweep/harvest 支持：只预览不执行", Required = false },
                    ["previewToken"] = new McpToolParameter { Type = "string", Description = "dig/sweep/harvest dryRun 返回的预览令牌", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "harvest 支持：mark、when_ready、cancel", Required = false, EnumValues = new List<string> { "mark", "when_ready", "cancel" } },
                    ["readyOnly"] = new McpToolParameter { Type = "boolean", Description = "harvest 支持：mark 模式是否只处理当前可收获对象，默认 true", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "sweep 支持：是否纳入已存储对象，默认 false", Required = false },
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "cancel 支持：是否取消攻击标记，默认 true", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "cancel 支持：是否取消抓捕标记，默认 true", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "dig/sweep 支持：是否返回逐项坐标样本；默认 false，优先读取摘要", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "dig/sweep 支持：逐项诊断最多返回数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作或大区域确认；dig 执行时必须 true", Required = false }
                }),
                Handler = args => RunAreaAction(args)
            };
        }

        public static McpTool DesignationControl()
        {
            return new McpTool
            {
                Name = "designation_control",
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders_designation_control", "map_designation_control" },
                Tags = new List<string> { "orders", "designation", "deconstruct", "attack", "capture", "conduit", "manual-delivery" },
                Description = "统一指定/取消类入口：action=deconstruct/attack/capture/empty_conduits/cut_conduits/manual_delivery。只做路由聚合，保留各旧工具的 confirm 和安全限制。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "指定动作：deconstruct、attack、capture、empty_conduits、cut_conduits、manual_delivery",
                        Required = true,
                        EnumValues = new List<string> { "deconstruct", "attack", "capture", "empty_conduits", "cut_conduits", "manual_delivery" }
                    },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；适用于 deconstruct/attack/capture/manual_delivery", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；适用于 deconstruct/attack/cut_conduits/manual_delivery", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；适用于 deconstruct/attack/cut_conduits/manual_delivery", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "empty_conduits/cut_conduits 的管线类型", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "支持该参数的动作使用：差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "支持该参数的动作使用：是否设为红色最高优先级", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "attack/capture 支持：mark、cancel；capture 额外支持 release", Required = false, EnumValues = new List<string> { "mark", "cancel", "release" } },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "attack 支持：允许标记友方/协助阵营目标", Required = false },
                    ["attackAreaConfirm"] = new McpToolParameter { Type = "string", Description = "attack 区域 mark 二次确认，必须精确为 attack area", Required = false },
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery 支持：true 暂停手动补料，false 恢复", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 支持：目标储量上限 kg", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 支持：补料阈值 kg", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "manual_delivery 支持：单次搬运最小质量 kg", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "manual_delivery 支持：立即请求一次搬运", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；deconstruct/attack/cut_conduits 或大区域 capture/empty_conduits 按原规则要求", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "deconstruct":
                        case "deconstruct_building":
                            return DeconstructBuilding().Handler(args);
                        case "attack":
                            return Attack().Handler(WithRoutedMode(args));
                        case "capture":
                        case "wrangle":
                            return CaptureCritters().Handler(WithRoutedMode(args));
                        case "empty_conduits":
                        case "empty_pipe":
                            return EmptyConduits().Handler(args);
                        case "cut_conduits":
                        case "cut":
                            return CutConduits().Handler(args);
                        case "manual_delivery":
                        case "delivery":
                            return ConfigureManualDelivery().Handler(args);
                        default:
                            return CallToolResult.Error("action must be deconstruct, attack, capture, empty_conduits, cut_conduits, or manual_delivery");
                    }
                }
            };
        }

        private static JObject WithRoutedMode(JObject args)
        {
            var routed = (JObject)args.DeepClone();
            string mode = routed["mode"]?.ToString();
            routed["action"] = string.IsNullOrWhiteSpace(mode) ? "mark" : mode;
            return routed;
        }

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
                            if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null)
                            {
                                IncrementSkip(skipped, "already_queued");
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

                            if (DigTool.PlaceDig(cell, dist++) != null)
                                marked++;
                        }
                    }

                    var execution = DigExecutionMetadata(rect, worldId, targetCells, skipped, detail, limit);
                    var responseDict = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["marked"] = marked,
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
                    return CallToolResult.Text(JsonConvert.SerializeObject(responseDict, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool Attack()
        {
            return new McpTool
            {
                Name = "orders_attack",
                Hidden = true,
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Description = "兼容入口：请优先使用 orders_control domain=designation action=attack。仅用于攻击小动物/敌对目标，不能用于挖掘。区域攻击除 confirm=true 外还必须 action=mark 且 attackAreaConfirm=\"attack area\"",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；提供后优先于坐标/区域", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格查找", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格查找", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark 标记攻击，cancel 取消攻击；默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "攻击差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许标记友方/协助阵营目标，默认 false", Required = false },
                    ["attackAreaConfirm"] = new McpToolParameter { Type = "string", Description = "区域攻击二次确认；矩形区域 mark 攻击必须精确填写 attack area，防止把挖掘误调成攻击", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；标记攻击时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    bool mark = action != "cancel";
                    if (mark && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for attack orders");
                    bool areaAttack = mark && args["id"] == null && HasRectInput(args);
                    if (areaAttack && args["attackAreaConfirm"]?.ToString() != "attack area")
                        return CallToolResult.Error("Refusing area attack without attackAreaConfirm=\"attack area\". For terrain excavation use orders_control domain=area action=dig, not orders_attack.");
                    if (FactionManager.Instance == null)
                        return CallToolResult.Error("FactionManager is not initialized");

                    var targets = FindAttackTargets(args);
                    if (targets.Count == 0)
                        return CallToolResult.Error("No attack target found");

                    bool force = ToolUtil.GetBool(args, "force", false);
                    int changed = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var target in targets)
                    {
                        var go = target?.gameObject;
                        if (go == null)
                            continue;

                        if (mark && !force && FactionManager.Instance.GetDisposition(FactionManager.FactionID.Duplicant, target.Alignment) == FactionManager.Disposition.Assist)
                        {
                            skipped++;
                            results.Add(TargetResult(go, target, "skipped_assist_faction"));
                            continue;
                        }

                        target.SetPlayerTargeted(mark);
                        if (mark)
                            ApplyPriority(go, args);

                        changed++;
                        results.Add(TargetResult(go, target, mark ? "marked" : "cancelled"));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = mark ? "mark" : "cancel",
                        ["changed"] = changed,
                        ["skipped"] = skipped,
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static CallToolResult RunAreaAction(JObject args)
        {
            string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "dig":
                    return DigArea().Handler(args);
                case "sweep":
                    return SweepArea().Handler(args);
                case "mop":
                    return MopArea().Handler(args);
                case "disinfect":
                    return DisinfectArea().Handler(args);
                case "cancel":
                    return CancelArea().Handler(args);
                case "harvest":
                    return HarvestArea().Handler(args);
                default:
                    return CallToolResult.Error("action must be dig, sweep, mop, disinfect, cancel, or harvest");
            }
        }

        public static McpTool MopArea()
        {
            return new McpTool
            {
                Name = "orders_mop_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "mop_area", "liquids_mop_area", "water_mop_area", "mop_liquid_area", "mop_water_area" },
                Tags = new List<string> { "orders", "mop", "liquid", "water", "polluted-water", "floor", "spill", "地上的水", "拖地", "液体", "水" },
                Description = "在矩形区域内对地上的水、污水或其他可拖地液体格子下达拖地命令；不是清扫固体碎片。遵循游戏限制：下方必须有地面且液体质量不能超过可拖地上限",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拖地差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when mopping more than 100 cells");

                    var prefab = Assets.GetPrefab(new Tag("MopPlacer"));
                    if (prefab == null)
                        return CallToolResult.Error("MopPlacer prefab is not available");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int marked = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    var targetCells = new List<int>();
                    var executionSkipped = new Dictionary<string, int>();
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;
                            if (Grid.Solid[cell] || !Grid.Element[cell].IsLiquid)
                                continue;
                        if (Grid.Objects[cell, (int)ObjectLayer.MopPlacer] != null)
                        {
                            skipped++;
                            IncrementSkip(executionSkipped, "already_queued");
                            continue;
                        }
                            bool onFloor = Grid.IsValidCell(Grid.CellBelow(cell)) && Grid.Solid[Grid.CellBelow(cell)];
                            bool smallEnough = Grid.Mass[cell] <= MopTool.maxMopAmt;
                        if (!onFloor || !smallEnough)
                        {
                            skipped++;
                            IncrementSkip(executionSkipped, onFloor ? "too_much_liquid" : "no_floor");
                            results.Add(CellResult(cell, onFloor ? "skipped_too_much_liquid" : "skipped_no_floor"));
                            continue;
                        }

                            if (DebugHandler.InstantBuildMode)
                            {
                            Moppable.MopCell(cell, 1000000f, null);
                            marked++;
                            targetCells.Add(cell);
                            results.Add(CellResult(cell, "instant_mopped"));
                            continue;
                        }

                            var placer = Util.KInstantiate(prefab);
                            Grid.Objects[cell, (int)ObjectLayer.MopPlacer] = placer;
                            var position = Grid.CellToPosCBC(cell, Grid.SceneLayer.Move);
                            position.z -= 0.15f;
                            placer.transform.SetPosition(position);
                            placer.SetActive(true);
                        ApplyPriority(placer, args);
                        marked++;
                        targetCells.Add(cell);
                        results.Add(CellResult(cell, "marked"));
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["skipped"] = skipped,
                        ["execution"] = CellExecutionMetadata("mop", worldId, targetCells, executionSkipped, false, 200),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DisinfectArea()
        {
            return new McpTool
            {
                Name = "orders_disinfect_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "disinfect_area", "germs_disinfect_area" },
                Description = "兼容入口：请优先使用 orders_control domain=area action=disinfect。标记矩形区域内带病菌且支持消毒的对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "消毒差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when disinfecting more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var disinfectable = go.GetComponent<Disinfectable>();
                                var element = go.GetComponent<PrimaryElement>();
                                if (disinfectable == null || element == null || element.DiseaseCount <= 0)
                                    continue;

                                disinfectable.MarkForDisinfect();
                                ApplyPriority(go, args);
                                marked++;
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool EmptyConduits()
        {
            return new McpTool
            {
                Name = "conduits_empty_area",
                Hidden = true,
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "empty_pipe_area", "orders_empty_pipe" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=empty_conduits。按区域标记气管、液管或运输轨道清空内容，对应游戏 Empty Pipe 工具",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "清空类型：all、gas、liquid、solid，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "gas", "liquid", "solid" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "清空差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when emptying conduits in more than 100 cells");

                    var layers = GetEmptyLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("type must be all, gas, liquid or solid");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int marked = 0;
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var layer in layers)
                            {
                                var go = Grid.Objects[cell, (int)layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                var workable = go.GetComponent<IEmptyConduitWorkable>();
                                if (workable == null)
                                    continue;

                                if (DebugHandler.InstantBuildMode)
                                    workable.EmptyContents();
                                else
                                    workable.MarkForEmptying();
                                ApplyPriority(go, args);
                                marked++;
                                results.Add(ObjectResult(go, DebugHandler.InstantBuildMode ? "instant_emptied" : "marked"));
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["marked"] = marked,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "all" : args["type"].ToString(),
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CutConduits()
        {
            return new McpTool
            {
                Name = "conduits_cut",
                Hidden = true,
                Group = "orders",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "orders_cut_conduits", "cut_conduits" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=cut_conduits。按格子或矩形区域剪断管路/电线/运输轨道，实际下达拆除对应段的命令，需要 confirm=true",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格处理", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格处理", Required = false },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "剪断类型：auto=气/液/固体管路，all=管路+电线+自动化线+运输管，或指定 gas/liquid/solid/wire/logic/travel_tube；默认 auto",
                        Required = false,
                        EnumValues = new List<string> { "auto", "all", "gas", "liquid", "solid", "wire", "logic", "travel_tube" }
                    },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "拆除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for cutting conduits");
                    if (!HasRectInput(args) && (args["x"] == null || args["y"] == null))
                        return CallToolResult.Error("areaId, x/y or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 200)
                        return CallToolResult.Error("Refusing to cut more than 200 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var layers = GetCutLayers(args["type"]?.ToString());
                    if (layers.Count == 0)
                        return CallToolResult.Error("Invalid type; expected auto, all, gas, liquid, solid, wire, logic or travel_tube");
                    var seen = new HashSet<GameObject>();
                    var results = new List<Dictionary<string, object>>();
                    int queued = 0;
                    int skipped = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            foreach (var go in FindCuttableObjects(cell, layers))
                            {
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);

                                string error;
                                if (QueueDeconstruct(go, args, out error))
                                {
                                    queued++;
                                    results.Add(CutResult(go, cell, "queued", null));
                                }
                                else
                                {
                                    skipped++;
                                    results.Add(CutResult(go, cell, "skipped", error));
                                }
                            }
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = queued,
                        ["skipped"] = skipped,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["type"] = string.IsNullOrWhiteSpace(args["type"]?.ToString()) ? "auto" : args["type"].ToString(),
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CancelArea()
        {
            return new McpTool
            {
                Name = "orders_cancel_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "cancel_area", "orders_cancel" },
                Description = "兼容入口：请优先使用 orders_control domain=area action=cancel。取消矩形区域内玩家已下达的订单：挖掘、建造、拆除、清扫、收获、攻击、抓捕等可取消对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内攻击标记，默认 true", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内抓捕标记，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when cancelling more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeAttack = ToolUtil.GetBool(args, "includeAttack", true);
                    bool includeCapture = ToolUtil.GetBool(args, "includeCapture", true);
                    var seen = new HashSet<GameObject>();
                    int triggered = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < 45; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);
                                go.Trigger(CancelEvent);
                                triggered++;
                            }
                        }
                    }

                    int attacksCancelled = includeAttack ? SetAttackMarks(rect, worldId, false, null, false) : 0;
                    int capturesCancelled = includeCapture ? SetCaptureMarks(rect, worldId, false, new PrioritySetting(PriorityScreen.PriorityClass.basic, 5), false, false) : 0;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["triggeredObjects"] = triggered,
                        ["attacksCancelled"] = attacksCancelled,
                        ["capturesCancelled"] = capturesCancelled,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

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

        public static McpTool SetBuildingEnabled()
        {
            return new McpTool
            {
                Name = "buildings_set_enabled",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "building_enable", "building_disable" },
                Hidden = true,
                Description = "兼容入口：请使用 building_control domain=config action=set_enabled。启用或禁用指定建筑；直接设置建筑状态，不排队复制人开关差事",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 启用，false 禁用", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var button = go.GetComponent<BuildingEnabledButton>();
                    if (button == null)
                        return CallToolResult.Error("Target does not support enabled/disabled state");

                    bool enabled = ToolUtil.GetBool(args, "enabled", true);
                    button.IsEnabled = enabled;
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["enabled"] = button.IsEnabled
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingToggle()
        {
            return new McpTool
            {
                Name = "buildings_set_toggle",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "logic_switch_set", "building_toggle_set" },
                Hidden = true,
                Description = "兼容入口：请使用 building_control domain=config action=set_toggle。设置支持玩家手动开关的建筑/自动化开关状态，例如逻辑开关；仅当目标实现 IPlayerControlledToggle 时可用",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["on"] = new McpToolParameter { Type = "boolean", Description = "true 打开，false 关闭", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var toggle = go.GetComponents<Component>().OfType<IPlayerControlledToggle>().FirstOrDefault();
                    if (toggle == null)
                        return CallToolResult.Error("Target does not expose a player-controlled toggle");

                    bool desired = ToolUtil.GetBool(args, "on", true);
                    bool before = toggle.ToggledOn();
                    if (before != desired)
                        toggle.ToggledByPlayer();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["before"] = before,
                        ["on"] = toggle.ToggledOn(),
                        ["sideScreenTitleKey"] = toggle.SideScreenTitleKey
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ConfigureManualDelivery()
        {
            return new McpTool
            {
                Name = "buildings_manual_delivery",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "manual_delivery_set", "building_refill_set" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=manual_delivery。配置建筑手动补料/搬运：暂停或恢复补料、设置容量/补料阈值、立即请求搬运",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "可选：true 暂停手动补料，false 恢复", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "可选：目标储量上限 kg", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "可选：低于该质量时请求补料 kg", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "可选：单次搬运最小质量 kg", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "是否立即请求一次搬运，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var delivery = go.GetComponent<ManualDeliveryKG>();
                    if (delivery == null)
                        return CallToolResult.Error("Target does not support manual delivery");

                    if (args["paused"] != null)
                        delivery.Pause(ToolUtil.GetBool(args, "paused", false), "Oni MCP manual delivery setting");
                    float? capacity = ToolUtil.GetFloat(args, "capacityKg");
                    if (capacity.HasValue)
                        delivery.capacity = Math.Max(0f, capacity.Value);
                    float? refill = ToolUtil.GetFloat(args, "refillMassKg");
                    if (refill.HasValue)
                        delivery.refillMass = Math.Max(0f, refill.Value);
                    float? minimum = ToolUtil.GetFloat(args, "minimumMassKg");
                    if (minimum.HasValue)
                        delivery.MinimumMass = Math.Max(0f, minimum.Value);
                    if (ToolUtil.GetBool(args, "requestNow", false))
                        delivery.RequestDelivery();

                    delivery.UpdateDeliveryState();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["paused"] = delivery.IsPaused,
                        ["capacityKg"] = Math.Round(delivery.capacity, 3),
                        ["refillMassKg"] = Math.Round(delivery.refillMass, 3),
                        ["minimumMassKg"] = Math.Round(delivery.MinimumMass, 3),
                        ["requestedItemTag"] = delivery.RequestedItemTag.Name
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
            ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
            ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；省略时可用 query/target/search 搜索定位", Required = false },
            ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；省略时可用 query/target/search 搜索定位", Required = false },
            ["query"] = new McpToolParameter { Type = "string", Description = "按对象名称、prefabId、元素或复制人搜索目标格", Required = false },
            ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
            ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
            ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
            ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
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

        private static GameObject FindTarget(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
        int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
        if (!cell.HasValue)
        {
            int searchX;
            int searchY;
            string searchError;
            if (ToolUtil.TryResolveSearchCell(args, out searchX, out searchY, out searchError))
                cell = Grid.XYToCell(searchX, searchY);
        }
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var prioritizable in Components.Prioritizables.Items)
            {
                var go = prioritizable?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }

            return null;
        }

        private static PrioritySetting ParsePrioritySetting(Newtonsoft.Json.Linq.JObject args)
        {
            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            return new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
        }

        private static int GetWorldFilter(Newtonsoft.Json.Linq.JObject args, bool hasRect)
        {
            if (ToolUtil.GetInt(args, "worldId").HasValue)
                return ToolUtil.GetInt(args, "worldId").Value;
            if (hasRect)
                return ToolUtil.ResolveWorldId(args);
            return -1;
        }

        private static bool MatchesPriorityTarget(Prioritizable prioritizable, Dictionary<string, int> rect, int worldId, string query, bool includeInactive)
        {
            var go = prioritizable?.gameObject;
            if (go == null) return false;
            if (!includeInactive && !prioritizable.IsPrioritizable()) return false;
            if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) return false;

            int cell = Grid.PosToCell(go);
            if (rect != null && !CellInRect(cell, rect, worldId)) return false;
            if (!MatchesQuery(go, query)) return false;
            return true;
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            string prefabId = kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> PriorityTargetToDictionary(Prioritizable prioritizable)
        {
            var go = prioritizable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            var setting = prioritizable.GetMasterPriority();
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : Mathf.RoundToInt(go.transform.GetPosition().x);
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : Mathf.RoundToInt(go.transform.GetPosition().y);

            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["position"] = new { x, y },
                ["worldId"] = GetTargetWorldId(go, cell),
                ["isPrioritizable"] = prioritizable.IsPrioritizable(),
                ["priorityClass"] = setting.priority_class.ToString(),
                ["priority"] = setting.priority_value,
                ["topPriority"] = setting.priority_class == PriorityScreen.PriorityClass.topPriority
            };
        }

        private static int GetTargetWorldId(GameObject go, int cell)
        {
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];

            var component = go.GetComponent<KMonoBehaviour>();
            return component != null ? component.GetMyWorldId() : -1;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static int SweepCell(Pickupable pickupable)
        {
            if (pickupable == null)
                return Grid.InvalidCell;
            if (Grid.IsValidCell(pickupable.cachedCell))
                return pickupable.cachedCell;
            return Grid.PosToCell(pickupable.gameObject);
        }

        private static Dictionary<string, object> ScanLiquidCells(Dictionary<string, int> rect, int worldId, int sampleLimit)
        {
            int count = 0;
            var samples = new List<Dictionary<string, object>>();

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (Grid.Solid[cell] || !Grid.Element[cell].IsLiquid)
                        continue;

                    count++;
                    if (samples.Count < sampleLimit)
                        samples.Add(CellResult(cell, "liquid_in_sweep_area"));
                }
            }

            return new Dictionary<string, object>
            {
                ["count"] = count,
                ["samples"] = samples
            };
        }

        private static List<Dictionary<string, object>> SweepPreviewRisks(Dictionary<string, object> liquidScan)
        {
            var risks = new List<Dictionary<string, object>>();
            int liquidCount = liquidScan != null && liquidScan.ContainsKey("count") ? Convert.ToInt32(liquidScan["count"]) : 0;
            if (liquidCount > 0)
            {
                risks.Add(new Dictionary<string, object>
                {
                    ["type"] = "liquid_in_area",
                    ["severity"] = "info",
                    ["count"] = liquidCount,
                    ["samples"] = liquidScan.ContainsKey("samples") ? liquidScan["samples"] : null,
                    ["message"] = "Sweep ignores liquid cells; use orders_control domain=area action=mop for floor liquids."
                });
            }
            return risks;
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

        private static bool TryFindReachableWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in TargetAndAdjacentCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !Grid.IsVisible(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> TargetAndAdjacentCells(int cell)
        {
            yield return cell;
            foreach (int adjacent in AdjacentDigWorkCells(cell))
                yield return adjacent;
        }

        private static Dictionary<string, object> ReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = CellResult(targetCell, status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static List<Navigator> ActiveNavigators(int worldId)
        {
            var navigators = new List<Navigator>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || (worldId >= 0 && dupe.GetMyWorldId() != worldId))
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null)
                    navigators.Add(navigator);
            }
            return navigators;
        }

        private static bool TryFindReachableDigWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in AdjacentDigWorkCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !Grid.IsVisible(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> AdjacentDigWorkCells(int cell)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            for (int yy = y - 1; yy <= y + 1; yy++)
            {
                for (int xx = x - 1; xx <= x + 1; xx++)
                {
                    if (xx == x && yy == y)
                        continue;
                    yield return Grid.XYToCell(xx, yy);
                }
            }
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> DigReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = DigTarget(targetCell, Grid.CellColumn(targetCell), Grid.CellRow(targetCell), status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static void IncrementSkip(Dictionary<string, int> skipped, string reason)
        {
            int count;
            skipped[reason] = skipped.TryGetValue(reason, out count) ? count + 1 : 1;
        }

        private sealed class DigRiskBuilder
        {
            private readonly Dictionary<string, RiskBucket> buckets = new Dictionary<string, RiskBucket>();

            public void ScanTarget(int cell, int x, int y, int worldId)
            {
                if (!Grid.IsValidCell(cell))
                    return;

                float tempC = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;
                if (tempC >= 75f)
                    Add("hot_target", "warning", cell, x, y, $"Target solid is hot ({Math.Round(tempC, 1)}C).");

                foreach (int neighbor in Neighbors(cell))
                {
                    if (!Grid.IsValidCell(neighbor) || !ToolUtil.CellMatchesWorld(neighbor, worldId))
                        continue;
                    int nx = Grid.CellColumn(neighbor);
                    int ny = Grid.CellRow(neighbor);
                    var element = Grid.Element[neighbor];
                    if (element != null && element.IsLiquid)
                        Add("adjacent_liquid", "danger", neighbor, nx, ny, "Digging may open into adjacent liquid.");
                    else if (Grid.Mass[neighbor] <= 0.001f)
                        Add("adjacent_vacuum", "warning", neighbor, nx, ny, "Digging may open into vacuum.");

                    float neighborTempC = ToolUtil.SafeFloat(Grid.Temperature[neighbor]) - 273.15f;
                    if (neighborTempC >= 75f)
                        Add("adjacent_hot_cell", "warning", neighbor, nx, ny, $"Adjacent cell is hot ({Math.Round(neighborTempC, 1)}C).");
                }
            }

            public List<Dictionary<string, object>> ToList()
            {
                return buckets.Values
                    .OrderByDescending(item => item.Severity == "danger" ? 2 : item.Severity == "warning" ? 1 : 0)
                    .ThenBy(item => item.Type)
                    .Select(item => item.ToDictionary())
                    .ToList();
            }

            private void Add(string type, string severity, int cell, int x, int y, string message)
            {
                RiskBucket bucket;
                if (!buckets.TryGetValue(type, out bucket))
                {
                    bucket = new RiskBucket { Type = type, Severity = severity, Message = message };
                    buckets[type] = bucket;
                }
                bucket.Count++;
                if (bucket.Samples.Count < 12)
                    bucket.Samples.Add(CellResult(cell, type));
            }

            private static IEnumerable<int> Neighbors(int cell)
            {
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                yield return Grid.XYToCell(x, y + 1);
                yield return Grid.XYToCell(x, y - 1);
                yield return Grid.XYToCell(x - 1, y);
                yield return Grid.XYToCell(x + 1, y);
            }
        }

        private sealed class RiskBucket
        {
            public string Type;
            public string Severity;
            public string Message;
            public int Count;
            public readonly List<Dictionary<string, object>> Samples = new List<Dictionary<string, object>>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["type"] = Type,
                    ["severity"] = Severity,
                    ["count"] = Count,
                    ["message"] = Message,
                    ["samples"] = Samples
                };
            }
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static bool HasRectInput(Newtonsoft.Json.Linq.JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static int SetAttackMarks(Dictionary<string, int> rect, int worldId, bool mark, Newtonsoft.Json.Linq.JObject args, bool applyPriority)
        {
            int changed = 0;
            foreach (var target in Components.FactionAlignments.Items)
            {
                var go = target?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && (!target.canBePlayerTargeted || !target.IsAlignmentActive()))
                    continue;

                target.SetPlayerTargeted(mark);
                if (mark && applyPriority && args != null)
                    ApplyPriority(go, args);
                changed++;
            }
            return changed;
        }

        private static int SetCaptureMarks(Dictionary<string, int> rect, int worldId, bool mark, PrioritySetting setting, bool updatePriority, bool requireCapturable)
        {
            int changed = 0;
            foreach (var capturable in Components.Capturables.Items)
            {
                var go = capturable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && requireCapturable && !capturable.IsCapturable())
                    continue;

                capturable.MarkForCapture(mark, setting, updatePriority);
                changed++;
            }
            return changed;
        }

        private static void ApplyPriority(GameObject go, Newtonsoft.Json.Linq.JObject args)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
            prioritizable.SetMasterPriority(setting);
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

        private static List<ObjectLayer> GetCutLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "auto" || type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit });
            if (type == "auto" || type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit });
            if (type == "auto" || type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit });
            if (type == "auto" || type == "all" || type == "wire")
                layers.AddRange(new[] { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire });
            if (type == "auto" || type == "all" || type == "logic")
                layers.AddRange(new[] { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire });
            if (type == "all" || type == "travel_tube")
                layers.AddRange(new[] { ObjectLayer.TravelTubeTile, ObjectLayer.ReplacementTravelTube, ObjectLayer.Building });
            return layers;
        }

        private static List<ObjectLayer> GetEmptyLayers(string value)
        {
            string type = string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();
            var layers = new List<ObjectLayer>();
            if (type == "all" || type == "gas")
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile });
            if (type == "all" || type == "liquid")
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile });
            if (type == "all" || type == "solid")
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile });
            return layers;
        }

        private static IEnumerable<GameObject> FindCuttableObjects(int cell, List<ObjectLayer> layers)
        {
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;
                if (layer == ObjectLayer.Building && go.GetComponent<TravelTube>() == null)
                    continue;
                yield return go;
            }
        }

        private static bool QueueDeconstruct(GameObject go, Newtonsoft.Json.Linq.JObject args, out string error)
        {
            var deconstructable = go.GetComponent<Deconstructable>();
            if (deconstructable == null)
            {
                error = "Target is not deconstructable";
                return false;
            }
            if (!deconstructable.allowDeconstruction && !DebugHandler.InstantBuildMode)
            {
                error = "Target does not allow deconstruction";
                return false;
            }

            deconstructable.QueueDeconstruction(userTriggered: true);
            ApplyPriority(go, args);
            error = null;
            return true;
        }

        private static Dictionary<string, object> CutResult(GameObject go, int cell, string status, string error)
        {
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            var result = new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? -1,
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell)
            };
            if (!string.IsNullOrEmpty(error))
                result["error"] = error;
            return result;
        }

        private static Dictionary<string, object> ObjectResult(GameObject go, string status)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
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

        private static Dictionary<string, object> CellResult(int cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["element"] = Grid.IsValidCell(cell) ? Grid.Element[cell].id.ToString() : "",
                ["massKg"] = Grid.IsValidCell(cell) ? Math.Round(Grid.Mass[cell], 3) : 0
            };
        }
    }
}
