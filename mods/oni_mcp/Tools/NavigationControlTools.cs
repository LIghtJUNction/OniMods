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
                Aliases = new List<string> { "spatial_control", "view_control" },
                Tags = new List<string> { "camera", "view", "screenshot", "overlay" },
                Description = "相机与视图聚合入口：domain=camera；处理相机移动、世界切换、覆盖层和截图。不传 domain 时按已知相机 action 自动判断。",
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
                            return CallToolResult.Error("agent pointer control has been removed. Use regular semantic tools with the required task text; the task text is shown near the player's mouse automatically.");
                        default:
                            return CallToolResult.Error("domain must be camera and action must be get_view, set_active_world, set_view, move, switch_view, focus_cell, focus_dupe, screenshot, or coordinate_screenshot");
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
                    return string.Empty;
            }
        }

        private static Dictionary<string, McpToolParameter> Params()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["domain"] = new McpToolParameter { Type = "string", Description = "相机域；省略时按已知相机 action 自动判断", Required = false, EnumValues = new List<string> { "camera" } },
                ["action"] = new McpToolParameter { Type = "string", Description = "相机动作：get_view、set_active_world、set_view、move、switch_view、focus_cell、focus_dupe、screenshot、coordinate_screenshot", Required = true, EnumValues = new List<string> { "get_view", "set_active_world", "set_view", "move", "switch_view", "focus_cell", "focus_dupe", "screenshot", "coordinate_screenshot" } },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；set_active_world 必填，其他 action 默认当前激活世界", Required = false },
                ["requireDiscovered"] = new McpToolParameter { Type = "boolean", Description = "set_active_world：是否要求目标世界已被发现，默认 true", Required = false },
                ["lookAtSurface"] = new McpToolParameter { Type = "boolean", Description = "set_active_world：世界未被访问时是否 LookAtSurface，默认 true", Required = false },
                ["x"] = new McpToolParameter { Type = "number", Description = "set_view/move jump 的目标 X，或 focus_cell 的格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "number", Description = "set_view/move jump 的目标 Y，或 focus_cell 的格子 Y", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域左下 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域左下 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域右上 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：区域右上 Y", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=camera action=coordinate_screenshot：区域句柄", Required = false },
                ["dx"] = new McpToolParameter { Type = "number", Description = "move pan：X 方向偏移，默认 0", Required = false },
                ["dy"] = new McpToolParameter { Type = "number", Description = "move pan：Y 方向偏移，默认 0", Required = false },
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
                ["step"] = new McpToolParameter { Type = "integer", Description = "domain=camera action=coordinate_screenshot：坐标标签步长", Required = false }
            };
        }
    }
}
