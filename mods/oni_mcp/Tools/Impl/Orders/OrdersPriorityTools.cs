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
        public static McpTool ListPriorities()
        {
            return new McpTool
            {
                Name = "priorities_list",
                Hidden = true,
                Group = "orders",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "orders_priorities_list" },
                Description = "兼容入口：请优先使用 orders_control domain=priority action=list。列出可设置优先级的对象，可按区域、世界和名称筛选",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = GetWorldFilter(args, hasRect);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var items = new List<Dictionary<string, object>>();
                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        items.Add(PriorityTargetToDictionary(prioritizable));
                        if (items.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["rect"] = rect,
                        ["priorities"] = items
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingPriority()
        {
            return new McpTool
            {
                Name = "buildings_set_priority",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Description = "兼容入口：请优先使用 orders_control domain=priority action=set_building。设置建筑或可优先级对象的差事优先级",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var prioritizable = go.GetComponent<Prioritizable>();
                    if (prioritizable == null)
                        return CallToolResult.Error("Target is not prioritizable");

                    int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
                    bool top = ToolUtil.GetBool(args, "topPriority", false);
                    var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
                    prioritizable.SetMasterPriority(setting);
                    return CallToolResult.Text($"Set priority for {go.GetProperName()} to {(top ? "topPriority" : priority.ToString())}");
                }
            };
        }

        public static McpTool SetPriorityArea()
        {
            return new McpTool
            {
                Name = "priorities_set_area",
                Hidden = true,
                Group = "orders",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "orders_set_priority_area", "set_priority_area" },
                Description = "兼容入口：请优先使用 orders_control domain=priority action=set_area。批量设置矩形区域内可优先级对象的差事优先级",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9", Required = true },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多修改数量，默认 200，最大 1000", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing priorities in more than 100 cells");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool includeInactive = ToolUtil.GetBool(args, "includeInactive", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 200, 1000));
                    var setting = ParsePrioritySetting(args);
                    var changed = new List<Dictionary<string, object>>();
                    int matched = 0;

                    foreach (var prioritizable in Components.Prioritizables.Items)
                    {
                        if (!MatchesPriorityTarget(prioritizable, rect, worldId, query, includeInactive))
                            continue;

                        matched++;
                        if (changed.Count >= limit)
                            continue;

                        prioritizable.SetMasterPriority(setting);
                        changed.Add(PriorityTargetToDictionary(prioritizable));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["matched"] = matched,
                        ["changed"] = changed.Count,
                        ["skippedByLimit"] = Math.Max(0, matched - changed.Count),
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["priorityClass"] = setting.priority_class.ToString(),
                        ["priority"] = setting.priority_value,
                        ["targets"] = changed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlPriority()
        {
            return new McpTool
            {
                Name = "priority_control",
                Group = "orders",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "orders_priority_control", "prioritize_control" },
                Tags = new List<string> { "orders", "priority", "prioritize", "buildings", "area" },
                Description = "优先级聚合工具：action=list/set_building/set_area；读取可设置优先级对象，或设置单体/区域优先级。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、set_building 或 set_area", Required = true, EnumValues = new List<string> { "list", "set_building", "set_area" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set_building 时的目标格子 Y", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "action=set_building/set_area 时的优先级 1-9", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "action=set_building/set_area 时是否设为红色最高优先级，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list/set_area 时按名称或 prefabId 关键词筛选", Required = false },
                    ["includeInactive"] = new McpToolParameter { Type = "boolean", Description = "action=list/set_area 时是否包含当前不可设置优先级的对象，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list/set_area 时最多返回或修改数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set_area 且区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListPriorities().Handler(args);
                    if (action == "set_building" || action == "set")
                        return SetBuildingPriority().Handler(args);
                    if (action == "set_area" || action == "area")
                        return SetPriorityArea().Handler(args);
                    return CallToolResult.Error("action must be list, set_building, or set_area");
                }
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
            ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
            ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；省略时可用 query/target/search 搜索定位", Required = false },
            ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；省略时可用 query/target/search 搜索定位", Required = false },
            ["query"] = new McpToolParameter { Type = "string", Description = "按对象名称、prefabId、元素或复制人搜索目标格", Required = false },
            ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
            ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
            ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
            ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
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

        private static GameObject FindTarget(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            // Keep original casing for MatchesQuery / CleanName (Chinese display names).
            string queryRaw = args["query"]?.ToString()?.Trim();
            string query = string.IsNullOrEmpty(queryRaw) ? null : queryRaw.ToLowerInvariant();
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            if (!cell.HasValue)
            {
                int searchX;
                int searchY;
                string searchError;
                if (ToolUtil.TryResolveSearchCell(args, out searchX, out searchY, out searchError))
                    cell = Grid.XYToCell(searchX, searchY);
            }
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var prioritizable in Components.Prioritizables.Items)
            {
                var go = prioritizable?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                {
                    if (!string.IsNullOrEmpty(query) && !go.name.ToLowerInvariant().Contains(query) && (kpid == null || !kpid.PrefabTag.Name.ToLowerInvariant().Contains(query)) && !MatchesQuery(go, queryRaw))
                        continue;
                    return go;
                }
            }

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null) continue;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                {
                    if (!string.IsNullOrEmpty(query) && !go.name.ToLowerInvariant().Contains(query) && (kpid == null || !kpid.PrefabTag.Name.ToLowerInvariant().Contains(query)) && !MatchesQuery(go, queryRaw))
                        continue;
                    return go;
                }
            }

            // query-only: match localized CleanName / prefabId the same way list does.
            // Without this, Chinese names like "研究站" fail while English prefab "ResearchCenter" may still resolve via TryResolveSearchCell.
            if (!id.HasValue && !cell.HasValue && !string.IsNullOrEmpty(queryRaw))
            {
                foreach (var prioritizable in Components.Prioritizables.Items)
                {
                    var go = prioritizable?.gameObject;
                    if (go == null) continue;
                    if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                    if (MatchesQuery(go, queryRaw))
                        return go;
                }

                foreach (var building in Components.BuildingCompletes.Items)
                {
                    var go = building?.gameObject;
                    if (go == null) continue;
                    if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) continue;
                    if (MatchesQuery(go, queryRaw))
                        return go;
                }
            }

            return null;
        }

        private static PrioritySetting ParsePrioritySetting(Newtonsoft.Json.Linq.JObject args)
        {
            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            return new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
        }

        private static int GetWorldFilter(Newtonsoft.Json.Linq.JObject args, bool hasRect)
        {
            if (ToolUtil.GetInt(args, "worldId").HasValue)
                return ToolUtil.GetInt(args, "worldId").Value;
            if (hasRect)
                return ToolUtil.ResolveWorldId(args);
            return -1;
        }

        private static bool MatchesPriorityTarget(Prioritizable prioritizable, Dictionary<string, int> rect, int worldId, string query, bool includeInactive)
        {
            var go = prioritizable?.gameObject;
            if (go == null) return false;
            if (!includeInactive && !prioritizable.IsPrioritizable()) return false;
            if (!ToolUtil.GameObjectMatchesWorld(go, worldId)) return false;

            int cell = Grid.PosToCell(go);
            if (rect != null && !CellInRect(cell, rect, worldId)) return false;
            if (!MatchesQuery(go, query)) return false;
            return true;
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            string prefabId = kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> PriorityTargetToDictionary(Prioritizable prioritizable)
        {
            var go = prioritizable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            var setting = prioritizable.GetMasterPriority();
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : Mathf.RoundToInt(go.transform.GetPosition().x);
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : Mathf.RoundToInt(go.transform.GetPosition().y);

            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["position"] = new { x, y },
                ["worldId"] = GetTargetWorldId(go, cell),
                ["isPrioritizable"] = prioritizable.IsPrioritizable(),
                ["priorityClass"] = setting.priority_class.ToString(),
                ["priority"] = setting.priority_value,
                ["topPriority"] = setting.priority_class == PriorityScreen.PriorityClass.topPriority
            };
        }
}
}
