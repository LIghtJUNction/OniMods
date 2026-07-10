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
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "最多自动标记挖掘/铲除格，默认 100，最大 500", Required = false },
                    ["nativePathPlacement"] = new McpToolParameter { Type = "boolean", Description = "Default true. Use ONI's native utility drag placement for the whole path before falling back to per-cell placement.", Required = false }
                },
                Handler = args =>
                {
                    bool dryRun = IsDryRun(args);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        prefabId = DefaultUtilityPrefab(args["type"]?.ToString());
                    string resolvedPrefabId;
                    string resolveError;
                    var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
                    if (def == null)
                        return CallToolResult.Error(resolveError);
prefabId = resolvedPrefabId;
args["prefabId"] = prefabId;

string availabilityError = BuildAvailabilityError(def);
if (availabilityError != null)
return CallToolResult.Error(availabilityError);

if (!IsLinearUtilityPrefab(def.PrefabID))
return CallToolResult.Error("utility_auto_connect only supports linear utility prefabs such as Wire, LiquidConduit, GasConduit, SolidConduit and LogicWire");

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 200, 500));
                    string error;
                    var path = ResolveUtilityPath(args, maxCells, out error);
                    if (error != null)
                        return CallToolResult.Error(error);

                    if (args["worldId"] == null)
                        args["worldId"] = ToolUtil.ResolveWorldId(args);
                    if (args["material"] == null)
                        args["material"] = "auto";
                    if (args["autoDigObstructions"] == null)
                        args["autoDigObstructions"] = true;
                    if (args["autoUprootObstructions"] == null)
                        args["autoUprootObstructions"] = true;

if (!dryRun && ToolUtil.GetBool(args, "nativePathPlacement", true))
                    {
                        var nativePath = TryPlaceUtilityPathNative(def, path, args);
                        if (GetBool(nativePath, "success") || !GetBool(nativePath, "shouldFallback"))
                            return CallToolResult.Text(JsonConvert.SerializeObject(nativePath, McpJsonUtil.Settings));
                    }

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
                    bool alreadyPresent = result.ContainsKey("alreadyPresent") && (bool)result["alreadyPresent"];
                    bool alreadyConnected = result.ContainsKey("alreadyConnected") && (bool)result["alreadyConnected"];
                        autoMarked += GetAutoDigInt(result, "marked") + GetAutoDigInt(result, "uprootMarked") + GetAutoDigInt(result, "alreadyMarked") + GetAutoDigInt(result, "alreadyUprootMarked");
                    if (ok || alreadyPresent || alreadyConnected || (dryRun && validPlacement) || IsAutoDigResult(result))
                    {
                        valid++;
                        if (ok)
                            planned++;
                        if (alreadyPresent || alreadyConnected)
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
                        ["placementMode"] = dryRun ? "dry_run_validation" : "cell_by_cell_fallback",
                        ["pathMode"] = "continuous_manhattan_path",
                ["pathCells"] = path.Count,
                ["planned"] = planned,
                ["reusedExisting"] = reused,
                ["valid"] = valid,
                ["autoMarkedObstructions"] = autoMarked,
                ["failed"] = errors.Count,
                ["autoDigLimitReached"] = autoDigContext.LimitReached,
                ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                ["segments"] = BuildPathSegments(path),
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

                    string resolvedPrefabId;
                    string resolveError;
                    var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
                    if (def == null)
                        return CallToolResult.Error(resolveError);
                    prefabId = resolvedPrefabId;

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
    }
}
