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
    public static class AutomationSideScreenTools
    {
        public static McpTool ListAutomatableControls()
        {
            return new McpTool
            {
                Name = "automatable_controls_list",
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "automatable_manual_controls_list", "automation_manual_allowed_list" },
                Tags = new List<string> { "automation", "side-screen", "automatable", "manual", "storage" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=automatable action=list。列出 AutomatableSideScreen 控件，显示建筑是否只允许自动化搬运以及是否允许复制人手动搬运",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名或 prefabId 筛选", Required = false },
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
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var controls = Components.BuildingCompletes.Items
                        .Select(item => item?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<Automatable>() != null)
                        .Select(AutomatableInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = controls.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["controls"] = controls
                    });
                }
            };
        }

        public static McpTool SetAutomatableControl()
        {
            return new McpTool
            {
                Name = "automatable_control_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "automatable_manual_control_set", "automation_manual_allowed_set" },
                Tags = new List<string> { "automation", "side-screen", "automatable", "manual", "storage" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=automatable action=set。设置 AutomatableSideScreen 的允许手动搬运开关；会影响复制人是否能手动搬运，需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["allowManual"] = new McpToolParameter { Type = "boolean", Description = "true 允许复制人手动搬运，false 只允许自动化搬运", Required = false },
                    ["automationOnly"] = new McpToolParameter { Type = "boolean", Description = "直接设置 Automatable.GetAutomationOnly；与 allowManual 二选一", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改搬运约束", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for Automatable changes");

                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target Automatable not found");

                    var automatable = go.GetComponent<Automatable>();
                    var before = AutomatableInfo(go);
                    ApplyAutomatable(automatable, args);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["automatable"] = AutomatableInfo(go)
                    });
                }
            };
        }

        public static McpTool BatchSetAutomatableControls()
        {
            return new McpTool
            {
                Name = "automatable_controls_batch_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "automatable_manual_controls_batch_set" },
                Tags = new List<string> { "automation", "side-screen", "automatable", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=automatable action=batch。批量设置 AutomatableSideScreen 的允许手动搬运开关，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "数组；每项支持 id 或 x/y/worldId，并提供 allowManual 或 automationOnly", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；子项参数优先，适合共享 allowManual/automationOnly/worldId", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量修改搬运约束", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for Automatable batch changes");

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
                        var item = MergeDefaults(rawItem, defaults);

                        var go = FindTarget(item);
                        if (go == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "Target Automatable not found", ["input"] = item });
                            continue;
                        }

                        var before = AutomatableInfo(go);
                        ApplyAutomatable(go.GetComponent<Automatable>(), item);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = true,
                            ["target"] = TargetInfo(go),
                            ["before"] = before,
                            ["automatable"] = AutomatableInfo(go)
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

        public static McpTool ListCritterSensors()
        {
            return new McpTool
            {
                Name = "critter_sensors_list",
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "critter_count_sensors_list", "logic_critter_sensors_list" },
                Tags = new List<string> { "automation", "sensor", "critter", "egg", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=critter_sensor action=list。列出 CritterSensorSideScreen / LogicCritterCountSensor 的小动物/蛋计数开关、阈值和当前计数",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名或 prefabId 筛选", Required = false },
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
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var sensors = Components.BuildingCompletes.Items
                        .Select(item => item?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<LogicCritterCountSensor>() != null)
                        .Select(CritterSensorInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = sensors.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["sensors"] = sensors
                    });
                }
            };
        }

        public static McpTool SetCritterSensorCounting()
        {
            return new McpTool
            {
                Name = "critter_sensor_counting_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "critter_count_sensor_set", "logic_critter_sensor_set" },
                Tags = new List<string> { "automation", "sensor", "critter", "egg", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=critter_sensor action=set。设置 CritterSensorSideScreen 的 Count Critters / Count Eggs 开关；阈值仍用 building_control domain=config action=set_threshold，需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["countCritters"] = new McpToolParameter { Type = "boolean", Description = "是否把小动物计入传感器当前值；留空不改", Required = false },
                    ["countEggs"] = new McpToolParameter { Type = "boolean", Description = "是否把蛋计入传感器当前值；留空不改", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改传感器计数来源", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for critter sensor changes");

                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target LogicCritterCountSensor not found");

                    var sensor = go.GetComponent<LogicCritterCountSensor>();
                    var before = CritterSensorInfo(go);
                    ApplyCritterSensor(sensor, args);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["sensor"] = CritterSensorInfo(go)
                    });
                }
            };
        }

        public static McpTool BatchSetCritterSensors()
        {
            return new McpTool
            {
                Name = "critter_sensors_batch_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "critter_count_sensors_batch_set" },
                Tags = new List<string> { "automation", "sensor", "critter", "egg", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=automation kind=critter_sensor action=batch。批量设置 CritterSensorSideScreen 的 Count Critters / Count Eggs 开关，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "数组；每项支持 id 或 x/y/worldId，并可提供 countCritters/countEggs", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；子项参数优先，适合共享 countCritters/countEggs/worldId", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量修改传感器计数来源", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for critter sensor batch changes");

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
                        var item = MergeDefaults(rawItem, defaults);

                        var go = FindTarget(item);
                        if (go == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "Target LogicCritterCountSensor not found", ["input"] = item });
                            continue;
                        }

                        var before = CritterSensorInfo(go);
                        ApplyCritterSensor(go.GetComponent<LogicCritterCountSensor>(), item);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = true,
                            ["target"] = TargetInfo(go),
                            ["before"] = before,
                            ["sensor"] = CritterSensorInfo(go)
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

        public static McpTool ControlAutomationSideScreen()
        {
            return new McpTool
            {
                Name = "automation_side_control",
                Hidden = true,
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "automation_side_screen_control", "side_automation_control" },
                Tags = new List<string> { "automation", "side-screen", "automatable", "critter-sensor", "batch" },
                Description = "统一读取、单点设置和批量设置自动化侧屏控件。kind=automatable/critter_sensor；action=list/set/batch；set/batch 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "控件类型：automatable 或 critter_sensor", Required = true },
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、set、batch", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名或 prefabId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "action=list 时区域句柄；与 x1/y1/x2/y2 二选一", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标 KPrefabID.InstanceID；推荐", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标格子 X；未传 id 时使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标格子 Y；未传 id 时使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；list 时可筛选，set 时按坐标查找建议提供", Required = false },
                    ["allowManual"] = new McpToolParameter { Type = "boolean", Description = "kind=automatable action=set/batch 时：true 允许手动搬运", Required = false },
                    ["automationOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=automatable action=set/batch 时：直接设置只允许自动化搬运", Required = false },
                    ["countCritters"] = new McpToolParameter { Type = "boolean", Description = "kind=critter_sensor action=set/batch 时：是否计入小动物", Required = false },
                    ["countEggs"] = new McpToolParameter { Type = "boolean", Description = "kind=critter_sensor action=set/batch 时：是否计入蛋", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时数组；每项支持 id 或 x/y/worldId，并提供对应 kind 的设置字段", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set/batch 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string kind = NormalizeKind(args["kind"]?.ToString());
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();

                    if (kind == "automatable")
                    {
                        if (action == "list")
                            return ListAutomatableControls().Handler(args);
                        if (action == "set")
                            return SetAutomatableControl().Handler(args);
                        if (action == "batch")
                            return BatchSetAutomatableControls().Handler(args);
                    }
                    else if (kind == "critter_sensor")
                    {
                        if (action == "list")
                            return ListCritterSensors().Handler(args);
                        if (action == "set")
                            return SetCritterSensorCounting().Handler(args);
                        if (action == "batch")
                            return BatchSetCritterSensors().Handler(args);
                    }

                    return CallToolResult.Error("kind must be automatable or critter_sensor; action must be list, set, or batch");
                }
            };
        }

        private static string NormalizeKind(string value)
        {
            string kind = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
            if (kind == "manual" || kind == "automatable_control" || kind == "automatable_controls")
                return "automatable";
            if (kind == "critter" || kind == "critter_sensor_counting" || kind == "critter_sensors" || kind == "logic_critter_sensor")
                return "critter_sensor";
            return kind;
        }

        private static void ApplyAutomatable(Automatable automatable, JObject args)
        {
            bool automationOnly = ToolUtil.GetBool(args, "automationOnly", automatable.GetAutomationOnly());
            if (args["allowManual"] != null)
                automationOnly = !ToolUtil.GetBool(args, "allowManual", !automationOnly);
            automatable.SetAutomationOnly(automationOnly);
        }

        private static void ApplyCritterSensor(LogicCritterCountSensor sensor, JObject args)
        {
            if (args["countCritters"] != null)
                sensor.countCritters = ToolUtil.GetBool(args, "countCritters", sensor.countCritters);
            if (args["countEggs"] != null)
                sensor.countEggs = ToolUtil.GetBool(args, "countEggs", sensor.countEggs);
        }

        private static JObject MergeDefaults(JObject item, JObject defaults)
        {
            var result = defaults == null ? new JObject() : (JObject)defaults.DeepClone();
            if (item == null)
                return result;

            foreach (var property in item.Properties())
                result[property.Name] = property.Value.DeepClone();
            return result;
        }

        private static Dictionary<string, object> AutomatableInfo(GameObject go)
        {
            var automatable = go.GetComponent<Automatable>();
            var result = TargetInfo(go);
            result["automationOnly"] = automatable.GetAutomationOnly();
            result["allowManual"] = !automatable.GetAutomationOnly();
            result["component"] = automatable.GetType().Name;
            return result;
        }

        private static Dictionary<string, object> CritterSensorInfo(GameObject go)
        {
            var sensor = go.GetComponent<LogicCritterCountSensor>();
            var result = TargetInfo(go);
            result["countCritters"] = sensor.countCritters;
            result["countEggs"] = sensor.countEggs;
            result["currentCount"] = sensor.currentCount;
            result["threshold"] = sensor.countThreshold;
            result["activateAboveThreshold"] = sensor.activateOnGreaterThan;
            result["isSwitchedOn"] = sensor.IsSwitchedOn;
            result["rangeMin"] = ToolUtil.SafeFloat(sensor.RangeMin);
            result["rangeMax"] = ToolUtil.SafeFloat(sensor.RangeMax);
            return result;
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
                if (id.HasValue)
                {
                    var kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id.Value)
                        return go;
                }
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
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

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
