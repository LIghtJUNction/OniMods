using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class SideScreenSurfaceTools
    {
        public static McpTool AuditSideScreenSurfaces()
        {
            return new McpTool
            {
                Name = "side_screen_surfaces_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sidescreen_surface_audit", "ui_surface_audit" },
                Tags = new List<string> { "coverage", "audit", "side-screen", "ui", "tools", "resources" },
                Description = "从运行时类型反射审计 ONI SideScreenContent 子类及辅助侧屏 KScreen 与 MCP 工具/资源覆盖映射，用于发现玩家侧屏操作缺口",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 class、说明、工具名或备注筛选", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "过滤状态：all、covered、review、no_action，默认 all", Required = false, EnumValues = new List<string> { "all", "covered", "review", "no_action" } },
                    ["includeNoAction"] = new McpToolParameter { Type = "boolean", Description = "是否返回纯显示/无玩家操作侧屏，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 200，最大 500", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").Trim();
                    string status = (args["status"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    bool includeNoAction = ToolUtil.GetBool(args, "includeNoAction", false);
                    int limit = ToolUtil.ClampLimit(args, 200, 500);
                    var toolNames = new HashSet<string>(OniToolRegistry.GetVisibleTools().Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
                    var resourceNames = new HashSet<string>(
                        OniResourceRegistry.GetResourceInfos().Select(info => info.Name)
                            .Concat(OniResourceRegistry.GetResourceTemplateInfos().Select(info => info.Name)),
                        StringComparer.OrdinalIgnoreCase);

                    var auditRows = BuildAuditRows(toolNames, resourceNames);
                    var rows = auditRows
                        .Where(row => includeNoAction || row.Status != "no_action")
                        .Where(row => status == "all" || string.IsNullOrEmpty(status) || row.Status == status)
                        .Where(row => string.IsNullOrWhiteSpace(query) || MatchesQuery(row, query))
                        .OrderBy(row => StatusRank(row.Status))
                        .ThenBy(row => row.ClassName)
                        .Take(limit)
                        .Select(row => row.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["runtimeSideScreenTypes"] = auditRows.Count,
                        ["statusFilter"] = status,
                        ["includeNoAction"] = includeNoAction,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["covered"] = rows.Count(row => (string)row["status"] == "covered"),
                            ["review"] = rows.Count(row => (string)row["status"] == "review"),
                            ["no_action"] = rows.Count(row => (string)row["status"] == "no_action")
                        },
                        ["surfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示该侧屏类或辅助侧屏 KScreen 已映射到 MCP 工具或明确的通用工具族。",
                            "review 表示反射发现了侧屏类型，但当前映射仍需人工确认是否有玩家操作语义。",
                            "no_action 表示纯显示、空实现或已由其它主 surface 覆盖，不应阻断完成证明。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static List<SurfaceRow> BuildAuditRows(HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            return RuntimeSideScreenTypes()
                .Select(type => BuildRow(type, toolNames, resourceNames))
                .ToList();
        }

        private static List<Type> RuntimeSideScreenTypes()
        {
            var sideScreenContentType = typeof(SideScreenContent);
            var auxiliaryTypes = new HashSet<string>(AuxiliarySideScreenClassNames(), StringComparer.Ordinal);
            return SafeTypes(sideScreenContentType.Assembly)
                .Where(type => type != null && !type.IsAbstract)
                .Where(type => sideScreenContentType.IsAssignableFrom(type) || auxiliaryTypes.Contains(type.Name))
                .OrderBy(type => type.Name)
                .ToList();
        }

        private static IEnumerable<string> AuxiliarySideScreenClassNames()
        {
            yield return "AssignmentGroupControllerSideScreen";
            yield return "FabricatorSideScreen";
            yield return "OwnablesSecondSideScreen";
            yield return "SelectModuleSideScreen";
            yield return "SelectedRecipeQueueScreen";
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static SurfaceRow BuildRow(Type type, HashSet<string> toolNames, HashSet<string> resourceNames)
        {
            var row = KnownSurface(type.Name) ?? PatternSurface(type.Name) ?? ReviewSurface(type.Name);
            row.ClassName = type.Name;
            row.BaseType = type.BaseType?.Name;
            row.MissingTools = SurfaceAuditUtil.MissingTools(row.Tools, toolNames);
            row.MissingResources = SurfaceAuditUtil.MissingResources(row.Resources, resourceNames);

            if (row.Status != "no_action")
                row.Status = row.MissingTools.Count == 0 && row.MissingResources.Count == 0 ? row.Status : "review";

            return row;
        }

        private static SurfaceRow KnownSurface(string className)
        {
            switch (className)
            {
                case "AccessControlSideScreen":
                    return Covered("Door access permissions", "building_config_control", "building_config_control", "building_config_control");
                case "ActiveRangeSideScreen":
                    return Covered("Activation/deactivation range sliders", "building_control domain=side_surface surface=activation", "building_control domain=side_surface surface=activation", "building_control domain=side_surface surface=activation");
                case "AlarmSideScreen":
                    return Covered("Logic alarm notification settings", "logic_alarm_control", "logic_alarm_control", "logic_alarm_control");
                case "ArtableSelectionSideScreen":
                    return Covered("Art stage/facade selection", "artable_control", "artable_control", "artable_control");
                case "ArtifactAnalysisSideScreen":
                    return Covered("Artifact analysis display and reveal", "building_control domain=side_surface surface=facility", "building_control domain=side_surface surface=facility", "artifacts_list");
                case "AssignableSideScreen":
                    return Covered("Assignable ownership", "dupes_control domain=assignable", "dupes_control domain=assignable", "dupes_control domain=assignable action=list");
                case "AssignmentGroupControllerSideScreen":
                    return Covered("Assignment group membership toggles", "building_control domain=rocket rocketDomain=assignment_group", "building_control domain=rocket rocketDomain=assignment_group", "building_control domain=rocket rocketDomain=assignment_group");
                case "OwnablesSecondSideScreen":
                    return Covered("Slot-specific assignable selection", "dupes_control domain=side_screen", "dupes_control domain=assignable", "dupes_control domain=side_screen action=equipment");
                case "AssignPilotAndCrewSideScreen":
                    return CoveredTools(
                        "Rocket pilot/crew assignment summary and edit-crew entry",
                        new[] { "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=assignment_group" },
                        new[] { "building_control domain=rocket rocketDomain=assignment_group" },
                        new[] { "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=assignment_group" });
                case "AutoPlumberSideScreen":
                    return Covered("Debug AutoPlumber buttons", "game_control", "game_control", "game_control");
                case "AutomatableSideScreen":
                    return Covered("Allow manual delivery/fetching for automatable buildings", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=automation");
                case "BionicSideScreen":
                    return Covered("Bionic upgrade slots", "dupes_control domain=side_screen", "dupes_control domain=assignable", "dupes_control domain=side_screen action=bionic_upgrades");
                case "ButtonMenuSideScreen":
                    return Covered("Generic ISidescreenButtonControl buttons", "building_control domain=side_surface", "building_control domain=side_surface", "building_control domain=side_surface");
                case "CapacityControlSideScreen":
                    return Covered("Capacity controls", "building_config_control", "building_config_control", "building_config_control");
                case "CargoModuleSideScreen":
                    return Covered("Rocket cargo collector status", "building_control domain=rocket rocketDomain=cargo_status action=collectors", null, "building_control domain=rocket rocketDomain=cargo_status action=collectors");
                case "CheckboxListGroupSideScreen":
                    return Covered("Read-only side-screen checklists", "building_control domain=side_surface", null, "side_checklists_list");
                case "ClusterDestinationSideScreen":
                    return CoveredTools(
                        "Rocket destination, round-trip mode and landing pad selection",
                        new[] { "building_control domain=rocket rocketDomain=ops" },
                        new[] { "building_control domain=rocket rocketDomain=ops" },
                        new[] { "building_control domain=rocket rocketDomain=ops" });
                case "ClusterGridWorldSideScreen":
                    return CoveredTools(
                        "Cluster map view-world button",
                        new[] { "world_list", "navigation_control domain=camera" },
                        new[] { "navigation_control domain=camera" },
                        new[] { "world_list" });
                case "ClusterLocationFilterSideScreen":
                    return Covered("Cluster location sensor filter", "cluster_location_sensor_control", "cluster_location_sensor_control", "cluster_location_sensor_control");
                case "CometDetectorSideScreen":
                    return Covered("Comet detector target selection", "comet_detector_control", "comet_detector_control", "comet_detector_control");
                case "CommandModuleSideScreen":
                    return CoveredTools(
                        "Base-game rocket launch conditions and starmap button",
                        new[] { "building_control domain=space_story", "building_control domain=rocket rocketDomain=ops" },
                        new[] { "ui_management_open" },
                        new[] { "building_control domain=space_story", "building_control domain=rocket rocketDomain=ops" });
                case "ComplexFabricatorSideScreen":
                    return CoveredTools(
                        "Fabricator recipe categories and selected recipe queue controls",
                        new[] { "building_control domain=production" },
                        new[] { "building_control domain=production" },
                        new[] { "building_control domain=production" });
                case "ConditionListSideScreen":
                    return Covered("Process condition list", "building_control domain=space_story", null, "building_control domain=space_story action=process_conditions");
                case "ConfigureConsumerSideScreen":
                    return Covered("Configurable consumer option", "configurable_consumer_control", "configurable_consumer_control", "configurable_consumer_control");
                case "CounterSideScreen":
                    return Covered("Counter control", "building_config_control", "building_config_control", "building_config_control");
                case "CritterSensorSideScreen":
                    return Covered("Critter sensor egg/critter count toggles", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=automation");
                case "DispenserSideScreen":
                    return Covered("Dispenser item selection/request", "building_control domain=side_surface surface=facility", "building_control domain=side_surface surface=facility", "dispensers_list");
                case "DoorToggleSideScreen":
                case "SealedDoorSideScreen":
                    return Covered("Door state toggles", "building_config_control", "building_config_control", "building_config_control");
                case "FilterSideScreen":
                case "FlatTagFilterSideScreen":
                case "TagFilterScreen":
                case "TreeFilterableSideScreen":
                    return Covered("Storage/filter selection", "building_control domain=filter", "building_control domain=filter", "building_control domain=filter");
                case "FewOptionSideScreen":
                    return Covered("Few-option side-screen choices", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option");
                case "GeneShufflerSideScreen":
                    return Covered("Gene shuffler control", "gene_shuffler_control", "gene_shuffler_control", "gene_shuffler_control");
                case "GeoTunerSideScreen":
                    return Covered("GeoTuner geyser assignment", "building_control domain=side_surface surface=geo_tuner", "building_control domain=side_surface surface=geo_tuner", "building_control domain=side_surface surface=geo_tuner");
                case "GeneticAnalysisStationSideScreen":
                    return Covered("Botanical analyzer seed permissions", "building_control domain=story_facility kind=genetic_analysis_station", "building_control domain=story_facility kind=genetic_analysis_station", "building_control domain=story_facility kind=genetic_analysis_station");
                case "HarvestModuleSideScreen":
                    return Covered("Rocket harvest module status", "building_control domain=rocket rocketDomain=cargo_status action=harvest_modules", null, "building_control domain=rocket rocketDomain=cargo_status action=harvest_modules");
                case "HabitatModuleSideScreen":
                    return NoAction("IsValidForTarget returns false in current build; superseded by RocketInteriorSectionSideScreen");
                case "HighEnergyParticleDirectionSideScreen":
                    return Covered("Radbolt direction", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option");
                case "IncubatorSideScreen":
                    return Covered("Incubator egg request", "incubator_control", "incubator_control", "incubator_control");
                case "IntSliderSideScreen":
                case "SingleSliderSideScreen":
                case "DualSliderSideScreen":
                case "MultiSliderSideScreen":
                    return Covered("Generic slider controls", "building_config_control", "building_config_control", "building_config_control");
                case "LaunchButtonSideScreen":
                    return Covered("Rocket launch/cancel", "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=ops action=status");
                case "LaunchPadSideScreen":
                    return Covered("Rocket landing pad selection", "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=ops");
                case "LimitValveSideScreen":
                    return Covered("Limit valve amount", "building_config_control", "building_config_control", "building_config_control");
                case "LogicBitSelectorSideScreen":
                    return Covered("Ribbon bit selector", "building_config_control", "building_config_control", "building_config_control");
                case "LogicBroadcastChannelSideScreen":
                    return Covered("Logic broadcast channel", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option", "building_control domain=side_surface surface=option");
                case "LoreBearerSideScreen":
                    return Covered("Lore bearer read button", "building_control domain=side_surface surface=facility", "building_control domain=side_surface surface=facility", "lore_bearers_list");
                case "LureSideScreen":
                    return Covered("Creature lure bait", "creature_lure_control", "creature_lure_control", "creature_lure_control");
                case "MinionTodoSideScreen":
                    return Covered("Duplicant todo list", "dupes_control domain=side_screen", null, "dupes_control domain=side_screen action=todos");
                case "MissileSelectionSideScreen":
                    return Covered("Missile launcher ammunition", "missile_launcher_control", "missile_launcher_control", "missile_launcher_control");
                case "ModuleFlightUtilitySideScreen":
                    return Covered("Rocket flight utility control", "building_control domain=rocket rocketDomain=flight_utility", "building_control domain=rocket rocketDomain=flight_utility", "building_control domain=rocket rocketDomain=flight_utility");
                case "MonumentSideScreen":
                    return Covered("Monument part facade/flip", "monument_part_control", "monument_part_control", "monument_part_control");
                case "NToggleSideScreen":
                    return Covered("Generic N-toggle side-screen", "n_toggle_control", "n_toggle_control", "n_toggle_control");
                case "PixelPackSideScreen":
                    return Covered("Pixel pack colors", "pixel_pack_control", "pixel_pack_control", "pixel_pack_control");
                case "PlanterSideScreen":
                    return Covered("Planting selection", "colony_control domain=bio bioDomain=farming", "colony_control domain=bio bioDomain=farming", "colony_control domain=bio bioDomain=farming");
                case "PlayerControlledToggleSideScreen":
                    return Covered("Player controlled toggle", "building_config_control", "building_config_control", "building_config_control");
                case "PrinterceptorSideScreen":
                    return Covered("Printerceptor control", "building_control domain=story_facility", "building_control domain=story_facility", "building_control domain=story_facility");
                case "ProgressBarSideScreen":
                    return Covered("Generic progress bar", "building_control domain=side_surface", null, "building_control domain=side_surface");
                case "RailGunSideScreen":
                    return Covered("Railgun launch mass", "railgun_control", "railgun_control", "railgun_control");
                case "ReceptacleSideScreen":
                    return Covered("Generic SingleEntityReceptacle request/cancel/remove controls", "building_control domain=receptacle", "building_control domain=receptacle", "building_control domain=receptacle");
                case "SpecialCargoBayClusterSideScreen":
                    return Covered("Special cargo bay entity request/cancel/remove controls", "building_control domain=receptacle", "building_control domain=receptacle", "building_control domain=receptacle");
                case "RelatedEntitiesSideScreen":
                    return Covered("Related entity navigation", "building_control domain=side_surface", "building_control domain=side_surface", "building_control domain=side_surface");
                case "RemoteWorkTerminalSidescreen":
                    return Covered("Remote work dock selection", "building_control domain=story_facility kind=remote_work_terminal", "building_control domain=story_facility kind=remote_work_terminal", "building_control domain=story_facility kind=remote_work_terminal");
                case "ResearchSideScreen":
                    return Covered("Research selection", "colony_control domain=management kind=research action=status", "colony_control domain=management kind=research", "colony_control domain=management kind=research action=status");
                case "RocketInteriorSectionSideScreen":
                    return CoveredTools(
                        "Rocket interior/exterior view navigation",
                        new[] { "building_control domain=rocket rocketDomain=ops", "world_list" },
                        new[] { "navigation_control domain=camera" },
                        new[] { "building_control domain=rocket rocketDomain=ops", "world_list" });
                case "RocketModuleSideScreen":
                    return Covered("Rocket module reordering", "building_control domain=rocket rocketDomain=module", "building_control domain=rocket rocketDomain=module", "building_control domain=rocket rocketDomain=module");
                case "RocketRestrictionSideScreen":
                    return Covered("Rocket console restrictions", "building_control domain=rocket rocketDomain=restriction", "building_control domain=rocket rocketDomain=restriction", "building_control domain=rocket rocketDomain=restriction");
                case "SelfDestructButtonSideScreen":
                    return Covered("Rocket self destruct", "building_control domain=rocket rocketDomain=self_destruct action=list", "building_control domain=rocket rocketDomain=self_destruct action=trigger", "building_control domain=rocket rocketDomain=self_destruct action=list");
                case "SelectModuleSideScreen":
                    return Covered("Rocket module definition selection and material choice", "building_control domain=rocket rocketDomain=module", "building_control domain=rocket rocketDomain=module", "building_control domain=rocket rocketDomain=module");
                case "SelectedRecipeQueueScreen":
                    return Covered("Fabricator selected recipe variant, queue count and infinite toggle", "building_control domain=production", "building_control domain=production", "building_control domain=production");
                case "SingleCheckboxSideScreen":
                    return Covered("Single checkbox controls", "building_config_control", "building_config_control", "building_config_control");
                case "SingleItemSelectionSideScreen":
                case "SingleItemSelectionSideScreenBase":
                    return Covered("StorageTile single item target selection", "building_control domain=tile_selection", "building_control domain=tile_selection", "building_control domain=tile_selection");
                case "SuitLockerSideScreen":
                    return Covered("Suit locker configuration", "building_control domain=side_surface surface=facility", "building_control domain=side_surface surface=facility", "suit_lockers_list");
                case "SummonCrewSideScreen":
                    return Covered("Rocket crew summon/release", "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=crew_request");
                case "TelepadSideScreen":
                    return Covered("Telepad controls and summary links", "building_control domain=side_surface surface=facility", "building_control domain=side_surface surface=facility", "telepads_list");
                case "TelescopeSideScreen":
                    return Covered("Telescope starmap analysis", "building_control domain=space_story", "building_control domain=space_story", "telescopes_list");
                case "TemporalTearSideScreen":
                    return Covered("Temporal tear consume craft", "building_control domain=space_story", "building_control domain=space_story", "temporal_tears_list");
                case "ThresholdSwitchSideScreen":
                case "TemperatureSwitchSideScreen":
                    return Covered("Threshold switch controls", "building_config_control", "building_config_control", "building_config_control");
                case "TimeRangeSideScreen":
                    return Covered("Time range controls", "building_config_control", "building_config_control", "building_config_control");
                case "TimerSideScreen":
                    return Covered("Logic timer controls", "building_config_control", "building_config_control", "building_config_control");
                case "TurboModeSideScreen":
                    return Covered("Liquid tepidizer turbo mode", "turbo_heater_control", "turbo_heater_control", "turbo_heater_control");
                case "ValveSideScreen":
                    return Covered("Valve flow controls", "building_config_control", "building_config_control", "building_config_control");
                case "WarpPortalSideScreen":
                    return Covered("Warp portal control", "building_control domain=space_story", "building_control domain=space_story", "warp_portals_list");
                case "BaseGameImpactorImperativeSideScreen":
                    return NoAction("Vanilla impactor health/time display; no direct player operation beyond MissileSelectionSideScreen ammunition");
                case "NoConfigSideScreen":
                case "RoleStationSideScreen":
                case "FabricatorSideScreen":
                    return NoAction("No valid target or no player operation in current build");
                default:
                    return null;
            }
        }

        private static SurfaceRow PatternSurface(string className)
        {
            if (className.Contains("Cluster") || className.Contains("CommandModule") || className.Contains("RocketInterior") || className.Contains("HabitatModule"))
                return Covered("Rocket/starmap navigation/status surface", "building_control domain=rocket rocketDomain=ops", null, "building_control domain=rocket rocketDomain=ops action=status");
            if (className == "OwnablesSecondSideScreen")
                return Covered("Slot-specific assignable selection", "dupes_control domain=side_screen", "dupes_control domain=assignable", "dupes_control domain=side_screen action=equipment");
            if (className.Contains("Ownables"))
                return Covered("Assignable ownership surface", "dupes_control domain=assignable", "dupes_control domain=assignable", "dupes_control domain=assignable action=list");
            if (className.Contains("Resource") || className.Contains("Details"))
                return NoAction("Read-only detail/status surface covered by world/resources/colony resources or base game UI");
            return null;
        }

        private static SurfaceRow Covered(string surface, string readTool, string writeTool, string resource)
        {
            var tools = new List<string>();
            if (!string.IsNullOrWhiteSpace(readTool))
                tools.Add(readTool);
            if (!string.IsNullOrWhiteSpace(writeTool))
                tools.Add(writeTool);
            return new SurfaceRow
            {
                Status = "covered",
                PlayerSurface = surface,
                CoverageKind = string.IsNullOrWhiteSpace(writeTool) ? "read" : "read_write",
                Tools = tools,
                Resources = string.IsNullOrWhiteSpace(resource) ? new List<string>() : new List<string> { resource },
                Notes = ""
            };
        }

        private static SurfaceRow CoveredTools(string surface, IEnumerable<string> readTools, IEnumerable<string> writeTools, IEnumerable<string> resources)
        {
            var tools = new List<string>();
            if (readTools != null)
                tools.AddRange(readTools.Where(tool => !string.IsNullOrWhiteSpace(tool)));
            if (writeTools != null)
                tools.AddRange(writeTools.Where(tool => !string.IsNullOrWhiteSpace(tool)));

            return new SurfaceRow
            {
                Status = "covered",
                PlayerSurface = surface,
                CoverageKind = writeTools != null && writeTools.Any(tool => !string.IsNullOrWhiteSpace(tool)) ? "read_write" : "read",
                Tools = tools.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Resources = resources == null
                    ? new List<string>()
                    : resources.Where(resource => !string.IsNullOrWhiteSpace(resource)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Notes = ""
            };
        }

        private static SurfaceRow NoAction(string note)
        {
            return new SurfaceRow
            {
                Status = "no_action",
                PlayerSurface = "No MCP operation required",
                CoverageKind = "none",
                Tools = new List<string>(),
                Resources = new List<string>(),
                Notes = note
            };
        }

        private static SurfaceRow ReviewSurface(string className)
        {
            return new SurfaceRow
            {
                Status = "review",
                PlayerSurface = "Unmapped side-screen surface",
                CoverageKind = "unknown",
                Tools = new List<string>(),
                Resources = new List<string>(),
                Notes = $"Review {className} for player-operable controls or mark no_action with evidence"
            };
        }

        private static bool MatchesQuery(SurfaceRow row, string query)
        {
            return JsonConvert.SerializeObject(row.ToDictionary())
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

        internal class SurfaceRow
        {
            public string ClassName { get; set; }
            public string BaseType { get; set; }
            public string Status { get; set; }
            public string PlayerSurface { get; set; }
            public string CoverageKind { get; set; }
            public List<string> Tools { get; set; }
            public List<string> Resources { get; set; }
            public List<string> MissingTools { get; set; }
            public List<string> MissingResources { get; set; }
            public string Notes { get; set; }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["class"] = ClassName,
                    ["baseType"] = BaseType,
                    ["status"] = Status,
                    ["playerSurface"] = PlayerSurface,
                    ["coverageKind"] = CoverageKind,
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
