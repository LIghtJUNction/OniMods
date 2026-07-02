using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class ReadTools
    {
        public static McpTool ControlRead()
        {
            return new McpTool
            {
                Name = "read_control",
                Group = "read",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "state_read_control", "query_control" },
                Tags = new List<string> { "read", "world", "area", "buildings", "resources", "infrastructure", "knowledge", "database", "guide" },
                Description = "读类聚合入口：domain=world/area/buildings/resources/infrastructure/knowledge；路由地图、区域句柄、建筑、资源、电力/房间和百科/机制查询，resources=set_pin 需 confirm。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "world、area、buildings、resources、infrastructure 或 knowledge", Required = true, EnumValues = new List<string> { "world", "area", "buildings", "resources", "infrastructure", "knowledge" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "对应 domain 的原 action，例如 world=cell_info/text_map/search，area=define/get/list/blocks/merge/forget，buildings=list/summary，resources=inventory/food/search_items/pins/set_pin，infrastructure=power_summary/power_ports/rooms，knowledge=query", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=knowledge 时选择 database 或 guide；也供部分兼容子动作使用", Required = false, EnumValues = new List<string> { "database", "guide" } },
                    ["id"] = new McpToolParameter { Type = "string", Description = "domain=knowledge kind=database 时精确条目 ID 或子条目 ID；优先于 query", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索或筛选词；domain=knowledge 时匹配百科条目或机制速查条目", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "domain=knowledge 时可选分类过滤；database 常用 BUILDINGS/ELEMENTS/CREATURES，guide 常用 thermal/oxygen/power 等", Required = false },
                    ["includeContent"] = new McpToolParameter { Type = "boolean", Description = "domain=knowledge kind=database 时是否返回正文摘要，默认 true", Required = false },
                    ["includeDisabled"] = new McpToolParameter { Type = "boolean", Description = "domain=knowledge kind=database 时是否包含禁用条目，默认 false", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "domain=knowledge kind=guide 时 detail=brief/full，默认 brief", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回上限；按子动作语义使用", Required = false },
                    ["maxResults"] = new McpToolParameter { Type = "integer", Description = "domain=knowledge kind=database 时最多返回多少百科条目，默认 3，最大 10", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标或区域 X；按子动作语义使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标或区域 Y；按子动作语义使用", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "domain=area action=define 时区域左下/起点 X；也供兼容子动作使用", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "domain=area action=define 时区域左下/起点 Y；也供兼容子动作使用", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "domain=area action=define 时区域右上/终点 X；也供兼容子动作使用", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "domain=area action=define 时区域右上/终点 Y；也供兼容子动作使用", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=area action=get/forget/merge 的区域句柄；merge 也支持 blk1+blk2", Required = false },
                    ["areaIds"] = new McpToolParameter { Type = "array", Description = "domain=area action=merge 的区域句柄数组", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "domain=area action=define/blocks/merge 的可选标签", Required = false },
                    ["blockWidth"] = new McpToolParameter { Type = "integer", Description = "domain=area action=blocks 的块宽度；建议 20..50", Required = false },
                    ["blockHeight"] = new McpToolParameter { Type = "integer", Description = "domain=area action=blocks 的块高度；建议 20..50", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "domain=area action=blocks 的每块目标最大格子数，默认 1600", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "domain=area action=merge 时只返回拼接预览，不创建新 areaId", Required = false },
                    ["pattern"] = new McpToolParameter { Type = "string", Description = "domain=world action=search 的连续格子排列，例如 粉砂岩-泥土-氧气、SiltStone>Dirt>Oxygen、Dirt{3}", Required = false },
                    ["sequence"] = new McpToolParameter { Type = "string", Description = "pattern 的别名；支持 * 任意格、A|B 备选、term{N} 重复", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "pattern/sequence 搜索方向：horizontal、vertical 或 both，默认 both", Required = false, EnumValues = new List<string> { "horizontal", "vertical", "both" } },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "pattern/query 匹配模式：exact 精确、smart 规范化/包含、fuzzy 少量拼写误差，默认 smart", Required = false, EnumValues = new List<string> { "exact", "smart", "fuzzy" } },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；按子动作语义使用", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "resources set_pin 等写入动作需要确认时传 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    OniMcp.Support.OniMcpLog.Debug($"[OniMcp] read_control: domain={domain}, action={action}");
                    switch (domain)
                    {
                        case "world":
                        case "map":
                            return WorldAnalysisTools.ReadWorldControl().Handler(args);
                        case "area":
                        case "areas":
                        case "region":
                        case "regions":
                            return AreaTools.ControlArea().Handler(args);
                        case "buildings":
                        case "building":
                            return GameControlTools.ControlBuildingsRead().Handler(args);
                        case "resources":
                        case "resource":
                        case "inventory":
                            return InventoryTools.ControlResources().Handler(args);
                        case "infrastructure":
                        case "infra":
                        case "power":
                        case "rooms":
                            return PowerAndRoomTools.InfrastructureReadControl().Handler(args);
                        case "knowledge":
                        case "database":
                        case "guide":
                        case "mechanics":
                        case "mechanic":
                            return ForwardKnowledge(args, domain);
                        default:
                            return CallToolResult.Error("domain must be world, area, buildings, resources, infrastructure, or knowledge");
                    }
                }
            };
        }

        private static CallToolResult ForwardKnowledge(Newtonsoft.Json.Linq.JObject args, string domain)
        {
            var forwarded = args == null ? new Newtonsoft.Json.Linq.JObject() : (Newtonsoft.Json.Linq.JObject)args.DeepClone();
            string kind = (forwarded["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(kind))
            {
                if (domain == "database")
                    kind = "database";
                else if (domain == "guide" || domain == "mechanics" || domain == "mechanic")
                    kind = "guide";
                else
                    kind = "database";
            }

            forwarded["domain"] = kind;
            forwarded.Remove("kind");
            if ((forwarded["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant() == "query")
                forwarded.Remove("action");
            return DatabaseTools.ControlKnowledgeQuery().Handler(forwarded);
        }
    }
}
