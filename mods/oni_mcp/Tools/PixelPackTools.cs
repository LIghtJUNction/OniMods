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
    public static class PixelPackTools
    {
        private static readonly Color[] PresetColors = new[]
        {
            new Color(0.4862745f, 0.4862745f, 0.4862745f),
            new Color(0f, 0f, 84f / 85f),
            new Color(0f, 0f, 0.7372549f),
            new Color(4f / 15f, 8f / 51f, 0.7372549f),
            new Color(0.5803922f, 0f, 44f / 85f),
            new Color(56f / 85f, 0f, 0.1254902f),
            new Color(56f / 85f, 0.0627451f, 0f),
            new Color(8f / 15f, 4f / 51f, 0f),
            new Color(16f / 51f, 16f / 85f, 0f),
            new Color(0f, 0.47058824f, 0f),
            new Color(0f, 0.40784314f, 0f),
            new Color(0f, 0.34509805f, 0f),
            new Color(0f, 0.2509804f, 0.34509805f),
            new Color(0f, 0f, 0f),
            new Color(0.7372549f, 0.7372549f, 0.7372549f),
            new Color(0f, 0.47058824f, 0.972549f),
            new Color(0f, 0.34509805f, 0.972549f),
            new Color(0.40784314f, 4f / 15f, 84f / 85f),
            new Color(72f / 85f, 0f, 0.8f),
            new Color(76f / 85f, 0f, 0.34509805f),
            new Color(0.972549f, 0.21960784f, 0f),
            new Color(76f / 85f, 0.36078432f, 0.0627451f),
            new Color(0.6745098f, 0.4862745f, 0f),
            new Color(0f, 0.72156864f, 0f),
            new Color(0f, 56f / 85f, 0f),
            new Color(0f, 56f / 85f, 4f / 15f),
            new Color(0f, 8f / 15f, 8f / 15f),
            new Color(0f, 0f, 0f),
            new Color(0.972549f, 0.972549f, 0.972549f),
            new Color(0.23529412f, 0.7372549f, 84f / 85f),
            new Color(0.40784314f, 8f / 15f, 84f / 85f),
            new Color(0.59607846f, 0.47058824f, 0.972549f),
            new Color(0.972549f, 0.47058824f, 0.972549f),
            new Color(0.972549f, 0.34509805f, 0.59607846f),
            new Color(0.972549f, 0.47058824f, 0.34509805f),
            new Color(84f / 85f, 32f / 51f, 4f / 15f),
            new Color(0.972549f, 0.72156864f, 0f),
            new Color(0.72156864f, 0.972549f, 8f / 85f),
            new Color(0.34509805f, 72f / 85f, 28f / 85f),
            new Color(0.34509805f, 0.972549f, 0.59607846f),
            new Color(0f, 0.9098039f, 72f / 85f),
            new Color(0.47058824f, 0.47058824f, 0.47058824f),
            new Color(84f / 85f, 84f / 85f, 84f / 85f),
            new Color(0.6431373f, 76f / 85f, 84f / 85f),
            new Color(0.72156864f, 0.72156864f, 0.972549f),
            new Color(72f / 85f, 0.72156864f, 0.972549f),
            new Color(0.972549f, 0.72156864f, 0.972549f),
            new Color(0.972549f, 0.72156864f, 64f / 85f),
            new Color(0.9411765f, 0.8156863f, 0.6901961f),
            new Color(84f / 85f, 0.8784314f, 56f / 85f),
            new Color(0.972549f, 72f / 85f, 0.47058824f),
            new Color(72f / 85f, 0.972549f, 0.47058824f),
            new Color(0.72156864f, 0.972549f, 0.72156864f),
            new Color(0.72156864f, 0.972549f, 72f / 85f),
            new Color(0f, 84f / 85f, 84f / 85f),
            new Color(72f / 85f, 72f / 85f, 72f / 85f)
        };

        public static McpTool ControlPixelPack()
        {
            return new McpTool
            {
                Name = "pixel_pack_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "pixelpack_control", "pixel_pack_color_control" },
                Tags = new List<string> { "buildings", "pixel-pack", "colors", "automation", "side-screen" },
                Description = "Pixel Pack 颜色聚合工具：action=list 查询；action=set_color 设置单面板颜色；action=copy_colors 执行复制/交换",
                Parameters = PixelPackControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListPixelPacks().Handler(args);
                    if (action == "set_color" || action == "set")
                        return SetPixelPackColor().Handler(args);
                    if (action == "copy_colors" || action == "copy")
                        return CopyPixelPackColors().Handler(args);
                    return CallToolResult.Error("action must be list, set_color, or copy_colors");
                }
            };
        }

        public static McpTool ListPixelPacks()
        {
            return new McpTool
            {
                Name = "pixel_packs_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "pixel_pack_colors_list", "pixelpack_list" },
                Tags = new List<string> { "buildings", "pixel-pack", "colors", "automation", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=pixel_pack action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名或 prefabId 筛选", Required = false },
                    ["includePresets"] = new McpToolParameter { Type = "boolean", Description = "是否返回 Pixel Pack 侧屏颜色预设，默认 false", Required = false },
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
                    bool includePresets = ToolUtil.GetBool(args, "includePresets", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var packs = Components.BuildingCompletes.Items
                        .Select(building => building?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<PixelPack>() != null)
                        .Select(go => PixelPackInfo(go.GetComponent<PixelPack>()))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    var payload = new Dictionary<string, object>
                    {
                        ["returned"] = packs.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["pixelPacks"] = packs
                    };
                    if (includePresets)
                        payload["colorPresets"] = ColorPresets();

                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetPixelPackColor()
        {
            return new McpTool
            {
                Name = "pixel_pack_color_set",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "pixelpack_color_set" },
                Tags = new List<string> { "buildings", "pixel-pack", "colors", "automation", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=pixel_pack action=set_color",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["panelIndex"] = new McpToolParameter { Type = "integer", Description = "面板索引 0-3", Required = true },
                    ["state"] = new McpToolParameter { Type = "string", Description = "active 或 standby", Required = true, EnumValues = new List<string> { "active", "standby" } },
                    ["colorIndex"] = new McpToolParameter { Type = "integer", Description = "Pixel Pack 侧屏颜色预设索引；可用 building_control domain=special kind=pixel_pack action=list includePresets=true 查询", Required = false },
                    ["r"] = new McpToolParameter { Type = "number", Description = "自定义颜色红色通道 0-1；colorIndex 为空时使用", Required = false },
                    ["g"] = new McpToolParameter { Type = "number", Description = "自定义颜色绿色通道 0-1；colorIndex 为空时使用", Required = false },
                    ["b"] = new McpToolParameter { Type = "number", Description = "自定义颜色蓝色通道 0-1；colorIndex 为空时使用", Required = false },
                    ["a"] = new McpToolParameter { Type = "number", Description = "自定义颜色透明通道 0-1，默认 1", Required = false }
                }),
                Handler = args =>
                {
                    var pack = FindTarget(args);
                    if (pack == null)
                        return CallToolResult.Error("Target Pixel Pack not found");
                    EnsureColorSettings(pack);

                    int? panelIndex = ToolUtil.GetInt(args, "panelIndex");
                    if (!panelIndex.HasValue || panelIndex.Value < 0 || panelIndex.Value >= pack.colorSettings.Count)
                        return CallToolResult.Error("panelIndex must be in range 0-3");

                    string state = (args["state"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (state != "active" && state != "standby")
                        return CallToolResult.Error("state must be active or standby");

                    Color color;
                    string colorError;
                    if (!TryReadColor(args, out color, out colorError))
                        return CallToolResult.Error(colorError);

                    var before = PixelPackInfo(pack);
                    var pair = pack.colorSettings[panelIndex.Value];
                    if (state == "active")
                        pair.activeColor = color;
                    else
                        pair.standbyColor = color;
                    pack.colorSettings[panelIndex.Value] = pair;
                    pack.UpdateColors();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(pack.gameObject),
                        ["before"] = before,
                        ["pixelPack"] = PixelPackInfo(pack)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CopyPixelPackColors()
        {
            return new McpTool
            {
                Name = "pixel_pack_colors_copy",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "pixelpack_colors_copy", "pixel_pack_colors_swap" },
                Tags = new List<string> { "buildings", "pixel-pack", "colors", "automation", "side-screen" },
                Description = "兼容旧工具：请改用 building_control domain=special kind=pixel_pack action=copy_colors",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["operation"] = new McpToolParameter { Type = "string", Description = "active_to_standby、standby_to_active 或 swap", Required = true, EnumValues = new List<string> { "active_to_standby", "standby_to_active", "swap" } }
                }),
                Handler = args =>
                {
                    var pack = FindTarget(args);
                    if (pack == null)
                        return CallToolResult.Error("Target Pixel Pack not found");
                    EnsureColorSettings(pack);

                    string operation = (args["operation"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (operation != "active_to_standby" && operation != "standby_to_active" && operation != "swap")
                        return CallToolResult.Error("operation must be active_to_standby, standby_to_active, or swap");

                    var before = PixelPackInfo(pack);
                    for (int i = 0; i < pack.colorSettings.Count; i++)
                    {
                        var pair = pack.colorSettings[i];
                        if (operation == "active_to_standby")
                            pair.standbyColor = pair.activeColor;
                        else if (operation == "standby_to_active")
                            pair.activeColor = pair.standbyColor;
                        else
                        {
                            var active = pair.activeColor;
                            pair.activeColor = pair.standbyColor;
                            pair.standbyColor = active;
                        }
                        pack.colorSettings[i] = pair;
                    }
                    pack.UpdateColors();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(pack.gameObject),
                        ["operation"] = operation,
                        ["before"] = before,
                        ["pixelPack"] = PixelPackInfo(pack)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> PixelPackInfo(PixelPack pack)
        {
            EnsureColorSettings(pack);
            var result = TargetInfo(pack.gameObject);
            result["logicValue"] = pack.logicValue;
            result["panels"] = pack.colorSettings.Select((pair, index) => new Dictionary<string, object>
            {
                ["index"] = index,
                ["active"] = ColorToDictionary(pair.activeColor),
                ["standby"] = ColorToDictionary(pair.standbyColor),
                ["activeBit"] = LogicCircuitNetwork.IsBitActive(index, pack.logicValue)
            }).ToList();
            return result;
        }

        private static void EnsureColorSettings(PixelPack pack)
        {
            if (pack.colorSettings == null)
                pack.colorSettings = new List<PixelPack.ColorPair>();
            while (pack.colorSettings.Count < 4)
            {
                pack.colorSettings.Add(new PixelPack.ColorPair
                {
                    activeColor = PresetColors[38],
                    standbyColor = PresetColors[34]
                });
            }
        }

        private static bool TryReadColor(JObject args, out Color color, out string error)
        {
            int? colorIndex = ToolUtil.GetInt(args, "colorIndex");
            if (colorIndex.HasValue)
            {
                if (colorIndex.Value < 0 || colorIndex.Value >= PresetColors.Length)
                {
                    color = Color.white;
                    error = "colorIndex is outside Pixel Pack preset range";
                    return false;
                }
                color = PresetColors[colorIndex.Value];
                error = null;
                return true;
            }

            float? r = ToolUtil.GetFloat(args, "r");
            float? g = ToolUtil.GetFloat(args, "g");
            float? b = ToolUtil.GetFloat(args, "b");
            if (!r.HasValue || !g.HasValue || !b.HasValue)
            {
                color = Color.white;
                error = "colorIndex or r/g/b is required";
                return false;
            }
            float a = ToolUtil.GetFloat(args, "a") ?? 1f;
            color = new Color(Mathf.Clamp01(r.Value), Mathf.Clamp01(g.Value), Mathf.Clamp01(b.Value), Mathf.Clamp01(a));
            error = null;
            return true;
        }

        private static List<Dictionary<string, object>> ColorPresets()
        {
            return PresetColors
                .Select((color, index) => new Dictionary<string, object>
                {
                    ["index"] = index,
                    ["color"] = ColorToDictionary(color)
                })
                .ToList();
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

        private static PixelPack FindTarget(JObject args)
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
                var pack = go.GetComponent<PixelPack>();
                if (pack == null)
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return pack;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return pack;
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
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标 Pixel Pack InstanceID", Required = false },
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

        private static Dictionary<string, McpToolParameter> PixelPackControlParams()
        {
            var parameters = RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list、set_color 或 copy_colors", Required = true, EnumValues = new List<string> { "list", "set_color", "copy_colors" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名或 prefabId 筛选", Required = false },
                ["includePresets"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回 Pixel Pack 侧屏颜色预设，默认 false", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "action=set_color/copy_colors 时的目标 Pixel Pack InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "action=set_color/copy_colors 时的目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "action=set_color/copy_colors 时的目标格子 Y", Required = false },
                ["panelIndex"] = new McpToolParameter { Type = "integer", Description = "action=set_color 时的面板索引 0-3", Required = false },
                ["state"] = new McpToolParameter { Type = "string", Description = "action=set_color 时为 active 或 standby", Required = false, EnumValues = new List<string> { "active", "standby" } },
                ["colorIndex"] = new McpToolParameter { Type = "integer", Description = "action=set_color 时的 Pixel Pack 侧屏颜色预设索引；可用 action=list includePresets=true 查询", Required = false },
                ["r"] = new McpToolParameter { Type = "number", Description = "action=set_color 时自定义颜色红色通道 0-1；colorIndex 为空时使用", Required = false },
                ["g"] = new McpToolParameter { Type = "number", Description = "action=set_color 时自定义颜色绿色通道 0-1；colorIndex 为空时使用", Required = false },
                ["b"] = new McpToolParameter { Type = "number", Description = "action=set_color 时自定义颜色蓝色通道 0-1；colorIndex 为空时使用", Required = false },
                ["a"] = new McpToolParameter { Type = "number", Description = "action=set_color 时自定义颜色透明通道 0-1，默认 1", Required = false },
                ["operation"] = new McpToolParameter { Type = "string", Description = "action=copy_colors 时为 active_to_standby、standby_to_active 或 swap", Required = false, EnumValues = new List<string> { "active_to_standby", "standby_to_active", "swap" } }
            });
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
