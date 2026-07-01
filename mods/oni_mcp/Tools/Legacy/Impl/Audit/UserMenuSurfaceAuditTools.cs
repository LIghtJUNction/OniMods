using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class UserMenuSurfaceAuditTools
    {
        public static McpTool AuditUserMenuSurfaces()
        {
            return new McpTool
            {
                Name = "user_menu_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "context_menu_surfaces_audit", "user_menu_coverage_audit" },
                Tags = new List<string> { "coverage", "audit", "user-menu", "context-menu", "tools", "resources" },
                Description = "审计 ONI 源码中玩家 UserMenu/context-menu 按钮来源与 MCP 工具/资源覆盖映射，并标注是否来自 U59 OnRefreshUserMenuDelegate 元数据",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 class、玩家操作、工具名或备注筛选", Required = false },
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
                        .ThenBy(row => row.SourceClass)
                        .Take(limit)
                        .Select(row => row.ToDictionary(detail == "brief"))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["sourceUserMenuSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action"),
                            ["u59RefreshUserMenuDelegateSources"] = U59RefreshUserMenuDelegateSources.Count,
                            ["delegateSourcesCovered"] = auditRows.Count(row => U59RefreshUserMenuDelegateSources.Contains(row.SourceClass)),
                            ["auxiliaryUserMenuSources"] = auditRows.Count(row => !U59RefreshUserMenuDelegateSources.Contains(row.SourceClass))
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示源码 UserMenu 按钮已映射到直接 user-menu action 或语义等价专用 MCP 工具。",
                            "review 会阻断 tools_static_audit，直到补工具或用证据标记 no_action。",
                            "sourceClass 来源于当前 U59 反编译源码中的 userMenu.AddButton/ButtonInfo surface。",
                            "sourceEvidence=OnRefreshUserMenuDelegate 表示该类在 U59 元数据中声明了 UserMenu 刷新委托；auxiliary 表示 state-machine/static/non-delegate user-menu 来源。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<UserMenuSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<UserMenuSurfaceRow> KnownRows()
        {
            yield return Covered("AutoDisinfectable", "Enable/disable auto-disinfect", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("BottleEmptier", "Allow/deny manual pump delivery", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("BuildingEnabledButton", "Enable/disable building", "building_config_control", "building_config_control");
            yield return Covered("Butcherable", "Meatify/cancel meatify critter", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("CancellableMove", "Cancel grouped pickupable move delivery", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Capturable", "Capture/cancel capture critter", "colony_control domain=bio bioDomain=ranching action=critters", "orders_control domain=designation action=capture");
            yield return Covered("CargoBay", "Empty cargo bay storage", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("CargoBayCluster", "Empty cluster cargo bay storage", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("Carvable", "Carve/cancel carve", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Clearable", "Sweep-to-storage/cancel sweep", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("ComplexFabricator", "Accept/reject mutant seeds", "building_control domain=production action=mutant_seed_list", "building_control domain=production action=mutant_seed_set");
            yield return Covered("Compostable", "Compost/cancel compost", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("ConnectionManager", "Reconnect/disconnect geothermal controller", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Constructable", "Cancel construction", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("CopyBuildingSettings", "Copy building settings", "building_config_control", "building_config_control");
            yield return Covered("Deconstructable", "Mark/cancel deconstruction", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Demolishable", "Demolish/cancel demolition", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Desalinator", "Early empty salt", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("Diggable", "Cancel dig", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("DirectionControl", "Change allowed workable direction", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option");
            yield return Covered("DropAllWorkable", "Empty storage/cancel empty storage", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Dumpable", "Dump/cancel dump", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("FactionAlignment", "Attack/cancel attack", "read_control domain=world action=text_map", "orders_control domain=designation action=attack");
            yield return Covered("FishFeeder", "Accept/reject mutant seeds", "building_control domain=production action=mutant_seed_list", "building_control domain=production action=mutant_seed_set");
            yield return Covered("HarvestDesignatable", "Harvest when ready/cancel harvest when ready", "colony_control domain=bio bioDomain=farming", "colony_control domain=bio bioDomain=farming");
            yield return Covered("HiveHarvestMonitor", "Empty/cancel Beeta hive harvest", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("LightColorMenu", "Set light color", "light_control", "light_control");
            yield return Covered("ManualDeliveryKG", "Pause/resume manual delivery", "building_config_control", "orders_control domain=designation action=manual_delivery");
            yield return Covered("Moppable", "Cancel mop", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Movable", "Move/cancel pickupable move", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("MoveToLocationMonitor", "Move duplicant to location", "dupes_control domain=side_screen", "dupes_control domain=command");
            yield return Covered("Navigator", "Draw navigation paths and follow camera", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Repairable", "Allow/cancel auto-repair", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("RocketUsageRestriction", "Rocket interior building controlled/uncontrolled", "building_control domain=rocket rocketDomain=usage", "building_control domain=rocket rocketDomain=usage");
            yield return Covered("SpiceGrinder", "Accept/reject mutant seeds", "building_control domain=production action=mutant_seed_list", "building_control domain=production action=mutant_seed_set");
            yield return Covered("SubstanceChunk", "Release element", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("SuitEquipper", "Unequip duplicant equipment", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("SuitMarker", "Suit checkpoint traversal policy", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Switch", "Toggle manual switch", "building_config_control", "building_config_control");
            yield return Covered("Tinkerable", "Allow/disallow tinker operation", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
            yield return Covered("Toilet", "Early clean toilet", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("TravelTubeEntrance", "Set transit tube wax use", "building_control domain=side_surface surface=maintenance", "building_control domain=side_surface surface=maintenance");
            yield return Covered("Uprootable", "Uproot/cancel uproot", "building_control domain=side_surface surface=user_menu", "building_control domain=side_surface surface=user_menu");
        }

        private static readonly HashSet<string> U59RefreshUserMenuDelegateSources = new HashSet<string>(StringComparer.Ordinal)
        {
            "AutoDisinfectable",
            "BottleEmptier",
            "BuildingEnabledButton",
            "Butcherable",
            "Capturable",
            "CargoBay",
            "CargoBayCluster",
            "Carvable",
            "Clearable",
            "ComplexFabricator",
            "Compostable",
            "ConnectionManager",
            "Constructable",
            "CopyBuildingSettings",
            "Deconstructable",
            "Demolishable",
            "Diggable",
            "DirectionControl",
            "DropAllWorkable",
            "Dumpable",
            "FactionAlignment",
            "HarvestDesignatable",
            "LightColorMenu",
            "ManualDeliveryKG",
            "Moppable",
            "Navigator",
            "Repairable",
            "SuitEquipper",
            "SuitMarker",
            "Switch",
            "Tinkerable",
            "Toilet",
            "TravelTubeEntrance",
            "Uprootable"
        };

        private static UserMenuSurfaceRow Covered(string sourceClass, string surface, string readTool, string writeTool)
        {
            var tools = new List<string>();
            if (!string.IsNullOrWhiteSpace(readTool))
                tools.Add(readTool);
            if (!string.IsNullOrWhiteSpace(writeTool))
                tools.Add(writeTool);

            return new UserMenuSurfaceRow
            {
                SourceClass = sourceClass,
                Status = "covered",
                PlayerSurface = surface,
                Tools = tools,
                Resources = string.IsNullOrWhiteSpace(readTool) ? new List<string>() : new List<string> { readTool },
                SourceEvidence = U59RefreshUserMenuDelegateSources.Contains(sourceClass) ? "OnRefreshUserMenuDelegate" : "auxiliary_user_menu_source",
                Notes = U59RefreshUserMenuDelegateSources.Contains(sourceClass)
                    ? "Present in U59 metadata as EventSystem.IntraObjectHandler<T> OnRefreshUserMenuDelegate."
                    : "Auxiliary user-menu source from state-machine/static or non-delegate UI flow; covered by specialized MCP tool or generic action mapper."
            };
        }

        private static bool MatchesQuery(UserMenuSurfaceRow row, string query)
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

        internal class UserMenuSurfaceRow
        {
            public string SourceClass { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string SourceEvidence { get; set; }
            public string Notes { get; set; }

            public UserMenuSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
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
                        ["status"] = Status,
                        ["surface"] = PlayerSurface,
                        ["tools"] = Tools,
                        ["sourceEvidence"] = SourceEvidence
                    };
                }

                return new Dictionary<string, object>
                {
                    ["class"] = SourceClass,
                    ["status"] = Status,
                    ["playerSurface"] = PlayerSurface,
                    ["tools"] = Tools,
                    ["resources"] = Resources,
                    ["missingTools"] = MissingTools,
                    ["missingResources"] = MissingResources,
                    ["sourceEvidence"] = SourceEvidence,
                    ["notes"] = Notes
                };
            }
        }
    }
}
