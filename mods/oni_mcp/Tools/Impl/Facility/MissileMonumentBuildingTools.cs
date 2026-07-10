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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=missile_launcher action=list",
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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=missile_launcher action=set_ammunition",
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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=monument_part action=list",
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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=special kind=monument_part action=set",
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

    }
}
