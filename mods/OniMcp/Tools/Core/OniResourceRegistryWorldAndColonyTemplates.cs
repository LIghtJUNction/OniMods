using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static void AddWorldAndColonyResourceTemplates(List<McpResourceTemplateInfo> templates)
        {
            templates.AddRange(new[]
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
                                    }
            });
        }
    }
}
