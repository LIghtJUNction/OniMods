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
    public static partial class AutomationSideScreenTools
    {
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
