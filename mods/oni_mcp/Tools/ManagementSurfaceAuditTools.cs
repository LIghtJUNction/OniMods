using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ManagementSurfaceAuditTools
    {
        public static McpTool AuditManagementSurfaces()
        {
            return new McpTool
            {
                Name = "management_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "management_screen_audit", "table_screen_audit" },
                Tags = new List<string> { "coverage", "audit", "management", "screen", "ui", "tools", "resources" },
                Description = "审计 ONI 管理菜单和 TableScreen/全屏管理界面与 MCP 工具/资源覆盖映射",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 screen、Action、玩家操作、工具名或备注筛选", Required = false },
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
                        .ThenBy(row => row.Screen)
                        .Take(limit)
                        .Select(row => row.ToDictionary(detail == "brief"))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["sourceManagementSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示 ManagementMenu/TableScreen/全屏管理界面的玩家操作已映射到 MCP 工具或资源。",
                            "review 会阻断 tools_static_audit，直到补工具、补资源或用源码证据标记 no_action。",
                            "sourceScreen/action 来源于当前 U59 反编译源码中的 ManagementMenu toggle 和对应 screen class。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<ManagementSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<ManagementSurfaceRow> KnownRows()
        {
            yield return Covered(
                "ManagementMenu",
                "top_right_management_toggles",
                "ManageVitals/ManageConsumables/ManagePriorities/ManageSchedule/ManageSkills/ManageResearch/ManageStarmap/ManageReport/ManageDatabase",
                "Open, close and switch top-right management screens",
                new[] { "game_control domain=ui uiDomain=action", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "game_control domain=ui uiDomain=action" });

            yield return Covered(
                "VitalsTableScreen",
                "vitals",
                "ManageVitals",
                "Inspect duplicant stress, morale expectation, fullness, health and immunity; select/focus duplicant rows",
                new[] { "dupes_control domain=info", "navigation_control domain=camera", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "dupes_list" });

            yield return Covered(
                "ConsumablesTableScreen",
                "consumables",
                "ManageConsumables",
                "Inspect and edit per-duplicant consumable food/medicine/battery permissions, including batch policies",
                new[] { "colony_control domain=management kind=diet", "resources_food", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "colony_control domain=management kind=diet", "resources_food" });

            yield return Covered(
                "JobsTableScreen",
                "priorities",
                "ManagePriorities",
                "Inspect and edit per-duplicant personal ChoreGroup priorities",
                new[] { "dupes_control domain=priority", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "dupes_control domain=priority" });

            yield return Covered(
                "JobsTableScreen",
                "priority_settings",
                "ManagePriorities",
                "Toggle advanced personal priority mode and reset personal priorities using the same semantics as the screen's Reset button",
                new[] { "dupes_control domain=priority", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "dupes_control domain=priority" });

            yield return Covered(
                "ScheduleScreen",
                "schedule",
                "ManageSchedule",
                "Create schedules, edit schedule blocks and assign duplicants to schedules",
                new[] { "colony_control domain=management kind=schedule", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "colony_control domain=management kind=schedule" });

            yield return Covered(
                "SkillsScreen",
                "skills",
                "ManageSkills",
                "Inspect duplicant skill points, learn skills and choose hats from the skill screen",
                new[] { "dupes_control domain=skill", "dupes_control domain=hat", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "dupes_control domain=skill", "dupes_control domain=hat" });

            yield return Covered(
                "ResearchScreen",
                "research",
                "ManageResearch",
                "Inspect research tree, search technologies and set/clear active research queue target",
                new[] { "colony_control domain=management kind=research action=status", "colony_control domain=management kind=research", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "research_status" });

            yield return Covered(
                "StarmapScreen",
                "starmap",
                "ManageStarmap",
                "Inspect base-game starmap, rocket state, destinations and telescope analysis targets",
                new[] { "game_control domain=ui uiDomain=action action=open_management", "building_control domain=rocket rocketDomain=ops", "building_control domain=space_story" },
                new[] { "building_control domain=rocket rocketDomain=ops", "starmap_analysis_targets_list" });

            yield return Covered(
                "ClusterMapScreen",
                "starmap_cluster",
                "ManageStarmap",
                "Inspect Spaced Out cluster map, switch active world and set rocket destinations/analysis targets",
                new[] { "game_control domain=ui uiDomain=action action=open_management", "colony_control domain=read action=worlds", "navigation_control domain=camera", "building_control domain=rocket rocketDomain=ops", "building_control domain=space_story" },
                new[] { "colony_control domain=read action=worlds", "building_control domain=rocket rocketDomain=ops", "starmap_analysis_targets_list" });

            yield return Covered(
                "ReportScreen",
                "report",
                "ManageReport",
                "Open and inspect current or historical colony reports",
                new[] { "colony_report", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "colony_report" });

            yield return Covered(
                "CodexScreen",
                "database_codex",
                "ManageDatabase",
                "Open codex/database entries and query ONI database content by id/name/category",
                new[] { "read_control domain=knowledge kind=database action=query", "game_control domain=ui uiDomain=action action=open_management" },
                new[] { "tools_read_resource" });
        }

        private static ManagementSurfaceRow Covered(string screen, string surface, string action, string playerSurface, string[] tools, string[] resources)
        {
            return new ManagementSurfaceRow
            {
                Screen = screen,
                Surface = surface,
                Action = action,
                Status = "covered",
                PlayerSurface = playerSurface,
                Tools = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList(),
                Resources = resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).ToList(),
                Notes = ""
            };
        }

        private static bool MatchesQuery(ManagementSurfaceRow row, string query)
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

        internal class ManagementSurfaceRow
        {
            public string Screen { get; set; }
            public string Surface { get; set; }
            public string Action { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public ManagementSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
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
                        ["screen"] = Screen,
                        ["surface"] = Surface,
                        ["status"] = Status,
                        ["tools"] = Tools
                    };
                }

                return new Dictionary<string, object>
                {
                    ["screen"] = Screen,
                    ["surface"] = Surface,
                    ["action"] = Action,
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
