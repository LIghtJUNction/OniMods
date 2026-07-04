using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static void AddResourceTemplatesPart2(List<McpResourceTemplateInfo> templates)
        {
            templates.AddRange(new[]
            {
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
                                    }
            });
        }
    }
}
