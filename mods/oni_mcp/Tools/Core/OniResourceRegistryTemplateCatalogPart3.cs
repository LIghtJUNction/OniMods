using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static void AddResourceTemplatesPart3(List<McpResourceTemplateInfo> templates)
        {
            templates.AddRange(new[]
            {
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
            });
        }
    }
}
