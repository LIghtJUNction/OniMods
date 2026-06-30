using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class VisualControlTools
    {
        public static McpTool ControlVisual()
        {
            return new McpTool
            {
                Name = "visual_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "building_visual_control", "color_control" },
                Tags = new List<string> { "buildings", "lights", "pixel-pack", "colors", "visual" },
                Description = "建筑视觉/颜色统一入口。kind=light/pixel_pack；action 透传到对应旧 control。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "视觉控件类型：light 或 pixel_pack", Required = true, EnumValues = new List<string> { "light", "pixel_pack" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "light 支持 list/set_color；pixel_pack 支持 list/set_color/copy_colors", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId 或颜色名筛选", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "action=list 时区域句柄；与 x1/y1/x2/y2 二选一", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；list 时可筛选，按坐标查找建议提供", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量", Required = false },
                    ["configurableOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=light action=list 时是否只返回带 LightColorMenu 的灯，默认 true", Required = false },
                    ["includePresets"] = new McpToolParameter { Type = "boolean", Description = "kind=pixel_pack action=list 时是否返回颜色预设，默认 false", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "写操作目标 KPrefabID.InstanceID；推荐", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "写操作目标格子 X；未传 id 时使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "写操作目标格子 Y；未传 id 时使用", Required = false },
                    ["colorIndex"] = new McpToolParameter { Type = "integer", Description = "目标颜色预设索引", Required = false },
                    ["colorName"] = new McpToolParameter { Type = "string", Description = "目标颜色预设名称；colorIndex 为空时使用", Required = false },
                    ["panel"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=set_color/copy_colors 时目标面板", Required = false },
                    ["state"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=set_color/copy_colors 时 active 或 standby", Required = false },
                    ["sourcePanel"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=copy_colors 时源面板", Required = false },
                    ["sourceState"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=copy_colors 时源状态", Required = false },
                    ["targetPanel"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=copy_colors 时目标面板", Required = false },
                    ["targetState"] = new McpToolParameter { Type = "string", Description = "kind=pixel_pack action=copy_colors 时目标状态", Required = false }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "light":
                        case "lights":
                            return LightTools.ControlLight().Handler(args);
                        case "pixel_pack":
                        case "pixelpack":
                            return PixelPackTools.ControlPixelPack().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be light or pixel_pack");
                    }
                }
            };
        }
    }
}
