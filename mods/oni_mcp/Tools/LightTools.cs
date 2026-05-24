using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static class LightTools
    {
        public static McpTool ListLights()
        {
            return new McpTool
            {
                Name = "lights_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "light_color_controls_list" },
                Tags = new List<string> { "lights", "color", "user-menu", "buildings" },
                Description = "列出灯光建筑和 LightColorMenu 预设颜色，对应用户菜单中的灯光颜色选择",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId 或颜色名筛选", Required = false },
                    ["configurableOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回带 LightColorMenu 的灯，默认 true", Required = false },
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
                    bool configurableOnly = ToolUtil.GetBool(args, "configurableOnly", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var lights = Components.BuildingCompletes.Items
                        .Select(building => building?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<LightColorMenu>() != null || (!configurableOnly && go.GetComponentsInChildren<Light2D>(true).Length > 0))
                        .Select(LightInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = lights.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["configurableOnly"] = configurableOnly,
                        ["lights"] = lights
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetLightColor()
        {
            return new McpTool
            {
                Name = "lights_color_set",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "light_color_set" },
                Tags = new List<string> { "lights", "color", "user-menu", "buildings" },
                Description = "选择 LightColorMenu 预设颜色，等同于灯光建筑用户菜单中的颜色按钮",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["colorIndex"] = new McpToolParameter { Type = "integer", Description = "颜色预设索引；可先用 lights_list 查询", Required = false },
                    ["colorName"] = new McpToolParameter { Type = "string", Description = "颜色预设名称；colorIndex 为空时使用", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var menu = go.GetComponent<LightColorMenu>();
                    if (menu == null || menu.lightColors == null || menu.lightColors.Length == 0)
                        return CallToolResult.Error("Target does not expose LightColorMenu presets");

                    int? requestedIndex = ToolUtil.GetInt(args, "colorIndex");
                    int index = requestedIndex ?? FindColorIndex(menu, args["colorName"]?.ToString());
                    if (index < 0 || index >= menu.lightColors.Length)
                        return CallToolResult.Error("colorIndex/colorName does not match any preset");

                    int before = GetCurrentColor(menu);
                    var method = OniReflection.GetMethodSafe(typeof(LightColorMenu), "SetColor", false, new[] { typeof(int) });
                    if (method == null)
                        return CallToolResult.Error("LightColorMenu.SetColor not found");
                    method.Invoke(menu, new object[] { index });

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["selected"] = ColorInfo(menu.lightColors[index], index),
                        ["currentIndex"] = GetCurrentColor(menu),
                        ["changed"] = before != GetCurrentColor(menu)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> LightInfo(GameObject go)
        {
            var result = TargetInfo(go);
            var menu = go.GetComponent<LightColorMenu>();
            result["hasColorMenu"] = menu != null;
            result["currentColorIndex"] = menu == null ? -1 : GetCurrentColor(menu);
            result["colorOptions"] = menu == null || menu.lightColors == null
                ? new List<Dictionary<string, object>>()
                : menu.lightColors.Select(ColorInfo).ToList();
            result["lights"] = go.GetComponentsInChildren<Light2D>(true)
                .Select(light => new Dictionary<string, object>
                {
                    ["color"] = ColorToDictionary(light.Color),
                    ["lux"] = light.Lux,
                    ["range"] = ToolUtil.SafeFloat(light.Range),
                    ["enabled"] = light.enabled
                })
                .ToList();
            return result;
        }

        private static Dictionary<string, object> ColorInfo(LightColorMenu.LightColor color, int index)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = color.name,
                ["color"] = ColorToDictionary(color.color)
            };
        }

        private static Dictionary<string, object> ColorToDictionary(Color color)
        {
            return new Dictionary<string, object>
            {
                ["r"] = Math.Round(color.r, 4),
                ["g"] = Math.Round(color.g, 4),
                ["b"] = Math.Round(color.b, 4),
                ["a"] = Math.Round(color.a, 4)
            };
        }

        private static int GetCurrentColor(LightColorMenu menu)
        {
            var field = OniReflection.GetFieldSafe(typeof(LightColorMenu), "currentColor", false);
            if (field == null)
                return -1;
            return (int)field.GetValue(menu);
        }

        private static int FindColorIndex(LightColorMenu menu, string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
                return -1;
            string name = colorName.Trim();
            for (int i = 0; i < menu.lightColors.Length; i++)
            {
                if (string.Equals(menu.lightColors[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static GameObject FindTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
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
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标灯光建筑 InstanceID", Required = false },
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
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }
    }
}
