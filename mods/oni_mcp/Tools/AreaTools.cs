using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class AreaTools
    {
        public static McpTool DefineArea()
        {
            return new McpTool
            {
                Name = "area_define",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "area_save", "rect_save" },
                Description = "把一个矩形区域保存为短 areaId，后续工具可用 areaId 代替 x1/y1/x2/y2 以节省 token",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选短标签，例如 oxygen_room_candidate", Required = false }
                }),
                Handler = args =>
                {
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId ?? 0;
                    var handle = AreaHandleRegistry.Define(ToolUtil.GetRect(args), worldId, args["label"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(handle.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetArea()
        {
            return new McpTool
            {
                Name = "area_get",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "area_rect" },
                Description = "查询 areaId 对应的区域坐标和世界 ID",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，例如 a1", Required = true }
                },
                Handler = args =>
                {
                    AreaHandle handle;
                    if (!AreaHandleRegistry.TryGet(args["areaId"]?.ToString(), out handle))
                        return CallToolResult.Error("Unknown areaId");

                    return CallToolResult.Text(JsonConvert.SerializeObject(handle.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListAreas()
        {
            return new McpTool
            {
                Name = "area_list",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "areas" },
                Description = "列出当前会话保存的 areaId，最近使用的排在前面",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 20, 100));
                    var areas = AreaHandleRegistry.List()
                        .Take(limit)
                        .Select(area => area.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = areas.Count,
                        ["areas"] = areas
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ForgetArea()
        {
            return new McpTool
            {
                Name = "area_forget",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Description = "删除不再需要的 areaId",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，例如 a1", Required = true }
                },
                Handler = args =>
                {
                    bool removed = AreaHandleRegistry.Remove(args["areaId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["areaId"] = args["areaId"]?.ToString()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X", Required = true },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y", Required = true },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X", Required = true },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y", Required = true }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }
    }
}
