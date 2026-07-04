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
                Tags = new List<string> { "orders", "area", "dig", "sweep", "mop", "disinfect", "cancel", "harvest", "挖", "扫", "擦", "毒", "消", "收" },
                Description = "统一区域命令入口。action=dig/挖、sweep/扫、mop/擦、disinfect/毒、cancel/消、harvest/收；复用各具体订单参数，危险或大区域仍需 confirm=true。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "区域命令：dig/挖、sweep/扫、mop/擦、disinfect/毒、cancel/消、harvest/收", Required = true, EnumValues = new List<string> { "dig", "sweep", "mop", "disinfect", "cancel", "harvest", "挖", "扫", "擦", "毒", "消", "收" } },
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
                Tags = new List<string> { "orders", "designation", "deconstruct", "attack", "capture", "conduit", "manual-delivery", "拆", "杀", "捕" },
                Description = "统一指定/取消类入口：action=deconstruct/拆、attack/杀、capture/捕、empty_conduits、cut_conduits、manual_delivery。只做路由聚合，保留各旧工具的 confirm 和安全限制。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "指定动作：deconstruct/拆、attack/杀、capture/捕、empty_conduits、cut_conduits、manual_delivery",
                        Required = true,
                        EnumValues = new List<string> { "deconstruct", "attack", "capture", "empty_conduits", "cut_conduits", "manual_delivery", "拆", "杀", "捕" }
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
                    string action = NormalizeDesignationAction(args["action"]?.ToString());
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
                    return CallToolResult.Error("action must be deconstruct/拆, attack/杀, capture/捕, empty_conduits, cut_conduits, or manual_delivery");
                    }
                }
            };
        }

        private static CallToolResult RunAreaAction(JObject args)
        {
            string action = NormalizeAreaAction(args["action"]?.ToString());
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
                    return CallToolResult.Error("action must be dig/挖, sweep/扫, mop/擦, disinfect/毒, cancel/消, or harvest/收");
            }
        }

        private static string NormalizeAreaAction(string action)
        {
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "挖":
                case "挖掘":
                case "开挖":
                    return "dig";
                case "扫":
                case "清":
                case "清扫":
                case "清理":
                case "打扫":
                case "捡":
                case "捡起":
                case "拾取":
                case "搬运":
                case "收拾":
                case "pickup":
                case "pick_up":
                case "clear":
                    return "sweep";
                case "擦":
                case "擦拭":
                case "擦水":
                case "拖":
                case "拖地":
                case "wipe":
                    return "mop";
                case "毒":
                case "消毒":
                case "杀菌":
                case "灭菌":
                case "sanitize":
                    return "disinfect";
                case "收":
                case "收获":
                case "收割":
                case "采收":
                    return "harvest";
                case "消":
                case "取消":
                case "取消任务":
                case "取消命令":
                    return "cancel";
            default:
                return (action ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        private static string NormalizeDesignationAction(string action)
        {
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
            case "拆":
            case "拆除":
                return "deconstruct";
            case "杀":
            case "攻击":
                return "attack";
                case "捕":
                case "捕捉":
                case "抓捕":
                case "抓":
                    return "capture";
            case "清管":
            case "清空管道":
                return "empty_conduits";
            case "剪":
            case "剪断":
            case "剪管":
                return "cut_conduits";
            case "送":
            case "送货":
            case "手动送货":
                return "manual_delivery";
            default:
                return (action ?? string.Empty).Trim().ToLowerInvariant();
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
}
}
