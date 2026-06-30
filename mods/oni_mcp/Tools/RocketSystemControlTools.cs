using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class RocketSystemControlTools
    {
        public static McpTool ControlRocketSystem()
        {
            return new McpTool
            {
                Name = "rocket_system_control",
                Group = "rockets",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "rocket_control", "rocket_domain_control" },
                Tags = new List<string> { "rocket", "space", "modules", "crew", "cargo", "restriction", "side-screen" },
                Description = "统一火箭系统入口。domain=ops/module/flight_utility/restriction/usage/crew_request/assignment_group/cargo_status/self_destruct；action 透传到对应旧 control。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "火箭子系统：ops、module、flight_utility、restriction、usage、crew_request、assignment_group、cargo_status、self_destruct", Required = true, EnumValues = new List<string> { "ops", "module", "flight_utility", "restriction", "usage", "crew_request", "assignment_group", "cargo_status", "self_destruct" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子系统动作，沿用对应旧 control 的 action 值", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标火箭、模块、建筑或控制器 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "目标火箭名称；domain=ops 时可用", Required = false },
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "目标火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "目标火箭名称", Required = false },
                    ["moduleId"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块 InstanceID", Required = false },
                    ["moduleName"] = new McpToolParameter { Type = "string", Description = "目标火箭模块名称", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "读取动作的关键词过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "读取动作最多返回数量", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层写入/执行/危险动作需要 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "ops":
                        case "operation":
                        case "operations":
                            return RocketTools.ControlRocketOps().Handler(args);
                        case "module":
                        case "modules":
                            return RocketModuleTools.ControlModule().Handler(args);
                        case "flight_utility":
                        case "flight":
                        case "utility":
                            return RocketFlightUtilityTools.ControlFlightUtility().Handler(args);
                        case "restriction":
                        case "restrictions":
                            return RocketFlightUtilityTools.ControlRocketRestriction().Handler(args);
                        case "usage":
                        case "usage_control":
                            return SpecialUserMenuActionTools.ControlRocketUsage().Handler(args);
                        case "crew_request":
                        case "crew":
                            return RocketCrewCargoTools.ControlCrewRequest().Handler(args);
                        case "assignment_group":
                        case "assignment":
                            return RocketCrewCargoTools.ControlAssignmentGroup().Handler(args);
                        case "cargo_status":
                        case "cargo":
                            return RocketCrewCargoTools.ControlCargoStatus().Handler(args);
                        case "self_destruct":
                            return MiscSideScreenTools.ControlSelfDestruct().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be ops, module, flight_utility, restriction, usage, crew_request, assignment_group, cargo_status, or self_destruct");
                    }
                }
            };
        }
    }
}
