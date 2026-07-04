using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        public static McpTool ControlFacilitySideScreen()
        {
            return new McpTool
            {
                Name = "facility_sidescreen_control",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "facility_control", "story_facility_sidescreen_control" },
                Tags = new List<string> { "building", "story", "side-screen", "facility" },
                Description = "设施侧屏组合工具。kind=dispenser/suit_locker/lore_bearer/telepad/artifact；action=list 或对应操作",
                Parameters = FacilitySideScreenControlParams(),
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    bool list = string.IsNullOrWhiteSpace(action) || action == "list" || action == "status";

                    switch (kind)
                    {
                        case "dispenser":
                        case "dispensers":
                            return list ? ListDispensers().Handler(args) : ControlDispenser().Handler(args);
                        case "suit_locker":
                        case "suit_lockers":
                        case "locker":
                            return list ? ListSuitLockers().Handler(args) : ControlSuitLocker().Handler(args);
                        case "lore":
                        case "lore_bearer":
                        case "lore_bearers":
                            return list ? ListLoreBearers().Handler(args) : PressLoreBearer().Handler(args);
                        case "telepad":
                        case "telepads":
                        case "printing_pod":
                            return list ? ListTelepads().Handler(args) : ControlTelepad().Handler(args);
                        case "artifact":
                        case "artifacts":
                            return list ? ListArtifacts().Handler(args) : OpenArtifactReveal().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be dispenser, suit_locker, lore_bearer, telepad, or artifact");
                    }
                }
            };
        }




        private static CallToolResult ListBuildingTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            var items = BuildingTargets(args, predicate, selector).ToList();
            int worldId = HasRectInput(args) || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static IEnumerable<Dictionary<string, object>> BuildingTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector)
        {
            if (Game.Instance == null)
                return Enumerable.Empty<Dictionary<string, object>>();
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            return Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit);
        }

        private static CallToolResult ListObjectTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = AllCandidateObjects()
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

        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate)
        {
            return FindTarget(args, Components.BuildingCompletes.Items.Select(building => building?.gameObject), predicate);
        }

        private static GameObject FindObjectTarget(JObject args, Func<GameObject, bool> predicate)
        {
            return FindTarget(args, AllCandidateObjects(), predicate);
        }

        private static GameObject FindTarget(JObject args, IEnumerable<GameObject> candidates, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var go in candidates)
            {
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

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标对象格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标对象格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> FacilitySideScreenControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["kind"] = new McpToolParameter { Type = "string", Description = "dispenser、suit_locker、lore_bearer、telepad 或 artifact", Required = true },
                ["action"] = new McpToolParameter { Type = "string", Description = "list/status 或对应操作：select_item/order/cancel、request_suit/no_suit/drop_suit、press、list_rewards/claim/open_immigrants/open_colony_summary/open_skills/open_research、open", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按名称、prefabId、状态或物品筛选；artifact 也可筛选 artifact id", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 返回上限", Required = false },
                ["itemId"] = new McpToolParameter { Type = "string", Description = "kind=dispenser action=select_item 或 kind=telepad action=claim 时的目标 Tag/prefab id", Required = false },
                ["rewardIndex"] = new McpToolParameter { Type = "integer", Description = "kind=telepad action=claim 时领取 rewards[index]，默认 0", Required = false },
                ["itemIndex"] = new McpToolParameter { Type = "integer", Description = "kind=dispenser action=select_item 时的可分发物品序号", Required = false },
                ["artifactId"] = new McpToolParameter { Type = "string", Description = "kind=artifact action=open 时的已分析 artifact prefab id", Required = false },
                ["interactableOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=lore_bearer action=list 时只返回可交互对象", Required = false },
                ["includeVictory"] = new McpToolParameter { Type = "boolean", Description = "kind=telepad action=list 时是否包含胜利条件 checklist，默认 true", Required = false },
                ["includeStations"] = new McpToolParameter { Type = "boolean", Description = "kind=artifact action=list 时是否包含分析站状态，默认 true", Required = false },
                ["includeWorldArtifacts"] = new McpToolParameter { Type = "boolean", Description = "kind=artifact action=list 时是否包含场上 artifact，默认 true", Required = false },
                ["force"] = new McpToolParameter { Type = "boolean", Description = "kind=lore_bearer action=press 时跳过 interactable 检查", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行写入/打开 UI/弹窗动作时按旧工具要求传 true", Required = false }
            }));
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

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
