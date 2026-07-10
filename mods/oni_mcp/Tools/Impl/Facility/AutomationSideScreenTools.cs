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

    }
}
