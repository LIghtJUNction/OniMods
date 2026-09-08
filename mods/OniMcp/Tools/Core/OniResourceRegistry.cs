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
    public static partial class OniResourceRegistry
    {
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
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != "oni")
                return null;

            string resourceUri = parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
            var resource = _resources.FirstOrDefault(item => item.Info.Uri.TrimEnd('/') == resourceUri);
            if (resource != null)
            {
                var arguments = ParseQuery(parsed.Query);
                if (resource.Arguments != null)
                {
                    foreach (var property in resource.Arguments.Properties())
                        arguments[property.Name] = property.Value.DeepClone();
                }
                return ReadToolResource(uri, resource.ToolName, arguments, resource.Info.MimeType);
            }

            return ReadDynamicResource(uri);
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
            if (!OniToolRegistry.TryGetOperation(toolName, out var operation))
                return ErrorResource(uri, "Resource operation not found: " + toolName);

            CallToolResult result;
            try
            {
                // Resource routes select their read operation. resources/read has no
                // tools/call task description and must not drain tool notifications.
                result = operation.Handler(arguments ?? new JObject());
            }
            catch (Exception ex)
            {
                return ErrorResource(uri, "Resource operation error: " + ex.Message);
            }

            if (result == null)
                return ErrorResource(uri, "Resource operation returned no result.");
            string text = ExtractText(result);
            if (result.IsError)
                return ErrorResource(uri, text);

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

        private static JObject ParseQuery(string query, bool allowOperationSelectors = false)
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

                // URI routes own operation selectors. Query strings supply filters;
                // they cannot redirect a read to another domain or authorize writes.
                if (key == "confirm" || key == "force" || (!allowOperationSelectors &&
                    (key == "action" || key == "domain" || key == "uiDomain" ||
                     key == "rocketDomain" || key == "bioDomain")))
                    continue;

                string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                if (string.IsNullOrEmpty(value))
                    continue;

                result[key] = value;
            }

            return result;
        }

        private static OniResource Resource(string uri, string toolName, string title, string description, JObject arguments)
        {
            return new OniResource
            {
                Info = new McpResourceInfo
                {
                    Uri = uri,
                    Name = toolName,
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

        private static readonly List<OniResource> _resources = new List<OniResource>
                {
                    Resource("oni://colony/status", "colony_control", "殖民地状态", "周期、复制人数、世界数量、速度和暂停状态。", new JObject { ["domain"] = "read", ["action"] = "status" }),
                    Resource("oni://colony/diagnostics", "colony_control", "殖民地诊断", "缺氧、断粮、过热等殖民地诊断结果。", new JObject { ["domain"] = "diagnostic", ["action"] = "diagnostics" }),
                    Resource("oni://colony/alerts", "colony_control", "殖民地警报", "当前游戏警报和通知。", new JObject { ["domain"] = "diagnostic", ["action"] = "alerts" }),
                    Resource("oni://colony/diagnostic-settings", "colony_control", "殖民地诊断设置", "AllDiagnosticsScreen 诊断显示模式、子条件启用状态和 Debug 通知禁用状态。", new JObject { ["domain"] = "diagnostic", ["action"] = "list_settings" }),
                    Resource("oni://colony/report", "colony_control", "殖民地报告", "殖民地报告。", new JObject { ["domain"] = "report", ["action"] = "report" }),
                    Resource("oni://colony/summary", "colony_control", "殖民地摘要", "面向行动规划的殖民地摘要。", new JObject { ["domain"] = "report", ["action"] = "summary" }),
                    Resource("oni://colony/notifications", "colony_control", "通知列表", "当前 HUD NotificationScreen/NotificationManager 通知、消息、聚焦目标和可清除状态。", new JObject { ["domain"] = "notification", ["action"] = "list" }),
                    Resource("oni://world/list", "colony_control", "世界列表", "已加载世界和当前激活世界。", new JObject { ["domain"] = "read", ["action"] = "worlds" }),
                    Resource("oni://world/elements", "read_control", "世界元素摘要", "当前世界元素质量和温度摘要。", new JObject { ["domain"] = "world", ["action"] = "element_summary" }),
                    Resource("oni://camera/view", "navigation_control", "相机视图", "当前相机位置、缩放、激活世界和屏幕尺寸。", new JObject { ["action"] = "get_view" }),
                    Resource("oni://resources/inventory", "read_control", "资源库存", "资源库存摘要。", new JObject { ["domain"] = "resources", ["action"] = "inventory" }),
                    Resource("oni://resources/food", "read_control", "食物库存", "食物库存和保质信息。", new JObject { ["domain"] = "resources", ["action"] = "food" }),
                    Resource("oni://resources/pins", "read_control", "资源面板固定和通知", "AllResourcesScreen 资源行固定显示和通知开关状态。", new JObject { ["domain"] = "resources", ["action"] = "pins" }),
                    Resource("oni://diet/status", "colony_control", "饮食权限", "Consumables 管理屏中的复制人饮食/药品/电池可消费权限和库存。", new JObject { ["domain"] = "management", ["kind"] = "diet", ["action"] = "status" }),
                    Resource("oni://storage/list", "building_control", "储存列表", "储存建筑和过滤器列表。", new JObject { ["domain"] = "storage", ["action"] = "list" }),
                    Resource("oni://storage/tile-selections", "building_control", "储存砖目标物品", "SingleItemSelectionSideScreen / StorageTile 目标物品和可选物品。", new JObject { ["domain"] = "tile_selection", ["action"] = "list" }),
                    Resource("oni://filters/controls", "building_control", "过滤器控件", "气/液/固体单选过滤器、元素传感器和树形/平铺多选过滤器。", new JObject { ["domain"] = "filter", ["action"] = "list" }),
                    Resource("oni://controls/options", "building_control", "选项型侧屏控件", "工作方向、少量选项、逻辑广播频道和辐射粒子方向控件。", new JObject { ["domain"] = "option", ["action"] = "list" }),
                    Resource("oni://controls/state", "building_control", "状态型侧屏控件", "容量上限、单 checkbox、逻辑计数器和时间范围传感器控件。", new JObject { ["action"] = "state_list" }),
                    Resource("oni://controls/activation-ranges", "building_control", "启停双阈值控件", "ActiveRangeSideScreen / IActivationRangeTarget 双阈值控件。", new JObject { ["domain"] = "activation", ["action"] = "list" }),
                    Resource("oni://controls/progress-bars", "building_control", "侧屏进度条", "ProgressBarSideScreen / IProgressBarSideScreen 只读进度条状态。", new JObject { ["kind"] = "progress", ["action"] = "list" }),
                    Resource("oni://controls/buttons", "building_control", "通用侧屏按钮", "实现 ISidescreenButtonControl 的通用侧屏按钮入口。", new JObject { ["kind"] = "button", ["action"] = "list" }),
                    Resource("oni://controls/user-menu-actions", "building_control", "对象用户菜单操作", "对象 UserMenu/context-menu 按钮映射：清扫、维修、堆肥、倒空、雕刻等非侧屏操作。", new JObject { ["domain"] = "user_menu", ["action"] = "list" }),
                    Resource("oni://controls/maintenance-actions", "building_control", "维护类用户菜单操作", "需要状态机或槽位参数的玩家维护操作：厕所清洁、淡化器清空、运输管蜡、蜂巢清空、货仓倒空、复制人卸装。", new JObject { ["domain"] = "maintenance", ["action"] = "list" }),
                    Resource("oni://controls/checklists", "building_control", "侧屏清单", "实现 ICheckboxListGroupControl 的故事任务、条件和设施清单。", new JObject { ["kind"] = "checklist", ["action"] = "list" }),
                    Resource("oni://controls/related-entities", "building_control", "关联对象", "实现 IRelatedEntities 的侧屏关联对象和可点击跳转目标。", new JObject { ["kind"] = "related", ["action"] = "list" }),
                    Resource("oni://controls/n-toggles", "building_control", "多选侧屏控件", "实现 INToggleSideScreenControl 的多选侧屏控件。", new JObject { ["domain"] = "misc", ["kind"] = "n_toggle", ["action"] = "list" }),
                    Resource("oni://automation/logic-alarms", "building_control", "逻辑报警器", "Logic Alarm 通知名称、提示、类型、暂停和镜头跳转设置。", new JObject { ["domain"] = "misc", ["kind"] = "logic_alarm", ["action"] = "list" }),
                    Resource("oni://automation/automatable", "building_control", "自动化专用搬运", "AutomatableSideScreen 只允许自动化/允许手动搬运状态。", new JObject { ["domain"] = "automation", ["kind"] = "automatable", ["action"] = "list" }),
                    Resource("oni://automation/critter-sensors", "building_control", "小动物计数传感器", "CritterSensorSideScreen 小动物/蛋计数开关、阈值和当前计数。", new JObject { ["domain"] = "automation", ["kind"] = "critter_sensor", ["action"] = "list" }),
                    Resource("oni://automation/comet-detectors", "building_control", "彗星探测器", "Comet Detector/Space Scanner 当前探测目标和可选择目标。", new JObject { ["domain"] = "space_building", ["kind"] = "comet_detector", ["action"] = "list" }),
                    Resource("oni://automation/cluster-location-sensors", "building_control", "星图位置传感器", "LogicClusterLocationSensor 空太空和星体/POI 坐标过滤设置。", new JObject { ["domain"] = "space_building", ["kind"] = "cluster_location_sensor", ["action"] = "list" }),
                    Resource("oni://buildings/lights", "building_control", "灯光控件", "灯光建筑、发光参数和可选颜色预设。", new JObject { ["action"] = "visual", ["kind"] = "light", ["visualAction"] = "list" }),
                    Resource("oni://buildings/turbo-heaters", "building_control", "液体加热器涡轮模式", "Liquid Tepidizer TurboModeSideScreen 功耗和开关状态。", new JObject { ["domain"] = "misc", ["kind"] = "turbo_heater", ["action"] = "list" }),
                    Resource("oni://buildings/pixel-packs", "building_control", "Pixel Pack 控件", "Pixel Pack 当前逻辑值、四面板 active/standby 颜色和颜色预设。", new JObject { ["action"] = "visual", ["kind"] = "pixel_pack", ["visualAction"] = "list" }),
                    Resource("oni://geotuners", "building_control", "GeoTuner", "GeoTuner 当前/未来目标喷泉和调谐分配状态。", new JObject { ["domain"] = "geo_tuner", ["action"] = "list" }),
                    Resource("oni://geotuners/geysers", "building_control", "GeoTuner 喷泉目标", "GeoTuner 可选择喷泉、研究状态、可见性和分配数量。", new JObject { ["domain"] = "geo_tuner", ["action"] = "list_geysers" }),
                    Resource("oni://buildings/artables", "building_control", "艺术建筑外观", "艺术建筑当前外观和可选择外观阶段。", new JObject { ["domain"] = "special", ["kind"] = "artable", ["action"] = "list" }),
                    Resource("oni://buildings/monument-parts", "building_control", "纪念碑部件外观", "纪念碑部件当前外观、可选外观和部件类型。", new JObject { ["domain"] = "special", ["kind"] = "monument_part", ["action"] = "list" }),
                    Resource("oni://ranching/lures", "building_control", "生物诱饵站", "生物诱饵站当前诱饵、可选诱饵和库存。", new JObject { ["domain"] = "special", ["kind"] = "creature_lure", ["action"] = "list" }),
                    Resource("oni://buildings/gene-shufflers", "building_control", "Gene Shuffler", "Gene Shuffler 分配、工作完成、消耗和充能请求状态。", new JObject { ["domain"] = "special", ["kind"] = "gene_shuffler", ["action"] = "list" }),
                    Resource("oni://story/printerceptors", "building_control", "Printerceptor", "Printerceptor passcode、拦截充能、打印界面和 databank 状态。", new JObject { ["domain"] = "story_facility", ["kind"] = "printerceptor", ["action"] = "list" }),
                    Resource("oni://story/poi-tech-unlocks", "building_control", "信息传送通道", "Research Portal/信息传送通道解锁差事、进度和 POI 科技项。", new JObject { ["domain"] = "story_facility", ["kind"] = "poi_tech_unlock", ["action"] = "list" }),
                    Resource("oni://buildings/remote-work-terminals", "building_control", "远程工作终端", "Remote Work Terminal 当前/未来 dock 和同世界可选 dock。", new JObject { ["domain"] = "story_facility", ["kind"] = "remote_work_terminal", ["action"] = "list" }),
                    Resource("oni://farming/genetic-analysis-stations", "building_control", "Botanical Analyzer", "Botanical Analyzer 可分析种子、允许/禁用状态和库存。", new JObject { ["domain"] = "story_facility", ["kind"] = "genetic_analysis_station", ["action"] = "list" }),
                    Resource("oni://buildings/dispensers", "building_control", "分发器", "DispenserSideScreen 可分发物品、当前选择和分发请求状态。", new JObject { ["domain"] = "facility", ["kind"] = "dispenser", ["action"] = "list" }),
                    Resource("oni://buildings/receptacles", "building_control", "实体陈列/插槽", "ReceptacleSideScreen / SpecialCargoBayClusterSideScreen / SingleEntityReceptacle 通用实体请求、取消和移除状态。", new JObject { ["domain"] = "receptacle", ["action"] = "list" }),
                    Resource("oni://buildings/suit-lockers", "building_control", "太空服柜", "SuitLockerSideScreen 配置、请求、存储装备和掉落能力状态。", new JObject { ["domain"] = "facility", ["kind"] = "suit_locker", ["action"] = "list" }),
                    Resource("oni://story/lore-bearers", "building_control", "LoreBearer", "LoreBearerSideScreen 可阅读/已阅读对象和按钮状态。", new JObject { ["domain"] = "facility", ["kind"] = "lore_bearer", ["action"] = "list" }),
                    Resource("oni://story/telepads", "building_control", "Printing Pod / Telepad", "TelepadSideScreen 移民、研究、技能提示和胜利条件状态。", new JObject { ["domain"] = "facility", ["kind"] = "telepad", ["action"] = "list" }),
                    Resource("oni://story/artifacts", "building_control", "Artifact Analysis", "ArtifactAnalysisSideScreen 已分析 artifact、场上 artifact 和分析站状态。", new JObject { ["domain"] = "facility", ["kind"] = "artifact", ["action"] = "list" }),
                    Resource("oni://story/warp-portals", "building_control", "Warp Portal", "WarpPortalSideScreen 等待传送、传送中、冷却和分配状态。", new JObject { ["domain"] = "space_story", ["kind"] = "warp_portal", ["action"] = "list" }),
                    Resource("oni://story/temporal-tears", "building_control", "Temporal Tear", "TemporalTearSideScreen 裂隙开启、消耗状态和可进入火箭。", new JObject { ["domain"] = "space_story", ["kind"] = "temporal_tear", ["action"] = "list" }),
                    Resource("oni://space/telescopes", "building_control", "Telescope", "TelescopeSideScreen 建筑状态和当前星图分析目标。", new JObject { ["domain"] = "space_story", ["kind"] = "telescope", ["action"] = "list" }),
                    Resource("oni://space/analysis-targets", "building_control", "星图分析目标", "星图目的地分析状态和可选择望远镜目标。", new JObject { ["domain"] = "space_story", ["kind"] = "starmap_analysis", ["action"] = "list" }),
                    Resource("oni://diagnostics/process-conditions", "building_control", "通用过程条件", "ConditionListSideScreen/IProcessConditionSet 条件状态、文本和 tooltip。", new JObject { ["domain"] = "space_story", ["kind"] = "process_conditions", ["action"] = "list" }),
                    Resource("oni://dupes/bionic-upgrades", "dupes_control", "仿生人升级槽", "BionicSideScreen 仿生人升级槽、分配和安装状态；写入使用 dupes_control domain=assignable action=set_slot。", new JObject { ["domain"] = "side_screen", ["action"] = "bionic_upgrades" }),
                    Resource("oni://rockets/missile-launchers", "building_control", "导弹发射器", "导弹发射器弹药允许状态。", new JObject { ["domain"] = "special", ["kind"] = "missile_launcher", ["action"] = "list" }),
                    Resource("oni://rockets/modules", "building_control", "火箭模块", "火箭模块顺序、移除、替换和添加能力状态。", new JObject { ["domain"] = "module", ["action"] = "list" }),
                    Resource("oni://rockets/module-defs", "building_control", "火箭模块定义", "SelectModuleSideScreen 可选择火箭模块定义和条件状态。", new JObject { ["domain"] = "module", ["action"] = "list_defs" }),
                    Resource("oni://rockets/launch-pads", "building_control", "火箭发射台", "LaunchPadSideScreen 发射台、已停靠火箭和可降落火箭。", new JObject { ["domain"] = "ops", ["action"] = "list_launch_pads" }),
                    Resource("oni://rockets/flight-utilities", "building_control", "火箭飞行模块实用操作", "ModuleFlightUtilitySideScreen 清空/投放、自动投放、目标和复制人选择状态。", new JObject { ["domain"] = "flight_utility", ["action"] = "list" }),
                    Resource("oni://rockets/restrictions", "building_control", "火箭控制台限制", "RocketRestrictionSideScreen 地面/太空使用限制状态。", new JObject { ["domain"] = "restriction", ["action"] = "list" }),
                    Resource("oni://rockets/usage-controls", "building_control", "火箭内部建筑使用限制", "火箭内部建筑是否受 RocketControlStation 限制的玩家菜单状态。", new JObject { ["domain"] = "usage", ["action"] = "list" }),
                    Resource("oni://rockets/crew-requests", "building_control", "火箭乘员召集", "SummonCrewSideScreen 乘员召集/释放状态、登船人数和驾驶员状态。", new JObject { ["domain"] = "crew_request", ["action"] = "list" }),
                    Resource("oni://rockets/assignment-groups", "building_control", "分配组成员", "AssignmentGroupControllerSideScreen 分配组成员状态和复制人成员开关。", new JObject { ["domain"] = "assignment_group", ["action"] = "list" }),
                    Resource("oni://rockets/cargo-collectors", "building_control", "火箭货舱收集器", "CargoModuleSideScreen 星图货舱收集模块容量、库存和收集进度。", new JObject { ["domain"] = "cargo_status", ["action"] = "collectors" }),
                    Resource("oni://rockets/harvest-modules", "building_control", "火箭钻探模块", "HarvestModuleSideScreen 太空钻探模块钻探状态和钻石库存。", new JObject { ["domain"] = "cargo_status", ["action"] = "harvest_modules" }),
                    Resource("oni://rockets/railguns", "building_control", "轨道炮", "轨道炮发射质量、库存和辐射粒子能量状态。", new JObject { ["domain"] = "space_building", ["kind"] = "railgun", ["action"] = "list" }),
                    Resource("oni://rockets/self-destruct", "building_control", "火箭自毁", "SelfDestructButtonSideScreen 可自毁火箭舱体。", new JObject { ["domain"] = "self_destruct", ["action"] = "list" }),
                    Resource("oni://buildings/defs", "building_control", "可建造建筑定义", "建造菜单建筑定义、分类、材料需求、可用外观和搜索结果。", new JObject { ["action"] = "search_defs" }),
                    Resource("oni://buildings/materials", "building_control", "可用建造材料", "指定建筑当前世界可用的合法建造材料，按库存排序；用于 material=auto 或显式材料选择。", new JObject { ["action"] = "materials" }),
                    Resource("oni://buildings/configurables", "building_control", "可配置建筑", "支持启用、开关、阈值、门状态、门禁和补料的建筑。", new JObject { ["action"] = "list" }),
                    Resource("oni://automation/controls", "building_control", "自动化控件", "逻辑/电力相关玩家可配置控件：端口、开关、阈值、阀门、计时器、ribbon bit。", new JObject { ["action"] = "list_automation" }),
                    Resource("oni://production/fabricators", "building_control", "生产制作站", "制作站/精炼/厨房等配方队列、当前订单和运行状态。", new JObject { ["domain"] = "production", ["action"] = "list_fabricators" }),
                    Resource("oni://production/recipes", "building_control", "生产配方", "制作站 ComplexRecipe 配方、材料、产物、解锁和队列数量。", new JObject { ["domain"] = "production", ["action"] = "list_recipes" }),
                    Resource("oni://production/mutant-seed-controls", "building_control", "突变种子接收开关", "制作站、鱼喂食器和香料研磨器的接受/拒收突变种子玩家菜单开关。", new JObject { ["domain"] = "production", ["action"] = "mutant_seed_list" }),
                    Resource("oni://production/configurable-consumers", "building_control", "可配置消费者", "ConfigureConsumerSideScreen 当前选项、可选项和消耗材料。", new JObject { ["domain"] = "misc", ["kind"] = "configurable_consumer", ["action"] = "list" }),
                    Resource("oni://rockets/status", "building_control", "火箭状态", "Spaced Out 火箭和基础版航天器状态。", new JObject { ["domain"] = "ops", ["action"] = "status" }),
                    Resource("oni://research/status", "colony_control", "研究状态", "当前研究状态。", new JObject { ["domain"] = "management", ["kind"] = "research", ["action"] = "status" }),
                    Resource("oni://schedules", "colony_control", "日程", "复制人日程。", new JObject { ["domain"] = "management", ["kind"] = "schedule", ["action"] = "list" }),
                    Resource("oni://dupes", "colony_control", "复制人", "复制人列表和基本状态。", new JObject { ["domain"] = "read", ["action"] = "dupes" }),
                    Resource("oni://dupes/priorities", "dupes_control", "复制人个人优先级", "Priorities/Jobs 管理屏中复制人对各 ChoreGroup 的个人工作优先级。", new JObject { ["domain"] = "priority", ["action"] = "list" }),
                    Resource("oni://dupes/priority-settings", "dupes_control", "复制人优先级设置", "Jobs/Priorities 管理屏全局高级模式开关、默认重置行为和重置后优先级状态。", new JObject { ["domain"] = "priority", ["action"] = "settings_get" }),
                    Resource("oni://dupes/skills", "dupes_control", "复制人技能", "Skills 管理屏中的复制人技能点、已学技能和可学习技能。", new JObject { ["domain"] = "skill", ["action"] = "list" }),
                    Resource("oni://dupes/hats", "dupes_control", "复制人帽子", "Skills 管理屏中的当前帽子、目标帽子和可选帽子列表。", new JObject { ["domain"] = "hat", ["action"] = "list" }),
                    Resource("oni://dupes/status-check", "dupes_control", "复制人状态检查", "复制人位置、当前差事、关键需求、周边可达格和疑似被困风险；只读。", new JObject { ["domain"] = "info", ["action"] = "status_check" }),
                    Resource("oni://dupes/direct-commands", "dupes_control", "复制人直接命令", "复制人可直接执行/配置的玩家操作入口。", new JObject { ["domain"] = "side_screen", ["action"] = "direct_commands" }),
                    Resource("oni://dupes/todos", "dupes_control", "复制人待办差事", "MinionTodoSideScreen 当前差事、可执行差事和阻塞差事。", new JObject { ["domain"] = "side_screen", ["action"] = "todos" }),
                    Resource("oni://dupes/equipment", "dupes_control", "复制人装备", "复制人装备槽、当前装备和可用装备分配对象；写入使用 dupes_control domain=assignable action=set_slot。", new JObject { ["domain"] = "side_screen", ["action"] = "equipment" }),
                    Resource("oni://assignables", "dupes_control", "可分配对象", "床、医疗床、餐桌、太空服等可分配对象和当前分配。", new JObject { ["domain"] = "assignable", ["action"] = "list" }),
                    Resource("oni://farming/planting", "colony_control", "种植槽", "种植箱、农砖、当前植物、请求种子和可接受种子。", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "list_planting" }),
                    Resource("oni://farming/harvestables", "colony_control", "可收获对象", "植物/作物的成熟、收获标记和成熟即收获状态。", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "list_harvestables" }),
                    Resource("oni://farming/seeds", "colony_control", "种子目录", "可用于种植请求的 PlantableSeed prefab。", new JObject { ["domain"] = "bio", ["bioDomain"] = "farming", ["action"] = "seed_catalog" }),
                    Resource("oni://ranching/critters", "colony_control", "小动物", "可抓捕小动物、抓捕标记和捆绑状态。", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "critters", ["action"] = "critters" }),
                    Resource("oni://ranching/dropoffs", "colony_control", "小动物投放点", "小动物/鱼类投放点过滤器、容量和计数。", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "dropoff", ["action"] = "list" }),
                    Resource("oni://ranching/incubators", "colony_control", "孵化器", "孵化器蛋请求、占用对象、进度和连续孵化设置。", new JObject { ["domain"] = "bio", ["bioDomain"] = "ranching", ["kind"] = "incubator", ["action"] = "list" }),
                    Resource("oni://medical/patients", "colony_control", "医疗患者", "需要医疗关注的复制人、疾病、生命值和医疗床分配。", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "patients" }),
                    Resource("oni://medical/clinics", "colony_control", "医疗床和诊所", "医疗床/诊所治疗阈值、分配对象和优先级。", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "clinics" }),
                    Resource("oni://medical/doctor-stations", "colony_control", "医生站", "医生站药品库存和可治疗患者。", new JObject { ["domain"] = "management", ["kind"] = "medical", ["action"] = "doctor_stations" }),
                    Resource("oni://sandbox/actions", "game_control", "沙盒操作", "MCP 暴露的沙盒/Debug 操作、风险和当前沙盒状态。", new JObject { ["domain"] = "sandbox", ["kind"] = "read", ["action"] = "list_actions" }),
                    Resource("oni://sandbox/story-traits", "game_control", "沙盒故事特质", "可由沙盒 Story Trait Tool 放置的故事特质模板。", new JObject { ["domain"] = "sandbox", ["kind"] = "read", ["action"] = "list_story_traits" }),
                    Resource("oni://game/time", "game_control", "游戏时间和速度", "当前周期、时间百分比、暂停状态和速度。", new JObject { ["domain"] = "speed", ["action"] = "time" }),
                    Resource("oni://game/red-alert", "game_control", "红色警戒", "当前/全部世界红色警戒（紧急模式）状态。", new JObject { ["domain"] = "state", ["action"] = "red_alert_status" }),
                    Resource("oni://game/saves", "game_control", "存档文件", "本地/云端存档文件、当前 active save 和保存根目录。", new JObject { ["domain"] = "save", ["action"] = "list" }),
                    Resource("oni://game/dlc", "game_control", "DLC 存档激活状态", "暂停菜单 DLC 激活按钮状态：订阅、当前存档启用、是否允许激活。", new JObject { ["domain"] = "dlc", ["action"] = "list" }),
                    Resource("oni://mcp/sessions", "server_control", "MCP 会话", "当前 MCP session 和客户端 sampling、elicitation、tasks 能力。", new JObject { ["domain"] = "diagnostics", ["action"] = "capabilities" }),
                    Resource("oni://ui/actions", "game_control", "UI Action 白名单", "可安全触发的管理菜单、覆盖层、建造分类和导航 Action。", new JObject { ["domain"] = "ui", ["uiDomain"] = "action", ["action"] = "list" }),
                    Resource("oni://tools/manifest", "server_control", "工具清单", "ONI MCP 工具目录。", new JObject { ["domain"] = "catalog", ["action"] = "manifest" }),
                    Resource("oni://tools/guide", "server_control", "工具意图指南", "按玩家目标推荐资源、工具链和批量策略。", new JObject { ["domain"] = "catalog", ["action"] = "guide" }),
                    Resource("oni://tools/player-action-coverage", "server_control", "玩家操作覆盖审计", "玩家可执行操作面、对应 MCP 工具和缺口状态。", new JObject { ["domain"] = "catalog", ["action"] = "coverage" }),
                    Resource("oni://tools/side-screen-surfaces", "server_control", "侧屏 surface 审计", "运行时 SideScreenContent 类型到 MCP 工具/资源覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "side_screen" }),
                    Resource("oni://tools/user-menu-surfaces", "server_control", "用户菜单 surface 审计", "源码 UserMenu/context-menu 按钮来源到 MCP 工具/资源覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "user_menu" }),
                    Resource("oni://tools/management-surfaces", "server_control", "管理界面 surface 审计", "源码 ManagementMenu/TableScreen/全屏管理界面到 MCP 工具/资源覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "management" }),
                    Resource("oni://tools/tool-menu-surfaces", "server_control", "工具栏 surface 审计", "源码 ToolMenu 主工具栏/沙盒工具栏到 MCP 工具/资源覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "tool_menu" }),
                    Resource("oni://tools/ui-menu-surfaces", "server_control", "UI 菜单 surface 审计", "源码 OverlayMenu/PlanScreen/BuildMenu/安全 UI hotkey 到 MCP 工具/资源覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "ui_menu" }),
                    Resource("oni://tools/global-control-surfaces", "server_control", "全局控制 surface 审计", "源码 SpeedControlScreen/TopLeftControlScreen/PauseScreen/Options/Locker 到 MCP 覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "global_control" }),
                    Resource("oni://tools/notification-surfaces", "server_control", "通知 surface 审计", "源码 NotificationScreen/NotificationManager/消息通知到 MCP 覆盖的映射审计。", new JObject { ["domain"] = "catalog", ["action"] = "surface_audit", ["surface"] = "notification" }),
                    Resource("oni://tools/static-audit", "server_control", "静态接口审计", "工具注册、玩家操作覆盖、资源入口和危险工具确认参数的静态自检。", new JObject { ["domain"] = "catalog", ["action"] = "static_audit" }),
                    Resource("oni://power/summary", "read_control", "电力摘要", "当前世界电力系统摘要：发电机额定功率、消费者负载、电池容量和电量，按 circuitId 聚合。", new JObject { ["domain"] = "infrastructure", ["action"] = "power_summary" }),
                    Resource("oni://power/ports", "read_control", "电力接口", "指定区域内建筑的电力接口格：锚点、输入/输出端口、相对偏移和可接线状态。", new JObject { ["domain"] = "infrastructure", ["action"] = "power_ports" }),
                    Resource("oni://rooms/list", "read_control", "房间列表", "房间系统状态：房间类型、大小、边界、对象计数和房间效果，适合检查士气房间是否成型。", new JObject { ["domain"] = "infrastructure", ["action"] = "rooms" }),
                    Resource("oni://thermal/overheat-risk", "read_control", "过热风险扫描", "建筑过热风险扫描：按当前格温和建筑过热温度差排序，发现即将过热或已经过热的设备。", new JObject { ["domain"] = "world", ["action"] = "thermal_overheat_risk" }),
                    Resource("oni://world/search", "read_control", "地图搜索", "按 query/条件在地图上搜索元素格、建筑、散落物和复制人，支持区域过滤和最近排序。", new JObject { ["domain"] = "world", ["action"] = "search" }),
                    Resource("oni://world/coordinate-screenshot", "navigation_control", "坐标截图", "保存带 ONI 世界坐标网格和坐标文本的截图，返回本地路径和 HTTP URL，适合视觉模型直接识别坐标。", new JObject { ["action"] = "coordinate_screenshot" }),
                    Resource("oni://world/layout-candidates", "read_control", "平面布局候选", "按用途扫描区域，返回房间/平台候选矩形、评分、需挖掘、需铺砖、危险格和连通性。", new JObject { ["domain"] = "world", ["action"] = "layout_candidates" })
                };

                private static readonly List<McpResourceTemplateInfo> _templates = BuildResourceTemplates();

        private static List<McpResourceTemplateInfo> BuildResourceTemplates()
        {
            var templates = new List<McpResourceTemplateInfo>();
            AddWorldAndColonyResourceTemplates(templates);
            AddBuildingAndSpaceResourceTemplates(templates);
            AddDuplicantAndUiResourceTemplates(templates);
            return templates;
        }

        private static ReadResourceResult ReadDynamicResource(string uri)
        {
            Uri parsed;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed) || parsed.Scheme != "oni")
                return null;

            ReadResourceResult result;
            result = ReadWorldAndColonyResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            result = ReadBuildingAndSpaceResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            result = ReadDuplicantAndUiResourceRoutes(uri, parsed);
            if (result != null)
                return result;

            return null;
        }
    }
}
