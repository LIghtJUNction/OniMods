using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// MCP prompt registry for reusable ONI agent workflows.
    /// </summary>
    public static class OniPromptRegistry
    {
        private static readonly List<McpPrompt> _prompts = new List<McpPrompt>
        {
            new McpPrompt
            {
                Name = "colony_triage",
                Title = "殖民地快速诊断",
                Description = "快速体检当前殖民地，优先找会导致死亡、停电、缺氧或断粮的问题。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "focus", Title = "关注点", Description = "可选关注点，例如 oxygen、food、power、dupes。", Required = false }
                },
                Builder = args => BuildResult(
                    "殖民地快速诊断流程",
                    "你是 Oxygen Not Included 殖民地诊断助手。先读取 oni://colony/status、oni://colony/diagnostics、oni://colony/alerts 和 oni://resources/food，再按风险排序给出下一步行动。" +
                    Optional(args, "focus", " 重点关注：{0}。") +
                    " 对会修改存档的动作，只提出建议，除非用户明确要求执行。")
            },
            new McpPrompt
            {
                Name = "next_cycle_plan",
                Title = "下一周期计划",
                Description = "根据当前状态生成下一周期行动计划。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "objective", Title = "目标", Description = "目标，例如 stabilize oxygen、expand food、research。", Required = false },
                    new McpPromptArgument { Name = "riskTolerance", Title = "风险偏好", Description = "风险偏好：low、medium、high。", Required = false }
                },
                Builder = args => BuildResult(
                    "下一周期规划流程",
                    "读取 oni://colony/summary、oni://resources/inventory、oni://research/status、oni://schedules 和 oni://dupes。输出一个紧凑的下一周期计划：立即处理、可排队、暂缓。" +
                    Optional(args, "objective", " 目标：{0}。") +
                    Optional(args, "riskTolerance", " 风险偏好：{0}。"))
            },
            new McpPrompt
            {
                Name = "inspect_area",
                Title = "检查区域",
                Description = "分析指定地图区域，优先使用文本地图而不是截图。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "x1", Title = "X1", Description = "左下 X。", Required = false },
                    new McpPromptArgument { Name = "y1", Title = "Y1", Description = "左下 Y。", Required = false },
                    new McpPromptArgument { Name = "x2", Title = "X2", Description = "右上 X。", Required = false },
                    new McpPromptArgument { Name = "y2", Title = "Y2", Description = "右上 Y。", Required = false }
                },
                Builder = args => BuildResult(
                    "区域检查流程",
                    "先读取 oni://world/text-map?x1=" + Arg(args, "x1", "") + "&y1=" + Arg(args, "y1", "") + "&x2=" + Arg(args, "x2", "") + "&y2=" + Arg(args, "y2", "") + "&profile=scan。用 RLE 文本地图低 token 初扫地形和气液固分布；需要建筑、复制人、资源或每格明细时，再用同一 areaId 调 world_text_map 并打开 includeBuildings/includeDupes/includeItems/includeElements 或 detail=full。只有需要视觉确认时再调用截图工具。")
            },
            new McpPrompt
            {
                Name = "dupe_care_review",
                Title = "复制人照护检查",
                Description = "检查复制人需求、压力、日程和技能配置。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "dupe", Title = "复制人", Description = "可选复制人姓名或 ID。", Required = false }
                },
                Builder = args => BuildResult(
                    "复制人照护流程",
                    "读取 oni://dupes 和 oni://schedules。若指定复制人，调用 dupes_control domain=info action=detail/needs/attributes 获取细节。" +
                    Optional(args, "dupe", " 指定复制人：{0}。") +
                    " 输出照护风险、可调整日程、饮食和技能建议。")
            },
            new McpPrompt
            {
                Name = "power_audit",
                Title = "电力审计",
                Description = "检查殖民地电力系统健康度，发现供电缺口、电池耗尽风险和导线过载。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "worldId", Title = "世界 ID", Description = "世界 ID，默认当前世界。", Required = false },
                    new McpPromptArgument { Name = "detail", Title = "详情级别", Description = "详情级别：brief、summary、full，默认 summary。", Required = false }
                },
                Builder = args => BuildResult(
                    "电力审计流程",
                    "先读取 oni://power/summary 获取整体电力状态。检查是否有 circuit 负载接近或超过 100%，检查电池电量是否偏低。" +
                    Optional(args, "detail", " 详情级别：{0}。若该值为 full，再读取 oni://buildings/configurables 过滤电力相关建筑，获取详细配置。") +
                    " 给出优化建议：增加发电、增加电池、减少负载、分电路。" +
                    Optional(args, "worldId", " 世界 ID：{0}。"))
            },
            new McpPrompt
            {
                Name = "rooms_overview",
                Title = "房间概览",
                Description = "检查殖民地房间系统状态，发现士气房间缺口和房间条件未满足的问题。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "worldId", Title = "世界 ID", Description = "世界 ID，默认当前世界。", Required = false },
                    new McpPromptArgument { Name = "focus", Title = "关注点", Description = "关注点，例如 morale、bed、food、toilet、recreation。", Required = false }
                },
                Builder = args => BuildResult(
                    "房间概览流程",
                    "读取 oni://rooms/list 获取所有房间。检查是否有缺失的关键房间类型（Barracks、Great Hall、Washroom、Recreation 等），检查房间大小和条件是否满足。" +
                    Optional(args, "focus", " 当前关注点：{0}。") +
                    " 给出房间规划建议，优先补齐士气相关房间。" +
                    Optional(args, "worldId", " 世界 ID：{0}。"))
            },
            new McpPrompt
            {
                Name = "thermal_audit",
                Title = "热管理审计",
                Description = "扫描殖民地过热风险，发现即将过热的设备和高温区域。",
                Arguments = new List<McpPromptArgument>
                {
                    new McpPromptArgument { Name = "worldId", Title = "世界 ID", Description = "世界 ID，默认当前世界。", Required = false },
                    new McpPromptArgument { Name = "marginC", Title = "风险温差阈值", Description = "风险温差阈值，默认 15°C。", Required = false }
                },
                Builder = args => BuildResult(
                    "热管理审计流程",
                    "读取 oni://thermal/overheat-risk?marginC=" + Arg(args, "marginC", "15") + " 扫描风险建筑。如果有 overheated 状态的设备，优先处理。" +
                    " 检查高温区域的元素分布（可用 oni://world/elements 辅助）。" +
                    " 给出降温建议：增加冷却、改善通风、使用隔热砖、移除热源。" +
                    Optional(args, "worldId", " 世界 ID：{0}。"))
            }
        };

        public static List<McpPromptInfo> GetPromptInfos()
        {
            return _prompts
                .Select(prompt => new McpPromptInfo
                {
                    Name = prompt.Name,
                    Title = prompt.Title,
                    Description = prompt.Description,
                    Arguments = prompt.Arguments
                })
                .OrderBy(prompt => prompt.Name)
                .ToList();
        }

        public static GetPromptResult GetPrompt(string name, Dictionary<string, string> arguments)
        {
            var prompt = _prompts.FirstOrDefault(p => p.Name == name);
            if (prompt == null)
                return null;
            return prompt.Builder(arguments ?? new Dictionary<string, string>());
        }

        private static GetPromptResult BuildResult(string description, string text)
        {
            return new GetPromptResult
            {
                Description = description,
                Messages = new List<PromptMessage>
                {
                    new PromptMessage
                    {
                        Role = "user",
                        Content = new ToolContent { Type = "text", Text = text }
                    }
                }
            };
        }

        private static string Arg(Dictionary<string, string> args, string key, string fallback)
        {
            string value;
            if (args != null && args.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                return value;
            return fallback;
        }

        private static string Optional(Dictionary<string, string> args, string key, string template)
        {
            string value = Arg(args, key, "");
            return string.IsNullOrEmpty(value) ? "" : string.Format(template, value);
        }

        private class McpPrompt
        {
            public string Name { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public List<McpPromptArgument> Arguments { get; set; }
            public Func<Dictionary<string, string>, GetPromptResult> Builder { get; set; }
        }
    }
}
