using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class NotificationSurfaceAuditTools
    {
        public static McpTool AuditNotificationSurfaces()
        {
            return new McpTool
            {
                Name = "notification_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "hud_notification_surface_audit", "message_surface_audit" },
                Tags = new List<string> { "coverage", "audit", "notifications", "messages", "hud", "focus", "tools", "resources" },
                Description = "审计 ONI NotificationScreen/NotificationManager/消息通知与 MCP 工具/资源覆盖映射",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 class、surface、玩家操作、工具名或备注筛选", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "过滤状态：all、covered、review、no_action，默认 all", Required = false, EnumValues = new List<string> { "all", "covered", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 或 full，默认 brief", Required = false, EnumValues = new List<string> { "brief", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 80，最大 200", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").Trim();
                    string status = (args["status"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    string detail = (args["detail"]?.ToString() ?? "brief").Trim().ToLowerInvariant();
                    int limit = ToolUtil.ClampLimit(args, 80, 200);
                    var toolNames = new HashSet<string>(OniToolRegistry.GetVisibleTools().Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
                    var resourceNames = new HashSet<string>(
                        OniResourceRegistry.GetResourceInfos().Select(info => info.Name)
                            .Concat(OniResourceRegistry.GetResourceTemplateInfos().Select(info => info.Name)),
                        StringComparer.OrdinalIgnoreCase);

                    var auditRows = BuildAuditRows(toolNames, resourceNames);
                    var rows = auditRows
                        .Where(row => status == "all" || string.IsNullOrEmpty(status) || row.Status == status)
                        .Where(row => string.IsNullOrWhiteSpace(query) || MatchesQuery(row, query))
                        .OrderBy(row => StatusRank(row.Status))
                        .ThenBy(row => row.SourceClass)
                        .ThenBy(row => row.Surface)
                        .Take(limit)
                        .Select(row => row.ToDictionary(detail == "brief"))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["sourceNotificationSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示 NotificationScreen/NotificationManager 中可点击、聚焦、消息打开或 dismiss 的玩家操作已映射到 MCP 工具。",
                            "SimpleInfoScreen status-item click callbacks are documented as covered by selected-object detail/related entity/side-screen tools where the underlying operation is exposed.",
                            "review 会阻断 tools_static_audit，直到补工具、补资源或用源码证据标记 no_action。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<NotificationSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<NotificationSurfaceRow> KnownRows()
        {
            yield return Covered("NotificationScreen", "notification_rows", "Click notification row to cycle grouped notifications, focus/select target, run custom callback or open message", new[] { "colony_control", "navigation_control domain=camera" }, new[] { "colony_control" });
            yield return Covered("NotificationScreen", "dismiss_button", "Dismiss clearable notification groups", new[] { "colony_control" }, new[] { "colony_control" });
            yield return Covered("NotificationManager", "active_and_pending_notifications", "Read active/pending notification stack and readiness", new[] { "colony_control", "colony_alerts", "colony_diagnostics" }, new[] { "colony_control", "colony_alerts" });
            yield return Covered("MessageNotification", "message_dialog", "Open message notifications through NotificationScreen message dialog path", new[] { "colony_control" }, new[] { "colony_control" });
            yield return Covered("EventInfoScreen", "event_notification_focus", "Event notifications with clickFocus/custom callback", new[] { "colony_control" }, new[] { "colony_control" });
            yield return Covered("SimpleInfoScreen.StatusItemEntry", "status_item_clicks", "Clickable selected-object status items and related targets", new[] { "building_control domain=side_surface", "building_control domain=space_story" }, new[] { "building_control domain=side_surface", "building_control domain=space_story action=process_conditions" });
        }

        private static NotificationSurfaceRow Covered(string sourceClass, string surface, string playerSurface, string[] tools, string[] resources)
        {
            return new NotificationSurfaceRow
            {
                SourceClass = sourceClass,
                Surface = surface,
                Status = "covered",
                PlayerSurface = playerSurface,
                Tools = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList(),
                Resources = resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).ToList(),
                Notes = ""
            };
        }

        private static bool MatchesQuery(NotificationSurfaceRow row, string query)
        {
            return JsonConvert.SerializeObject(row.ToDictionary(false))
                .IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int StatusRank(string status)
        {
            switch (status)
            {
                case "review": return 0;
                case "covered": return 1;
                case "no_action": return 2;
                default: return 3;
            }
        }

        internal class NotificationSurfaceRow
        {
            public string SourceClass { get; set; }
            public string Surface { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public NotificationSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
            {
                MissingTools = SurfaceAuditUtil.MissingTools(Tools, toolNames);
                MissingResources = SurfaceAuditUtil.MissingResources(Resources, resourceNames);
                if (Status != "no_action" && (MissingTools.Count > 0 || MissingResources.Count > 0))
                    Status = "review";
                return this;
            }

            public Dictionary<string, object> ToDictionary(bool brief)
            {
                if (brief)
                {
                    return new Dictionary<string, object>
                    {
                        ["class"] = SourceClass,
                        ["surface"] = Surface,
                        ["status"] = Status,
                        ["tools"] = Tools
                    };
                }

                return new Dictionary<string, object>
                {
                    ["class"] = SourceClass,
                    ["surface"] = Surface,
                    ["status"] = Status,
                    ["playerSurface"] = PlayerSurface,
                    ["tools"] = Tools,
                    ["resources"] = Resources,
                    ["missingTools"] = MissingTools,
                    ["missingResources"] = MissingResources,
                    ["notes"] = Notes
                };
            }
        }
    }
}
