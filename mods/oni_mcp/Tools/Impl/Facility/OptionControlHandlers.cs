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
    public static partial class OptionControlTools
    {
        private static CallToolResult SetOptionControl(JObject args, string forcedKind)
        {
            var go = FindTarget(args);
            if (go == null)
                return CallToolResult.Error("Target not found");

            string kind = forcedKind ?? (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
            switch (kind)
            {
                case "direction":
                    return SetDirectionControl(go, args);
                case "few_option":
                case "few-option":
                case "fewoption":
                    return SetFewOptionControl(go, args);
                case "broadcast_receiver":
                case "broadcast-receiver":
                case "broadcast":
                    return SetBroadcastReceiverControl(go, args);
                case "radbolt_direction":
                case "radbolt-direction":
                case "radbolt":
                    return SetRadboltDirectionControl(go, args);
                default:
                    return CallToolResult.Error("kind must be direction, few_option, broadcast_receiver, or radbolt_direction");
            }
        }

        private static CallToolResult SetDirectionControl(GameObject go, JObject args)
        {
            var control = go.GetComponent<DirectionControl>();
            if (control == null)
                return CallToolResult.Error("Target does not expose DirectionControl");

            WorkableReactable.AllowedDirection direction;
            if (!Enum.TryParse(args["direction"]?.ToString() ?? "", true, out direction))
                return CallToolResult.Error("direction must be Any, Left or Right");

            var before = control.allowedDirection;
            var method = OniReflection.GetMethodSafe(typeof(DirectionControl), "SetAllowedDirection", false, new[] { typeof(WorkableReactable.AllowedDirection) });
            if (method == null)
                return CallToolResult.Error("DirectionControl.SetAllowedDirection not found");
            method.Invoke(control, new object[] { direction });

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = "direction",
                ["before"] = before.ToString(),
                ["direction"] = control.allowedDirection.ToString(),
                ["changed"] = before != control.allowedDirection
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult SetFewOptionControl(GameObject go, JObject args)
        {
            var control = go.GetComponent<FewOptionSideScreen.IFewOptionSideScreen>();
            if (control == null)
                return CallToolResult.Error("Target does not expose IFewOptionSideScreen");

            string tagName = args["tag"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName))
                return CallToolResult.Error("tag is required");
            var tag = new Tag(tagName.Trim());
            var option = control.GetOptions().FirstOrDefault(item => item.tag == tag);
            if (option.tag != tag)
                return CallToolResult.Error("tag is not a valid option for this target");

            var before = control.GetSelectedOption();
            control.OnOptionSelected(option);

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = "few_option",
                ["before"] = TagInfo(before),
                ["selected"] = TagInfo(control.GetSelectedOption()),
                ["changed"] = before != control.GetSelectedOption()
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult SetBroadcastReceiverControl(GameObject go, JObject args)
        {
            var receiver = go.GetComponent<LogicBroadcastReceiver>();
            if (receiver == null)
                return CallToolResult.Error("Target does not expose LogicBroadcastReceiver");

            var before = receiver.GetChannel();
            if (ToolUtil.GetBool(args, "clear", false))
            {
                receiver.SetChannel(null);
            }
            else
            {
                int? broadcasterId = ToolUtil.GetInt(args, "broadcasterId");
                if (!broadcasterId.HasValue)
                    return CallToolResult.Error("broadcasterId is required unless clear=true");
                var broadcaster = FindBroadcaster(broadcasterId.Value);
                if (broadcaster == null)
                    return CallToolResult.Error("LogicBroadcaster not found");
                receiver.SetChannel(broadcaster);
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = "broadcast_receiver",
                ["before"] = before == null ? null : TargetInfo(before.gameObject),
                ["channel"] = receiver.GetChannel() == null ? null : TargetInfo(receiver.GetChannel().gameObject),
                ["changed"] = before != receiver.GetChannel()
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult SetRadboltDirectionControl(GameObject go, JObject args)
        {
            var control = go.GetComponent<IHighEnergyParticleDirection>();
            if (control == null)
                return CallToolResult.Error("Target does not expose IHighEnergyParticleDirection");

            EightDirection direction;
            if (!Enum.TryParse(args["direction"]?.ToString() ?? "", true, out direction))
                return CallToolResult.Error("direction must be one of the eight EightDirection values");

            var before = control.Direction;
            control.Direction = direction;
            if (Game.Instance != null)
                Game.Instance.ForceOverlayUpdate(clearLastMode: true);

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = "radbolt_direction",
                ["before"] = before.ToString(),
                ["direction"] = control.Direction.ToString(),
                ["changed"] = before != control.Direction
            }, McpJsonUtil.Settings));
        }

        private static Dictionary<string, object> ControlInfo(GameObject go, bool includeOptions)
        {
            var result = TargetInfo(go);
            var kinds = new List<string>();

            var direction = go.GetComponent<DirectionControl>();
            if (direction != null)
            {
                kinds.Add("direction");
                result["direction"] = new Dictionary<string, object>
                {
                    ["selected"] = direction.allowedDirection.ToString(),
                    ["options"] = new[] { "Any", "Left", "Right" }
                };
            }

            var few = go.GetComponent<FewOptionSideScreen.IFewOptionSideScreen>();
            if (few != null)
            {
                kinds.Add("few_option");
                var info = new Dictionary<string, object>
                {
                    ["selected"] = TagInfo(few.GetSelectedOption())
                };
                if (includeOptions)
                    info["options"] = few.GetOptions().Select(OptionInfo).ToList();
                result["fewOption"] = info;
            }

            var receiver = go.GetComponent<LogicBroadcastReceiver>();
            if (receiver != null)
            {
                kinds.Add("broadcast_receiver");
                var info = new Dictionary<string, object>
                {
                    ["channel"] = receiver.GetChannel() == null ? null : TargetInfo(receiver.GetChannel().gameObject)
                };
                if (includeOptions)
                    info["broadcasters"] = Components.LogicBroadcasters.Items.Where(item => item != null).Select(BroadcasterInfo).ToList();
                result["broadcastReceiver"] = info;
            }

            var radbolt = go.GetComponent<IHighEnergyParticleDirection>();
            if (radbolt != null)
            {
                kinds.Add("radbolt_direction");
                result["radboltDirection"] = new Dictionary<string, object>
                {
                    ["direction"] = radbolt.Direction.ToString(),
                    ["options"] = Enum.GetNames(typeof(EightDirection)).ToList()
                };
            }

            result["controlKinds"] = kinds.Distinct().OrderBy(kind => kind).ToList();
            return result;
        }

        private static Dictionary<string, object> OptionInfo(FewOptionSideScreen.IFewOptionSideScreen.Option option)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = option.tag.Name,
                ["label"] = ToolUtil.CleanName(option.labelText),
                ["tooltip"] = ToolUtil.CleanName(option.tooltipText)
            };
        }

        private static Dictionary<string, object> BroadcasterInfo(LogicBroadcaster broadcaster)
        {
            var result = TargetInfo(broadcaster.gameObject);
            result["currentValue"] = broadcaster.GetCurrentValue();
            return result;
        }

        private static LogicBroadcaster FindBroadcaster(int id)
        {
            foreach (var broadcaster in Components.LogicBroadcasters.Items)
            {
                var go = broadcaster?.gameObject;
                var kpid = go?.GetComponent<KPrefabID>();
                if (kpid != null && kpid.InstanceID == id)
                    return broadcaster;
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

        private static Dictionary<string, object> TagInfo(Tag tag)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = tag.Name,
                ["name"] = SafeProperName(tag),
                ["valid"] = tag.IsValid
            };
        }

        private static string SafeProperName(Tag tag)
        {
            try
            {
                return ToolUtil.CleanName(tag.ProperName());
            }
            catch
            {
                return tag.Name;
            }
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
