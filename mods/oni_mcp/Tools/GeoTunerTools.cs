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
    public static class GeoTunerTools
    {
        private const int MaxTunersPerGeyser = 5;

        public static McpTool ListGeoTuners()
        {
            return new McpTool
            {
                Name = "geo_tuners_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "geotuners_list", "geo_tuner_assignments_list" },
                Tags = new List<string> { "buildings", "geotuner", "geyser", "side-screen", "spaced-out" },
                Description = "列出 GeoTuner 建筑、当前/未来目标喷泉和调谐分配状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、喷泉名或喷泉类型筛选", Required = false },
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
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var tuners = AllGeoTuners(worldId)
                        .Where(tuner => MatchesTarget(tuner?.gameObject, rect, worldId))
                        .Select(GeoTunerInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = tuners.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["maxTunersPerGeyser"] = MaxTunersPerGeyser,
                        ["geoTuners"] = tuners
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListGeoTunerGeysers()
        {
            return new McpTool
            {
                Name = "geo_tuner_geysers_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "geotuner_geysers_list", "geotuner_targets_list" },
                Tags = new List<string> { "buildings", "geotuner", "geyser", "side-screen", "spaced-out" },
                Description = "列出 GeoTuner 可选择的喷泉目标，包括研究、可见、已分配数量和是否可分配",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按喷泉名、prefabId 或喷泉类型筛选", Required = false },
                    ["includeUnstudied"] = new McpToolParameter { Type = "boolean", Description = "是否包含可见但未研究喷泉，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var tuner = HasLookupInput(args) ? FindGeoTuner(args) : null;
                    int worldId = tuner != null ? tuner.GetMyWorldId() : ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool includeUnstudied = ToolUtil.GetBool(args, "includeUnstudied", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var geysers = Components.Geysers.GetItems(worldId)
                        .Where(geyser => geyser != null)
                        .Select(geyser => GeyserInfo(geyser, tuner))
                        .Where(info => (bool)info["studied"] || (includeUnstudied && (bool)info["visible"] && (bool)info["uncovered"]))
                        .Where(info => MatchesQuery(info, query))
                        .OrderByDescending(info => (bool)info["studied"])
                        .ThenBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["targetGeoTuner"] = tuner != null ? TargetInfo(tuner.gameObject) : null,
                        ["maxTunersPerGeyser"] = MaxTunersPerGeyser,
                        ["returned"] = geysers.Count,
                        ["geysers"] = geysers
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AssignGeoTuner()
        {
            return new McpTool
            {
                Name = "geo_tuner_assign",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "geotuner_assign", "geo_tuner_set_geyser" },
                Tags = new List<string> { "buildings", "geotuner", "geyser", "side-screen", "spaced-out" },
                Description = "设置 GeoTuner 未来目标喷泉或清空目标，等同于 GeoTuner 侧屏选择喷泉",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["geyserId"] = new McpToolParameter { Type = "integer", Description = "目标喷泉 InstanceID；clear=true 时忽略", Required = false },
                    ["geyserX"] = new McpToolParameter { Type = "integer", Description = "目标喷泉格子 X；geyserId 为空时可用坐标定位", Required = false },
                    ["geyserY"] = new McpToolParameter { Type = "integer", Description = "目标喷泉格子 Y；geyserId 为空时可用坐标定位", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "true 表示选择 Nothing/清空未来目标", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过未研究和 5 台上限检查；默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var tuner = FindGeoTuner(args);
                    if (tuner == null)
                        return CallToolResult.Error("Target GeoTuner not found");

                    bool clear = ToolUtil.GetBool(args, "clear", false);
                    bool force = ToolUtil.GetBool(args, "force", false);
                    Geyser geyser = null;
                    if (!clear)
                    {
                        geyser = FindGeyser(args, tuner.GetMyWorldId());
                        if (geyser == null)
                            return CallToolResult.Error("Target geyser not found; provide geyserId or geyserX/geyserY, or set clear=true");
                        if (geyser.GetMyWorldId() != tuner.GetMyWorldId())
                            return CallToolResult.Error("GeoTuner and geyser must be in the same world");
                        if (!force && !IsStudied(geyser))
                            return CallToolResult.Error("GeoTuner side screen only allows studied geysers; use force=true to override");
                        int count = CountFutureOrAssigned(tuner.GetMyWorldId(), geyser);
                        if (!force && geyser != tuner.GetFutureGeyser() && count >= MaxTunersPerGeyser)
                            return CallToolResult.Error("Target geyser already has the maximum number of assigned/future GeoTuners");
                    }

                    var before = GeoTunerInfo(tuner);
                    tuner.AssignFutureGeyser(geyser);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(tuner.gameObject),
                        ["clear"] = clear,
                        ["before"] = before,
                        ["geoTuner"] = GeoTunerInfo(tuner),
                        ["geyser"] = geyser != null ? GeyserInfo(geyser, tuner) : null
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> GeoTunerInfo(GeoTuner.Instance tuner)
        {
            var result = TargetInfo(tuner.gameObject);
            var future = tuner.GetFutureGeyser();
            var assigned = tuner.GetAssignedGeyser();
            result["futureGeyser"] = future != null ? GeyserInfo(future, tuner) : null;
            result["assignedGeyser"] = assigned != null ? GeyserInfo(assigned, tuner) : null;
            result["enhancementDuration"] = Math.Round(ToolUtil.SafeFloat(tuner.enhancementDuration), 2);
            result["hasSwitchChore"] = future != assigned;
            return result;
        }

        private static Dictionary<string, object> GeyserInfo(Geyser geyser, GeoTuner.Instance contextTuner)
        {
            int cell = Grid.PosToCell(geyser);
            int worldId = geyser.GetMyWorldId();
            int futureCount = Components.GeoTuners.GetItems(worldId).Count(tuner => tuner.GetFutureGeyser() == geyser);
            int assignedCount = Components.GeoTuners.GetItems(worldId).Count(tuner => tuner.GetAssignedGeyser() == geyser);
            int futureOrAssignedCount = Components.GeoTuners.GetItems(worldId).Count(tuner => tuner.GetFutureGeyser() == geyser || tuner.GetAssignedGeyser() == geyser);
            bool isCurrentFuture = contextTuner != null && contextTuner.GetFutureGeyser() == geyser;
            bool studied = IsStudied(geyser);
            bool visible = Grid.IsValidCell(cell) && Grid.Visible[cell] > 0;
            bool uncovered = geyser.GetComponent<Uncoverable>()?.IsUncovered ?? false;
            var kpid = geyser.GetComponent<KPrefabID>();
            var info = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? geyser.gameObject.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? geyser.gameObject.name,
                ["name"] = ToolUtil.CleanName(geyser.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = worldId,
                ["typeId"] = geyser.configuration.typeId.ToString(),
                ["element"] = geyser.configuration.geyserType.element.ToString(),
                ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(geyser.configuration.geyserType.temperature), 2),
                ["studied"] = studied,
                ["visible"] = visible,
                ["uncovered"] = uncovered,
                ["futureGeoTuners"] = futureCount,
                ["assignedGeoTuners"] = assignedCount,
                ["futureOrAssignedGeoTuners"] = futureOrAssignedCount,
                ["selectedByContextTuner"] = isCurrentFuture,
                ["assignableBySideScreen"] = studied && (isCurrentFuture || futureOrAssignedCount < MaxTunersPerGeyser),
                ["blockedReason"] = studied ? (isCurrentFuture || futureOrAssignedCount < MaxTunersPerGeyser ? null : "max_geotuners") : "not_studied"
            };

            if (contextTuner != null && studied)
            {
                var settings = contextTuner.def.GetSettingsForGeyser(geyser);
                info["geotuneMaterial"] = settings.material.ProperName();
            }
            return info;
        }

        private static bool IsStudied(Geyser geyser)
        {
            return geyser.GetComponent<Studyable>()?.Studied ?? false;
        }

        private static int CountFutureOrAssigned(int worldId, Geyser geyser)
        {
            return Components.GeoTuners.GetItems(worldId)
                .Count(tuner => tuner.GetFutureGeyser() == geyser || tuner.GetAssignedGeyser() == geyser);
        }

        private static GeoTuner.Instance FindGeoTuner(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var tuner in AllGeoTuners(worldId))
            {
                if (tuner == null)
                    continue;
                var go = tuner.gameObject;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return tuner;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return tuner;
            }
            return null;
        }

        private static Geyser FindGeyser(JObject args, int worldId)
        {
            int? geyserId = ToolUtil.GetInt(args, "geyserId");
            int? x = ToolUtil.GetInt(args, "geyserX");
            int? y = ToolUtil.GetInt(args, "geyserY");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;

            foreach (var geyser in Components.Geysers.GetItems(worldId))
            {
                if (geyser == null)
                    continue;
                var kpid = geyser.GetComponent<KPrefabID>();
                if (geyserId.HasValue && kpid != null && kpid.InstanceID == geyserId.Value)
                    return geyser;
                if (cell.HasValue && Grid.PosToCell(geyser) == cell.Value)
                    return geyser;
            }
            return null;
        }

        private static IEnumerable<GeoTuner.Instance> AllGeoTuners(int worldId)
        {
            if (worldId >= 0)
                return Components.GeoTuners.GetItems(worldId);
            return Components.GeoTuners.GetWorldsIds()
                .SelectMany(id => Components.GeoTuners.GetItems(id));
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
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标 GeoTuner InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标 GeoTuner 格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标 GeoTuner 格子 Y", Required = false },
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

        private static bool HasLookupInput(JObject args)
        {
            return ToolUtil.GetInt(args, "id").HasValue
                   || ToolUtil.GetInt(args, "x").HasValue
                   || ToolUtil.GetInt(args, "y").HasValue;
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
