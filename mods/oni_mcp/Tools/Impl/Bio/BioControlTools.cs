using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class BioControlTools
    {
        public static McpTool ControlBio()
        {
            return new McpTool
            {
                Name = "bio_control",
                Group = "bio",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "life_control", "farming_ranching_control" },
                Tags = new List<string> { "farming", "ranching", "plants", "critters", "seeds", "incubator", "dropoff" },
                Description = "生物生产统一入口：domain=farming/ranching。种植、收获、铲除、小动物、投放点和孵化器都通过 domain 参数路由。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "farming 或 ranching", Required = true, EnumValues = new List<string> { "farming", "ranching" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "farming: list_planting/seed_catalog/list_harvestables/set_harvestable/set_planting/batch_set_planting/uproot；ranching: critters/list/configure/batch", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=ranching 时为 critters/dropoff/incubator", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索或筛选词，按子动作语义使用", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，按子动作语义使用", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标 X，按子动作语义使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标 Y，按子动作语义使用", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，按子动作语义使用", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回或处理上限", Required = false },
                    ["seedTag"] = new McpToolParameter { Type = "string", Description = "domain=farming 设置种植请求时的种子 prefab/tag", Required = false },
                    ["cropSeedOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=farming action=seed_catalog 时仅返回带 CropSeed 标签的候选种子", Required = false },
                    ["eggTag"] = new McpToolParameter { Type = "string", Description = "domain=ranching kind=incubator 设置蛋请求时的蛋 prefab/tag", Required = false },
                    ["critterTags"] = new McpToolParameter { Type = "array", Description = "domain=ranching kind=dropoff 的小动物 prefab/tag 列表", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "批量或危险写操作确认，按子工具规则使用", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "farming":
                        case "farm":
                        case "planting":
                        case "plants":
                            return Forward(args, FarmingTools.ControlPlanting());
                        case "ranching":
                        case "ranch":
                        case "critters":
                        case "critter":
                            return Forward(args, RanchingTools.ControlRanching());
                        default:
                            return CallToolResult.Error("domain must be farming or ranching");
                    }
                }
            };
        }

        private static CallToolResult Forward(JObject args, McpTool tool)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded.Remove("domain");
            return tool.Handler(forwarded);
        }
    }
}
