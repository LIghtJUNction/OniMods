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
            "building_control",
            "colony_control",
            "dupes_control",
            "game_control",
            "navigation_control",
            "orders_control",
            "read_control",
            "server_control",
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

            Register(ColonyTools.ControlColony());
            Register(HiddenCompat(ColonyTools.ReadColonyControl()));
            Register(HiddenCompat(ColonyTools.GetColonyInfo()));
            Register(HiddenCompat(SnapshotTools.GetColonyStateSnapshot()));
            Register(HiddenCompat(ColonyTools.GetDuplicants()));
            Register(HiddenCompat(ColonyTools.GetWorlds()));
            Register(HiddenCompat(ColonyTools.GetResources()));
            Register(HiddenCompat(ToolCatalogTools.ControlToolCatalog()));
            Register(HiddenCompat(ToolCatalogTools.GetToolsManifest()));
            Register(HiddenCompat(ToolCatalogTools.SearchTools()));
            Register(HiddenCompat(ToolCatalogTools.GetToolsGuide()));
            Register(HiddenCompat(ToolCoverageTools.GetPlayerActionCoverage()));
            Register(HiddenCompat(ToolCoverageTools.GetStaticAudit()));
            Register(HiddenCompat(DatabaseTools.ControlKnowledgeQuery()));
            Register(HiddenCompat(GuideMechanicsTools.QueryGuideMechanics()));
            Register(HiddenCompat(SurfaceAuditControlTools.ControlSurfaceAudit()));
            Register(HiddenCompat(SideScreenSurfaceTools.AuditSideScreenSurfaces()));
            Register(HiddenCompat(UserMenuSurfaceAuditTools.AuditUserMenuSurfaces()));
            Register(HiddenCompat(ManagementSurfaceAuditTools.AuditManagementSurfaces()));
            Register(HiddenCompat(ToolMenuSurfaceAuditTools.AuditToolMenuSurfaces()));
            Register(HiddenCompat(UiMenuSurfaceAuditTools.AuditUiMenuSurfaces()));
            Register(HiddenCompat(GlobalControlSurfaceAuditTools.AuditGlobalControlSurfaces()));
            Register(HiddenCompat(NotificationSurfaceAuditTools.AuditNotificationSurfaces()));
            Register(HiddenCompat(ToolBatchTools.CallMany()));
            Register(HiddenCompat(AgentProgramTools.ExecuteProgram()));
            Register(HiddenCompat(DatabaseTools.QueryDatabase()));
            Register(HiddenCompat(AreaTools.ControlArea()));
            Register(HiddenCompat(AreaTools.DefineArea()));
            Register(HiddenCompat(AreaTools.GetArea()));
            Register(HiddenCompat(AreaTools.ListAreas()));
            Register(HiddenCompat(AreaTools.GenerateAreaBlocks()));
            Register(HiddenCompat(AreaTools.MergeAreas()));
            Register(HiddenCompat(AreaTools.ForgetArea()));
            Register(NavigationControlTools.ControlNavigation());
            Register(HiddenCompat(CameraTools.ControlCamera()));
            Register(HiddenCompat(CameraTools.GetCameraView()));
            Register(HiddenCompat(CameraTools.SetActiveWorld()));
            Register(HiddenCompat(CameraTools.SetCameraView()));
            Register(HiddenCompat(CameraTools.MoveCamera()));
            Register(HiddenCompat(CameraTools.SwitchView()));
            Register(HiddenCompat(CameraTools.FocusCell()));
            Register(HiddenCompat(CameraTools.FocusDupe()));
            Register(HiddenCompat(CameraTools.TakeScreenshot()));
            Register(HiddenCompat(CameraTools.TakeCoordinateScreenshot()));
            Register(HiddenCompat(AgentPointerTools.Control()));
            Register(HiddenCompat(AgentPointerTools.GetPointerState()));
            Register(HiddenCompat(AgentPointerTools.GetUserMouse()));
            Register(HiddenCompat(AgentPointerTools.AimCell()));
            Register(HiddenCompat(AgentPointerTools.AimWorld()));
            Register(HiddenCompat(AgentPointerTools.Nudge()));
            Register(HiddenCompat(AgentPointerTools.SelectTool()));
            Register(HiddenCompat(AgentPointerTools.Say()));
            Register(HiddenCompat(AgentPointerTools.LeftClick()));
            Register(HiddenCompat(AgentPointerTools.HoldLeft()));
            Register(HiddenCompat(AgentPointerTools.Jump()));
            Register(HiddenCompat(AgentPointerTools.ControlJumpPoint()));
            Register(HiddenCompat(AgentPointerTools.SetJumpPoint()));
            Register(HiddenCompat(AgentPointerTools.ListJumpPoints()));
            Register(HiddenCompat(AgentPointerTools.ClearJumpPoint()));
            Register(HiddenCompat(AgentPointerTools.ClearPointer()));
            Register(HiddenCompat(UiControlTools.ControlUi()));
            Register(HiddenCompat(UiHintTools.ControlUiFeedback()));
            Register(HiddenCompat(UiHintTools.ControlUiHint()));
            Register(HiddenCompat(UiHintTools.CreateNotification()));
            Register(HiddenCompat(UiHintTools.CreatePopupText()));
            Register(HiddenCompat(UiHintTools.ControlMapMarker()));
            Register(HiddenCompat(UiHintTools.CreateMapMarker()));
            Register(HiddenCompat(UiHintTools.ListMapMarkers()));
            Register(HiddenCompat(UiHintTools.ClearMapMarker()));
            Register(HiddenCompat(UiTools.ControlUiAction()));
            Register(HiddenCompat(UiTools.ListUiActions()));
            Register(HiddenCompat(UiTools.OpenManagementScreen()));
            Register(HiddenCompat(UiTools.TriggerUiAction()));
            Register(HiddenCompat(EditMarkTools.ControlEditMarkRequest()));
            Register(HiddenCompat(EditMarkTools.CreateEditMarkRequest()));
            Register(HiddenCompat(EditMarkTools.ListEditMarkRequests()));
            Register(HiddenCompat(EditMarkTools.ClearEditMarkRequest()));
            Register(HiddenCompat(DuplicantTools.ControlDupeInfo()));
            Register(HiddenCompat(DuplicantTools.GetDupeStatusCheck()));
            Register(HiddenCompat(DuplicantTools.GetDupeDetails()));
            Register(HiddenCompat(DuplicantTools.GetDupeAttributes()));
            Register(HiddenCompat(DuplicantTools.GetDupeNeeds()));
            Register(HiddenCompat(DuplicantTools.ControlPersonalPriority()));
            Register(HiddenCompat(DuplicantTools.ListPersonalPriorities()));
            Register(HiddenCompat(DuplicantTools.SetPersonalPriority()));
            Register(HiddenCompat(DuplicantTools.BatchSetPersonalPriorities()));
            Register(HiddenCompat(DuplicantTools.GetPersonalPrioritySettings()));
            Register(HiddenCompat(DuplicantTools.SetPersonalPrioritySettings()));
            Register(HiddenCompat(DuplicantTools.ControlHat()));
            Register(HiddenCompat(DuplicantTools.ListHatOptions()));
            Register(HiddenCompat(DuplicantTools.SetHat()));
            Register(HiddenCompat(DuplicantTools.RenameDupe()));
            Register(HiddenCompat(DuplicantTools.AutoRenameDupes()));
            Register(HiddenCompat(DuplicantTools.ControlDupeCommands()));
            Register(HiddenCompat(DuplicantTools.MoveDupe()));
            Register(HiddenCompat(DuplicantTools.MoveDupesBatch()));
            Register(HiddenCompat(DuplicantTools.ForceDupeAction()));
            Register(HiddenCompat(DuplicantTools.ControlDupeSideScreens()));
            Register(HiddenCompat(DuplicantTools.ListDirectCommands()));
            Register(HiddenCompat(DuplicantTools.ListEquipment()));
            Register(HiddenCompat(DuplicantTools.ControlSkill()));
            Register(HiddenCompat(DuplicantTools.ListSkills()));
            Register(HiddenCompat(DuplicantTools.LearnSkill()));
            Register(HiddenCompat(DuplicantTools.ControlAssignable()));
            Register(HiddenCompat(DuplicantTools.ListAssignables()));
            Register(HiddenCompat(DuplicantTools.SetAssignable()));
            Register(HiddenCompat(DuplicantTools.SetAssignableSlotItem()));
            Register(DuplicantTools.ControlDupes());
            Register(HiddenCompat(ManagementTools.ControlManagement()));
            Register(HiddenCompat(ScheduleTools.ControlSchedule()));
            Register(HiddenCompat(ScheduleTools.GetSchedules()));
            Register(HiddenCompat(ScheduleTools.CreateSchedule()));
            Register(HiddenCompat(ScheduleTools.SetScheduleBlock()));
            Register(HiddenCompat(ScheduleTools.AssignDupeSchedule()));
            Register(HiddenCompat(ScheduleTools.OptimizeSchedules()));
            Register(HiddenCompat(DiagnosticsTools.ControlDiagnostics()));
            Register(HiddenCompat(DiagnosticsTools.GetColonyDiagnostics()));
            Register(HiddenCompat(DiagnosticsTools.GetColonyAlerts()));
            Register(HiddenCompat(DiagnosticsTools.ListDiagnosticSettings()));
            Register(HiddenCompat(DiagnosticsTools.SetDiagnosticSettings()));
            Register(HiddenCompat(DiagnosticsTools.SetGlobalAutoDisinfect()));
            Register(HiddenCompat(ColonyReportTools.ControlColonyReport()));
            Register(HiddenCompat(ColonyReportTools.GetColonyReport()));
            Register(HiddenCompat(ColonyReportTools.GetColonySummary()));
            Register(HiddenCompat(NotificationTools.ControlNotification()));
            Register(HiddenCompat(NotificationTools.ListNotifications()));
            Register(HiddenCompat(NotificationTools.ClickNotification()));
            Register(HiddenCompat(NotificationTools.DismissNotification()));
            Register(ReadTools.ControlRead());
            Register(HiddenCompat(InventoryTools.ReadResourcesControl()));
            Register(HiddenCompat(InventoryTools.GetInventory()));
            Register(HiddenCompat(InventoryTools.GetFoodInventory()));
            Register(HiddenCompat(InventoryTools.SearchItems()));
            Register(HiddenCompat(InventoryTools.ControlResourcePin()));
            Register(HiddenCompat(InventoryTools.ListResourcePins()));
            Register(HiddenCompat(InventoryTools.SetResourcePin()));
            Register(HiddenCompat(InventoryTools.ControlResources()));
            Register(HiddenCompat(DietTools.ControlDiet()));
            Register(HiddenCompat(DietTools.GetDietStatus()));
            Register(HiddenCompat(DietTools.SetDietFood()));
            Register(HiddenCompat(DietTools.ApplyDietPolicy()));
            Register(HiddenCompat(StorageTools.ControlStorageSystem()));
            Register(HiddenCompat(StorageTools.ControlStorage()));
            Register(HiddenCompat(StorageTools.GetStorageList()));
            Register(HiddenCompat(StorageTools.GetStorageDetail()));
            Register(HiddenCompat(StorageTools.SetStorageFilter()));
            Register(HiddenCompat(ReceptacleTools.ControlStorageTileSelection()));
            Register(HiddenCompat(ReceptacleTools.ListStorageTileSelections()));
            Register(HiddenCompat(ReceptacleTools.SetStorageTileSelection()));
            Register(HiddenCompat(ReceptacleTools.BatchSetStorageTileSelections()));
            Register(HiddenCompat(FilterTools.ControlFilter()));
            Register(HiddenCompat(FilterTools.ListFilters()));
            Register(HiddenCompat(FilterTools.SetFilter()));
            Register(HiddenCompat(FilterTools.SetSingleFilter()));
            Register(HiddenCompat(FilterTools.SetTreeFilter()));
            Register(HiddenCompat(OptionControlTools.ControlSideOption()));
            Register(HiddenCompat(OptionControlTools.ListOptionControls()));
            Register(HiddenCompat(OptionControlTools.SetOptionControl()));
            Register(HiddenCompat(OptionControlTools.SetDirection()));
            Register(HiddenCompat(OptionControlTools.SetFewOption()));
            Register(HiddenCompat(OptionControlTools.SetBroadcastChannel()));
            Register(HiddenCompat(OptionControlTools.SetRadboltDirection()));
            Register(HiddenCompat(StateControlTools.ControlState()));
            Register(HiddenCompat(StateControlTools.ListStateControls()));
            Register(HiddenCompat(StateControlTools.SetStateControl()));
            Register(HiddenCompat(StateControlTools.SetCapacity()));
            Register(HiddenCompat(StateControlTools.SetCheckbox()));
            Register(HiddenCompat(StateControlTools.SetCounter()));
            Register(HiddenCompat(StateControlTools.SetTimeRange()));
            Register(HiddenCompat(AutomationSideScreenTools.ControlAutomationSideScreen()));
            Register(HiddenCompat(AutomationSideScreenTools.ListAutomatableControls()));
            Register(HiddenCompat(AutomationSideScreenTools.SetAutomatableControl()));
            Register(HiddenCompat(AutomationSideScreenTools.BatchSetAutomatableControls()));
            Register(HiddenCompat(AutomationSideScreenTools.ListCritterSensors()));
            Register(HiddenCompat(AutomationSideScreenTools.SetCritterSensorCounting()));
            Register(HiddenCompat(AutomationSideScreenTools.BatchSetCritterSensors()));
            Register(HiddenCompat(LightTools.ControlLight()));
            Register(HiddenCompat(LightTools.ListLights()));
            Register(HiddenCompat(LightTools.SetLightColor()));
            Register(HiddenCompat(MiscSideScreenTools.ControlNToggle()));
            Register(HiddenCompat(MiscSideScreenTools.ListNToggles()));
            Register(HiddenCompat(MiscSideScreenTools.SetNToggle()));
            Register(HiddenCompat(MiscSideScreenTools.ControlLogicAlarm()));
            Register(HiddenCompat(MiscSideScreenTools.ListLogicAlarms()));
            Register(HiddenCompat(MiscSideScreenTools.SetLogicAlarm()));
            Register(HiddenCompat(MiscSideScreenTools.ControlTurboHeater()));
            Register(HiddenCompat(MiscSideScreenTools.ListTurboHeaters()));
            Register(HiddenCompat(MiscSideScreenTools.SetTurboHeater()));
            Register(HiddenCompat(MiscSideScreenTools.ControlMiscSideScreen()));
            Register(BuildingControlTools.ControlBuilding());
            Register(HiddenCompat(FacilityTools.ControlFacility()));
            Register(HiddenCompat(SpaceBuildingTools.ControlSpaceBuilding()));
            Register(HiddenCompat(SpaceBuildingTools.ControlCometDetector()));
            Register(HiddenCompat(SpaceBuildingTools.ListCometDetectors()));
            Register(HiddenCompat(SpaceBuildingTools.SetCometDetectorTarget()));
            Register(HiddenCompat(SpaceBuildingTools.ControlClusterLocationSensor()));
            Register(HiddenCompat(SpaceBuildingTools.ListClusterLocationSensors()));
            Register(HiddenCompat(SpaceBuildingTools.SetClusterLocationSensor()));
            Register(HiddenCompat(PixelPackTools.ControlPixelPack()));
            Register(HiddenCompat(PixelPackTools.ListPixelPacks()));
            Register(HiddenCompat(PixelPackTools.SetPixelPackColor()));
            Register(HiddenCompat(PixelPackTools.CopyPixelPackColors()));
            Register(HiddenCompat(VisualControlTools.ControlVisual()));
            Register(HiddenCompat(GeoTunerTools.ControlGeoTuner()));
            Register(HiddenCompat(GeoTunerTools.ListGeoTuners()));
            Register(HiddenCompat(GeoTunerTools.ListGeoTunerGeysers()));
            Register(HiddenCompat(GeoTunerTools.AssignGeoTuner()));
            Register(HiddenCompat(SpecialBuildingTools.ControlSpecialBuilding()));
            Register(HiddenCompat(SpecialBuildingTools.ControlArtable()));
            Register(HiddenCompat(SpecialBuildingTools.ListArtables()));
            Register(HiddenCompat(SpecialBuildingTools.SetArtableStage()));
            Register(HiddenCompat(SpecialBuildingTools.ControlCreatureLure()));
            Register(HiddenCompat(SpecialBuildingTools.ListCreatureLures()));
            Register(HiddenCompat(SpecialBuildingTools.SetCreatureLureBait()));
            Register(HiddenCompat(SpecialBuildingTools.ListGeneShufflers()));
            Register(HiddenCompat(SpecialBuildingTools.ControlGeneShuffler()));
            Register(HiddenCompat(StoryFacilityTools.ListPrinterceptors()));
            Register(HiddenCompat(StoryFacilityTools.ControlStoryFacility()));
            Register(HiddenCompat(StoryFacilityTools.ControlPrinterceptor()));
            Register(HiddenCompat(StoryFacilityTools.ListPoiTechUnlocks()));
            Register(HiddenCompat(StoryFacilityTools.ControlPoiTechUnlock()));
            Register(HiddenCompat(StoryFacilityTools.ControlRemoteWorkTerminal()));
            Register(HiddenCompat(StoryFacilityTools.ListRemoteWorkTerminals()));
            Register(HiddenCompat(StoryFacilityTools.SetRemoteWorkDock()));
            Register(HiddenCompat(StoryFacilityTools.ControlGeneticAnalysisStation()));
            Register(HiddenCompat(StoryFacilityTools.ListGeneticAnalysisStations()));
            Register(HiddenCompat(StoryFacilityTools.SetGeneticAnalysisSeed()));
            Register(HiddenCompat(FacilitySideScreenTools.ControlFacilitySideScreen()));
            Register(HiddenCompat(FacilitySideScreenTools.ListDispensers()));
            Register(HiddenCompat(FacilitySideScreenTools.ControlDispenser()));
            Register(HiddenCompat(FacilitySideScreenTools.ListSuitLockers()));
            Register(HiddenCompat(FacilitySideScreenTools.ControlSuitLocker()));
            Register(HiddenCompat(FacilitySideScreenTools.ListLoreBearers()));
            Register(HiddenCompat(FacilitySideScreenTools.PressLoreBearer()));
            Register(HiddenCompat(FacilitySideScreenTools.ListTelepads()));
            Register(HiddenCompat(FacilitySideScreenTools.ControlTelepad()));
            Register(HiddenCompat(FacilitySideScreenTools.ListBionicUpgrades()));
            Register(HiddenCompat(FacilitySideScreenTools.ListMinionTodos()));
            Register(HiddenCompat(FacilitySideScreenTools.ListArtifacts()));
            Register(HiddenCompat(FacilitySideScreenTools.OpenArtifactReveal()));
            Register(HiddenCompat(ReceptacleTools.ListReceptacles()));
            Register(HiddenCompat(ReceptacleTools.ControlReceptacle()));
            Register(HiddenCompat(ReceptacleTools.BatchControlReceptacles()));
            Register(HiddenCompat(SpaceStoryTools.ControlSpaceStory()));
            Register(HiddenCompat(SpaceStoryTools.ListWarpPortals()));
            Register(HiddenCompat(SpaceStoryTools.ControlWarpPortal()));
            Register(HiddenCompat(SpaceStoryTools.ListTelescopes()));
            Register(HiddenCompat(SpaceStoryTools.ListStarmapAnalysisTargets()));
            Register(HiddenCompat(SpaceStoryTools.SetStarmapAnalysisTarget()));
            Register(HiddenCompat(SpaceStoryTools.ControlTelescope()));
            Register(HiddenCompat(SpaceStoryTools.ListTemporalTears()));
            Register(HiddenCompat(SpaceStoryTools.ConsumeTemporalTearCraft()));
            Register(HiddenCompat(SpaceStoryTools.ListProcessConditions()));
            Register(HiddenCompat(SpecialBuildingTools.ControlMissileLauncher()));
            Register(HiddenCompat(SpecialBuildingTools.ListMissileLaunchers()));
            Register(HiddenCompat(SpecialBuildingTools.SetMissileAmmunition()));
            Register(HiddenCompat(RocketModuleTools.ListModules()));
            Register(HiddenCompat(RocketModuleTools.ListModuleDefinitions()));
            Register(HiddenCompat(RocketModuleTools.ControlModule()));
            Register(HiddenCompat(SpaceBuildingTools.ControlRailGun()));
            Register(HiddenCompat(SpaceBuildingTools.ListRailGuns()));
            Register(HiddenCompat(SpaceBuildingTools.SetRailGunLaunchMass()));
            Register(HiddenCompat(SpecialBuildingTools.ControlMonumentPart()));
            Register(HiddenCompat(SpecialBuildingTools.ListMonumentParts()));
            Register(HiddenCompat(SpecialBuildingTools.SetMonumentPart()));
            Register(HiddenCompat(GenericSideSurfaceTools.ControlSideSurface()));
            Register(HiddenCompat(SideScreenButtonTools.ListButtons()));
            Register(HiddenCompat(SideScreenButtonTools.PressButton()));
            Register(HiddenCompat(UserMenuActionTools.ControlUserAction()));
            Register(HiddenCompat(UserMenuActionTools.ControlUserMenuAction()));
            Register(HiddenCompat(UserMenuActionTools.ListUserMenuActions()));
            Register(HiddenCompat(UserMenuActionTools.PressUserMenuAction()));
            Register(HiddenCompat(UserMenuActionTools.BatchPressUserMenuActions()));
            Register(HiddenCompat(MaintenanceActionTools.ControlMaintenanceAction()));
            Register(HiddenCompat(MaintenanceActionTools.ListMaintenanceActions()));
            Register(HiddenCompat(MaintenanceActionTools.ExecuteMaintenanceAction()));
            Register(HiddenCompat(MaintenanceActionTools.BatchExecuteMaintenanceActions()));
            Register(HiddenCompat(ChecklistTools.ListChecklists()));
            Register(HiddenCompat(RelatedEntityTools.ListRelatedEntities()));
            Register(HiddenCompat(RelatedEntityTools.FocusRelatedEntity()));
            Register(HiddenCompat(ActivationRangeTools.ControlActivationRange()));
            Register(HiddenCompat(ActivationRangeTools.ListActivationRanges()));
            Register(HiddenCompat(ActivationRangeTools.SetActivationRange()));
            Register(HiddenCompat(ActivationRangeTools.BatchSetActivationRanges()));
            Register(HiddenCompat(ProgressBarTools.ListProgressBars()));
            Register(HiddenCompat(GameControlTools.GetGameTime()));
            Register(HiddenCompat(GameControlTools.ControlGameSpeed()));
            Register(HiddenCompat(GameControlTools.SetGameSpeed()));
            Register(HiddenCompat(GameControlTools.PauseGame()));
            Register(HiddenCompat(GameControlTools.ResumeGame()));
            Register(HiddenCompat(GameControlTools.ControlGameState()));
            Register(HiddenCompat(GameControlTools.GetRedAlertStatus()));
            Register(HiddenCompat(GameControlTools.SetRedAlert()));
            Register(HiddenCompat(GameControlTools.SetSandboxMode()));
            Register(HiddenCompat(GameControlTools.ControlGameSave()));
            Register(HiddenCompat(GameControlTools.ListSaves()));
            Register(HiddenCompat(GameControlTools.SaveGame()));
            Register(HiddenCompat(GameControlTools.LoadSave()));
            Register(HiddenCompat(GameControlTools.QuitGame()));
            Register(HiddenCompat(GameControlTools.ControlDlcActivation()));
            Register(HiddenCompat(GameControlTools.ListDlcActivation()));
            Register(HiddenCompat(GameControlTools.ActivateDlcForSave()));
            Register(GameControlTools.ControlGame());
            Register(HiddenCompat(GameControlTools.ControlBuildingsRead()));
            Register(HiddenCompat(GameControlTools.GetBuildings()));
            Register(HiddenCompat(GameControlTools.GetBuildingSummary()));
            Register(HiddenCompat(BuildingConfigTools.ControlBuildingConfig()));
            Register(HiddenCompat(BuildingConfigTools.ListConfigurableBuildings()));
            Register(HiddenCompat(BuildingConfigTools.ListAutomationControls()));
            Register(HiddenCompat(BuildingConfigTools.SetThreshold()));
            Register(HiddenCompat(BuildingConfigTools.SetSlider()));
            Register(HiddenCompat(BuildingConfigTools.SetValveFlow()));
            Register(HiddenCompat(BuildingConfigTools.SetLimitValve()));
            Register(HiddenCompat(BuildingConfigTools.SetLogicTimer()));
            Register(HiddenCompat(BuildingConfigTools.SetLogicRibbonBit()));
            Register(HiddenCompat(BuildingConfigTools.SetDoorState()));
            Register(HiddenCompat(BuildingConfigTools.GetAccessControl()));
            Register(HiddenCompat(BuildingConfigTools.SetAccessControl()));
            Register(HiddenCompat(BuildingConfigTools.CopySettings()));
            Register(HiddenCompat(ConfigBatchTools.BatchSetBuildingConfigs()));
            Register(HiddenCompat(ConfigBatchTools.BatchSetAutomationControls()));
            Register(HiddenCompat(ProductionTools.ListFabricators()));
            Register(HiddenCompat(ProductionTools.ListRecipes()));
            Register(HiddenCompat(ProductionTools.ControlQueue()));
            Register(HiddenCompat(ProductionTools.SetQueue()));
            Register(HiddenCompat(ProductionTools.BatchSetQueue()));
            Register(HiddenCompat(ProductionTools.SetMutantSeeds()));
            Register(HiddenCompat(SpecialUserMenuActionTools.ControlMutantSeed()));
            Register(HiddenCompat(SpecialUserMenuActionTools.ListMutantSeedControls()));
            Register(HiddenCompat(SpecialUserMenuActionTools.SetMutantSeedControl()));
            Register(HiddenCompat(MiscSideScreenTools.ControlConfigurableConsumer()));
            Register(HiddenCompat(MiscSideScreenTools.ListConfigurableConsumers()));
            Register(HiddenCompat(MiscSideScreenTools.SetConfigurableConsumerOption()));
            Register(HiddenCompat(BuildPlanningTools.ControlBuildPlanning()));
            Register(HiddenCompat(BuildPlanningTools.SearchBuildables()));
            Register(HiddenCompat(BuildPlanningTools.ListBuildMaterials()));
            Register(HiddenCompat(BuildPlanningTools.PreviewBuild()));
            Register(HiddenCompat(BuildPlanningTools.AutoConnectUtility()));
            Register(HiddenCompat(BuildPlanningTools.FindPlacementCandidates()));
            Register(HiddenCompat(BuildPlanningTools.BuildArea()));
            Register(HiddenCompat(RocketTools.ListRockets()));
            Register(HiddenCompat(RocketTools.GetRocketStatus()));
            Register(HiddenCompat(RocketTools.GetRocketDetail()));
            Register(HiddenCompat(RocketTools.ListSpaceDestinations()));
            Register(HiddenCompat(RocketTools.ListLaunchPads()));
            Register(HiddenCompat(RocketTools.ControlRocketOps()));
            Register(HiddenCompat(RocketTools.SetRocketDestination()));
            Register(HiddenCompat(RocketTools.ControlRocketFlight()));
            Register(HiddenCompat(RocketTools.SetRocketRoundTrip()));
            Register(HiddenCompat(RocketTools.SetRocketLandingPad()));
            Register(HiddenCompat(RocketTools.RequestRocketLaunch()));
            Register(HiddenCompat(RocketTools.CancelRocketLaunch()));
            Register(HiddenCompat(RocketFlightUtilityTools.ControlFlightUtility()));
            Register(HiddenCompat(RocketFlightUtilityTools.ListFlightUtilities()));
            Register(HiddenCompat(RocketFlightUtilityTools.ControlRocketRestriction()));
            Register(HiddenCompat(RocketFlightUtilityTools.ListRocketRestrictions()));
            Register(HiddenCompat(RocketFlightUtilityTools.SetRocketRestriction()));
            Register(HiddenCompat(SpecialUserMenuActionTools.ControlRocketUsage()));
            Register(HiddenCompat(SpecialUserMenuActionTools.ListRocketUsageControls()));
            Register(HiddenCompat(SpecialUserMenuActionTools.SetRocketUsageControl()));
            Register(HiddenCompat(RocketCrewCargoTools.ControlCrewRequest()));
            Register(HiddenCompat(RocketCrewCargoTools.ListCrewRequests()));
            Register(HiddenCompat(RocketCrewCargoTools.SetCrewRequest()));
            Register(HiddenCompat(RocketCrewCargoTools.ControlAssignmentGroup()));
            Register(HiddenCompat(RocketCrewCargoTools.ListAssignmentGroups()));
            Register(HiddenCompat(RocketCrewCargoTools.SetAssignmentGroupMember()));
            Register(HiddenCompat(RocketCrewCargoTools.ControlCargoStatus()));
            Register(HiddenCompat(RocketCrewCargoTools.ListCargoCollectors()));
            Register(HiddenCompat(RocketCrewCargoTools.ListHarvestModules()));
            Register(HiddenCompat(MiscSideScreenTools.ControlSelfDestruct()));
            Register(HiddenCompat(MiscSideScreenTools.ListSelfDestructModules()));
            Register(HiddenCompat(MiscSideScreenTools.TriggerSelfDestruct()));
            Register(HiddenCompat(RocketSystemControlTools.ControlRocketSystem()));
            Register(OrdersTools.ControlOrders());
            Register(HiddenCompat(OrdersTools.ControlPriority()));
            Register(HiddenCompat(OrdersTools.ListPriorities()));
            Register(HiddenCompat(OrdersTools.SetBuildingPriority()));
            Register(HiddenCompat(OrdersTools.SetPriorityArea()));
            Register(HiddenCompat(OrdersTools.DesignationControl()));
            Register(HiddenCompat(OrdersTools.DeconstructBuilding()));
            Register(HiddenCompat(OrdersTools.AreaAction()));
            Register(HiddenCompat(OrdersTools.SweepArea()));
            Register(HiddenCompat(OrdersTools.DigArea()));
            Register(HiddenCompat(OrdersTools.MopArea()));
            Register(HiddenCompat(OrdersTools.DisinfectArea()));
            Register(HiddenCompat(OrdersTools.Attack()));
            Register(HiddenCompat(OrdersTools.CancelArea()));
            Register(HiddenCompat(OrdersTools.HarvestArea()));
            Register(HiddenCompat(OrdersTools.CaptureCritters()));
            Register(HiddenCompat(BioControlTools.ControlBio()));
            Register(HiddenCompat(FarmingTools.ControlPlanting()));
            Register(HiddenCompat(FarmingTools.ListPlanting()));
            Register(HiddenCompat(FarmingTools.ListHarvestables()));
            Register(HiddenCompat(FarmingTools.SetHarvestable()));
            Register(HiddenCompat(FarmingTools.ListSeedCatalog()));
            Register(HiddenCompat(FarmingTools.SetPlanting()));
            Register(HiddenCompat(FarmingTools.BatchSetPlanting()));
            Register(HiddenCompat(FarmingTools.UprootArea()));
            Register(HiddenCompat(RanchingTools.ReadRanchingControl()));
            Register(HiddenCompat(RanchingTools.ListCritters()));
            Register(HiddenCompat(RanchingTools.ControlDropOff()));
            Register(HiddenCompat(RanchingTools.ListDropOffs()));
            Register(HiddenCompat(RanchingTools.ConfigureDropOff()));
            Register(HiddenCompat(RanchingTools.BatchConfigureDropOffs()));
            Register(HiddenCompat(RanchingTools.ControlIncubator()));
            Register(HiddenCompat(RanchingTools.ListIncubators()));
            Register(HiddenCompat(RanchingTools.ConfigureIncubator()));
            Register(HiddenCompat(RanchingTools.BatchConfigureIncubators()));
            Register(HiddenCompat(RanchingTools.ControlRanching()));
            Register(HiddenCompat(MedicalTools.ControlMedical()));
            Register(HiddenCompat(MedicalTools.ListPatients()));
            Register(HiddenCompat(MedicalTools.ControlClinic()));
            Register(HiddenCompat(MedicalTools.ListClinics()));
            Register(HiddenCompat(MedicalTools.ListDoctorStations()));
            Register(HiddenCompat(MedicalTools.SetClinicThreshold()));
            Register(HiddenCompat(MedicalTools.BatchSetClinicThreshold()));
            Register(HiddenCompat(MedicalTools.AssignMedicalBed()));
            Register(HiddenCompat(OrdersTools.SetBuildingEnabled()));
            Register(HiddenCompat(OrdersTools.SetBuildingToggle()));
            Register(HiddenCompat(OrdersTools.ConfigureManualDelivery()));
            Register(HiddenCompat(OrdersTools.EmptyConduits()));
            Register(HiddenCompat(OrdersTools.CutConduits()));
            Register(HiddenCompat(PowerAndRoomTools.InfrastructureReadControl()));
            Register(HiddenCompat(PowerAndRoomTools.GetPowerSummaryCompat()));
            Register(HiddenCompat(PowerAndRoomTools.GetBuildingPowerPortsCompat()));
            Register(HiddenCompat(PowerAndRoomTools.ListRoomsCompat()));
            Register(HiddenCompat(WorldAnalysisTools.ReadWorldControl()));
            Register(HiddenCompat(WorldAnalysisTools.GetCellInfo()));
            Register(HiddenCompat(WorldAnalysisTools.GetWorldElementSummary()));
            Register(HiddenCompat(WorldAnalysisTools.GetWorldTextMap()));
            Register(HiddenCompat(WorldAnalysisTools.GetWorldAreaSnapshot()));
            Register(HiddenCompat(WorldSearchTools.SearchWorld()));
            Register(HiddenCompat(WorldSearchTools.SearchCells()));
            Register(HiddenCompat(WorldSearchTools.SearchObjects()));
            Register(HiddenCompat(WorldAnalysisTools.GetLayoutCandidates()));
            Register(HiddenCompat(WorldAnalysisTools.ScanOverheatRisk()));
            Register(HiddenCompat(SandboxTools.ControlSandbox()));
            Register(HiddenCompat(SandboxTools.ReadControl()));
            Register(HiddenCompat(SandboxTools.ListSandboxActions()));
            Register(HiddenCompat(SandboxTools.SampleCell()));
            Register(HiddenCompat(SandboxTools.ReplaceMapPattern()));
            Register(HiddenCompat(SandboxTools.AreaControl()));
            Register(HiddenCompat(SandboxTools.EntityControl()));
            Register(HiddenCompat(SandboxTools.PaintElement()));
            Register(HiddenCompat(SandboxTools.FloodFillElement()));
            Register(HiddenCompat(SandboxTools.SetTemperatureArea()));
            Register(HiddenCompat(SandboxTools.RevealArea()));
            Register(HiddenCompat(SandboxTools.ClearFloorArea()));
            Register(HiddenCompat(SandboxTools.ClearCrittersArea()));
            Register(HiddenCompat(SandboxTools.DestroyArea()));
            Register(HiddenCompat(SandboxTools.SpawnEntity()));
            Register(HiddenCompat(SandboxTools.ListStoryTraits()));
            Register(HiddenCompat(SandboxTools.StampStoryTrait()));
            Register(HiddenCompat(SandboxTools.StressArea()));
            Register(HiddenCompat(SandboxTools.AutoPlumbBuilding()));
            Register(HiddenCompat(ResearchTools.GetResearchStatus()));
            Register(HiddenCompat(ResearchTools.ControlResearch()));
            Register(HiddenCompat(ResearchTools.ListResearch()));
            Register(HiddenCompat(ResearchTools.SetResearch()));
            Register(HiddenCompat(ResearchTools.ClearResearch()));
            Register(ServerTools.ControlServer());
            Register(HiddenCompat(ServerTools.DiagnosticsControl()));
            Register(HiddenCompat(ServerTools.GetMcpStatus()));
            Register(HiddenCompat(ServerTools.GetClientCapabilities()));
            Register(HiddenCompat(ServerTools.ControlClientRequest()));
            Register(HiddenCompat(ServerTools.CreateSamplingRequest()));
            Register(HiddenCompat(ServerTools.CreateElicitationRequest()));
            Register(HiddenCompat(ServerTools.TailLogs()));

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

        private static McpTool HiddenCompat(McpTool tool)
        {
            if (tool != null)
                tool.Hidden = true;
            return tool;
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
