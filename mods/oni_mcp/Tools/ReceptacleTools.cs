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
    public static class ReceptacleTools
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

        private static string ApplyReceptacleAction(SingleEntityReceptacle receptacle, JObject args)
        {
            string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "request":
                {
                    string tagName = args["entityTag"]?.ToString();
                    if (string.IsNullOrWhiteSpace(tagName))
                        return "entityTag is required for action=request";
                    var tag = TagManager.Create(tagName.Trim());
                    var prefab = Assets.GetPrefab(tag);
                    if (prefab == null)
                        return "entityTag prefab not found";
                    if (!IsValidReceptacleOption(receptacle, prefab))
                        return "entityTag is not valid for this receptacle";
                    if (receptacle.Occupant != null)
                        return "Receptacle already has an occupant; use action=remove_occupant first";
                    if (receptacle.GetActiveRequest != null && ToolUtil.GetBool(args, "replaceExistingRequest", true))
                        receptacle.CancelActiveRequest();
                    if (receptacle.GetActiveRequest != null)
                        return "Receptacle already has an active request";
                    var additional = string.IsNullOrWhiteSpace(args["additionalTag"]?.ToString())
                        ? AdditionalTagForPrefab(prefab)
                        : TagManager.Create(args["additionalTag"].ToString().Trim());
                    receptacle.CreateOrder(tag, additional);
                    return null;
                }
                case "cancel_request":
                    receptacle.CancelActiveRequest();
                    return null;
                case "remove_occupant":
                    if (receptacle.Occupant == null)
                        return "Receptacle has no occupant";
                    receptacle.OrderRemoveOccupant();
                    return null;
                case "cancel_remove":
                {
                    var uprootable = receptacle.Occupant == null ? null : receptacle.Occupant.GetComponent<Uprootable>();
                    if (uprootable == null || !uprootable.IsMarkedForUproot)
                        return "Receptacle occupant is not marked for removal";
                    uprootable.ForceCancelUproot();
                    return null;
                }
                default:
                    return "action must be request, cancel_request, remove_occupant, or cancel_remove";
            }
        }

        private static string ApplyStorageTileSelection(StorageTile.Instance tile, JObject args)
        {
            if (ToolUtil.GetBool(args, "clear", false))
            {
                tile.SetTargetItem(StorageTile.INVALID_TAG);
                return null;
            }

            string tagName = args["itemTag"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName))
                return "itemTag is required unless clear=true";
            var tag = TagManager.Create(tagName.Trim());
            if (!StorageTileOptions(tile).Any(item => item.Tag == tag))
                return "itemTag is not a valid option for this StorageTile";
            tile.SetTargetItem(tag);
            return null;
        }

        private static JObject MergeReceptacleDefaults(JObject item, JObject defaults)
        {
            return MergeBatchDefaults(item, defaults, CopyReceptacleAliases, IsReceptacleAlias);
        }

        private static JObject MergeStorageTileDefaults(JObject item, JObject defaults)
        {
            return MergeBatchDefaults(item, defaults, CopyStorageTileAliases, IsStorageTileAlias);
        }

        private static JObject MergeBatchDefaults(JObject item, JObject defaults, Action<JObject, JObject, bool> copyAliases, Func<string, bool> isAlias)
        {
            var result = new JObject();
            copyAliases(defaults, result, false);
            CopyNonAliases(defaults, result, false, isAlias);
            copyAliases(item, result, true);
            CopyNonAliases(item, result, true, isAlias);
            return result;
        }

        private static void CopyReceptacleAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "action", "a", overwrite);
            CopyAlias(source, target, "entityTag", "tag", overwrite);
            CopyAlias(source, target, "worldId", "w", overwrite);
        }

        private static void CopyStorageTileAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "itemTag", "i", overwrite);
            CopyAlias(source, target, "clear", "c", overwrite);
            CopyAlias(source, target, "worldId", "w", overwrite);
        }

        private static void CopyAlias(JObject source, JObject target, string longKey, string shortKey, bool overwrite)
        {
            var token = source[longKey] ?? source[shortKey];
            if (token != null && (overwrite || target[longKey] == null))
                target[longKey] = token.DeepClone();
        }

        private static void CopyNonAliases(JObject source, JObject target, bool overwrite, Func<string, bool> isAlias)
        {
            if (source == null)
                return;

            foreach (var property in source.Properties())
            {
                if (isAlias(property.Name))
                    continue;
                if (overwrite || target[property.Name] == null)
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        private static bool IsReceptacleAlias(string name)
        {
            return string.Equals(name, "action", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "entityTag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "tag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "worldId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStorageTileAlias(string name)
        {
            return string.Equals(name, "itemTag", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "i", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "clear", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "c", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "worldId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<SingleEntityReceptacle> AllReceptacles()
        {
            return Components.BuildingCompletes.Items
                .Select(item => item?.GetComponent<SingleEntityReceptacle>())
                .Where(item => item != null && IsGenericReceptacle(item));
        }

        private static IEnumerable<StorageTile.Instance> AllStorageTiles()
        {
            return Components.BuildingCompletes.Items
                .Select(item => item?.gameObject?.GetSMI<StorageTile.Instance>())
                .Where(item => item != null && item.gameObject.GetComponent<TreeFilterable>() != null);
        }

        private static bool IsGenericReceptacle(SingleEntityReceptacle receptacle)
        {
            if (receptacle == null || !receptacle.enabled)
                return false;
            var go = receptacle.gameObject;
            return go.GetComponent<PlantablePlot>() == null
                && go.GetComponent<EggIncubator>() == null;
        }

        private static SingleEntityReceptacle FindReceptacle(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var receptacle in AllReceptacles())
            {
                var go = receptacle.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return receptacle;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return receptacle;
            }
            return null;
        }

        private static StorageTile.Instance FindStorageTile(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var tile in AllStorageTiles())
            {
                var go = tile.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return tile;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return tile;
            }
            return null;
        }

        private static Dictionary<string, object> ReceptacleInfo(SingleEntityReceptacle receptacle, bool includeOptions)
        {
            var result = TargetInfo(receptacle.gameObject);
            result["direction"] = receptacle.Direction.ToString();
            result["requestedEntity"] = receptacle.requestedEntityTag.IsValid ? receptacle.requestedEntityTag.Name : null;
            result["requestedAdditionalTag"] = receptacle.requestedEntityAdditionalFilterTag.IsValid ? receptacle.requestedEntityAdditionalFilterTag.Name : null;
            result["hasActiveRequest"] = receptacle.GetActiveRequest != null;
            result["activeRequestTags"] = receptacle.GetActiveRequest == null ? new List<string>() : receptacle.GetActiveRequest.tags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            result["acceptedCategoryTags"] = receptacle.possibleDepositObjectTags.Select(tag => tag.Name).OrderBy(name => name).ToList();
            result["occupant"] = receptacle.Occupant == null ? null : TargetInfo(receptacle.Occupant);
            var uprootable = receptacle.Occupant == null ? null : receptacle.Occupant.GetComponent<Uprootable>();
            result["occupantMarkedForRemoval"] = uprootable != null && uprootable.IsMarkedForUproot;
            if (includeOptions)
                result["options"] = ReceptacleOptions(receptacle).Select(item => item.ToDictionary()).ToList();
            return result;
        }

        private static Dictionary<string, object> StorageTileInfo(StorageTile.Instance tile, bool includeOptions)
        {
            var result = TargetInfo(tile.gameObject);
            result["targetTag"] = tile.TargetTag == StorageTile.INVALID_TAG ? null : tile.TargetTag.Name;
            result["hasContents"] = tile.HasContents;
            result["hasAnyDesiredContents"] = tile.HasAnyDesiredContents;
            result["amountStoredKg"] = Math.Round(ToolUtil.SafeFloat(tile.AmountStored), 3);
            result["amountOfDesiredContentStoredKg"] = Math.Round(ToolUtil.SafeFloat(tile.AmountOfDesiredContentStored), 3);
            result["userMaxCapacityKg"] = Math.Round(ToolUtil.SafeFloat(tile.UserMaxCapacity), 3);
            result["isPendingChange"] = tile.IsPendingChange;
            var storage = tile.gameObject.GetComponent<Storage>();
            result["filterCategoryTags"] = storage == null ? new List<string>() : storage.storageFilters.Select(tag => tag.Name).OrderBy(name => name).ToList();
            if (includeOptions)
                result["options"] = StorageTileOptions(tile).Select(item => item.ToDictionary()).ToList();
            return result;
        }

        private static List<EntityOption> ReceptacleOptions(SingleEntityReceptacle receptacle)
        {
            var options = new Dictionary<Tag, EntityOption>();
            foreach (var category in receptacle.possibleDepositObjectTags)
            {
                foreach (var prefab in Assets.GetPrefabsWithTag(category))
                {
                    if (prefab == null || options.ContainsKey(prefab.PrefabID()) || !receptacle.IsValidEntity(prefab))
                        continue;
                    options[prefab.PrefabID()] = EntityOption.FromPrefab(prefab, category, AvailableAmount(receptacle.gameObject, prefab.PrefabID()));
                }
            }
            return options.Values.OrderBy(item => item.Name).ToList();
        }

        private static List<EntityOption> StorageTileOptions(StorageTile.Instance tile)
        {
            var storage = tile.gameObject.GetComponent<Storage>();
            if (storage == null)
                return new List<EntityOption>();

            var tags = new HashSet<Tag>();
            foreach (var filter in storage.storageFilters)
            {
                var discovered = DiscoveredResources.Instance?.GetDiscoveredResourcesFromTag(filter);
                if (discovered == null || discovered.Count == 0)
                    continue;
                foreach (var tag in discovered)
                    tags.Add(tag);
            }

            return tags
                .Select(tag => new EntityOption
                {
                    Tag = tag,
                    Name = tag.ProperNameStripLink(),
                    CategoryTag = null,
                    AvailableAmount = AvailableAmount(tile.gameObject, tag)
                })
                .OrderBy(item => item.Name)
                .ToList();
        }

        private static bool IsValidReceptacleOption(SingleEntityReceptacle receptacle, GameObject prefab)
        {
            if (prefab == null || !receptacle.IsValidEntity(prefab))
                return false;
            var kpid = prefab.GetComponent<KPrefabID>();
            return receptacle.possibleDepositObjectTags.Any(tag => kpid != null && kpid.HasTag(tag));
        }

        private static Tag AdditionalTagForPrefab(GameObject prefab)
        {
            var mutant = prefab.GetComponent<MutantPlant>();
            return mutant == null ? Tag.Invalid : mutant.SubSpeciesID;
        }

        private static float AvailableAmount(GameObject target, Tag tag)
        {
            var world = target.GetMyWorld();
            if (world == null || !tag.IsValid)
                return 0f;
            return ToolUtil.SafeFloat(world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            if (rect == null)
                return true;
            int cell = Grid.PosToCell(go);
            return Grid.IsValidCell(cell)
                && ToolUtil.CellMatchesWorld(cell, worldId)
                && Grid.CellColumn(cell) >= rect["x1"]
                && Grid.CellColumn(cell) <= rect["x2"]
                && Grid.CellRow(cell) >= rect["y1"]
                && Grid.CellRow(cell) <= rect["y2"];
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄；与 x1/y1/x2/y2 二选一", Required = false };
            parameters["x1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 X", Required = false };
            parameters["y1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 Y", Required = false };
            parameters["x2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 X", Required = false };
            parameters["y2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 Y", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；省略时不限世界", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["id"] = new McpToolParameter { Type = "integer", Description = "目标 KPrefabID.InstanceID；推荐", Required = false };
            parameters["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；未传 id 时使用", Required = false };
            parameters["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；未传 id 时使用", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时建议提供", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> ReceptacleControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list、request、cancel_request、remove_occupant、cancel_remove 或 batch", Required = false, EnumValues = new List<string> { "list", "request", "cancel_request", "remove_occupant", "cancel_remove", "batch" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、请求对象或当前 occupant 筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可请求实体选项，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["entityTag"] = new McpToolParameter { Type = "string", Description = "action=request 时请求的实体 prefab/tag", Required = false },
                ["additionalTag"] = new McpToolParameter { Type = "string", Description = "可选附加过滤 tag，例如突变植物 SubSpeciesID", Required = false },
                ["replaceExistingRequest"] = new McpToolParameter { Type = "boolean", Description = "action=request 时若已有请求，是否先取消旧请求，默认 true", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时数组；每项支持 id 或 x/y/worldId，并提供 action、entityTag/additionalTag；短字段 a/tag/w", Required = false },
                ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数；支持 action/a、entityTag/tag、worldId/w", Required = false },
                ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作必须为 true，确认创建/取消/移除实体请求", Required = false }
            }));
        }

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }

        private class EntityOption
        {
            public Tag Tag { get; set; }
            public string Name { get; set; }
            public string CategoryTag { get; set; }
            public float AvailableAmount { get; set; }

            public static EntityOption FromPrefab(GameObject prefab, Tag category, float availableAmount)
            {
                return new EntityOption
                {
                    Tag = prefab.PrefabID(),
                    Name = ToolUtil.CleanName(prefab.GetProperName()),
                    CategoryTag = category.Name,
                    AvailableAmount = availableAmount
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["tag"] = Tag.Name,
                    ["name"] = Name,
                    ["categoryTag"] = CategoryTag,
                    ["availableAmount"] = Math.Round(ToolUtil.SafeFloat(AvailableAmount), 3)
                };
            }
        }
    }
}
