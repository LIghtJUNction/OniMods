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
        private static Dictionary<string, object> SliderInfo(ISliderControl slider, int index)
        {
            return new Dictionary<string, object>
            {
                ["component"] = slider.GetType().Name,
                ["index"] = index,
                ["titleKey"] = slider.SliderTitleKey,
                ["units"] = slider.SliderUnits,
                ["value"] = ToolUtil.SafeFloat(slider.GetSliderValue(index)),
                ["min"] = ToolUtil.SafeFloat(slider.GetSliderMin(index)),
                ["max"] = ToolUtil.SafeFloat(slider.GetSliderMax(index)),
                ["decimalPlaces"] = slider.SliderDecimalPlaces(index),
                ["tooltipKey"] = slider.GetSliderTooltipKey(index)
            };
        }

        private static List<SliderControlRef> SliderControls(GameObject go)
        {
            var result = new List<SliderControlRef>();
            var seen = new HashSet<ISliderControl>();

            Action<ISliderControl, int, string> add = (control, index, source) =>
            {
                if (control == null || !seen.Add(control))
                    return;
                result.Add(new SliderControlRef { Control = control, Index = index, Source = source });
            };

            foreach (var control in go.GetComponents<Component>().OfType<ISliderControl>())
                add(control, 0, "component");

            add(go.GetSMI<ISliderControl>(), 0, "smi");
            add(go.GetSMI<IIntSliderControl>(), 0, "smi_int");
            add(go.GetSMI<IDualSliderControl>(), 0, "smi_dual");

            var multi = go.GetComponent<IMultiSliderControl>();
            if (multi != null && multi.SidescreenEnabled() && multi.sliderControls != null)
            {
                for (int i = 0; i < multi.sliderControls.Length; i++)
                    add(multi.sliderControls[i], i, "multi");
            }

            return result;
        }

        private static List<Dictionary<string, object>> SliderControlInfos(GameObject go)
        {
            return SliderControls(go)
                .Select(item =>
                {
                    var info = SliderInfo(item.Control, item.Index);
                    info["source"] = item.Source;
                    info["recommendedIndex"] = item.Index;
                    return info;
                })
                .ToList();
        }

        private static Dictionary<string, object> ValveInfo(Valve valve)
        {
            return new Dictionary<string, object>
            {
                ["desiredFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.DesiredFlow),
                ["queuedMaxFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.QueuedMaxFlow),
                ["maxFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.MaxFlow)
            };
        }

        private static Dictionary<string, object> LimitValveInfo(LimitValve valve)
        {
            return new Dictionary<string, object>
            {
                ["limit"] = ToolUtil.SafeFloat(valve.Limit),
                ["amount"] = ToolUtil.SafeFloat(valve.Amount),
                ["remainingCapacity"] = ToolUtil.SafeFloat(valve.RemainingCapacity),
                ["maxLimit"] = ToolUtil.SafeFloat(valve.maxLimitKg),
                ["displayUnitsInsteadOfMass"] = valve.displayUnitsInsteadOfMass,
                ["conduitType"] = valve.conduitType.ToString()
            };
        }

        private static Dictionary<string, object> TimerInfo(LogicTimerSensor timer)
        {
            return new Dictionary<string, object>
            {
                ["onSeconds"] = ToolUtil.SafeFloat(timer.onDuration),
                ["offSeconds"] = ToolUtil.SafeFloat(timer.offDuration),
                ["displayCyclesMode"] = timer.displayCyclesMode,
                ["timeElapsedInCurrentState"] = ToolUtil.SafeFloat(timer.timeElapsedInCurrentState),
                ["isSwitchedOn"] = timer.IsSwitchedOn
            };
        }

        private static Dictionary<string, object> RibbonBitInfo(ILogicRibbonBitSelector selector)
        {
            int depth = selector.GetBitDepth();
            var bits = new List<Dictionary<string, object>>();
            for (int i = 0; i < depth; i++)
            {
                bits.Add(new Dictionary<string, object>
                {
                    ["bitIndex"] = i,
                    ["active"] = selector.IsBitActive(i)
                });
            }

            return new Dictionary<string, object>
            {
                ["component"] = selector.GetType().Name,
                ["selectedBit"] = selector.GetBitSelection(),
                ["bitDepth"] = depth,
                ["inputValue"] = selector.GetInputValue(),
                ["outputValue"] = selector.GetOutputValue(),
                ["displayReaderDescription"] = selector.SideScreenDisplayReaderDescription(),
                ["displayWriterDescription"] = selector.SideScreenDisplayWriterDescription(),
                ["bits"] = bits
            };
        }

        private static Dictionary<string, object> LogicPortsInfo(LogicPorts ports)
        {
            if (LogicPortReadSemantics.TryBridgeRoute(ports?.gameObject, out int from, out int to)
                && (ports.inputPortInfo?.Length ?? 0) > 0)
            {
                var native = ports.inputPortInfo[0];
                return new Dictionary<string, object>
                {
                    ["inputs"] = new List<Dictionary<string, object>>
                        { PortInfo(ports, native, true, 0, from, "bridge_from") },
                    ["outputs"] = new List<Dictionary<string, object>>
                        { PortInfo(ports, native, false, 1, to, "bridge_to") },
                    ["bridgeRoute"] = new Dictionary<string, object>
                        { ["from"] = PortCoordinate(from), ["via"] = PortCoordinate(Grid.PosToCell(ports.gameObject)),
                            ["to"] = PortCoordinate(to) }
                };
            }
            return new Dictionary<string, object>
            {
                ["inputs"] = PortInfos(ports, ports.inputPortInfo, true),
                ["outputs"] = PortInfos(ports, ports.outputPortInfo, false)
            };
        }

        private static List<Dictionary<string, object>> PortInfos(LogicPorts ports, LogicPorts.Port[] portInfos, bool isInput)
        {
            var result = new List<Dictionary<string, object>>();
            if (portInfos == null)
                return result;

            for (int i = 0; i < portInfos.Length; i++)
                result.Add(PortInfo(ports, portInfos[i], isInput, i,
                    LogicPortReadSemantics.ActualCell(ports, portInfos[i]), null));

            return result;
        }

        private static Dictionary<string, object> PortInfo(LogicPorts ports, LogicPorts.Port port,
            bool isInput, int index, int cell, string semanticRole)
        {
            int value = semanticRole == "bridge_to" || isInput
                ? LogicPortReadSemantics.InputValue(ports, index)
                : LogicPortReadSemantics.OutputValue(ports, index);
            return new Dictionary<string, object>
            {
                ["id"] = port.id.ToString(),
                ["kind"] = isInput ? "input" : "output",
                ["semanticRole"] = semanticRole,
                ["cell"] = cell,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["connected"] = LogicPortReadSemantics.ConnectedAtCell(cell),
                ["value"] = value,
                ["spriteType"] = semanticRole == "bridge_to" ? "Output" : port.spriteType.ToString(),
                ["nativeSpriteType"] = port.spriteType.ToString(),
                ["requiresConnection"] = port.requiresConnection
            };
        }

        private static Dictionary<string, object> PortCoordinate(int cell)
        {
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static Dictionary<string, object> DoorInfo(Door door)
        {
            return new Dictionary<string, object>
            {
                ["current"] = door.CurrentState.ToString(),
                ["requested"] = door.RequestedState.ToString(),
                ["isOpen"] = door.IsOpen(),
                ["isSealed"] = door.isSealed
            };
        }

        private static Dictionary<string, object> AccessControlInfo(GameObject go, AccessControl access, bool includeDupes)
        {
            var result = TargetInfo(go);
            result["registered"] = access.registered;
            result["controlEnabled"] = access.controlEnabled;
            result["overrideAccess"] = access.overrideAccess.ToString();
            result["defaults"] = AccessDefaults(access);

            if (includeDupes)
            {
                result["dupes"] = Components.MinionAssignablesProxy.Items
                    .Where(proxy => proxy != null && proxy.GetTargetGameObject() != null)
                    .Select(proxy => DupeAccessInfo(access, proxy))
                    .OrderBy(item => item["name"].ToString())
                    .ToList();
            }

            return result;
        }

        private static Dictionary<string, object> AccessDefaults(AccessControl access)
        {
            return new Dictionary<string, object>
            {
                ["standard"] = access.GetDefaultPermission(GameTags.Minions.Models.Standard).ToString(),
                ["bionic"] = access.GetDefaultPermission(GameTags.Minions.Models.Bionic).ToString(),
                ["robot"] = access.GetDefaultPermission(GameTags.Robot).ToString()
            };
        }

        private static Dictionary<string, object> DupeAccessInfo(AccessControl access, MinionAssignablesProxy proxy)
        {
            var target = proxy.GetTargetGameObject();
            var proxyId = proxy.GetComponent<KPrefabID>();
            var targetId = target?.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["proxyId"] = proxyId?.InstanceID ?? proxy.GetInstanceID(),
                ["dupeId"] = targetId?.InstanceID ?? -1,
                ["name"] = proxy.GetProperName(),
                ["model"] = proxy.GetMinionModel().Name,
                ["permission"] = access.GetSetPermission(proxy).ToString(),
                ["usesDefault"] = access.IsDefaultPermission(proxy)
            };
        }

        private static MinionAssignablesProxy FindAssignableProxy(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "dupeId");
            string name = args["dupeName"]?.ToString();

            foreach (var proxy in Components.MinionAssignablesProxy.Items)
            {
                if (proxy == null)
                    continue;
                var proxyKpid = proxy.GetComponent<KPrefabID>();
                var target = proxy.GetTargetGameObject();
                var targetKpid = target?.GetComponent<KPrefabID>();
                if (id.HasValue && ((proxyKpid != null && proxyKpid.InstanceID == id.Value) || (targetKpid != null && targetKpid.InstanceID == id.Value)))
                    return proxy;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(proxy.GetProperName(), name, StringComparison.OrdinalIgnoreCase))
                    return proxy;
            }

            return null;
        }

        private static bool TryParseDoorState(string value, out Door.ControlState state)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "auto":
                    state = Door.ControlState.Auto;
                    return true;
                case "open":
                case "opened":
                    state = Door.ControlState.Opened;
                    return true;
                case "lock":
                case "locked":
                    state = Door.ControlState.Locked;
                    return true;
                default:
                    state = Door.ControlState.NumStates;
                    return false;
            }
        }

        private static bool TryParsePermission(string value, out AccessControl.Permission permission)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "both":
                    permission = AccessControl.Permission.Both;
                    return true;
                case "go_left":
                case "left":
                    permission = AccessControl.Permission.GoLeft;
                    return true;
                case "go_right":
                case "right":
                    permission = AccessControl.Permission.GoRight;
                    return true;
                case "neither":
                case "none":
                    permission = AccessControl.Permission.Neither;
                    return true;
                default:
                    permission = AccessControl.Permission.Both;
                    return false;
            }
        }

        private static string NormalizeScope(string value)
        {
            string scope = (value ?? "default_standard").Trim().ToLowerInvariant();
            if (scope == "standard")
                return "default_standard";
            if (scope == "bionic")
                return "default_bionic";
            if (scope == "robot")
                return "default_robot";
            if (scope == "dupe" || scope == "default_bionic" || scope == "default_robot")
                return scope;
            return "default_standard";
        }

        private static Tag DefaultScopeTag(string scope)
        {
            switch (scope)
            {
                case "default_bionic":
                    return GameTags.Minions.Models.Bionic;
                case "default_robot":
                    return GameTags.Robot;
                default:
                    return GameTags.Minions.Models.Standard;
            }
        }

        private static string NormalizeCapability(string value)
        {
            string capability = (value ?? "any").Trim().ToLowerInvariant();
            switch (capability)
            {
                case "automation":
                case "enabled":
                case "toggle":
                case "threshold":
                case "slider":
                case "direction":
                case "few_option":
                case "broadcast_receiver":
                case "radbolt_direction":
                case "capacity":
                case "checkbox":
                case "counter":
                case "time_range":
                case "light_color":
                case "door":
                case "access":
                case "manual_delivery":
                case "filterable":
                case "tree_filter":
                case "flat_filter":
                case "valve":
                case "limit_valve":
                case "timer":
                case "ribbon_bit":
                case "logic_ports":
                    return capability;
                default:
                    return "any";
            }
        }

        private static bool IsAutomationControl(GameObject go)
        {
            if (go == null)
                return false;
            return go.GetComponent<LogicPorts>() != null
                || go.GetComponents<Component>().OfType<IPlayerControlledToggle>().Any()
                || go.GetComponents<Component>().OfType<IThresholdSwitch>().Any()
                || SliderControls(go).Count > 0
                || go.GetComponent<Filterable>() != null
                || go.GetComponent<LogicBroadcastReceiver>() != null
                || go.GetComponent<LogicCounter>() != null
                || go.GetComponent<LogicTimeOfDaySensor>() != null
                || go.GetComponent<Valve>() != null
                || go.GetComponent<LimitValve>() != null
                || go.GetComponent<LogicTimerSensor>() != null
                || go.GetComponent<ILogicRibbonBitSelector>() != null;
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            string prefabId = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
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

        private class SliderControlRef
        {
            public ISliderControl Control { get; set; }
            public int Index { get; set; }
            public string Source { get; set; }
        }
    }
}
