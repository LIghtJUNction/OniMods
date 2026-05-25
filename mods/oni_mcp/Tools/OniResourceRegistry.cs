using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// Maps stable MCP resource URIs to live ONI state snapshots.
    /// </summary>
    public static class OniResourceRegistry
    {
        private static readonly List<OniResource> _resources = new List<OniResource>
        {
            Resource("oni://colony/status", "colony_status", "殖民地状态", "周期、复制人数、世界数量、速度和暂停状态。", "colony_status"),
            Resource("oni://colony/diagnostics", "colony_diagnostics", "殖民地诊断", "缺氧、断粮、过热等殖民地诊断结果。", "colony_diagnostics"),
            Resource("oni://colony/alerts", "colony_alerts", "殖民地警报", "当前游戏警报和通知。", "colony_alerts"),
            Resource("oni://colony/diagnostic-settings", "colony_diagnostic_settings_list", "殖民地诊断设置", "AllDiagnosticsScreen 诊断显示模式、子条件启用状态和 Debug 通知禁用状态。", "colony_diagnostic_settings_list"),
            Resource("oni://colony/report", "colony_report", "殖民地报告", "殖民地报告。", "colony_report"),
            Resource("oni://colony/summary", "colony_summary", "殖民地摘要", "面向行动规划的殖民地摘要。", "colony_summary"),
            Resource("oni://colony/notifications", "notifications_list", "通知列表", "当前 HUD NotificationScreen/NotificationManager 通知、消息、聚焦目标和可清除状态。", "notifications_list"),
            Resource("oni://world/list", "world_list", "世界列表", "已加载世界和当前激活世界。", "world_list"),
            Resource("oni://world/elements", "world_element_summary", "世界元素摘要", "当前世界元素质量和温度摘要。", "world_element_summary"),
            Resource("oni://camera/view", "camera_get_view", "相机视图", "当前相机位置、缩放、激活世界和屏幕尺寸。", "camera_get_view"),
            Resource("oni://resources/inventory", "resources_inventory", "资源库存", "资源库存摘要。", "resources_inventory"),
            Resource("oni://resources/food", "resources_food", "食物库存", "食物库存和保质信息。", "resources_food"),
            Resource("oni://resources/pins", "resources_pins_list", "资源面板固定和通知", "AllResourcesScreen 资源行固定显示和通知开关状态。", "resources_pins_list"),
            Resource("oni://diet/status", "diet_status", "饮食权限", "Consumables 管理屏中的复制人饮食/药品/电池可消费权限和库存。", "diet_status"),
            Resource("oni://storage/list", "resources_storage_list", "储存列表", "储存建筑和过滤器列表。", "resources_storage_list"),
            Resource("oni://storage/tile-selections", "storage_tile_selections_list", "储存砖目标物品", "SingleItemSelectionSideScreen / StorageTile 目标物品和可选物品。", "storage_tile_selections_list"),
            Resource("oni://filters/controls", "filters_list", "过滤器控件", "气/液/固体单选过滤器、元素传感器和树形/平铺多选过滤器。", "filters_list"),
            Resource("oni://controls/options", "side_options_list", "选项型侧屏控件", "工作方向、少量选项、逻辑广播频道和辐射粒子方向控件。", "side_options_list"),
            Resource("oni://controls/state", "state_controls_list", "状态型侧屏控件", "容量上限、单 checkbox、逻辑计数器和时间范围传感器控件。", "state_controls_list"),
            Resource("oni://controls/activation-ranges", "activation_ranges_list", "启停双阈值控件", "ActiveRangeSideScreen / IActivationRangeTarget 双阈值控件。", "activation_ranges_list"),
            Resource("oni://controls/progress-bars", "progress_bars_list", "侧屏进度条", "ProgressBarSideScreen / IProgressBarSideScreen 只读进度条状态。", "progress_bars_list"),
            Resource("oni://controls/buttons", "side_buttons_list", "通用侧屏按钮", "实现 ISidescreenButtonControl 的通用侧屏按钮入口。", "side_buttons_list"),
            Resource("oni://controls/user-menu-actions", "user_menu_actions_list", "对象用户菜单操作", "对象 UserMenu/context-menu 按钮映射：清扫、维修、堆肥、倒空、雕刻等非侧屏操作。", "user_menu_actions_list"),
            Resource("oni://controls/maintenance-actions", "maintenance_actions_list", "维护类用户菜单操作", "需要状态机或槽位参数的玩家维护操作：厕所清洁、淡化器清空、运输管蜡、蜂巢清空、货仓倒空、复制人卸装。", "maintenance_actions_list"),
            Resource("oni://controls/checklists", "side_checklists_list", "侧屏清单", "实现 ICheckboxListGroupControl 的故事任务、条件和设施清单。", "side_checklists_list"),
            Resource("oni://controls/related-entities", "related_entities_list", "关联对象", "实现 IRelatedEntities 的侧屏关联对象和可点击跳转目标。", "related_entities_list"),
            Resource("oni://controls/n-toggles", "n_toggles_list", "多选侧屏控件", "实现 INToggleSideScreenControl 的多选侧屏控件。", "n_toggles_list"),
            Resource("oni://automation/logic-alarms", "logic_alarms_list", "逻辑报警器", "Logic Alarm 通知名称、提示、类型、暂停和镜头跳转设置。", "logic_alarms_list"),
            Resource("oni://automation/automatable", "automatable_controls_list", "自动化专用搬运", "AutomatableSideScreen 只允许自动化/允许手动搬运状态。", "automatable_controls_list"),
            Resource("oni://automation/critter-sensors", "critter_sensors_list", "小动物计数传感器", "CritterSensorSideScreen 小动物/蛋计数开关、阈值和当前计数。", "critter_sensors_list"),
            Resource("oni://automation/comet-detectors", "comet_detectors_list", "彗星探测器", "Comet Detector/Space Scanner 当前探测目标和可选择目标。", "comet_detectors_list"),
            Resource("oni://automation/cluster-location-sensors", "cluster_location_sensors_list", "星图位置传感器", "LogicClusterLocationSensor 空太空和星体/POI 坐标过滤设置。", "cluster_location_sensors_list"),
            Resource("oni://buildings/lights", "lights_list", "灯光控件", "灯光建筑、发光参数和可选颜色预设。", "lights_list"),
            Resource("oni://buildings/turbo-heaters", "turbo_heaters_list", "液体加热器涡轮模式", "Liquid Tepidizer TurboModeSideScreen 功耗和开关状态。", "turbo_heaters_list"),
            Resource("oni://buildings/pixel-packs", "pixel_packs_list", "Pixel Pack 控件", "Pixel Pack 当前逻辑值、四面板 active/standby 颜色和颜色预设。", "pixel_packs_list"),
            Resource("oni://geotuners", "geo_tuners_list", "GeoTuner", "GeoTuner 当前/未来目标喷泉和调谐分配状态。", "geo_tuners_list"),
            Resource("oni://geotuners/geysers", "geo_tuner_geysers_list", "GeoTuner 喷泉目标", "GeoTuner 可选择喷泉、研究状态、可见性和分配数量。", "geo_tuner_geysers_list"),
            Resource("oni://buildings/artables", "artables_list", "艺术建筑外观", "艺术建筑当前外观和可选择外观阶段。", "artables_list"),
            Resource("oni://buildings/monument-parts", "monument_parts_list", "纪念碑部件外观", "纪念碑部件当前外观、可选外观和部件类型。", "monument_parts_list"),
            Resource("oni://ranching/lures", "creature_lures_list", "生物诱饵站", "生物诱饵站当前诱饵、可选诱饵和库存。", "creature_lures_list"),
            Resource("oni://buildings/gene-shufflers", "gene_shufflers_list", "Gene Shuffler", "Gene Shuffler 分配、工作完成、消耗和充能请求状态。", "gene_shufflers_list"),
            Resource("oni://story/printerceptors", "printerceptors_list", "Printerceptor", "Printerceptor passcode、拦截充能、打印界面和 databank 状态。", "printerceptors_list"),
            Resource("oni://buildings/remote-work-terminals", "remote_work_terminals_list", "远程工作终端", "Remote Work Terminal 当前/未来 dock 和同世界可选 dock。", "remote_work_terminals_list"),
            Resource("oni://farming/genetic-analysis-stations", "genetic_analysis_stations_list", "Botanical Analyzer", "Botanical Analyzer 可分析种子、允许/禁用状态和库存。", "genetic_analysis_stations_list"),
            Resource("oni://buildings/dispensers", "dispensers_list", "分发器", "DispenserSideScreen 可分发物品、当前选择和分发请求状态。", "dispensers_list"),
            Resource("oni://buildings/receptacles", "receptacles_list", "实体陈列/插槽", "ReceptacleSideScreen / SpecialCargoBayClusterSideScreen / SingleEntityReceptacle 通用实体请求、取消和移除状态。", "receptacles_list"),
            Resource("oni://buildings/suit-lockers", "suit_lockers_list", "太空服柜", "SuitLockerSideScreen 配置、请求、存储装备和掉落能力状态。", "suit_lockers_list"),
            Resource("oni://story/lore-bearers", "lore_bearers_list", "LoreBearer", "LoreBearerSideScreen 可阅读/已阅读对象和按钮状态。", "lore_bearers_list"),
            Resource("oni://story/telepads", "telepads_list", "Printing Pod / Telepad", "TelepadSideScreen 移民、研究、技能提示和胜利条件状态。", "telepads_list"),
            Resource("oni://story/artifacts", "artifacts_list", "Artifact Analysis", "ArtifactAnalysisSideScreen 已分析 artifact、场上 artifact 和分析站状态。", "artifacts_list"),
            Resource("oni://story/warp-portals", "warp_portals_list", "Warp Portal", "WarpPortalSideScreen 等待传送、传送中、冷却和分配状态。", "warp_portals_list"),
            Resource("oni://story/temporal-tears", "temporal_tears_list", "Temporal Tear", "TemporalTearSideScreen 裂隙开启、消耗状态和可进入火箭。", "temporal_tears_list"),
            Resource("oni://space/telescopes", "telescopes_list", "Telescope", "TelescopeSideScreen 建筑状态和当前星图分析目标。", "telescopes_list"),
            Resource("oni://space/analysis-targets", "starmap_analysis_targets_list", "星图分析目标", "星图目的地分析状态和可选择望远镜目标。", "starmap_analysis_targets_list"),
            Resource("oni://diagnostics/process-conditions", "process_conditions_list", "通用过程条件", "ConditionListSideScreen/IProcessConditionSet 条件状态、文本和 tooltip。", "process_conditions_list"),
            Resource("oni://dupes/bionic-upgrades", "bionic_upgrades_list", "仿生人升级槽", "BionicSideScreen 仿生人升级槽、分配和安装状态；写入使用 assignable_slot_item_set。", "bionic_upgrades_list"),
            Resource("oni://rockets/missile-launchers", "missile_launchers_list", "导弹发射器", "导弹发射器弹药允许状态。", "missile_launchers_list"),
            Resource("oni://rockets/modules", "rocket_modules_list", "火箭模块", "火箭模块顺序、移除、替换和添加能力状态。", "rocket_modules_list"),
            Resource("oni://rockets/module-defs", "rocket_module_defs_list", "火箭模块定义", "SelectModuleSideScreen 可选择火箭模块定义和条件状态。", "rocket_module_defs_list"),
            Resource("oni://rockets/launch-pads", "launch_pads_list", "火箭发射台", "LaunchPadSideScreen 发射台、已停靠火箭和可降落火箭。", "launch_pads_list"),
            Resource("oni://rockets/flight-utilities", "rocket_flight_utilities_list", "火箭飞行模块实用操作", "ModuleFlightUtilitySideScreen 清空/投放、自动投放、目标和复制人选择状态。", "rocket_flight_utilities_list"),
            Resource("oni://rockets/restrictions", "rocket_restrictions_list", "火箭控制台限制", "RocketRestrictionSideScreen 地面/太空使用限制状态。", "rocket_restrictions_list"),
            Resource("oni://rockets/usage-controls", "rocket_usage_controls_list", "火箭内部建筑使用限制", "火箭内部建筑是否受 RocketControlStation 限制的玩家菜单状态。", "rocket_usage_controls_list"),
            Resource("oni://rockets/crew-requests", "rocket_crew_requests_list", "火箭乘员召集", "SummonCrewSideScreen 乘员召集/释放状态、登船人数和驾驶员状态。", "rocket_crew_requests_list"),
            Resource("oni://rockets/assignment-groups", "assignment_groups_list", "分配组成员", "AssignmentGroupControllerSideScreen 分配组成员状态和复制人成员开关。", "assignment_groups_list"),
            Resource("oni://rockets/cargo-collectors", "rocket_cargo_collectors_list", "火箭货舱收集器", "CargoModuleSideScreen 星图货舱收集模块容量、库存和收集进度。", "rocket_cargo_collectors_list"),
            Resource("oni://rockets/harvest-modules", "rocket_harvest_modules_list", "火箭钻探模块", "HarvestModuleSideScreen 太空钻探模块钻探状态和钻石库存。", "rocket_harvest_modules_list"),
            Resource("oni://rockets/railguns", "railguns_list", "轨道炮", "轨道炮发射质量、库存和辐射粒子能量状态。", "railguns_list"),
            Resource("oni://rockets/self-destruct", "rocket_self_destruct_list", "火箭自毁", "SelfDestructButtonSideScreen 可自毁火箭舱体。", "rocket_self_destruct_list"),
            Resource("oni://buildings/defs", "buildings_search_defs", "可建造建筑定义", "建造菜单建筑定义、分类、材料需求、可用外观和搜索结果。", "buildings_search_defs"),
            Resource("oni://buildings/materials", "buildings_materials", "可用建造材料", "指定建筑当前世界可用的合法建造材料，按库存排序；用于 material=auto 或显式材料选择。", "buildings_materials"),
            Resource("oni://buildings/configurables", "buildings_config_list", "可配置建筑", "支持启用、开关、阈值、门状态、门禁和补料的建筑。", "buildings_config_list"),
            Resource("oni://automation/controls", "automation_controls_list", "自动化控件", "逻辑/电力相关玩家可配置控件：端口、开关、阈值、阀门、计时器、ribbon bit。", "automation_controls_list"),
            Resource("oni://production/fabricators", "production_fabricators_list", "生产制作站", "制作站/精炼/厨房等配方队列、当前订单和运行状态。", "production_fabricators_list"),
            Resource("oni://production/recipes", "production_recipes_list", "生产配方", "制作站 ComplexRecipe 配方、材料、产物、解锁和队列数量。", "production_recipes_list"),
            Resource("oni://production/mutant-seed-controls", "mutant_seed_controls_list", "突变种子接收开关", "制作站、鱼喂食器和香料研磨器的接受/拒收突变种子玩家菜单开关。", "mutant_seed_controls_list"),
            Resource("oni://production/configurable-consumers", "configurable_consumers_list", "可配置消费者", "ConfigureConsumerSideScreen 当前选项、可选项和消耗材料。", "configurable_consumers_list"),
            Resource("oni://rockets/status", "rockets_status", "火箭状态", "Spaced Out 火箭和基础版航天器状态。", "rockets_status"),
            Resource("oni://research/status", "research_status", "研究状态", "当前研究状态。", "research_status"),
            Resource("oni://schedules", "schedule_list", "日程", "复制人日程。", "schedule_list"),
            Resource("oni://dupes", "dupes_list", "复制人", "复制人列表和基本状态。", "dupes_list"),
            Resource("oni://dupes/priorities", "dupes_priorities_list", "复制人个人优先级", "Priorities/Jobs 管理屏中复制人对各 ChoreGroup 的个人工作优先级。", "dupes_priorities_list"),
            Resource("oni://dupes/priority-settings", "dupes_priority_settings_list", "复制人优先级设置", "Jobs/Priorities 管理屏全局高级模式开关、默认重置行为和重置后优先级状态。", "dupes_priority_settings_get"),
            Resource("oni://dupes/skills", "dupes_skills_list", "复制人技能", "Skills 管理屏中的复制人技能点、已学技能和可学习技能。", "dupes_skills_list"),
            Resource("oni://dupes/hats", "dupes_hats_list", "复制人帽子", "Skills 管理屏中的当前帽子、目标帽子和可选帽子列表。", "dupes_hats_list"),
            Resource("oni://dupes/status-check", "dupes_status_check", "复制人状态检查", "复制人位置、当前差事、关键需求、周边可达格和疑似被困风险；只读。", "dupes_status_check"),
            Resource("oni://dupes/direct-commands", "dupes_direct_commands_list", "复制人直接命令", "复制人可直接执行/配置的玩家操作入口。", "dupes_direct_commands_list"),
            Resource("oni://dupes/todos", "minion_todos_list", "复制人待办差事", "MinionTodoSideScreen 当前差事、可执行差事和阻塞差事。", "minion_todos_list"),
            Resource("oni://dupes/equipment", "dupes_equipment_list", "复制人装备", "复制人装备槽、当前装备和可用装备分配对象；写入使用 assignable_slot_item_set。", "dupes_equipment_list"),
            Resource("oni://assignables", "assignables_list", "可分配对象", "床、医疗床、餐桌、太空服等可分配对象和当前分配。", "assignables_list"),
            Resource("oni://farming/planting", "farming_planting_list", "种植槽", "种植箱、农砖、当前植物、请求种子和可接受种子。", "farming_planting_list"),
            Resource("oni://farming/harvestables", "farming_harvestables_list", "可收获对象", "植物/作物的成熟、收获标记和成熟即收获状态。", "farming_harvestables_list"),
            Resource("oni://farming/seeds", "farming_seed_catalog", "种子目录", "可用于种植请求的 PlantableSeed prefab。", "farming_seed_catalog"),
            Resource("oni://ranching/critters", "critters_list", "小动物", "可抓捕小动物、抓捕标记和捆绑状态。", "critters_list"),
            Resource("oni://ranching/dropoffs", "critters_dropoff_list", "小动物投放点", "小动物/鱼类投放点过滤器、容量和计数。", "critters_dropoff_list"),
            Resource("oni://ranching/incubators", "incubators_list", "孵化器", "孵化器蛋请求、占用对象、进度和连续孵化设置。", "incubators_list"),
            Resource("oni://medical/patients", "medical_patients_list", "医疗患者", "需要医疗关注的复制人、疾病、生命值和医疗床分配。", "medical_patients_list"),
            Resource("oni://medical/clinics", "medical_clinics_list", "医疗床和诊所", "医疗床/诊所治疗阈值、分配对象和优先级。", "medical_clinics_list"),
            Resource("oni://medical/doctor-stations", "doctor_stations_list", "医生站", "医生站药品库存、可治疗疾病和可治疗患者。", "doctor_stations_list"),
            Resource("oni://sandbox/actions", "sandbox_actions_list", "沙盒操作", "MCP 暴露的沙盒/Debug 操作、风险和当前沙盒状态。", "sandbox_actions_list"),
            Resource("oni://sandbox/story-traits", "sandbox_story_traits_list", "沙盒故事特质", "可由沙盒 Story Trait Tool 放置的故事特质模板。", "sandbox_story_traits_list"),
            Resource("oni://game/time", "game_time", "游戏时间和速度", "当前周期、时间百分比、暂停状态和速度。", "game_time"),
            Resource("oni://game/red-alert", "game_red_alert_status", "红色警戒", "当前/全部世界红色警戒（紧急模式）状态。", "game_red_alert_status"),
            Resource("oni://game/saves", "game_saves_list", "存档文件", "本地/云端存档文件、当前 active save 和保存根目录。", "game_saves_list"),
            Resource("oni://game/dlc", "game_dlc_activation_list", "DLC 存档激活状态", "暂停菜单 DLC 激活按钮状态：订阅、当前存档启用、是否允许激活。", "game_dlc_activation_list"),
            Resource("oni://mcp/sessions", "mcp_client_capabilities", "MCP 会话", "当前 MCP session 和客户端 sampling、elicitation、tasks 能力。", "mcp_client_capabilities"),
            Resource("oni://ui/actions", "ui_actions_list", "UI Action 白名单", "可安全触发的管理菜单、覆盖层、建造分类和导航 Action。", "ui_actions_list"),
            Resource("oni://tools/manifest", "tools_manifest", "工具清单", "ONI MCP 工具目录。", "tools_manifest"),
            Resource("oni://tools/guide", "tools_guide", "工具意图指南", "按玩家目标推荐资源、工具链和批量策略。", "tools_guide"),
            Resource("oni://guide/mechanics", "guide_mechanics_query", "缺氧机制速查", "结构化缺氧机制、公式、边界条件和工程注意事项；不包含攻略长文本。", "guide_mechanics_query"),
            Resource("oni://tools/player-action-coverage", "tools_player_action_coverage", "玩家操作覆盖审计", "玩家可执行操作面、对应 MCP 工具和缺口状态。", "tools_player_action_coverage"),
            Resource("oni://tools/side-screen-surfaces", "side_screen_surfaces_audit", "侧屏 surface 审计", "运行时 SideScreenContent 类型到 MCP 工具/资源覆盖的映射审计。", "side_screen_surfaces_audit"),
            Resource("oni://tools/user-menu-surfaces", "user_menu_surfaces_audit", "用户菜单 surface 审计", "源码 UserMenu/context-menu 按钮来源到 MCP 工具/资源覆盖的映射审计。", "user_menu_surfaces_audit"),
            Resource("oni://tools/management-surfaces", "management_surfaces_audit", "管理界面 surface 审计", "源码 ManagementMenu/TableScreen/全屏管理界面到 MCP 工具/资源覆盖的映射审计。", "management_surfaces_audit"),
            Resource("oni://tools/tool-menu-surfaces", "tool_menu_surfaces_audit", "工具栏 surface 审计", "源码 ToolMenu 主工具栏/沙盒工具栏到 MCP 工具/资源覆盖的映射审计。", "tool_menu_surfaces_audit"),
            Resource("oni://tools/ui-menu-surfaces", "ui_menu_surfaces_audit", "UI 菜单 surface 审计", "源码 OverlayMenu/PlanScreen/BuildMenu/安全 UI hotkey 到 MCP 工具/资源覆盖的映射审计。", "ui_menu_surfaces_audit"),
            Resource("oni://tools/global-control-surfaces", "global_control_surfaces_audit", "全局控制 surface 审计", "源码 SpeedControlScreen/TopLeftControlScreen/PauseScreen/Options/Locker 到 MCP 覆盖的映射审计。", "global_control_surfaces_audit"),
            Resource("oni://tools/notification-surfaces", "notification_surfaces_audit", "通知 surface 审计", "源码 NotificationScreen/NotificationManager/消息通知到 MCP 覆盖的映射审计。", "notification_surfaces_audit"),
            Resource("oni://tools/static-audit", "tools_static_audit", "静态接口审计", "工具注册、玩家操作覆盖、资源入口和危险工具确认参数的静态自检。", "tools_static_audit"),
            Resource("oni://power/summary", "power_summary", "电力摘要", "当前世界电力系统摘要：发电机额定功率、消费者负载、电池容量和电量，按 circuitId 聚合。", "power_summary"),
            Resource("oni://rooms/list", "rooms_list", "房间列表", "房间系统状态：房间类型、大小、边界、对象计数和房间效果，适合检查士气房间是否成型。", "rooms_list"),
            Resource("oni://thermal/overheat-risk", "thermal_overheat_risk_scan", "过热风险扫描", "建筑过热风险扫描：按当前格温和建筑过热温度差排序，发现即将过热或已经过热的设备。", "thermal_overheat_risk_scan"),
            Resource("oni://world/layout-candidates", "layout_candidates", "平面布局候选", "按用途扫描区域，返回房间/平台候选矩形、评分、需挖掘、需铺砖、危险格和连通性。", "layout_candidates")
        };

        private static readonly List<McpResourceTemplateInfo> _templates = new List<McpResourceTemplateInfo>
        {
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/cell/{x}/{y}",
                Name = "world_cell_info",
                Title = "世界格子",
                Description = "读取指定地图格子的元素、质量、温度、病菌和可见性。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/text-map{?areaId,x1,y1,x2,y2,worldId,visibleOnly,view,sparse,includeBuildings,includeItems,includeDupes,includeElements,includeSummary,detail,encoding,profile,format,elementLimit,objectLimit,maxCells}",
                Name = "world_text_map",
                Title = "世界文本地图",
                Description = "读取指定矩形区域或 areaId 的文本地图；默认 plain 逐格输出便于 agent 直接读图。返回 areaId、origin、relativeRect、rx/ry 以及世界绝对坐标；后续建造/订单编辑使用世界绝对 x/y。view=temperature 输出温度分级图，view=power/gas_conduits/liquid_conduits/solid_conveyor/logic 输出对应 overlay；sparse/profile=scan 仅用于很大范围的低 token 初扫，format=json 用于规划 harness 结构化校验。",
                MimeType = "text/plain"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://power/summary{?worldId,includeDetails,limit}",
                Name = "power_summary",
                Title = "电力摘要",
                Description = "读取指定世界电力系统摘要；支持按世界 ID、明细开关和数量限制过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rooms/list{?worldId,type,includeBuildings,includeCriteria,limit}",
                Name = "rooms_list",
                Title = "房间列表",
                Description = "读取房间系统状态；支持按世界 ID、房间类型、建筑明细和条件文本过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://thermal/overheat-risk{?worldId,marginC,includeNonOverheatable,minTempC,limit}",
                Name = "thermal_overheat_risk_scan",
                Title = "过热风险扫描",
                Description = "读取建筑过热风险扫描；支持按世界 ID、温差阈值、非过热建筑和数量限制过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/manifest{?query,group,mode,risk,detail,limit}",
                Name = "tools_manifest",
                Title = "工具清单",
                Description = "读取完整或过滤后的工具清单；detail=brief/compact 用于低 token 清单。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/search{?query,group,mode,risk,detail,limit}",
                Name = "tools_search",
                Title = "工具搜索",
                Description = "按关键词、分组、模式和风险低 token 检索工具；detail=brief 返回极简结果。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/guide{?goal,detail}",
                Name = "tools_guide",
                Title = "工具意图指南",
                Description = "按目标生成低 token 工具使用指南，推荐资源、搜索词、工具链、批量策略和规划 harness 流程。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://guide/mechanics{?query,category,detail,limit}",
                Name = "guide_mechanics_query",
                Title = "缺氧机制速查",
                Description = "查询结构化缺氧机制/公式：热量、制氧、保鲜、养殖、电力、自动化、太空等；detail=brief/full。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/player-action-coverage{?query,group,status,detail,includeResources,includeHotkeys,limit}",
                Name = "tools_player_action_coverage",
                Title = "玩家操作覆盖",
                Description = "按玩家操作面搜索 MCP 工具/资源覆盖；detail=brief 适合低 token 查询，detail=full 返回资源锚点。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/static-audit{?includeWarnings}",
                Name = "tools_static_audit",
                Title = "静态接口审计",
                Description = "读取工具注册、覆盖表、资源入口和危险工具确认参数的静态自检。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://game/red-alert{?worldId,allWorlds}",
                Name = "game_red_alert_status",
                Title = "红色警戒状态",
                Description = "读取当前/指定/全部世界红色警戒（紧急模式）状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/side-screen-surfaces{?query,status,includeNoAction,limit}",
                Name = "side_screen_surfaces_audit",
                Title = "侧屏 surface 覆盖",
                Description = "按运行时 SideScreenContent 类型及辅助侧屏 KScreen 读取 MCP 工具/资源覆盖；status=review 可找缺口。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/user-menu-surfaces{?query,status,detail,limit}",
                Name = "user_menu_surfaces_audit",
                Title = "用户菜单 surface 覆盖",
                Description = "按源码 UserMenu/context-menu 按钮来源读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/management-surfaces{?query,status,detail,limit}",
                Name = "management_surfaces_audit",
                Title = "管理界面 surface 覆盖",
                Description = "按 ManagementMenu/TableScreen/全屏管理界面读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/tool-menu-surfaces{?query,status,detail,limit}",
                Name = "tool_menu_surfaces_audit",
                Title = "工具栏 surface 覆盖",
                Description = "按 ToolMenu 主工具栏/沙盒工具栏读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/ui-menu-surfaces{?query,status,detail,limit}",
                Name = "ui_menu_surfaces_audit",
                Title = "UI 菜单 surface 覆盖",
                Description = "按 OverlayMenu/BuildCategory/安全 UI hotkey 读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://colony/notifications{?query,includePending,limit}",
                Name = "notifications_list",
                Title = "通知列表",
                Description = "读取当前 HUD 通知、消息、聚焦目标和可清除状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/global-control-surfaces{?query,status,detail,limit}",
                Name = "global_control_surfaces_audit",
                Title = "全局控制 surface 覆盖",
                Description = "按速度、顶层 HUD、暂停菜单、设置和外部账号菜单读取 MCP 覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/notification-surfaces{?query,status,detail,limit}",
                Name = "notification_surfaces_audit",
                Title = "通知 surface 覆盖",
                Description = "按 NotificationScreen/NotificationManager/MessageNotification 读取 MCP 覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/read/{name}{?...}",
                Name = "tools_read_resource",
                Title = "通用只读工具资源",
                Description = "把任意已注册 read 工具作为 MCP resource 读取；查询串会作为工具参数，复杂参数仍建议使用语义化资源或直接调用工具。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/defs{?query,category,includeUnavailable,limit}",
                Name = "buildings_search_defs",
                Title = "可建造建筑定义",
                Description = "按关键词、分类和可用性搜索可建造建筑定义；用于建造菜单、材料/外观选择和蓝图规划。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/materials{?prefabId,worldId,includeUnavailable,limit}",
                Name = "buildings_materials",
                Title = "可用建造材料",
                Description = "读取指定建筑在当前/指定世界的合法材料候选和库存；默认只返回可用材料，首项即 material=auto 会选择的材料。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/configurables{?areaId,x1,y1,x2,y2,worldId,capability,query,limit}",
                Name = "buildings_config_list",
                Title = "可配置建筑",
                Description = "按区域、能力和关键词读取支持玩家侧屏配置的建筑。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/lights{?areaId,x1,y1,x2,y2,worldId,query,configurableOnly,limit}",
                Name = "lights_list",
                Title = "灯光控件",
                Description = "按区域和关键词读取灯光建筑及其颜色预设。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/pixel-packs{?areaId,x1,y1,x2,y2,worldId,query,includePresets,limit}",
                Name = "pixel_packs_list",
                Title = "Pixel Pack 控件",
                Description = "按区域和关键词读取 Pixel Pack 颜色面板和预设。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://geotuners{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "geo_tuners_list",
                Title = "GeoTuner",
                Description = "按区域和关键词读取 GeoTuner 分配状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://geotuners/geysers{?id,x,y,worldId,query,includeUnstudied,limit}",
                Name = "geo_tuner_geysers_list",
                Title = "GeoTuner 喷泉目标",
                Description = "读取 GeoTuner 同世界可选择喷泉及分配约束。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/artables{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "artables_list",
                Title = "艺术建筑外观",
                Description = "按区域和关键词读取艺术建筑外观选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/monument-parts{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "monument_parts_list",
                Title = "纪念碑部件外观",
                Description = "按区域和关键词读取纪念碑部件外观选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/lures{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "creature_lures_list",
                Title = "生物诱饵站",
                Description = "按区域和关键词读取生物诱饵站诱饵选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/gene-shufflers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "gene_shufflers_list",
                Title = "Gene Shuffler",
                Description = "按区域和关键词读取 Gene Shuffler 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/printerceptors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "printerceptors_list",
                Title = "Printerceptor",
                Description = "按区域和关键词读取 PrinterceptorSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/remote-work-terminals{?areaId,x1,y1,x2,y2,worldId,query,includeDocks,limit}",
                Name = "remote_work_terminals_list",
                Title = "远程工作终端",
                Description = "按区域和关键词读取 RemoteWorkTerminalSidescreen dock 选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/genetic-analysis-stations{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "genetic_analysis_stations_list",
                Title = "Botanical Analyzer",
                Description = "按区域和关键词读取 Botanical Analyzer 种子允许/禁用状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/dispensers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "dispensers_list",
                Title = "分发器",
                Description = "按区域和关键词读取 DispenserSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/receptacles{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "receptacles_list",
                Title = "实体陈列/插槽",
                Description = "按区域和关键词读取 ReceptacleSideScreen / SpecialCargoBayClusterSideScreen / SingleEntityReceptacle 状态和可请求实体。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/suit-lockers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "suit_lockers_list",
                Title = "太空服柜",
                Description = "按区域和关键词读取 SuitLockerSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/lore-bearers{?areaId,x1,y1,x2,y2,worldId,query,interactableOnly,limit}",
                Name = "lore_bearers_list",
                Title = "LoreBearer",
                Description = "按区域和关键词读取可阅读/已阅读 LoreBearer。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/telepads{?areaId,x1,y1,x2,y2,worldId,query,includeVictory,limit}",
                Name = "telepads_list",
                Title = "Printing Pod / Telepad",
                Description = "按区域和关键词读取 TelepadSideScreen 移民、研究、技能和胜利条件状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/artifacts{?areaId,x1,y1,x2,y2,worldId,query,includeStations,includeWorldArtifacts,limit}",
                Name = "artifacts_list",
                Title = "Artifact Analysis",
                Description = "读取已分析 artifact、场上 artifact 和 Artifact Analysis Station 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/warp-portals{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "warp_portals_list",
                Title = "Warp Portal",
                Description = "按区域和关键词读取 WarpPortalSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/temporal-tears{?query,limit}",
                Name = "temporal_tears_list",
                Title = "Temporal Tear",
                Description = "读取时间裂隙状态和可进入/可消耗火箭。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://space/telescopes{?areaId,x1,y1,x2,y2,worldId,query,includeTargets,limit}",
                Name = "telescopes_list",
                Title = "Telescope",
                Description = "按区域和关键词读取望远镜和星图分析目标。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://space/analysis-targets{?query,includeComplete,limit}",
                Name = "starmap_analysis_targets_list",
                Title = "星图分析目标",
                Description = "读取可由望远镜分析的星图目的地。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://diagnostics/process-conditions{?id,x,y,worldId,conditionType,query,showHidden,limit}",
                Name = "process_conditions_list",
                Title = "通用过程条件",
                Description = "读取 IProcessConditionSet/ConditionListSideScreen 条件列表。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/bionic-upgrades{?id,name,query,limit}",
                Name = "bionic_upgrades_list",
                Title = "仿生人升级槽",
                Description = "读取 BionicSideScreen 升级槽、分配和安装状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/missile-launchers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "missile_launchers_list",
                Title = "导弹发射器",
                Description = "按区域和关键词读取导弹发射器弹药允许状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/modules{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "rocket_modules_list",
                Title = "火箭模块",
                Description = "按区域和关键词读取可重排火箭模块及操作能力。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/module-defs{?id,x,y,worldId,mode,query,limit}",
                Name = "rocket_module_defs_list",
                Title = "火箭模块定义",
                Description = "读取 SelectModuleSideScreen 火箭模块定义和 add/replace 条件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/launch-pads{?query,limit}",
                Name = "launch_pads_list",
                Title = "火箭发射台",
                Description = "读取 LaunchPadSideScreen 发射台、已停靠火箭和可降落火箭。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/flight-utilities{?rocketId,rocketName,query,includeDupes,limit}",
                Name = "rocket_flight_utilities_list",
                Title = "火箭飞行模块实用操作",
                Description = "读取 ModuleFlightUtilitySideScreen 清空/投放、自动投放、目标和复制人选择状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/restrictions{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "rocket_restrictions_list",
                Title = "火箭控制台限制",
                Description = "按区域和关键词读取 RocketRestrictionSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/usage-controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "rocket_usage_controls_list",
                Title = "火箭内部建筑使用限制",
                Description = "按区域和关键词读取火箭内部建筑是否受控制台限制。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/crew-requests{?rocketId,rocketName,query,limit}",
                Name = "rocket_crew_requests_list",
                Title = "火箭乘员召集",
                Description = "读取 SummonCrewSideScreen 乘员召集/释放状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/assignment-groups{?groupId,controllerId,query,includeDupes,limit}",
                Name = "assignment_groups_list",
                Title = "分配组成员",
                Description = "读取 AssignmentGroupControllerSideScreen 分配组及复制人成员开关状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/cargo-collectors{?rocketId,rocketName,query,limit}",
                Name = "rocket_cargo_collectors_list",
                Title = "火箭货舱收集器",
                Description = "读取 CargoModuleSideScreen 收集模块容量、库存和进度。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/harvest-modules{?rocketId,rocketName,query,limit}",
                Name = "rocket_harvest_modules_list",
                Title = "火箭钻探模块",
                Description = "读取 HarvestModuleSideScreen 钻探状态和钻石库存。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "automation_controls_list",
                Title = "自动化控件",
                Description = "按区域和关键词读取逻辑/电力相关玩家可配置控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/automatable{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "automatable_controls_list",
                Title = "自动化专用搬运",
                Description = "按区域和关键词读取 AutomatableSideScreen 允许手动搬运状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/critter-sensors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "critter_sensors_list",
                Title = "小动物计数传感器",
                Description = "按区域和关键词读取 CritterSensorSideScreen 小动物/蛋计数开关。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://storage/tile-selections{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "storage_tile_selections_list",
                Title = "储存砖目标物品",
                Description = "按区域和关键词读取 SingleItemSelectionSideScreen / StorageTile 目标物品选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://filters/controls{?areaId,x1,y1,x2,y2,worldId,kind,query,includeOptions,limit}",
                Name = "filters_list",
                Title = "过滤器控件",
                Description = "按区域、类型和关键词读取单选/多选过滤器控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/options{?areaId,x1,y1,x2,y2,worldId,kind,query,includeOptions,limit}",
                Name = "side_options_list",
                Title = "选项型侧屏控件",
                Description = "按区域、类型和关键词读取方向、少量选项、广播频道和辐射粒子方向控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/state{?areaId,x1,y1,x2,y2,worldId,kind,query,limit}",
                Name = "state_controls_list",
                Title = "状态型侧屏控件",
                Description = "按区域、类型和关键词读取容量、checkbox、计数器和时间范围控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/activation-ranges{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "activation_ranges_list",
                Title = "启停双阈值控件",
                Description = "按区域和关键词读取 ActiveRangeSideScreen / IActivationRangeTarget 双阈值控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/progress-bars{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "progress_bars_list",
                Title = "侧屏进度条",
                Description = "按区域和关键词读取 ProgressBarSideScreen / IProgressBarSideScreen 只读进度条状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/buttons{?areaId,x1,y1,x2,y2,worldId,query,interactableOnly,limit}",
                Name = "side_buttons_list",
                Title = "通用侧屏按钮",
                Description = "按区域和关键词读取 ISidescreenButtonControl 通用侧屏按钮。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/user-menu-actions{?areaId,x1,y1,x2,y2,worldId,category,query,limit}",
                Name = "user_menu_actions_list",
                Title = "对象用户菜单操作",
                Description = "按区域、分类和关键词读取对象 UserMenu/context-menu 按钮映射。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/maintenance-actions{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "maintenance_actions_list",
                Title = "维护类用户菜单操作",
                Description = "按区域和关键词读取需要状态机/槽位参数的玩家维护操作。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/checklists{?areaId,x1,y1,x2,y2,worldId,query,checkedOnly,enabledOnly,limit}",
                Name = "side_checklists_list",
                Title = "侧屏清单",
                Description = "按区域和关键词读取 ICheckboxListGroupControl 故事任务、条件和设施清单。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/related-entities{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "related_entities_list",
                Title = "关联对象",
                Description = "按区域和关键词读取 IRelatedEntities 侧屏关联对象及可点击跳转目标。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/n-toggles{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "n_toggles_list",
                Title = "多选侧屏控件",
                Description = "按区域和关键词读取 INToggleSideScreenControl 多选控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/logic-alarms{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "logic_alarms_list",
                Title = "逻辑报警器",
                Description = "按区域和关键词读取 Logic Alarm 通知设置。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/comet-detectors{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "comet_detectors_list",
                Title = "彗星探测器",
                Description = "按区域和关键词读取彗星探测器目标和可选目标。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/cluster-location-sensors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "cluster_location_sensors_list",
                Title = "星图位置传感器",
                Description = "按区域和关键词读取星图位置传感器过滤设置。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/railguns{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "railguns_list",
                Title = "轨道炮",
                Description = "按区域和关键词读取轨道炮发射质量、库存和能量状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/turbo-heaters{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "turbo_heaters_list",
                Title = "液体加热器涡轮模式",
                Description = "按区域和关键词读取 Liquid Tepidizer TurboModeSideScreen。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/self-destruct{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "rocket_self_destruct_list",
                Title = "火箭自毁",
                Description = "按区域和关键词读取可自毁火箭舱体。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/fabricators{?areaId,x1,y1,x2,y2,worldId,query,queuedOnly,includeRecipes,limit}",
                Name = "production_fabricators_list",
                Title = "生产制作站",
                Description = "按区域和关键词读取制作站队列、订单和运行状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/recipes{?id,x,y,areaId,x1,y1,x2,y2,worldId,recipeId,categoryId,query,queuedOnly,includeLocked,limit}",
                Name = "production_recipes_list",
                Title = "生产配方",
                Description = "按制作站、区域和关键词读取配方材料、产物和队列数量。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/mutant-seed-controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "mutant_seed_controls_list",
                Title = "突变种子接收开关",
                Description = "按区域和关键词读取接受/拒收突变种子的玩家菜单开关。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/configurable-consumers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "configurable_consumers_list",
                Title = "可配置消费者",
                Description = "按区域和关键词读取 ConfigureConsumerSideScreen 选项。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/planting{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "farming_planting_list",
                Title = "种植槽",
                Description = "按区域和关键词读取可种植建筑、当前植物和种植请求。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/harvestables{?areaId,x1,y1,x2,y2,worldId,query,readyOnly,limit}",
                Name = "farming_harvestables_list",
                Title = "可收获对象",
                Description = "按区域和关键词读取植物/作物收获状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/seeds{?query,limit}",
                Name = "farming_seed_catalog",
                Title = "种子目录",
                Description = "搜索可种植种子 prefab/tag。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/critters{?areaId,x1,y1,x2,y2,worldId,query,capturableOnly,wrangledOnly,limit}",
                Name = "critters_list",
                Title = "小动物",
                Description = "按区域和关键词读取可抓捕对象状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/dropoffs{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "critters_dropoff_list",
                Title = "小动物投放点",
                Description = "按区域和关键词读取小动物/鱼类投放点。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/incubators{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "incubators_list",
                Title = "孵化器",
                Description = "按区域和关键词读取孵化器状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/clinics{?areaId,x1,y1,x2,y2,worldId,limit}",
                Name = "medical_clinics_list",
                Title = "医疗床和诊所",
                Description = "按区域读取医疗床/诊所状态和阈值。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://sandbox/story-traits{?query}",
                Name = "sandbox_story_traits_list",
                Title = "沙盒故事特质",
                Description = "读取可由沙盒 Story Trait Tool 放置的故事特质模板。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/patients{?worldId,includeHealthy,query,limit}",
                Name = "medical_patients_list",
                Title = "医疗患者",
                Description = "读取复制人疾病、生命值和医疗分配状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/doctor-stations{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "doctor_stations_list",
                Title = "医生站",
                Description = "按区域读取医生站药品库存和可治疗患者。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/direct-commands{?id,name,limit}",
                Name = "dupes_direct_commands_list",
                Title = "复制人直接命令",
                Description = "读取复制人的直接操作入口和对应 MCP 工具。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/todos{?id,name,query,includeBlocked,includePotentialOnly,limit,taskLimit}",
                Name = "minion_todos_list",
                Title = "复制人待办差事",
                Description = "读取 MinionTodoSideScreen 当前差事、可执行差事和阻塞差事。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/equipment{?id,name,slotId,includeAvailable}",
                Name = "dupes_equipment_list",
                Title = "复制人装备",
                Description = "读取复制人装备槽、当前装备和可用装备；写入使用 assignable_slot_item_set。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/priorities{?id,name,choreGroup,includeNonUserPrioritizable}",
                Name = "dupes_priorities_list",
                Title = "复制人个人优先级",
                Description = "读取 Priorities/Jobs 管理屏中复制人对各 ChoreGroup 的个人工作优先级。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/hats{?id,name}",
                Name = "dupes_hats_list",
                Title = "复制人帽子",
                Description = "读取 Skills 管理屏中的当前帽子、目标帽子和可选帽子列表。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/priority-settings",
                Name = "dupes_priority_settings_list",
                Title = "复制人优先级设置",
                Description = "读取 Jobs/Priorities 管理屏全局高级模式开关和重置行为。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ui/actions{?kind}",
                Name = "ui_actions_list",
                Title = "UI Action 白名单",
                Description = "按类型读取可安全触发的 UI Action。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://sandbox/cell/{x}/{y}",
                Name = "sandbox_sample_cell",
                Title = "沙盒格子取样",
                Description = "读取指定格子的沙盒刷子参数。",
                MimeType = "application/json"
            }
        };

        public static List<McpResourceInfo> GetResourceInfos()
        {
            return _resources
                .Select(resource => resource.Info)
                .OrderBy(resource => resource.Uri)
                .ToList();
        }

        public static List<McpResourceTemplateInfo> GetResourceTemplateInfos()
        {
            return _templates.OrderBy(template => template.UriTemplate).ToList();
        }

        public static ReadResourceResult ReadResource(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return null;

            var resource = _resources.FirstOrDefault(item => item.Info.Uri == uri);
            if (resource != null)
                return ReadToolResource(uri, resource.ToolName, new JObject(), resource.Info.MimeType);

            return ReadDynamicResource(uri);
        }

        private static ReadResourceResult ReadDynamicResource(string uri)
        {
            Uri parsed;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed) || parsed.Scheme != "oni")
                return null;

            if (parsed.Host == "world" && parsed.AbsolutePath.StartsWith("/cell/"))
            {
                var parts = parsed.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 3)
                {
                    return ReadToolResource(uri, "world_cell_info", new JObject
                    {
                        ["x"] = parts[1],
                        ["y"] = parts[2]
                    }, "application/json");
                }
            }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/text-map")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "world_text_map", query, "text/plain");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath.StartsWith("/read/"))
            {
                var parts = parsed.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    string toolName = Uri.UnescapeDataString(parts[1]);
                    var tool = OniToolRegistry.GetTools().FirstOrDefault(item => string.Equals(item.Name, toolName, StringComparison.OrdinalIgnoreCase));
                    if (tool == null)
                        return ErrorResource(uri, "Tool not found: " + toolName);
                    if (!string.Equals(tool.Mode, "read", StringComparison.OrdinalIgnoreCase))
                        return ErrorResource(uri, "Only read tools can be exposed via oni://tools/read/{name}");
                    return ReadToolResource(uri, tool.Name, ParseQuery(parsed.Query), "application/json");
                }
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/guide")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tools_guide", query, "application/json");
            }

            if (parsed.Host == "guide" && parsed.AbsolutePath == "/mechanics")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "guide_mechanics_query", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/manifest")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tools_manifest", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/search")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tools_search", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/player-action-coverage")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tools_player_action_coverage", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/side-screen-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "side_screen_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/user-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "user_menu_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/management-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "management_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/tool-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tool_menu_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/ui-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "ui_menu_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/global-control-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "global_control_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/notification-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "notification_surfaces_audit", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/static-audit")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "tools_static_audit", query, "application/json");
            }

            if (parsed.Host == "colony" && parsed.AbsolutePath == "/notifications")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "notifications_list", query, "application/json");
            }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/saves")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "game_saves_list", query, "application/json");
            }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/red-alert")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "game_red_alert_status", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/defs")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "buildings_search_defs", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/materials")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "buildings_materials", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/configurables")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "buildings_config_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/lights")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "lights_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/pixel-packs")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "pixel_packs_list", query, "application/json");
            }

            if (parsed.Host == "geotuners" && (parsed.AbsolutePath == "" || parsed.AbsolutePath == "/"))
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "geo_tuners_list", query, "application/json");
            }

            if (parsed.Host == "geotuners" && parsed.AbsolutePath == "/geysers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "geo_tuner_geysers_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/artables")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "artables_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/monument-parts")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "monument_parts_list", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/lures")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "creature_lures_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/gene-shufflers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "gene_shufflers_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/printerceptors")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "printerceptors_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/remote-work-terminals")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "remote_work_terminals_list", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/genetic-analysis-stations")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "genetic_analysis_stations_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/dispensers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dispensers_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/receptacles")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "receptacles_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/suit-lockers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "suit_lockers_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/lore-bearers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "lore_bearers_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/telepads")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "telepads_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/artifacts")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "artifacts_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/warp-portals")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "warp_portals_list", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/temporal-tears")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "temporal_tears_list", query, "application/json");
            }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/telescopes")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "telescopes_list", query, "application/json");
            }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/analysis-targets")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "starmap_analysis_targets_list", query, "application/json");
            }

            if (parsed.Host == "diagnostics" && parsed.AbsolutePath == "/process-conditions")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "process_conditions_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/bionic-upgrades")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "bionic_upgrades_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/missile-launchers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "missile_launchers_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/modules")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_modules_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/module-defs")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_module_defs_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/launch-pads")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "launch_pads_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/flight-utilities")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_flight_utilities_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/restrictions")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_restrictions_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/usage-controls")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_usage_controls_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/crew-requests")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_crew_requests_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/assignment-groups")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "assignment_groups_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/cargo-collectors")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_cargo_collectors_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/harvest-modules")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_harvest_modules_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/controls")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "automation_controls_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/automatable")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "automatable_controls_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/critter-sensors")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "critter_sensors_list", query, "application/json");
            }

            if (parsed.Host == "storage" && parsed.AbsolutePath == "/tile-selections")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "storage_tile_selections_list", query, "application/json");
            }

            if (parsed.Host == "filters" && parsed.AbsolutePath == "/controls")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "filters_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/options")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "side_options_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/state")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "state_controls_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/activation-ranges")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "activation_ranges_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/progress-bars")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "progress_bars_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/buttons")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "side_buttons_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/user-menu-actions")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "user_menu_actions_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/maintenance-actions")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "maintenance_actions_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/checklists")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "side_checklists_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/related-entities")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "related_entities_list", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/n-toggles")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "n_toggles_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/logic-alarms")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "logic_alarms_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/comet-detectors")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "comet_detectors_list", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/cluster-location-sensors")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "cluster_location_sensors_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/railguns")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "railguns_list", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/turbo-heaters")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "turbo_heaters_list", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/self-destruct")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "rocket_self_destruct_list", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/fabricators")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "production_fabricators_list", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/recipes")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "production_recipes_list", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/mutant-seed-controls")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "mutant_seed_controls_list", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/configurable-consumers")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "configurable_consumers_list", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/planting")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "farming_planting_list", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/harvestables")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "farming_harvestables_list", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/seeds")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "farming_seed_catalog", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/critters")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "critters_list", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/dropoffs")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "critters_dropoff_list", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/incubators")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "incubators_list", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/clinics")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "medical_clinics_list", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/patients")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "medical_patients_list", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/doctor-stations")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "doctor_stations_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/direct-commands")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dupes_direct_commands_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/todos")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "minion_todos_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/equipment")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dupes_equipment_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priorities")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dupes_priorities_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/hats")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dupes_hats_list", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priority-settings")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "dupes_priority_settings_list", query, "application/json");
            }

            if (parsed.Host == "ui" && parsed.AbsolutePath == "/actions")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "ui_actions_list", query, "application/json");
            }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath == "/story-traits")
            {
                var query = ParseQuery(parsed.Query);
                return ReadToolResource(uri, "sandbox_story_traits_list", query, "application/json");
            }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath.StartsWith("/cell/"))
            {
                var parts = parsed.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 3)
                {
                    return ReadToolResource(uri, "sandbox_sample_cell", new JObject
                    {
                        ["x"] = parts[1],
                        ["y"] = parts[2]
                    }, "application/json");
                }
            }

            return null;
        }

        private static ReadResourceResult ErrorResource(string uri, string message)
        {
            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["error"] = true,
                            ["message"] = message
                        }, McpJsonUtil.Settings)
                    }
                }
            };
        }

        private static ReadResourceResult ReadToolResource(string uri, string toolName, JObject arguments, string mimeType)
        {
            var result = OniToolRegistry.CallTool(toolName, arguments);
            string text = ExtractText(result);
            if (result != null && result.IsError)
                text = JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["error"] = true,
                    ["message"] = text
                }, McpJsonUtil.Settings);

            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = mimeType,
                        Text = text
                    }
                }
            };
        }

        private static string ExtractText(CallToolResult result)
        {
            if (result == null || result.Content == null)
                return "";
            return string.Join("\n", result.Content.Where(content => content != null).Select(content => content.Text ?? "").ToArray());
        }

        private static JObject ParseQuery(string query)
        {
            var result = new JObject();
            if (string.IsNullOrEmpty(query))
                return result;

            string trimmed = query[0] == '?' ? query.Substring(1) : query;
            foreach (var pair in trimmed.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                var parts = pair.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(parts[0]);
                if (string.IsNullOrEmpty(key))
                    continue;

                string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                if (string.IsNullOrEmpty(value))
                    continue;

                result[key] = value;
            }

            return result;
        }

        private static OniResource Resource(string uri, string name, string title, string description, string toolName)
        {
            return new OniResource
            {
                Info = new McpResourceInfo
                {
                    Uri = uri,
                    Name = name,
                    Title = title,
                    Description = description,
                    MimeType = "application/json"
                },
                ToolName = toolName
            };
        }

        private class OniResource
        {
            public McpResourceInfo Info { get; set; }
            public string ToolName { get; set; }
        }
    }
}
