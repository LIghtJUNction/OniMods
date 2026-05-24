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
                    var toolNames = new HashSet<string>(OniToolRegistry.GetTools().Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
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
                    return Covered("Door access permissions", "access_control_get", "access_control_set", "access_control_get");
                case "ActiveRangeSideScreen":
                    return Covered("Activation/deactivation range sliders", "activation_ranges_list", "activation_range_set", "activation_ranges_list");
                case "AlarmSideScreen":
                    return Covered("Logic alarm notification settings", "logic_alarms_list", "logic_alarm_set", "logic_alarms_list");
                case "ArtableSelectionSideScreen":
                    return Covered("Art stage/facade selection", "artables_list", "artable_stage_set", "artables_list");
                case "ArtifactAnalysisSideScreen":
                    return Covered("Artifact analysis display and reveal", "artifacts_list", "artifact_reveal_open", "artifacts_list");
                case "AssignableSideScreen":
                    return Covered("Assignable ownership", "assignables_list", "assignables_set", "assignables_list");
                case "AssignmentGroupControllerSideScreen":
                    return Covered("Assignment group membership toggles", "assignment_groups_list", "assignment_group_member_set", "assignment_groups_list");
                case "OwnablesSecondSideScreen":
                    return Covered("Slot-specific assignable selection", "dupes_equipment_list", "assignable_slot_item_set", "dupes_equipment_list");
                case "AssignPilotAndCrewSideScreen":
                    return CoveredTools(
                        "Rocket pilot/crew assignment summary and edit-crew entry",
                        new[] { "rocket_crew_requests_list", "assignment_groups_list" },
                        new[] { "assignment_group_member_set" },
                        new[] { "rocket_crew_requests_list", "assignment_groups_list" });
                case "AutoPlumberSideScreen":
                    return Covered("Debug AutoPlumber buttons", "sandbox_actions_list", "debug_auto_plumb_building", "sandbox_actions_list");
                case "AutomatableSideScreen":
                    return Covered("Allow manual delivery/fetching for automatable buildings", "automatable_controls_list", "automatable_control_set", "automatable_controls_list");
                case "BionicSideScreen":
                    return Covered("Bionic upgrade slots", "bionic_upgrades_list", "assignable_slot_item_set", "bionic_upgrades_list");
                case "ButtonMenuSideScreen":
                    return Covered("Generic ISidescreenButtonControl buttons", "side_buttons_list", "side_button_press", "side_buttons_list");
                case "CapacityControlSideScreen":
                    return Covered("Capacity controls", "state_controls_list", "capacity_control_set", "state_controls_list");
                case "CargoModuleSideScreen":
                    return Covered("Rocket cargo collector status", "rocket_cargo_collectors_list", null, "rocket_cargo_collectors_list");
                case "CheckboxListGroupSideScreen":
                    return Covered("Read-only side-screen checklists", "side_checklists_list", null, "side_checklists_list");
                case "ClusterDestinationSideScreen":
                    return CoveredTools(
                        "Rocket destination, round-trip mode and landing pad selection",
                        new[] { "rockets_status", "space_destinations_list", "launch_pads_list" },
                        new[] { "rockets_set_destination", "rocket_round_trip_set", "rocket_landing_pad_set" },
                        new[] { "rockets_status", "launch_pads_list" });
                case "ClusterGridWorldSideScreen":
                    return CoveredTools(
                        "Cluster map view-world button",
                        new[] { "world_list", "camera_get_view" },
                        new[] { "camera_set_active_world" },
                        new[] { "world_list" });
                case "ClusterLocationFilterSideScreen":
                    return Covered("Cluster location sensor filter", "cluster_location_sensors_list", "cluster_location_sensor_set", "cluster_location_sensors_list");
                case "CometDetectorSideScreen":
                    return Covered("Comet detector target selection", "comet_detectors_list", "comet_detector_target_set", "comet_detectors_list");
                case "CommandModuleSideScreen":
                    return CoveredTools(
                        "Base-game rocket launch conditions and starmap button",
                        new[] { "process_conditions_list", "rockets_status" },
                        new[] { "ui_management_open" },
                        new[] { "process_conditions_list", "rockets_status" });
                case "ComplexFabricatorSideScreen":
                    return CoveredTools(
                        "Fabricator recipe categories and selected recipe queue controls",
                        new[] { "production_fabricators_list", "production_recipes_list" },
                        new[] { "production_queue_set" },
                        new[] { "production_fabricators_list", "production_recipes_list" });
                case "ConditionListSideScreen":
                    return Covered("Process condition list", "process_conditions_list", null, "process_conditions_list");
                case "ConfigureConsumerSideScreen":
                    return Covered("Configurable consumer option", "configurable_consumers_list", "configurable_consumer_option_set", "configurable_consumers_list");
                case "CounterSideScreen":
                    return Covered("Counter control", "state_controls_list", "logic_counter_set", "state_controls_list");
                case "CritterSensorSideScreen":
                    return Covered("Critter sensor egg/critter count toggles", "critter_sensors_list", "critter_sensor_counting_set", "critter_sensors_list");
                case "DispenserSideScreen":
                    return Covered("Dispenser item selection/request", "dispensers_list", "dispenser_control", "dispensers_list");
                case "DoorToggleSideScreen":
                case "SealedDoorSideScreen":
                    return Covered("Door state toggles", "buildings_config_list", "doors_set_state", "buildings_config_list");
                case "FilterSideScreen":
                case "FlatTagFilterSideScreen":
                case "TagFilterScreen":
                case "TreeFilterableSideScreen":
                    return Covered("Storage/filter selection", "filters_list", "filters_tree_set", "filters_list");
                case "FewOptionSideScreen":
                    return Covered("Few-option side-screen choices", "side_options_list", "few_option_set", "side_options_list");
                case "GeneShufflerSideScreen":
                    return Covered("Gene shuffler control", "gene_shufflers_list", "gene_shuffler_control", "gene_shufflers_list");
                case "GeoTunerSideScreen":
                    return Covered("GeoTuner geyser assignment", "geo_tuners_list", "geo_tuner_assign", "geo_tuners_list");
                case "GeneticAnalysisStationSideScreen":
                    return Covered("Botanical analyzer seed permissions", "genetic_analysis_stations_list", "genetic_analysis_seed_set", "genetic_analysis_stations_list");
                case "HarvestModuleSideScreen":
                    return Covered("Rocket harvest module status", "rocket_harvest_modules_list", null, "rocket_harvest_modules_list");
                case "HabitatModuleSideScreen":
                    return NoAction("IsValidForTarget returns false in current build; superseded by RocketInteriorSectionSideScreen");
                case "HighEnergyParticleDirectionSideScreen":
                    return Covered("Radbolt direction", "side_options_list", "radbolt_direction_set", "side_options_list");
                case "IncubatorSideScreen":
                    return Covered("Incubator egg request", "incubators_list", "incubator_configure", "incubators_list");
                case "IntSliderSideScreen":
                case "SingleSliderSideScreen":
                case "DualSliderSideScreen":
                case "MultiSliderSideScreen":
                    return Covered("Generic slider controls", "buildings_config_list", "buildings_slider_set", "buildings_config_list");
                case "LaunchButtonSideScreen":
                    return Covered("Rocket launch/cancel", "rockets_status", "rockets_request_launch", "rockets_status");
                case "LaunchPadSideScreen":
                    return Covered("Rocket landing pad selection", "launch_pads_list", "rocket_landing_pad_set", "launch_pads_list");
                case "LimitValveSideScreen":
                    return Covered("Limit valve amount", "automation_controls_list", "limit_valves_set", "automation_controls_list");
                case "LogicBitSelectorSideScreen":
                    return Covered("Ribbon bit selector", "automation_controls_list", "logic_ribbon_bit_set", "automation_controls_list");
                case "LogicBroadcastChannelSideScreen":
                    return Covered("Logic broadcast channel", "side_options_list", "logic_broadcast_channel_set", "side_options_list");
                case "LoreBearerSideScreen":
                    return Covered("Lore bearer read button", "lore_bearers_list", "lore_bearer_press", "lore_bearers_list");
                case "LureSideScreen":
                    return Covered("Creature lure bait", "creature_lures_list", "creature_lure_bait_set", "creature_lures_list");
                case "MinionTodoSideScreen":
                    return Covered("Duplicant todo list", "minion_todos_list", null, "minion_todos_list");
                case "MissileSelectionSideScreen":
                    return Covered("Missile launcher ammunition", "missile_launchers_list", "missile_ammunition_set", "missile_launchers_list");
                case "ModuleFlightUtilitySideScreen":
                    return Covered("Rocket flight utility control", "rocket_flight_utilities_list", "rocket_flight_utility_control", "rocket_flight_utilities_list");
                case "MonumentSideScreen":
                    return Covered("Monument part facade/flip", "monument_parts_list", "monument_part_set", "monument_parts_list");
                case "NToggleSideScreen":
                    return Covered("Generic N-toggle side-screen", "n_toggles_list", "n_toggle_set", "n_toggles_list");
                case "PixelPackSideScreen":
                    return Covered("Pixel pack colors", "pixel_packs_list", "pixel_pack_color_set", "pixel_packs_list");
                case "PlanterSideScreen":
                    return Covered("Planting selection", "farming_planting_list", "farming_planting_set", "farming_planting_list");
                case "PlayerControlledToggleSideScreen":
                    return Covered("Player controlled toggle", "buildings_config_list", "buildings_set_toggle", "buildings_config_list");
                case "PrinterceptorSideScreen":
                    return Covered("Printerceptor control", "printerceptors_list", "printerceptor_control", "printerceptors_list");
                case "ProgressBarSideScreen":
                    return Covered("Generic progress bar", "progress_bars_list", null, "progress_bars_list");
                case "RailGunSideScreen":
                    return Covered("Railgun launch mass", "railguns_list", "railgun_launch_mass_set", "railguns_list");
                case "ReceptacleSideScreen":
                    return Covered("Generic SingleEntityReceptacle request/cancel/remove controls", "receptacles_list", "receptacle_control", "receptacles_list");
                case "SpecialCargoBayClusterSideScreen":
                    return Covered("Special cargo bay entity request/cancel/remove controls", "receptacles_list", "receptacle_control", "receptacles_list");
                case "RelatedEntitiesSideScreen":
                    return Covered("Related entity navigation", "related_entities_list", "related_entity_focus", "related_entities_list");
                case "RemoteWorkTerminalSidescreen":
                    return Covered("Remote work dock selection", "remote_work_terminals_list", "remote_work_terminal_dock_set", "remote_work_terminals_list");
                case "ResearchSideScreen":
                    return Covered("Research selection", "research_status", "research_set", "research_status");
                case "RocketInteriorSectionSideScreen":
                    return CoveredTools(
                        "Rocket interior/exterior view navigation",
                        new[] { "rockets_status", "world_list" },
                        new[] { "camera_set_active_world" },
                        new[] { "rockets_status", "world_list" });
                case "RocketModuleSideScreen":
                    return Covered("Rocket module reordering", "rocket_modules_list", "rocket_module_control", "rocket_modules_list");
                case "RocketRestrictionSideScreen":
                    return Covered("Rocket console restrictions", "rocket_restrictions_list", "rocket_restriction_set", "rocket_restrictions_list");
                case "SelfDestructButtonSideScreen":
                    return Covered("Rocket self destruct", "rocket_self_destruct_list", "rocket_self_destruct_trigger", "rocket_self_destruct_list");
                case "SelectModuleSideScreen":
                    return Covered("Rocket module definition selection and material choice", "rocket_module_defs_list", "rocket_module_control", "rocket_module_defs_list");
                case "SelectedRecipeQueueScreen":
                    return Covered("Fabricator selected recipe variant, queue count and infinite toggle", "production_recipes_list", "production_queue_set", "production_recipes_list");
                case "SingleCheckboxSideScreen":
                    return Covered("Single checkbox controls", "state_controls_list", "checkbox_control_set", "state_controls_list");
                case "SingleItemSelectionSideScreen":
                case "SingleItemSelectionSideScreenBase":
                    return Covered("StorageTile single item target selection", "storage_tile_selections_list", "storage_tile_selection_set", "storage_tile_selections_list");
                case "SuitLockerSideScreen":
                    return Covered("Suit locker configuration", "suit_lockers_list", "suit_locker_control", "suit_lockers_list");
                case "SummonCrewSideScreen":
                    return Covered("Rocket crew summon/release", "rocket_crew_requests_list", "rocket_crew_request_set", "rocket_crew_requests_list");
                case "TelepadSideScreen":
                    return Covered("Telepad controls and summary links", "telepads_list", "telepad_control", "telepads_list");
                case "TelescopeSideScreen":
                    return Covered("Telescope starmap analysis", "telescopes_list", "telescope_control", "telescopes_list");
                case "TemporalTearSideScreen":
                    return Covered("Temporal tear consume craft", "temporal_tears_list", "temporal_tear_consume_craft", "temporal_tears_list");
                case "ThresholdSwitchSideScreen":
                case "TemperatureSwitchSideScreen":
                    return Covered("Threshold switch controls", "buildings_config_list", "buildings_threshold_set", "buildings_config_list");
                case "TimeRangeSideScreen":
                    return Covered("Time range controls", "state_controls_list", "time_range_set", "state_controls_list");
                case "TimerSideScreen":
                    return Covered("Logic timer controls", "automation_controls_list", "logic_timer_set", "automation_controls_list");
                case "TurboModeSideScreen":
                    return Covered("Liquid tepidizer turbo mode", "turbo_heaters_list", "turbo_heater_set", "turbo_heaters_list");
                case "ValveSideScreen":
                    return Covered("Valve flow controls", "automation_controls_list", "valves_flow_set", "automation_controls_list");
                case "WarpPortalSideScreen":
                    return Covered("Warp portal control", "warp_portals_list", "warp_portal_control", "warp_portals_list");
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
                return Covered("Rocket/starmap navigation/status surface", "rockets_status", null, "rockets_status");
            if (className == "OwnablesSecondSideScreen")
                return Covered("Slot-specific assignable selection", "dupes_equipment_list", "assignable_slot_item_set", "dupes_equipment_list");
            if (className.Contains("Ownables"))
                return Covered("Assignable ownership surface", "assignables_list", "assignables_set", "assignables_list");
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
