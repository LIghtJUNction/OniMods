using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class SurfaceAuditControlTools
    {
        public static McpTool ControlSurfaceAudit()
        {
            return new McpTool
            {
                Name = "surface_audit_control",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "ui_surface_audit", "surface_coverage_audit" },
                Tags = new List<string> { "coverage", "audit", "surfaces", "ui", "tools", "resources" },
                Description = "统一审计 ONI 玩家操作 surface 覆盖。action=side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "审计类型：side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification", Required = true, EnumValues = new List<string> { "side_screen", "user_menu", "management", "tool_menu", "ui_menu", "global_control", "notification" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 class、surface、工具名、资源名或备注筛选", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "过滤状态：all、covered、review、no_action，默认 all", Required = false, EnumValues = new List<string> { "all", "covered", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 或 full；side_screen 忽略该参数", Required = false, EnumValues = new List<string> { "brief", "full" } },
                    ["includeNoAction"] = new McpToolParameter { Type = "boolean", Description = "side_screen 时是否返回纯显示/无玩家操作侧屏，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量；各 audit 保持原默认值和上限", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    var delegated = (JObject)args.DeepClone();
                    delegated.Remove("action");

                    switch (action)
                    {
                        case "side_screen":
                            return SideScreenSurfaceTools.AuditSideScreenSurfaces().Handler(delegated);
                        case "user_menu":
                            return UserMenuSurfaceAuditTools.AuditUserMenuSurfaces().Handler(delegated);
                        case "management":
                            return ManagementSurfaceAuditTools.AuditManagementSurfaces().Handler(delegated);
                        case "tool_menu":
                            return ToolMenuSurfaceAuditTools.AuditToolMenuSurfaces().Handler(delegated);
                        case "ui_menu":
                            return UiMenuSurfaceAuditTools.AuditUiMenuSurfaces().Handler(delegated);
                        case "global_control":
                            return GlobalControlSurfaceAuditTools.AuditGlobalControlSurfaces().Handler(delegated);
                        case "notification":
                            return NotificationSurfaceAuditTools.AuditNotificationSurfaces().Handler(delegated);
                        default:
                            return CallToolResult.Error("action must be one of side_screen, user_menu, management, tool_menu, ui_menu, global_control, notification");
                    }
                }
            };
        }
    }
}
