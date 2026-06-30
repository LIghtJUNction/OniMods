using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ToolMenuSurfaceAuditTools
    {
        public static McpTool AuditToolMenuSurfaces()
        {
            return new McpTool
            {
                Name = "tool_menu_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "toolbar_surfaces_audit", "sandbox_toolbar_audit" },
                Tags = new List<string> { "coverage", "audit", "toolbar", "tool-menu", "orders", "sandbox", "tools", "resources" },
                Description = "审计 ONI 主工具栏和沙盒工具栏按钮与 MCP 工具/资源覆盖映射",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 toolbar、Action、toolName、玩家操作或 MCP 工具名筛选", Required = false },
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
                        .ThenBy(row => row.Toolbar)
                        .ThenBy(row => row.SourceOrder)
                        .Take(limit)
                        .Select(row => row.ToDictionary(detail == "brief"))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["sourceToolMenuSurfaces"] = auditRows.Count,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = auditRows.Count(row => row.Status == "covered"),
                            ["review"] = auditRows.Count(row => row.Status == "review"),
                            ["no_action"] = auditRows.Count(row => row.Status == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示 ToolMenu.CreateBasicTools/CreateSandBoxTools 中的按钮语义已映射到 MCP 工具或资源。",
                            "review 会阻断 tools_static_audit，直到补工具、补资源或用源码证据标记 no_action。",
                            "sourceToolbar/action/toolName 来源于当前 U59 反编译源码中的 ToolMenu 工具栏创建代码。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<ToolMenuSurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return KnownRows().Select(row => row.WithRuntimeStatus(toolNames, resourceNames)).ToList();
        }

        private static IEnumerable<ToolMenuSurfaceRow> KnownRows()
        {
            int order = 0;
            yield return Covered(order++, "basic", "DigTool", "Dig", "Designate dig over an area", new[] { "world_text_map", "orders_control" }, new[] { "world_text_map" });
            yield return Covered(order++, "basic", "CancelTool", "BuildingCancel", "Cancel dig/build/sweep/harvest/attack/capture and other errands over an area", new[] { "world_text_map", "orders_control" }, new[] { "world_text_map" });
            yield return Covered(order++, "basic", "DeconstructTool", "BuildingDeconstruct", "Designate buildings and conduits for deconstruction", new[] { "read_control domain=buildings action=list", "orders_control domain=designation action=deconstruct", "orders_control domain=designation action=cut_conduits" }, new[] { "read_control domain=buildings action=list" });
            yield return Covered(order++, "basic", "PrioritizeTool", "Prioritize", "Set priority on buildings, chores and areas", new[] { "orders_control" }, new[] { "orders_control" });
            yield return Covered(order++, "basic", "DisinfectTool", "Disinfect", "Designate disinfect errands over an area", new[] { "world_text_map", "orders_control" }, new[] { "world_text_map" });
            yield return Covered(order++, "basic", "ClearTool", "Clear", "Designate sweep-to-storage errands over an area", new[] { "world_text_map", "orders_control" }, new[] { "world_text_map" });
            yield return Covered(order++, "basic", "AttackTool", "Attack", "Designate attack targets over an area", new[] { "read_control domain=world action=text_map", "colony_control domain=bio bioDomain=ranching action=critters", "orders_control domain=designation action=attack" }, new[] { "read_control domain=world action=text_map", "colony_control domain=bio bioDomain=ranching action=critters" });
            yield return Covered(order++, "basic", "MopTool", "Mop", "Designate mop errands over an area", new[] { "world_text_map", "orders_control" }, new[] { "world_text_map" });
            yield return Covered(order++, "basic", "CaptureTool", "Capture", "Designate critter capture/wrangle errands", new[] { "colony_control domain=bio bioDomain=ranching action=critters", "orders_control domain=designation action=capture" }, new[] { "colony_control domain=bio bioDomain=ranching action=critters" });
            yield return Covered(order++, "basic", "HarvestTool", "Harvest", "Designate harvest errands over an area", new[] { "colony_control domain=bio bioDomain=farming", "orders_control" }, new[] { "colony_control domain=bio bioDomain=farming" });
            yield return Covered(order++, "basic", "EmptyPipeTool", "EmptyPipe", "Empty pipe/duct/conveyor contents over an area", new[] { "read_control domain=world action=text_map", "orders_control domain=designation action=empty_conduits" }, new[] { "read_control domain=world action=text_map" });
            yield return Covered(order++, "basic", "DisconnectTool", "Disconnect", "Cut wire/pipe/duct/conveyor/travel tube connections over an area", new[] { "read_control domain=world action=text_map", "orders_control domain=designation action=cut_conduits" }, new[] { "read_control domain=world action=text_map" });
            yield return Covered(order++, "basic", "OniMcpEditMarker", "Invalid", "Create an MCP edit marker request from a selected area", new[] { "game_control domain=ui uiDomain=edit_mark" }, new[] { "tools_read_resource" });

            order = 0;
            yield return Covered(order++, "sandbox", "SandboxBrushTool", "SandboxBrush", "Replace cells with selected element, mass, temperature and disease", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxSprinkleTool", "SandboxSprinkle", "Noise-scattered replace cells with selected element settings", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxFloodTool", "SandboxFlood", "Flood-fill connected cells with selected element settings", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxSampleTool", "SandboxSample", "Sample a cell for element/mass/temperature/disease settings", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxHeatTool", "SandboxHeatGun", "Set or add temperature over an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxStressTool", "SandboxStressTool", "Add or remove stress from duplicants in an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxSpawnerTool", "SandboxSpawnEntity", "Spawn entities, items, critters, duplicants or completed buildings", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxClearFloorTool", "SandboxClearFloor", "Remove floor pickupables over an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxDestroyerTool", "SandboxDestroy", "Destroy cell contents over an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxFOWTool", "SandboxReveal", "Reveal fog of war over an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxCritterTool", "SandboxCritterTool", "Remove critters over an area", new[] { "game_control" }, new[] { "game_control" });
            yield return Covered(order++, "sandbox", "SandboxStoryTraitTool", "SandboxStoryTraitTool", "Stamp story trait retrofit templates", new[] { "game_control" }, new[] { "game_control" });
        }

        private static ToolMenuSurfaceRow Covered(int order, string toolbar, string toolName, string action, string playerSurface, string[] tools, string[] resources)
        {
            return new ToolMenuSurfaceRow
            {
                SourceOrder = order,
                Toolbar = toolbar,
                ToolName = toolName,
                Action = action,
                Status = "covered",
                PlayerSurface = playerSurface,
                Tools = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList(),
                Resources = resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).ToList(),
                Notes = ""
            };
        }

        private static bool MatchesQuery(ToolMenuSurfaceRow row, string query)
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

        internal class ToolMenuSurfaceRow
        {
            public int SourceOrder { get; set; }
            public string Toolbar { get; set; }
            public string ToolName { get; set; }
            public string Action { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public ToolMenuSurfaceRow WithRuntimeStatus(HashSet<string> toolNames, HashSet<string> resourceNames)
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
                        ["toolbar"] = Toolbar,
                        ["toolName"] = ToolName,
                        ["action"] = Action,
                        ["status"] = Status,
                        ["tools"] = Tools
                    };
                }

                return new Dictionary<string, object>
                {
                    ["sourceOrder"] = SourceOrder,
                    ["toolbar"] = Toolbar,
                    ["toolName"] = ToolName,
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
