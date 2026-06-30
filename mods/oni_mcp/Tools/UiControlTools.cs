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
                Tags = new List<string> { "ui", "action", "feedback", "hint", "marker", "edit-mark", "management", "overlay" },
                Description = "UI 聚合入口：domain=action/feedback/edit_mark。action 读取或触发安全 UI Action，feedback 创建通知/浮字/地图标记，edit_mark 管理框选区域编辑请求。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "action、feedback 或 edit_mark", Required = true, EnumValues = new List<string> { "action", "feedback", "edit_mark" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=action: list/trigger/open_management；domain=feedback: notification/popup/marker；domain=edit_mark: create/list/clear", Required = true },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=action action=list 时过滤类型：all/management/overlay/build/navigation；其他 domain 按子工具语义使用", Required = false },
                    ["uiAction"] = new McpToolParameter { Type = "string", Description = "domain=action action=trigger 时的 Action 枚举名", Required = false },
                    ["screen"] = new McpToolParameter { Type = "string", Description = "domain=action action=open_management 时的页面名", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "通知正文或提示内容，按子动作语义使用", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=popup 时浮动提示文字", Required = false },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=feedback action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "domain=edit_mark action=create 时用户对框选区域的修改提示词", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=edit_mark action=create 时可选区域句柄", Required = false },
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
                        case "edit_mark":
                        case "edit_marks":
                        case "edit_marker":
                            return ForwardFeedback(args, "edit_mark");
                        default:
                            return CallToolResult.Error("domain must be action, feedback, or edit_mark");
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
