using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class UiControlTools
    {
        public static McpTool ControlUi()
        {
            return new McpTool
            {
                Name = "ui_control",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "ui_unified_control", "player_feedback_control" },
                Tags = new List<string> { "ui", "action", "feedback", "hint", "marker", "management", "overlay" },
                Description = "UI 聚合入口：domain=action/feedback。action 读取或触发安全 UI Action，feedback 创建通知/浮字/复制人聊天气泡/地图标记。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "action 或 feedback", Required = true, EnumValues = new List<string> { "action", "feedback" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=action: list/trigger/open_management；domain=feedback: notification/popup/speech_bubble/marker", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=action action=list 时过滤类型：all/management/overlay/build/navigation；其他 domain 按子工具语义使用", Required = false },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "domain=action action=trigger 时的 Action 枚举名", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "domain=action action=open_management 时的页面名", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "通知正文或提示内容，按子动作语义使用", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=popup/speech_bubble 时显示文字；speech_bubble 必填", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=speech_bubble 时复制人名称；与 id 二选一", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "domain=feedback action=speech_bubble 时复制人 InstanceID；与 name 二选一", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "domain=feedback action=speech_bubble 时显示秒数，默认 5，范围 0.5-30", Required = false },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格 X，按 action 解释", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格 Y，按 action 解释", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点 X，按 action 解释", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点 Y，按 action 解释", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点 X，按 action 解释", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点 Y，按 action 解释", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时返回数量限制", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "强制执行，按 action 解释", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "action":
                        case "ui_action":
                        case "actions":
                            return UiTools.ControlUiAction().Handler(args);
                        case "feedback":
                        case "hint":
                        case "hints":
                            return ForwardFeedback(args, "hint");
                        default:
                            return CallToolResult.Error("domain must be action or feedback");
                    }
                }
            };
        }

        private static CallToolResult ForwardFeedback(JObject args, string feedbackDomain)
        {
            var forwarded = (JObject)args.DeepClone();
            forwarded["domain"] = feedbackDomain;
            return UiHintTools.ControlUiFeedback().Handler(forwarded);
        }
    }
}
