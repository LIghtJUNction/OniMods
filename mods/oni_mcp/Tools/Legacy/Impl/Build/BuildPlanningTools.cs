using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        public static McpTool ControlBuildPlanning()
        {
            return new McpTool
            {
                Name = "build_planning_control",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "buildings_planning_control", "build_control" },
                Tags = new List<string> { "buildings", "materials", "preview", "placement", "utility", "建造", "材料", "预检", "候选" },
                Description = "建造规划组合工具：action=search_defs/materials/preview/placement_candidates/auto_connect/build_area",
                Parameters = BuildPlanningControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    var forwardArgs = ForwardArgs(args);
                    switch (action)
                {
                    case "parse_plan":
                    case "parse_sequence":
                    case "parse":
                    case "plan_text":
                        return ParseBuildPlan().Handler(forwardArgs);
                    case "search_defs":
                        case "search":
                        case "defs":
                            return SearchBuildables().Handler(forwardArgs);
                        case "materials":
                        case "list_materials":
                            return ListBuildMaterials().Handler(forwardArgs);
                        case "preview":
                        case "validate":
                            return PreviewBuild().Handler(forwardArgs);
                        case "placement_candidates":
                        case "candidates":
                        case "anchors":
                            return FindPlacementCandidates().Handler(forwardArgs);
                        case "auto_connect":
                        case "utility_auto_connect":
                        case "connect":
                            return AutoConnectUtility().Handler(forwardArgs);
                        case "build_area":
                        case "area":
                        case "batch_build":
                            return BuildArea().Handler(forwardArgs);
                        default:
                        return CallToolResult.Error("action must be parse_plan, search_defs, materials, preview, placement_candidates, auto_connect, or build_area");
                    }
                }
            };
        }

        private static JObject ForwardArgs(JObject args)
        {
            var forwardArgs = (JObject)args.DeepClone();
            forwardArgs.Remove("action");
            return forwardArgs;
        }

        private static Dictionary<string, McpToolParameter> BuildPlanningControlParams()
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter
                {
                    Type = "string",
                    Description = "操作：parse_plan/search_defs/materials/preview/placement_candidates/auto_connect/build_area",
                    Required = true,
                    EnumValues = new List<string> { "parse_plan", "search_defs", "materials", "preview", "placement_candidates", "auto_connect", "build_area" }
                }
            };

            MergeParameters(parameters, ParseBuildPlan().Parameters);
            MergeParameters(parameters, SearchBuildables().Parameters);
            MergeParameters(parameters, ListBuildMaterials().Parameters);
            MergeParameters(parameters, PreviewBuild().Parameters);
            MergeParameters(parameters, FindPlacementCandidates().Parameters);
            MergeParameters(parameters, AutoConnectUtility().Parameters);
            MergeParameters(parameters, BuildArea().Parameters);
            return parameters;
        }

        private static void MergeParameters(Dictionary<string, McpToolParameter> target, Dictionary<string, McpToolParameter> source)
        {
            if (source == null)
                return;
            foreach (var item in source)
            {
                if (target.ContainsKey(item.Key))
                    continue;
                target[item.Key] = CopyOptionalParameter(item.Value);
            }
        }

        private static McpToolParameter CopyOptionalParameter(McpToolParameter source)
        {
            return new McpToolParameter
            {
                Type = source.Type,
                Description = source.Description,
                Required = false,
                EnumValues = source.EnumValues == null ? null : new List<string>(source.EnumValues)
            };
        }

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
                    var def = Assets.GetBuildingDef(prefabId);
                    if (def == null)
                        return CallToolResult.Error("Building def not found");

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

        public static McpTool AutoConnectUtility()
        {
            return new McpTool
            {
                Name = "utility_auto_connect",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "build_auto_connect", "wire_auto_connect", "pipe_auto_connect", "logic_auto_connect" },
                Tags = new List<string> { "buildings", "utility", "wire", "pipe", "logic", "connect", "电线", "水管", "气管", "信号线" },
                Description = "兼容入口：请使用 building_control domain=planning action=auto_connect",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter { Type = "string", Description = "utility 类型：wire、liquid、gas、solid、logic；prefabId 留空时用它选择默认建筑", Required = false, EnumValues = new List<string> { "wire", "liquid", "gas", "solid", "logic" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "可选建筑 prefabId，默认按 type 选择 Wire/LiquidConduit/GasConduit/SolidConduit/LogicWire", Required = false },
                    ["fromX"] = new McpToolParameter { Type = "integer", Description = "起点 X", Required = false },
                    ["fromY"] = new McpToolParameter { Type = "integer", Description = "起点 Y", Required = false },
                    ["toX"] = new McpToolParameter { Type = "integer", Description = "终点 X", Required = false },
                    ["toY"] = new McpToolParameter { Type = "integer", Description = "终点 Y", Required = false },
                    ["fromQuery"] = new McpToolParameter { Type = "string", Description = "可选起点搜索词；wire 自动连接时可搜索已有电源/电线/建筑", Required = false },
                    ["toQuery"] = new McpToolParameter { Type = "string", Description = "可选终点搜索词；wire 自动连接时搜索要接电的设备", Required = false },
                    ["maxAutoConnectRadius"] = new McpToolParameter { Type = "integer", Description = "wire 缺省起点时，围绕目标电口搜索已有电线/输出端口的半径，默认 80，最大 200", Required = false },
                    ["points"] = new McpToolParameter { Type = "array", Description = "可选折线路径点数组，支持 [[x,y],...] 或 [{x,y},...]；提供后优先使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "建造材料 tag；auto/default 自动选择", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "建造优先级 1..9，默认 5", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，不生成蓝图", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最多处理路径格数，默认 200，最大 500", Required = false },
                    ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true，遇到自然固体自动标记挖掘", Required = false },
                    ["autoUprootObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true，遇到可铲植物自动标记铲除", Required = false },
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "最多自动标记挖掘/铲除格，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    bool dryRun = IsDryRun(args);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        prefabId = DefaultUtilityPrefab(args["type"]?.ToString());
                    var def = Assets.GetBuildingDef(prefabId);
                    if (def == null)
                        return CallToolResult.Error("Building def not found: " + prefabId);
                    if (!IsLinearUtilityPrefab(def.PrefabID))
                        return CallToolResult.Error("utility_auto_connect only supports linear utility prefabs such as Wire, LiquidConduit, GasConduit, SolidConduit and LogicWire");

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 200, 500));
                    string error;
                    var path = ResolveUtilityPath(args, maxCells, out error);
                    if (error != null)
                        return CallToolResult.Error(error);

                    if (path.Count > maxCells)
                        return CallToolResult.Error($"Path too large: {path.Count} cells, maxCells={maxCells}");

                    if (args["worldId"] == null)
                        args["worldId"] = ToolUtil.ResolveWorldId(args);
                    if (args["material"] == null)
                        args["material"] = "auto";
                    if (args["autoDigObstructions"] == null)
                        args["autoDigObstructions"] = true;
                    if (args["autoUprootObstructions"] == null)
                        args["autoUprootObstructions"] = true;

                    var results = new List<Dictionary<string, object>>();
                    var errors = new List<Dictionary<string, object>>();
                    var plannedSupportCells = new HashSet<int>();
                    var autoDigContext = AutoDigContext.FromArgs(args);
                    int planned = 0;
                    int reused = 0;
                    int valid = 0;
                    int autoMarked = 0;

                    foreach (var point in path)
                    {
                        var result = TryPlanOne(def.PrefabID, point.x, point.y, args, plannedSupportCells, autoDigContext);
                        bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                        bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                        bool alreadyConnected = result.ContainsKey("alreadyConnected") && (bool)result["alreadyConnected"];
                        autoMarked += GetAutoDigInt(result, "marked") + GetAutoDigInt(result, "uprootMarked") + GetAutoDigInt(result, "alreadyMarked") + GetAutoDigInt(result, "alreadyUprootMarked");
                        if (ok || (dryRun && validPlacement) || IsAutoDigResult(result))
                        {
                            valid++;
                            if (ok)
                                planned++;
                            if (alreadyConnected)
                                reused++;
                        }
                        else
                        {
                            errors.Add(result);
                        }
                        results.Add(result);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = def.PrefabID,
                        ["dryRun"] = dryRun,
                        ["committed"] = !dryRun && (planned > 0 || reused > 0 || autoMarked > 0),
                        ["pathCells"] = path.Count,
                        ["planned"] = planned,
                        ["reusedExisting"] = reused,
                        ["valid"] = valid,
                        ["autoMarkedObstructions"] = autoMarked,
                        ["failed"] = errors.Count,
                        ["autoDigLimitReached"] = autoDigContext.LimitReached,
                        ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                        ["errors"] = errors.Take(50).ToList(),
                        ["results"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FindPlacementCandidates()
        {
            return new McpTool
            {
                Name = "build_placement_candidates",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "placement_candidates", "footprint_candidates", "anchor_candidates", "build_anchor_candidates" },
                Tags = new List<string> { "buildings", "preview", "footprint", "placement", "anchor", "candidate", "layout", "建造", "候选", "空位", "支撑", "碰撞" },
                Description = "兼容入口：请使用 building_control domain=planning action=placement_candidates",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 MedicalCot、Tile、Wire", Required = true },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只考虑已揭示格子，默认 true", Required = false },
                    ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "是否允许 OnFloor 建筑缺少支撑时仍返回候选，默认 false", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "可选建材；留空或 auto 时使用自动选择", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "可选建筑外观/permit id", Required = false },
                    ["orientation"] = new McpToolParameter { Type = "string", Description = "可选朝向，默认 Neutral", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "建造优先级 1..9，默认 5", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个候选，默认 8，最大 50", Required = false },
                    ["includeRejected"] = new McpToolParameter { Type = "boolean", Description = "是否在没有足够可行候选时补充失败候选，默认 false", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大扫描格子数，默认 2500，硬上限 2500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required");

                    var def = Assets.GetBuildingDef(prefabId);
                    if (def == null)
                        return CallToolResult.Error("Building def not found");

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 2500, 2500));
                    var rect = ToolUtil.GetRect(args);
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (cells > maxCells)
                        return CallToolResult.Error($"Area too large: {width}x{height}={cells} cells, maxCells={maxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 8, 50));
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", true);
                    bool allowUnsupported = ToolUtil.GetBool(args, "allowUnsupported", false);
                    bool includeRejected = ToolUtil.GetBool(args, "includeRejected", false);

                    var scanArgs = (JObject)args.DeepClone();
                    scanArgs["dryRun"] = true;
                    scanArgs["worldId"] = worldId;
                    scanArgs["allowUnsupported"] = allowUnsupported;

                    var validCandidates = new List<PlacementCandidate>();
                    var rejectedCandidates = new List<PlacementCandidate>();
                    int scannedAnchors = 0;
                    int validCount = 0;
                    int warningCount = 0;
                    int rejectedCount = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            scannedAnchors++;
                            var previewArgs = (JObject)scanArgs.DeepClone();
                            var preview = TryPlanOne(prefabId, x, y, previewArgs);
                            bool valid = GetDictionaryBool(preview, "valid");
                            bool warningOnly = valid && GetNestedBool(preview, "support", "warningOnly");
                            int score = ScorePlacementCandidate(preview, valid, warningOnly);

                            var candidate = new PlacementCandidate
                            {
                                Score = score,
                                Status = valid ? (warningOnly ? "warning" : "valid") : "invalid",
                                Preview = preview
                            };
                            AddAnchorInfo(candidate.Anchor, args, x, y);

                            if (valid)
                            {
                                validCount++;
                                if (warningOnly)
                                    warningCount++;
                                validCandidates.Add(candidate);
                            }
                            else
                            {
                                rejectedCount++;
                                rejectedCandidates.Add(candidate);
                            }
                        }
                    }

                    var selected = validCandidates
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.AnchorY)
                        .ThenBy(item => item.AnchorX)
                        .Take(limit)
                        .ToList();

                    if (selected.Count < limit && (includeRejected || validCandidates.Count == 0))
                    {
                        selected.AddRange(rejectedCandidates
                            .OrderByDescending(item => item.Score)
                            .ThenBy(item => item.AnchorY)
                            .ThenBy(item => item.AnchorX)
                            .Take(limit - selected.Count));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["prefabId"] = prefabId,
                        ["name"] = ToolUtil.CleanName(def.Name),
                        ["worldId"] = worldId,
                        ["rect"] = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                        ["size"] = new[] { width, height },
                        ["scannedAnchors"] = scannedAnchors,
                        ["validCount"] = validCount,
                        ["warningCount"] = warningCount,
                        ["rejectedCount"] = rejectedCount,
                        ["limit"] = limit,
                        ["includeRejected"] = includeRejected,
                        ["candidates"] = selected.Select(item => item.ToDictionary()).ToList(),
                        ["bestCandidate"] = selected.Count > 0 ? (object)selected[0].ToDictionary() : null,
                        ["next"] = selected.Count == 0
                            ? "No viable anchor found. Use build_preview on a tighter rect or raise allowUnsupported/material settings."
                            : "Use build_preview on the top candidate, then build_area with dryRun=true or confirm=true if you want to place it."
                    }, McpJsonUtil.Settings));
                }
            };
        }

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
                var def = Assets.GetBuildingDef(prefabId);
                if (def == null)
                    return CallToolResult.Error("Building def not found");

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
                    int planned = 0;
                    int alreadyPresent = 0;
                    int alreadyConnected = 0;
                    int succeeded = 0;
                    int failed = 0;
                    int autoDigQueued = 0;
                    int autoDigAlreadyMarked = 0;
                    foreach (var anchor in allowPartial ? actionableAnchors : anchors)
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

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = prefabId,
                        ["dryRun"] = false,
                        ["committed"] = planned > 0 || autoDigQueued > 0,
                        ["allowPartial"] = allowPartial,
                        ["anchorResolution"] = anchorResolution,
                        ["planResolution"] = planResolution.ToDictionary(),
                        ["anchorCount"] = anchors.Count,
                        ["attempted"] = allowPartial ? actionableAnchors.Count : anchors.Count,
                        ["succeeded"] = succeeded,
                        ["planned"] = planned,
                        ["alreadyPresent"] = alreadyPresent,
                        ["alreadyConnected"] = alreadyConnected,
                        ["autoDigQueued"] = autoDigQueued,
                        ["autoDigAlreadyMarked"] = autoDigAlreadyMarked,
                        ["autoDigLimitReached"] = autoDigContext.LimitReached,
                        ["pendingBuildAfterDig"] = autoDigQueued + autoDigAlreadyMarked,
                        ["failed"] = failed + (allowPartial ? hardFailedPreviews.Count : 0),
                        ["allRequestedSatisfied"] = planned + alreadyPresent + alreadyConnected >= anchors.Count,
                        ["remainingAnchors"] = remainingAnchors.Select(anchor => AnchorDictionary(anchor.x, anchor.y, ToolUtil.ResolveWorldId(args))).ToList(),
                        ["remainingAction"] = BuildRemainingBuildAreaAction(args, prefabId, remainingAnchors),
                        ["preflightErrors"] = hardFailedPreviews.Take(50).ToList(),
                        ["results"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> BuildPlacementCandidate(Dictionary<string, object> preview, int x, int y, JObject args, int score, string status)
        {
            var candidate = new Dictionary<string, object>
            {
                ["score"] = score,
                ["status"] = status,
                ["anchor"] = new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y
                },
                ["preview"] = preview,
                ["placement"] = GetObject(preview, "placement"),
                ["footprint"] = GetObjectList(preview, "footprint"),
                ["support"] = GetObject(preview, "support"),
                ["materialSelection"] = GetObject(preview, "materialSelection"),
                ["facade"] = preview.ContainsKey("facade") ? preview["facade"] : null,
                ["error"] = preview.ContainsKey("error") ? preview["error"] : null
            };

            WorldEditor.AddRelativeInfo(candidate["anchor"] as Dictionary<string, object>, args, x, y);
            return candidate;
        }

        private static int ScorePlacementCandidate(Dictionary<string, object> preview, bool valid, bool warningOnly)
        {
            if (!valid)
            {
                int invalidPenalty = 200;
                if (preview != null && preview.ContainsKey("failureReason"))
                {
                    string reason = preview["failureReason"]?.ToString() ?? "";
                    if (reason == "unsupported")
                        invalidPenalty = 140;
                    else if (reason == "unavailableMaterial")
                        invalidPenalty = 160;
                    else if (reason == "obstructed")
                        invalidPenalty = 180;
                }
                return -invalidPenalty;
            }

            int score = 100;
            var support = GetObject(preview, "support");
            var footprint = GetObjectList(preview, "footprint");
            var placement = GetObject(preview, "placement");
            int width = GetInt(placement, "width");
            int height = GetInt(placement, "height");

            score -= Math.Max(0, footprint.Count - Math.Max(1, width * height)) * 2;

            if (warningOnly)
                score -= 10;

            var missingSupport = GetObjectList(support, "missingSupportCells");
            score -= missingSupport.Count * 12;

            var obstructions = GetObjectList(preview, "obstructions");
            score -= obstructions.Count * 25;

            if (GetBool(support, "valid"))
                score += 10;
            if (!GetBool(support, "warningOnly"))
                score += 5;

            return score;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static List<Dictionary<string, object>> GetObjectList(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as List<Dictionary<string, object>> : null ?? new List<Dictionary<string, object>>();
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool SameAnchor(Dictionary<string, object> result, CellCoord anchor)
        {
            return GetInt(result, "x") == anchor.x && GetInt(result, "y") == anchor.y;
        }

        private static Dictionary<string, object> BuildRemainingBuildAreaAction(JObject args, string prefabId, List<CellCoord> anchors)
        {
            if (anchors == null || anchors.Count == 0)
                return null;

            var arguments = new Dictionary<string, object>
            {
                ["domain"] = "planning",
                ["action"] = "build_area",
                ["prefabId"] = prefabId,
                ["worldId"] = ToolUtil.ResolveWorldId(args),
                ["anchors"] = anchors
                    .Select(anchor => new Dictionary<string, object> { ["x"] = anchor.x, ["y"] = anchor.y })
                    .ToList(),
                ["confirm"] = true,
                ["dryRun"] = false,
                ["allowPartial"] = true
            };

            CopyIfPresent(args, arguments, "material");
            CopyIfPresent(args, arguments, "facade");
            CopyIfPresent(args, arguments, "facadeId");
            CopyIfPresent(args, arguments, "orientation");
            CopyIfPresent(args, arguments, "priority");
            CopyIfPresent(args, arguments, "allowUnsupported");
            CopyIfPresent(args, arguments, "autoConnectPower");
            CopyIfPresent(args, arguments, "maxAutoConnectRadius");

            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = arguments,
                ["note"] = "Retry only anchors that are not yet built/blueprinted/connected. If autoDigQueued > 0, run after dig chores finish."
            };
        }

        private static void CopyIfPresent(JObject source, Dictionary<string, object> target, string key)
        {
            if (source == null || target == null || source[key] == null)
                return;
            if (source[key].Type == JTokenType.Boolean)
                target[key] = source[key].Value<bool>();
            else if (source[key].Type == JTokenType.Integer)
                target[key] = source[key].Value<int>();
            else
                target[key] = source[key].ToString();
        }

        private static bool GetDictionaryBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool GetNestedBool(Dictionary<string, object> dict, string parentKey, string childKey)
        {
            var parent = GetObject(dict, parentKey);
            return GetBool(parent, childKey);
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return 0;
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static bool IsAutoDiggableFailure(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetBool(autoDig, "available") && GetInt(autoDig, "targetCount") > 0;
        }

        private static bool IsAutoDigResult(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetInt(autoDig, "marked") > 0
                || GetInt(autoDig, "alreadyMarked") > 0
                || GetInt(autoDig, "uprootMarked") > 0
                || GetInt(autoDig, "alreadyUprootMarked") > 0;
        }

        private static int GetAutoDigInt(Dictionary<string, object> result, string key)
        {
            return GetInt(GetObject(result, "autoDig"), key);
        }

        private static void AddAnchorInfo(Dictionary<string, object> anchor, JObject args, int x, int y)
        {
            var area = WorldEditor.ResolveRelativeArea(args);
            if (area == null)
                return;

            anchor["areaId"] = area.Id;
            anchor["rx"] = x - area.X1;
            anchor["ry"] = y - area.Y1;
            anchor["origin"] = new[] { area.X1, area.Y1 };
            anchor["coordMode"] = "relative";
        }

        private static string DefaultUtilityPrefab(string type)
        {
            switch ((type ?? "wire").Trim().ToLowerInvariant())
            {
                case "liquid":
                case "water":
                case "pipe":
                    return "LiquidConduit";
                case "gas":
                    return "GasConduit";
                case "solid":
                case "conveyor":
                case "shipping":
                    return "SolidConduit";
                case "logic":
                case "automation":
                case "signal":
                    return "LogicWire";
                default:
                    return "Wire";
            }
        }

        private static List<CellCoord> ResolveUtilityPath(JObject args, int maxCells, out string error)
        {
            error = null;
            var points = ParsePathPoints(args["points"]);
            if (points.Count == 0)
            {
                int? fromX = ToolUtil.GetInt(args, "fromX");
                int? fromY = ToolUtil.GetInt(args, "fromY");
                int? toX = ToolUtil.GetInt(args, "toX");
                int? toY = ToolUtil.GetInt(args, "toY");
                if (!fromX.HasValue || !fromY.HasValue || !toX.HasValue || !toY.HasValue)
                {
                    if (!TryResolveAutoUtilityEndpoints(args, out points, out error))
                        return new List<CellCoord>();
                }
                else
                {
                    points.Add(new CellCoord(fromX.Value, fromY.Value));
                    points.Add(new CellCoord(toX.Value, toY.Value));
                }
            }

            if (points.Count < 2)
            {
                error = "At least two path points are required";
                return new List<CellCoord>();
            }

            var path = new List<CellCoord>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (!TryAddManhattanSegment(path, points[i], points[i + 1], maxCells, out error))
                    return path;
            }
            return path;
        }

        private static bool TryResolveAutoUtilityEndpoints(JObject args, out List<CellCoord> points, out string error)
        {
            points = new List<CellCoord>();
            error = null;

            string prefabId = args["prefabId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(prefabId) && prefabId.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = "Provide either points or fromX/fromY/toX/toY for non-wire utility auto_connect";
                return false;
            }

            int worldId = ToolUtil.ResolveWorldId(args);
            string toQuery = FirstNonEmpty(args["toQuery"], args["targetQuery"], args["query"], args["target"], args["search"], args["name"]);
            if (string.IsNullOrWhiteSpace(toQuery))
            {
                error = "Provide points, fromX/fromY/toX/toY, or query/toQuery for one-call wire auto_connect";
                return false;
            }

            int toCell;
            string toError;
            if (!TryResolvePowerEndpointCell(toQuery, worldId, preferOutput: false, out toCell, out toError))
            {
                error = toError;
                return false;
            }

            int fromCell;
            string fromQuery = FirstNonEmpty(args["fromQuery"], args["sourceQuery"], args["source"], args["from"]);
            if (!string.IsNullOrWhiteSpace(fromQuery))
            {
                string fromError;
                if (!TryResolvePowerEndpointCell(fromQuery, worldId, preferOutput: true, out fromCell, out fromError))
                {
                    error = fromError;
                    return false;
                }
            }
            else if (!TryFindNearestWireOrPowerOutput(toCell, worldId, ToolUtil.GetInt(args, "maxAutoConnectRadius") ?? 80, out fromCell))
            {
                error = $"No connected power output found near '{toQuery}'. Provide fromQuery/fromX/fromY for a powered source.";
                return false;
            }

            points.Add(CellCoordFromCell(fromCell));
            points.Add(CellCoordFromCell(toCell));
            return true;
        }

        private static bool TryResolvePowerEndpointCell(string query, int worldId, bool preferOutput, out int cell, out string error)
        {
            cell = Grid.InvalidCell;
            error = null;
            GameObject best = null;
            int bestScore = int.MinValue;

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;

                var def = building.Def;
                if (def == null || (!def.RequiresPowerInput && !def.RequiresPowerOutput))
                    continue;

                int score = PowerEndpointScore(building.gameObject, def, query, preferOutput);
                if (score > bestScore)
                {
                    best = building.gameObject;
                    bestScore = score;
                }
            }

            if (best == null || bestScore <= 0)
            {
                error = $"No built power endpoint instance matched '{query}'. Use read_control domain=buildings action=list query={query} to inspect existing targets, or provide explicit fromX/fromY/toX/toY.";
                return false;
            }

            var bestBuilding = best.GetComponent<Building>();
            var bestDef = bestBuilding?.Def;
            if (bestBuilding == null || bestDef == null)
            {
                error = $"Matched '{query}' but could not inspect building ports";
                return false;
            }

            int preferred = preferOutput && bestDef.RequiresPowerOutput
                ? bestBuilding.GetPowerOutputCell()
                : bestBuilding.GetPowerInputCell();
            int fallback = preferOutput ? bestBuilding.GetPowerInputCell() : bestBuilding.GetPowerOutputCell();
            cell = Grid.IsValidCell(preferred) ? preferred : fallback;
            if (!Grid.IsValidCell(cell))
            {
                error = $"Matched '{query}' but it has no valid power port cell";
                return false;
            }
            return true;
        }

        private static int PowerEndpointScore(GameObject go, BuildingDef def, string query, bool preferOutput)
        {
            if (go == null || def == null || string.IsNullOrWhiteSpace(query))
                return 0;

            int score = 0;
            var kpid = go.GetComponent<KPrefabID>();
            score = Math.Max(score, SimpleMatchScore(def.PrefabID, query));
            score = Math.Max(score, SimpleMatchScore(kpid?.PrefabTag.Name, query));
            score = Math.Max(score, SimpleMatchScore(ToolUtil.CleanName(go.GetProperName()), query));
            score = Math.Max(score, SimpleMatchScore(go.name, query));
            if (score > 0)
            {
                if (preferOutput && def.RequiresPowerOutput)
                    score += 40;
                if (!preferOutput && def.RequiresPowerInput)
                    score += 40;
            }
            return score;
        }

        private static int SimpleMatchScore(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
                return 0;
            if (string.Equals(value.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase))
                return 1000;
            if (value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 700;

            string normalizedValue = NormalizeBuildSearchText(value);
            string normalizedQuery = NormalizeBuildSearchText(query);
            if (normalizedValue == normalizedQuery)
                return 950;
            return normalizedValue.Contains(normalizedQuery) ? 650 : 0;
        }

        private static string NormalizeBuildSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var chars = new List<char>(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }
            return new string(chars.ToArray());
        }

        private static bool TryFindNearestWireOrPowerOutput(int targetCell, int worldId, int radius, out int cell)
        {
            cell = Grid.InvalidCell;
            if (!Grid.IsValidCell(targetCell))
                return false;

            radius = Math.Max(1, Math.Min(radius, 200));
            int targetX = Grid.CellColumn(targetCell);
            int targetY = Grid.CellRow(targetCell);
            int bestDistance = int.MaxValue;

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.Def == null || !building.Def.RequiresPowerOutput)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                if (!TryGetConnectedCircuitId(building.gameObject, out _))
                    continue;

                int candidate = building.GetPowerOutputCell();
                if (!Grid.IsValidCell(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                int x = Grid.CellColumn(candidate);
                int y = Grid.CellRow(candidate);
                int distance = Math.Abs(x - targetX) + Math.Abs(y - targetY);
                if (distance > radius)
                    continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    cell = candidate;
                }
            }

            return Grid.IsValidCell(cell);
        }

        private static bool TryGetConnectedCircuitId(GameObject go, out ushort circuitId)
        {
            circuitId = ushort.MaxValue;
            if (go == null)
                return false;

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                var type = component.GetType();
                var property = type.GetProperty("CircuitID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (TryReadCircuitId(property == null ? null : property.GetValue(component, null), out circuitId))
                    return true;
                var field = type.GetField("CircuitID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (TryReadCircuitId(field == null ? null : field.GetValue(component), out circuitId))
                    return true;
            }
            return false;
        }

        private static bool TryReadCircuitId(object value, out ushort circuitId)
        {
            circuitId = ushort.MaxValue;
            if (value == null)
                return false;
            try
            {
                circuitId = Convert.ToUInt16(value);
                return circuitId != ushort.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static CellCoord CellCoordFromCell(int cell)
        {
            return new CellCoord(Grid.CellColumn(cell), Grid.CellRow(cell));
        }

        private static string FirstNonEmpty(params JToken[] values)
        {
            foreach (var value in values)
            {
                string text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
            return null;
        }

        private static bool TryBuildAutoConnectArgs(JObject args, string prefabId, out JObject connectArgs)
        {
            connectArgs = args == null ? new JObject() : (JObject)args.DeepClone();
            connectArgs["prefabId"] = prefabId;
            connectArgs["action"] = "auto_connect";

            if (connectArgs["points"] != null)
                return true;

            var anchors = args?["anchors"] as JArray;
            if (anchors != null && anchors.Count >= 2)
            {
                connectArgs["points"] = anchors.DeepClone();
                return true;
            }

            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? x1 = ToolUtil.GetInt(args, "x1");
            int? y1 = ToolUtil.GetInt(args, "y1");
            int? x2 = ToolUtil.GetInt(args, "x2");
            int? y2 = ToolUtil.GetInt(args, "y2");

            if (x.HasValue && y.HasValue && x2.HasValue && y2.HasValue)
            {
                connectArgs["fromX"] = x.Value;
                connectArgs["fromY"] = y.Value;
                connectArgs["toX"] = x2.Value;
                connectArgs["toY"] = y2.Value;
                return true;
            }

            if (x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue)
            {
                connectArgs["fromX"] = x1.Value;
                connectArgs["fromY"] = y1.Value;
                connectArgs["toX"] = x2.Value;
                connectArgs["toY"] = y2.Value;
                return true;
            }

            if (HasAutoConnectQuery(args))
                return true;

            return false;
        }

        private static bool HasAutoConnectQuery(JObject args)
        {
            return !string.IsNullOrWhiteSpace(FirstNonEmpty(
                args?["toQuery"],
                args?["targetQuery"],
                args?["fromQuery"],
                args?["sourceQuery"],
                args?["query"],
                args?["target"],
                args?["search"],
                args?["name"]));
        }

        private static List<CellCoord> ParsePathPoints(JToken token)
        {
            var result = new List<CellCoord>();
            var array = token as JArray;
            if (array == null)
                return result;

            foreach (var item in array)
            {
                int? x = null;
                int? y = null;
                var pair = item as JArray;
                if (pair != null && pair.Count >= 2)
                {
                    int parsedX;
                    int parsedY;
                    if (int.TryParse(pair[0]?.ToString(), out parsedX) && int.TryParse(pair[1]?.ToString(), out parsedY))
                    {
                        x = parsedX;
                        y = parsedY;
                    }
                }
                else
                {
                    var obj = item as JObject;
                    if (obj != null)
                    {
                        int parsedX;
                        int parsedY;
                        if (int.TryParse(obj["x"]?.ToString(), out parsedX) && int.TryParse(obj["y"]?.ToString(), out parsedY))
                        {
                            x = parsedX;
                            y = parsedY;
                        }
                    }
                }

                if (x.HasValue && y.HasValue)
                    result.Add(new CellCoord(x.Value, y.Value));
            }
            return result;
        }

        private static bool TryAddManhattanSegment(List<CellCoord> path, CellCoord from, CellCoord to, int maxCells, out string error)
        {
            error = null;
            int x = from.x;
            int y = from.y;
            AddPathPoint(path, x, y);
            if (path.Count > maxCells)
            {
                error = $"Path too large: {path.Count} cells, maxCells={maxCells}";
                return false;
            }
            while (x != to.x)
            {
                x += to.x > x ? 1 : -1;
                AddPathPoint(path, x, y);
                if (path.Count > maxCells)
                {
                    error = $"Path too large: {path.Count} cells, maxCells={maxCells}";
                    return false;
                }
            }
            while (y != to.y)
            {
                y += to.y > y ? 1 : -1;
                AddPathPoint(path, x, y);
                if (path.Count > maxCells)
                {
                    error = $"Path too large: {path.Count} cells, maxCells={maxCells}";
                    return false;
                }
            }
            return true;
        }

        private static void AddPathPoint(List<CellCoord> path, int x, int y)
        {
            if (path.Count > 0 && path[path.Count - 1].x == x && path[path.Count - 1].y == y)
                return;
            path.Add(new CellCoord(x, y));
        }

        internal static CallToolResult PlanAtPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            string prefabId = args["prefabId"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabId))
                return CallToolResult.Error("prefabId is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

            Grid.CellToXY(pointer.Cell, out int x, out int y);
            if (args["worldId"] == null && pointer.WorldId >= 0)
                args["worldId"] = pointer.WorldId;
            var result = TryPlanOne(prefabId, x, y, args);
            result["pointer"] = pointer.ToDictionary();
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        internal static CallToolResult DragLineFromPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

            int? requestedLength = ToolUtil.GetInt(args, "length");
            if (!requestedLength.HasValue || requestedLength.Value <= 0)
                return CallToolResult.Error("length must be a positive integer");
            int length = Math.Max(1, Math.Min(requestedLength.Value, 200));

            string direction = (args["direction"]?.ToString() ?? "").Trim().ToLowerInvariant();
            int dx;
            int dy;
            if (!TryDirection(direction, out dx, out dy))
                return CallToolResult.Error("direction must be right, left, up or down");

            string prefabId = string.IsNullOrWhiteSpace(args["prefabId"]?.ToString()) ? "Wire" : args["prefabId"].ToString();
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
                return CallToolResult.Error("Building def not found");

            var dragPolicy = BuildDragPolicy(def, args);
            if (!dragPolicy.Allowed)
                return CallToolResult.Error(JsonConvert.SerializeObject(dragPolicy.ToDictionary(), McpJsonUtil.Settings));

            Grid.CellToXY(pointer.Cell, out int startX, out int startY);
            int endX = startX + dx * (length - 1);
            int endY = startY + dy * (length - 1);
            int worldId = pointer.WorldId >= 0 ? pointer.WorldId : ToolUtil.ResolveWorldId(args);
            int endCell = Grid.XYToCell(endX, endY);
            if (!Grid.IsValidCell(endCell))
                return CallToolResult.Error("Drag end cell is outside the grid");
            if (!ToolUtil.CellMatchesWorld(endCell, worldId))
                return CallToolResult.Error($"Drag end cell is not in worldId={worldId}");
            if (args["worldId"] == null && worldId >= 0)
                args["worldId"] = worldId;

            var results = new List<Dictionary<string, object>>();
            var errors = new List<Dictionary<string, object>>();
            var plannedSupportCells = new HashSet<int>();
            var autoDigContext = AutoDigContext.FromArgs(args);
            AgentPointerRegistry.BeginDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), worldId, pointer.Cell, prefabId);

            int planned = 0;
            int valid = 0;
            int autoDigQueued = 0;
            foreach (var cell in StraightLineCells(startX, startY, endX, endY))
            {
                AgentPointerRegistry.UpdateDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), Grid.XYToCell(cell.x, cell.y));
                var result = TryPlanOne(prefabId, cell.x, cell.y, args, plannedSupportCells, autoDigContext);
                bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                autoDigQueued += GetAutoDigInt(result, "marked");
                if (ok || (IsDryRun(args) && validPlacement))
                {
                    valid++;
                    if (ok)
                        planned++;
                    RegisterSupportBlueprint(prefabId, cell.x, cell.y, plannedSupportCells);
                }
                else if (IsAutoDigResult(result))
                {
                    valid++;
                }
                else
                {
                    errors.Add(result);
                }
                results.Add(result);
            }

            var finalPointer = AgentPointerRegistry.EndDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), endCell);
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["prefabId"] = prefabId,
                ["dragPolicy"] = dragPolicy.ToDictionary(),
                ["dryRun"] = IsDryRun(args),
                ["drag"] = new Dictionary<string, object>
                {
                    ["from"] = new { x = startX, y = startY },
                    ["to"] = new { x = endX, y = endY },
                    ["direction"] = direction,
                    ["length"] = length,
                    ["mouseButton"] = "left",
                    ["gesture"] = "long_press_drag_line"
                },
                ["valid"] = valid,
                ["planned"] = planned,
                ["autoDigQueued"] = autoDigQueued,
                ["autoDigLimitReached"] = autoDigContext.LimitReached,
                ["failed"] = errors.Count,
                ["errors"] = errors.Take(50).ToList(),
                ["pointer"] = finalPointer.ToDictionary(),
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

        private static Dictionary<string, object> TryPlanOne(string prefabId, int x, int y, JObject args, HashSet<int> plannedSupportCells = null, AutoDigContext autoDigContext = null)
        {
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
                return ErrorResult(prefabId, x, y, "Building def not found");

            int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidBuildingCell(cell) || !Grid.IsVisible(cell))
                return ErrorResult(prefabId, x, y, "Invalid or not visible cell");

            int worldId = ToolUtil.ResolveWorldId(args);
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return ErrorResult(prefabId, x, y, $"Cell is not in worldId={worldId}");

            var orientation = ParseOrientation(args["orientation"]?.ToString());
            var earlyPlacement = BuildPlacementDetails(def, x, y, worldId);
            var earlyExistingBuild = ExistingMatchingBuildAtPlacement(def, earlyPlacement);
            if (earlyExistingBuild != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = earlyPlacement.ToDictionary(),
                    ["footprint"] = earlyPlacement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = earlyExistingBuild
                };
            }
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            materialResult.RequiredKg = RequiredMaterialKg(def);
            if (!materialResult.Valid)
                return ErrorResult(prefabId, x, y, materialResult.Error, materialResult.ToDictionary());

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
                return ErrorResult(prefabId, x, y, facadeResult.Error);

            var supportResult = ValidateSupport(def, x, y, ToolUtil.GetBool(args, "allowUnsupported", false), plannedSupportCells);
            if (!supportResult.Valid)
                return ErrorResult(prefabId, x, y, supportResult.Error, supportResult.ToDictionary());

            var placement = BuildPlacementDetails(def, x, y, worldId);
            var footprintResult = ValidateFootprint(placement);
            var existingBuild = ExistingMatchingBuildAtPlacement(def, placement);
            if (existingBuild != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(existingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(existingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = existingBuild,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId
                };
            }
            Dictionary<string, object> autoDig = null;
            if (!footprintResult.Valid)
            {
                var details = footprintResult.ToDictionary(placement);
                autoDig = TryAutoDigObstructions(placement, footprintResult, args, autoDigContext);
                if (autoDig == null)
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
                details["autoDig"] = autoDig;
                if (!GetBool(autoDig, "available"))
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
            }

            if (IsDryRun(args))
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                var powerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["actualAnchor"] = null,
                    ["valid"] = true,
                    ["dryRun"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId,
                    ["powerAutoConnect"] = powerAutoConnect,
                    ["autoDig"] = autoDig
                };
            }

            var existingUtility = ExistingMatchingUtilityAtPlacement(def, placement);
            if (existingUtility != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = true,
                    ["blueprintPlaced"] = false,
                    ["alreadyConnected"] = true,
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existingUtility"] = existingUtility,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
            ["materialSelection"] = materialResult.ToDictionary(),
            ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId,
                    ["autoDig"] = autoDig
                };
            }

            var pos = BuildPlacementPosition(cell, def);
            var go = def.TryPlace(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId);
            Dictionary<string, object> fallbackPlacement = null;
            if (go == null && autoDig != null)
                go = TryPlaceWithBuildTool(def, cell, orientation, materialResult.Elements, facadeResult.ResponseId, placement, out fallbackPlacement);
            if (go == null)
            {
                var failureDetails = BuildPlacementFailureDetails(placement, materialResult);
                if (autoDig != null)
                    failureDetails["autoDig"] = autoDig;
                if (fallbackPlacement != null)
                    failureDetails["fallbackPlacement"] = fallbackPlacement;
                return ErrorResult(prefabId, x, y, "Placement failed", failureDetails);
            }

            SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
            RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
            var actualPlacement = ActualPlacementDetails(go, def, x, y);
            var placedPowerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
            return new Dictionary<string, object>
            {
                ["planned"] = true,
                ["blueprintPlaced"] = true,
                ["valid"] = true,
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = x,
                ["y"] = y,
                ["anchor"] = AnchorDictionary(x, y, worldId),
                ["worldId"] = worldId,
                ["placement"] = placement.ToDictionary(),
                ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                ["actualPlacement"] = actualPlacement,
                ["actualAnchor"] = ActualAnchorArray(actualPlacement),
                ["placementCheck"] = ComparePlacement(placement, actualPlacement),
                ["fallbackPlacement"] = fallbackPlacement,
                ["support"] = supportResult.ToDictionary(),
                ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["facade"] = facadeResult.ResponseId,
                ["powerAutoConnect"] = placedPowerAutoConnect,
                ["autoDig"] = autoDig,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static Dictionary<string, object> TryAutoConnectPower(
            BuildingDef def,
            int anchorX,
            int anchorY,
            Orientation orientation,
            JObject args,
            HashSet<int> plannedSupportCells,
            AutoDigContext autoDigContext)
        {
            bool enabled = def != null
                && def.RequiresPowerInput
                && ToolUtil.GetBool(args, "autoConnectPower", true);
            if (!enabled)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = false,
                    ["reason"] = def == null || !def.RequiresPowerInput ? "building_has_no_power_input" : "disabled_by_argument"
                };
            }

            int worldId = ToolUtil.ResolveWorldId(args);
            int inputCell = PowerInputCell(def, anchorX, anchorY, orientation);
            if (!Grid.IsValidCell(inputCell) || !ToolUtil.CellMatchesWorld(inputCell, worldId))
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "no_valid_power_input_cell",
                    ["planned"] = 0,
                    ["failed"] = 0
                };
            }

            int sourceCell;
            int radius = ToolUtil.GetInt(args, "maxAutoConnectRadius") ?? 80;
            if (!TryFindNearestWireOrPowerOutput(inputCell, worldId, radius, out sourceCell))
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "no_connected_power_source",
                    ["input"] = CellCoordDictionary(inputCell),
                    ["planned"] = 0,
                    ["failed"] = 0,
                    ["next"] = "Connect a power producer/output first, provide fromQuery/fromX/fromY for a powered source, or pass autoConnectPower=false."
                };
            }

            int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 200, 500));
            string pathError;
            var path = new List<CellCoord>();
            if (!TryAddManhattanSegment(path, CellCoordFromCell(sourceCell), CellCoordFromCell(inputCell), maxCells, out pathError))
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "path_too_large",
                    ["input"] = CellCoordDictionary(inputCell),
                    ["source"] = CellCoordDictionary(sourceCell),
                    ["pathCells"] = path.Count,
                    ["maxCells"] = maxCells,
                    ["planned"] = 0,
                    ["failed"] = 0,
                    ["error"] = pathError
                };
            }
            if (path.Count > maxCells)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "path_too_large",
                    ["input"] = CellCoordDictionary(inputCell),
                    ["source"] = CellCoordDictionary(sourceCell),
                    ["pathCells"] = path.Count,
                    ["maxCells"] = maxCells,
                    ["planned"] = 0,
                    ["failed"] = 0
                };
            }

            var wireArgs = (JObject)args.DeepClone();
            wireArgs["prefabId"] = "Wire";
            wireArgs["material"] = args["wireMaterial"] ?? args["material"] ?? new JValue("auto");
            wireArgs["autoConnectPower"] = false;
            wireArgs["confirm"] = true;

            var results = new List<Dictionary<string, object>>();
            int planned = 0;
            int reused = 0;
            int failed = 0;
            int autoDigQueued = 0;
            foreach (var point in path)
            {
                var result = TryPlanOne("Wire", point.x, point.y, wireArgs, plannedSupportCells, autoDigContext);
                bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                bool alreadyConnected = result.ContainsKey("alreadyConnected") && (bool)result["alreadyConnected"];
                autoDigQueued += GetAutoDigInt(result, "marked");
                if (ok)
                    planned++;
                else if (alreadyConnected)
                    reused++;
                else if (!IsAutoDigResult(result))
                    failed++;
                if (results.Count < 50)
                    results.Add(result);
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["status"] = failed == 0 ? "connected_or_planned" : planned + reused > 0 ? "partially_connected" : "failed",
                ["dryRun"] = IsDryRun(args),
                ["input"] = CellCoordDictionary(inputCell),
                ["source"] = CellCoordDictionary(sourceCell),
                ["pathCells"] = path.Count,
                ["planned"] = planned,
                ["reused"] = reused,
                ["failed"] = failed,
                ["autoDigQueued"] = autoDigQueued,
                ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                ["results"] = results
            };
        }

        private static int PowerInputCell(BuildingDef def, int anchorX, int anchorY, Orientation orientation)
        {
            int anchorCell = Grid.XYToCell(anchorX, anchorY);
            if (!Grid.IsValidCell(anchorCell) || def == null)
                return Grid.InvalidCell;
            CellOffset rotated = Rotatable.GetRotatedCellOffset(def.PowerInputOffset, orientation);
            return Grid.OffsetCell(anchorCell, rotated);
        }

        private static Dictionary<string, object> CellCoordDictionary(int cell)
        {
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static IEnumerable<CellCoord> LineCells(int x1, int y1, int x2, int y2, HashSet<string> seen)
        {
            int dx = x2 == x1 ? 0 : x2 > x1 ? 1 : -1;
            int dy = y2 == y1 ? 0 : y2 > y1 ? 1 : -1;
            int x = x1;
            int y = y1;
            while (true)
            {
                string key = x + "," + y;
                if (seen.Add(key))
                    yield return new CellCoord(x, y);
                if (x == x2 && y == y2)
                    yield break;
                if (x != x2)
                    x += dx;
                else if (y != y2)
                    y += dy;
            }
        }

        private static IEnumerable<CellCoord> StraightLineCells(int x1, int y1, int x2, int y2)
        {
            var seen = new HashSet<string>();
            foreach (var cell in LineCells(x1, y1, x2, y2, seen))
                yield return cell;
        }

        private static bool TryDirection(string direction, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            switch ((direction ?? "").Trim().ToLowerInvariant())
            {
                case "right":
                case "east":
                case "e":
                    dx = 1;
                    return true;
                case "left":
                case "west":
                case "w":
                    dx = -1;
                    return true;
                case "up":
                case "north":
                case "n":
                    dy = 1;
                    return true;
                case "down":
                case "south":
                case "s":
                    dy = -1;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDryRun(JObject args)
        {
            return ToolUtil.GetBool(args, "dryRun", false) || ToolUtil.GetBool(args, "validateOnly", false);
        }

        private static Dictionary<string, McpToolParameter> BuildPlacementParameters(bool includeConfirm, bool includeArea)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 MedicalCot、Tile、Wire；也可省略并用 plan/blueprint/sequence 解析", Required = false },
                ["plan"] = new McpToolParameter { Type = "string", Description = "文字建造序列/短语，解析成 prefabId/material/query，例如 粉砂岩砖@氧气、Wire-小型冰箱、用铜矿造手动发电机在电池旁", Required = false },
                ["blueprint"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                ["sequence"] = new McpToolParameter { Type = "string", Description = "plan 的别名；用于搜索/行动一体化时传文字序列", Required = false },
                ["text"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "lowerLeftCell anchor X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "lowerLeftCell anchor Y", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "坐标省略时按对象/元素/复制人名称搜索定位 anchor", Required = false },
                ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名：搜索目标名称、prefabId、元素或复制人", Required = false },
                ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名：搜索目标名称、prefabId、元素或复制人", Required = false },
                ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
                ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
                ["offsetX"] = new McpToolParameter { Type = "integer", Description = "搜索/x,y 解析出 anchor 后的 X 偏移；别名 dx。用于一次调用内按搜索结果相对放置蓝图", Required = false },
                ["offsetY"] = new McpToolParameter { Type = "integer", Description = "搜索/x,y 解析出 anchor 后的 Y 偏移；别名 dy。用于一次调用内按搜索结果相对放置蓝图", Required = false },
                ["dx"] = new McpToolParameter { Type = "integer", Description = "offsetX 的短别名", Required = false },
                ["dy"] = new McpToolParameter { Type = "integer", Description = "offsetY 的短别名", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界或 areaId 绑定世界", Required = false },
                ["material"] = new McpToolParameter { Type = "string", Description = "建造材料 tag；auto/default 自动选择", Required = false },
                ["facade"] = new McpToolParameter { Type = "string", Description = "可选建筑外观/permit id", Required = false },
                ["orientation"] = new McpToolParameter { Type = "string", Description = "可选朝向，默认 Neutral", Required = false },
                ["priority"] = new McpToolParameter { Type = "integer", Description = "建造优先级 1..9，默认 5", Required = false },
                ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "默认 false；OnFloor 建筑无支撑时拒绝", Required = false },
                ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true。建造 footprint 遇到可挖自然固体时，执行建造会先自动标记挖掘，并继续尝试在同一格放置建造蓝图", Required = false },
                ["autoUprootObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true。建造 footprint 遇到可铲植物时自动标记铲除，并继续尝试放置建造蓝图", Required = false },
                ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "单次命令最多自动标记多少个挖掘/铲除格，默认 100，最大 500", Required = false },
                ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，不生成蓝图", Required = false }
            };

            if (includeConfirm)
                parameters["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false };
            if (includeArea)
            {
                parameters["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；relative=true 时坐标按区域 rx/ry 解释", Required = false };
                parameters["relative"] = new McpToolParameter { Type = "boolean", Description = "配合 areaId 使用相对坐标", Required = false };
            }

            return parameters;
        }

        private static bool TryResolvePoint(JObject args, out int x, out int y, out string error)
        {
            x = 0;
            y = 0;
            error = null;
            int? requestedX = ToolUtil.GetInt(args, "x");
            int? requestedY = ToolUtil.GetInt(args, "y");
            if (!requestedX.HasValue || !requestedY.HasValue)
            {
                if (!ToolUtil.TryResolveSearchCell(args, out x, out y, out error))
                    return false;
                ApplyAnchorOffset(args, ref x, ref y);
                return true;
            }

            var area = WorldEditor.ResolveRelativeArea(args);
            if (area != null)
            {
                var absolute = WorldEditor.ToAbsoluteCell(requestedX.Value, requestedY.Value, area);
                x = absolute.x;
                y = absolute.y;
            }
            else
            {
                x = requestedX.Value;
                y = requestedY.Value;
            }

            ApplyAnchorOffset(args, ref x, ref y);
            return true;
        }

        private static void ApplyAnchorOffset(JObject args, ref int x, ref int y)
        {
            x += ToolUtil.GetInt(args, "offsetX") ?? ToolUtil.GetInt(args, "dx") ?? 0;
            y += ToolUtil.GetInt(args, "offsetY") ?? ToolUtil.GetInt(args, "dy") ?? 0;
        }

        private static Dictionary<string, object> BuildAnchorResolution(JObject args, List<CellCoord> anchors)
        {
            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["search"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["name"]?.ToString();

            int offsetX = ToolUtil.GetInt(args, "offsetX") ?? ToolUtil.GetInt(args, "dx") ?? 0;
            int offsetY = ToolUtil.GetInt(args, "offsetY") ?? ToolUtil.GetInt(args, "dy") ?? 0;
            var first = anchors != null && anchors.Count > 0
                ? new Dictionary<string, object> { ["x"] = anchors[0].x, ["y"] = anchors[0].y }
                : null;

            return new Dictionary<string, object>
            {
                ["mode"] = anchors != null && anchors.Count > 1
                    ? "anchors"
                    : string.IsNullOrWhiteSpace(query) ? "coordinate_or_area" : "search_anchor",
                ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                ["offset"] = new Dictionary<string, int> { ["x"] = offsetX, ["y"] = offsetY },
                ["anchorCount"] = anchors?.Count ?? 0,
                ["firstAnchor"] = first,
                ["oneCallSearchAndAction"] = !string.IsNullOrWhiteSpace(query)
            };
        }

        private static List<CellCoord> ResolveAnchors(JObject args, BuildingDef def, out string error)
        {
            error = null;
            var anchors = ParseAnchorArray(args, out error);
            if (error != null)
                return anchors;
            if (anchors.Count > 0)
                return anchors;

            int x;
            int y;
            if (TryResolvePoint(args, out x, out y, out error))
                return new List<CellCoord> { new CellCoord(x, y) };
            error = null;

            bool hasRect = !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || ToolUtil.GetInt(args, "x1").HasValue
                || ToolUtil.GetInt(args, "y1").HasValue
                || ToolUtil.GetInt(args, "x2").HasValue
                || ToolUtil.GetInt(args, "y2").HasValue;
            if (!hasRect)
            {
                error = "anchors, x/y, areaId, or x1/y1/x2/y2 are required";
                return anchors;
            }

            var rect = ToolUtil.GetRect(args);
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            bool dense = ToolUtil.GetBool(args, "dense", width == 1 && height == 1);
            int stepX = Math.Max(1, ToolUtil.GetInt(args, "stepX") ?? (dense ? 1 : width));
            int stepY = Math.Max(1, ToolUtil.GetInt(args, "stepY") ?? (dense ? 1 : height));

            for (int ay = rect["y1"]; ay <= rect["y2"]; ay += stepY)
                for (int ax = rect["x1"]; ax <= rect["x2"]; ax += stepX)
                    anchors.Add(new CellCoord(ax, ay));

            return anchors;
        }

        private static List<CellCoord> ParseAnchorArray(JObject args, out string error)
        {
            error = null;
            var result = new List<CellCoord>();
            var anchors = args["anchors"] as JArray;
            if (anchors == null)
                return result;

            var area = WorldEditor.ResolveRelativeArea(args);
            foreach (var item in anchors)
            {
                int? x = null;
                int? y = null;
                var obj = item as JObject;
                if (obj != null)
                {
                    x = ParseInt(obj["x"]);
                    y = ParseInt(obj["y"]);
                }
                else
                {
                    var pair = item as JArray;
                    if (pair != null && pair.Count >= 2)
                    {
                        x = ParseInt(pair[0]);
                        y = ParseInt(pair[1]);
                    }
                }

                if (!x.HasValue || !y.HasValue)
                {
                    error = "Each anchors item must be {x,y} or [x,y]";
                    return result;
                }

                if (area != null)
                {
                    var absolute = WorldEditor.ToAbsoluteCell(x.Value, y.Value, area);
                    result.Add(new CellCoord(absolute.x, absolute.y));
                }
                else
                {
                    result.Add(new CellCoord(x.Value, y.Value));
                }
            }

            return result;
        }

        private static int? ParseInt(JToken token)
        {
            int value;
            return token != null && int.TryParse(token.ToString(), out value) ? value : (int?)null;
        }

        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def)
        {
            return Grid.CellToPosCBC(cell, def.SceneLayer);
        }

        private static Dictionary<string, object> BuildDefPlacementToDictionary(BuildingDef def)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            return new Dictionary<string, object>
            {
                ["anchor"] = "lowerLeftCell",
                ["anchorDescription"] = "agent_pointer cell is treated as the lower-left footprint cell, not the visual center",
                ["width"] = width,
                ["height"] = height,
                ["footprintCells"] = width * height,
                ["singleCellDragSafe"] = width == 1 && height == 1,
                ["dragGuidance"] = width == 1 && height == 1
                    ? "May be placed with navigation_control action=hold_left for straight lines."
                    : "Recommended: use navigation_control action=left_click on each lower-left anchor. navigation_control action=left_click works but is less predictable for multi-cell footprints."
            };
        }

        private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId)
        {
            int cell = Grid.XYToCell(x, y);
            return new PlacementDetails
            {
                PrefabId = def.PrefabID,
                AnchorX = x,
                AnchorY = y,
                WorldId = worldId,
                Width = Math.Max(1, def.WidthInCells),
                Height = Math.Max(1, def.HeightInCells),
                PlacementPoint = BuildPlacementPosition(cell, def),
                Footprint = FootprintCells(def, x, y, worldId).ToList()
            };
        }

        private static IEnumerable<FootprintCell> FootprintCells(BuildingDef def, int x, int y, int worldId)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int fx = x + dx;
                    int fy = y + dy;
                    int cell = Grid.XYToCell(fx, fy);
                    yield return new FootprintCell
                    {
                        X = fx,
                        Y = fy,
                        Cell = cell,
                        WorldId = worldId,
                        Valid = Grid.IsValidCell(cell),
                        Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell),
                        InWorld = Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                    };
                }
            }
        }

        private static FootprintValidation ValidateFootprint(PlacementDetails placement)
        {
            var invalid = placement.Footprint
                .Where(cell => !cell.Valid || !cell.Visible || !cell.InWorld)
                .Select(cell => cell.ToDictionary())
                .ToList();

            var obstructions = FindFootprintObstructions(placement);

            if (invalid.Count == 0 && obstructions.Count == 0)
                return FootprintValidation.Success();

            string error = invalid.Count > 0
                ? "Invalid footprint: every occupied cell must be visible, valid, and inside the selected world"
                : "Obstructed footprint: occupied terrain, building, or blueprint overlaps the requested cells";
            return FootprintValidation.Invalid(error, invalid, obstructions);
        }

        private static Dictionary<string, object> ActualPlacementDetails(GameObject go, BuildingDef def, int expectedX, int expectedY)
        {
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int originX = x >= 0 ? x : expectedX;
            int originY = y >= 0 ? y : expectedY;

            var building = go.GetComponent<Building>();
            if (building != null)
            {
                int anchorCell = building.GetBottomLeftCell();
                if (Grid.IsValidCell(anchorCell))
                {
                    originX = Grid.CellColumn(anchorCell);
                    originY = Grid.CellRow(anchorCell);
                }
            }

            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;

            return new Dictionary<string, object>
            {
                ["objectCell"] = cell,
                ["objectX"] = x,
                ["objectY"] = y,
                ["derivedAnchorX"] = originX,
                ["derivedAnchorY"] = originY,
                ["worldId"] = worldId,
                ["note"] = "derivedAnchor is Building.GetBottomLeftCell when available; placement uses the requested anchor cell directly"
            };
        }

        private static Dictionary<string, object> ComparePlacement(PlacementDetails expected, Dictionary<string, object> actual)
        {
            int actualX = actual.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actual["derivedAnchorX"]) : -1;
            int actualY = actual.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actual["derivedAnchorY"]) : -1;
            int actualWorld = actual.ContainsKey("worldId") ? Convert.ToInt32(actual["worldId"]) : -1;
            bool anchorMatches = actualX == expected.AnchorX && actualY == expected.AnchorY;
            bool worldMatches = actualWorld < 0 || expected.WorldId < 0 || actualWorld == expected.WorldId;
            bool valid = anchorMatches && worldMatches;
            return new Dictionary<string, object>
            {
                ["valid"] = valid,
                ["anchorMatches"] = anchorMatches,
                ["worldMatches"] = worldMatches,
                ["expectedAnchor"] = new { x = expected.AnchorX, y = expected.AnchorY },
                ["actualDerivedAnchor"] = new { x = actualX, y = actualY },
                ["expectedWorldId"] = expected.WorldId,
                ["actualWorldId"] = actualWorld,
                ["next"] = valid
                    ? "Verify with world_area_snapshot/world_text_map before placing the next footprint batch."
                    : "Cancel the misplaced blueprint before retrying from the expected anchor."
            };
        }

        private static Dictionary<string, object> BuildPlacementFailureDetails(PlacementDetails placement, MaterialSelection materialResult)
        {
            return new Dictionary<string, object>
            {
                ["placement"] = placement.ToDictionary(),
                ["obstructions"] = FindFootprintObstructions(placement).Take(50).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["reasonHint"] = "TryPlace returned null after preflight; inspect obstructions/support/materialSelection for likely cause."
            };
        }

        private static List<Dictionary<string, object>> FindFootprintObstructions(PlacementDetails placement)
        {
            var result = new List<Dictionary<string, object>>();
            var footprintCells = new HashSet<int>(placement.Footprint.Where(cell => cell.Valid).Select(cell => cell.Cell));
            bool utility = IsUtilityPrefab(placement.PrefabId);

            foreach (var cellInfo in placement.Footprint)
            {
                if (!cellInfo.Valid)
                    continue;
                if (!utility && Grid.Solid[cellInfo.Cell])
                {
                    bool diggable = IsNaturalDiggableSolidCell(cellInfo.Cell, placement.WorldId);
                    bool alreadyMarked = Grid.Objects[cellInfo.Cell, (int)ObjectLayer.DigPlacer] != null;
                    result.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "solid_cell",
                        ["x"] = cellInfo.X,
                        ["y"] = cellInfo.Y,
                        ["cell"] = cellInfo.Cell,
                        ["diggable"] = diggable,
                        ["alreadyMarkedForDig"] = alreadyMarked,
                        ["reasonCode"] = "solid_cell",
                        ["reason"] = diggable
                            ? "target footprint contains natural solid terrain that can be marked for digging"
                            : "target footprint contains solid terrain or constructed tile that cannot be auto-dug"
                    });
                }

                foreach (var uproot in UprootableObstructionsAtCell(cellInfo.Cell, placement.WorldId))
                {
                    uproot["x"] = cellInfo.X;
                    uproot["y"] = cellInfo.Y;
                    uproot["cell"] = cellInfo.Cell;
                    result.Add(uproot);
                }
            }

            var seen = new HashSet<string>();
            foreach (var obstruction in ExistingBuildingFootprintObstructions(placement.WorldId, footprintCells))
            {
                string id = obstruction.ContainsKey("id") ? obstruction["id"]?.ToString() : "";
                if (utility || IsUtilityPrefab(id))
                    continue;
                string key = obstruction["kind"] + "|" + id + "|" + obstruction["objectX"] + "|" + obstruction["objectY"] + "|" + obstruction["x"] + "|" + obstruction["y"];
                if (seen.Add(key))
                    result.Add(obstruction);
            }

            return result;
        }

        private static Dictionary<string, object> TryAutoDigObstructions(PlacementDetails placement, FootprintValidation footprintResult, JObject args, AutoDigContext context)
        {
            bool enabled = ToolUtil.GetBool(args, "autoDigObstructions", true);
            if (!enabled || placement == null || footprintResult == null)
                return null;
            if (footprintResult.InvalidCells.Count > 0)
                return null;

            foreach (var obstruction in footprintResult.Obstructions)
            {
                string kind = obstruction.ContainsKey("kind") ? obstruction["kind"]?.ToString() : null;
                if (EqualsIgnoreCase(kind, "solid_cell"))
                {
                    object diggableValue;
                    bool diggable = obstruction.TryGetValue("diggable", out diggableValue)
                        && diggableValue != null
                        && bool.TryParse(diggableValue.ToString(), out bool parsed)
                        && parsed;
                    if (!diggable)
                        return null;
                    continue;
                }

                if (EqualsIgnoreCase(kind, "uprootable"))
                {
                    bool uprootEnabled = ToolUtil.GetBool(args, "autoUprootObstructions", true);
                    object canUprootValue;
                    bool canUproot = obstruction.TryGetValue("canUproot", out canUprootValue)
                        && canUprootValue != null
                        && bool.TryParse(canUprootValue.ToString(), out bool parsed)
                        && parsed;
                    if (!uprootEnabled || !canUproot)
                        return null;
                    continue;
                }

                    return null;
            }

            var digTargets = placement.Footprint
                .Where(cell => IsNaturalDiggableSolidCell(cell.Cell, placement.WorldId))
                .ToList();
            var uprootTargets = placement.Footprint
                .SelectMany(cell => UprootablesAtCell(cell.Cell, placement.WorldId).Select(uprootable => new { cell, uprootable }))
                .GroupBy(item => item.uprootable.gameObject.GetInstanceID())
                .Select(group => group.First())
                .ToList();
            if (digTargets.Count == 0 && uprootTargets.Count == 0)
                return null;

            bool dryRun = IsDryRun(args);
            context = context ?? AutoDigContext.FromArgs(args);
            var targetResults = new List<Dictionary<string, object>>();
            int wouldMark = 0;
            int marked = 0;
            int alreadyMarked = 0;
            int skipped = 0;
            int failed = 0;
            double kgTotal = 0;

            if (!dryRun && DigTool.Instance == null)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["available"] = false,
                    ["dryRun"] = false,
                    ["needsRetryAfterDig"] = true,
                    ["targetCount"] = digTargets.Count + uprootTargets.Count,
                    ["error"] = "DigTool is not initialized; open a loaded colony UI before issuing automatic dig orders"
                };
            }

            int uprootWouldMark = 0;
            int uprootMarked = 0;
            int alreadyUprootMarked = 0;

            foreach (var target in digTargets)
            {
                bool already = Grid.Objects[target.Cell, (int)ObjectLayer.DigPlacer] != null;
                kgTotal += ToolUtil.SafeFloat(Grid.Mass[target.Cell]);

                if (already)
                {
                    alreadyMarked++;
                    targetResults.Add(AutoDigTarget(target, "already_marked"));
                    continue;
                }

                if (dryRun)
                {
                    wouldMark++;
                    targetResults.Add(AutoDigTarget(target, "would_dig"));
                    continue;
                }

                if (!context.TryReserve(target.Cell))
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target, context.LimitReached ? "limit_skipped" : "duplicate_skipped"));
                    continue;
                }

                if (DigTool.PlaceDig(target.Cell, context.NextDistance()) != null)
                {
                    marked++;
                    context.Marked++;
                    targetResults.Add(AutoDigTarget(target, "marked"));
                }
                else
                {
                    failed++;
                    targetResults.Add(AutoDigTarget(target, "failed"));
                }
            }

            foreach (var target in uprootTargets)
            {
                var uprootable = target.uprootable;
                var go = uprootable.gameObject;
                bool already = uprootable.IsMarkedForUproot;
                if (already)
                {
                    alreadyUprootMarked++;
                    targetResults.Add(AutoDigTarget(target.cell, "already_uproot_marked"));
                    continue;
                }

                if (dryRun)
                {
                    uprootWouldMark++;
                    targetResults.Add(AutoDigTarget(target.cell, "would_uproot"));
                    continue;
                }

                if (!uprootable.CanUproot())
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target.cell, "uproot_unavailable"));
                    continue;
                }

                if (!context.TryReserve(target.cell.Cell))
                {
                    skipped++;
                    targetResults.Add(AutoDigTarget(target.cell, context.LimitReached ? "limit_skipped" : "duplicate_skipped"));
                    continue;
                }

                uprootable.MarkForUproot();
                SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
                uprootMarked++;
                context.Marked++;
                targetResults.Add(AutoDigTarget(target.cell, "uproot_marked"));
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["available"] = true,
                ["dryRun"] = dryRun,
                ["needsRetryAfterDig"] = false,
                ["action"] = dryRun ? "would_clear_obstructions_for_build" : "queued_clear_obstructions_for_build",
                ["targetCount"] = digTargets.Count + uprootTargets.Count,
                ["digTargets"] = digTargets.Count,
                ["uprootTargets"] = uprootTargets.Count,
                ["wouldMark"] = dryRun ? wouldMark : (object)null,
                ["uprootWouldMark"] = dryRun ? uprootWouldMark : (object)null,
                ["marked"] = marked,
                ["alreadyMarked"] = alreadyMarked,
                ["uprootMarked"] = uprootMarked,
                ["alreadyUprootMarked"] = alreadyUprootMarked,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["limitReached"] = context.LimitReached,
                ["maxCells"] = context.MaxCells,
                ["kgTotal"] = Math.Round(kgTotal, 3),
                ["targets"] = targetResults.Take(50).ToList(),
                ["truncatedTargets"] = Math.Max(0, targetResults.Count - 50),
                ["note"] = "Natural solid cells were marked for digging and uprootable plants were marked for removal; the build planner will still attempt to place the build blueprint on the same cells."
            };
        }

        private static IEnumerable<Dictionary<string, object>> UprootableObstructionsAtCell(int cell, int worldId)
        {
            foreach (var uprootable in UprootablesAtCell(cell, worldId))
            {
                var go = uprootable.gameObject;
                var kpid = go.GetComponent<KPrefabID>();
                yield return new Dictionary<string, object>
                {
                    ["kind"] = "uprootable",
                    ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                    ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                    ["name"] = ToolUtil.CleanName(go.GetProperName()),
                    ["canUproot"] = uprootable.CanUproot(),
                    ["alreadyMarkedForUproot"] = uprootable.IsMarkedForUproot,
                    ["reasonCode"] = "uprootable_plant",
                    ["reason"] = "target footprint contains a plant or uprootable object that can be marked for uproot"
                };
            }
        }

        private static IEnumerable<Uprootable> UprootablesAtCell(int cell, int worldId)
        {
            foreach (var uprootable in Components.Uprootables.Items)
            {
                var go = uprootable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                if (Grid.PosToCell(go) == cell)
                    yield return uprootable;
            }
        }

        private static bool IsNaturalDiggableSolidCell(int cell, int worldId)
        {
            return Grid.IsValidCell(cell)
                && Grid.IsVisible(cell)
                && ToolUtil.CellMatchesWorld(cell, worldId)
                && Grid.Solid[cell]
                && !Grid.Foundation[cell];
        }

        private static Dictionary<string, object> AutoDigTarget(FootprintCell cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["x"] = cell.X,
                ["y"] = cell.Y,
                ["cell"] = cell.Cell,
                ["worldId"] = cell.WorldId,
                ["status"] = status
            };
        }

        private static IEnumerable<Dictionary<string, object>> ExistingBuildingFootprintObstructions(int worldId, HashSet<int> footprintCells)
        {
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null || !ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                foreach (var item in ExistingObjectFootprint(building.gameObject, building.Def, "building", footprintCells))
                    yield return item;
            }

            foreach (var constructable in FindConstructables(worldId))
            {
                var go = constructable?.gameObject;
                if (go == null)
                    continue;
                var building = go.GetComponent<Building>();
                foreach (var item in ExistingObjectFootprint(go, building?.Def, "blueprint", footprintCells))
                    yield return item;
            }
        }

        private static GameObject TryPlaceWithBuildTool(BuildingDef def, int cell, Orientation orientation, IList<Tag> selectedElements, string facadeId, PlacementDetails placement, out Dictionary<string, object> details)
        {
            details = new Dictionary<string, object>
            {
                ["attempted"] = true,
                ["path"] = "BuildTool.TryBuild",
                ["reason"] = "direct BuildingDef.TryPlace returned null while natural solid footprint cells were auto-dig queued"
            };

            if (BuildTool.Instance == null)
            {
                details["available"] = false;
                details["error"] = "BuildTool.Instance is not initialized";
                return null;
            }

            try
            {
                BuildTool.Instance.Activate(def, selectedElements, facadeId);
                BuildTool.Instance.SetToolOrientation(orientation);
                var tryBuild = typeof(BuildTool).GetMethod("TryBuild", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (tryBuild == null)
                {
                    details["available"] = false;
                    details["error"] = "BuildTool.TryBuild method was not found";
                    return null;
                }

                tryBuild.Invoke(BuildTool.Instance, new object[] { cell });
                var placed = FindConstructableAtPlacement(def, placement);
                details["available"] = true;
                details["placed"] = placed != null;
                if (placed != null)
                {
                    details["id"] = placed.GetComponent<KPrefabID>()?.InstanceID ?? -1;
                    details["actualPlacement"] = ActualPlacementDetails(placed, def, placement.AnchorX, placement.AnchorY);
                }
                return placed;
            }
            catch (Exception ex)
            {
                details["available"] = false;
                details["error"] = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static GameObject FindConstructableAtPlacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null)
                return null;

            foreach (var constructable in FindConstructables(placement.WorldId))
            {
                var go = constructable?.gameObject;
                if (go == null)
                    continue;

                var building = go.GetComponent<Building>();
                string prefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                if (!EqualsIgnoreCase(prefabId, def.PrefabID))
                    continue;

                var actual = ActualPlacementDetails(go, def, placement.AnchorX, placement.AnchorY);
                var check = ComparePlacement(placement, actual);
                if (GetBool(check, "valid"))
                    return go;
            }

            return null;
        }

        private static Dictionary<string, object> ExistingMatchingBuildAtPlacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null)
                return null;

            foreach (var complete in Components.BuildingCompletes.Items)
            {
                var go = complete?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, placement.WorldId))
                    continue;

                var building = go.GetComponent<Building>();
                string existingPrefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                if (!EqualsIgnoreCase(existingPrefabId, def.PrefabID))
                    continue;

                var actual = ActualPlacementDetails(go, def, placement.AnchorX, placement.AnchorY);
                var check = ComparePlacement(placement, actual);
                if (!GetBool(check, "valid"))
                    continue;

                return new Dictionary<string, object>
                {
                    ["kind"] = "building",
                    ["prefabId"] = existingPrefabId,
                    ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                    ["actualPlacement"] = actual,
                    ["placementCheck"] = check
                };
            }

            var blueprint = FindConstructableAtPlacement(def, placement);
            if (blueprint == null)
                return null;

            var blueprintBuilding = blueprint.GetComponent<Building>();
            return new Dictionary<string, object>
            {
                ["kind"] = "blueprint",
                ["prefabId"] = blueprintBuilding?.Def?.PrefabID ?? blueprint.GetComponent<KPrefabID>()?.PrefabTag.Name ?? blueprint.name,
                ["id"] = blueprint.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["actualPlacement"] = ActualPlacementDetails(blueprint, def, placement.AnchorX, placement.AnchorY),
                ["placementCheck"] = ComparePlacement(placement, ActualPlacementDetails(blueprint, def, placement.AnchorX, placement.AnchorY))
            };
        }

        private static Dictionary<string, object> ExistingMatchingUtilityAtPlacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null || !IsLinearUtilityPrefab(def.PrefabID))
                return null;

            var layers = UtilityLayersForPrefab(def.PrefabID);
            if (layers.Count == 0)
                return null;

            var existing = new List<Dictionary<string, object>>();
            foreach (var cellInfo in placement.Footprint)
            {
                if (!cellInfo.Valid || !cellInfo.Visible || !cellInfo.InWorld)
                    return null;

                var found = ExistingUtilityAtCell(cellInfo, layers);
                if (found == null)
                    return null;
                existing.Add(found);
            }

            return new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["mode"] = "reuse_existing_same_utility_layer",
                ["cells"] = existing,
                ["note"] = "A matching wire/pipe already exists on this utility layer, so this cell is treated as connected instead of placing a duplicate blueprint."
            };
        }

        private static Dictionary<string, object> ExistingUtilityAtCell(FootprintCell cellInfo, List<ObjectLayer> layers)
        {
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cellInfo.Cell, (int)layer];
                if (go == null)
                    continue;

                var building = go.GetComponent<Building>();
                string prefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                return new Dictionary<string, object>
                {
                    ["x"] = cellInfo.X,
                    ["y"] = cellInfo.Y,
                    ["cell"] = cellInfo.Cell,
                    ["layer"] = layer.ToString(),
                    ["id"] = prefabId,
                    ["name"] = ToolUtil.CleanName(go.GetProperName())
                };
            }

            return null;
        }

        private static IEnumerable<Dictionary<string, object>> ExistingObjectFootprint(GameObject go, BuildingDef def, string kind, HashSet<int> footprintCells)
        {
            if (go == null)
                yield break;
            int objectCell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(objectCell))
                yield break;

            int objectX = Grid.CellColumn(objectCell);
            int objectY = Grid.CellRow(objectCell);
            int width = Math.Max(1, def?.WidthInCells ?? 1);
            int height = Math.Max(1, def?.HeightInCells ?? 1);
            var building = go.GetComponent<Building>();
            int anchorCell = building != null ? building.GetBottomLeftCell() : objectCell;
            int anchorX = Grid.IsValidCell(anchorCell) ? Grid.CellColumn(anchorCell) : objectX - width / 2;
            int anchorY = Grid.IsValidCell(anchorCell) ? Grid.CellRow(anchorCell) : objectY - height / 2;
            var kpid = go.GetComponent<KPrefabID>();
            string id = def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;

            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int x = anchorX + dx;
                    int y = anchorY + dy;
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !footprintCells.Contains(cell))
                        continue;
                    yield return new Dictionary<string, object>
                    {
                        ["kind"] = kind,
                        ["id"] = id,
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["x"] = x,
                        ["y"] = y,
                        ["cell"] = cell,
                        ["objectX"] = objectX,
                        ["objectY"] = objectY,
                        ["anchorX"] = anchorX,
                        ["anchorY"] = anchorY,
                        ["reasonCode"] = "occupied_by_" + kind,
                        ["reason"] = "requested footprint overlaps an existing " + kind + " footprint"
                    };
                }
            }
        }

        private static IEnumerable<Constructable> FindConstructables(int worldId)
        {
            Constructable[] constructables;
            try
            {
                constructables = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            }
            catch
            {
                yield break;
            }

            foreach (var constructable in constructables)
            {
                if (constructable == null || constructable.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(constructable.gameObject, worldId))
                    continue;
                yield return constructable;
            }
        }

        private static BuildDragPolicyResult BuildDragPolicy(BuildingDef def, JObject args)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            bool singleCell = width == 1 && height == 1;
            bool allowFootprintDrag = ToolUtil.GetBool(args, "allowFootprintDrag", false);
            if (singleCell || allowFootprintDrag)
                return BuildDragPolicyResult.Allow(def.PrefabID, width, height, singleCell, allowFootprintDrag);
            return BuildDragPolicyResult.Reject(def.PrefabID, width, height);
        }

        private static void RegisterSupportBlueprint(string prefabId, int x, int y, HashSet<int> plannedSupportCells)
        {
            if (plannedSupportCells == null || !IsSupportPrefab(prefabId))
                return;

            int cell = Grid.XYToCell(x, y);
            if (Grid.IsValidCell(cell))
                plannedSupportCells.Add(cell);
        }

        private static bool IsSupportPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;

            return EqualsIgnoreCase(prefabId, "Tile")
                || EqualsIgnoreCase(prefabId, "MeshTile")
                || EqualsIgnoreCase(prefabId, "GasPermeableMembrane")
                || EqualsIgnoreCase(prefabId, "AirflowTile")
                || EqualsIgnoreCase(prefabId, "BunkerTile")
                || EqualsIgnoreCase(prefabId, "GlassTile")
                || EqualsIgnoreCase(prefabId, "InsulationTile")
                || EqualsIgnoreCase(prefabId, "PlasticTile")
                || EqualsIgnoreCase(prefabId, "MetalTile")
                || EqualsIgnoreCase(prefabId, "CarpetTile");
        }

        private static bool IsUtilityPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;

            string id = prefabId.Trim();
            return id.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("TravelTube", StringComparison.OrdinalIgnoreCase) >= 0
                || EqualsIgnoreCase(id, "GasConduit")
                || EqualsIgnoreCase(id, "LiquidConduit")
                || EqualsIgnoreCase(id, "SolidConduit")
                || EqualsIgnoreCase(id, "SolidConduitBridge")
                || EqualsIgnoreCase(id, "WireBridge")
                || EqualsIgnoreCase(id, "LogicWire")
                || EqualsIgnoreCase(id, "LogicWireBridge");
        }

        private static bool IsLinearUtilityPrefab(string prefabId)
        {
            if (!IsUtilityPrefab(prefabId))
                return false;
            string id = prefabId.Trim();
            return id.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) < 0
                && id.IndexOf("TravelTube", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static List<ObjectLayer> UtilityLayersForPrefab(string prefabId)
        {
            var layers = new List<ObjectLayer>();
            if (string.IsNullOrWhiteSpace(prefabId))
                return layers;

            string id = prefabId.Trim();
            if (id.IndexOf("GasConduit", StringComparison.OrdinalIgnoreCase) >= 0)
                layers.AddRange(new[] { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit });
            else if (id.IndexOf("LiquidConduit", StringComparison.OrdinalIgnoreCase) >= 0)
                layers.AddRange(new[] { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit });
            else if (id.IndexOf("SolidConduit", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Conveyor", StringComparison.OrdinalIgnoreCase) >= 0)
                layers.AddRange(new[] { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit });
            else if (id.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0)
                layers.AddRange(new[] { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire });
            else if (id.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                layers.AddRange(new[] { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire });

            return layers;
        }

        private static SupportValidation ValidateSupport(BuildingDef def, int x, int y, bool allowUnsupported, HashSet<int> plannedSupportCells)
        {
            if (def == null)
                return SupportValidation.Success("unknown", null);

            string rule = def.BuildLocationRule.ToString();
            if (!EqualsIgnoreCase(rule, "OnFloor"))
                return SupportValidation.Success(rule, null);

            var missing = new List<Dictionary<string, object>>();
            foreach (var supportCell in FloorSupportCells(def, x, y))
            {
                bool supported = Grid.IsValidCell(supportCell.Cell)
                    && (Grid.Solid[supportCell.Cell]
                        || HasSupportBlueprint(supportCell.Cell)
                        || (plannedSupportCells != null && plannedSupportCells.Contains(supportCell.Cell)));
                if (!supported)
                    missing.Add(new Dictionary<string, object>
                    {
                        ["x"] = supportCell.X,
                        ["y"] = supportCell.Y,
                        ["cell"] = supportCell.Cell,
                        ["reasonCode"] = "missing_support",
                        ["reason"] = "OnFloor building requires solid terrain, a constructed support tile, or a support blueprint below this cell."
                    });
            }

            if (missing.Count == 0)
                return SupportValidation.Success(rule, null);

            string error = $"Unsupported OnFloor building: place floor/support tiles below {def.PrefabID} first, or set allowUnsupported=true";
            return allowUnsupported
                ? SupportValidation.Warning(rule, missing, error)
                : SupportValidation.Invalid(rule, missing, error);
        }

        private static IEnumerable<SupportCell> FloorSupportCells(BuildingDef def, int x, int y)
        {
            int width = Math.Max(1, def.WidthInCells);
            int supportY = y - 1;
            for (int dx = 0; dx < width; dx++)
            {
                int sx = x + dx;
                yield return new SupportCell(sx, supportY, Grid.XYToCell(sx, supportY));
            }
        }

        private static bool HasSupportBlueprint(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return false;

            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
            {
                var go = Grid.Objects[cell, layer];
                if (go == null)
                    continue;

                var building = go.GetComponent<Building>();
                if (building != null && building.Def != null && IsSupportPrefab(building.Def.PrefabID))
                    return true;

                var prefabId = go.GetComponent<KPrefabID>()?.PrefabTag.Name;
                if (IsSupportPrefab(prefabId))
                    return true;
            }

            return false;
        }

        private struct CellCoord
        {
            public readonly int x;
            public readonly int y;

            public CellCoord(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        private struct SupportCell
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Cell;

            public SupportCell(int x, int y, int cell)
            {
                X = x;
                Y = y;
                Cell = cell;
            }
        }

        private static MaterialSelection SelectElements(BuildingDef def, string material, int worldId)
        {
            string requested = material?.Trim();
            bool auto = string.IsNullOrWhiteSpace(requested)
                || requested.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || requested.Equals("default", StringComparison.OrdinalIgnoreCase);

            var available = AvailableMaterials(def, worldId, includeUnavailable: false).ToList();
            if (auto)
            {
                var selected = available.FirstOrDefault();
                if (selected != null)
                    return MaterialSelection.Success(new List<Tag> { selected.Tag }, "auto", requested, selected, available);

                var defaults = DefaultBuildElements(def);
                if (defaults.Count > 0 && IsFreeBuildContext())
                    return MaterialSelection.Success(defaults, "default_no_inventory_in_debug", requested, null, available);

                return MaterialSelection.Invalid(
                    "No available build material in current world inventory",
                    requested,
                    available,
                    AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList());
            }

            var match = AvailableMaterials(def, worldId, includeUnavailable: true)
                .FirstOrDefault(item => EqualsIgnoreCase(item.Tag.Name, requested)
                    || EqualsIgnoreCase(item.Name, requested)
                    || Contains(item.Tag.Name, requested)
                    || Contains(item.Name, requested));
            var candidates = AvailableMaterials(def, worldId, includeUnavailable: true).Take(20).ToList();
            if (match == null || !match.ValidForBuilding)
            {
                return MaterialSelection.Invalid(
                    $"Material '{requested}' is not valid for {def.PrefabID}",
                    requested,
                    available,
                    candidates);
            }

            if (match.AvailableKg <= 0f && !IsFreeBuildContext())
            {
                return MaterialSelection.Invalid(
                    $"Material '{match.Tag.Name}' is valid for {def.PrefabID}, but none is currently available",
                    requested,
                    available,
                    candidates);
            }

            return MaterialSelection.Success(new List<Tag> { match.Tag }, "explicit", requested, match, available);
        }

        private static List<BuildMaterialInfo> AvailableMaterials(BuildingDef def, int worldId, bool includeUnavailable)
        {
            var categories = MaterialCategoryTags(def).ToList();
            var candidates = CandidateMaterialTags(def, worldId)
                .Where(tag => tag.IsValid)
                .Distinct()
                .Select(tag =>
                {
                    var matches = categories.Where(category => MaterialMatchesCategory(tag, category)).ToList();
                    return new BuildMaterialInfo
                    {
                        Tag = tag,
                        Name = tag.ProperNameStripLink(),
                        AvailableKg = AvailableAmount(worldId, tag),
                        ValidForBuilding = categories.Count == 0 || matches.Count > 0,
                        Categories = matches
                    };
                })
                .Where(item => item.ValidForBuilding)
                .Where(item => includeUnavailable || item.AvailableKg > 0f || IsFreeBuildContext())
                .OrderByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag.Name)
                .ToList();

            return candidates;
        }

        private static float RequiredMaterialKg(BuildingDef def)
        {
            if (def == null)
                return 0f;

            try
            {
                var buildingsType = typeof(BuildingDef).Assembly.GetType("TUNING+BUILDINGS") ?? Type.GetType("TUNING+BUILDINGS");
                var massField = buildingsType?.GetField("CONSTRUCTION_MASS_KG", BindingFlags.Public | BindingFlags.Static);
                var masses = massField?.GetValue(null) as float[];
                int tier = Mathf.RoundToInt(ToolUtil.SafeFloat(def.MassTier));
                if (masses != null && tier >= 0 && tier < masses.Length)
                    return Math.Max(0f, masses[tier]);
            }
            catch
            {
                // Keep material diagnostics best-effort across ONI builds.
            }

            return 0f;
        }

        private static IEnumerable<Tag> CandidateMaterialTags(BuildingDef def, int worldId)
        {
            foreach (var tag in DefaultBuildElements(def))
                yield return tag;

            foreach (var category in MaterialCategoryTags(def))
            {
                if (DiscoveredResources.Instance != null)
                {
                    IEnumerable<Tag> discovered = null;
                    try
                    {
                        discovered = DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category);
                    }
                    catch
                    {
                        discovered = null;
                    }

                    if (discovered != null)
                    {
                        foreach (var tag in discovered)
                            yield return tag;
                    }
                }
            }

            foreach (var tag in InventoryMaterialTags(def, worldId))
                yield return tag;
        }

        private static IEnumerable<Tag> InventoryMaterialTags(BuildingDef def, int worldId)
        {
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                yield break;

            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;

                int itemWorldId = PickupableWorldId(pickupable);
                if (worldId >= 0 && itemWorldId != worldId)
                    continue;

                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                Tag prefabTag = kpid?.PrefabTag ?? Tag.Invalid;
                Tag elementTag = primary != null ? new Tag(primary.ElementID.ToString()) : Tag.Invalid;

                if (MaterialMatchesAnyCategory(prefabTag, categories))
                    yield return prefabTag;
                if (MaterialMatchesAnyCategory(elementTag, categories))
                    yield return elementTag;
                if (kpid != null && categories.Any(category => kpid.HasTag(category)))
                {
                    if (elementTag.IsValid)
                        yield return elementTag;
                    if (prefabTag.IsValid)
                        yield return prefabTag;
                }
            }
        }

        private static IEnumerable<Tag> MatchingMaterialCategories(BuildingDef def, Tag material)
        {
            var categories = MaterialCategoryTags(def).ToList();
            foreach (var category in categories)
            {
                if (MaterialMatchesCategory(material, category))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> MaterialCategoryTags(BuildingDef def)
        {
            if (def.MaterialCategory == null)
                yield break;
            foreach (string categoryName in def.MaterialCategory)
            {
                foreach (var category in ParseMaterialCategoryExpression(categoryName))
                    yield return category;
            }
        }

        private static IEnumerable<Tag> ParseMaterialCategoryExpression(string categoryExpression)
        {
            if (string.IsNullOrWhiteSpace(categoryExpression))
                yield break;

            char[] separators = { '&', '|', ',', ';' };
            foreach (var part in categoryExpression.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var category = new Tag(part.Trim());
                if (category.IsValid)
                    yield return category;
            }
        }

        private static List<Tag> DefaultBuildElements(BuildingDef def)
        {
            var defaults = def.DefaultElements() ?? new List<Tag>();
            var categories = MaterialCategoryTags(def).ToList();
            if (categories.Count == 0)
                return defaults.Where(tag => tag.IsValid).Distinct().ToList();

            return defaults
                .Where(tag => MaterialMatchesAnyCategory(tag, categories))
                .Distinct()
                .ToList();
        }

        private static bool MaterialMatchesAnyCategory(Tag material, List<Tag> categories)
        {
            return material.IsValid && categories.Any(category => MaterialMatchesCategory(material, category));
        }

        private static bool MaterialMatchesCategory(Tag material, Tag category)
        {
            if (!material.IsValid || !category.IsValid)
                return false;
            if (material == category)
                return true;

            var element = ElementLoader.GetElement(material);
            if (element != null && (element.GetMaterialCategoryTag() == category || element.HasTag(category)))
                return true;

            var prefab = Assets.GetPrefab(material);
            var kpid = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            return kpid != null && kpid.HasTag(category);
        }

        private static int PickupableWorldId(Pickupable pickupable)
        {
            int cell = pickupable.cachedCell;
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];
            return pickupable.GetMyWorldId();
        }

        private static float AvailableAmount(int worldId, Tag tag)
        {
            if (!tag.IsValid || ClusterManager.Instance == null)
                return 0f;
            var world = ClusterManager.Instance.GetWorld(worldId >= 0 ? worldId : ClusterManager.Instance.activeWorldId);
            if (world == null || world.worldInventory == null)
                return 0f;
            return ToolUtil.SafeFloat(world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
        }

        private static bool IsFreeBuildContext()
        {
            return DebugHandler.InstantBuildMode || (Game.Instance != null && Game.Instance.SandboxModeActive);
        }

        private static FacadeSelection ResolveFacade(BuildingDef def, string facade)
        {
            if (string.IsNullOrWhiteSpace(facade))
                return FacadeSelection.Default();

            var facadeId = facade.Trim();
            if (facadeId.Equals("default", StringComparison.OrdinalIgnoreCase) || facadeId == "DEFAULT_FACADE")
                return FacadeSelection.Default();

            if (def.AvailableFacades == null || !def.AvailableFacades.Contains(facadeId))
                return FacadeSelection.Invalid($"Facade '{facadeId}' is not available for {def.PrefabID}");

            var permit = Db.Get().Permits.TryGet(facadeId);
            if (permit == null)
                return FacadeSelection.Invalid($"Facade '{facadeId}' has no permit resource");

            if (!permit.IsUnlocked())
                return FacadeSelection.Invalid($"Facade '{facadeId}' is locked");

            return FacadeSelection.Custom(facadeId);
        }

        private static void SetPriority(GameObject go, int priority)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            int clamped = Math.Max(1, Math.Min(priority, 9));
            prioritizable.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, clamped));
        }

        private static Orientation ParseOrientation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Orientation.Neutral;

            Orientation orientation;
            return Enum.TryParse(value, true, out orientation) ? orientation : Orientation.Neutral;
        }

        private static bool Matches(BuildingDef def, string query)
        {
            string q = query.Trim();
            return Contains(def.PrefabID, q)
                || Contains(def.Name, q)
                || Contains(def.Desc, q)
                || BuildingCategories(def).Any(category => Contains(category, q))
                || def.SearchTerms.Any(term => Contains(term, q));
        }

        private static bool MatchesCategory(BuildingDef def, string category)
        {
            string q = category.Trim();
            return BuildingCategories(def).Any(value => Contains(value, q));
        }

        private static Dictionary<string, object> BuildingDefToDictionary(BuildingDef def)
        {
            int worldId = ClusterManager.Instance?.activeWorldId ?? -1;
            var availableMaterials = AvailableMaterials(def, worldId, includeUnavailable: false).ToList();
            var defaultElements = DefaultBuildElements(def);
            var availableTags = new HashSet<Tag>(availableMaterials.Select(m => m.Tag));
            var filteredDefaults = defaultElements
                .Where(tag => availableTags.Contains(tag))
                .ToList();
            if (filteredDefaults.Count == 0 && defaultElements.Count > 0)
                filteredDefaults = defaultElements;

            string recommendedMaterial = availableMaterials.Count > 0
                ? availableMaterials[0].Tag.Name
                : null;

            return new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["width"] = def.WidthInCells,
                ["height"] = def.HeightInCells,
                ["buildLocationRule"] = def.BuildLocationRule.ToString(),
                ["placement"] = BuildDefPlacementToDictionary(def),
                ["categories"] = BuildingCategories(def),
                ["materialCategories"] = def.MaterialCategory,
                ["resolvedMaterialCategories"] = MaterialCategoryTags(def).Select(tag => tag.Name).ToList(),
                ["defaultMaterials"] = filteredDefaults.Select(tag => tag.Name).ToList(),
                ["availableMaterials"] = availableMaterials.Take(20).Select(item => item.ToDictionary()).ToList(),
                ["recommendedMaterial"] = recommendedMaterial,
                ["autoMaterial"] = AutoMaterialValue(def, worldId),
                ["autoMaterialReason"] = AutoMaterialReason(def, worldId),
                ["facades"] = BuildingFacades(def),
                ["requiresPower"] = def.RequiresPowerInput,
                ["powerWatts"] = Math.Round(def.EnergyConsumptionWhenActive, 1),
                ["unlocked"] = IsTechUnlocked(def),
                ["availableNow"] = IsUnlockedAndAvailable(def)
            };
        }

