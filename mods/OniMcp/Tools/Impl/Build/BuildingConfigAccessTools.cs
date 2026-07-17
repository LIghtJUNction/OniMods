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
    public static partial class BuildingConfigTools
    {
        public static McpTool CopySettings()
        {
            return new McpTool
            {
                Name = "buildings_copy_settings",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "copy_building_settings", "settings_copy" },
                Tags = new List<string> { "buildings", "copy", "settings", "side-screen", "batch" },
                Description = "兼容入口：请使用 building_control domain=config action=copy_settings",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["sourceId"] = new McpToolParameter { Type = "integer", Description = "源建筑 InstanceID；优先于 sourceX/sourceY", Required = false },
                    ["sourceX"] = new McpToolParameter { Type = "integer", Description = "源建筑格子 X", Required = false },
                    ["sourceY"] = new McpToolParameter { Type = "integer", Description = "源建筑格子 Y", Required = false },
                    ["targetId"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑 InstanceID；提供后忽略区域", Required = false },
                    ["targetX"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑格子 X；需配合 targetY", Required = false },
                    ["targetY"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑格子 Y；需配合 targetX", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var sourceArgs = new JObject();
                    if (args["sourceId"] != null)
                        sourceArgs["id"] = args["sourceId"];
                    if (args["sourceX"] != null)
                        sourceArgs["x"] = args["sourceX"];
                    if (args["sourceY"] != null)
                        sourceArgs["y"] = args["sourceY"];
                    if (args["worldId"] != null)
                        sourceArgs["worldId"] = args["worldId"];

                    var source = FindTarget(sourceArgs);
                    if (source == null)
                        return CallToolResult.Error("Source building not found; provide sourceId or sourceX/sourceY");
                    var sourceId = source.GetComponent<KPrefabID>();
                    var sourceSettings = source.GetComponent<CopyBuildingSettings>();
                    if (sourceId == null || sourceSettings == null)
                        return CallToolResult.Error("Source building does not support Copy Settings");

                    var targets = new List<KPrefabID>();
                    if (args["targetId"] != null || (args["targetX"] != null && args["targetY"] != null))
                    {
                        var targetArgs = new JObject();
                        if (args["targetId"] != null)
                            targetArgs["id"] = args["targetId"];
                        if (args["targetX"] != null)
                            targetArgs["x"] = args["targetX"];
                        if (args["targetY"] != null)
                            targetArgs["y"] = args["targetY"];
                        if (args["worldId"] != null)
                            targetArgs["worldId"] = args["worldId"];
                        var target = FindTarget(targetArgs);
                        if (target == null)
                            return CallToolResult.Error("Target building not found");
                        var targetId = target.GetComponent<KPrefabID>();
                        if (targetId != null)
                            targets.Add(targetId);
                    }
                    else
                    {
                        if (!HasRectInput(args))
                            return CallToolResult.Error("targetId, targetX/targetY, areaId or x1/y1/x2/y2 are required");

                        var rect = ToolUtil.GetRect(args);
                        int cells = (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
                        if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required when copying settings across more than 100 cells");

                        int worldId = ToolUtil.ResolveWorldId(args);
                        ObjectLayer layer = CopyBuildingSettings.ResolveLayer(source);
                        var seen = new HashSet<KPrefabID>();
                        for (int y = rect["y1"]; y <= rect["y2"]; y++)
                        {
                            for (int x = rect["x1"]; x <= rect["x2"]; x++)
                            {
                                int cell = Grid.XYToCell(x, y);
                                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                    continue;
                                var targetId = CopyBuildingSettings.ResolveTarget(layer, cell);
                                if (targetId != null && targetId.gameObject != source && seen.Add(targetId))
                                    targets.Add(targetId);
                            }
                        }
                    }

                    int applied = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var target in targets)
                    {
                        if (target == null || target.gameObject == null || target.gameObject == source)
                        {
                            skipped++;
                            continue;
                        }

                        bool copied = CopyBuildingSettings.ApplyCopy(target, source, sourceId, sourceSettings);
                        if (copied)
                            applied++;
                        else
                            skipped++;
                        var info = TargetInfo(target.gameObject);
                        info["status"] = copied ? "copied" : "skipped_incompatible";
                        results.Add(info);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["source"] = TargetInfo(source),
                        ["matched"] = targets.Count,
                        ["applied"] = applied,
                        ["skipped"] = skipped,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
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

        private static Dictionary<string, McpToolParameter> BuildingConfigControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list/list_automation/set_enabled/set_toggle/set_threshold/set_slider/set_valve_flow/set_limit_valve/set_logic_timer/set_logic_ribbon_bit/set_door_state/get_access/set_access/copy_settings/batch_set/batch_set_automation/state_list/state_set/visual；兼容 operation", Required = false },
                ["operation"] = new McpToolParameter { Type = "string", Description = "兼容旧参数；优先使用 action", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "action=batch_set/batch_set_automation 的批量操作数组", Required = false },
                ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch_set/batch_set_automation 合并到每个子项的默认参数", Required = false },
                ["capability"] = new McpToolParameter { Type = "string", Description = "action=list 时过滤配置能力", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list/list_automation 时按名称或 prefabId 筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "number", Description = "action=list/list_automation 时为返回上限；action=set_limit_valve 时为目标通过上限", Required = false },
                ["enabled"] = new McpToolParameter { Type = "boolean", Description = "action=set_enabled 时 true 启用建筑，false 禁用建筑", Required = false },
                ["on"] = new McpToolParameter { Type = "boolean", Description = "action=set_toggle 时 true 打开玩家手动开关，false 关闭", Required = false },
                ["component"] = new McpToolParameter { Type = "string", Description = "阈值/slider 目标组件类型名筛选", Required = false },
                ["threshold"] = new McpToolParameter { Type = "number", Description = "action=set_threshold 阈值", Required = false },
                ["activateAbove"] = new McpToolParameter { Type = "boolean", Description = "action=set_threshold 高于阈值激活", Required = false },
                ["value"] = new McpToolParameter { Type = "number", Description = "action=set_slider 目标 slider 值；action=state_set kind=checkbox 时为 boolean", Required = false },
                ["index"] = new McpToolParameter { Type = "integer", Description = "action=set_slider slider 索引", Required = false },
                ["flowKgPerSecond"] = new McpToolParameter { Type = "number", Description = "action=set_valve_flow 流量 kg/s", Required = false },
                ["resetAmount"] = new McpToolParameter { Type = "boolean", Description = "action=set_limit_valve 是否重置已通过计数", Required = false },
                ["onSeconds"] = new McpToolParameter { Type = "number", Description = "action=set_logic_timer 绿色信号持续秒数", Required = false },
                ["offSeconds"] = new McpToolParameter { Type = "number", Description = "action=set_logic_timer 红色信号持续秒数", Required = false },
                ["displayCyclesMode"] = new McpToolParameter { Type = "boolean", Description = "action=set_logic_timer 是否按周期显示", Required = false },
                ["reset"] = new McpToolParameter { Type = "boolean", Description = "action=set_logic_timer 是否重置计时器", Required = false },
                ["bitIndex"] = new McpToolParameter { Type = "integer", Description = "action=set_logic_ribbon_bit bit 索引 0..3", Required = false },
                ["state"] = new McpToolParameter { Type = "string", Description = "action=set_door_state：auto/opened/locked", Required = false },
                ["kind"] = new McpToolParameter { Type = "string", Description = "action=state_list/state_set 时为 any/capacity/checkbox/counter/time_range；action=visual 时为 light/pixel_pack", Required = false },
                ["visualAction"] = new McpToolParameter { Type = "string", Description = "action=visual 时的子动作：light 支持 list/set_color；pixel_pack 支持 list/set_color/copy_colors", Required = false },
                ["colorIndex"] = new McpToolParameter { Type = "integer", Description = "action=visual 写颜色时的目标颜色预设索引", Required = false },
                ["colorName"] = new McpToolParameter { Type = "string", Description = "action=visual 写颜色时的目标颜色预设名称", Required = false },
                ["panel"] = new McpToolParameter { Type = "string", Description = "action=visual kind=pixel_pack 时的目标面板", Required = false },
                ["sourcePanel"] = new McpToolParameter { Type = "string", Description = "action=visual kind=pixel_pack visualAction=copy_colors 时源面板", Required = false },
                ["sourceState"] = new McpToolParameter { Type = "string", Description = "action=visual kind=pixel_pack visualAction=copy_colors 时源状态", Required = false },
                ["targetPanel"] = new McpToolParameter { Type = "string", Description = "action=visual kind=pixel_pack visualAction=copy_colors 时目标面板", Required = false },
                ["targetState"] = new McpToolParameter { Type = "string", Description = "action=visual kind=pixel_pack visualAction=copy_colors 时目标状态", Required = false },
                ["capacity"] = new McpToolParameter { Type = "number", Description = "action=state_set kind=capacity 时目标容量", Required = false },
                ["maxCount"] = new McpToolParameter { Type = "integer", Description = "action=state_set kind=counter 时目标最大计数", Required = false },
                ["advancedMode"] = new McpToolParameter { Type = "boolean", Description = "action=state_set kind=counter 时是否启用高级模式", Required = false },
                ["start"] = new McpToolParameter { Type = "number", Description = "action=state_set kind=time_range 时开始时间，周期百分比 0-1", Required = false },
                ["duration"] = new McpToolParameter { Type = "number", Description = "action=state_set kind=time_range 时持续时间，周期百分比 0-1", Required = false },
                ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "action=get_access 是否包含复制人权限", Required = false },
                ["scope"] = new McpToolParameter { Type = "string", Description = "action=set_access：default_standard/default_bionic/default_robot/dupe", Required = false },
                ["permission"] = new McpToolParameter { Type = "string", Description = "action=set_access：both/go_left/go_right/neither", Required = false },
                ["dupeId"] = new McpToolParameter { Type = "integer", Description = "action=set_access scope=dupe 的复制人或 proxy InstanceID", Required = false },
                ["dupeName"] = new McpToolParameter { Type = "string", Description = "action=set_access scope=dupe 的复制人名称", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set_access scope=dupe 时清除覆盖权限", Required = false },
                ["sourceId"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 源建筑 InstanceID", Required = false },
                ["sourceX"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 源建筑 X", Required = false },
                ["sourceY"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 源建筑 Y", Required = false },
                ["targetId"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 单个目标建筑 InstanceID", Required = false },
                ["targetX"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 单个目标建筑 X", Required = false },
                ["targetY"] = new McpToolParameter { Type = "integer", Description = "action=copy_settings 单个目标建筑 Y", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=copy_settings 大区域复制时可能要求 true", Required = false }
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

            if (id.HasValue)
            {
                foreach (var prioritizable in Components.Prioritizables.Items)
                {
                    var go = prioritizable?.gameObject;
                    if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                        continue;
                    var kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id.Value)
                        return go;
                }
            }

            return null;
        }

        private static IThresholdSwitch FindThresholdSwitch(GameObject go, string componentName)
        {
            var matches = go.GetComponents<Component>()
                .OfType<IThresholdSwitch>()
                .ToList();
            if (matches.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(componentName))
                return matches[0];

            return matches.FirstOrDefault(item => item.GetType().Name.IndexOf(componentName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static ISliderControl FindSliderControl(GameObject go, string componentName)
        {
            var matches = SliderControls(go)
                .Select(item => item.Control)
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (matches.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(componentName))
                return matches[0];

            return matches.FirstOrDefault(item => item.GetType().Name.IndexOf(componentName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Dictionary<string, object> BuildConfigInfo(GameObject go)
        {
            var capabilities = new List<string>();
            var thresholds = go.GetComponents<Component>().OfType<IThresholdSwitch>().Select(ThresholdInfo).ToList();
            var sliders = SliderControlInfos(go);
            if (go.GetComponent<BuildingEnabledButton>() != null)
                capabilities.Add("enabled");
            if (go.GetComponents<Component>().OfType<IPlayerControlledToggle>().Any())
                capabilities.Add("toggle");
            if (thresholds.Count > 0)
                capabilities.Add("threshold");
            if (sliders.Count > 0)
                capabilities.Add("slider");
            if (go.GetComponent<DirectionControl>() != null)
                capabilities.Add("direction");
            if (go.GetComponent<FewOptionSideScreen.IFewOptionSideScreen>() != null)
                capabilities.Add("few_option");
            if (go.GetComponent<LogicBroadcastReceiver>() != null)
                capabilities.Add("broadcast_receiver");
            if (go.GetComponent<IHighEnergyParticleDirection>() != null)
                capabilities.Add("radbolt_direction");
            var capacity = go.GetComponent<IUserControlledCapacity>() ?? go.GetSMI<IUserControlledCapacity>();
            if (capacity != null && capacity.ControlEnabled())
                capabilities.Add("capacity");
            if (go.GetComponent<ICheckboxControl>() != null || go.GetSMI<ICheckboxControl>() != null)
                capabilities.Add("checkbox");
            if (go.GetComponent<LogicCounter>() != null)
                capabilities.Add("counter");
            if (go.GetComponent<LogicTimeOfDaySensor>() != null)
                capabilities.Add("time_range");
            if (go.GetComponent<LightColorMenu>() != null)
                capabilities.Add("light_color");
            if (go.GetComponent<Door>() != null)
                capabilities.Add("door");
            if (go.GetComponent<AccessControl>() != null)
                capabilities.Add("access");
            if (go.GetComponent<ManualDeliveryKG>() != null)
                capabilities.Add("manual_delivery");
            if (go.GetComponent<Filterable>() != null)
                capabilities.Add("filterable");
            if (go.GetComponent<TreeFilterable>() != null)
                capabilities.Add("tree_filter");
            if (go.GetComponent<FlatTagFilterable>() != null)
                capabilities.Add("flat_filter");
            if (go.GetComponent<Valve>() != null)
                capabilities.Add("valve");
            if (go.GetComponent<LimitValve>() != null)
                capabilities.Add("limit_valve");
            if (go.GetComponent<LogicTimerSensor>() != null)
                capabilities.Add("timer");
            if (go.GetComponent<ILogicRibbonBitSelector>() != null)
                capabilities.Add("ribbon_bit");
            if (go.GetComponent<LogicPorts>() != null)
                capabilities.Add("logic_ports");
            if (IsAutomationControl(go))
                capabilities.Add("automation");

            var result = TargetInfo(go);
            result["capabilities"] = capabilities;
            var enabledButton = go.GetComponent<BuildingEnabledButton>();
            if (enabledButton != null)
                result["enabled"] = enabledButton.IsEnabled;
            var playerToggle = go.GetComponents<Component>().OfType<IPlayerControlledToggle>().FirstOrDefault();
            if (playerToggle != null)
                result["toggle"] = playerToggle.ToggledOn();
            if (thresholds.Count > 0)
                result["thresholds"] = thresholds;
            if (sliders.Count > 0)
                result["sliders"] = sliders;
            var door = go.GetComponent<Door>();
            if (door != null)
                result["door"] = DoorInfo(door);
            var access = go.GetComponent<AccessControl>();
            if (access != null)
                result["access"] = AccessDefaults(access);
            var valve = go.GetComponent<Valve>();
            if (valve != null)
                result["valve"] = ValveInfo(valve);
            var limitValve = go.GetComponent<LimitValve>();
            if (limitValve != null)
                result["limitValve"] = LimitValveInfo(limitValve);
            var timer = go.GetComponent<LogicTimerSensor>();
            if (timer != null)
                result["timer"] = TimerInfo(timer);
            var ribbon = go.GetComponent<ILogicRibbonBitSelector>();
            if (ribbon != null)
                result["ribbonBit"] = RibbonBitInfo(ribbon);
            var ports = go.GetComponent<LogicPorts>();
            if (ports != null)
                result["logicPorts"] = LogicPortsInfo(ports);
            return result;
        }

        internal static Dictionary<string, object> SnapshotConfig(GameObject go)
        {
            return BuildConfigInfo(go);
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

        private static Dictionary<string, object> ThresholdInfo(IThresholdSwitch threshold)
        {
            return new Dictionary<string, object>
            {
                ["component"] = threshold.GetType().Name,
                ["title"] = threshold.Title.ToString(),
                ["valueName"] = threshold.ThresholdValueName.ToString(),
                ["threshold"] = ToolUtil.SafeFloat(threshold.Threshold),
                ["currentValue"] = ToolUtil.SafeFloat(threshold.CurrentValue),
                ["formattedThreshold"] = threshold.Format(threshold.Threshold, true),
                ["formattedCurrentValue"] = threshold.Format(threshold.CurrentValue, true),
                ["activateAbove"] = threshold.ActivateAboveThreshold,
                ["rangeMin"] = ToolUtil.SafeFloat(threshold.RangeMin),
                ["rangeMax"] = ToolUtil.SafeFloat(threshold.RangeMax),
                ["inputMin"] = ToolUtil.SafeFloat(threshold.GetRangeMinInputField()),
                ["inputMax"] = ToolUtil.SafeFloat(threshold.GetRangeMaxInputField()),
                ["units"] = threshold.ThresholdValueUnits().ToString()
            };
        }

    }
}
