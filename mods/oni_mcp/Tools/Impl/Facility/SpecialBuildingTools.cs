using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class SpecialBuildingTools
    {
        public static McpTool ControlSpecialBuilding()
        {
            return new McpTool
            {
                Name = "special_building_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "special_side_building_control" },
                Tags = new List<string> { "buildings", "special", "side-screen", "art", "ranching", "rocket", "monument" },
                Description = "特殊建筑侧屏聚合工具：kind=artable/creature_lure/gene_shuffler/missile_launcher/monument_part，action 使用对应旧 control 的动作。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "特殊建筑类型：artable、creature_lure、gene_shuffler、missile_launcher、monument_part", Required = true, EnumValues = new List<string> { "artable", "creature_lure", "gene_shuffler", "missile_launcher", "monument_part" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：list、set_stage、set_bait、complete、request_recharge、cancel_recharge、toggle_recharge、set_ammunition、set", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑或区域 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑或区域 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按名称、prefabId 或选项筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选项", Required = false },
                    ["stageId"] = new McpToolParameter { Type = "string", Description = "kind=artable action=set_stage 时的阶段 id", Required = false },
                    ["baitTag"] = new McpToolParameter { Type = "string", Description = "kind=creature_lure action=set_bait 时的诱饵 tag", Required = false },
                    ["missileId"] = new McpToolParameter { Type = "string", Description = "kind=missile_launcher action=set_ammunition 时的弹药 id", Required = false },
                    ["allowed"] = new McpToolParameter { Type = "boolean", Description = "kind=missile_launcher action=set_ammunition 时是否允许", Required = false },
                    ["partId"] = new McpToolParameter { Type = "string", Description = "kind=monument_part action=set 时的外观 id", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "清空当前选择；用于 artable/creature_lure", Required = false },
                    ["rotate"] = new McpToolParameter { Type = "boolean", Description = "kind=monument_part action=set 时执行翻转", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过部分预检查", Required = false }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "artable":
                            return ControlArtable().Handler(args);
                        case "creature_lure":
                        case "lure":
                            return ControlCreatureLure().Handler(args);
                        case "gene_shuffler":
                        case "neural_vacillator":
                            return ControlGeneShuffler().Handler(args);
                        case "missile_launcher":
                            return ControlMissileLauncher().Handler(args);
                        case "monument_part":
                            return ControlMonumentPart().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be artable, creature_lure, gene_shuffler, missile_launcher, or monument_part");
                    }
                }
            };
        }

        public static McpTool ControlArtable()
        {
            return new McpTool
            {
                Name = "artable_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "artwork_selection_control", "artable_facade_control" },
                Tags = new List<string> { "buildings", "art", "decor", "facade", "side-screen" },
                Description = "艺术建筑外观聚合工具：action=list 查询可选阶段；action=set_stage 设置阶段或 clear=true 重做",
                Parameters = ArtableControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListArtables().Handler(args);
                    if (action == "set_stage" || action == "set")
                        return SetArtableStage().Handler(args);
                    return CallToolResult.Error("action must be list or set_stage");
                }
            };
        }

        public static McpTool ListArtables()
        {
            return new McpTool
            {
                Name = "artables_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "artwork_selection_list", "artable_facades_list" },
                Tags = new List<string> { "buildings", "art", "decor", "facade", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=artable action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、阶段 id 或阶段名筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选阶段，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool includeOptions = ToolUtil.GetBool(args, "includeOptions", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var results = Components.BuildingCompletes.Items
                        .Select(building => building?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<Artable>() != null)
                        .Select(go => ArtableInfo(go.GetComponent<Artable>(), includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["artables"] = results
                    });
                }
            };
        }

        public static McpTool SetArtableStage()
        {
            return new McpTool
            {
                Name = "artable_stage_set",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "artwork_selection_set", "artable_facade_set" },
                Tags = new List<string> { "buildings", "art", "decor", "facade", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=artable action=set_stage",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["stageId"] = new McpToolParameter { Type = "string", Description = "目标 ArtableStage id；clear=true 时忽略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "true 表示清空成 Default 并重新等待创作", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过解锁和当前品质过滤检查，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, component => component.GetComponent<Artable>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target Artable not found");
                    var artable = go.GetComponent<Artable>();
                    bool clear = ToolUtil.GetBool(args, "clear", false);
                    bool force = ToolUtil.GetBool(args, "force", false);
                    var before = ArtableInfo(artable, true);

                    if (clear)
                    {
                        artable.SetDefault();
                    }
                    else
                    {
                        string stageId = args["stageId"]?.ToString();
                        if (string.IsNullOrWhiteSpace(stageId))
                            return CallToolResult.Error("stageId or clear=true is required");
                        var stage = Db.GetArtableStages().TryGet(stageId);
                        if (stage == null)
                            return CallToolResult.Error("Unknown ArtableStage id");
                        if (!force && !GetSelectableArtableStages(artable).Any(item => item.id == stage.id))
                            return CallToolResult.Error("Stage is not currently selectable for this artable; use force=true to override");
                        artable.SetUserChosenTargetState(stage.id);
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["artable"] = ArtableInfo(artable, true)
                    });
                }
            };
        }

    }
}
