using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// MCP Tool 注册表
    /// 集中管理所有暴露给 AI 的游戏操作工具
    /// </summary>
    public static class OniToolRegistry
    {
        private static readonly Dictionary<string, McpTool> _tools = new Dictionary<string, McpTool>();
        private static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private static readonly HashSet<string> CoreToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "server_status",
            "logs_tail",
            "mcp_client_capabilities",
            "tools_manifest",
            "tools_search",
            "tools_guide",
            "tools_player_action_coverage",
            "tools_static_audit",
            "tools_call_many",
            "agent_program_execute",
            "agent_pointer_get",
            "agent_pointer_user_mouse_get",
            "agent_pointer_aim_cell",
            "agent_pointer_aim_world",
            "agent_pointer_nudge",
            "agent_pointer_select_tool",
            "agent_pointer_say",
            "agent_pointer_left_click",
            "agent_pointer_hold_left",
            "agent_pointer_jump",
            "agent_pointer_jump_point_set",
            "agent_pointer_jump_point_list",
            "agent_pointer_jump_point_clear",
            "agent_pointer_clear",
            "colony_state_snapshot",
            "edit_mark_request_create",
            "edit_mark_request_list",
            "edit_mark_request_clear",
            "game_time",
            "game_pause",
            "game_resume",
            "game_set_speed",
            "game_red_alert_status",
            "game_red_alert_set",
            "power_summary",
            "rooms_list",
            "thermal_overheat_risk_scan",
            "world_text_map",
            "world_area_snapshot",
            "layout_candidates",
            "area_define",
            "area_get",
            "area_list",
            "area_blocks",
            "area_merge",
            "area_forget",
            "critters_list",
            "dupes_status_check",
            "orders_dig_area",
            "orders_sweep_area",
            "orders_cancel_area",
            "orders_harvest_area",
            "buildings_search_defs",
            "buildings_materials",
            "build_preview"
        };
        private static bool _initialized;
        private static List<McpToolInfo> _cachedCoreToolInfos;
        private static List<McpToolInfo> _cachedAllToolInfos;

        /// <summary>
        /// 初始化所有工具
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            Register(ColonyTools.GetColonyInfo());
            Register(SnapshotTools.GetColonyStateSnapshot());
            Register(ColonyTools.GetDuplicants());
            Register(ColonyTools.GetWorlds());
            Register(ColonyTools.GetResources());
            Register(ToolCatalogTools.GetToolsManifest());
            Register(ToolCatalogTools.SearchTools());
            Register(ToolCatalogTools.GetToolsGuide());
            Register(ToolCoverageTools.GetPlayerActionCoverage());
            Register(ToolCoverageTools.GetStaticAudit());
            Register(SideScreenSurfaceTools.AuditSideScreenSurfaces());
            Register(UserMenuSurfaceAuditTools.AuditUserMenuSurfaces());
            Register(ManagementSurfaceAuditTools.AuditManagementSurfaces());
            Register(ToolMenuSurfaceAuditTools.AuditToolMenuSurfaces());
            Register(UiMenuSurfaceAuditTools.AuditUiMenuSurfaces());
            Register(GlobalControlSurfaceAuditTools.AuditGlobalControlSurfaces());
            Register(NotificationSurfaceAuditTools.AuditNotificationSurfaces());
            Register(ToolBatchTools.CallMany());
            Register(AgentProgramTools.ExecuteProgram());
            Register(DatabaseTools.QueryDatabase());
            Register(AreaTools.DefineArea());
            Register(AreaTools.GetArea());
            Register(AreaTools.ListAreas());
            Register(AreaTools.GenerateAreaBlocks());
            Register(AreaTools.MergeAreas());
            Register(AreaTools.ForgetArea());
            Register(CameraTools.GetCameraView());
            Register(CameraTools.SetActiveWorld());
            Register(CameraTools.SetCameraView());
            Register(CameraTools.MoveCamera());
            Register(CameraTools.SwitchView());
            Register(CameraTools.FocusCell());
            Register(CameraTools.FocusDupe());
            Register(CameraTools.TakeScreenshot());
            Register(AgentPointerTools.GetPointerState());
            Register(AgentPointerTools.GetUserMouse());
            Register(AgentPointerTools.AimCell());
            Register(AgentPointerTools.AimWorld());
            Register(AgentPointerTools.Nudge());
            Register(AgentPointerTools.SelectTool());
            Register(AgentPointerTools.Say());
            Register(AgentPointerTools.LeftClick());
            Register(AgentPointerTools.HoldLeft());
            Register(AgentPointerTools.Jump());
            Register(AgentPointerTools.SetJumpPoint());
            Register(AgentPointerTools.ListJumpPoints());
            Register(AgentPointerTools.ClearJumpPoint());
            Register(AgentPointerTools.ClearPointer());
            Register(UiHintTools.CreateNotification());
            Register(UiHintTools.CreatePopupText());
            Register(UiHintTools.CreateMapMarker());
            Register(UiHintTools.ListMapMarkers());
            Register(UiHintTools.ClearMapMarker());
            Register(UiTools.ListUiActions());
            Register(UiTools.OpenManagementScreen());
            Register(UiTools.TriggerUiAction());
            Register(EditMarkTools.CreateEditMarkRequest());
            Register(EditMarkTools.ListEditMarkRequests());
            Register(EditMarkTools.ClearEditMarkRequest());
            Register(DuplicantTools.GetDupeStatusCheck());
            Register(DuplicantTools.GetDupeDetails());
            Register(DuplicantTools.GetDupeAttributes());
            Register(DuplicantTools.GetDupeNeeds());
            Register(DuplicantTools.ListPersonalPriorities());
            Register(DuplicantTools.SetPersonalPriority());
            Register(DuplicantTools.BatchSetPersonalPriorities());
            Register(DuplicantTools.GetPersonalPrioritySettings());
            Register(DuplicantTools.SetPersonalPrioritySettings());
            Register(DuplicantTools.ListHatOptions());
            Register(DuplicantTools.SetHat());
            Register(DuplicantTools.RenameDupe());
            Register(DuplicantTools.AutoRenameDupes());
            Register(DuplicantTools.MoveDupe());
            Register(DuplicantTools.MoveDupesBatch());
            Register(DuplicantTools.ListDirectCommands());
            Register(DuplicantTools.ListEquipment());
            Register(DuplicantTools.ListSkills());
            Register(DuplicantTools.LearnSkill());
            Register(DuplicantTools.ListAssignables());
            Register(DuplicantTools.SetAssignable());
            Register(DuplicantTools.SetAssignableSlotItem());
            Register(ScheduleTools.GetSchedules());
            Register(ScheduleTools.CreateSchedule());
            Register(ScheduleTools.SetScheduleBlock());
            Register(ScheduleTools.AssignDupeSchedule());
            Register(ScheduleTools.OptimizeSchedules());
            Register(DiagnosticsTools.GetColonyDiagnostics());
            Register(DiagnosticsTools.GetColonyAlerts());
            Register(DiagnosticsTools.ListDiagnosticSettings());
            Register(DiagnosticsTools.SetDiagnosticSettings());
            Register(DiagnosticsTools.SetGlobalAutoDisinfect());
            Register(ColonyReportTools.GetColonyReport());
            Register(ColonyReportTools.GetColonySummary());
            Register(NotificationTools.ListNotifications());
            Register(NotificationTools.ClickNotification());
            Register(NotificationTools.DismissNotification());
            Register(InventoryTools.GetInventory());
            Register(InventoryTools.GetFoodInventory());
            Register(InventoryTools.ListResourcePins());
            Register(InventoryTools.SetResourcePin());
            Register(DietTools.GetDietStatus());
            Register(DietTools.SetDietFood());
            Register(DietTools.ApplyDietPolicy());
            Register(StorageTools.GetStorageList());
            Register(StorageTools.GetStorageDetail());
            Register(StorageTools.SetStorageFilter());
            Register(ReceptacleTools.ListStorageTileSelections());
            Register(ReceptacleTools.SetStorageTileSelection());
            Register(ReceptacleTools.BatchSetStorageTileSelections());
            Register(FilterTools.ListFilters());
            Register(FilterTools.SetSingleFilter());
            Register(FilterTools.SetTreeFilter());
            Register(OptionControlTools.ListOptionControls());
            Register(OptionControlTools.SetDirection());
            Register(OptionControlTools.SetFewOption());
            Register(OptionControlTools.SetBroadcastChannel());
            Register(OptionControlTools.SetRadboltDirection());
            Register(StateControlTools.ListStateControls());
            Register(StateControlTools.SetCapacity());
            Register(StateControlTools.SetCheckbox());
            Register(StateControlTools.SetCounter());
            Register(StateControlTools.SetTimeRange());
            Register(AutomationSideScreenTools.ListAutomatableControls());
            Register(AutomationSideScreenTools.SetAutomatableControl());
            Register(AutomationSideScreenTools.BatchSetAutomatableControls());
            Register(AutomationSideScreenTools.ListCritterSensors());
            Register(AutomationSideScreenTools.SetCritterSensorCounting());
            Register(AutomationSideScreenTools.BatchSetCritterSensors());
            Register(LightTools.ListLights());
            Register(LightTools.SetLightColor());
            Register(MiscSideScreenTools.ListNToggles());
            Register(MiscSideScreenTools.SetNToggle());
            Register(MiscSideScreenTools.ListLogicAlarms());
            Register(MiscSideScreenTools.SetLogicAlarm());
            Register(MiscSideScreenTools.ListTurboHeaters());
            Register(MiscSideScreenTools.SetTurboHeater());
            Register(SpaceBuildingTools.ListCometDetectors());
            Register(SpaceBuildingTools.SetCometDetectorTarget());
            Register(SpaceBuildingTools.ListClusterLocationSensors());
            Register(SpaceBuildingTools.SetClusterLocationSensor());
            Register(PixelPackTools.ListPixelPacks());
            Register(PixelPackTools.SetPixelPackColor());
            Register(PixelPackTools.CopyPixelPackColors());
            Register(GeoTunerTools.ListGeoTuners());
            Register(GeoTunerTools.ListGeoTunerGeysers());
            Register(GeoTunerTools.AssignGeoTuner());
            Register(SpecialBuildingTools.ListArtables());
            Register(SpecialBuildingTools.SetArtableStage());
            Register(SpecialBuildingTools.ListCreatureLures());
            Register(SpecialBuildingTools.SetCreatureLureBait());
            Register(SpecialBuildingTools.ListGeneShufflers());
            Register(SpecialBuildingTools.ControlGeneShuffler());
            Register(StoryFacilityTools.ListPrinterceptors());
            Register(StoryFacilityTools.ControlPrinterceptor());
            Register(StoryFacilityTools.ListRemoteWorkTerminals());
            Register(StoryFacilityTools.SetRemoteWorkDock());
            Register(StoryFacilityTools.ListGeneticAnalysisStations());
            Register(StoryFacilityTools.SetGeneticAnalysisSeed());
            Register(FacilitySideScreenTools.ListDispensers());
            Register(FacilitySideScreenTools.ControlDispenser());
            Register(FacilitySideScreenTools.ListSuitLockers());
            Register(FacilitySideScreenTools.ControlSuitLocker());
            Register(FacilitySideScreenTools.ListLoreBearers());
            Register(FacilitySideScreenTools.PressLoreBearer());
            Register(FacilitySideScreenTools.ListTelepads());
            Register(FacilitySideScreenTools.ControlTelepad());
            Register(FacilitySideScreenTools.ListBionicUpgrades());
            Register(FacilitySideScreenTools.ListMinionTodos());
            Register(FacilitySideScreenTools.ListArtifacts());
            Register(FacilitySideScreenTools.OpenArtifactReveal());
            Register(ReceptacleTools.ListReceptacles());
            Register(ReceptacleTools.ControlReceptacle());
            Register(ReceptacleTools.BatchControlReceptacles());
            Register(SpaceStoryTools.ListWarpPortals());
            Register(SpaceStoryTools.ControlWarpPortal());
            Register(SpaceStoryTools.ListTelescopes());
            Register(SpaceStoryTools.ListStarmapAnalysisTargets());
            Register(SpaceStoryTools.SetStarmapAnalysisTarget());
            Register(SpaceStoryTools.ControlTelescope());
            Register(SpaceStoryTools.ListTemporalTears());
            Register(SpaceStoryTools.ConsumeTemporalTearCraft());
            Register(SpaceStoryTools.ListProcessConditions());
            Register(SpecialBuildingTools.ListMissileLaunchers());
            Register(SpecialBuildingTools.SetMissileAmmunition());
            Register(RocketModuleTools.ListModules());
            Register(RocketModuleTools.ListModuleDefinitions());
            Register(RocketModuleTools.ControlModule());
            Register(SpaceBuildingTools.ListRailGuns());
            Register(SpaceBuildingTools.SetRailGunLaunchMass());
            Register(SpecialBuildingTools.ListMonumentParts());
            Register(SpecialBuildingTools.SetMonumentPart());
            Register(SideScreenButtonTools.ListButtons());
            Register(SideScreenButtonTools.PressButton());
            Register(UserMenuActionTools.ListUserMenuActions());
            Register(UserMenuActionTools.PressUserMenuAction());
            Register(UserMenuActionTools.BatchPressUserMenuActions());
            Register(MaintenanceActionTools.ListMaintenanceActions());
            Register(MaintenanceActionTools.ExecuteMaintenanceAction());
            Register(MaintenanceActionTools.BatchExecuteMaintenanceActions());
            Register(ChecklistTools.ListChecklists());
            Register(RelatedEntityTools.ListRelatedEntities());
            Register(RelatedEntityTools.FocusRelatedEntity());
            Register(ActivationRangeTools.ListActivationRanges());
            Register(ActivationRangeTools.SetActivationRange());
            Register(ActivationRangeTools.BatchSetActivationRanges());
            Register(ProgressBarTools.ListProgressBars());
            Register(GameControlTools.GetGameTime());
            Register(GameControlTools.SetGameSpeed());
            Register(GameControlTools.PauseGame());
            Register(GameControlTools.ResumeGame());
            Register(GameControlTools.GetRedAlertStatus());
            Register(GameControlTools.SetRedAlert());
            Register(GameControlTools.SetSandboxMode());
            Register(GameControlTools.ListSaves());
            Register(GameControlTools.SaveGame());
            Register(GameControlTools.LoadSave());
            Register(GameControlTools.QuitGame());
            Register(GameControlTools.ListDlcActivation());
            Register(GameControlTools.ActivateDlcForSave());
            Register(GameControlTools.GetBuildings());
            Register(GameControlTools.GetBuildingSummary());
            Register(BuildingConfigTools.ListConfigurableBuildings());
            Register(BuildingConfigTools.ListAutomationControls());
            Register(BuildingConfigTools.SetThreshold());
            Register(BuildingConfigTools.SetSlider());
            Register(BuildingConfigTools.SetValveFlow());
            Register(BuildingConfigTools.SetLimitValve());
            Register(BuildingConfigTools.SetLogicTimer());
            Register(BuildingConfigTools.SetLogicRibbonBit());
            Register(BuildingConfigTools.SetDoorState());
            Register(BuildingConfigTools.GetAccessControl());
            Register(BuildingConfigTools.SetAccessControl());
            Register(BuildingConfigTools.CopySettings());
            Register(ConfigBatchTools.BatchSetBuildingConfigs());
            Register(ConfigBatchTools.BatchSetAutomationControls());
            Register(ProductionTools.ListFabricators());
            Register(ProductionTools.ListRecipes());
            Register(ProductionTools.SetQueue());
            Register(ProductionTools.BatchSetQueue());
            Register(ProductionTools.SetMutantSeeds());
            Register(SpecialUserMenuActionTools.ListMutantSeedControls());
            Register(SpecialUserMenuActionTools.SetMutantSeedControl());
            Register(MiscSideScreenTools.ListConfigurableConsumers());
            Register(MiscSideScreenTools.SetConfigurableConsumerOption());
            Register(BuildPlanningTools.SearchBuildables());
            Register(BuildPlanningTools.ListBuildMaterials());
            Register(BuildPlanningTools.PreviewBuild());
            Register(BuildPlanningTools.BuildArea());
            Register(RocketTools.ListRockets());
            Register(RocketTools.GetRocketStatus());
            Register(RocketTools.GetRocketDetail());
            Register(RocketTools.ListSpaceDestinations());
            Register(RocketTools.ListLaunchPads());
            Register(RocketTools.SetRocketDestination());
            Register(RocketTools.SetRocketRoundTrip());
            Register(RocketTools.SetRocketLandingPad());
            Register(RocketTools.RequestRocketLaunch());
            Register(RocketTools.CancelRocketLaunch());
            Register(RocketFlightUtilityTools.ListFlightUtilities());
            Register(RocketFlightUtilityTools.ControlFlightUtility());
            Register(RocketFlightUtilityTools.ListRocketRestrictions());
            Register(RocketFlightUtilityTools.SetRocketRestriction());
            Register(SpecialUserMenuActionTools.ListRocketUsageControls());
            Register(SpecialUserMenuActionTools.SetRocketUsageControl());
            Register(RocketCrewCargoTools.ListCrewRequests());
            Register(RocketCrewCargoTools.SetCrewRequest());
            Register(RocketCrewCargoTools.ListAssignmentGroups());
            Register(RocketCrewCargoTools.SetAssignmentGroupMember());
            Register(RocketCrewCargoTools.ListCargoCollectors());
            Register(RocketCrewCargoTools.ListHarvestModules());
            Register(MiscSideScreenTools.ListSelfDestructModules());
            Register(MiscSideScreenTools.TriggerSelfDestruct());
            Register(OrdersTools.ListPriorities());
            Register(OrdersTools.SetBuildingPriority());
            Register(OrdersTools.SetPriorityArea());
            Register(OrdersTools.DeconstructBuilding());
            Register(OrdersTools.SweepArea());
            Register(OrdersTools.DigArea());
            Register(OrdersTools.MopArea());
            Register(OrdersTools.DisinfectArea());
            Register(OrdersTools.Attack());
            Register(OrdersTools.CancelArea());
            Register(OrdersTools.HarvestArea());
            Register(OrdersTools.CaptureCritters());
            Register(FarmingTools.ListPlanting());
            Register(FarmingTools.ListHarvestables());
            Register(FarmingTools.SetHarvestable());
            Register(FarmingTools.ListSeedCatalog());
            Register(FarmingTools.SetPlanting());
            Register(FarmingTools.BatchSetPlanting());
            Register(FarmingTools.UprootArea());
            Register(RanchingTools.ListCritters());
            Register(RanchingTools.ListDropOffs());
            Register(RanchingTools.ConfigureDropOff());
            Register(RanchingTools.BatchConfigureDropOffs());
            Register(RanchingTools.ListIncubators());
            Register(RanchingTools.ConfigureIncubator());
            Register(RanchingTools.BatchConfigureIncubators());
            Register(MedicalTools.ListPatients());
            Register(MedicalTools.ListClinics());
            Register(MedicalTools.ListDoctorStations());
            Register(MedicalTools.SetClinicThreshold());
            Register(MedicalTools.BatchSetClinicThreshold());
            Register(MedicalTools.AssignMedicalBed());
            Register(OrdersTools.SetBuildingEnabled());
            Register(OrdersTools.SetBuildingToggle());
            Register(OrdersTools.ConfigureManualDelivery());
            Register(OrdersTools.EmptyConduits());
            Register(OrdersTools.CutConduits());
            Register(PowerAndRoomTools.GetPowerSummary());
            Register(PowerAndRoomTools.ListRooms());
            Register(WorldAnalysisTools.GetCellInfo());
            Register(WorldAnalysisTools.GetWorldElementSummary());
            Register(WorldAnalysisTools.GetWorldTextMap());
            Register(WorldAnalysisTools.GetWorldAreaSnapshot());
            Register(WorldAnalysisTools.GetLayoutCandidates());
            Register(WorldAnalysisTools.ScanOverheatRisk());
            Register(SandboxTools.ListSandboxActions());
            Register(SandboxTools.SampleCell());
            Register(SandboxTools.PaintElement());
            Register(SandboxTools.FloodFillElement());
            Register(SandboxTools.SetTemperatureArea());
            Register(SandboxTools.RevealArea());
            Register(SandboxTools.ClearFloorArea());
            Register(SandboxTools.ClearCrittersArea());
            Register(SandboxTools.DestroyArea());
            Register(SandboxTools.SpawnEntity());
            Register(SandboxTools.ListStoryTraits());
            Register(SandboxTools.StampStoryTrait());
            Register(SandboxTools.StressArea());
            Register(SandboxTools.AutoPlumbBuilding());
            Register(ResearchTools.GetResearchStatus());
            Register(ResearchTools.ListResearch());
            Register(ResearchTools.SetResearch());
            Register(ResearchTools.ClearResearch());
            Register(ServerTools.GetMcpStatus());
            Register(ServerTools.GetClientCapabilities());
            Register(ServerTools.CreateSamplingRequest());
            Register(ServerTools.CreateElicitationRequest());
            Register(ServerTools.TailLogs());

            BuildToolInfoCache();
        }

        private static void Register(McpTool tool)
        {
            ToolMetadata.ApplyDefaults(tool);
            _tools[tool.Name] = tool;
            foreach (var alias in tool.Aliases)
            {
                if (!string.IsNullOrEmpty(alias))
                    _aliases[alias] = tool.Name;
            }
        }

        public static List<McpTool> GetTools()
        {
            return _tools.Values.OrderBy(t => t.Group).ThenBy(t => t.Name).ToList();
        }

        public static List<McpTool> GetVisibleTools()
        {
            return _tools.Values.Where(t => !t.Hidden).OrderBy(t => t.Group).ThenBy(t => t.Name).ToList();
        }

        public static bool TryGetTool(string name, out McpTool tool)
        {
            tool = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (_tools.TryGetValue(name, out tool))
                return true;

            string canonicalName;
            return _aliases.TryGetValue(name, out canonicalName) && _tools.TryGetValue(canonicalName, out tool);
        }

        /// <summary>
        /// 获取 Tool 元信息（默认供 tools/list 暴露低 token 核心入口）
        /// </summary>
        public static List<McpToolInfo> GetToolInfos(bool includeAll = false)
        {
            var cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return cached;

            BuildToolInfoCache();
            cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return cached;

            return BuildToolInfos(includeAll);
        }

        private static void BuildToolInfoCache()
        {
            _cachedCoreToolInfos = BuildToolInfos(includeAll: false);
            _cachedAllToolInfos = BuildToolInfos(includeAll: true);
        }

        private static List<McpToolInfo> BuildToolInfos(bool includeAll)
        {
            var infos = new List<McpToolInfo>();
            foreach (var tool in _tools.Values
                .Where(tool => !tool.Hidden && (includeAll || CoreToolNames.Contains(tool.Name)))
                .OrderBy(tool => tool.Group)
                .ThenBy(tool => tool.Name))
            {
                var properties = new Dictionary<string, SchemaProperty>();
                var required = new List<string>();

                if (tool.Parameters != null)
                {
                    foreach (var param in tool.Parameters)
                    {
                        properties[param.Key] = new SchemaProperty
                        {
                            Type = param.Value.Type,
                            Description = param.Value.Description,
                            Enum = param.Value.SchemaEnumValues
                        };
                        if (param.Value.Required)
                            required.Add(param.Key);
                    }
                }

                infos.Add(new McpToolInfo
                {
                    Name = tool.Name,
                    Description = ToolMetadata.FormatDescription(tool),
                    Execution = new ToolExecution { TaskSupport = "optional" },
                    InputSchema = new InputSchema
                    {
                        Properties = properties,
                        Required = required.Count > 0 ? required : null
                    }
                });
            }
            return infos;
        }

        public static int GetDefaultToolInfoCount()
        {
            return _cachedCoreToolInfos?.Count ?? _tools.Values.Count(tool => CoreToolNames.Contains(tool.Name));
        }

        /// <summary>
        /// 调用指定 Tool
        /// </summary>
        public static CallToolResult CallTool(string name, JObject arguments)
        {
            if (!_tools.TryGetValue(name, out var tool))
            {
                if (!_aliases.TryGetValue(name, out var canonicalName) || !_tools.TryGetValue(canonicalName, out tool))
                    return CallToolResult.Error($"Tool not found: {name}");
            }

            try
            {
                return tool.Handler(arguments ?? new JObject());
            }
            catch (Exception ex)
            {
                return CallToolResult.Error($"Tool execution error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tool 定义
    /// </summary>
    public class McpTool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string Mode { get; set; }
        public string Risk { get; set; }
        public bool Hidden { get; set; }
        public List<string> Aliases { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, McpToolParameter> Parameters { get; set; }
        public Func<JObject, CallToolResult> Handler { get; set; }
    }

    public class McpToolParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public List<string> EnumValues { get; set; }

        public List<object> SchemaEnumValues
        {
            get
            {
                if (EnumValues == null)
                    return null;

                var values = new List<object>();
                foreach (var value in EnumValues)
                {
                    if (Type == "integer")
                    {
                        int intValue;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        {
                            values.Add(intValue);
                            continue;
                        }
                    }
                    else if (Type == "number")
                    {
                        double numberValue;
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numberValue))
                        {
                            values.Add(numberValue);
                            continue;
                        }
                    }

                    values.Add(value);
                }

                return values;
            }
        }
    }

    internal static class ToolMetadata
    {
        public static void ApplyDefaults(McpTool tool)
        {
            if (string.IsNullOrEmpty(tool.Group))
                tool.Group = InferGroup(tool.Name);
            if (string.IsNullOrEmpty(tool.Mode))
                tool.Mode = InferMode(tool.Name);
            if (string.IsNullOrEmpty(tool.Risk))
                tool.Risk = InferRisk(tool.Name);
            if (tool.Parameters == null)
                tool.Parameters = new Dictionary<string, McpToolParameter>();
            if (tool.Aliases == null)
                tool.Aliases = new List<string>();
            if (tool.Tags == null)
                tool.Tags = new List<string>();
        }

        public static string FormatDescription(McpTool tool)
        {
            return $"[{tool.Group}/{tool.Mode}/{tool.Risk}] {tool.Description}";
        }

        private static string InferGroup(string name)
        {
            name = (name ?? "").ToLowerInvariant();

            if (name.StartsWith("tools_")) return "tools";
            if (name.StartsWith("server_") || name.StartsWith("logs_") || name.StartsWith("mcp_") || name.Contains("mcp")) return "server";
            if (name.StartsWith("database_")) return "database";
            if (name.StartsWith("research_")) return "research";
            if (name.StartsWith("edit_mark_") || name.StartsWith("ui_")) return "ui";
            if (name.StartsWith("map_")) return "map";
            if (name.StartsWith("sandbox_") || name.StartsWith("debug_")) return "sandbox";
            if (name.StartsWith("rocket") || name.StartsWith("launch_") || name.StartsWith("assignment_group_") || name.Contains("spacecraft")) return "rockets";
            if (name.StartsWith("space_") || name.StartsWith("starmap_") || name.StartsWith("temporal_") || name.StartsWith("warp_")) return "space";
            if (name.StartsWith("story_") || name.StartsWith("lore_") || name.StartsWith("printerceptor") || name.StartsWith("remote_work_") || name.StartsWith("artifact_")) return "story";
            if (name.StartsWith("diet_")) return "diet";
            if (name.StartsWith("game_") || name.Contains("speed") || name.Contains("pause")) return "game";
            if (name.StartsWith("camera_")) return "camera";
            if (name.StartsWith("dupe") || name.StartsWith("assignable") || name.StartsWith("minion_") || name.StartsWith("bionic_") || name.Contains("duplicant")) return "dupes";
            if (name.StartsWith("schedule_")) return "schedules";
            if (name.StartsWith("resources_") || name.StartsWith("storage_") || name.StartsWith("receptacle") || name.Contains("inventory") || name.Contains("food") || name.Contains("resources")) return "resources";
            if (name.StartsWith("filters_")) return "filters";
            if (name.StartsWith("automation_") || name.StartsWith("automatable_") || name.StartsWith("logic_") || name.StartsWith("critter_sensor") || name.StartsWith("comet_detector") || name.StartsWith("cluster_location_sensor")) return "automation";
            if (name.StartsWith("side_") || name.StartsWith("state_") || name.StartsWith("direction_") || name.StartsWith("few_option_") || name.StartsWith("capacity_") || name.StartsWith("checkbox_") || name.StartsWith("time_range_") || name.StartsWith("activation_") || name.StartsWith("progress_") || name.StartsWith("user_menu_") || name.StartsWith("maintenance_") || name.StartsWith("related_") || name.StartsWith("n_toggle")) return "controls";
            if (name.StartsWith("building") || name.StartsWith("buildings_") || name.StartsWith("doors_") || name.StartsWith("access_control_") || name.StartsWith("lights_") || name.StartsWith("pixel_") || name.StartsWith("geo_") || name.StartsWith("dispenser") || name.StartsWith("suit_locker") || name.StartsWith("telepad") || name.Contains("building")) return "buildings";
            if (name.StartsWith("production_") || name.StartsWith("configurable_consumer") || name.StartsWith("mutant_seed")) return "production";
            if (name.StartsWith("orders_") || name.StartsWith("priorities_") || name.StartsWith("conduits_") || name.StartsWith("plants_uproot") || name.Contains("dig") || name.Contains("sweep") || name.Contains("deconstruct")) return "orders";
            if (name.StartsWith("critters_") || name.StartsWith("incubator") || name.StartsWith("creature_lure")) return "ranching";
            if (name.StartsWith("farming_")) return "farming";
            if (name.StartsWith("medical_") || name.StartsWith("doctor_")) return "medical";
            if (name.StartsWith("power_")) return "power";
            if (name.StartsWith("rooms_")) return "rooms";
            if (name.StartsWith("world_") || name.StartsWith("area_") || name.StartsWith("layout_") || name.StartsWith("thermal_") || name.Contains("cell")) return "world";
            if (name.StartsWith("notification") || name.StartsWith("colony_") || name.Contains("colony") || name.Contains("alerts")) return "colony";
            return "misc";
        }

        private static string InferMode(string name)
        {
            if (name.Contains("set_") || name.Contains("rename") || name.Contains("assign") || name.Contains("deconstruct") || name.Contains("sweep") || name.Contains("dig"))
                return "write";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("screenshot") || name.Contains("focus"))
                return "execute";
            return "read";
        }

        private static string InferRisk(string name)
        {
            if (name.Contains("deconstruct") || name.Contains("dig"))
                return "dangerous";
            if (name.Contains("rename") || name.Contains("assign") || name.Contains("set_") || name.Contains("sweep") || name.Contains("launch") || name.Contains("cancel"))
                return "medium";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("focus") || name.Contains("screenshot"))
                return "low";
            return "none";
        }
    }
}
