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
    public static class BuildPlanningTools
    {
        public static McpTool SearchBuildables()
        {
            return new McpTool
            {
                Name = "buildings_search_defs",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Description = "搜索可建造建筑定义，返回 prefabId、尺寸、材料类别、可用外观和是否解锁",
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
                        .Where(def => def != null && (includeUnavailable || def.IsAvailable()))
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

        public static McpTool ListBuildMaterials()
        {
            return new McpTool
            {
                Name = "buildings_materials",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "building_materials", "build_materials" },
                Tags = new List<string> { "buildings", "materials", "inventory", "available", "建造", "材料" },
                Description = "列出指定建筑当前可用建造材料，按库存量排序；用于避免使用 SandStone 等当前星球没有的材料。material=auto 会使用同一选择逻辑。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 Outhouse、Tile、Wire", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界；会包含关联世界库存", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "是否同时返回已发现但当前库存为 0 的候选，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 200", Required = false }
                },
                Handler = args =>
                {
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
                        ["defaultMaterials"] = def.DefaultElements().Select(tag => tag.Name).ToList(),
                        ["autoMaterial"] = materials.Count > 0 ? materials[0]["tag"] : null,
                        ["returned"] = materials.Count,
                        ["materials"] = materials
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PlanBuilding()
        {
            return new McpTool
            {
                Name = "buildings_plan",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Description = "在指定格子放置建筑蓝图，支持材料和建筑外观选择。对砖块、电线等可拖拽建筑可用 buildings_plan_rect",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 Outhouse、Bed、OxygenDiffuser、Tile、Wire", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "可选材料 Tag；默认/auto 自动选择当前可用且库存最多的合法材料。可先用 buildings_materials 或 buildings_search_defs 查看候选。", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "可选建筑外观 ID；使用 buildings_search_defs 查看 facades，default/DEFAULT_FACADE 表示默认外观", Required = false },
                    ["orientation"] = new McpToolParameter { Type = "string", Description = "方向：Neutral、R90、R180、R270、FlipH，默认 Neutral", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9，默认 5", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只验证位置/材料/支撑，不实际放置蓝图，默认 false", Required = false },
                    ["validateOnly"] = new McpToolParameter { Type = "boolean", Description = "dryRun 的别名", Required = false },
                    ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "允许 OnFloor 建筑缺少地板/支撑面，默认 false；通常不要开启", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认放置，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string prefabId = args["prefabId"]?.ToString();
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (string.IsNullOrWhiteSpace(prefabId) || !x.HasValue || !y.HasValue)
                        return CallToolResult.Error("prefabId, x and y are required");

                    var result = TryPlanOne(prefabId, x.Value, y.Value, args);
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PlanBuildingRect()
        {
            return new McpTool
            {
                Name = "buildings_plan_rect",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Description = "在矩形区域/直线区域批量放置同一种建筑蓝图，适合砖块、电线、管道",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 Tile、Wire、Ladder", Required = true },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2，并在整个区域批量放置", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "起点 X；使用 areaId 时可省略", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "起点 Y；使用 areaId 时可省略", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "终点 X；使用 areaId 时可省略", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "终点 Y；使用 areaId 时可省略", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "可选材料 Tag；默认/auto 自动选择当前可用且库存最多的合法材料", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "可选建筑外观 ID；default/DEFAULT_FACADE 表示默认外观", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9，默认 5", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只验证位置/材料/支撑，不实际放置蓝图，默认 false", Required = false },
                    ["validateOnly"] = new McpToolParameter { Type = "boolean", Description = "dryRun 的别名", Required = false },
                    ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "允许 OnFloor 建筑缺少地板/支撑面，默认 false；通常不要开启", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认放置，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                        && (args["x1"] == null || args["y1"] == null || args["x2"] == null || args["y2"] == null))
                    {
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");
                    }

                    var rect = ToolUtil.GetRect(args);
                    int cells = (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
                    if (cells > 200)
                        return CallToolResult.Error("Refusing to place more than 200 cells");

                    var results = new List<Dictionary<string, object>>();
                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                            results.Add(TryPlanOne(prefabId, x, y, args));
                    }

                    int valid = results.Count(item => item.ContainsKey("valid") && (bool)item["valid"]);
                    int planned = results.Count(item => item.ContainsKey("planned") && (bool)item["planned"]);
                    int blocked = results.Count - valid;
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["prefabId"] = prefabId,
                        ["dryRun"] = IsDryRun(args),
                        ["rect"] = rect,
                        ["valid"] = valid,
                        ["planned"] = planned,
                        ["blocked"] = blocked,
                        ["results"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PlanMany()
        {
            return new McpTool
            {
                Name = "buildings_plan_many",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Description = "紧凑批量放置建筑蓝图；items 支持 {p,x,y}、{p,line:[x1,y1,x2,y2]}、{p,path:[[x,y],...] }、{p,r:[x1,y1,x2,y2]}、{p,cells:[[x,y]]}，顶层可给默认 m/f/pri/w/o，默认只返回汇总",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "批量计划项。长字段 prefabId/material/facade/facadeId/priority/worldId/orientation 也可用短字段 p/m/f/fid/pri/w/o；位置支持 x/y、line/l、path/points、r、cells/cs、areaId/a", Required = true },
                    ["routes"] = new McpToolParameter { Type = "array", Description = "可选自动连线/管路项，会展开为 Wire/GasConduit/LiquidConduit/SolidConduit/LogicWire。格式：[{p:\"Wire\",from:{prefabId,x,y,port:\"powerInput\"},to:{x,y},viaY:135}]；短字段 from/to 可用 a/b", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "默认世界 ID；短字段 w", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "默认材料 Tag；短字段 m；默认/auto 自动选择当前可用且库存最多的合法材料", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "默认建筑外观 ID；短字段 f，也兼容 facadeId/fid", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "默认优先级 1-9；短字段 pri", Required = false },
                    ["orientation"] = new McpToolParameter { Type = "string", Description = "默认方向 Neutral/R90/R180/R270/FlipH；短字段 o", Required = false },
                    ["detail"] = new McpToolParameter { Type = "boolean", Description = "是否返回逐格结果，默认 false 以节省 token", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最多展开格数，默认 500，最大 1000", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只验证整批位置/材料/支撑，不实际放置蓝图，默认 false", Required = false },
                    ["validateOnly"] = new McpToolParameter { Type = "boolean", Description = "dryRun 的别名", Required = false },
                    ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "允许 OnFloor 建筑缺少地板/支撑面，默认 false；通常不要开启", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认放置，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var items = args["items"] as JArray ?? args["plans"] as JArray;
                    var routes = args["routes"] as JArray ?? args["connect"] as JArray;
                    if ((items == null || items.Count == 0) && (routes == null || routes.Count == 0))
                        return CallToolResult.Error("items or routes array is required");

                    var expandedItems = new JArray();
                    if (items != null)
                        foreach (var item in items)
                            expandedItems.Add(item.DeepClone());
                    var routeErrors = ExpandRoutes(args, routes, expandedItems);
                    if (routeErrors.Count > 0)
                        return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["error"] = "route expansion failed",
                            ["routeErrors"] = routeErrors
                        }, McpJsonUtil.Settings));

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 500, 1000));
                    int requested = CountRequestedCells(expandedItems);
                    if (requested > maxCells)
                        return CallToolResult.Error($"Refusing to place {requested} cells; maxCells={maxCells}");

                    bool detail = ToolUtil.GetBool(args, "detail", false);
                    bool dryRun = IsDryRun(args);
                    int planned = 0;
                    int valid = 0;
                    int failed = 0;
                    var byPrefab = new Dictionary<string, int>();
                    var errors = new List<Dictionary<string, object>>();
                    var details = new List<Dictionary<string, object>>();
                    var plannedSupportCells = new HashSet<int>();

                    for (int i = 0; i < expandedItems.Count; i++)
                    {
                        var item = expandedItems[i] as JObject;
                        if (item == null)
                        {
                            failed++;
                            errors.Add(BatchError(i, null, -1, -1, "Item must be an object"));
                            continue;
                        }

                        string prefabId = GetString(item, "prefabId", "p");
                        if (string.IsNullOrWhiteSpace(prefabId))
                        {
                            failed++;
                            errors.Add(BatchError(i, null, -1, -1, "prefabId/p is required"));
                            continue;
                        }
                        var planArgs = BuildItemArgs(args, item);
                        if (!HasLocation(item) && string.IsNullOrWhiteSpace(planArgs["areaId"]?.ToString()))
                        {
                            failed++;
                            errors.Add(BatchError(i, prefabId, -1, -1, "x/y, line/l, path/points, r, cells/cs or areaId/a is required"));
                            continue;
                        }

                        string locationError = ValidateItemLocation(item);
                        if (!string.IsNullOrWhiteSpace(locationError))
                        {
                            failed++;
                            errors.Add(BatchError(i, prefabId, -1, -1, locationError));
                            continue;
                        }

                        foreach (var cell in ExpandItemCells(item, planArgs))
                        {
                            var result = TryPlanOne(prefabId, cell.x, cell.y, planArgs, plannedSupportCells);
                            bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                            bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                            if (ok || (dryRun && validPlacement))
                            {
                                valid++;
                                if (ok)
                                    planned++;
                                byPrefab[prefabId] = byPrefab.ContainsKey(prefabId) ? byPrefab[prefabId] + 1 : 1;
                                RegisterSupportBlueprint(prefabId, cell.x, cell.y, plannedSupportCells);
                            }
                            else
                            {
                                failed++;
                                errors.Add(BatchError(
                                    i,
                                    prefabId,
                                    cell.x,
                                    cell.y,
                                    result.ContainsKey("error") ? result["error"]?.ToString() : "Placement failed",
                                    result.ContainsKey("details") ? result["details"] as Dictionary<string, object> : null));
                            }

                            if (detail)
                            {
                                result["index"] = i;
                                details.Add(result);
                            }
                        }
                    }

                    var response = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["requested"] = requested,
                        ["valid"] = valid,
                        ["planned"] = planned,
                        ["failed"] = failed,
                        ["blocked"] = failed,
                        ["byPrefab"] = byPrefab,
                        ["routesExpanded"] = expandedItems.Count - (items?.Count ?? 0),
                        ["errors"] = errors.Take(50).ToList(),
                        ["truncatedErrors"] = Math.Max(0, errors.Count - 50)
                    };
                    if (detail)
                        response["results"] = details;

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> TryPlanOne(string prefabId, int x, int y, JObject args, HashSet<int> plannedSupportCells = null)
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
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            if (!materialResult.Valid)
                return ErrorResult(prefabId, x, y, materialResult.Error, materialResult.ToDictionary());

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
                return ErrorResult(prefabId, x, y, facadeResult.Error);

            var supportResult = ValidateSupport(def, x, y, ToolUtil.GetBool(args, "allowUnsupported", false), plannedSupportCells);
            if (!supportResult.Valid)
                return ErrorResult(prefabId, x, y, supportResult.Error, supportResult.ToDictionary());

            if (IsDryRun(args))
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["valid"] = true,
                    ["dryRun"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = worldId,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId
                };
            }

            var pos = BuildPlacementPosition(cell, def);
            var go = def.TryPlace(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId);
            if (go == null)
                return ErrorResult(prefabId, x, y, "Placement failed", materialResult.ToDictionary());

            SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
            RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
            return new Dictionary<string, object>
            {
                ["planned"] = true,
                ["valid"] = true,
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["support"] = supportResult.ToDictionary(),
                ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["facade"] = facadeResult.ResponseId,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static JObject BuildItemArgs(JObject defaults, JObject item)
        {
            var result = new JObject();
            CopyCompact(defaults, result, "worldId", "w");
            CopyCompact(defaults, result, "material", "m");
            CopyCompact(defaults, result, "facade", "f");
            CopyCompact(defaults, result, "facadeId", "fid");
            CopyCompact(defaults, result, "priority", "pri");
            CopyCompact(defaults, result, "orientation", "o");
            CopyCompact(defaults, result, "dryRun", "dry");
            CopyCompact(defaults, result, "validateOnly", "validate");
            CopyCompact(defaults, result, "allowUnsupported", "unsupported");
            CopyCompact(item, result, "worldId", "w");
            CopyCompact(item, result, "material", "m");
            CopyCompact(item, result, "facade", "f");
            CopyCompact(item, result, "facadeId", "fid");
            CopyCompact(item, result, "priority", "pri");
            CopyCompact(item, result, "orientation", "o");
            CopyCompact(item, result, "dryRun", "dry");
            CopyCompact(item, result, "validateOnly", "validate");
            CopyCompact(item, result, "allowUnsupported", "unsupported");
            CopyCompact(item, result, "areaId", "a");
            return result;
        }

        private static void CopyCompact(JObject source, JObject target, string longKey, string shortKey)
        {
            var token = source[longKey] ?? source[shortKey];
            if (token != null)
                target[longKey] = token.DeepClone();
        }

        private static string GetString(JObject item, string longKey, string shortKey)
        {
            return (item[longKey] ?? item[shortKey])?.ToString();
        }

        private static List<Dictionary<string, object>> ExpandRoutes(JObject defaults, JArray routes, JArray expandedItems)
        {
            var errors = new List<Dictionary<string, object>>();
            if (routes == null)
                return errors;

            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i] as JObject;
                if (route == null)
                {
                    errors.Add(RouteError(i, "route must be an object"));
                    continue;
                }

                string prefabId = GetString(route, "prefabId", "p");
                if (string.IsNullOrWhiteSpace(prefabId))
                    prefabId = argsString(defaults, "routePrefabId", "rp") ?? "Wire";
                if (!IsRoutePrefab(prefabId))
                {
                    errors.Add(RouteError(i, $"unsupported route prefab '{prefabId}'"));
                    continue;
                }

                RouteEndpoint from;
                RouteEndpoint to;
                string error;
                if (!TryResolveRouteEndpoint(route["from"] ?? route["a"], out from, out error))
                {
                    errors.Add(RouteError(i, "from: " + error));
                    continue;
                }
                if (!TryResolveRouteEndpoint(route["to"] ?? route["b"], out to, out error))
                {
                    errors.Add(RouteError(i, "to: " + error));
                    continue;
                }

                var cells = RouteCells(from.X, from.Y, to.X, to.Y, ToolUtil.GetInt(route, "viaX"), ToolUtil.GetInt(route, "viaY")).ToList();
                if (cells.Count == 0)
                {
                    errors.Add(RouteError(i, "route produced no cells"));
                    continue;
                }

                var item = new JObject
                {
                    ["prefabId"] = prefabId,
                    ["cells"] = new JArray(cells.Select(cell => new JArray(cell.x, cell.y))),
                    ["routeIndex"] = i
                };
                CopyCompact(defaults, item, "worldId", "w");
                CopyCompact(defaults, item, "material", "m");
                CopyCompact(defaults, item, "priority", "pri");
                CopyCompact(defaults, item, "dryRun", "dry");
                CopyCompact(defaults, item, "validateOnly", "validate");
                CopyCompact(route, item, "worldId", "w");
                CopyCompact(route, item, "material", "m");
                CopyCompact(route, item, "priority", "pri");
                CopyCompact(route, item, "dryRun", "dry");
                CopyCompact(route, item, "validateOnly", "validate");
                expandedItems.Add(item);
            }

            return errors;
        }

        private static string argsString(JObject args, string longKey, string shortKey)
        {
            return (args[longKey] ?? args[shortKey])?.ToString();
        }

        private static Dictionary<string, object> RouteError(int index, string error)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["error"] = error
            };
        }

        private static bool IsRoutePrefab(string prefabId)
        {
            return EqualsIgnoreCase(prefabId, "Wire")
                || EqualsIgnoreCase(prefabId, "HighWattageWire")
                || EqualsIgnoreCase(prefabId, "HighWattageWireBridge")
                || EqualsIgnoreCase(prefabId, "LogicWire")
                || EqualsIgnoreCase(prefabId, "GasConduit")
                || EqualsIgnoreCase(prefabId, "LiquidConduit")
                || EqualsIgnoreCase(prefabId, "SolidConduit");
        }

        private static bool TryResolveRouteEndpoint(JToken token, out RouteEndpoint endpoint, out string error)
        {
            endpoint = default(RouteEndpoint);
            error = null;
            var obj = token as JObject;
            if (obj == null)
            {
                var pair = token as JArray;
                if (pair != null && pair.Count >= 2)
                {
                    endpoint = new RouteEndpoint(TokenInt(pair[0]), TokenInt(pair[1]));
                    return true;
                }
                error = "endpoint must be object or [x,y]";
                return false;
            }

            int? x = ToolUtil.GetInt(obj, "x");
            int? y = ToolUtil.GetInt(obj, "y");
            if (!x.HasValue || !y.HasValue)
            {
                error = "x and y are required";
                return false;
            }

            string prefabId = obj["prefabId"]?.ToString() ?? obj["p"]?.ToString();
            string port = obj["port"]?.ToString() ?? obj["kind"]?.ToString();
            if (!string.IsNullOrWhiteSpace(prefabId) && !string.IsNullOrWhiteSpace(port))
            {
                var def = Assets.GetBuildingDef(prefabId);
                if (def == null)
                {
                    error = "Building def not found for endpoint prefabId=" + prefabId;
                    return false;
                }

                var offset = ResolvePortOffset(def, port);
                x = x.Value + offset.x;
                y = y.Value + offset.y;
            }

            endpoint = new RouteEndpoint(x.Value, y.Value);
            return true;
        }

        private static CellCoord ResolvePortOffset(BuildingDef def, string port)
        {
            string normalized = (port ?? "").Trim().ToLowerInvariant();
            if (normalized == "power" || normalized == "powerinput" || normalized == "input")
                return ToCellCoord(def.PowerInputOffset);
            if (normalized == "poweroutput" || normalized == "output")
                return ToCellCoord(def.PowerOutputOffset);
            if (normalized == "utilityinput" || normalized == "pipeinput" || normalized == "gasinput" || normalized == "liquidinput")
                return new CellCoord(Math.Max(0, def.WidthInCells / 2), 0);
            if (normalized == "utilityoutput" || normalized == "pipeoutput" || normalized == "gasoutput" || normalized == "liquidoutput")
                return new CellCoord(Math.Max(0, def.WidthInCells / 2), 0);

            return new CellCoord(Math.Max(0, def.WidthInCells / 2), 0);
        }

        private static CellCoord ToCellCoord(CellOffset offset)
        {
            return new CellCoord(offset.x, offset.y);
        }

        private static IEnumerable<CellCoord> RouteCells(int x1, int y1, int x2, int y2, int? viaX, int? viaY)
        {
            var seen = new HashSet<string>();

            if (viaX.HasValue)
            {
                foreach (var cell in LineCells(x1, y1, viaX.Value, y1, seen))
                    yield return cell;
                foreach (var cell in LineCells(viaX.Value, y1, viaX.Value, y2, seen))
                    yield return cell;
                foreach (var cell in LineCells(viaX.Value, y2, x2, y2, seen))
                    yield return cell;
                yield break;
            }

            if (viaY.HasValue)
            {
                foreach (var cell in LineCells(x1, y1, x1, viaY.Value, seen))
                    yield return cell;
                foreach (var cell in LineCells(x1, viaY.Value, x2, viaY.Value, seen))
                    yield return cell;
                foreach (var cell in LineCells(x2, viaY.Value, x2, y2, seen))
                    yield return cell;
                yield break;
            }

            foreach (var cell in LineCells(x1, y1, x2, y1, seen))
                yield return cell;
            foreach (var cell in LineCells(x2, y1, x2, y2, seen))
                yield return cell;
        }

        private static IEnumerable<CellCoord> LineCells(int x1, int y1, int x2, int y2, HashSet<string> seen)
        {
            int dx = Math.Sign(x2 - x1);
            int dy = Math.Sign(y2 - y1);
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

        private struct RouteEndpoint
        {
            public readonly int X;
            public readonly int Y;

            public RouteEndpoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private static int CountRequestedCells(JArray items)
        {
            int count = 0;
            foreach (var token in items)
            {
                var item = token as JObject;
                if (item == null)
                {
                    count++;
                    continue;
                }

                var cells = item["cells"] as JArray ?? item["cs"] as JArray;
                if (cells != null)
                {
                    count += cells.Count;
                    continue;
                }

                var line = item["line"] as JArray ?? item["l"] as JArray;
                if (line != null && line.Count >= 4)
                {
                    int x1 = TokenInt(line[0]);
                    int y1 = TokenInt(line[1]);
                    int x2 = TokenInt(line[2]);
                    int y2 = TokenInt(line[3]);
                    count += Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;
                    continue;
                }

                var path = item["path"] as JArray ?? item["points"] as JArray ?? item["pts"] as JArray;
                if (path != null)
                {
                    count += CountPathCells(path);
                    continue;
                }

                var rect = item["r"] as JArray;
                if (rect != null && rect.Count >= 4)
                {
                    int x1 = TokenInt(rect[0]);
                    int y1 = TokenInt(rect[1]);
                    int x2 = TokenInt(rect[2]);
                    int y2 = TokenInt(rect[3]);
                    count += (Math.Abs(x2 - x1) + 1) * (Math.Abs(y2 - y1) + 1);
                    continue;
                }

                if (item["areaId"] != null || item["a"] != null || (item["x1"] != null && item["y1"] != null && item["x2"] != null && item["y2"] != null))
                {
                    var args = new JObject();
                    CopyCompact(item, args, "areaId", "a");
                    CopyCompact(item, args, "x1", "x1");
                    CopyCompact(item, args, "y1", "y1");
                    CopyCompact(item, args, "x2", "x2");
                    CopyCompact(item, args, "y2", "y2");
                    var resolved = ToolUtil.GetRect(args);
                    count += (resolved["x2"] - resolved["x1"] + 1) * (resolved["y2"] - resolved["y1"] + 1);
                    continue;
                }

                count++;
            }
            return count;
        }

        private static bool HasLocation(JObject item)
        {
            return (item["x"] != null && item["y"] != null)
                || item["line"] != null
                || item["l"] != null
                || item["path"] != null
                || item["points"] != null
                || item["pts"] != null
                || item["r"] != null
                || item["cells"] != null
                || item["cs"] != null
                || item["areaId"] != null
                || item["a"] != null
                || (item["x1"] != null && item["y1"] != null && item["x2"] != null && item["y2"] != null);
        }

        private static string ValidateItemLocation(JObject item)
        {
            var line = item["line"] as JArray ?? item["l"] as JArray;
            if (line != null)
            {
                if (line.Count < 4)
                    return "line/l must be [x1,y1,x2,y2]";
                int x1 = TokenInt(line[0]);
                int y1 = TokenInt(line[1]);
                int x2 = TokenInt(line[2]);
                int y2 = TokenInt(line[3]);
                if (x1 != x2 && y1 != y2)
                    return "line/l only supports horizontal or vertical lines; use path/points for L-shaped or polyline routes";
            }

            var path = item["path"] as JArray ?? item["points"] as JArray ?? item["pts"] as JArray;
            if (path != null)
            {
                if (path.Count < 2)
                    return "path/points must contain at least two [x,y] points";
                for (int i = 1; i < path.Count; i++)
                {
                    CellCoord a;
                    CellCoord b;
                    if (!TryReadPoint(path[i - 1], out a) || !TryReadPoint(path[i], out b))
                        return "path/points entries must be [x,y]";
                    if (a.x != b.x && a.y != b.y)
                        return "path/points segments must be horizontal or vertical";
                }
            }

            return null;
        }

        private static IEnumerable<CellCoord> ExpandItemCells(JObject item, JObject planArgs)
        {
            var cells = item["cells"] as JArray ?? item["cs"] as JArray;
            if (cells != null)
            {
                foreach (var token in cells)
                {
                    var pair = token as JArray;
                    if (pair == null || pair.Count < 2)
                        continue;
                    yield return new CellCoord(TokenInt(pair[0]), TokenInt(pair[1]));
                }
                yield break;
            }

            var line = item["line"] as JArray ?? item["l"] as JArray;
            if (line != null && line.Count >= 4)
            {
                foreach (var cell in StraightLineCells(TokenInt(line[0]), TokenInt(line[1]), TokenInt(line[2]), TokenInt(line[3])))
                    yield return cell;
                yield break;
            }

            var path = item["path"] as JArray ?? item["points"] as JArray ?? item["pts"] as JArray;
            if (path != null)
            {
                foreach (var cell in PathCells(path))
                    yield return cell;
                yield break;
            }

            var compactRect = item["r"] as JArray;
            if (compactRect != null && compactRect.Count >= 4)
            {
                foreach (var cell in RectCells(TokenInt(compactRect[0]), TokenInt(compactRect[1]), TokenInt(compactRect[2]), TokenInt(compactRect[3])))
                    yield return cell;
                yield break;
            }

            if (item["areaId"] != null || item["a"] != null || (item["x1"] != null && item["y1"] != null && item["x2"] != null && item["y2"] != null))
            {
                CopyCompact(item, planArgs, "x1", "x1");
                CopyCompact(item, planArgs, "y1", "y1");
                CopyCompact(item, planArgs, "x2", "x2");
                CopyCompact(item, planArgs, "y2", "y2");
                var rect = ToolUtil.GetRect(planArgs);
                foreach (var cell in RectCells(rect["x1"], rect["y1"], rect["x2"], rect["y2"]))
                    yield return cell;
                yield break;
            }

            yield return new CellCoord(TokenInt(item["x"]), TokenInt(item["y"]));
        }

        private static int CountPathCells(JArray path)
        {
            return PathCells(path).Count();
        }

        private static IEnumerable<CellCoord> PathCells(JArray path)
        {
            var seen = new HashSet<string>();
            for (int i = 1; i < path.Count; i++)
            {
                CellCoord a;
                CellCoord b;
                if (!TryReadPoint(path[i - 1], out a) || !TryReadPoint(path[i], out b))
                    yield break;
                foreach (var cell in LineCells(a.x, a.y, b.x, b.y, seen))
                    yield return cell;
            }
        }

        private static IEnumerable<CellCoord> StraightLineCells(int x1, int y1, int x2, int y2)
        {
            var seen = new HashSet<string>();
            foreach (var cell in LineCells(x1, y1, x2, y2, seen))
                yield return cell;
        }

        private static bool TryReadPoint(JToken token, out CellCoord point)
        {
            point = default(CellCoord);
            var pair = token as JArray;
            if (pair == null || pair.Count < 2)
                return false;
            point = new CellCoord(TokenInt(pair[0]), TokenInt(pair[1]));
            return true;
        }

        private static IEnumerable<CellCoord> RectCells(int x1, int y1, int x2, int y2)
        {
            if (x2 < x1) { int t = x1; x1 = x2; x2 = t; }
            if (y2 < y1) { int t = y1; y1 = y2; y2 = t; }
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                    yield return new CellCoord(x, y);
        }

        private static int TokenInt(JToken token)
        {
            int value;
            return token != null && int.TryParse(token.ToString(), out value) ? value : 0;
        }

        private static Dictionary<string, object> BatchError(int index, string prefabId, int x, int y, string error, Dictionary<string, object> details = null)
        {
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["error"] = error
            };
            if (details != null)
                result["details"] = details;
            return result;
        }

        private static bool IsDryRun(JObject args)
        {
            return ToolUtil.GetBool(args, "dryRun", false) || ToolUtil.GetBool(args, "validateOnly", false);
        }

        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def)
        {
            var pos = Grid.CellToPosCBC(cell, def.SceneLayer);
            pos.x += (Math.Max(1, def.WidthInCells) - 1) * 0.5f;
            pos.y += (Math.Max(1, def.HeightInCells) - 1) * 0.5f;
            return pos;
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
                        ["y"] = supportCell.Y
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

                var defaults = def.DefaultElements();
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
            var candidates = CandidateMaterialTags(def, worldId)
                .Where(tag => tag.IsValid)
                .Distinct()
                .Select(tag => new BuildMaterialInfo
                {
                    Tag = tag,
                    Name = tag.ProperNameStripLink(),
                    AvailableKg = AvailableAmount(worldId, tag),
                    ValidForBuilding = true,
                    Categories = MatchingMaterialCategories(def, tag).ToList()
                })
                .Where(item => includeUnavailable || item.AvailableKg > 0f || IsFreeBuildContext())
                .OrderByDescending(item => item.AvailableKg)
                .ThenBy(item => item.Tag.Name)
                .ToList();

            return candidates;
        }

        private static IEnumerable<Tag> CandidateMaterialTags(BuildingDef def, int worldId)
        {
            foreach (var tag in def.DefaultElements())
                yield return tag;

            if (def.MaterialCategory == null)
                yield break;

            foreach (string categoryName in def.MaterialCategory)
            {
                if (string.IsNullOrWhiteSpace(categoryName))
                    continue;

                var category = new Tag(categoryName);
                if (!category.IsValid)
                    continue;

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
                if (string.IsNullOrWhiteSpace(categoryName))
                    continue;
                var category = new Tag(categoryName);
                if (category.IsValid)
                    yield return category;
            }
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
            return new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["width"] = def.WidthInCells,
                ["height"] = def.HeightInCells,
                ["buildLocationRule"] = def.BuildLocationRule.ToString(),
                ["categories"] = BuildingCategories(def),
                ["materialCategories"] = def.MaterialCategory,
                ["defaultMaterials"] = def.DefaultElements().Select(tag => tag.Name).ToList(),
                ["availableMaterials"] = AvailableMaterials(def, ClusterManager.Instance?.activeWorldId ?? -1, includeUnavailable: false).Take(20).Select(item => item.ToDictionary()).ToList(),
                ["autoMaterial"] = AvailableMaterials(def, ClusterManager.Instance?.activeWorldId ?? -1, includeUnavailable: false).FirstOrDefault()?.Tag.Name,
                ["facades"] = BuildingFacades(def),
                ["requiresPower"] = def.RequiresPowerInput,
                ["powerWatts"] = Math.Round(def.EnergyConsumptionWhenActive, 1),
                ["unlocked"] = Db.Get().Techs.IsTechItemComplete(def.PrefabID)
            };
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
            var result = new Dictionary<string, object>
            {
                ["planned"] = false,
                ["valid"] = false,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["error"] = error
            };
            if (details != null)
                result["details"] = details;
            return result;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
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
                    Available = available ?? new List<BuildMaterialInfo>()
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
                    Candidates = candidates ?? new List<BuildMaterialInfo>()
                };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["valid"] = Valid,
                    ["mode"] = Mode,
                    ["requested"] = Requested,
                    ["selected"] = Selected != null ? Selected.ToDictionary() : null,
                    ["elements"] = Elements.Select(tag => tag.Name).ToList(),
                    ["availableMaterials"] = Available.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["candidateMaterials"] = Candidates.Take(20).Select(item => item.ToDictionary()).ToList(),
                    ["suggestion"] = Available.Count > 0 ? "Use material=auto or material=" + Available[0].Tag.Name : "No available material; inspect resources_inventory/buildings_materials",
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
    }
}
