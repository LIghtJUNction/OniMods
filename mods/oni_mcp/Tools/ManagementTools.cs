using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class ManagementTools
    {
        public static McpTool ControlManagement()
        {
            return new McpTool
            {
                Name = "management_control",
                Group = "management",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "colony_management_control", "management_screen_control" },
                Tags = new List<string> { "management", "schedule", "diet", "research", "medical" },
                Description = "殖民地管理聚合入口：domain=schedule/diet/research/medical；路由日程、饮食权限、研究队列和医疗管理，保留各子工具原 action 与确认规则。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "schedule、diet、research 或 medical", Required = true, EnumValues = new List<string> { "schedule", "diet", "research", "medical" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "对应 domain 的原 action，例如 schedule=list/create/set_block/assign_dupe/optimize，diet=status/set/policy，research=status/list/set/clear，medical=patients/clinics/doctor_stations/set_threshold/batch_set_threshold/assign_bed", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标 InstanceID；按子动作语义使用", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "目标名称；按子动作语义使用", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索或筛选词；按子动作语义使用", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "列表或批量处理上限；按子动作语义使用", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写入或危险动作需要确认时传 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "schedule":
                        case "schedules":
                            return ScheduleTools.ControlSchedule().Handler(args);
                        case "diet":
                        case "consumables":
                            return DietTools.ControlDiet().Handler(args);
                        case "research":
                        case "tech":
                            return ResearchTools.ControlResearch().Handler(args);
                        case "medical":
                        case "medicine":
                        case "clinic":
                            return MedicalTools.ControlMedical().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be schedule, diet, research, or medical");
                    }
                }
            };
        }
    }
}
