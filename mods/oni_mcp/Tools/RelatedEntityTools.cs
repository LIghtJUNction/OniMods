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
    public static class RelatedEntityTools
    {
        public static McpTool ListRelatedEntities()
        {
            return new McpTool
            {
                Name = "related_entities_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "related_entities_side_screen_list", "side_related_entities_list" },
                Tags = new List<string> { "controls", "side-screen", "related-entities", "navigation", "selection" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface kind=related action=list。列出实现 IRelatedEntities 的侧屏关联对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按目标对象、prefabId、关联对象名称、状态或类型筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回对象数量，默认 100，最大 500", Required = false }
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

                    var targets = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(RelatedTargetInfo)
                        .Where(info => ((List<Dictionary<string, object>>)info["related"]).Count > 0)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = targets.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["targets"] = targets
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FocusRelatedEntity()
        {
            return new McpTool
            {
                Name = "related_entity_focus",
                Group = "controls",
                Mode = "write",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "related_entities_side_screen_focus", "side_related_entity_select" },
                Tags = new List<string> { "controls", "side-screen", "related-entities", "navigation", "selection", "camera" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface kind=related action=focus。选择并聚焦 RelatedEntitiesSideScreen 中的一个关联对象",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["relatedIndex"] = new McpToolParameter { Type = "integer", Description = "关联对象序号；先用 related_entities_list 查询，默认 0", Required = false },
                    ["relatedId"] = new McpToolParameter { Type = "integer", Description = "关联对象 InstanceID；优先于 relatedIndex", Required = false },
                    ["relatedQuery"] = new McpToolParameter { Type = "string", Description = "按关联对象名称、prefabId 或状态匹配；未提供 relatedId 时可用", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null || SelectTool.Instance == null)
                        return CallToolResult.Error("Game selection tools are not initialized");

                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target with related entities not found");

                    var related = GetRelated(go);
                    var entity = FindRelated(args, related);
                    if (entity == null)
                        return CallToolResult.Error("relatedIndex, relatedId, or relatedQuery must match an available related entity");

                    var before = RelatedTargetInfo(go);
                    SelectTool.Instance.SelectAndFocus(entity.transform.position, entity);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["focused"] = SelectableInfo(entity, related.IndexOf(entity)),
                        ["before"] = before,
                        ["after"] = RelatedTargetInfo(go)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> RelatedTargetInfo(GameObject go)
        {
            var result = TargetInfo(go);
            result["related"] = GetRelated(go)
                .Select(SelectableInfo)
                .ToList();
            return result;
        }

        private static Dictionary<string, object> SelectableInfo(KSelectable selectable, int index)
        {
            var go = selectable?.gameObject;
            int cell = go == null ? Grid.InvalidCell : Grid.PosToCell(go);
            var building = go?.GetComponent<Building>();
            var kpid = go?.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["id"] = kpid?.InstanceID ?? go?.GetInstanceID() ?? -1,
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go?.name,
                ["name"] = go == null ? null : ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1,
                ["selected"] = SelectTool.Instance != null && SelectTool.Instance.selected == selectable,
                ["mainStatus"] = MainStatus(selectable),
                ["selectableType"] = selectable?.GetType().FullName
            };
        }

        private static List<KSelectable> GetRelated(GameObject go)
        {
            var related = go?.GetComponent<IRelatedEntities>();
            if (related == null)
                return new List<KSelectable>();

            try
            {
                return (related.GetRelatedEntities() ?? new List<KSelectable>())
                    .Where(item => item != null && item.gameObject != null)
                    .ToList();
            }
            catch
            {
                return new List<KSelectable>();
            }
        }

        private static KSelectable FindRelated(JObject args, List<KSelectable> related)
        {
            int? relatedId = ToolUtil.GetInt(args, "relatedId");
            if (relatedId.HasValue)
            {
                foreach (var selectable in related)
                {
                    var kpid = selectable.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == relatedId.Value)
                        return selectable;
                }
            }

            string query = args["relatedQuery"]?.ToString();
            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var selectable in related)
                {
                    if (JsonConvert.SerializeObject(SelectableInfo(selectable, related.IndexOf(selectable)))
                        .IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        return selectable;
                }
            }

            int index = Math.Max(0, ToolUtil.GetInt(args, "relatedIndex") ?? 0);
            return index < related.Count ? related[index] : null;
        }

        private static string MainStatus(KSelectable selectable)
        {
            if (selectable == null || Db.Get() == null)
                return null;
            try
            {
                var statusItem = selectable.GetStatusItem(Db.Get().StatusItemCategories.Main);
                return statusItem.data == null ? null : statusItem.item.GetName(statusItem.data);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<GameObject> AllCandidateObjects()
        {
            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null || kpid.gameObject == null)
                    continue;
                int id = kpid.gameObject.GetInstanceID();
                if (seen.Add(id))
                    yield return kpid.gameObject;
            }
        }

        private static GameObject FindTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var go in AllCandidateObjects())
            {
                if (go.GetComponent<IRelatedEntities>() == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || go.GetComponent<IRelatedEntities>() == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
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
