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
    public static class SideScreenButtonTools
    {
        public static McpTool ListButtons()
        {
            return new McpTool
            {
                Name = "side_buttons_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "button_menu_controls_list", "sidescreen_buttons_list" },
                Tags = new List<string> { "controls", "side-screen", "button", "activatable", "studyable", "generic" },
                Description = "列出实现 ISidescreenButtonControl 的通用侧屏按钮控件，例如研究、挖掘 POI、激活设施、查看对象等",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、按钮文本或 tooltip 筛选", Required = false },
                    ["interactableOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回当前可点击按钮，默认 false", Required = false },
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
                    bool interactableOnly = ToolUtil.GetBool(args, "interactableOnly", false);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var results = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(ButtonTargetInfo)
                        .Where(info => ((List<Dictionary<string, object>>)info["buttons"]).Count > 0)
                        .Where(info => !interactableOnly || ((List<Dictionary<string, object>>)info["buttons"]).Any(button => (bool)button["interactable"]))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["interactableOnly"] = interactableOnly,
                        ["targets"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PressButton()
        {
            return new McpTool
            {
                Name = "side_button_press",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "button_menu_press", "sidescreen_button_press" },
                Tags = new List<string> { "controls", "side-screen", "button", "activatable", "studyable", "generic" },
                Description = "按下目标对象的 ISidescreenButtonControl 侧屏按钮。该通用按钮可能触发研究、激活、开门、挖掘等操作，需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["buttonIndex"] = new McpToolParameter { Type = "integer", Description = "按钮索引；先用 side_buttons_list 查询，默认 0", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认触发通用侧屏按钮", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过 SidescreenEnabled/Interactable 检查，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for generic side-screen button presses");

                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target with side-screen buttons not found");

                    var buttons = GetButtons(go);
                    int index = Math.Max(0, ToolUtil.GetInt(args, "buttonIndex") ?? 0);
                    if (index >= buttons.Count)
                        return CallToolResult.Error("buttonIndex is outside available button range");

                    var button = buttons[index];
                    bool force = ToolUtil.GetBool(args, "force", false);
                    if (!force && !button.SidescreenEnabled())
                        return CallToolResult.Error("Button side screen is not currently enabled");
                    if (!force && !button.SidescreenButtonInteractable())
                        return CallToolResult.Error("Button is not currently interactable");

                    var before = ButtonTargetInfo(go);
                    button.OnSidescreenButtonPressed();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["pressed"] = ButtonInfo(button, index),
                        ["before"] = before,
                        ["after"] = ButtonTargetInfo(go)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ButtonTargetInfo(GameObject go)
        {
            var result = TargetInfo(go);
            result["buttons"] = GetButtons(go).Select(ButtonInfo).ToList();
            return result;
        }

        private static Dictionary<string, object> ButtonInfo(ISidescreenButtonControl button, int index)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["text"] = button.SidescreenButtonText,
                ["tooltip"] = button.SidescreenButtonTooltip,
                ["enabled"] = button.SidescreenEnabled(),
                ["interactable"] = button.SidescreenButtonInteractable(),
                ["horizontalGroupId"] = button.HorizontalGroupID(),
                ["sortOrder"] = button.ButtonSideScreenSortOrder(),
                ["controlType"] = button.GetType().FullName
            };
        }

        private static List<ISidescreenButtonControl> GetButtons(GameObject go)
        {
            if (go == null)
                return new List<ISidescreenButtonControl>();
            var buttons = go.GetAllSMI<ISidescreenButtonControl>();
            buttons.AddRange(go.GetComponents<ISidescreenButtonControl>());
            return buttons
                .Where(button => button != null && button.SidescreenEnabled())
                .ToList();
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
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var buttons = GetButtons(go);
                if (buttons.Count == 0)
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
