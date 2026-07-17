using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class UiMenuSurfaceAuditTools
    {
        public static McpTool AuditUiMenuSurfaces()
        {
            return new McpTool
            {
                Name = "ui_menu_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "overlay_menu_audit", "build_menu_audit", "ui_hotkey_surface_audit" },
                Tags = new List<string> { "coverage", "audit", "ui", "overlay", "build-menu", "hotkey", "tools", "resources" },
                Description = "审计 ONI 覆盖层菜单、建造分类入口和安全 UI hotkey surface 与 MCP 工具/资源覆盖映射",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 kind、Action、surface、工具名或资源名筛选", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "过滤状态：all、covered、review、no_action，默认 all", Required = false, EnumValues = new List<string> { "all", "covered", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 或 full，默认 brief", Required = false, EnumValues = new List<string> { "brief", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 120，最大 300", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").Trim();
                    string status = (args["status"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    string detail = (args["detail"]?.ToString() ?? "brief").Trim().ToLowerInvariant();
                    int limit = ToolUtil.ClampLimit(args, 120, 300);
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
                        .ThenBy(row => row.Kind)
                        .ThenBy(row => row.SourceOrder)
                        .Take(limit)
                        .Select(row => row.ToDictionary(detail == "brief"))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["sourceUiMenuSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示 OverlayMenu/PlanScreen/BuildMenu/UiTools 白名单中的 UI 操作入口已映射到 MCP 工具或资源。",
                            "review 会阻断 tools_static_audit，直到补工具、补资源或用源码证据标记 no_action。",
                            "overlay rows 来源于 OverlayMenu.InitializeToggles；build rows 来源于 UiTools 安全 BuildCategory 白名单和建造规划工具。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<UiMenuSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<UiMenuSurfaceRow> KnownRows()
        {
            int order = 0;
            foreach (var overlay in OverlayRows())
                yield return Covered(order++, "overlay", overlay.Action, overlay.Surface, overlay.View, new[] { "game_control domain=ui uiDomain=action", "navigation_control domain=camera" }, new[] { "game_control domain=ui uiDomain=action", "navigation_control domain=camera" });

            order = 0;
            foreach (string action in BuildCategoryActions())
            {
                string suffix = action.Substring("BuildCategory".Length);
                yield return Covered(
                    order++,
                    "build_category",
                    action,
                    "Open build category " + suffix + ", discover available buildings/materials/facades, then plan and place through building_control",
                    NormalizeBuildCategory(suffix),
                    new[] { "game_control domain=ui uiDomain=action", "building_control domain=planning action=search_defs", "building_control domain=planning action=materials", "building_control domain=planning action=placement_candidates", "building_control domain=planning action=build_area" },
                    new[] { "game_control domain=ui uiDomain=action", "building_control domain=planning action=search_defs", "building_control domain=planning action=materials", "building_control domain=planning action=placement_candidates", "building_control domain=planning action=build_area" });
            }

            yield return Covered(
                0,
                "build_hotkeys",
                "BuildMenuKeyA-Z",
                "Trigger whitelisted build-menu item hotkeys after a build category is open; resolve prefab, material, facade, and lower-left anchors through building_control planning",
                "build_menu_keys",
                new[] { "game_control domain=ui uiDomain=action", "building_control domain=planning action=search_defs", "building_control domain=planning action=materials", "building_control domain=planning action=placement_candidates", "building_control domain=planning action=build_area" },
                new[] { "game_control domain=ui uiDomain=action", "building_control domain=planning action=search_defs", "building_control domain=planning action=materials", "building_control domain=planning action=placement_candidates", "building_control domain=planning action=build_area" });

            yield return Covered(
                0,
                "navigation",
                "Escape/Find/Help/ToggleScreenshotMode",
                "Close active UI, open find/help, and toggle screenshot mode through the safe UI action whitelist",
                "safe_navigation",
                new[] { "game_control domain=ui uiDomain=action" },
                new[] { "game_control domain=ui uiDomain=action" });

            yield return Covered(
                0,
                "fullscreen_panel",
                "AllResourcesScreen",
                "Read resource rows and set per-resource pinned and notification toggles",
                "all_resources_screen",
                new[] { "resources_inventory", "resources_food", "resource_pin_control" },
                new[] { "resources_inventory", "resources_food", "resource_pin_control" });

            yield return Covered(
                1,
                "fullscreen_panel",
                "AllDiagnosticsScreen",
                "Read diagnostic rows, set per-diagnostic display mode, criteria toggles and debug notification suppression",
                "all_diagnostics_screen",
                new[] { "colony_diagnostics", "colony_alerts", "colony_control" },
                new[] { "colony_diagnostics", "colony_alerts", "colony_control" });
        }

        private static IEnumerable<(string Action, string Surface, string View)> OverlayRows()
        {
            yield return ("Overlay1", "Oxygen overlay", "oxygen");
            yield return ("Overlay2", "Power overlay", "power");
            yield return ("Overlay3", "Temperature overlay", "temperature");
            yield return ("Overlay4", "Materials/tile overlay", "materials");
            yield return ("Overlay5", "Light overlay", "light");
            yield return ("Overlay6", "Liquid plumbing overlay", "liquid_conduits");
            yield return ("Overlay7", "Gas plumbing overlay", "gas_conduits");
            yield return ("Overlay8", "Decor overlay", "decor");
            yield return ("Overlay9", "Disease overlay", "disease");
            yield return ("Overlay10", "Crop overlay", "crop");
            yield return ("Overlay11", "Rooms overlay", "rooms");
            yield return ("Overlay12", "Suit overlay", "suit");
            yield return ("Overlay13", "Logic overlay", "logic");
            yield return ("Overlay14", "Conveyor overlay", "solid_conveyor");
            yield return ("Overlay15", "Radiation overlay", "radiation");
        }

        private static IEnumerable<string> BuildCategoryActions()
        {
            return new[]
            {
                "BuildCategoryLadders", "BuildCategoryTiles", "BuildCategoryDoors", "BuildCategoryStorage",
                "BuildCategoryGenerators", "BuildCategoryWires", "BuildCategoryPowerControl",
                "BuildCategoryPlumbingStructures", "BuildCategoryPipes", "BuildCategoryVentilationStructures",
                "BuildCategoryTubes", "BuildCategoryTravelTubes", "BuildCategoryConveyance",
                "BuildCategoryLogicWiring", "BuildCategoryLogicGates", "BuildCategoryLogicSwitches",
                "BuildCategoryLogicConduits", "BuildCategoryCooking", "BuildCategoryFarming",
                "BuildCategoryRanching", "BuildCategoryResearch", "BuildCategoryHygiene",
                "BuildCategoryMedical", "BuildCategoryRecreation", "BuildCategoryFurniture",
                "BuildCategoryDecor", "BuildCategoryOxygen", "BuildCategoryUtilities",
                "BuildCategoryRefining", "BuildCategoryEquipment", "BuildCategoryRocketry"
            };
        }

        private static string NormalizeBuildCategory(string suffix)
        {
            var chars = new List<char>();
            for (int i = 0; i < suffix.Length; i++)
            {
                char c = suffix[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(suffix[i - 1]))
                    chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }

        private static UiMenuSurfaceRow Covered(int order, string kind, string action, string playerSurface, string target, string[] tools, string[] resources)
        {
            return new UiMenuSurfaceRow
            {
                SourceOrder = order,
                Kind = kind,
                Action = action,
                Target = target,
                Status = "covered",
                PlayerSurface = playerSurface,
                Tools = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList(),
                Resources = resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).ToList(),
                Notes = ""
            };
        }

        private static bool MatchesQuery(UiMenuSurfaceRow row, string query)
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

        internal class UiMenuSurfaceRow
        {
            public int SourceOrder { get; set; }
            public string Kind { get; set; }
            public string Action { get; set; }
            public string Target { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public UiMenuSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
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
                        ["kind"] = Kind,
                        ["action"] = Action,
                        ["target"] = Target,
                        ["status"] = Status,
                        ["tools"] = Tools
                    };
                }

                return new Dictionary<string, object>
                {
                    ["sourceOrder"] = SourceOrder,
                    ["kind"] = Kind,
                    ["action"] = Action,
                    ["target"] = Target,
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
