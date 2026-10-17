using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        public static McpTool GetRedAlertStatus()
        {
            return new McpTool
            {
                Name = "game_red_alert_status",
                Hidden = true,
                Group = "game",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "red_alert_status", "emergency_status" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "兼容入口：请优先使用 game_control domain=state action=red_alert_status",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "是否返回全部已加载世界，默认 false", Required = false }
                },
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");

                    bool allWorlds = ToolUtil.GetBool(args, "allWorlds", false);
                    var worlds = ResolveAlertWorlds(args, allWorlds);
                    if (worlds.Count == 0)
                        return CallToolResult.Error("No matching world found");

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["activeWorldId"] = ClusterManager.Instance.activeWorldId,
                        ["returned"] = worlds.Count,
                        ["worlds"] = worlds.Select(RedAlertWorldInfo).ToList()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetRedAlert()
        {
            return new McpTool
            {
                Name = "game_red_alert_set",
                Hidden = true,
                Group = "game",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "red_alert_set", "emergency_set", "game_emergency_set" },
                Tags = new List<string> { "game", "red-alert", "emergency", "priority", "紧急", "红色警戒" },
                Description = "兼容入口：game_control domain=state action=set_red_alert。默认只修改当前世界；allWorlds=true 可同步全部已加载世界。需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true 开启红色警戒，false 关闭", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前世界；allWorlds=true 时忽略", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "应用到全部已加载世界；默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认切换紧急模式", Required = true }
                },
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    if (args["enabled"] == null)
                        return CallToolResult.Error("enabled is required");

                    bool enabled = ToolUtil.GetBool(args, "enabled", false);
                    bool allWorlds = ToolUtil.GetBool(args, "allWorlds", false);
                    var worlds = ResolveAlertWorlds(args, allWorlds);
                    if (worlds.Count == 0)
                        return CallToolResult.Error("No matching world found");

                    var before = worlds.Select(RedAlertWorldInfo).ToList();
                    foreach (var world in worlds)
                    {
                        var alert = world?.AlertManager;
                        if (alert == null)
                            continue;
                        if (alert.IsRedAlertToggledOn() != enabled)
                            alert.ToggleRedAlert(enabled);
                    }
                    var after = worlds.Select(RedAlertWorldInfo).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["requestedEnabled"] = enabled,
                        ["allWorlds"] = allWorlds,
                        ["changed"] = JsonConvert.SerializeObject(before) != JsonConvert.SerializeObject(after),
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlGameState()
        {
            return new McpTool
            {
                Name = "game_state_control",
                Group = "game",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "game_emergency_control" },
                Tags = new List<string> { "game", "red-alert", "emergency", "state" },
                Description = "游戏状态：red_alert_status/set_red_alert。写动作需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "red_alert_status 或 set_red_alert", Required = true, EnumValues = new List<string> { "red_alert_status", "set_red_alert" } },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "set_red_alert：true 开启，false 关闭", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前世界", Required = false },
                    ["allWorlds"] = new McpToolParameter { Type = "boolean", Description = "全部已加载世界；默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "set_red_alert 必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "red_alert_status" || action == "status")
                        return GetRedAlertStatus().Handler(args);
                    if (action == "set_red_alert" || action == "red_alert")
                        return SetRedAlert().Handler(args);
                    return CallToolResult.Error("action must be red_alert_status or set_red_alert");
                }
            };
        }

        private static List<WorldContainer> ResolveAlertWorlds(JObject args, bool allWorlds)
        {
            if (ClusterManager.Instance == null)
                return new List<WorldContainer>();

            if (allWorlds)
                return ClusterManager.Instance.WorldContainers
                    .Where(item => item != null)
                    .OrderBy(item => item.id)
                    .ToList();

            int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance.activeWorldId;
            var world = ClusterManager.Instance.GetWorld(worldId);
            return world == null ? new List<WorldContainer>() : new List<WorldContainer> { world };
        }

        private static Dictionary<string, object> RedAlertWorldInfo(WorldContainer world)
        {
            var alert = world?.AlertManager;
            return new Dictionary<string, object>
            {
                ["worldId"] = world?.id ?? -1,
                ["name"] = world == null ? null : ToolUtil.CleanName(world.GetProperName()),
                ["isActive"] = ClusterManager.Instance != null && world != null && world.id == ClusterManager.Instance.activeWorldId,
                ["available"] = alert != null,
                ["redAlertToggledOn"] = alert != null && alert.IsRedAlertToggledOn(),
                ["isRedAlert"] = alert != null && alert.IsRedAlert(),
                ["isYellowAlert"] = alert != null && alert.IsYellowAlert(),
                ["isOn"] = alert != null && alert.IsOn()
            };
        }
    }
}