private static bool IsUnlockedAndAvailable(BuildingDef def)
{
if (def == null)
return false;
try
{
return def.IsAvailable() && IsTechUnlocked(def);
}
catch
{
return false;
}
}

private static bool IsTechUnlocked(BuildingDef def)
{
if (def == null || Db.Get() == null || Db.Get().Techs == null)
return false;
try
{
return Db.Get().Techs.IsTechItemComplete(def.PrefabID);
}
catch
{
return false;
}
}

        private static object AutoMaterialValue(BuildingDef def, int worldId)
        {
            var material = AvailableMaterials(def, worldId, includeUnavailable: false).FirstOrDefault();
            return material != null ? (object)material.Tag.Name : "unavailable";
        }

        private static object AutoMaterialReason(BuildingDef def, int worldId)
        {
            if (!IsTechUnlocked(def))
                return "building_locked_by_research";
            if (!def.IsAvailable())
                return "building_not_available_in_current_context";
            var material = AvailableMaterials(def, worldId, includeUnavailable: false).FirstOrDefault();
            return material != null ? null : "no_currently_available_material";
        }

        private static List<string> BuildingCategories(BuildingDef def)
        {
            var categories = new List<string>();
            AddCategory(categories, ReadMemberString(def, "Category"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuCategory"));
            AddCategory(categories, ReadMemberString(def, "MenuCategory"));
            AddCategory(categories, ReadMemberString(def, "PlanScreenCategory"));
            AddCategory(categories, ReadMemberString(def, "Subcategory"));
            AddCategory(categories, ReadMemberString(def, "BuildMenuSubcategory"));
            AddCategory(categories, ReadMemberString(def, "TechCategory"));

            if (def.MaterialCategory != null)
                foreach (var category in def.MaterialCategory)
                    AddCategory(categories, category);

            return categories.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        }

        private static string ReadMemberString(BuildingDef def, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = def.GetType();
            var property = type.GetProperty(name, flags);
            if (property != null)
                return MemberValueToString(property.GetValue(def, null));

            var field = type.GetField(name, flags);
            return field == null ? null : MemberValueToString(field.GetValue(def));
        }

        private static string MemberValueToString(object value)
        {
            if (value == null)
                return null;
            var tag = value as Tag?;
            if (tag.HasValue)
                return tag.Value.Name;
            return value.ToString();
        }

        private static void AddCategory(List<string> categories, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                categories.Add(value.Trim());
        }

        private static List<Dictionary<string, object>> BuildingFacades(BuildingDef def)
        {
            var facades = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["id"] = "DEFAULT_FACADE",
                    ["name"] = "Default",
                    ["unlocked"] = true,
                    ["default"] = true
                }
            };

            if (def.AvailableFacades == null)
                return facades;

            foreach (var facadeId in def.AvailableFacades)
            {
                var permit = Db.Get().Permits.TryGet(facadeId);
                bool unlocked = permit != null && permit.IsUnlocked();
                if (!unlocked)
                    continue;

                var facade = Db.GetBuildingFacades().TryGet(facadeId);
                facades.Add(new Dictionary<string, object>
                {
                    ["id"] = facadeId,
                    ["name"] = facade != null ? ToolUtil.CleanName(facade.Name) : facadeId,
                    ["unlocked"] = true,
                    ["default"] = false
                });
            }

            return facades;
        }

        private static Dictionary<string, object> ErrorResult(string prefabId, int x, int y, string error, Dictionary<string, object> details = null)
        {
            string reasonCode = ClassifyBuildFailure(error, details);
            var result = new Dictionary<string, object>
            {
                ["planned"] = false,
                ["blueprintPlaced"] = false,
                ["actualAnchor"] = null,
                ["valid"] = false,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["anchor"] = AnchorDictionary(x, y, ExtractWorldId(details)),
                ["error"] = error,
                ["failureReason"] = reasonCode,
                ["reasonCode"] = reasonCode,
                ["coordinateContract"] = "x/y and anchor are the requested lower-left footprint cell; footprint/obstruction/support cells are absolute world cells when present."
            };
            if (details != null)
            {
                result["details"] = details;
                result["diagnostics"] = BuildFailureDiagnostics(reasonCode, error, details);
                CopyFailureField(details, result, "placement");
                CopyFailureField(details, result, "support");
                CopyFailureField(details, result, "materialSelection");
                CopyFailureField(details, result, "invalidCells");
                CopyFailureField(details, result, "obstructions");
                CopyFailureField(details, result, "missingSupportCells");
                CopyFailureField(details, result, "autoDig");
            }
            return result;
        }

        private static int ExtractWorldId(Dictionary<string, object> details)
        {
            var placement = GetObject(details, "placement");
            if (placement != null && placement.ContainsKey("worldId"))
                return Convert.ToInt32(placement["worldId"]);
            object value;
            if (details != null && details.TryGetValue("worldId", out value) && value != null)
                return Convert.ToInt32(value);
            return -1;
        }

        private static Dictionary<string, object> BuildFailureDiagnostics(string reasonCode, string error, Dictionary<string, object> details)
        {
            var diagnostics = new Dictionary<string, object>
            {
                ["reasonCode"] = reasonCode,
                ["message"] = error
            };
            CopyFailureField(details, diagnostics, "placement");
            CopyFailureField(details, diagnostics, "support");
            CopyFailureField(details, diagnostics, "materialSelection");
            CopyFailureField(details, diagnostics, "invalidCells");
            CopyFailureField(details, diagnostics, "obstructions");
            CopyFailureField(details, diagnostics, "missingSupportCells");
            CopyFailureField(details, diagnostics, "autoDig");
            CopyFailureField(details, diagnostics, "reasonHint");
            return diagnostics;
        }

        private static void CopyFailureField(Dictionary<string, object> source, Dictionary<string, object> target, string key)
        {
            object value;
            if (source != null && target != null && source.TryGetValue(key, out value))
                target[key] = value;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object ActualAnchorArray(Dictionary<string, object> actualPlacement)
        {
            if (actualPlacement == null)
                return null;
            int x = actualPlacement.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actualPlacement["derivedAnchorX"]) : -1;
            int y = actualPlacement.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actualPlacement["derivedAnchorY"]) : -1;
            return new[] { x, y };
        }

        private static Dictionary<string, object> AnchorDictionary(int x, int y, int worldId)
        {
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["coordRole"] = "lowerLeftCell",
                ["note"] = "Anchor is the lower-left footprint cell used by build_preview/build_area/agent pointer placement."
            };
        }

        private static string ClassifyBuildFailure(string error, Dictionary<string, object> details)
        {
            string text = (error ?? "") + " " + (details != null ? JsonConvert.SerializeObject(details, Formatting.None) : "");
            if (text.IndexOf("Unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unsupported";
            if (text.IndexOf("Obstructed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("obstructions", StringComparison.OrdinalIgnoreCase) >= 0)
                return "obstructed";
            if (text.IndexOf("Invalid footprint", StringComparison.OrdinalIgnoreCase) >= 0)
                return "invalidFloor";
            if (text.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unavailableMaterial";
            if (text.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
                return "locked";
            return "failed";
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class PlacementDetails
        {
            public string PrefabId;
            public int AnchorX;
            public int AnchorY;
            public int WorldId;
            public int Width;
            public int Height;
            public Vector3 PlacementPoint;
            public List<FootprintCell> Footprint = new List<FootprintCell>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["prefabId"] = PrefabId,
                    ["anchor"] = "lowerLeftCell",
                    ["anchorX"] = AnchorX,
                    ["anchorY"] = AnchorY,
                    ["worldId"] = WorldId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["footprintCells"] = Width * Height,
                    ["placementPoint"] = new
                    {
                        x = Math.Round(PlacementPoint.x, 3),
                        y = Math.Round(PlacementPoint.y, 3),
                        z = Math.Round(PlacementPoint.z, 3)
                    },
                    ["guidance"] = Width == 1 && Height == 1
                        ? "This is a single-cell footprint and can be line-dragged."
                        : "This is a multi-cell footprint; place each anchor with a separate left click and verify before continuing."
                };
            }
        }

        private sealed class FootprintCell
        {
            public int X;
            public int Y;
            public int Cell;
            public int WorldId;
            public bool Valid;
            public bool Visible;
            public bool InWorld;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["x"] = X,
                    ["y"] = Y,
                    ["cell"] = Cell,
                    ["worldId"] = WorldId,
                    ["valid"] = Valid,
                    ["visible"] = Visible,
                    ["inWorld"] = InWorld,
                    ["reasonCode"] = Valid && Visible && InWorld ? null : (!Valid ? "invalid_cell" : (!Visible ? "unrevealed" : "wrong_world"))
                };
            }
        }

        private sealed class FootprintValidation
        {
            public bool Valid;
            public string Error;
            public List<Dictionary<string, object>> InvalidCells = new List<Dictionary<string, object>>();
            public List<Dictionary<string, object>> Obstructions = new List<Dictionary<string, object>>();

            public static FootprintValidation Success()
            {
                return new FootprintValidation { Valid = true };
            }

            public static FootprintValidation Invalid(string error, List<Dictionary<string, object>> invalidCells, List<Dictionary<string, object>> obstructions = null)
            {
                return new FootprintValidation
                {
                    Valid = false,
                    Error = error,
                    InvalidCells = invalidCells ?? new List<Dictionary<string, object>>(),
                    Obstructions = obstructions ?? new List<Dictionary<string, object>>()
                };
            }

            public Dictionary<string, object> ToDictionary(PlacementDetails placement)
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["error"] = Error,
                    ["placement"] = placement.ToDictionary(),
                    ["invalidCells"] = InvalidCells,
                    ["obstructions"] = Obstructions
                };
            }
        }

        private sealed class BuildDragPolicyResult
        {
            public bool Allowed;
            public string PrefabId;
            public int Width;
            public int Height;
            public bool SingleCell;
            public bool AllowFootprintDrag;
            public string Reason;

            public static BuildDragPolicyResult Allow(string prefabId, int width, int height, bool singleCell, bool allowFootprintDrag)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = true,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = singleCell,
                    AllowFootprintDrag = allowFootprintDrag,
                    Reason = singleCell ? "single-cell footprint" : "allowFootprintDrag=true"
                };
            }

            public static BuildDragPolicyResult Reject(string prefabId, int width, int height)
            {
                return new BuildDragPolicyResult
                {
                    Allowed = false,
                    PrefabId = prefabId,
                    Width = width,
                    Height = height,
                    SingleCell = false,
                    AllowFootprintDrag = false,
                    Reason = "Multi-cell buildings must be placed one anchor click at a time to avoid shifted furniture or machines."
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["allowed"] = Allowed,
                    ["prefabId"] = PrefabId,
                    ["width"] = Width,
                    ["height"] = Height,
                    ["singleCell"] = SingleCell,
                    ["allowFootprintDrag"] = AllowFootprintDrag,
                    ["reason"] = Reason,
                    ["next"] = Allowed ? null : "Use navigation_control action=left_click for each lower-left anchor cell, or retry with allowFootprintDrag=true if this repeated footprint is intentional."
                };
            }
        }

        private sealed class AutoDigContext
        {
            private readonly HashSet<int> reservedCells = new HashSet<int>();
            private int distance;

            public int MaxCells;
            public int Marked;
            public bool LimitReached;

            public static AutoDigContext FromArgs(JObject args)
            {
                return new AutoDigContext
                {
                    MaxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxAutoDigCells") ?? 100, 500))
                };
            }

            public bool TryReserve(int cell)
            {
                if (!reservedCells.Add(cell))
                    return false;
                if (Marked >= MaxCells)
                {
                    LimitReached = true;
                    return false;
                }
                return true;
            }

            public int NextDistance()
            {
                return distance++;
            }
        }

        private struct FacadeSelection
        {
            public readonly bool Valid;
            public readonly string TryPlaceId;
            public readonly string ResponseId;
            public readonly string Error;

            private FacadeSelection(bool valid, string tryPlaceId, string responseId, string error)
            {
                Valid = valid;
                TryPlaceId = tryPlaceId;
                ResponseId = responseId;
                Error = error;
            }

            public static FacadeSelection Default()
            {
                return new FacadeSelection(true, null, "DEFAULT_FACADE", null);
            }

            public static FacadeSelection Custom(string facadeId)
            {
                return new FacadeSelection(true, facadeId, facadeId, null);
            }

            public static FacadeSelection Invalid(string error)
            {
                return new FacadeSelection(false, null, null, error);
            }
        }

        private sealed class BuildMaterialInfo
        {
            public Tag Tag;
            public string Name;
            public float AvailableKg;
            public bool ValidForBuilding;
            public List<Tag> Categories = new List<Tag>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["availableKg"] = Math.Round(ToolUtil.SafeFloat(AvailableKg), 3),
                    ["validForBuilding"] = ValidForBuilding,
                    ["categories"] = Categories.Select(tag => tag.Name).OrderBy(name => name).ToList()
                };
            }
        }

        private sealed class MaterialSelection
        {
            public bool Valid;
            public string Mode;
            public string Requested;
            public List<Tag> Elements = new List<Tag>();
        public BuildMaterialInfo Selected;
        public List<BuildMaterialInfo> Available = new List<BuildMaterialInfo>();
        public List<BuildMaterialInfo> Candidates = new List<BuildMaterialInfo>();
        public float RequiredKg;
        public string Error;

        public static MaterialSelection Success(List<Tag> elements, string mode, string requested, BuildMaterialInfo selected, List<BuildMaterialInfo> available)
        {
            return new MaterialSelection
                {
                    Valid = true,
                    Mode = mode,
                Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                Elements = elements ?? new List<Tag>(),
                Selected = selected,
                Available = available ?? new List<BuildMaterialInfo>(),
                RequiredKg = 0f
            };
        }

            public static MaterialSelection Invalid(string error, string requested, List<BuildMaterialInfo> available, List<BuildMaterialInfo> candidates)
            {
                return new MaterialSelection
                {
                    Valid = false,
                    Mode = "invalid",
                Requested = string.IsNullOrWhiteSpace(requested) ? "auto" : requested,
                Error = error,
                Available = available ?? new List<BuildMaterialInfo>(),
                Candidates = candidates ?? new List<BuildMaterialInfo>(),
                RequiredKg = 0f
            };
        }

        public Dictionary<string, object> ToDictionary()
        {
            float selectedAvailableKg = Selected != null ? ToolUtil.SafeFloat(Selected.AvailableKg) : 0f;
            bool hasRequiredKg = RequiredKg > 0f;
            object satisfied = hasRequiredKg ? (object)(IsFreeBuildContext() || selectedAvailableKg >= RequiredKg) : null;
            float shortageKg = hasRequiredKg ? Math.Max(0f, RequiredKg - selectedAvailableKg) : 0f;

            return new Dictionary<string, object>
            {
                ["valid"] = Valid,
                ["mode"] = Mode,
                ["requested"] = Requested,
                ["requirementKnown"] = hasRequiredKg,
                ["requiredKg"] = hasRequiredKg ? (object)Math.Round(ToolUtil.SafeFloat(RequiredKg), 3) : null,
                ["selectedAvailableKg"] = Selected != null ? (object)Math.Round(selectedAvailableKg, 3) : null,
                ["satisfied"] = satisfied,
                ["shortageKg"] = hasRequiredKg ? (object)Math.Round(shortageKg, 3) : null,
                ["selected"] = Selected != null ? Selected.ToDictionary() : null,
                ["elements"] = Elements.Select(tag => tag.Name).ToList(),
                ["availableMaterials"] = Available.Take(20).Select(item => item.ToDictionary()).ToList(),
                ["candidateMaterials"] = Candidates.Take(20).Select(item => item.ToDictionary()).ToList(),
                ["fallbackMaterial"] = Available.Count > 0 ? Available[0].Tag.Name : null,
                ["next"] = Valid
                    ? "Material is usable."
                    : Available.Count > 0
                        ? "Retry with material=auto or material=" + Available[0].Tag.Name + " if exact material is not required."
                        : "No usable material is currently available; inspect inventory/material candidates before retrying.",
                ["suggestion"] = Available.Count > 0 ? "Use material=auto or material=" + Available[0].Tag.Name : "No available material; inspect read_control domain=resources action=inventory/building_control domain=planning action=materials",
                ["error"] = Error
            };
            }
        }

        private sealed class SupportValidation
        {
            public bool Valid;
            public bool WarningOnly;
            public string Rule;
            public List<Dictionary<string, object>> MissingSupportCells = new List<Dictionary<string, object>>();
            public string Error;

            public static SupportValidation Success(string rule, List<Dictionary<string, object>> missing)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>()
                };
            }

            public static SupportValidation Warning(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = true,
                    WarningOnly = true,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public static SupportValidation Invalid(string rule, List<Dictionary<string, object>> missing, string error)
            {
                return new SupportValidation
                {
                    Valid = false,
                    WarningOnly = false,
                    Rule = rule,
                    MissingSupportCells = missing ?? new List<Dictionary<string, object>>(),
                    Error = error
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["warningOnly"] = WarningOnly,
                    ["buildLocationRule"] = Rule,
                    ["missingSupportCells"] = MissingSupportCells,
                    ["error"] = Error
                };
            }
        }

        private sealed class PlacementCandidate
        {
            public int Score;
            public string Status;
            public Dictionary<string, object> Anchor = new Dictionary<string, object>();
            public Dictionary<string, object> Preview = new Dictionary<string, object>();

            public int AnchorX => GetInt(Anchor, "x");
            public int AnchorY => GetInt(Anchor, "y");

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["score"] = Score,
                    ["status"] = Status,
                    ["anchor"] = Anchor,
                    ["preview"] = Preview,
                    ["placement"] = GetObject(Preview, "placement"),
                    ["footprint"] = GetObjectList(Preview, "footprint"),
                    ["support"] = GetObject(Preview, "support"),
                    ["materialSelection"] = GetObject(Preview, "materialSelection"),
                    ["facade"] = Preview != null && Preview.ContainsKey("facade") ? Preview["facade"] : null,
                    ["error"] = Preview != null && Preview.ContainsKey("error") ? Preview["error"] : null
                };
            }
        }
    }
}
