using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolCoverageTools
    {
        private static List<CoverageRow> BuildRows()
        {
            return new List<CoverageRow>
            {
                Row("game", "pause_resume_speed_sandbox_save_load_quit_dlc", "暂停、继续、调速、沙盒模式开关、列出存档、保存/另存为、确认载入存档、退出到主菜单或桌面、读取并激活当前存档可编辑 DLC", "covered", "game_control domain=speed", "game_control domain=speed action=time", "game_control domain=state", "game_control", "game_control domain=save", "game_control domain=dlc"),
                Row("camera", "camera_navigation", "移动/聚焦/切换世界/切换视图/截图", "covered", "navigation_control"),
                Row("world", "inspect_world_cells", "检查格子、元素统计、文本地图", "covered", "read_control domain=world action=cell_info", "read_control domain=world action=element_summary", "read_control domain=world action=text_map"),
                Row("areas", "area_handles", "定义、读取、列出、分块、拼接和遗忘地图区域句柄", "covered", "read_control domain=area"),
                Row("orders", "dig_sweep_mop_disinfect_attack_priority", "挖掘、清扫、拖地、消毒、取消、收获、攻击、区域优先级", "covered", "orders_control domain=area", "orders_control domain=designation action=attack", "orders_control domain=priority"),
                Row("orders", "cancel_orders", "取消建筑/挖掘/清扫/收获/攻击/抓捕等差事", "covered", "read_control domain=world action=text_map", "orders_control domain=area action=cancel"),
                Row("buildings", "build_plan_place", "搜索可建造物、材料和建筑外观，并通过可视 agent 指针点击/拖拽放置蓝图", "covered", "building_control domain=planning", "navigation_control"),
                Row("buildings", "deconstruct_priority_conduit_jobs", "设置建筑优先级、拆除建筑/管线、清空管线/运输轨道", "covered", "read_control domain=buildings action=list", "building_control domain=config", "orders_control domain=priority", "orders_control domain=designation action=deconstruct", "orders_control domain=designation action=cut_conduits", "orders_control domain=designation action=empty_conduits"),
                Row("buildings", "building_toggles_sliders_copy_settings", "建筑启用、玩家手动开关、通用 slider、阈值传感器、启停双阈值、方向/少量选项、容量、checkbox、灯光颜色、Pixel Pack 颜色、门状态、门禁、补料阈值、储存/元素/树形过滤器、储存砖单物品目标、通用实体插槽请求/移除（含特殊火箭货舱）、只允许自动化搬运、阀门、计时器、ribbon bit、复制设置和受限批量配置", "covered", "building_control domain=config", "building_control domain=side_surface surface=activation", "building_control domain=side_surface surface=option", "building_control domain=config", "building_control domain=config action=visual kind=light", "building_control domain=config action=visual kind=pixel_pack", "orders_control domain=designation action=manual_delivery", "building_control domain=storage", "building_control domain=filter", "building_control domain=tile_selection", "building_control domain=receptacle", "building_control domain=side_surface surface=automation"),
                Row("buildings", "geotuner_target_assignment", "GeoTuner 目标喷泉查看、可选喷泉列表、清空和分配未来目标喷泉", "covered", "building_control domain=side_surface surface=geo_tuner"),
                Row("buildings", "art_and_monument_facades", "艺术建筑外观选择、清空重做、纪念碑部件外观和翻转", "covered", "building_control domain=special kind=artable", "building_control domain=special kind=monument_part"),
                Row("buildings", "gene_shuffler_operations", "Gene Shuffler 完成按钮、请求充能和取消充能", "covered", "building_control domain=special kind=gene_shuffler"),
                Row("buildings", "liquid_heater_turbo_mode", "Liquid Tepidizer 涡轮模式开关和功耗状态", "covered", "building_control domain=side_surface surface=misc kind=turbo_heater"),
                Row("buildings", "remote_work_terminal_dock_selection", "RemoteWorkTerminalSidescreen 远程工作 dock 选择、清空选择和可选 dock 列表", "covered", "building_control domain=story_facility kind=remote_work_terminal"),
                Row("buildings", "dispenser_operations", "DispenserSideScreen / IDispenser：查看可分发物品、选择物品、请求分发和取消分发", "covered", "building_control domain=side_surface surface=facility"),
                Row("buildings", "suit_locker_configuration", "SuitLockerSideScreen：初始配置、请求太空服、取消请求/设为无需服装、掉出已存装备", "covered", "building_control domain=side_surface surface=facility"),
                Row("controls", "generic_sidescreen_buttons", "ISidescreenButtonControl 通用按钮：Studyable、Activatable、ExcavateButton、CryoTank、GeothermalController、POI 解锁等", "covered", "building_control"),
                Row("controls", "generic_user_menu_actions", "对象 UserMenu/context-menu 按钮：取消建造/挖掘/拖地、移动/清扫、自动消毒、维修、堆肥、倒空、倾倒、屠宰、雕刻、拆毁、元素释放、太空服检查点通行、Tinker 等映射操作", "covered", "building_control domain=side_surface surface=user_menu"),
                Row("controls", "focused_maintenance_user_menu_actions", "状态机/槽位参数 UserMenu 操作：厕所提前清洁、淡化器提前清空、运输管入口蜡启用/取消、蜂巢清空/取消、货仓倒空、复制人逐槽卸装", "covered", "building_control domain=side_surface surface=maintenance"),
                Row("controls", "generic_sidescreen_checklists", "ICheckboxListGroupControl 只读侧屏清单：故事任务、化石挖掘、地热设施、孤独复制人房屋等条件/进度清单", "covered", "building_control"),
                Row("controls", "generic_sidescreen_progress_bars", "ProgressBarSideScreen / IProgressBarSideScreen 只读进度条：读取标题、标签、tooltip、最大值和填充百分比", "covered", "building_control"),
                Row("controls", "related_entities_navigation", "RelatedEntitiesSideScreen / IRelatedEntities：读取关联对象列表，并执行玩家点击关联行时的选择和镜头聚焦", "covered", "building_control"),
                Row("controls", "generic_n_toggle_controls", "INToggleSideScreenControl 多选侧屏控件：显示选项、当前/排队状态和排队选择", "covered", "building_control domain=side_surface surface=misc kind=n_toggle"),
                Row("automation", "space_detector_targets", "彗星探测器/Space Scanner 目标选择：流星、玩家发射物和指定火箭", "covered", "building_control domain=space_building kind=comet_detector"),
                Row("automation", "cluster_location_sensor_filters", "星图位置传感器过滤：空太空和指定星图坐标/星体/POI", "covered", "building_control domain=space_building kind=cluster_location_sensor"),
                Row("automation", "logic_alarm_notifications", "Logic Alarm 通知名称、提示文案、通知类型、触发暂停和触发镜头跳转", "covered", "building_control domain=side_surface surface=misc kind=logic_alarm"),
                Row("production", "fabricator_recipe_queue", "制作站/精炼/厨房/制药/服装/碎石/窑炉等 ComplexFabricator 配方查看、材料变体 recipeId 选择、排队、批量排队、清空、无限制作和突变种子设置", "covered", "building_control domain=production"),
                Row("production", "mutant_seed_acceptance_controls", "玩家菜单接受/拒收突变种子开关：ComplexFabricator、FishFeeder、SpiceGrinder", "covered", "building_control domain=production action=mutant_seed_list", "building_control domain=production action=mutant_seed_set"),
                Row("production", "configurable_consumer_options", "ConfigureConsumerSideScreen 选项型消费者：查看当前选项、材料需求并切换选项", "covered", "building_control domain=side_surface surface=misc kind=configurable_consumer"),
                Row("storage", "storage_filters", "储存箱、StorageTile 单物品选择和 TreeFilterable/FlatTagFilterable 过滤器", "covered", "building_control domain=storage", "building_control domain=tile_selection", "building_control domain=filter"),
                Row("dupes", "duplicant_info_and_names", "复制人列表、属性、需求、改名", "covered", "dupes_control domain=info", "dupes_control domain=command action=rename", "dupes_control domain=command action=auto_rename"),
                Row("dupes", "duplicant_direct_commands", "移动到这里单点/批量命令、强制动作、可分配对象、装备/Ownables 槽位选择、卸下当前装备、技能点分配、个人工作优先级、帽子和直接命令入口", "covered", "dupes_control domain=side_screen", "dupes_control domain=command", "dupes_control domain=assignable", "building_control domain=side_surface surface=maintenance", "dupes_control domain=skill", "dupes_control domain=hat", "dupes_control domain=priority"),
                Row("dupes", "bionic_upgrade_slots", "BionicSideScreen 仿生人升级槽查看、锁定/空/已分配/已安装状态；槽位分配/取消分配由 dupes_control domain=assignable action=set_slot 覆盖", "covered", "dupes_control domain=side_screen", "dupes_control domain=assignable"),
                Row("dupes", "minion_todo_side_screen", "MinionTodoSideScreen 当前差事、可执行差事、阻塞差事、优先级、目标和当前日程块", "covered", "dupes_control domain=side_screen"),
                Row("schedules", "schedule_management", "创建日程、改区块、分配复制人", "covered", "colony_control domain=management kind=schedule"),
                Row("diet", "consumable_permissions", "饮食/可食用项权限", "covered", "colony_control domain=management kind=diet"),
                Row("research", "research_management", "查看/搜索/设置/取消研究队列", "covered", "colony_control domain=management kind=research action=status", "colony_control domain=management kind=research action=list", "colony_control domain=management kind=research"),
                Row("space", "telescope_starmap_analysis", "TelescopeSideScreen 打开星图、查看/设置/清除星图目的地分析目标", "covered", "building_control domain=space_story"),
                Row("rockets", "rocket_operations", "火箭状态、目的地、往返/单程、发射、取消发射、发射台降落/取消降落、控制台限制、火箭内部建筑受控/不受控、乘员召集/释放、分配组逐复制人成员开关、导弹发射器弹药选择", "covered", "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=restriction", "building_control domain=rocket rocketDomain=usage", "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=assignment_group", "building_control domain=special kind=missile_launcher"),
                Row("rockets", "rocket_module_reordering", "火箭模块添加、替换、上下移动、标记移除和取消移除", "covered", "building_control domain=rocket rocketDomain=module"),
                Row("rockets", "rocket_flight_utility_modules", "ModuleFlightUtilitySideScreen：飞行模块清空/投放、自动投放、星图目标选择、复制人选择", "covered", "building_control domain=rocket rocketDomain=flight_utility"),
                Row("rockets", "rocket_cargo_and_harvest_progress", "CargoModuleSideScreen/HarvestModuleSideScreen：星图货舱收集进度、容量、太空钻探和钻石库存", "covered", "building_control domain=rocket rocketDomain=cargo_status action=collectors", "building_control domain=rocket rocketDomain=cargo_status action=harvest_modules"),
                Row("rockets", "railgun_launch_mass", "轨道炮发射质量 slider/数字输入、库存和辐射粒子能量状态", "covered", "building_control domain=space_building kind=railgun"),
                Row("rockets", "rocket_self_destruct", "在途火箭 SelfDestructButtonSideScreen 自毁操作，高风险确认后触发", "covered", "building_control domain=rocket rocketDomain=self_destruct"),
                Row("resources", "inventory_and_reports", "资源、食物、AllResourcesScreen 固定/通知开关、殖民地报告和诊断设置", "covered", "read_control domain=resources", "colony_control", "colony_control"),
                Row("ui", "notifications_and_markers", "通知读取、点击聚焦、dismiss、弹字、地图标记", "covered", "colony_control", "game_control domain=ui uiDomain=feedback"),
                Row("ui", "management_screens", "打开/切换管理面板、覆盖视图、百科、查找、建造分类 UI 入口", "covered", "game_control domain=ui uiDomain=action", "navigation_control", "disabled: in-game knowledge/database query", "dupes_control domain=priority", "dupes_control domain=hat"),
                Row("automation", "logic_and_power_controls", "自动化开关、电闸、信号阈值、元素过滤/传感器、逻辑广播频道、计数器、Critter Sensor 小动物/蛋计数、时间范围、滤波/缓冲延迟、计时器、ribbon bit、阀门、逻辑报警器、星图传感器、彗星探测器、自动化专用搬运、逻辑端口状态和受限批量控制", "covered", "building_control domain=config", "building_control domain=filter", "building_control domain=side_surface surface=option", "building_control domain=config", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=misc kind=logic_alarm", "building_control domain=space_building kind=cluster_location_sensor", "building_control domain=space_building kind=comet_detector"),
                Row("ranching", "critter_and_egg_operations", "小动物清单、抓捕、放生、投放点过滤和容量、孵化器蛋请求与连续孵化、生物诱饵站、牧场/蛋/鱼相关操作", "covered", "colony_control domain=bio bioDomain=ranching kind=critters action=critters", "orders_control domain=designation action=capture", "colony_control domain=bio bioDomain=ranching kind=dropoff", "colony_control domain=bio bioDomain=ranching kind=incubator", "building_control domain=special kind=creature_lure", "orders_control domain=designation action=attack"),
                Row("farming", "harvest_and_planting", "种子目录、收获状态、自动收获、区域收获、铲除、单点/批量种植选择和种植请求", "covered", "colony_control domain=bio bioDomain=farming action=seed_catalog", "colony_control domain=bio bioDomain=farming", "orders_control domain=area action=harvest"),
                Row("farming", "genetic_analysis_seed_permissions", "GeneticAnalysisStationSideScreen / Botanical Analyzer 突变种子允许/禁用分析", "covered", "building_control domain=story_facility kind=genetic_analysis_station"),
                Row("medical", "medical_and_care_assignments", "患者清单、床位、医疗床单点/批量阈值、床位分配、医生站药品/可治疗疾病、诊疗、护理、制药相关分配", "covered", "colony_control domain=management kind=medical", "dupes_control domain=assignable", "orders_control domain=designation action=manual_delivery"),
                Row("combat", "combat_targeting", "攻击标记、取消攻击和优先级", "covered", "read_control domain=world action=text_map", "colony_control domain=bio bioDomain=ranching kind=critters action=critters", "orders_control domain=designation action=attack"),
                Row("story", "printerceptor_operations", "PrinterceptorSideScreen 打开打印选择界面、拦截打印舱候选和 databank/充能状态", "covered", "building_control domain=story_facility"),
                Row("story", "poi_tech_unlock_portals", "Research Portal/信息传送通道：查看解锁差事、进度、会解锁的 POI 科技项，开始或取消解锁研究", "covered", "building_control domain=story_facility"),
                Row("story", "lore_bearer_reading", "LoreBearerSideScreen 阅读/检查按钮、已读状态、tooltip 和弹窗触发", "covered", "building_control domain=side_surface surface=facility"),
                Row("story", "telepad_side_screen", "TelepadSideScreen 查看移民倒计时、打开移民选择、殖民地摘要、技能和研究界面、胜利条件状态", "covered", "building_control domain=side_surface surface=facility"),
                Row("story", "artifact_analysis_display", "ArtifactAnalysisSideScreen 已分析 artifact 列表、分析站状态、场上 artifact 和 reveal/lore 弹窗", "covered", "building_control domain=side_surface surface=facility"),
                Row("story", "warp_portal_side_screen", "WarpPortalSideScreen 等待复制人后开始传送、取消分配/传送准备和冷却状态读取", "covered", "building_control domain=space_story"),
                Row("story", "temporal_tear_side_screen", "TemporalTearSideScreen 查看裂隙开启/消耗状态并在双重确认后消耗当前位置火箭", "covered", "building_control domain=space_story"),
                Row("diagnostics", "generic_process_conditions", "ConditionListSideScreen / IProcessConditionSet 通用条件状态读取，包括火箭发射/储存/飞行条件", "covered", "building_control domain=space_story"),
                Row("sandbox", "sandbox_tools", "沙盒刷子、桶填充、取样、生成、清地面、清小动物、揭示、温度、压力、故事特质盖章和 Debug AutoPlumber/InstantBuild 操作", "covered", "game_control", "game_control", "game_control")
            };
        }

        private static CoverageRow Row(string group, string operation, string playerSurface, string status, params string[] tools)
        {
            return new CoverageRow
            {
                Group = group,
                Operation = operation,
                PlayerSurface = playerSurface,
                DeclaredStatus = status,
                Tools = tools.ToList()
            };
        }

        private static Dictionary<string, object> BuildHotkeySummary(HashSet<string> toolNames)
        {
            var actionNames = Enum.GetNames(typeof(global::Action))
                .Where(name => name != "NumActions")
                .OrderBy(name => name)
                .ToList();
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "AlternateView", "Attack", "BuildingCancel", "BuildingDeconstruct", "CameraHome",
                "Capture", "Clear", "DebugReportBug", "Dig", "Disconnect", "EmptyPipe", "Escape",
                "Harvest", "Help", "Mop", "PanDown", "PanLeft", "PanRight", "PanUp", "Prioritize",
                "RotateBuilding", "SlowDown", "SpeedUp", "TogglePause", "ZoomIn", "ZoomOut"
            };

            return new Dictionary<string, object>
            {
                ["enumCount"] = actionNames.Count,
                ["mappedExamples"] = covered.Where(actionNames.Contains).OrderBy(name => name).ToList(),
                ["unmappedCount"] = actionNames.Count(name => !covered.Contains(name)),
                ["unmappedExamples"] = actionNames.Where(name => !covered.Contains(name)).Take(80).ToList(),
                ["note"] = "Action 枚举是键位/界面入口，不等同于完整玩家操作语义；以 operationSurfaces 作为补齐接口的主审计清单。"
            };
        }

        private static int StatusRank(string status)
        {
            switch (status)
            {
                case "missing": return 0;
                case "partial": return 1;
                case "covered": return 2;
                default: return 3;
            }
        }

        private static string NormalizeCoverageDetail(string value)
        {
            string detail = string.IsNullOrWhiteSpace(value) ? "compact" : value.Trim().ToLowerInvariant();
            if (detail == "brief" || detail == "full")
                return detail;
            return "compact";
        }

        private static int CoverageScore(CoverageRow row, string query)
        {
            if (string.IsNullOrEmpty(query))
                return 1;

            string haystack = string.Join(" ", new[]
            {
                row.Group ?? "",
                row.Operation ?? "",
                row.PlayerSurface ?? "",
                string.Join(" ", row.Tools ?? new List<string>()),
                string.Join(" ", (row.ResourceAnchors ?? new List<Dictionary<string, object>>())
                    .SelectMany(anchor => AnchorUris(anchor)))
            }).ToLowerInvariant();

            int score = 0;
            foreach (string token in ExpandCoverageQuery(query).Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':', '，', '。', '、' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length <= 1)
                    continue;
                if (haystack.Contains(token))
                    score++;
            }
            return score;
        }

        private static IEnumerable<string> AnchorUris(Dictionary<string, object> anchor)
        {
            object uris;
            if (anchor == null || !anchor.TryGetValue("uris", out uris))
                return new string[0];

            var strings = uris as IEnumerable<string>;
            if (strings != null)
                return strings;

            var objects = uris as IEnumerable<object>;
            return objects != null ? objects.Select(item => item?.ToString() ?? "") : new string[0];
        }

        private static string ExpandCoverageQuery(string query)
        {
            var tokens = new List<string>();
            foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':', '，', '。', '、' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = token.ToLowerInvariant();
                tokens.Add(normalized);
                switch (normalized)
                {
                    case "挖":
                    case "挖掘":
                        tokens.AddRange(new[] { "dig", "orders", "area" });
                        break;
                    case "清扫":
                    case "打扫":
                        tokens.AddRange(new[] { "sweep", "clear", "storage", "orders" });
                        break;
                    case "建造":
                    case "建筑":
                        tokens.AddRange(new[] { "build", "building", "buildings", "plan" });
                        break;
                    case "复制人":
                    case "小人":
                        tokens.AddRange(new[] { "dupe", "duplicant", "dupes", "assign" });
                        break;
                    case "火箭":
                    case "太空":
                        tokens.AddRange(new[] { "rocket", "rockets", "space", "launch" });
                        break;
                    case "自动化":
                    case "逻辑":
                        tokens.AddRange(new[] { "automation", "logic", "sensor" });
                        break;
                    case "种植":
                    case "农场":
                        tokens.AddRange(new[] { "farming", "planting", "harvest" });
                        break;
                    case "小动物":
                    case "牧场":
                        tokens.AddRange(new[] { "ranching", "critters", "incubator" });
                        break;
                    case "地图":
                    case "地形":
                        tokens.AddRange(new[] { "world", "map", "cell", "area" });
                        break;
                }
            }
            return string.Join(" ", tokens.Distinct().ToArray());
        }

        private static bool HasConfirmParameter(McpTool tool)
        {
            return tool.Parameters != null && tool.Parameters.ContainsKey("confirm");
        }

        private static bool IsPlayerFacingTool(McpTool tool)
        {
            if (tool.Hidden)
                return false;
            switch (tool.Group)
            {
                case "tools":
                case "server":
                case "database":
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsGameStateReadTool(McpTool tool)
        {
            switch (tool.Group)
            {
                case "tools":
                case "server":
                case "database":
                default:
                    return true;
            }
        }

        private static Dictionary<string, List<string>> BuildResourceUriIndex()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in OniResourceRegistry.GetResourceInfos())
                AddResourceUri(result, info.Name, info.Uri);
            foreach (var info in OniResourceRegistry.GetResourceTemplateInfos())
                AddResourceUri(result, info.Name, info.UriTemplate);
            return result;
        }

        private static void AddResourceUri(Dictionary<string, List<string>> index, string name, string uri)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uri))
                return;

            List<string> uris;
            if (!index.TryGetValue(name, out uris))
            {
                uris = new List<string>();
                index[name] = uris;
            }
            if (!uris.Contains(uri))
                uris.Add(uri);
        }

        private static List<Dictionary<string, object>> ResourceAnchorsForRow(CoverageRow row, List<McpTool> tools, Dictionary<string, List<string>> resourceUrisByName, bool hasGenericReadResource)
        {
            var toolByName = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
            var anchors = new List<Dictionary<string, object>>();
            foreach (string toolName in row.Tools)
            {
                McpTool tool;
                if (!toolByName.TryGetValue(toolName, out tool))
                    continue;

                List<string> uris;
                if (resourceUrisByName.TryGetValue(tool.Name, out uris))
                {
                    anchors.Add(new Dictionary<string, object>
                    {
                        ["tool"] = tool.Name,
                        ["kind"] = "semantic",
                        ["uris"] = uris
                    });
                    continue;
                }

                if (hasGenericReadResource && string.Equals(tool.Mode, "read", StringComparison.OrdinalIgnoreCase))
                {
                    anchors.Add(new Dictionary<string, object>
                    {
                        ["tool"] = tool.Name,
                        ["kind"] = "generic_read",
                        ["uris"] = new[] { "oni://tools/read/" + tool.Name + "{?...}" }
                    });
                }
            }

            return anchors;
        }

        private static Dictionary<string, object> Issue(string code, string message, object detail)
        {
            var issue = new Dictionary<string, object>
            {
                ["code"] = code,
                ["message"] = message
            };
            if (detail != null)
                issue["detail"] = detail;
            return issue;
        }

        private class CoverageRow
        {
            public string Group { get; set; }
            public string Operation { get; set; }
            public string PlayerSurface { get; set; }
            public string DeclaredStatus { get; set; }
            public string Status { get; set; }
            public List<string> Tools { get; set; }
            public List<string> MissingTools { get; set; }
            public List<Dictionary<string, object>> ResourceAnchors { get; set; }

            public CoverageRow WithRuntimeStatus(HashSet<string> toolNames)
            {
                MissingTools = Tools.Where(tool => !toolNames.Contains(SurfaceAuditUtil.ToolNameOnly(tool))).ToList();
                if (DeclaredStatus == "missing")
                    Status = "missing";
                else if (MissingTools.Count == 0 && Tools.Count > 0)
                    Status = DeclaredStatus;
                else if (MissingTools.Count < Tools.Count)
                    Status = "partial";
                else
                    Status = "missing";
                return this;
            }

            public Dictionary<string, object> ToDictionary(string detail, bool includeResources, int score)
            {
                if (detail == "brief")
                {
                    var brief = new Dictionary<string, object>
                    {
                        ["g"] = Group,
                        ["op"] = Operation,
                        ["status"] = Status,
                        ["tools"] = Tools,
                        ["score"] = score
                    };
                    if (includeResources)
                        brief["resources"] = ResourceAnchors ?? new List<Dictionary<string, object>>();
                    return brief;
                }

                var result = new Dictionary<string, object>
                {
                    ["group"] = Group,
                    ["operation"] = Operation,
                    ["playerSurface"] = PlayerSurface,
                    ["status"] = Status,
                    ["tools"] = Tools,
                    ["score"] = score
                };
                if (detail == "full")
                    result["missingTools"] = MissingTools;
                if (includeResources)
                    result["resourceAnchors"] = ResourceAnchors ?? new List<Dictionary<string, object>>();
                return result;
            }
        }
    }
}
