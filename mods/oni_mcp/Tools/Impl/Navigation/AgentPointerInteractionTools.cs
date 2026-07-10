using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class AgentPointerTools
    {
        public static McpTool SelectTool()
        {
            return new McpTool
            {
                Name = "agent_pointer_select_tool",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "切换当前 agent 指针选中的工具；build 工具可同时选择建筑蓝图、材料、外观和优先级，并会在鼠标旁可视化显示。继续传同一个 agentId，并用 displayText 告诉玩家已切到什么工具",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["tool"] = new McpToolParameter { Type = "string", Description = "工具类型：inspect、build、dig、cancel、sweep、mop、disinfect、harvest、deconstruct", Required = true, EnumValues = new List<string> { "inspect", "build", "dig", "cancel", "sweep", "mop", "disinfect", "harvest", "deconstruct" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "tool=build 时的建筑 prefabId，例如 Wire、Tile、Ladder", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "tool=build 时的材料 Tag；默认/auto 自动选择", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "tool=build 时的外观 ID", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9，默认 5", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    string tool = args["tool"]?.ToString();
                    if (string.IsNullOrWhiteSpace(tool))
                        return CallToolResult.Error("tool is required");
                    string normalized = NormalizeActionTool(tool);
                    if (normalized == "build" && string.IsNullOrWhiteSpace(args["prefabId"]?.ToString()))
                        return CallToolResult.Error("prefabId is required when tool=build");

                    var pointer = AgentPointerRegistry.SelectTool(
                        ToolSessionContext.SessionId,
                        args["agentId"]?.ToString(),
                        normalized,
                        args["prefabId"]?.ToString(),
                        args["material"]?.ToString(),
                        args["facade"]?.ToString(),
                        ToolUtil.GetInt(args, "priority") ?? 5);
                    ApplyDisplayText(args, args["agentId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool Say()
        {
            return new McpTool
            {
                Name = "agent_pointer_say",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "在当前 agent 指针旁显示一条聊天气泡消息；只影响画面提示，不执行游戏操作。适合在执行前解释计划、等待玩家确认或标注风险",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["message"] = new McpToolParameter { Type = "string", Description = "要显示的短消息，最长 160 字符；留空或 clear=true 会清除气泡", Required = false },
                    ["durationSeconds"] = new McpToolParameter { Type = "number", Description = "显示秒数，默认 8，范围 1-60", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "true=立即清除当前 agent 的气泡", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false }
                },
                Handler = args =>
                {
                    string agentId = args["agentId"]?.ToString();
                    var current = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
                    if (current == null || !Grid.IsValidCell(current.Cell))
                        return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

                    if (ToolUtil.GetBool(args, "clear", false))
                    {
                        var cleared = AgentPointerRegistry.ClearMessage(ToolSessionContext.SessionId, agentId);
                        return CallToolResult.Text(JsonConvert.SerializeObject(cleared.ToDictionary(), McpJsonUtil.Settings));
                    }

                    string message = args["message"]?.ToString();
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        var cleared = AgentPointerRegistry.ClearMessage(ToolSessionContext.SessionId, agentId);
                        return CallToolResult.Text(JsonConvert.SerializeObject(cleared.ToDictionary(), McpJsonUtil.Settings));
                    }

                    float duration = ToolUtil.GetFloat(args, "durationSeconds") ?? 8f;
                    var pointer = AgentPointerRegistry.SetMessage(ToolSessionContext.SessionId, agentId, message, duration);
                    ApplyDisplayText(args, agentId);
                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool LeftClick()
        {
            return new McpTool
            {
                Name = "agent_pointer_left_click",
                Group = "camera",
                Mode = "execute",
                Risk = "medium",
                Description = "在当前指针格子执行一次左键确认，按当前选中工具触发建造/挖掘/取消/清扫等操作；执行时继续传同一个 agentId，并尽量传 displayText 让玩家看到这次点击的目的",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，传给支持 dryRun 的子工具", Required = false },
                    ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "build 工具默认 true。建造 footprint 遇到可挖自然固体时，先自动标记挖掘，并继续尝试在同一格放置建造蓝图", Required = false },
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "build 工具单次最多自动标记多少个挖掘格，默认 100，最大 500", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false) && !ToolUtil.GetBool(args, "dryRun", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");
                    var pointer = RequirePointer(args["agentId"]?.ToString());
                    if (pointer.Error != null)
                        return CallToolResult.Error(pointer.Error);

                    Grid.CellToXY(pointer.State.Cell, out int x, out int y);
                    var result = ExecuteSelectedTool(pointer.State, x, y, x, y, args, isDrag: false);
                    ApplyDisplayText(args, args["agentId"]?.ToString());
                    pointer.State.LastAction = "left_click";
                    pointer.State.UpdatedAt = System.DateTime.UtcNow;
                    return WrapActionResult(pointer.State, result);
                }
            };
        }

        public static McpTool HoldLeft()
        {
            return new McpTool
            {
                Name = "agent_pointer_hold_left",
                Group = "camera",
                Mode = "execute",
                Risk = "medium",
                Description = "模拟按住左键向上下左右拖拽若干格，并按当前选中工具执行直线操作；执行时继续传同一个 agentId，并尽量传 displayText 说明拖拽范围/目的",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["direction"] = new McpToolParameter { Type = "string", Description = "方向：right、left、up、down", Required = true, EnumValues = new List<string> { "right", "left", "up", "down" } },
                    ["length"] = new McpToolParameter { Type = "integer", Description = "覆盖格数，包含起点；例如 5 表示 5 格", Required = true },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，传给支持 dryRun 的子工具", Required = false },
                    ["allowFootprintDrag"] = new McpToolParameter { Type = "boolean", Description = "默认 false。拖拽建造只允许 1x1 footprint；床、厕所、机器等多格建筑需逐个 left_click，除非显式设为 true", Required = false },
                    ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "build 工具默认 true。建造 footprint 遇到可挖自然固体时，先自动标记挖掘，并继续尝试在同一格放置建造蓝图", Required = false },
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "build 工具单次最多自动标记多少个挖掘格，默认 100，最大 500", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false) && !ToolUtil.GetBool(args, "dryRun", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");
                    var pointer = RequirePointer(args["agentId"]?.ToString());
                    if (pointer.Error != null)
                        return CallToolResult.Error(pointer.Error);
                    int? requestedLength = ToolUtil.GetInt(args, "length");
                    if (!requestedLength.HasValue || requestedLength.Value <= 0)
                        return CallToolResult.Error("length must be a positive integer");
                    int length = Math.Max(1, Math.Min(requestedLength.Value, 200));
                    if (!TryDirection(args["direction"]?.ToString(), out int dx, out int dy))
                        return CallToolResult.Error("direction must be right, left, up or down");

                    Grid.CellToXY(pointer.State.Cell, out int x, out int y);
                    int endX = x + dx * (length - 1);
                    int endY = y + dy * (length - 1);
                    int endCell = Grid.XYToCell(endX, endY);
                    if (!Grid.IsValidCell(endCell))
                        return CallToolResult.Error("Drag end cell is outside the grid");
                    if (pointer.State.WorldId >= 0 && !ToolUtil.CellMatchesWorld(endCell, pointer.State.WorldId))
                        return CallToolResult.Error($"Drag end cell is not in worldId={pointer.State.WorldId}");

                    AgentPointerRegistry.BeginDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), pointer.State.WorldId, pointer.State.Cell, pointer.State.CurrentTool);
                    var result = ExecuteSelectedTool(pointer.State, x, y, endX, endY, args, isDrag: true);
                    var finalPointer = AgentPointerRegistry.EndDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), endCell);
                    ApplyDisplayText(args, args["agentId"]?.ToString());
                    return WrapActionResult(finalPointer, result);
                }
            };
        }

        public static McpTool Jump()
        {
            return new McpTool
            {
                Name = "agent_pointer_jump",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "跳转 agent 指针，不默认移动相机。支持绝对 x/y、相对 dx/dy、方向 steps，或跳转到 p1/p2 等标点；多步操作请传稳定 agentId，并用 displayText 说明跳转目标",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["code"] = new McpToolParameter { Type = "string", Description = "跳转点代号，如 p1、p2；提供时优先使用", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "绝对目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "绝对目标 Y", Required = false },
                    ["dx"] = new McpToolParameter { Type = "integer", Description = "相对 X 偏移", Required = false },
                    ["dy"] = new McpToolParameter { Type = "integer", Description = "相对 Y 偏移", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "相对方向：right、left、up、down", Required = false },
                    ["steps"] = new McpToolParameter { Type = "integer", Description = "direction 的移动格数，默认 1", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前或指针世界", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["moveCamera"] = new McpToolParameter { Type = "boolean", Description = "是否同时把相机移动到指针，默认 false", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "moveCamera=true 时的相机缩放，默认保持当前缩放", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    return JumpPointer(args);
                }
            };
        }

        public static McpTool SetJumpPoint()
        {
            return new McpTool
            {
                Name = "agent_pointer_jump_point_set",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 navigation_control action=jump_point jumpPointAction=set。设置 AI 跳转点，代号为 p1、p2、p+数字；未给 x/y 时保存当前指针位置。建议配合同一个 agentId 记住常用施工/观察点",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["code"] = new McpToolParameter { Type = "string", Description = "跳转点代号，如 p1、p2；p 等价 p1", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "可选绝对 X；留空使用当前指针", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "可选绝对 Y；留空使用当前指针", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "可选世界 ID；默认当前或指针世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选标签", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId ?? 0;
                    int cell;
                    if (x.HasValue && y.HasValue)
                    {
                        cell = Grid.XYToCell(x.Value, y.Value);
                    }
                    else
                    {
                        var pointer = RequirePointer(args["agentId"]?.ToString());
                        if (pointer.Error != null)
                            return CallToolResult.Error(pointer.Error);
                        cell = pointer.State.Cell;
                        worldId = pointer.State.WorldId >= 0 ? pointer.State.WorldId : worldId;
                    }
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Jump point cell is outside the grid");
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Jump point cell is not in worldId={worldId}");

                    var point = AgentPointerRegistry.SetJumpPoint(ToolSessionContext.SessionId, args["agentId"]?.ToString(), args["code"]?.ToString(), worldId, cell, args["label"]?.ToString());
                    ApplyDisplayText(args, args["agentId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(point.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListJumpPoints()
        {
            return new McpTool
            {
                Name = "agent_pointer_jump_point_list",
                Group = "camera",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "兼容入口：请优先使用 navigation_control action=jump_point jumpPointAction=list。列出当前 agent 的 AI 跳转点",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false }
                },
                Handler = args =>
                {
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["points"] = AgentPointerRegistry.ListJumpPoints(ToolSessionContext.SessionId, args["agentId"]?.ToString())
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearJumpPoint()
        {
            return new McpTool
            {
                Name = "agent_pointer_jump_point_clear",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 navigation_control action=jump_point jumpPointAction=clear。取消指定 AI 跳转点",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["code"] = new McpToolParameter { Type = "string", Description = "跳转点代号，如 p1、p2", Required = true },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false }
                },
                Handler = args =>
                {
                    bool removed = AgentPointerRegistry.ClearJumpPoint(ToolSessionContext.SessionId, args["agentId"]?.ToString(), args["code"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["code"] = AgentPointerRegistry.NormalizeJumpCode(args["code"]?.ToString())
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlJumpPoint()
        {
            return new McpTool
            {
                Name = "agent_pointer_jump_point_control",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "jump_point_control", "agent_jump_point_control" },
                Tags = new List<string> { "pointer", "jump", "bookmark", "camera", "control" },
                Description = "统一管理 agent 指针跳转点。action=set/list/clear；set 保存 p1/p2 等位置，list 查看当前 agent 的跳转点，clear 删除指定代号。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：set、list、clear", Required = true },
                    ["code"] = new McpToolParameter { Type = "string", Description = "action=set/clear 时跳转点代号，如 p1、p2；p 等价 p1", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时可选绝对 X；留空使用当前指针", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时可选绝对 Y；留空使用当前指针", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=set 时可选世界 ID；默认当前或指针世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "action=set 时可选标签", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = "action=set 时可选，在指针旁显示当前意图", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "set")
                        return SetJumpPoint().Handler(args);
                    if (action == "list")
                        return ListJumpPoints().Handler(args);
                    if (action == "clear")
                        return ClearJumpPoint().Handler(args);
                    return CallToolResult.Error("action must be one of set, list, clear");
                }
            };
        }

        public static McpTool ClearPointer()
        {
            return new McpTool
            {
                Name = "agent_pointer_clear",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "删除指定 agent 指针及其 AI 跳转点；省略 agentId 时删除全局默认 agent 指针",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false }
                },
                Handler = args =>
                {
                    string agentId = args["agentId"]?.ToString();
                    bool jumpPointsRemoved;
                    bool removed = AgentPointerRegistry.Remove(ToolSessionContext.SessionId, agentId, out jumpPointsRemoved);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["scope"] = "agent",
                        ["sessionId"] = ToolSessionContext.SessionId,
                        ["agentId"] = AgentPointerRegistry.PublicAgentId(agentId),
                        ["jumpPointsCleared"] = jumpPointsRemoved
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
