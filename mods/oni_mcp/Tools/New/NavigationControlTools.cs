using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class NavigationControlTools
    {
        public static McpTool ControlNavigation()
        {
            return new McpTool
            {
                Name = "navigation_control",
                Group = "camera",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "spatial_control", "view_control", "pointer_camera_control" },
                Tags = new List<string> { "camera", "pointer", "navigation", "screenshot", "overlay", "click", "drag" },
                Description = "空间导航聚合入口：domain=camera/pointer；camera 处理视角、覆盖层和截图，pointer 处理可视 agent 指针、选工具、点击和拖拽。不传 domain 时按 action 自动判断。",
                Parameters = Params(),
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(action))
                        return CallToolResult.Error("action is required");

                    if (string.IsNullOrWhiteSpace(domain))
                        domain = InferDomain(action);

                    var forwarded = new JObject(args);
                    forwarded.Remove("domain");
                    switch (domain)
                    {
                        case "camera":
                        case "view":
                        case "screenshot":
                            return CameraTools.ControlCamera().Handler(forwarded);
                        case "pointer":
                        case "agent_pointer":
                        case "mouse":
                            return AgentPointerTools.Control().Handler(forwarded);
                        default:
                            return CallToolResult.Error("domain must be camera or pointer");
                    }
                }
            };
        }

        private static string InferDomain(string action)
        {
            switch (action)
            {
                case "get_view":
                case "set_active_world":
                case "set_world":
                case "switch_world":
                case "set_view":
                case "move":
                case "switch_view":
                case "overlay":
                case "focus_cell":
                case "focus_dupe":
                case "screenshot":
                case "game_screenshot":
                case "coordinate_screenshot":
                case "coord_screenshot":
                    return "camera";
                default:
                    return "pointer";
            }
        }

        private static Dictionary<string, McpToolParameter> Params()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["domain"] = new McpToolParameter { Type = "string", Description = "camera 或 pointer；省略时按 action 自动判断", Required = false, EnumValues = new List<string> { "camera", "pointer" } },
                ["action"] = new McpToolParameter { Type = "string", Description = "camera=get_view/set_active_world/set_view/move/switch_view/focus_cell/focus_dupe/screenshot/coordinate_screenshot；pointer=get/user_mouse/aim_cell/aim_world/nudge/select_tool/say/left_click/hold_left/jump/jump_point/clear", Required = true },
                ["agentId"] = new McpToolParameter { Type = "string", Description = "domain=pointer：稳定指针名，省略使用默认 agent", Required = false },
                ["displayText"] = new McpToolParameter { Type = "string", Description = "domain=pointer：显示在指针旁的短说明", Required = false },
                ["jumpPointAction"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=jump_point：set/list/clear", Required = false, EnumValues = new List<string> { "set", "list", "clear" } },
                ["tool"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=select_tool：inspect/build/dig/cancel/sweep/mop/disinfect/harvest/deconstruct", Required = false },
                ["prefabId"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=select_tool tool=build：建筑 prefabId", Required = false },
                ["material"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=select_tool tool=build：材料 Tag；auto 自动选择", Required = false },
                ["facade"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=select_tool tool=build：外观 ID", Required = false },
                ["priority"] = new McpToolParameter { Type = "integer", Description = "domain=pointer action=select_tool：优先级 1-9", Required = false },
                ["message"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=say：短消息", Required = false },
                ["durationSeconds"] = new McpToolParameter { Type = "number", Description = "domain=pointer action=say：显示秒数", Required = false },
                ["code"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=jump/jump_point：跳转点代号；code=mouse 跳到玩家鼠标格", Required = false },
                ["label"] = new McpToolParameter { Type = "string", Description = "指针、跳转点或相机标签；按 action 解释", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按 action 解释", Required = false },
                ["x"] = new McpToolParameter { Type = "number", Description = "目标 X；camera 用世界/格子坐标，pointer 用格子或世界坐标", Required = false },
                ["y"] = new McpToolParameter { Type = "number", Description = "目标 Y；camera 用世界/格子坐标，pointer 用格子或世界坐标", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域左下 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域左下 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域右上 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域右上 Y", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=camera action=coordinate_screenshot：区域句柄", Required = false },
                ["direction"] = new McpToolParameter { Type = "string", Description = "domain=pointer action=nudge/hold_left/jump：right/left/up/down", Required = false, EnumValues = new List<string> { "right", "left", "up", "down" } },
                ["steps"] = new McpToolParameter { Type = "integer", Description = "domain=pointer action=nudge/jump：方向移动格数", Required = false },
                ["dx"] = new McpToolParameter { Type = "number", Description = "相对 X 偏移；camera move pan 或 pointer jump/nudge", Required = false },
                ["dy"] = new McpToolParameter { Type = "number", Description = "相对 Y 偏移；camera move pan 或 pointer jump/nudge", Required = false },
                ["length"] = new McpToolParameter { Type = "integer", Description = "domain=pointer action=hold_left：覆盖格数，包含起点", Required = false },
                ["allowFootprintDrag"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer action=hold_left：允许多格 footprint 拖拽", Required = false },
                ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer build：自动标记可挖阻挡", Required = false },
                ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "domain=pointer build：单次最多自动挖掘格", Required = false },
                ["moveCamera"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer action=jump：是否同步移动相机", Required = false },
                ["zoom"] = new McpToolParameter { Type = "number", Description = "相机缩放；按 action 解释", Required = false },
                ["snap"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=set_view/move：是否立即跳转", Required = false },
                ["mode"] = new McpToolParameter { Type = "string", Description = "domain=camera action=move：pan 或 jump", Required = false, EnumValues = new List<string> { "pan", "jump" } },
                ["duration"] = new McpToolParameter { Type = "number", Description = "domain=camera action=move：平滑移动秒数", Required = false },
                ["view"] = new McpToolParameter { Type = "string", Description = "domain=camera action=switch_view/coordinate_screenshot：覆盖层视图", Required = false },
                ["screenshot"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=switch_view：是否截图", Required = false },
                ["filename"] = new McpToolParameter { Type = "string", Description = "domain=camera：截图文件名", Required = false },
                ["waitFrames"] = new McpToolParameter { Type = "integer", Description = "domain=camera：截图前等待帧数", Required = false },
                ["allowSound"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=switch_view：是否播放切换音效", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=focus_dupe：复制人 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "domain=camera action=focus_dupe：复制人名称", Required = false },
                ["focusCamera"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=coordinate_screenshot：是否自动移动相机", Required = false },
                ["paddingCells"] = new McpToolParameter { Type = "number", Description = "domain=camera action=coordinate_screenshot：对焦留白格数", Required = false },
                ["showGrid"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=coordinate_screenshot：是否显示格线", Required = false },
                ["showCoordinates"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=coordinate_screenshot：是否显示坐标", Required = false },
                ["includeCellLabels"] = new McpToolParameter { Type = "boolean", Description = "domain=camera action=coordinate_screenshot：是否标注格心坐标", Required = false },
                ["step"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：坐标标签步长", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer left_click/hold_left 执行修改必须 true；dryRun=true 可省略", Required = false },
                ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer left_click/hold_left：仅预检", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "domain=pointer action=say：清除气泡；action=clear 删除指针", Required = false }
            };
        }
    }
}
