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
            Resource("oni://colony/status", "colony_control", "殖民地状态", "周期、复制人数、世界数量、速度和暂停状态。", "colony_control domain=read action=status", new JObject { ["domain"] = "read", ["action"] = "status" }),
            Resource("oni://colony/diagnostics", "colony_control", "殖民地诊断", "缺氧、断粮、过热等殖民地诊断结果。", "colony_control domain=diagnostic action=diagnostics", new JObject { ["domain"] = "diagnostic", ["action"] = "diagnostics" }),
            Resource("oni://colony/alerts", "colony_control", "殖民地警报", "当前游戏警报和通知。", "colony_control domain=diagnostic action=alerts", new JObject { ["domain"] = "diagnostic", ["action"] = "alerts" }),
            Resource("oni://colony/diagnostic-settings", "colony_control", "殖民地诊断设置", "AllDiagnosticsScreen 诊断显示模式、子条件启用状态和 Debug 通知禁用状态。", "colony_control domain=diagnostic action=list_settings", new JObject { ["domain"] = "diagnostic", ["action"] = "list_settings" }),
            Resource("oni://colony/report", "colony_control", "殖民地报告", "殖民地报告。", "colony_control domain=report action=report", new JObject { ["domain"] = "report", ["action"] = "report" }),
            Resource("oni://colony/summary", "colony_control", "殖民地摘要", "面向行动规划的殖民地摘要。", "colony_control domain=report action=summary", new JObject { ["domain"] = "report", ["action"] = "summary" }),
            Resource("oni://colony/notifications", "colony_control", "通知列表", "当前 HUD NotificationScreen/NotificationManager 通知、消息、聚焦目标和可清除状态。", "colony_control domain=notification action=list", new JObject { ["domain"] = "notification", ["action"] = "list" }),
            Resource("oni://world/list", "colony_control", "世界列表", "已加载世界和当前激活世界。", "colony_control domain=read action=worlds", new JObject { ["domain"] = "read", ["action"] = "worlds" }),
            Resource("oni://world/elements", "read_control", "世界元素摘要", "当前世界元素质量和温度摘要。", "read_control domain=world action=element_summary", new JObject { ["domain"] = "world", ["action"] = "element_summary" }),
            Resource("oni://camera/view", "navigation_control", "相机视图", "当前相机位置、缩放、激活世界和屏幕尺寸。", "navigation_control action=get_view", new JObject { ["action"] = "get_view" }),
            Resource("oni://resources/inventory", "read_control", "资源库存", "资源库存摘要。", "read_control domain=resources action=inventory", new JObject { ["domain"] = "resources", ["action"] = "inventory" }),
            Resource("oni://resources/food", "read_control", "食物库存", "食物库存和保质信息。", "read_control domain=resources action=food", new JObject { ["domain"] = "resources", ["action"] = "food" }),
            Resource("oni://resources/pins", "read_control", "资源面板固定和通知", "AllResourcesScreen 资源行固定显示和通知开关状态。", "read_control domain=resources action=pins", new JObject { ["domain"] = "resources", ["action"] = "pins" }),
            Resource("oni://diet/status", "colony_control", "饮食权限", "Consumables 管理屏中的复制人饮食/药品/电池可消费权限和库存。", "colony_control domain=management kind=diet action=status", new JObject { ["domain"] = "management", ["kind"] = "diet", ["action"] = "status" }),
            Resource("oni://storage/list", "building_control", "储存列表", "储存建筑和过滤器列表。", "building_control domain=storage action=list", new JObject { ["domain"] = "storage", ["action"] = "list" }),
            Resource("oni://storage/tile-selections", "building_control", "储存砖目标物品", "SingleItemSelectionSideScreen / StorageTile 目标物品和可选物品。", "building_control domain=tile_selection action=list", new JObject { ["domain"] = "tile_selection", ["action"] = "list" }),
            Resource("oni://filters/controls", "building_control", "过滤器控件", "气/液/固体单选过滤器、元素传感器和树形/平铺多选过滤器。", "building_control domain=filter action=list", new JObject { ["domain"] = "filter", ["action"] = "list" }),
            Resource("oni://controls/options", "building_control", "选项型侧屏控件", "工作方向、少量选项、逻辑广播频道和辐射粒子方向控件。", "building_control domain=side_surface surface=option action=list", new JObject { ["domain"] = "option", ["action"] = "list" }),
            Resource("oni://controls/state", "building_control", "状态型侧屏控件", "容量上限、单 checkbox、逻辑计数器和时间范围传感器控件。", "building_control action=state_list", new JObject { ["action"] = "state_list" }),
            Resource("oni://controls/activation-ranges", "building_control", "启停双阈值控件", "ActiveRangeSideScreen / IActivationRangeTarget 双阈值控件。", "building_control domain=side_surface surface=activation action=list", new JObject { ["domain"] = "activation", ["action"] = "list" }),
            Resource("oni://controls/progress-bars", "building_control", "侧屏进度条", "ProgressBarSideScreen / IProgressBarSideScreen 只读进度条状态。", "building_control domain=side_surface kind=progress action=list", new JObject { ["kind"] = "progress", ["action"] = "list" }),
            Resource("oni://controls/buttons", "building_control", "通用侧屏按钮", "实现 ISidescreenButtonControl 的通用侧屏按钮入口。", "building_control domain=side_surface kind=button action=list/press", new JObject { ["kind"] = "button", ["action"] = "list" }),
            Resource("oni://controls/user-menu-actions", "building_control", "对象用户菜单操作", "对象 UserMenu/context-menu 按钮映射：清扫、维修、堆肥、倒空、雕刻等非侧屏操作。", "building_control domain=side_surface surface=user_menu action=list", new JObject { ["domain"] = "user_menu", ["action"] = "list" }),
            Resource("oni://controls/maintenance-actions", "building_control", "维护类用户菜单操作", "需要状态机或槽位参数的玩家维护操作：厕所清洁、淡化器清空、运输管蜡、蜂巢清空、货仓倒空、复制人卸装。", "building_control domain=side_surface surface=maintenance action=list", new JObject { ["domain"] = "maintenance", ["action"] = "list" }),
            Resource("oni://controls/checklists", "building_control", "侧屏清单", "实现 ICheckboxListGroupControl 的故事任务、条件和设施清单。", "building_control domain=side_surface kind=checklist action=list", new JObject { ["kind"] = "checklist", ["action"] = "list" }),
            Resource("oni://controls/related-entities", "building_control", "关联对象", "实现 IRelatedEntities 的侧屏关联对象和可点击跳转目标。", "building_control domain=side_surface kind=related action=list/focus", new JObject { ["kind"] = "related", ["action"] = "list" }),
            Resource("oni://controls/n-toggles", "building_control", "多选侧屏控件", "实现 INToggleSideScreenControl 的多选侧屏控件。", "building_control domain=side_surface surface=misc kind=n_toggle action=list", new JObject { ["domain"] = "misc", ["kind"] = "n_toggle", ["action"] = "list" }),
            Resource("oni://automation/logic-alarms", "building_control", "逻辑报警器", "Logic Alarm 通知名称、提示、类型、暂停和镜头跳转设置。", "building_control domain=side_surface surface=misc kind=logic_alarm action=list", new JObject { ["domain"] = "misc", ["kind"] = "logic_alarm", ["action"] = "list" }),
            Resource("oni://automation/automatable", "building_control", "自动化专用搬运", "AutomatableSideScreen 只允许自动化/允许手动搬运状态。", "building_control domain=side_surface surface=automation kind=automatable action=list", new JObject { ["domain"] = "automation", ["kind"] = "automatable", ["action"] = "list" }),
            Resource("oni://automation/critter-sensors", "building_control", "小动物计数传感器", "CritterSensorSideScreen 小动物/蛋计数开关、阈值和当前计数。", "building_control domain=side_surface surface=automation kind=critter_sensor action=list", new JObject { ["domain"] = "automation", ["kind"] = "critter_sensor", ["action"] = "list" }),
            Resource("oni://automation/comet-detectors", "building_control", "彗星探测器", "Comet Detector/Space Scanner 当前探测目标和可选择目标。", "building_control domain=space_building kind=comet_detector action=list", new JObject { ["domain"] = "space_building", ["kind"] = "comet_detector", ["action"] = "list" }),
            Resource("oni://automation/cluster-location-sensors", "building_control", "星图位置传感器", "LogicClusterLocationSensor 空太空和星体/POI 坐标过滤设置。", "building_control domain=space_building kind=cluster_location_sensor action=list", new JObject { ["domain"] = "space_building", ["kind"] = "cluster_location_sensor", ["action"] = "list" }),
            Resource("oni://buildings/lights", "building_control", "灯光控件", "灯光建筑、发光参数和可选颜色预设。", "building_control action=visual kind=light visualAction=list", new JObject { ["action"] = "visual", ["kind"] = "light", ["visualAction"] = "list" }),
            Resource("oni://buildings/turbo-heaters", "building_control", "液体加热器涡轮模式", "Liquid Tepidizer TurboModeSideScreen 功耗和开关状态。", "building_control domain=side_surface surface=misc kind=turbo_heater action=list", new JObject { ["domain"] = "misc", ["kind"] = "turbo_heater", ["action"] = "list" }),
            Resource("oni://buildings/pixel-packs", "building_control", "Pixel Pack 控件", "Pixel Pack 当前逻辑值、四面板 active/standby 颜色和颜色预设。", "building_control action=visual kind=pixel_pack visualAction=list", new JObject { ["action"] = "visual", ["kind"] = "pixel_pack", ["visualAction"] = "list" }),
            Resource("oni://geotuners", "building_control", "GeoTuner", "GeoTuner 当前/未来目标喷泉和调谐分配状态。", "building_control domain=side_surface surface=geo_tuner action=list", new JObject { ["domain"] = "geo_tuner", ["action"] = "list" }),
            Resource("oni://geotuners/geysers", "building_control", "GeoTuner 喷泉目标", "GeoTuner 可选择喷泉、研究状态、可见性和分配数量。", "building_control domain=side_surface surface=geo_tuner action=list_geysers", new JObject { ["domain"] = "geo_tuner", ["action"] = "list_geysers" }),
            Resource("oni://buildings/artables", "building_control", "艺术建筑外观", "艺术建筑当前外观和可选择外观阶段。", "building_control domain=special kind=artable action=list", new JObject { ["domain"] = "special", ["kind"] = "artable", ["action"] = "list" }),
            Resource("oni://buildings/monument-parts", "building_control", "纪念碑部件外观", "纪念碑部件当前外观、可选外观和部件类型。", "building_control domain=special kind=monument_part action=list", new JObject { ["domain"] = "special", ["kind"] = "monument_part", ["action"] = "list" }),
            Resource("oni://ranching/lures", "building_control", "生物诱饵站", "生物诱饵站当前诱饵、可选诱饵和库存。", "building_control domain=special kind=creature_lure action=list", new JObject { ["domain"] = "special", ["kind"] = "creature_lure", ["action"] = "list" }),
            Resource("oni://buildings/gene-shufflers", "building_control", "Gene Shuffler", "Gene Shuffler 分配、工作完成、消耗和充能请求状态。", "building_control domain=special kind=gene_shuffler action=list", new JObject { ["domain"] = "special", ["kind"] = "gene_shuffler", ["action"] = "list" }),
            Resource("oni://story/printerceptors", "building_control", "Printerceptor", "Printerceptor passcode、拦截充能、打印界面和 databank 状态。", "building_control domain=story_facility kind=printerceptor action=list", new JObject { ["domain"] = "story_facility", ["kind"] = "printerceptor", ["action"] = "list" }),
            Resource("oni://story/poi-tech-unlocks", "building_control", "信息传送通道", "Research Portal/信息传送通道解锁差事、进度和 POI 科技项。", "building_control domain=story_facility kind=poi_tech_unlock action=list", new JObject { ["domain"] = "story_facility", ["kind"] = "poi_tech_unlock", ["action"] = "list" }),
            Resource("oni://buildings/remote-work-terminals", "building_control", "远程工作终端", "Remote Work Terminal 当前/未来 dock 和同世界可选 dock。", "building_control domain=story_facility kind=remote_work_terminal action=list", new JObject { ["domain"] = "story_facility", ["kind"] = "remote_work_terminal", ["action"] = "list" }),
            Resource("oni://farming/genetic-analysis-stations", "building_control", "Botanical Analyzer", "Botanical Analyzer 可分析种子、允许/禁用状态和库存。", "building_control domain=story_facility kind=genetic_analysis_station action=list", new JObject { ["domain"] = "story_facility", ["kind"] = "genetic_analysis_station", ["action"] = "list" }),
            Resource("oni://buildings/dispensers", "building_control", "分发器", "DispenserSideScreen 可分发物品、当前选择和分发请求状态。", "building_control domain=side_surface surface=facility kind=dispenser action=list", new JObject { ["domain"] = "facility", ["kind"] = "dispenser", ["action"] = "list" }),
            Resource("oni://buildings/receptacles", "building_control", "实体陈列/插槽", "ReceptacleSideScreen / SpecialCargoBayClusterSideScreen / SingleEntityReceptacle 通用实体请求、取消和移除状态。", "building_control domain=receptacle action=list", new JObject { ["domain"] = "receptacle", ["action"] = "list" }),
            Resource("oni://buildings/suit-lockers", "building_control", "太空服柜", "SuitLockerSideScreen 配置、请求、存储装备和掉落能力状态。", "building_control domain=side_surface surface=facility kind=suit_locker action=list", new JObject { ["domain"] = "facility", ["kind"] = "suit_locker", ["action"] = "list" }),
            Resource("oni://story/lore-bearers", "building_control", "LoreBearer", "LoreBearerSideScreen 可阅读/已阅读对象和按钮状态。", "building_control domain=side_surface surface=facility kind=lore_bearer action=list", new JObject { ["domain"] = "facility", ["kind"] = "lore_bearer", ["action"] = "list" }),
            Resource("oni://story/telepads", "building_control", "Printing Pod / Telepad", "TelepadSideScreen 移民、研究、技能提示和胜利条件状态。", "building_control domain=side_surface surface=facility kind=telepad action=list", new JObject { ["domain"] = "facility", ["kind"] = "telepad", ["action"] = "list" }),
            Resource("oni://story/artifacts", "building_control", "Artifact Analysis", "ArtifactAnalysisSideScreen 已分析 artifact、场上 artifact 和分析站状态。", "building_control domain=side_surface surface=facility kind=artifact action=list", new JObject { ["domain"] = "facility", ["kind"] = "artifact", ["action"] = "list" }),
            Resource("oni://story/warp-portals", "building_control", "Warp Portal", "WarpPortalSideScreen 等待传送、传送中、冷却和分配状态。", "building_control domain=space_story kind=warp_portal action=list", new JObject { ["domain"] = "space_story", ["kind"] = "warp_portal", ["action"] = "list" }),
            Resource("oni://story/temporal-tears", "building_control", "Temporal Tear", "TemporalTearSideScreen 裂隙开启、消耗状态和可进入火箭。", "building_control domain=space_story kind=temporal_tear action=list", new JObject { ["domain"] = "space_story", ["kind"] = "temporal_tear", ["action"] = "list" }),
            Resource("oni://space/telescopes", "building_control", "Telescope", "TelescopeSideScreen 建筑状态和当前星图分析目标。", "building_control domain=space_story kind=telescope action=list", new JObject { ["domain"] = "space_story", ["kind"] = "telescope", ["action"] = "list" }),
            Resource("oni://space/analysis-targets", "building_control", "星图分析目标", "星图目的地分析状态和可选择望远镜目标。", "building_control domain=space_story kind=starmap_analysis action=list", new JObject { ["domain"] = "space_story", ["kind"] = "starmap_analysis", ["action"] = "list" }),
            Resource("oni://diagnostics/process-conditions", "building_control", "通用过程条件", "ConditionListSideScreen/IProcessConditionSet 条件状态、文本和 tooltip。", "building_control domain=space_story kind=process_conditions action=list", new JObject { ["domain"] = "space_story", ["kind"] = "process_conditions", ["action"] = "list" }),
            Resource("oni://dupes/bionic-upgrades", "dupes_control", "仿生人升级槽", "BionicSideScreen 仿生人升级槽、分配和安装状态；写入使用 dupes_control domain=assignable action=set_slot。", "dupes_control domain=side_screen action=bionic_upgrades", new JObject { ["domain"] = "side_screen", ["action"] = "bionic_upgrades" }),
            Resource("oni://rockets/missile-launchers", "building_control", "导弹发射器", "导弹发射器弹药允许状态。", "building_control domain=special kind=missile_launcher action=list", new JObject { ["domain"] = "special", ["kind"] = "missile_launcher", ["action"] = "list" }),
            Resource("oni://rockets/modules", "building_control", "火箭模块", "火箭模块顺序、移除、替换和添加能力状态。", "building_control domain=rocket rocketDomain=module action=list", new JObject { ["domain"] = "module", ["action"] = "list" }),
            Resource("oni://rockets/module-defs", "building_control", "火箭模块定义", "SelectModuleSideScreen 可选择火箭模块定义和条件状态。", "building_control domain=rocket rocketDomain=module action=list_defs", new JObject { ["domain"] = "module", ["action"] = "list_defs" }),
            Resource("oni://rockets/launch-pads", "building_control", "火箭发射台", "LaunchPadSideScreen 发射台、已停靠火箭和可降落火箭。", "building_control domain=rocket rocketDomain=ops action=list_launch_pads", new JObject { ["domain"] = "ops", ["action"] = "list_launch_pads" }),
            Resource("oni://rockets/flight-utilities", "building_control", "火箭飞行模块实用操作", "ModuleFlightUtilitySideScreen 清空/投放、自动投放、目标和复制人选择状态。", "building_control domain=rocket rocketDomain=flight_utility action=list", new JObject { ["domain"] = "flight_utility", ["action"] = "list" }),
            Resource("oni://rockets/restrictions", "building_control", "火箭控制台限制", "RocketRestrictionSideScreen 地面/太空使用限制状态。", "building_control domain=rocket rocketDomain=restriction action=list", new JObject { ["domain"] = "restriction", ["action"] = "list" }),
            Resource("oni://rockets/usage-controls", "building_control", "火箭内部建筑使用限制", "火箭内部建筑是否受 RocketControlStation 限制的玩家菜单状态。", "building_control domain=rocket rocketDomain=usage action=list", new JObject { ["domain"] = "usage", ["action"] = "list" }),
            Resource("oni://rockets/crew-requests", "building_control", "火箭乘员召集", "SummonCrewSideScreen 乘员召集/释放状态、登船人数和驾驶员状态。", "building_control domain=rocket rocketDomain=crew_request action=list", new JObject { ["domain"] = "crew_request", ["action"] = "list" }),
            Resource("oni://rockets/assignment-groups", "building_control", "分配组成员", "AssignmentGroupControllerSideScreen 分配组成员状态和复制人成员开关。", "building_control domain=rocket rocketDomain=assignment_group action=list", new JObject { ["domain"] = "assignment_group", ["action"] = "list" }),
            Resource("oni://rockets/cargo-collectors", "building_control", "火箭货舱收集器", "CargoModuleSideScreen 星图货舱收集模块容量、库存和收集进度。", "building_control domain=rocket rocketDomain=cargo_status action=collectors", new JObject { ["domain"] = "cargo_status", ["action"] = "collectors" }),
            Resource("oni://rockets/harvest-modules", "building_control", "火箭钻探模块", "HarvestModuleSideScreen 太空钻探模块钻探状态和钻石库存。", "building_control domain=rocket rocketDomain=cargo_status action=harvest_modules", new JObject { ["domain"] = "cargo_status", ["action"] = "harvest_modules" }),
            Resource("oni://rockets/railguns", "building_control", "轨道炮", "轨道炮发射质量、库存和辐射粒子能量状态。", "building_control domain=space_building kind=railgun action=list", new JObject { ["domain"] = "space_building", ["kind"] = "railgun", ["action"] = "list" }),
            Resource("oni://rockets/self-destruct", "building_control", "火箭自毁", "SelfDestructButtonSideScreen 可自毁火箭舱体。", "building_control domain=rocket rocketDomain=self_destruct action=list", new JObject { ["domain"] = "self_destruct", ["action"] = "list" }),
            Resource("oni://buildings/defs", "building_control", "可建造建筑定义", "建造菜单建筑定义、分类、材料需求、可用外观和搜索结果。", "building_control action=search_defs", new JObject { ["action"] = "search_defs" }),
            Resource("oni://buildings/materials", "building_control", "可用建造材料", "指定建筑当前世界可用的合法建造材料，按库存排序；用于 material=auto 或显式材料选择。", "building_control action=materials", new JObject { ["action"] = "materials" }),
            Resource("oni://buildings/configurables", "building_control", "可配置建筑", "支持启用、开关、阈值、门状态、门禁和补料的建筑。", "building_control action=list", new JObject { ["action"] = "list" }),
            Resource("oni://automation/controls", "building_control", "自动化控件", "逻辑/电力相关玩家可配置控件：端口、开关、阈值、阀门、计时器、ribbon bit。", "building_control action=list_automation", new JObject { ["action"] = "list_automation" }),
            Resource("oni://production/fabricators", "building_control", "生产制作站", "制作站/精炼/厨房等配方队列、当前订单和运行状态。", "building_control domain=production action=list_fabricators", new JObject { ["domain"] = "production", ["action"] = "list_fabricators" }),
            Resource("oni://production/recipes", "building_control", "生产配方", "制作站 ComplexRecipe 配方、材料、产物、解锁和队列数量。", "building_control domain=production action=list_recipes", new JObject { ["domain"] = "production", ["action"] = "list_recipes" }),
            Resource("oni://production/mutant-seed-controls", "building_control", "突变种子接收开关", "制作站、鱼喂食器和香料研磨器的接受/拒收突变种子玩家菜单开关。", "building_control domain=production action=mutant_seed_list", new JObject { ["domain"] = "production", ["action"] = "mutant_seed_list" }),
            Resource("oni://production/configurable-consumers", "building_control", "可配置消费者", "ConfigureConsumerSideScreen 当前选项、可选项和消耗材料。", "building_control domain=side_surface surface=misc kind=configurable_consumer action=list", new JObject { ["domain"] = "misc", ["kind"] = "configurable_consumer", ["action"] = "list" }),
            Resource("oni://rockets/status", "building_control", "火箭状态", "Spaced Out 火箭和基础版航天器状态。", "building_control domain=rocket rocketDomain=ops action=status", new JObject { ["domain"] = "ops", ["action"] = "status" }),
            Resource("oni://research/status", "colony_control", "研究状态", "当前研究状态。", "colony_control domain=management kind=research action=status", new JObject { ["domain"] = "management", ["kind"] = "research", ["action"] = "status" }),
            Resource("oni://schedules", "colony_control", "日程", "复制人日程。", "colony_control domain=management kind=schedule action=list", new JObject { ["domain"] = "management", ["kind"] = "schedule", ["action"] = "list" }),
            Resource("oni://dupes", "colony_control", "复制人", "复制人列表和基本状态。", "colony_control domain=read action=dupes", new JObject { ["domain"] = "read", ["action"] = "dupes" }),
            Resource("oni://dupes/priorities", "dupes_control", "复制人个人优先级", "Priorities/Jobs 管理屏中复制人对各 ChoreGroup 的个人工作优先级。", "dupes_control domain=priority action=list", new JObject { ["domain"] = "priority", ["action"] = "list" }),
            Resource("oni://dupes/priority-settings", "dupes_control", "复制人优先级设置", "Jobs/Priorities 管理屏全局高级模式开关、默认重置行为和重置后优先级状态。", "dupes_control domain=priority action=settings_get", new JObject { ["domain"] = "priority", ["action"] = "settings_get" }),
            Resource("oni://dupes/skills", "dupes_control", "复制人技能", "Skills 管理屏中的复制人技能点、已学技能和可学习技能。", "dupes_control domain=skill action=list", new JObject { ["domain"] = "skill", ["action"] = "list" }),
            Resource("oni://dupes/hats", "dupes_control", "复制人帽子", "Skills 管理屏中的当前帽子、目标帽子和可选帽子列表。", "dupes_control domain=hat action=list", new JObject { ["domain"] = "hat", ["action"] = "list" }),
            Resource("oni://dupes/status-check", "dupes_control", "复制人状态检查", "复制人位置、当前差事、关键需求、周边可达格和疑似被困风险；只读。", "dupes_control domain=info action=status_check", new JObject { ["domain"] = "info", ["action"] = "status_check" }),
            Resource("oni://dupes/direct-commands", "dupes_control", "复制人直接命令", "复制人可直接执行/配置的玩家操作入口。", "dupes_control domain=side_screen action=direct_commands", new JObject { ["domain"] = "side_screen", ["action"] = "direct_commands" }),
            Resource("oni://dupes/todos", "dupes_control", "复制人待办差事", "MinionTodoSideScreen 当前差事、可执行差事和阻塞差事。", "dupes_control domain=side_screen action=todos", new JObject { ["domain"] = "side_screen", ["action"] = "todos" }),
            Resource("oni://dupes/equipment", "dupes_control", "复制人装备", "复制人装备槽、当前装备和可用装备分配对象；写入使用 dupes_control domain=assignable action=set_slot。", "dupes_control domain=side_screen action=equipment", new JObject { ["domain"] = "side_screen", ["action"] = "equipment" }),
            Resource("oni://assignables", "dupes_control", "可分配对象", "床、医疗床、餐桌、太空服等可分配对象和当前分配。", "dupes_control domain=assignable action=list", new JObject { ["domain"] = "assignable", ["action"] = "list" }),
            Resource("oni://farming/planting", "colony_control", "种植槽", "种植箱、农砖、当前植物、请求种子和可接受种子。", "colony_control domain=bio bioDomain=farming action=list_planting", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "list_planting" }),
            Resource("oni://farming/harvestables", "colony_control", "可收获对象", "植物/作物的成熟、收获标记和成熟即收获状态。", "colony_control domain=bio bioDomain=farming action=list_harvestables", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "list_harvestables" }),
            Resource("oni://farming/seeds", "colony_control", "种子目录", "可用于种植请求的 PlantableSeed prefab。", "colony_control domain=bio bioDomain=farming action=seed_catalog", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "seed_catalog" }),
            Resource("oni://ranching/critters", "colony_control", "小动物", "可抓捕小动物、抓捕标记和捆绑状态。", "colony_control domain=bio bioDomain=ranching kind=critters action=critters", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "critters", ["action"] = "critters" }),
            Resource("oni://ranching/dropoffs", "colony_control", "小动物投放点", "小动物/鱼类投放点过滤器、容量和计数。", "colony_control domain=bio bioDomain=ranching kind=dropoff action=list", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "dropoff", ["action"] = "list" }),
            Resource("oni://ranching/incubators", "colony_control", "孵化器", "孵化器蛋请求、占用对象、进度和连续孵化设置。", "colony_control domain=bio bioDomain=ranching kind=incubator action=list", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "incubator", ["action"] = "list" }),
            Resource("oni://medical/patients", "colony_control", "医疗患者", "需要医疗关注的复制人、疾病、生命值和医疗床分配。", "colony_control domain=management kind=medical action=patients", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "patients" }),
            Resource("oni://medical/clinics", "colony_control", "医疗床和诊所", "医疗床/诊所治疗阈值、分配对象和优先级。", "colony_control domain=management kind=medical action=clinics", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "clinics" }),
            Resource("oni://medical/doctor-stations", "colony_control", "医生站", "医生站药品库存和可治疗患者。", "colony_control domain=management kind=medical action=doctor_stations", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "doctor_stations" }),
            Resource("oni://sandbox/actions", "game_control", "沙盒操作", "MCP 暴露的沙盒/Debug 操作、风险和当前沙盒状态。", "game_control domain=sandbox kind=read action=list_actions", new JObject { ["domain"] = "sandbox", ["kind"] = "read", ["action"] = "list_actions" }),
            Resource("oni://sandbox/story-traits", "game_control", "沙盒故事特质", "可由沙盒 Story Trait Tool 放置的故事特质模板。", "game_control domain=sandbox kind=read action=list_story_traits", new JObject { ["domain"] = "sandbox", ["kind"] = "read", ["action"] = "list_story_traits" }),
            Resource("oni://game/time", "game_control", "游戏时间和速度", "当前周期、时间百分比、暂停状态和速度。", "game_control domain=speed action=time", new JObject { ["domain"] = "speed", ["action"] = "time" }),
            Resource("oni://game/red-alert", "game_control", "红色警戒", "当前/全部世界红色警戒（紧急模式）状态。", "game_control domain=state action=red_alert_status", new JObject { ["domain"] = "state", ["action"] = "red_alert_status" }),
            Resource("oni://game/saves", "game_control", "存档文件", "本地/云端存档文件、当前 active save 和保存根目录。", "game_control domain=save action=list", new JObject { ["domain"] = "save", ["action"] = "list" }),
            Resource("oni://game/dlc", "game_control", "DLC 存档激活状态", "暂停菜单 DLC 激活按钮状态：订阅、当前存档启用、是否允许激活。", "game_control domain=dlc action=list", new JObject { ["domain"] = "dlc", ["action"] = "list" }),
            Resource("oni://mcp/sessions", "server_control", "MCP 会话", "当前 MCP session 和客户端 sampling、elicitation、tasks 能力。", "server_control domain=diagnostics action=capabilities", new JObject { ["domain"] = "diagnostics", ["action"] = "capabilities" }),
            Resource("oni://ui/actions", "game_control", "UI Action 白名单", "可安全触发的管理菜单、覆盖层、建造分类和导航 Action。", "game_control domain=ui uiDomain=action action=list", new JObject { ["domain"] = "ui", ["uiDomain"] = "action", ["action"] = "list" }),
            Resource("oni://tools/manifest", "server_control", "工具清单", "ONI MCP 工具目录。", "server_control domain=catalog action=manifest", new JObject { ["domain"] = "catalog", ["action"] = "manifest" }),
            Resource("oni://tools/guide", "server_control", "工具意图指南", "按玩家目标推荐资源、工具链和批量策略。", "server_control domain=catalog action=guide", new JObject { ["domain"] = "catalog", ["action"] = "guide" }),
            Resource("oni://guide/mechanics", "read_control", "缺氧机制速查", "结构化缺氧机制、公式、边界条件和工程注意事项；不包含攻略长文本。", "read_control domain=knowledge kind=guide action=query", new JObject { ["domain"] = "knowledge", ["kind"] = "guide", ["action"] = "query" }),
            Resource("oni://tools/player-action-coverage", "server_control", "玩家操作覆盖审计", "玩家可执行操作面、对应 MCP 工具和缺口状态。", "server_control domain=catalog action=coverage", new JObject { ["domain"] = "catalog", ["action"] = "coverage" }),
            Resource("oni://tools/side-screen-surfaces", "server_control", "侧屏 surface 审计", "运行时 SideScreenContent 类型到 MCP 工具/资源覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=side_screen", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "side_screen" }),
            Resource("oni://tools/user-menu-surfaces", "server_control", "用户菜单 surface 审计", "源码 UserMenu/context-menu 按钮来源到 MCP 工具/资源覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=user_menu", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "user_menu" }),
            Resource("oni://tools/management-surfaces", "server_control", "管理界面 surface 审计", "源码 ManagementMenu/TableScreen/全屏管理界面到 MCP 工具/资源覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=management", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "management" }),
            Resource("oni://tools/tool-menu-surfaces", "server_control", "工具栏 surface 审计", "源码 ToolMenu 主工具栏/沙盒工具栏到 MCP 工具/资源覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=tool_menu", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "tool_menu" }),
            Resource("oni://tools/ui-menu-surfaces", "server_control", "UI 菜单 surface 审计", "源码 OverlayMenu/PlanScreen/BuildMenu/安全 UI hotkey 到 MCP 工具/资源覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=ui_menu", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "ui_menu" }),
            Resource("oni://tools/global-control-surfaces", "server_control", "全局控制 surface 审计", "源码 SpeedControlScreen/TopLeftControlScreen/PauseScreen/Options/Locker 到 MCP 覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=global_control", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "global_control" }),
            Resource("oni://tools/notification-surfaces", "server_control", "通知 surface 审计", "源码 NotificationScreen/NotificationManager/消息通知到 MCP 覆盖的映射审计。", "server_control domain=catalog action=surface_audit surface=notification", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "notification" }),
            Resource("oni://tools/static-audit", "server_control", "静态接口审计", "工具注册、玩家操作覆盖、资源入口和危险工具确认参数的静态自检。", "server_control domain=catalog action=static_audit", new JObject { ["domain"] = "catalog", ["action"] = "static_audit" }),
            Resource("oni://power/summary", "read_control", "电力摘要", "当前世界电力系统摘要：发电机额定功率、消费者负载、电池容量和电量，按 circuitId 聚合。", "read_control domain=infrastructure action=power_summary", new JObject { ["domain"] = "infrastructure", ["action"] = "power_summary" }),
            Resource("oni://power/ports", "read_control", "电力接口", "指定区域内建筑的电力接口格：锚点、输入/输出端口、相对偏移和可接线状态。", "read_control domain=infrastructure action=power_ports", new JObject { ["domain"] = "infrastructure", ["action"] = "power_ports" }),
            Resource("oni://rooms/list", "read_control", "房间列表", "房间系统状态：房间类型、大小、边界、对象计数和房间效果，适合检查士气房间是否成型。", "read_control domain=infrastructure action=rooms", new JObject { ["domain"] = "infrastructure", ["action"] = "rooms" }),
            Resource("oni://thermal/overheat-risk", "read_control", "过热风险扫描", "建筑过热风险扫描：按当前格温和建筑过热温度差排序，发现即将过热或已经过热的设备。", "read_control domain=world action=thermal_overheat_risk", new JObject { ["domain"] = "world", ["action"] = "thermal_overheat_risk" }),
            Resource("oni://world/search", "read_control", "地图搜索", "按 query/条件在地图上搜索元素格、建筑、散落物和复制人，支持区域过滤和最近排序。", "read_control domain=world action=search", new JObject { ["domain"] = "world", ["action"] = "search" }),
            Resource("oni://world/coordinate-screenshot", "navigation_control", "坐标截图", "保存带 ONI 世界坐标网格和坐标文本的截图，返回本地路径和 HTTP URL，适合视觉模型直接识别坐标。", "navigation_control action=coordinate_screenshot", new JObject { ["action"] = "coordinate_screenshot" }),
            Resource("oni://world/layout-candidates", "read_control", "平面布局候选", "按用途扫描区域，返回房间/平台候选矩形、评分、需挖掘、需铺砖、危险格和连通性。", "read_control domain=world action=layout_candidates", new JObject { ["domain"] = "world", ["action"] = "layout_candidates" })
        };

        private static readonly List<McpResourceTemplateInfo> _templates = new List<McpResourceTemplateInfo>
        {
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/cell/{x}/{y}",
                Name = "read_control",
                Title = "世界格子",
                Description = "通过 read_control domain=world action=cell_info 读取指定地图格子的元素、质量、温度、病菌和可见性。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/search{?query,kinds,areaId,x1,y1,x2,y2,worldId,visibleOnly,state,solid,minMassKg,maxMassKg,minTempC,maxTempC,nearX,nearY,sort,limit,maxCells}",
                Name = "read_control",
                Title = "地图搜索",
                Description = "通过 read_control domain=world action=search 按 query/条件搜索地图元素格、建筑、散落物和复制人；支持 areaId/矩形/世界过滤、温度/质量/状态过滤和 nearX/nearY 最近排序。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/coordinate-screenshot{?areaId,x1,y1,x2,y2,worldId,view,focusCamera,paddingCells,showGrid,showCoordinates,includeCellLabels,step,waitFrames,filename}",
                Name = "navigation_control",
                Title = "坐标截图",
                Description = "移动相机到指定区域，叠加世界坐标格线和坐标文本并截图；返回 screenshot.url/latestUrl，供视觉模型直接读坐标。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://world/text-map{?areaId,x1,y1,x2,y2,worldId,visibleOnly,view,sparse,includeBuildings,includeItems,includeDupes,includeElements,includeSummary,detail,encoding,profile,format,elementLimit,objectLimit,maxCells}",
                Name = "read_control",
                Title = "世界文本地图",
                Description = "通过 read_control domain=world action=text_map 读取指定矩形区域或 areaId 的文本地图；作为低 token 扫描/无视觉能力兜底。需要图像坐标上下文时优先用 oni://world/coordinate-screenshot。",
                MimeType = "text/plain"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://power/summary{?worldId,includeDetails,limit}",
                Name = "read_control",
                Title = "电力摘要",
                Description = "通过 read_control domain=infrastructure action=power_summary 读取指定世界电力系统摘要；支持按世界 ID、明细开关和数量限制过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://power/ports{?worldId,areaId,x1,y1,x2,y2,query,limit}",
                Name = "read_control",
                Title = "电力接口",
                Description = "通过 read_control domain=infrastructure action=power_ports 读取指定区域内建筑的电力接口格；支持按世界 ID、区域和关键词过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rooms/list{?worldId,type,includeBuildings,includeCriteria,limit}",
                Name = "read_control",
                Title = "房间列表",
                Description = "通过 read_control domain=infrastructure action=rooms 读取房间系统状态；支持按世界 ID、房间类型、建筑明细和条件文本过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://thermal/overheat-risk{?worldId,marginC,includeNonOverheatable,minTempC,limit}",
                Name = "read_control",
                Title = "过热风险扫描",
                Description = "通过 read_control domain=world action=thermal_overheat_risk 读取建筑过热风险扫描；支持按世界 ID、温差阈值、非过热建筑和数量限制过滤。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/manifest{?query,group,mode,risk,detail,limit}",
                Name = "server_control",
                Title = "工具清单",
                Description = "通过 server_control domain=catalog action=manifest 读取完整或过滤后的工具清单；detail=brief/compact 用于低 token 清单。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/search{?query,group,mode,risk,detail,limit}",
                Name = "server_control",
                Title = "工具搜索",
                Description = "通过 server_control domain=catalog action=search 按关键词、分组、模式和风险低 token 检索工具；detail=brief 返回极简结果。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/guide{?goal,detail}",
                Name = "server_control",
                Title = "工具意图指南",
                Description = "通过 server_control domain=catalog action=guide 按目标生成低 token 工具使用指南，推荐资源、搜索词、工具链、批量策略和规划 harness 流程。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://guide/mechanics{?query,category,detail,limit}",
                Name = "read_control",
                Title = "缺氧机制速查",
                Description = "查询结构化缺氧机制/公式：热量、制氧、保鲜、养殖、电力、自动化、太空等；detail=brief/full。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/player-action-coverage{?query,group,status,detail,includeResources,includeHotkeys,limit}",
                Name = "server_control",
                Title = "玩家操作覆盖",
                Description = "按玩家操作面搜索 MCP 工具/资源覆盖；detail=brief 适合低 token 查询，detail=full 返回资源锚点。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/static-audit{?includeWarnings}",
                Name = "server_control",
                Title = "静态接口审计",
                Description = "读取工具注册、覆盖表、资源入口和危险工具确认参数的静态自检。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://game/saves{?type,limit}",
                Name = "game_control",
                Title = "存档文件",
                Description = "读取本地/云端存档文件；等价于 game_control domain=save action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://game/dlc{?includeCosmetic}",
                Name = "game_control",
                Title = "DLC 存档激活状态",
                Description = "读取暂停菜单 DLC 激活按钮状态；等价于 game_control domain=dlc action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://game/red-alert{?worldId,allWorlds}",
                Name = "game_control",
                Title = "红色警戒状态",
                Description = "通过 game_control domain=state action=red_alert_status 读取当前/指定/全部世界红色警戒（紧急模式）状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/side-screen-surfaces{?query,status,includeNoAction,limit}",
                Name = "server_control",
                Title = "侧屏 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=side_screen 按运行时 SideScreenContent 类型及辅助侧屏 KScreen 读取 MCP 工具/资源覆盖；status=review 可找缺口。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/user-menu-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "用户菜单 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=user_menu 按源码 UserMenu/context-menu 按钮来源读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/management-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "管理界面 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=management 按 ManagementMenu/TableScreen/全屏管理界面读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/tool-menu-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "工具栏 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=tool_menu 按 ToolMenu 主工具栏/沙盒工具栏读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/ui-menu-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "UI 菜单 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=ui_menu 按 OverlayMenu/BuildCategory/安全 UI hotkey 读取 MCP 工具/资源覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://colony/notifications{?query,includePending,limit}",
                Name = "colony_control",
                Title = "通知列表",
                Description = "读取当前 HUD 通知、消息、聚焦目标和可清除状态；等价于 colony_control domain=notification action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://resources/pins{?worldId,query,includeUnpinned,limit}",
                Name = "read_control",
                Title = "资源面板固定和通知",
                Description = "读取 AllResourcesScreen 资源行固定显示和通知开关状态；等价于 read_control domain=resources action=pins。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/global-control-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "全局控制 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=global_control 按速度、顶层 HUD、暂停菜单、设置和外部账号菜单读取 MCP 覆盖；detail=brief 适合低 token 查询。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://tools/notification-surfaces{?query,status,detail,limit}",
                Name = "server_control",
                Title = "通知 surface 覆盖",
                Description = "通过 server_control domain=catalog action=surface_audit surface=notification 按 NotificationScreen/NotificationManager/MessageNotification 读取 MCP 覆盖；detail=brief 适合低 token 查询。",
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
                Name = "building_control",
                Title = "可建造建筑定义",
                Description = "按关键词、分类和可用性搜索可建造建筑定义；用于建造菜单、材料/外观选择和蓝图规划。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/materials{?prefabId,worldId,includeUnavailable,limit}",
                Name = "building_control",
                Title = "可用建造材料",
                Description = "读取指定建筑在当前/指定世界的合法材料候选和库存；默认只返回可用材料，首项即 material=auto 会选择的材料。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/configurables{?areaId,x1,y1,x2,y2,worldId,capability,query,limit}",
                Name = "building_control",
                Title = "可配置建筑",
                Description = "按区域、能力和关键词读取支持玩家侧屏配置的建筑；等价于 building_control action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/lights{?areaId,x1,y1,x2,y2,worldId,query,configurableOnly,limit}",
                Name = "building_control",
                Title = "灯光控件",
                Description = "通过 building_control action=visual kind=light visualAction=list 按区域和关键词读取灯光建筑及其颜色预设。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/pixel-packs{?areaId,x1,y1,x2,y2,worldId,query,includePresets,limit}",
                Name = "building_control",
                Title = "Pixel Pack 控件",
                Description = "通过 building_control action=visual kind=pixel_pack visualAction=list 按区域和关键词读取 Pixel Pack 颜色面板和预设。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://geotuners{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "GeoTuner",
                Description = "按区域和关键词读取 GeoTuner 分配状态；等价于 building_control domain=side_surface surface=geo_tuner action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://geotuners/geysers{?id,x,y,worldId,query,includeUnstudied,limit}",
                Name = "building_control",
                Title = "GeoTuner 喷泉目标",
                Description = "读取 GeoTuner 同世界可选择喷泉及分配约束；等价于 building_control domain=side_surface surface=geo_tuner action=list_geysers。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/artables{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "艺术建筑外观",
                Description = "通过 building_control domain=special kind=artable action=list 按区域和关键词读取艺术建筑外观选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/monument-parts{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "纪念碑部件外观",
                Description = "通过 building_control domain=special kind=monument_part action=list 按区域和关键词读取纪念碑部件外观选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/lures{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "生物诱饵站",
                Description = "通过 building_control domain=special kind=creature_lure action=list 按区域和关键词读取生物诱饵站诱饵选择。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/gene-shufflers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "Gene Shuffler",
                Description = "通过 building_control domain=special kind=gene_shuffler action=list 按区域和关键词读取 Gene Shuffler 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/printerceptors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "Printerceptor",
                Description = "按区域和关键词读取 PrinterceptorSideScreen 状态；等价于 building_control domain=story_facility kind=printerceptor action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/remote-work-terminals{?areaId,x1,y1,x2,y2,worldId,query,includeDocks,limit}",
                Name = "building_control",
                Title = "远程工作终端",
                Description = "按区域和关键词读取 RemoteWorkTerminalSidescreen dock 选择；等价于 building_control domain=story_facility kind=remote_work_terminal action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/genetic-analysis-stations{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "Botanical Analyzer",
                Description = "按区域和关键词读取 Botanical Analyzer 种子允许/禁用状态；等价于 building_control domain=story_facility kind=genetic_analysis_station action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/dispensers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "分发器",
                Description = "按区域和关键词读取 DispenserSideScreen 状态；等价于 building_control domain=side_surface surface=facility kind=dispenser action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/receptacles{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "实体陈列/插槽",
                Description = "按区域和关键词读取 ReceptacleSideScreen / SpecialCargoBayClusterSideScreen / SingleEntityReceptacle 状态和可请求实体；等价于 building_control domain=receptacle action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/suit-lockers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "太空服柜",
                Description = "按区域和关键词读取 SuitLockerSideScreen 状态；等价于 building_control domain=side_surface surface=facility kind=suit_locker action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/poi-tech-unlocks{?areaId,x1,y1,x2,y2,worldId,query,pendingOnly,lockedOnly,limit}",
                Name = "building_control",
                Title = "信息传送通道",
                Description = "按区域和关键词读取 Research Portal/信息传送通道解锁差事、进度和解锁项；等价于 building_control domain=story_facility kind=poi_tech_unlock action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/lore-bearers{?areaId,x1,y1,x2,y2,worldId,query,interactableOnly,limit}",
                Name = "building_control",
                Title = "LoreBearer",
                Description = "按区域和关键词读取可阅读/已阅读 LoreBearer；等价于 building_control domain=side_surface surface=facility kind=lore_bearer action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/telepads{?areaId,x1,y1,x2,y2,worldId,query,includeVictory,limit}",
                Name = "building_control",
                Title = "Printing Pod / Telepad",
                Description = "按区域和关键词读取 TelepadSideScreen 移民、研究、技能和胜利条件状态；等价于 building_control domain=side_surface surface=facility kind=telepad action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/artifacts{?areaId,x1,y1,x2,y2,worldId,query,includeStations,includeWorldArtifacts,limit}",
                Name = "building_control",
                Title = "Artifact Analysis",
                Description = "读取已分析 artifact、场上 artifact 和 Artifact Analysis Station 状态；等价于 building_control domain=side_surface surface=facility kind=artifact action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/warp-portals{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "Warp Portal",
                Description = "按区域和关键词读取 WarpPortalSideScreen 状态；等价于 building_control domain=space_story kind=warp_portal action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://story/temporal-tears{?query,limit}",
                Name = "building_control",
                Title = "Temporal Tear",
                Description = "读取时间裂隙状态和可进入/可消耗火箭；等价于 building_control domain=space_story kind=temporal_tear action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://space/telescopes{?areaId,x1,y1,x2,y2,worldId,query,includeTargets,limit}",
                Name = "building_control",
                Title = "Telescope",
                Description = "按区域和关键词读取望远镜和星图分析目标；等价于 building_control domain=space_story kind=telescope action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://space/analysis-targets{?query,includeComplete,limit}",
                Name = "building_control",
                Title = "星图分析目标",
                Description = "读取可由望远镜分析的星图目的地；等价于 building_control domain=space_story kind=starmap_analysis action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://diagnostics/process-conditions{?id,x,y,worldId,conditionType,query,showHidden,limit}",
                Name = "building_control",
                Title = "通用过程条件",
                Description = "读取 IProcessConditionSet/ConditionListSideScreen 条件列表；等价于 building_control domain=space_story kind=process_conditions action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/bionic-upgrades{?id,name,query,limit}",
                Name = "dupes_control",
                Title = "仿生人升级槽",
                Description = "读取 BionicSideScreen 升级槽、分配和安装状态；等价于 dupes_control domain=side_screen action=bionic_upgrades。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/missile-launchers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "导弹发射器",
                Description = "通过 building_control domain=special kind=missile_launcher action=list 按区域和关键词读取导弹发射器弹药允许状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/modules{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "火箭模块",
                Description = "通过 building_control domain=rocket rocketDomain=module action=list 按区域和关键词读取可重排火箭模块及操作能力。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/module-defs{?id,x,y,worldId,mode,query,limit}",
                Name = "building_control",
                Title = "火箭模块定义",
                Description = "通过 building_control domain=rocket rocketDomain=module action=list_defs 读取 SelectModuleSideScreen 火箭模块定义和 add/replace 条件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/launch-pads{?query,limit}",
                Name = "building_control",
                Title = "火箭发射台",
                Description = "通过 building_control domain=rocket rocketDomain=ops action=list_launch_pads 读取 LaunchPadSideScreen 发射台、已停靠火箭和可降落火箭。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/flight-utilities{?rocketId,rocketName,query,includeDupes,limit}",
                Name = "building_control",
                Title = "火箭飞行模块实用操作",
                Description = "通过 building_control domain=rocket rocketDomain=flight_utility action=list 读取 ModuleFlightUtilitySideScreen 清空/投放、自动投放、目标和复制人选择状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/restrictions{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "火箭控制台限制",
                Description = "通过 building_control domain=rocket rocketDomain=restriction action=list 按区域和关键词读取 RocketRestrictionSideScreen 状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/usage-controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "火箭内部建筑使用限制",
                Description = "通过 building_control domain=rocket rocketDomain=usage action=list 按区域和关键词读取火箭内部建筑是否受控制台限制。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/crew-requests{?rocketId,rocketName,query,limit}",
                Name = "building_control",
                Title = "火箭乘员召集",
                Description = "通过 building_control domain=rocket rocketDomain=crew_request action=list 读取 SummonCrewSideScreen 乘员召集/释放状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/assignment-groups{?groupId,controllerId,query,includeDupes,limit}",
                Name = "building_control",
                Title = "分配组成员",
                Description = "通过 building_control domain=rocket rocketDomain=assignment_group action=list 读取 AssignmentGroupControllerSideScreen 分配组及复制人成员开关状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/cargo-collectors{?rocketId,rocketName,query,limit}",
                Name = "building_control",
                Title = "火箭货舱收集器",
                Description = "通过 building_control domain=rocket rocketDomain=cargo_status action=collectors 读取 CargoModuleSideScreen 收集模块容量、库存和进度。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/harvest-modules{?rocketId,rocketName,query,limit}",
                Name = "building_control",
                Title = "火箭钻探模块",
                Description = "通过 building_control domain=rocket rocketDomain=cargo_status action=harvest_modules 读取 HarvestModuleSideScreen 钻探状态和钻石库存。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "自动化控件",
                Description = "按区域和关键词读取逻辑/电力相关玩家可配置控件；等价于 building_control action=list_automation。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/automatable{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "自动化专用搬运",
                Description = "通过 building_control domain=side_surface surface=automation kind=automatable action=list 按区域和关键词读取 AutomatableSideScreen 允许手动搬运状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/critter-sensors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "小动物计数传感器",
                Description = "通过 building_control domain=side_surface surface=automation kind=critter_sensor action=list 按区域和关键词读取 CritterSensorSideScreen 小动物/蛋计数开关。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://storage/list{?resource,worldId,limit}",
                Name = "building_control",
                Title = "储存列表",
                Description = "按资源、世界和数量读取储存建筑列表；等价于 building_control domain=storage action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://storage/tile-selections{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "储存砖目标物品",
                Description = "按区域和关键词读取 SingleItemSelectionSideScreen / StorageTile 目标物品选择；等价于 building_control domain=tile_selection action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://filters/controls{?areaId,x1,y1,x2,y2,worldId,kind,query,includeOptions,limit}",
                Name = "building_control",
                Title = "过滤器控件",
                Description = "按区域、类型和关键词读取单选/多选过滤器控件；等价于 building_control domain=filter action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/options{?areaId,x1,y1,x2,y2,worldId,kind,query,includeOptions,limit}",
                Name = "building_control",
                Title = "选项型侧屏控件",
                Description = "按区域、类型和关键词读取方向、少量选项、广播频道和辐射粒子方向控件；等价于 building_control domain=side_surface surface=option action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/state{?areaId,x1,y1,x2,y2,worldId,kind,query,limit}",
                Name = "building_control",
                Title = "状态型侧屏控件",
                Description = "按区域、类型和关键词读取容量、checkbox、计数器和时间范围控件；等价于 building_control action=state_list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/activation-ranges{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "启停双阈值控件",
                Description = "按区域和关键词读取 ActiveRangeSideScreen / IActivationRangeTarget 双阈值控件；等价于 building_control domain=side_surface surface=activation action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/progress-bars{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "侧屏进度条",
                Description = "按区域和关键词读取 ProgressBarSideScreen / IProgressBarSideScreen 只读进度条状态；等价于 building_control domain=side_surface kind=progress action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/buttons{?areaId,x1,y1,x2,y2,worldId,query,interactableOnly,limit}",
                Name = "building_control",
                Title = "通用侧屏按钮",
                Description = "按区域和关键词读取 ISidescreenButtonControl 通用侧屏按钮；等价于 building_control domain=side_surface kind=button action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/user-menu-actions{?areaId,x1,y1,x2,y2,worldId,category,query,limit}",
                Name = "building_control",
                Title = "对象用户菜单操作",
                Description = "按区域、分类和关键词读取对象 UserMenu/context-menu 按钮映射；等价于 building_control domain=side_surface surface=user_menu action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/maintenance-actions{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "维护类用户菜单操作",
                Description = "通过 building_control domain=side_surface surface=maintenance action=list 按区域和关键词读取需要状态机/槽位参数的玩家维护操作。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/checklists{?areaId,x1,y1,x2,y2,worldId,query,checkedOnly,enabledOnly,limit}",
                Name = "building_control",
                Title = "侧屏清单",
                Description = "按区域和关键词读取 ICheckboxListGroupControl 故事任务、条件和设施清单；等价于 building_control domain=side_surface kind=checklist action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/related-entities{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "关联对象",
                Description = "按区域和关键词读取 IRelatedEntities 侧屏关联对象及可点击跳转目标；等价于 building_control domain=side_surface kind=related action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://controls/n-toggles{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "多选侧屏控件",
                Description = "通过 building_control domain=side_surface surface=misc kind=n_toggle action=list 按区域和关键词读取 INToggleSideScreenControl 多选控件。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/logic-alarms{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "逻辑报警器",
                Description = "通过 building_control domain=side_surface surface=misc kind=logic_alarm action=list 按区域和关键词读取 Logic Alarm 通知设置。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/comet-detectors{?areaId,x1,y1,x2,y2,worldId,query,includeOptions,limit}",
                Name = "building_control",
                Title = "彗星探测器",
                Description = "通过 building_control domain=space_building kind=comet_detector action=list 按区域和关键词读取彗星探测器目标和可选目标。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://automation/cluster-location-sensors{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "星图位置传感器",
                Description = "通过 building_control domain=space_building kind=cluster_location_sensor action=list 按区域和关键词读取星图位置传感器过滤设置。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/railguns{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "轨道炮",
                Description = "通过 building_control domain=space_building kind=railgun action=list 按区域和关键词读取轨道炮发射质量、库存和能量状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://buildings/turbo-heaters{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "液体加热器涡轮模式",
                Description = "通过 building_control domain=side_surface surface=misc kind=turbo_heater action=list 按区域和关键词读取 Liquid Tepidizer TurboModeSideScreen。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://rockets/self-destruct{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "火箭自毁",
                Description = "通过 building_control domain=rocket rocketDomain=self_destruct action=list 按区域和关键词读取可自毁火箭舱体。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/fabricators{?areaId,x1,y1,x2,y2,worldId,query,queuedOnly,includeRecipes,limit}",
                Name = "building_control",
                Title = "生产制作站",
                Description = "按区域和关键词读取制作站队列、订单和运行状态；等价于 building_control domain=production action=list_fabricators。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/recipes{?id,x,y,areaId,x1,y1,x2,y2,worldId,recipeId,categoryId,query,queuedOnly,includeLocked,limit}",
                Name = "building_control",
                Title = "生产配方",
                Description = "按制作站、区域和关键词读取配方材料、产物和队列数量；等价于 building_control domain=production action=list_recipes。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/mutant-seed-controls{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "突变种子接收开关",
                Description = "按区域和关键词读取接受/拒收突变种子的玩家菜单开关；等价于 building_control domain=production action=mutant_seed_list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://production/configurable-consumers{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "building_control",
                Title = "可配置消费者",
                Description = "通过 building_control domain=side_surface surface=misc kind=configurable_consumer action=list 按区域和关键词读取 ConfigureConsumerSideScreen 选项。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/planting{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "colony_control",
                Title = "种植槽",
                Description = "按区域和关键词读取可种植建筑、当前植物和种植请求；等价于 colony_control domain=bio bioDomain=farming action=list_planting。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/harvestables{?areaId,x1,y1,x2,y2,worldId,query,readyOnly,limit}",
                Name = "colony_control",
                Title = "可收获对象",
                Description = "按区域和关键词读取植物/作物收获状态；等价于 colony_control domain=bio bioDomain=farming action=list_harvestables。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://farming/seeds{?query,limit}",
                Name = "colony_control",
                Title = "种子目录",
                Description = "搜索可种植种子 prefab/tag；等价于 colony_control domain=bio bioDomain=farming action=seed_catalog。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/critters{?areaId,x1,y1,x2,y2,worldId,query,capturableOnly,wrangledOnly,limit}",
                Name = "colony_control",
                Title = "小动物",
                Description = "通过 colony_control domain=bio bioDomain=ranching kind=critters action=critters 按区域和关键词读取可抓捕对象状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/dropoffs{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "colony_control",
                Title = "小动物投放点",
                Description = "通过 colony_control domain=bio bioDomain=ranching kind=dropoff action=list 按区域和关键词读取小动物/鱼类投放点。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ranching/incubators{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "colony_control",
                Title = "孵化器",
                Description = "通过 colony_control domain=bio bioDomain=ranching kind=incubator action=list 按区域和关键词读取孵化器状态。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/clinics{?areaId,x1,y1,x2,y2,worldId,limit}",
                Name = "colony_control",
                Title = "医疗床和诊所",
                Description = "按区域读取医疗床/诊所状态和阈值；等价于 colony_control domain=management kind=medical action=clinics。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://sandbox/story-traits{?query}",
                Name = "game_control",
                Title = "沙盒故事特质",
                Description = "读取可由沙盒 Story Trait Tool 放置的故事特质模板；等价于 game_control domain=sandbox kind=read action=list_story_traits。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/patients{?worldId,includeHealthy,query,limit}",
                Name = "colony_control",
                Title = "医疗患者",
                Description = "读取复制人疾病、生命值和医疗分配状态；等价于 colony_control domain=management kind=medical action=patients。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://medical/doctor-stations{?areaId,x1,y1,x2,y2,worldId,query,limit}",
                Name = "colony_control",
                Title = "医生站",
                Description = "按区域读取医生站药品库存和可治疗患者；等价于 colony_control domain=management kind=medical action=doctor_stations。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/direct-commands{?id,name,limit}",
                Name = "dupes_control",
                Title = "复制人直接命令",
                Description = "读取复制人的直接操作入口和对应 MCP 工具；等价于 dupes_control domain=side_screen action=direct_commands。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/todos{?id,name,query,includeBlocked,includePotentialOnly,limit,taskLimit}",
                Name = "dupes_control",
                Title = "复制人待办差事",
                Description = "读取 MinionTodoSideScreen 当前差事、可执行差事和阻塞差事；等价于 dupes_control domain=side_screen action=todos。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/equipment{?id,name,slotId,includeAvailable}",
                Name = "dupes_control",
                Title = "复制人装备",
                Description = "读取复制人装备槽、当前装备和可用装备；等价于 dupes_control domain=side_screen action=equipment。写入使用 dupes_control domain=assignable action=set_slot。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/priorities{?id,name,choreGroup,includeNonUserPrioritizable}",
                Name = "dupes_control",
                Title = "复制人个人优先级",
                Description = "读取 Priorities/Jobs 管理屏中复制人对各 ChoreGroup 的个人工作优先级；等价于 dupes_control domain=priority action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/hats{?id,name}",
                Name = "dupes_control",
                Title = "复制人帽子",
                Description = "读取 Skills 管理屏中的当前帽子、目标帽子和可选帽子列表；等价于 dupes_control domain=hat action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/skills{?id,name,query,limit}",
                Name = "dupes_control",
                Title = "复制人技能",
                Description = "读取 Skills 管理屏中的技能树、技能点和可学习状态；等价于 dupes_control domain=skill action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://dupes/priority-settings",
                Name = "dupes_control",
                Title = "复制人优先级设置",
                Description = "读取 Jobs/Priorities 管理屏全局高级模式开关和重置行为；等价于 dupes_control domain=priority action=settings_get。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://ui/actions{?kind}",
                Name = "game_control",
                Title = "UI Action 白名单",
                Description = "按类型读取可安全触发的 UI Action；等价于 game_control domain=ui uiDomain=action action=list。",
                MimeType = "application/json"
            },
            new McpResourceTemplateInfo
            {
                UriTemplate = "oni://sandbox/cell/{x}/{y}",
                Name = "game_control",
                Title = "沙盒格子取样",
                Description = "读取指定格子的沙盒刷子参数；等价于 game_control domain=sandbox kind=read action=sample_cell。",
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
                return ReadToolResource(uri, resource.ToolName, resource.Arguments != null ? new JObject(resource.Arguments) : new JObject(), resource.Info.MimeType);

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
                    return ReadToolResource(uri, "read_control", new JObject
                    {
                        ["domain"] = "world",
                        ["action"] = "cell_info",
                        ["x"] = parts[1],
                        ["y"] = parts[2]
                    }, "application/json");
                }
            }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/text-map")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "world";
                query["action"] = "text_map";
                return ReadToolResource(uri, "read_control", query, "text/plain");
            }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/search")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "world";
                query["action"] = "search";
                return ReadToolResource(uri, "read_control", query, "application/json");
            }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/coordinate-screenshot")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "coordinate_screenshot";
                return ReadToolResource(uri, "navigation_control", query, "application/json");
            }

            if (parsed.Host == "power" && parsed.AbsolutePath == "/summary")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "infrastructure";
                query["action"] = "power_summary";
                return ReadToolResource(uri, "read_control", query, "application/json");
            }

            if (parsed.Host == "power" && parsed.AbsolutePath == "/ports")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "infrastructure";
                query["action"] = "power_ports";
                return ReadToolResource(uri, "read_control", query, "application/json");
            }

            if (parsed.Host == "rooms" && parsed.AbsolutePath == "/list")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "infrastructure";
                query["action"] = "rooms";
                return ReadToolResource(uri, "read_control", query, "application/json");
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
                query["domain"] = "catalog";
                query["action"] = "guide";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "guide" && parsed.AbsolutePath == "/mechanics")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "knowledge";
                query["kind"] = "guide";
                query["action"] = "query";
                return ReadToolResource(uri, "read_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/manifest")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "manifest";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/search")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "search";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/player-action-coverage")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "coverage";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/side-screen-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "side_screen";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/user-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "user_menu";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/management-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "management";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/tool-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "tool_menu";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/ui-menu-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "ui_menu";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/global-control-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "global_control";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/notification-surfaces")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "surface_audit";
                query["surface"] = "notification";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/static-audit")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "catalog";
                query["action"] = "static_audit";
                return ReadToolResource(uri, "server_control", query, "application/json");
            }

            if (parsed.Host == "colony" && parsed.AbsolutePath == "/notifications")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "notification";
                query["action"] = "list";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "resources" && parsed.AbsolutePath == "/pins")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "resources";
                query["action"] = "pins";
                return ReadToolResource(uri, "read_control", query, "application/json");
            }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/saves")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "save";
                query["action"] = "list";
                return ReadToolResource(uri, "game_control", query, "application/json");
            }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/dlc")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "dlc";
                query["action"] = "list";
                return ReadToolResource(uri, "game_control", query, "application/json");
            }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/red-alert")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "state";
                query["action"] = "red_alert_status";
                return ReadToolResource(uri, "game_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/defs")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "search_defs";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/materials")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "materials";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/configurables")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "state_list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/lights")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "visual";
                query["kind"] = "light";
                query["visualAction"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/pixel-packs")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "visual";
                query["kind"] = "pixel_pack";
                query["visualAction"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "geotuners" && (parsed.AbsolutePath == "" || parsed.AbsolutePath == "/"))
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "geo_tuner";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "geotuners" && parsed.AbsolutePath == "/geysers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "geo_tuner";
                query["action"] = "list_geysers";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/artables")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "special";
                query["kind"] = "artable";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/monument-parts")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "special";
                query["kind"] = "monument_part";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/lures")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "special";
                query["kind"] = "creature_lure";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/gene-shufflers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "special";
                query["kind"] = "gene_shuffler";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/printerceptors")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "story_facility";
                query["kind"] = "printerceptor";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/remote-work-terminals")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "story_facility";
                query["kind"] = "remote_work_terminal";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/genetic-analysis-stations")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "story_facility";
                query["kind"] = "genetic_analysis_station";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/dispensers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "facility";
                query["kind"] = "dispenser";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/receptacles")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "receptacle";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/suit-lockers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "facility";
                query["kind"] = "suit_locker";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/poi-tech-unlocks")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "story_facility";
                query["kind"] = "poi_tech_unlock";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/lore-bearers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "facility";
                query["kind"] = "lore_bearer";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/telepads")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "facility";
                query["kind"] = "telepad";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/artifacts")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "facility";
                query["kind"] = "artifact";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/warp-portals")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_story";
                query["kind"] = "warp_portal";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/temporal-tears")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_story";
                query["kind"] = "temporal_tear";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/telescopes")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_story";
                query["kind"] = "telescope";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/analysis-targets")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_story";
                query["kind"] = "starmap_analysis";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "diagnostics" && parsed.AbsolutePath == "/process-conditions")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_story";
                query["kind"] = "process_conditions";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/bionic-upgrades")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "side_screen";
                query["action"] = "bionic_upgrades";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/missile-launchers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "special";
                query["kind"] = "missile_launcher";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/modules")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "module";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/module-defs")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "module";
                query["action"] = "list_defs";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/launch-pads")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "ops";
                query["action"] = "list_launch_pads";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/flight-utilities")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "flight_utility";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/restrictions")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "restriction";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/usage-controls")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "usage";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/crew-requests")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "crew_request";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/assignment-groups")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "assignment_group";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/cargo-collectors")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "cargo_status";
                query["action"] = "collectors";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/harvest-modules")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "cargo_status";
                query["action"] = "harvest_modules";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/controls")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "list_automation";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/automatable")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "automation";
                query["kind"] = "automatable";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/critter-sensors")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "automation";
                query["kind"] = "critter_sensor";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "storage" && parsed.AbsolutePath == "/list")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "storage";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "storage" && parsed.AbsolutePath == "/tile-selections")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "tile_selection";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "filters" && parsed.AbsolutePath == "/controls")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "filter";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/options")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "option";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/state")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/activation-ranges")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "activation";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/progress-bars")
            {
                var query = ParseQuery(parsed.Query);
                query["kind"] = "progress";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/buttons")
            {
                var query = ParseQuery(parsed.Query);
                query["kind"] = "button";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/user-menu-actions")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "list";
                query["domain"] = "user_menu";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/maintenance-actions")
            {
                var query = ParseQuery(parsed.Query);
                query["action"] = "list";
                query["domain"] = "maintenance";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/checklists")
            {
                var query = ParseQuery(parsed.Query);
                query["kind"] = "checklist";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/related-entities")
            {
                var query = ParseQuery(parsed.Query);
                query["kind"] = "related";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/n-toggles")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "misc";
                query["kind"] = "n_toggle";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/logic-alarms")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "misc";
                query["kind"] = "logic_alarm";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/comet-detectors")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_building";
                query["kind"] = "comet_detector";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/cluster-location-sensors")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_building";
                query["kind"] = "cluster_location_sensor";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/railguns")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "space_building";
                query["kind"] = "railgun";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/turbo-heaters")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "misc";
                query["kind"] = "turbo_heater";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/self-destruct")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "self_destruct";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/fabricators")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "production";
                query["action"] = "list_fabricators";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/recipes")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "production";
                query["action"] = "list_recipes";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/mutant-seed-controls")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "production";
                query["action"] = "mutant_seed_list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/configurable-consumers")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "misc";
                query["kind"] = "configurable_consumer";
                query["action"] = "list";
                return ReadToolResource(uri, "building_control", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/planting")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "farming";
                query["action"] = "list_planting";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/harvestables")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "farming";
                query["action"] = "list_harvestables";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/seeds")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "farming";
                query["action"] = "seed_catalog";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/critters")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "ranching";
                query["kind"] = "critters";
                query["action"] = "critters";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/dropoffs")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "ranching";
                query["kind"] = "dropoff";
                query["action"] = "list";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/incubators")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "bio";
                query["bioDomain"] = "ranching";
                query["kind"] = "incubator";
                query["action"] = "list";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/clinics")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "management";
                query["kind"] = "medical";
                query["action"] = "clinics";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/patients")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "management";
                query["kind"] = "medical";
                query["action"] = "patients";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/doctor-stations")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "management";
                query["kind"] = "medical";
                query["action"] = "doctor_stations";
                return ReadToolResource(uri, "colony_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/direct-commands")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "side_screen";
                query["action"] = "direct_commands";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/todos")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "side_screen";
                query["action"] = "todos";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/equipment")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "side_screen";
                query["action"] = "equipment";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priorities")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "priority";
                query["action"] = "list";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/hats")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "hat";
                query["action"] = "list";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/skills")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "skill";
                query["action"] = "list";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priority-settings")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "priority";
                query["action"] = "settings_get";
                return ReadToolResource(uri, "dupes_control", query, "application/json");
            }

            if (parsed.Host == "ui" && parsed.AbsolutePath == "/actions")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "ui";
                query["uiDomain"] = "action";
                query["action"] = "list";
                return ReadToolResource(uri, "game_control", query, "application/json");
            }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath == "/story-traits")
            {
                var query = ParseQuery(parsed.Query);
                query["domain"] = "sandbox";
                query["kind"] = "read";
                query["action"] = "list_story_traits";
                return ReadToolResource(uri, "game_control", query, "application/json");
            }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath.StartsWith("/cell/"))
            {
                var parts = parsed.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 3)
                {
                    return ReadToolResource(uri, "game_control", new JObject
                    {
                        ["domain"] = "sandbox",
                        ["kind"] = "read",
                        ["action"] = "sample_cell",
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
            NormalizeResourceArguments(toolName, arguments);
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

        private static void NormalizeResourceArguments(string toolName, JObject arguments)
        {
            if (arguments == null)
                return;

            NormalizeColonyArguments(toolName, arguments);
            NormalizeBuildingArguments(toolName, arguments);
            NormalizeGameArguments(toolName, arguments);
        }

        private static void NormalizeGameArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "game_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            switch (domain)
            {
                case "action":
                case "ui_action":
                case "actions":
                case "feedback":
                case "hint":
                case "hints":
                    arguments["uiDomain"] = domain;
                    arguments["domain"] = "ui";
                    return;
            }
        }

        private static void NormalizeColonyArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "colony_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (domain == "farming" || domain == "ranching")
            {
                arguments["kind"] = domain;
                arguments["domain"] = "bio";
            }
        }

        private static void NormalizeBuildingArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "building_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (IsSideSurfaceDomain(domain))
            {
                arguments["surface"] = domain;
                arguments["domain"] = "side_surface";
                return;
            }

            if (IsRocketDomain(domain))
            {
                arguments["rocketDomain"] = domain;
                arguments["domain"] = "rocket";
                return;
            }

            if (string.IsNullOrEmpty(domain))
            {
                string kind = (arguments["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                if (IsGenericSideSurfaceKind(kind))
                    arguments["domain"] = "side_surface";
            }
        }

        private static bool IsSideSurfaceDomain(string domain)
        {
            switch (domain)
            {
                case "generic":
                case "option":
                case "activation":
                case "automation":
                case "facility":
                case "misc":
                case "geo_tuner":
                case "user_menu":
                case "maintenance":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGenericSideSurfaceKind(string kind)
        {
            switch (kind)
            {
                case "button":
                case "buttons":
                case "checklist":
                case "checklists":
                case "progress":
                case "progress_bar":
                case "progress_bars":
                case "related":
                case "related_entity":
                case "related_entities":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsRocketDomain(string domain)
        {
            switch (domain)
            {
                case "ops":
                case "module":
                case "flight_utility":
                case "restriction":
                case "usage":
                case "crew_request":
                case "assignment_group":
                case "cargo_status":
                case "self_destruct":
                    return true;
                default:
                    return false;
            }
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

        private static OniResource Resource(string uri, string name, string title, string description, string toolName, JObject arguments)
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
                ToolName = toolName,
                Arguments = arguments
            };
        }

        private class OniResource
        {
            public McpResourceInfo Info { get; set; }
            public string ToolName { get; set; }
            public JObject Arguments { get; set; }
        }
    }
}
