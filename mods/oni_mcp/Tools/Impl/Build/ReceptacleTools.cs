using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ReceptacleTools
    {
        public static McpTool ListReceptacles()
        {
            return new McpTool
            {
                Name = "receptacles_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "single_entity_receptacles_list", "receptacle_side_screens_list" },
                Tags = new List<string> { "buildings", "side-screen", "receptacle", "single-entity", "pedestal", "rocket", "cargo" },
                Description = "兼容入口：请优先使用 building_control domain=receptacle action=list。列出 ReceptacleSideScreen / SingleEntityReceptacle 通用实体请求控件，包含特殊火箭货舱；不含种植箱和孵化器",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、请求对象或当前 occupant 筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可请求实体选项，默认 true", Required = false },
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

                    var rows = AllReceptacles()
                        .Where(item => MatchesTarget(item.gameObject, rect, worldId))
                        .Select(item => ReceptacleInfo(item, includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["receptacles"] = rows
                    });
                }
            };
        }

        public static McpTool ControlReceptacle()
        {
            return new McpTool
            {
                Name = "receptacle_control",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "single_entity_receptacle_control", "receptacle_side_screen_control" },
                Tags = new List<string> { "buildings", "side-screen", "receptacle", "single-entity", "rocket", "cargo" },
                Description = "统一读取、单点和批量执行 ReceptacleSideScreen 操作。action=list/request/cancel_request/remove_occupant/cancel_remove/batch；写操作需 confirm=true。",
                Parameters = ReceptacleControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(action) || action == "list")
                        return ListReceptacles().Handler(args);
                    if (action == "batch")
                        return BatchControlReceptacles().Handler(args);

                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for receptacle changes");

                    var receptacle = FindReceptacle(args);
                    if (receptacle == null)
                        return CallToolResult.Error("SingleEntityReceptacle target not found");

                    var before = ReceptacleInfo(receptacle, includeOptions: false);
                    var error = ApplyReceptacleAction(receptacle, args);
                    if (error != null)
                        return CallToolResult.Error(error);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(receptacle.gameObject),
                        ["before"] = before,
                        ["receptacle"] = ReceptacleInfo(receptacle, includeOptions: true)
                    });
                }
            };
        }

        public static McpTool BatchControlReceptacles()
        {
            return new McpTool
            {
                Name = "receptacles_batch_control",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "single_entity_receptacles_batch_control" },
                Tags = new List<string> { "buildings", "side-screen", "receptacle", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=receptacle action=batch。批量执行 ReceptacleSideScreen 操作；items 支持短字段 a/tag/w，defaults 可共享 action/entityTag/worldId，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "数组；每项支持 id 或 x/y/worldId，并提供 action、entityTag/additionalTag 等参数；短字段 a=action、tag=entityTag、w=worldId", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；支持 action/a、entityTag/tag、worldId/w，子项参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量修改实体请求", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for receptacle batch changes");

                    var items = args["items"] as JArray;
                    if (items == null || items.Count == 0)
                        return CallToolResult.Error("items array is required");

                    var defaults = args["defaults"] as JObject ?? args["defaultArguments"] as JObject;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var token in items)
                    {
                        var rawItem = token as JObject;
                        if (rawItem == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "item must be an object" });
                            continue;
                        }

                        var item = MergeReceptacleDefaults(rawItem, defaults);
                        var receptacle = FindReceptacle(item);
                        if (receptacle == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "SingleEntityReceptacle target not found", ["input"] = item });
                            continue;
                        }

                        var before = ReceptacleInfo(receptacle, includeOptions: false);
                        var error = ApplyReceptacleAction(receptacle, item);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = error == null,
                            ["error"] = error,
                            ["target"] = TargetInfo(receptacle.gameObject),
                            ["before"] = before,
                            ["receptacle"] = ReceptacleInfo(receptacle, includeOptions: false)
                        });
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["requested"] = items.Count,
                        ["succeeded"] = results.Count(item => (bool)item["ok"]),
                        ["failed"] = results.Count(item => !(bool)item["ok"]),
                        ["results"] = results
                    });
                }
            };
        }

    }
}
