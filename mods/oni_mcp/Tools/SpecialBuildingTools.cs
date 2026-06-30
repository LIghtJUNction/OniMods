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
    public static class SpecialBuildingTools
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
                Description = "兼容旧工具：请改用 building_control domain=special kind=artable action=list",
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
                Description = "兼容旧工具：请改用 building_control domain=special kind=artable action=set_stage",
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

        public static McpTool ControlCreatureLure()
        {
            return new McpTool
            {
                Name = "creature_lure_control",
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "lure_control", "creature_lure_bait_control" },
                Tags = new List<string> { "buildings", "ranching", "lure", "bait", "side-screen" },
                Description = "生物诱饵站聚合工具：action=list 查询可选诱饵；action=set_bait 选择或清空诱饵",
                Parameters = CreatureLureControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListCreatureLures().Handler(args);
                    if (action == "set_bait" || action == "set")
                        return SetCreatureLureBait().Handler(args);
                    return CallToolResult.Error("action must be list or set_bait");
                }
            };
        }

        public static McpTool ListCreatureLures()
        {
            return new McpTool
            {
                Name = "creature_lures_list",
                Hidden = true,
                Group = "ranching",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "lures_list", "creature_lure_baits_list" },
                Tags = new List<string> { "buildings", "ranching", "lure", "bait", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=creature_lure action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId 或诱饵 tag 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingComponent(args, go => go.GetComponent<CreatureLure>() != null, go => CreatureLureInfo(go.GetComponent<CreatureLure>()), "lures")
            };
        }

        public static McpTool SetCreatureLureBait()
        {
            return new McpTool
            {
                Name = "creature_lure_bait_set",
                Hidden = true,
                Group = "ranching",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "lure_bait_set" },
                Tags = new List<string> { "buildings", "ranching", "lure", "bait", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=creature_lure action=set_bait",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["baitTag"] = new McpToolParameter { Type = "string", Description = "诱饵 tag，如 SlimeMold 或 Phosphorite；clear=true 时忽略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "true 表示清空诱饵选择", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, component => component.GetComponent<CreatureLure>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target CreatureLure not found");
                    var lure = go.GetComponent<CreatureLure>();
                    var before = CreatureLureInfo(lure);
                    bool clear = ToolUtil.GetBool(args, "clear", false);
                    Tag bait = Tag.Invalid;
                    if (!clear)
                    {
                        bait = new Tag(args["baitTag"]?.ToString() ?? "");
                        if (bait == Tag.Invalid || !lure.baitTypes.Contains(bait))
                            return CallToolResult.Error("baitTag must be one of the target lure baitTypes");
                    }
                    lure.ChangeBaitSetting(bait);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["lure"] = CreatureLureInfo(lure)
                    });
                }
            };
        }

        public static McpTool ListGeneShufflers()
        {
            return new McpTool
            {
                Name = "gene_shufflers_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "neural_vacillators_list" },
                Tags = new List<string> { "buildings", "gene-shuffler", "trait", "recharge", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=gene_shuffler action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId 或分配对象筛选", Required = false },
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
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var items = UnityEngine.Object.FindObjectsByType<GeneShuffler>(FindObjectsSortMode.None)
                        .Where(item => MatchesTarget(item?.gameObject, rect, worldId))
                        .Select(GeneShufflerInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["geneShufflers"] = items
                    });
                }
            };
        }

        public static McpTool ControlGeneShuffler()
        {
            return new McpTool
            {
                Name = "gene_shuffler_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "neural_vacillator_control" },
                Tags = new List<string> { "buildings", "gene-shuffler", "trait", "recharge", "side-screen" },
                Description = "Gene Shuffler 聚合工具：action=list 查询状态；complete/request_recharge/cancel_recharge/toggle_recharge 执行按钮",
                Parameters = GeneShufflerControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(action) || action == "list")
                        return ListGeneShufflers().Handler(args);

                    var shuffler = FindGeneShuffler(args);
                    if (shuffler == null)
                        return CallToolResult.Error("Target GeneShuffler not found");
                    var before = GeneShufflerInfo(shuffler);

                    if (action == "complete")
                    {
                        if (!shuffler.WorkComplete)
                            return CallToolResult.Error("GeneShuffler work is not complete");
                        shuffler.SetWorkTime(0f);
                    }
                    else if (action == "request_recharge" || action == "cancel_recharge" || action == "toggle_recharge")
                    {
                        if (!shuffler.IsConsumed)
                            return CallToolResult.Error("GeneShuffler is not consumed");
                        bool next = action == "toggle_recharge" ? !shuffler.RechargeRequested : action == "request_recharge";
                        shuffler.RequestRecharge(next);
                        shuffler.RefreshSideScreen();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be complete, request_recharge, cancel_recharge, or toggle_recharge");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(shuffler.gameObject),
                        ["before"] = before,
                        ["geneShuffler"] = GeneShufflerInfo(shuffler)
                    });
                }
            };
        }

        public static McpTool ControlMissileLauncher()
        {
            return new McpTool
            {
                Name = "missile_launcher_control",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "missile_ammunition_control", "missile_launcher_ammunition_control" },
                Tags = new List<string> { "buildings", "rocket", "missile", "ammunition", "side-screen" },
                Description = "导弹发射器聚合工具：action=list 查询弹药允许状态；action=set_ammunition 设置弹药允许状态",
                Parameters = MissileLauncherControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListMissileLaunchers().Handler(args);
                    if (action == "set_ammunition" || action == "set")
                        return SetMissileAmmunition().Handler(args);
                    return CallToolResult.Error("action must be list or set_ammunition");
                }
            };
        }

        public static McpTool ListMissileLaunchers()
        {
            return new McpTool
            {
                Name = "missile_launchers_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "missile_ammunition_list" },
                Tags = new List<string> { "buildings", "rocket", "missile", "ammunition", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=missile_launcher action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId 或弹药 tag 筛选", Required = false },
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
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var items = Components.MissileLaunchers.Items
                        .Where(item => MatchesTarget(item?.gameObject, rect, worldId))
                        .Select(MissileInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["missileLaunchers"] = items
                    });
                }
            };
        }

        public static McpTool SetMissileAmmunition()
        {
            return new McpTool
            {
                Name = "missile_ammunition_set",
                Hidden = true,
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "missile_launcher_ammunition_set" },
                Tags = new List<string> { "buildings", "rocket", "missile", "ammunition", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=missile_launcher action=set_ammunition",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["ammoTag"] = new McpToolParameter { Type = "string", Description = "弹药 tag，如 MissileBasic、MissileLongRange 或 DLC cosmic blast 类型", Required = true },
                    ["allowed"] = new McpToolParameter { Type = "boolean", Description = "是否允许该弹药", Required = true }
                }),
                Handler = args =>
                {
                    var launcher = FindMissileLauncher(args);
                    if (launcher == null)
                        return CallToolResult.Error("Target missile launcher not found");
                    var ammo = new Tag(args["ammoTag"]?.ToString() ?? "");
                    var valid = GetMissileAmmunitionTags(launcher);
                    if (ammo == Tag.Invalid || !valid.Contains(ammo))
                        return CallToolResult.Error("ammoTag is not valid for this missile launcher");
                    bool allowed = ToolUtil.GetBool(args, "allowed", launcher.AmmunitionIsAllowed(ammo));
                    var before = MissileInfo(launcher);
                    launcher.ChangeAmmunition(ammo, allowed);
                    launcher.OnRowToggleClick();
                    if (DetailsScreen.Instance != null && launcher.gameObject.GetComponent<KSelectable>()?.IsSelected == true)
                        DetailsScreen.Instance.Refresh(launcher.gameObject);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(launcher.gameObject),
                        ["before"] = before,
                        ["missileLauncher"] = MissileInfo(launcher)
                    });
                }
            };
        }

        public static McpTool ControlMonumentPart()
        {
            return new McpTool
            {
                Name = "monument_part_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "monument_facade_control" },
                Tags = new List<string> { "buildings", "monument", "decor", "facade", "side-screen" },
                Description = "纪念碑部件聚合工具：action=list 查询可选外观；action=set 设置外观或 rotate=true 翻转",
                Parameters = MonumentPartControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListMonumentParts().Handler(args);
                    if (action == "set")
                        return SetMonumentPart().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ListMonumentParts()
        {
            return new McpTool
            {
                Name = "monument_parts_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "monument_facades_list" },
                Tags = new List<string> { "buildings", "monument", "decor", "facade", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=monument_part action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、part 或外观 id 筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选外观，默认 true", Required = false },
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
                    var items = Components.MonumentParts.Items
                        .Where(item => MatchesTarget(item?.gameObject, rect, worldId))
                        .Select(item => MonumentInfo(item, includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["monumentParts"] = items
                    });
                }
            };
        }

        public static McpTool SetMonumentPart()
        {
            return new McpTool
            {
                Name = "monument_part_set",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "monument_facade_set" },
                Tags = new List<string> { "buildings", "monument", "decor", "facade", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=monument_part action=set",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["partId"] = new McpToolParameter { Type = "string", Description = "MonumentPartResource id；rotate=true 时可省略", Required = false },
                    ["rotate"] = new McpToolParameter { Type = "boolean", Description = "true 表示执行翻转按钮", Required = false }
                }),
                Handler = args =>
                {
                    var part = FindMonumentPart(args);
                    if (part == null)
                        return CallToolResult.Error("Target monument part not found");
                    bool rotate = ToolUtil.GetBool(args, "rotate", false);
                    string partId = args["partId"]?.ToString();
                    if (!rotate && string.IsNullOrWhiteSpace(partId))
                        return CallToolResult.Error("partId or rotate=true is required");

                    var before = MonumentInfo(part, true);
                    if (!string.IsNullOrWhiteSpace(partId))
                    {
                        var option = Db.GetMonumentParts().TryGet(partId);
                        if (option == null || option.part != part.part)
                            return CallToolResult.Error("partId is not valid for this monument part type");
                        part.SetState(option.Id);
                    }
                    if (rotate)
                    {
                        var rotatable = part.GetComponent<Rotatable>();
                        if (rotatable == null)
                            return CallToolResult.Error("Target monument part is not rotatable");
                        rotatable.Rotate();
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(part.gameObject),
                        ["before"] = before,
                        ["monumentPart"] = MonumentInfo(part, true)
                    });
                }
            };
        }

        private static CallToolResult ListBuildingComponent(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit)
                .ToList();
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static Dictionary<string, object> ArtableInfo(Artable artable, bool includeOptions)
        {
            var result = TargetInfo(artable.gameObject);
            result["currentStage"] = artable.CurrentStage;
            result["sideScreenValid"] = artable.CurrentStage != "Default";
            result["options"] = includeOptions ? GetSelectableArtableStages(artable).Select(ArtableStageInfo).ToList() : new List<Dictionary<string, object>>();
            return result;
        }

        private static IEnumerable<ArtableStage> GetSelectableArtableStages(Artable artable)
        {
            var prefabStages = Db.GetArtableStages().GetPrefabStages(artable.GetComponent<KPrefabID>().PrefabID());
            var current = prefabStages.Find(stage => stage.id == artable.CurrentStage);
            return prefabStages
                .Where(stage => stage.id != "Default")
                .Where(stage => current == null || stage.statusItem.StatusType == current.statusItem.StatusType)
                .Where(stage => stage.IsUnlocked());
        }

        private static Dictionary<string, object> ArtableStageInfo(ArtableStage stage)
        {
            return new Dictionary<string, object>
            {
                ["id"] = stage.id,
                ["name"] = stage.Name,
                ["status"] = stage.statusItem.Id,
                ["quality"] = stage.statusItem.StatusType.ToString(),
                ["decor"] = stage.decor,
                ["unlocked"] = stage.IsUnlocked()
            };
        }

        private static Dictionary<string, object> CreatureLureInfo(CreatureLure lure)
        {
            var result = TargetInfo(lure.gameObject);
            result["activeBait"] = lure.activeBaitSetting == Tag.Invalid ? null : lure.activeBaitSetting.Name;
            result["baitTypes"] = lure.baitTypes.Select(BaitInfo).ToList();
            result["storageKg"] = lure.baitStorage == null ? 0 : Math.Round(ToolUtil.SafeFloat(lure.baitStorage.MassStored()), 3);
            return result;
        }

        private static Dictionary<string, object> BaitInfo(Tag tag)
        {
            var element = ElementLoader.GetElement(tag);
            return new Dictionary<string, object>
            {
                ["tag"] = tag.Name,
                ["name"] = element?.name ?? tag.ProperName(),
                ["element"] = element?.id.ToString()
            };
        }

        private static Dictionary<string, object> GeneShufflerInfo(GeneShuffler shuffler)
        {
            var result = TargetInfo(shuffler.gameObject);
            result["isConsumed"] = shuffler.IsConsumed;
            result["rechargeRequested"] = shuffler.RechargeRequested;
            result["workComplete"] = shuffler.WorkComplete;
            result["isWorking"] = shuffler.IsWorking;
            result["assigned"] = shuffler.assignable?.assignee == null ? null : ToolUtil.CleanName(shuffler.assignable.assignee.GetProperName());
            result["storageKg"] = shuffler.storage == null ? 0 : Math.Round(ToolUtil.SafeFloat(shuffler.storage.MassStored()), 3);
            return result;
        }

        private static Dictionary<string, object> MissileInfo(MissileLauncher.Instance launcher)
        {
            var result = TargetInfo(launcher.gameObject);
            result["ammunition"] = GetMissileAmmunitionTags(launcher).Select(tag => new Dictionary<string, object>
            {
                ["tag"] = tag.Name,
                ["name"] = tag.ProperNameStripLink(),
                ["allowed"] = launcher.AmmunitionIsAllowed(tag)
            }).ToList();
            result["anyCosmicBlastShotAllowed"] = launcher.IsAnyCosmicBlastShotAllowed();
            return result;
        }

        private static List<Tag> GetMissileAmmunitionTags(MissileLauncher.Instance launcher)
        {
            var tags = launcher.GetValidAmmunitionTags();
            if (DlcManager.IsExpansion1Active())
            {
                foreach (var tag in MissileLauncherConfig.CosmicBlastShotTypes)
                {
                    if (!tags.Contains(tag))
                        tags.Add(tag);
                }
            }
            return tags;
        }

        private static Dictionary<string, object> MonumentInfo(MonumentPart part, bool includeOptions)
        {
            var result = TargetInfo(part.gameObject);
            result["part"] = part.part.ToString();
            result["chosenState"] = GetMonumentChosenState(part);
            result["options"] = includeOptions ? Db.GetMonumentParts().GetParts(part.part).Select(MonumentOptionInfo).ToList() : new List<Dictionary<string, object>>();
            return result;
        }

        private static Dictionary<string, object> MonumentOptionInfo(MonumentPartResource resource)
        {
            return new Dictionary<string, object>
            {
                ["id"] = resource.Id,
                ["name"] = resource.Name,
                ["part"] = resource.part.ToString(),
                ["state"] = resource.State,
                ["unlocked"] = resource.IsUnlocked()
            };
        }

        private static string GetMonumentChosenState(MonumentPart part)
        {
            var field = OniReflection.GetFieldSafe(typeof(MonumentPart), "chosenState", false);
            return field?.GetValue(part)?.ToString();
        }

        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId) || !predicate(go))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static GeneShuffler FindGeneShuffler(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var shuffler in UnityEngine.Object.FindObjectsByType<GeneShuffler>(FindObjectsSortMode.None))
            {
                if (shuffler == null || !ToolUtil.GameObjectMatchesWorld(shuffler.gameObject, worldId))
                    continue;
                var kpid = shuffler.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return shuffler;
                if (cell.HasValue && Grid.PosToCell(shuffler) == cell.Value)
                    return shuffler;
            }
            return null;
        }

        private static MissileLauncher.Instance FindMissileLauncher(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var launcher in Components.MissileLaunchers.Items)
            {
                if (launcher == null || !ToolUtil.GameObjectMatchesWorld(launcher.gameObject, worldId))
                    continue;
                var kpid = launcher.gameObject.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return launcher;
                if (cell.HasValue && Grid.PosToCell(launcher.gameObject) == cell.Value)
                    return launcher;
            }
            return null;
        }

        private static MonumentPart FindMonumentPart(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var part in Components.MonumentParts.Items)
            {
                if (part == null || !ToolUtil.GameObjectMatchesWorld(part.gameObject, worldId))
                    continue;
                var kpid = part.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return part;
                if (cell.HasValue && Grid.PosToCell(part) == cell.Value)
                    return part;
            }
            return null;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
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

        private static Dictionary<string, McpToolParameter> AreaLookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = RectParams(new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false }
            });
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> ArtableControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_stage", Required = true, EnumValues = new List<string> { "list", "set_stage" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、阶段 id 或阶段名筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选阶段，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["stageId"] = new McpToolParameter { Type = "string", Description = "action=set_stage 时的目标 ArtableStage id；clear=true 时忽略", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set_stage 时 true 表示清空成 Default 并重新等待创作", Required = false },
                ["force"] = new McpToolParameter { Type = "boolean", Description = "action=set_stage 时跳过解锁和当前品质过滤检查，默认 false", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> CreatureLureControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_bait", Required = true, EnumValues = new List<string> { "list", "set_bait" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId 或诱饵 tag 筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["baitTag"] = new McpToolParameter { Type = "string", Description = "action=set_bait 时的诱饵 tag，如 SlimeMold 或 Phosphorite；clear=true 时忽略", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set_bait 时 true 表示清空诱饵选择", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> MissileLauncherControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_ammunition", Required = true, EnumValues = new List<string> { "list", "set_ammunition" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId 或弹药 tag 筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["ammoTag"] = new McpToolParameter { Type = "string", Description = "action=set_ammunition 时的弹药 tag，如 MissileBasic、MissileLongRange 或 DLC cosmic blast 类型", Required = false },
                ["allowed"] = new McpToolParameter { Type = "boolean", Description = "action=set_ammunition 时是否允许该弹药", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> GeneShufflerControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list、complete、request_recharge、cancel_recharge 或 toggle_recharge", Required = false, EnumValues = new List<string> { "list", "complete", "request_recharge", "cancel_recharge", "toggle_recharge" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按名称、prefabId 或分配对象筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> MonumentPartControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、part 或外观 id 筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选外观，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["partId"] = new McpToolParameter { Type = "string", Description = "action=set 时的 MonumentPartResource id；rotate=true 时可省略", Required = false },
                ["rotate"] = new McpToolParameter { Type = "boolean", Description = "action=set 时 true 表示执行翻转按钮", Required = false }
            });
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || ToolUtil.GetInt(args, "x1").HasValue
                   || ToolUtil.GetInt(args, "y1").HasValue
                   || ToolUtil.GetInt(args, "x2").HasValue
                   || ToolUtil.GetInt(args, "y2").HasValue;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }
    }
}
