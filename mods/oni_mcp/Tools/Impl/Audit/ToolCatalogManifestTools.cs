using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolCatalogTools
{
        public static McpTool GetToolsManifest()
        {
            return new McpTool
            {
                Name = "tools_manifest",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "获取 ONI MCP 工具分组、读写模式、风险等级和参数摘要；默认 brief 低 token 输出，按需传 detail=full 查看完整参数",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["group"] = new McpToolParameter { Type = "string", Description = "工具分组过滤，如 game、dupes、resources、orders", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "关键词或目标意图，匹配工具名、描述、标签、别名和参数", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "read、write、execute 或 any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "none、low、medium、dangerous 或 any", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 极简；compact 返回摘要；full 返回完整参数 schema；默认 brief", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个公开组合工具；默认 80，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").ToLowerInvariant();
                    string expandedQuery = ExpandQuery(query);
                    string groupFilter = (args["group"]?.ToString() ?? "").ToLowerInvariant();
                    string mode = (args["mode"]?.ToString() ?? "any").ToLowerInvariant();
                    string risk = (args["risk"]?.ToString() ?? "any").ToLowerInvariant();
            string detail = string.IsNullOrWhiteSpace(args["detail"]?.ToString()) ? "brief" : NormalizeDetail(args["detail"]?.ToString().ToLowerInvariant());
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 80, 100));
            string exactLookup = ExactVisibleToolLookup(query);

            var matchedTools = OniToolRegistry.GetVisibleTools()
                .Where(tool => string.IsNullOrEmpty(groupFilter) || tool.Group.ToLowerInvariant() == groupFilter)
                .Where(tool => mode == "any" || string.IsNullOrEmpty(mode) || tool.Mode.ToLowerInvariant() == mode)
                .Where(tool => risk == "any" || string.IsNullOrEmpty(risk) || tool.Risk.ToLowerInvariant() == risk)
                .Where(tool => string.IsNullOrEmpty(exactLookup) || string.Equals(tool.Name, exactLookup, StringComparison.Ordinal))
                .Select(tool => new { Tool = tool, Score = Score(tool, expandedQuery, query) })
                        .Where(item => string.IsNullOrEmpty(query) || item.Score > 0)
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.Tool.Group)
                        .ThenBy(item => item.Tool.Name)
                        .Take(limit)
                        .ToList();

                    var groups = matchedTools
                        .GroupBy(item => item.Tool.Group)
                        .OrderBy(group => group.Key)
                        .Select(group => new Dictionary<string, object>
                        {
                            ["group"] = group.Key,
                            ["description"] = GroupDescription(group.Key),
                            ["tools"] = group.Select(item => ManifestByDetail(item.Tool, item.Score, detail)).ToList()
                        })
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["toolCount"] = OniToolRegistry.GetVisibleTools().Count,
                        ["returned"] = matchedTools.Count,
                        ["groupSummary"] = matchedTools
                            .GroupBy(item => item.Tool.Group)
                            .OrderBy(group => group.Key)
                            .Select(group => new Dictionary<string, object>
                            {
                                ["group"] = group.Key,
                                ["count"] = group.Count(),
                                ["description"] = GroupDescription(group.Key),
                                ["sampleTools"] = group.Select(item => item.Tool.Name).OrderBy(name => name).Take(8).ToList()
                            })
                            .ToList(),
                        ["detail"] = detail,
                        ["query"] = query,
                        ["expandedQuery"] = expandedQuery,
                        ["limit"] = limit,
                        ["groups"] = groups,
                        ["modes"] = new[] { "read", "write", "execute" },
                        ["risks"] = new[] { "none", "low", "medium", "dangerous" }
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ToolToBriefManifest(McpTool tool, int score)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["g"] = tool.Group,
                ["m"] = tool.Mode,
                ["r"] = tool.Risk,
                ["score"] = score
            };

            var required = tool.Parameters
                .Where(kv => kv.Value.Required)
                .Select(kv => kv.Key)
                .OrderBy(name => name)
                .ToList();
            if (required.Count > 0)
                result["req"] = required;

            return result;
        }

        private static Dictionary<string, object> ManifestByDetail(McpTool tool, int score, string detail)
        {
            if (detail == "brief")
                return ToolToBriefManifest(tool, score);
            if (detail == "compact")
                return ToolToCompactManifest(tool, score);
            return ToolToManifest(tool);
        }

        private static Dictionary<string, object> ToolToCompactManifest(McpTool tool, int score)
        {
            return new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["group"] = tool.Group,
                ["mode"] = tool.Mode,
                ["risk"] = tool.Risk,
                ["score"] = score,
                ["summary"] = tool.Description,
                ["required"] = tool.Parameters
                    .Where(kv => kv.Value.Required)
                    .Select(kv => kv.Key)
                    .OrderBy(name => name)
                    .ToList(),
                ["aliases"] = tool.Aliases != null && tool.Aliases.Count > 0 ? (object)tool.Aliases : null
            };
        }

        private static Dictionary<string, object> ToolToManifest(McpTool tool)
        {
            return new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["group"] = tool.Group,
                ["mode"] = tool.Mode,
                ["risk"] = tool.Risk,
                ["description"] = tool.Description,
                ["aliases"] = tool.Aliases,
                ["tags"] = tool.Tags,
                ["parameters"] = tool.Parameters
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => new Dictionary<string, object>
                        {
                            ["type"] = kv.Value.Type,
                            ["description"] = kv.Value.Description,
                            ["required"] = kv.Value.Required,
                            ["enum"] = kv.Value.SchemaEnumValues
                        }),
                ["exampleArguments"] = ExampleArguments(tool)
            };
        }

        private static Dictionary<string, object> ExampleArguments(McpTool tool)
        {
            var example = new Dictionary<string, object>();
            foreach (var param in tool.Parameters)
            {
                if (!param.Value.Required)
                    continue;
                switch (param.Value.Type)
                {
                    case "integer":
                        example[param.Key] = param.Key == "hour" ? 8 : 1;
                        break;
                    case "number":
                        example[param.Key] = 1.0;
                        break;
                    case "boolean":
                        example[param.Key] = true;
                        break;
                    case "array":
                        example[param.Key] = new[] { "Dirt" };
                        break;
                    default:
                        example[param.Key] = param.Value.EnumValues != null && param.Value.EnumValues.Count > 0 ? param.Value.EnumValues[0] : "value";
                        break;
                }
            }
            AddPointerExampleArguments(tool, example);
            return example;
        }

        private static void AddPointerExampleArguments(McpTool tool, Dictionary<string, object> example)
        {
            if (tool == null || !string.Equals(tool.Name, "navigation_control", StringComparison.Ordinal))
                return;

            if (!example.ContainsKey("action"))
                example["action"] = "get";
            if (tool.Parameters.ContainsKey("agentId") && !example.ContainsKey("agentId"))
                example["agentId"] = "planner";
            if (tool.Parameters.ContainsKey("displayText") && !example.ContainsKey("displayText"))
                example["displayText"] = PointerDisplayTextExample(tool.Name);
        }

        private static string PointerDisplayTextExample(string toolName)
        {
            return "正在操作指针";
        }

        private static string GroupDescription(string group)
        {
            switch (group)
            {
                case "tools": return "工具目录、搜索和能力发现。";
                case "database": return "游戏内置 Database/百科条目查询，以及结构化玩家机制/公式速查。";
                case "server": return "MCP 服务状态和连接信息。";
                case "game": return "游戏时间、暂停、速度、红色警戒/紧急模式和截图。";
                case "camera": return "相机视角、聚焦和观察控制。";
                case "map": return "地图上的提示、标记和可视反馈。";
                case "ui": return "管理面板、覆盖层、建造分类、百科和安全 UI Action 入口。";
                case "sandbox": return "沙盒/调试模式下的生成、刷元素和清除操作。";
                case "colony": return "殖民地总体状态、告警和诊断。";
                case "diagnostics": return "诊断条件、过程状态和异常/风险检查。";
                case "dupes": return "复制人列表、属性、需求、改名和自动命名。";
                case "schedules": return "日程读取、区块编辑和复制人分配。";
                case "resources": return "资源、食物、库存和储存过滤器。";
                case "diet": return "复制人饮食许可、食物策略和口粮控制。";
                case "filters": return "单选元素过滤器和树形/平铺多选过滤器。";
                case "controls": return "方向、少量选项、广播频道等通用侧屏控件。";
                case "automation": return "自动化、逻辑、传感器阈值和自动化侧屏设置。";
                case "buildings": return "建筑查询、蓝图、运行状态、侧屏配置、优先级和拆除。";
                case "production": return "制作站、精炼、厨房等配方队列和生产设置。";
                case "orders": return "对地图区域下达清扫、挖掘等命令。";
                case "ranching": return "小动物抓捕、放生和牧场相关命令。";
                case "farming": return "植物收获、铲除、种植选择和种植槽状态。";
                case "medical": return "医疗床、诊疗阈值和护理相关分配。";
                case "power": return "电力网络、电池、发电/耗电统计和电力接口格。";
                case "rooms": return "房间识别、房间类型和士气相关概览。";
                case "rockets": return "火箭、发射台、舱组、乘员货物和飞行控制。";
                case "space": return "星图、望远镜、太空目标和裂隙相关操作。";
                case "story": return "故事建筑、遗迹设施、传送器和特殊剧情对象。";
                case "research": return "研究状态、科技列表、研究队列设置和取消。";
                case "world": return "地图格子、元素、温度、气液固统计。";
                default: return "未归类工具。";
            }
        }
}
}
