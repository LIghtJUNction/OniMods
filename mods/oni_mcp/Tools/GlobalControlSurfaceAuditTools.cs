using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class GlobalControlSurfaceAuditTools
    {
        public static McpTool AuditGlobalControlSurfaces()
        {
            return new McpTool
            {
                Name = "global_control_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "hud_control_surface_audit", "pause_menu_surface_audit" },
                Tags = new List<string> { "coverage", "audit", "hud", "pause", "speed", "sandbox", "global", "tools", "resources" },
                Description = "审计 ONI 顶层 HUD 控件、速度控件、暂停菜单和外部账号/生命周期菜单与 MCP 覆盖映射",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 class、surface、Action、工具名或备注筛选", Required = false },
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
                    var toolNames = new HashSet<string>(OniToolRegistry.GetTools().Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
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
                        ["sourceGlobalControlSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示顶层 HUD/速度/沙盒/暂停导航语义已映射到 MCP 工具或资源。",
                            "no_action 表示外部账号、商店、设置或纯客户端偏好等非殖民地内操作；记录用于完成证明，不阻断静态审计。",
                            "review 会阻断 tools_static_audit，直到补工具、补资源或用源码证据标记 no_action。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<GlobalControlSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<GlobalControlSurfaceRow> KnownRows()
        {
            yield return Covered("SpeedControlScreen", "pause_toggle", "TogglePause", "Pause/unpause the simulation", new[] { "game_time", "game_pause", "game_resume", "game_set_speed" }, new[] { "game_time" });
            yield return Covered("SpeedControlScreen", "speed_buttons", "CycleSpeed/SpeedUp/SlowDown", "Set normal/fast/ultra simulation speed", new[] { "game_time", "game_set_speed" }, new[] { "game_time" });
            yield return Covered("SpeedControlScreen", "main_menu_widget", "Escape", "Open/close pause menu and resume game", new[] { "ui_actions_list", "ui_action_trigger", "game_resume" }, new[] { "ui_actions_list" });

            yield return Covered("TopLeftControlScreen", "base_name_display", "", "Read current colony/base name", new[] { "colony_status" }, new[] { "colony_status" });
            yield return Covered("TopLeftControlScreen", "sandbox_toggle", "ToggleSandboxTools", "Enable/disable active sandbox mode when the save permits sandbox", new[] { "sandbox_actions_list", "game_sandbox_mode_set" }, new[] { "sandbox_actions_list" });
            yield return NoAction("TopLeftControlScreen", "klei_item_drop_button", "Klei item drop / external account cosmetic flow; not an in-colony player action surface");

            yield return Covered("PauseScreen", "resume", "Escape", "Close pause screen and resume menu flow", new[] { "ui_actions_list", "ui_action_trigger", "game_resume" }, new[] { "ui_actions_list", "game_time" });
            yield return Covered("PauseScreen", "options", "", "Open options submenu; individual settings are user preference/client configuration", new[] { "ui_actions_list", "ui_action_trigger" }, new[] { "ui_actions_list" });
            yield return Covered("PauseScreen", "colony_summary", "", "Open/read colony summary/report information", new[] { "colony_report", "colony_summary", "ui_management_open" }, new[] { "colony_report", "colony_summary" });
            yield return Covered("PauseScreen", "save_saveas_load", "", "List saves, save/overwrite/save-as and load a selected save with explicit confirmation", new[] { "game_saves_list", "game_save", "game_load_save" }, new[] { "game_saves_list" });
            yield return Covered("PauseScreen", "quit_desktopquit", "", "Quit current game to main menu or desktop, optionally saving first, with explicit confirmation", new[] { "game_time", "game_saves_list", "game_quit" }, new[] { "game_time", "game_saves_list" });
            yield return Covered("PauseScreen", "dlc_activation_buttons", "", "Read DLC save activation state and activate subscribed, user-editable DLC for the current save with backup/reload confirmation", new[] { "game_dlc_activation_list", "game_dlc_activate" }, new[] { "game_dlc_activation_list" });

            yield return NoAction("OptionsMenuScreen", "graphics_audio_game_metrics_feedback_credits", "Options, metrics, feedback and credits are client preferences/external UX, not colony action surfaces.");
            yield return NoAction("LockerMenuScreen", "inventory_duplicants_outfits_claim_items", "Supply Closet and Klei item claims are external account/cosmetic surfaces, not colony action surfaces.");
            yield return NoAction("KleiItemDropScreen", "accept_acknowledge_item_drop", "Klei item drop reveal/acknowledge flow is external account/cosmetic, not colony action surface.");
        }

        private static GlobalControlSurfaceRow Covered(string sourceClass, string surface, string action, string playerSurface, string[] tools, string[] resources)
        {
            return new GlobalControlSurfaceRow
            {
                SourceClass = sourceClass,
                Surface = surface,
                Action = action,
                Status = "covered",
                PlayerSurface = playerSurface,
                Tools = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList(),
                Resources = resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).ToList(),
                Notes = ""
            };
        }

        private static GlobalControlSurfaceRow NoAction(string sourceClass, string surface, string notes)
        {
            return new GlobalControlSurfaceRow
            {
                SourceClass = sourceClass,
                Surface = surface,
                Action = "",
                Status = "no_action",
                PlayerSurface = "",
                Tools = new List<string>(),
                Resources = new List<string>(),
                Notes = notes
            };
        }

        private static bool MatchesQuery(GlobalControlSurfaceRow row, string query)
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

        internal class GlobalControlSurfaceRow
        {
            public string SourceClass { get; set; }
            public string Surface { get; set; }
            public string Action { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public GlobalControlSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
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
                        ["tools"] = Tools,
                        ["notes"] = Notes
                    };
                }

                return new Dictionary<string, object>
                {
                    ["class"] = SourceClass,
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
