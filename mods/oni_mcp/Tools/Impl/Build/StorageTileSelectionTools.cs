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
        public static McpTool ListStorageTileSelections()
        {
            return new McpTool
            {
                Name = "storage_tile_selections_list",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "single_item_selection_list", "storage_tiles_target_items_list" },
                Tags = new List<string> { "resources", "storage", "side-screen", "single-item", "tile" },
                Description = "兼容入口：请优先使用 building_control domain=tile_selection action=list。列出 SingleItemSelectionSideScreen / StorageTile 的目标物品选择、容量、当前内容和可选物品",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、目标物品或可选物品筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选择物品，默认 true", Required = false },
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

                    var rows = AllStorageTiles()
                        .Where(item => MatchesTarget(item.gameObject, rect, worldId))
                        .Select(item => StorageTileInfo(item, includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["storageTiles"] = rows
                    });
                }
            };
        }

        public static McpTool SetStorageTileSelection()
        {
            return new McpTool
            {
                Name = "storage_tile_selection_set",
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "single_item_selection_set", "storage_tile_target_item_set" },
                Tags = new List<string> { "resources", "storage", "side-screen", "single-item", "tile" },
                Description = "兼容入口：请优先使用 building_control domain=tile_selection action=set。设置 SingleItemSelectionSideScreen / StorageTile 的目标物品；clear=true 选择 None，需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["itemTag"] = new McpToolParameter { Type = "string", Description = "目标物品 tag；clear=true 时可省略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "是否选择 None / GameTags.Void", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改目标物品", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for storage tile target changes");

                    var tile = FindStorageTile(args);
                    if (tile == null)
                        return CallToolResult.Error("StorageTile target not found");

                    var before = StorageTileInfo(tile, includeOptions: false);
                    var error = ApplyStorageTileSelection(tile, args);
                    if (error != null)
                        return CallToolResult.Error(error);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(tile.gameObject),
                        ["before"] = before,
                        ["storageTile"] = StorageTileInfo(tile, includeOptions: true)
                    });
                }
            };
        }

        public static McpTool BatchSetStorageTileSelections()
        {
            return new McpTool
            {
                Name = "storage_tile_selections_batch_set",
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "single_item_selections_batch_set" },
                Tags = new List<string> { "resources", "storage", "side-screen", "single-item", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=tile_selection action=batch。批量设置 StorageTile 目标物品；items 支持短字段 i/c/w，defaults 可共享 itemTag/clear/worldId，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "数组；每项支持 id 或 x/y/worldId，并提供 itemTag 或 clear=true；短字段 i=itemTag、c=clear、w=worldId", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；支持 itemTag/i、clear/c、worldId/w，子项参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量修改目标物品", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for storage tile target batch changes");

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

                        var item = MergeStorageTileDefaults(rawItem, defaults);
                        var tile = FindStorageTile(item);
                        if (tile == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "StorageTile target not found", ["input"] = item });
                            continue;
                        }

                        var before = StorageTileInfo(tile, includeOptions: false);
                        var error = ApplyStorageTileSelection(tile, item);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = error == null,
                            ["error"] = error,
                            ["target"] = TargetInfo(tile.gameObject),
                            ["before"] = before,
                            ["storageTile"] = StorageTileInfo(tile, includeOptions: false)
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

        public static McpTool ControlStorageTileSelection()
        {
            return new McpTool
            {
                Name = "storage_tile_selection_control",
                Hidden = true,
                Group = "resources",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "single_item_selection_control", "storage_tile_target_item_control" },
                Tags = new List<string> { "resources", "storage", "side-screen", "single-item", "tile", "batch" },
                Description = "统一读取、单点设置和批量设置 StorageTile 目标物品。action=list/set/batch；set/batch 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、set、batch", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、目标物品或可选物品筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选择物品，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标 KPrefabID InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=list/set 时可选区域或目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=list/set 时可选区域或目标 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前或目标格所在世界", Required = false },
                    ["itemTag"] = new McpToolParameter { Type = "string", Description = "action=set 时目标物品 tag；clear=true 时可省略", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set 时是否选择 None / GameTags.Void", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时数组；每项支持 id 或 x/y/worldId，并提供 itemTag 或 clear=true；短字段 i/c/w", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set/batch 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListStorageTileSelections().Handler(args);
                    if (action == "set")
                        return SetStorageTileSelection().Handler(args);
                    if (action == "batch")
                        return BatchSetStorageTileSelections().Handler(args);
                    return CallToolResult.Error("action must be one of list, set, batch");
                }
            };
        }

    }
}
