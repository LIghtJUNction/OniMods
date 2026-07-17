using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        public static McpTool SetBuildingEnabled()
        {
            return new McpTool
            {
                Name = "buildings_set_enabled",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "building_enable", "building_disable" },
                Hidden = true,
                Description = "兼容入口：请使用 building_control domain=config action=set_enabled。启用或禁用指定建筑；直接设置建筑状态，不排队复制人开关差事",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 启用，false 禁用", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var button = go.GetComponent<BuildingEnabledButton>();
                    if (button == null)
                        return CallToolResult.Error("Target does not support enabled/disabled state");

                    bool enabled = ToolUtil.GetBool(args, "enabled", true);
                    button.IsEnabled = enabled;
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["enabled"] = button.IsEnabled
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetBuildingToggle()
        {
            return new McpTool
            {
                Name = "buildings_set_toggle",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "logic_switch_set", "building_toggle_set" },
                Hidden = true,
                Description = "兼容入口：请使用 building_control domain=config action=set_toggle。设置支持玩家手动开关的建筑/自动化开关状态，例如逻辑开关；仅当目标实现 IPlayerControlledToggle 时可用",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["on"] = new McpToolParameter { Type = "boolean", Description = "true 打开，false 关闭", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var toggle = go.GetComponents<Component>().OfType<IPlayerControlledToggle>().FirstOrDefault();
                    if (toggle == null)
                        return CallToolResult.Error("Target does not expose a player-controlled toggle");

                    bool desired = ToolUtil.GetBool(args, "on", true);
                    bool before = toggle.ToggledOn();
                    if (before != desired)
                        toggle.ToggledByPlayer();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["before"] = before,
                        ["on"] = toggle.ToggledOn(),
                        ["sideScreenTitleKey"] = toggle.SideScreenTitleKey
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ConfigureManualDelivery()
        {
            return new McpTool
            {
                Name = "buildings_manual_delivery",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "manual_delivery_set", "building_refill_set" },
                Description = "兼容入口：请优先使用 orders_control domain=designation action=manual_delivery。配置建筑手动补料/搬运：暂停或恢复补料、设置容量/补料阈值、立即请求搬运",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["paused"] = new McpToolParameter { Type = "boolean", Description = "可选：true 暂停手动补料，false 恢复", Required = false },
                    ["capacityKg"] = new McpToolParameter { Type = "number", Description = "可选：目标储量上限 kg", Required = false },
                    ["refillMassKg"] = new McpToolParameter { Type = "number", Description = "可选：低于该质量时请求补料 kg", Required = false },
                    ["minimumMassKg"] = new McpToolParameter { Type = "number", Description = "可选：单次搬运最小质量 kg", Required = false },
                    ["requestNow"] = new McpToolParameter { Type = "boolean", Description = "是否立即请求一次搬运，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var delivery = go.GetComponent<ManualDeliveryKG>();
                    if (delivery == null)
                        return CallToolResult.Error("Target does not support manual delivery");

                    if (args["paused"] != null)
                        delivery.Pause(ToolUtil.GetBool(args, "paused", false), "Oni MCP manual delivery setting");
                    float? capacity = ToolUtil.GetFloat(args, "capacityKg");
                    if (capacity.HasValue)
                        delivery.capacity = Math.Max(0f, capacity.Value);
                    float? refill = ToolUtil.GetFloat(args, "refillMassKg");
                    if (refill.HasValue)
                        delivery.refillMass = Math.Max(0f, refill.Value);
                    float? minimum = ToolUtil.GetFloat(args, "minimumMassKg");
                    if (minimum.HasValue)
                        delivery.MinimumMass = Math.Max(0f, minimum.Value);
                    if (ToolUtil.GetBool(args, "requestNow", false))
                        delivery.RequestDelivery();

                    delivery.UpdateDeliveryState();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        ["name"] = ToolUtil.CleanName(go.GetProperName()),
                        ["paused"] = delivery.IsPaused,
                        ["capacityKg"] = Math.Round(delivery.capacity, 3),
                        ["refillMassKg"] = Math.Round(delivery.refillMass, 3),
                        ["minimumMassKg"] = Math.Round(delivery.MinimumMass, 3),
                        ["requestedItemTag"] = delivery.RequestedItemTag.Name
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ObjectResult(GameObject go, string status)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static Dictionary<string, object> CellResult(int cell, string status)
        {
            return new Dictionary<string, object>
            {
                ["status"] = status,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["element"] = Grid.IsValidCell(cell) ? Grid.Element[cell].id.ToString() : "",
                ["massKg"] = Grid.IsValidCell(cell) ? Math.Round(Grid.Mass[cell], 3) : 0
            };
        }
}
}
