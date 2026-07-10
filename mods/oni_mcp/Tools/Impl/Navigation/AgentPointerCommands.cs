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
        private const float DisplayTextDurationSeconds = 8f;
        private const string AgentIdDescription = "可选逻辑指针名；建议首次指针操作就选一个短而稳定的 agentId（如 planner、builder），并在后续所有 navigation_control pointer actions 调用中持续传入，让模型记住并复用同一个可视指针。省略时使用全局默认 agent 指针；不同 agentId 可并行显示多个指针。";
        private const string DisplayTextDescription = "可选显示文本，会立刻在指针旁短暂显示。建议在移动、选工具、点击、拖拽等可见动作中传入 6-40 字给玩家看的状态说明，例如“准备铺线”“标记挖掘”。";

        public static McpTool Control()
        {
            return new McpTool
            {
                Name = "agent_pointer_control",
                Group = "camera",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "pointer_control", "agent_pointer" },
                Tags = new List<string> { "pointer", "mouse", "click", "drag", "jump", "control" },
                Description = "统一 agent 可视指针入口。action=get/user_mouse/aim_cell/aim_world/nudge/select_tool/say/left_click/hold_left/jump/jump_point/clear；jump_point 子操作用 jumpPointAction=set/list/clear。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "指针操作：get、user_mouse、aim_cell、aim_world、nudge、select_tool、say、left_click、hold_left、jump、jump_point、clear", Required = true, EnumValues = new List<string> { "get", "user_mouse", "aim_cell", "aim_world", "nudge", "select_tool", "say", "left_click", "hold_left", "jump", "jump_point", "clear" } },
                    ["jumpPointAction"] = new McpToolParameter { Type = "string", Description = "action=jump_point 时必填：set、list、clear。转发到底层兼容入口时会映射为 action", Required = false, EnumValues = new List<string> { "set", "list", "clear" } },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false },
                    ["x"] = new McpToolParameter { Type = "number", Description = "aim_cell/jump/jump_point 使用格子 X；aim_world 使用世界 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "number", Description = "aim_cell/jump/jump_point 使用格子 Y；aim_world 使用世界 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界或当前指针世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针或跳转点标签", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "nudge/hold_left/jump 的方向：right、left、up、down", Required = false, EnumValues = new List<string> { "right", "left", "up", "down" } },
                    ["steps"] = new McpToolParameter { Type = "integer", Description = "nudge/jump 按方向移动的格数，默认 1", Required = false },
                    ["dx"] = new McpToolParameter { Type = "integer", Description = "jump/nudge 的相对 X 偏移", Required = false },
                    ["dy"] = new McpToolParameter { Type = "integer", Description = "jump/nudge 的相对 Y 偏移", Required = false },
                    ["tool"] = new McpToolParameter { Type = "string", Description = "select_tool 的工具类型：inspect、build、dig、cancel、sweep、mop、disinfect、harvest、deconstruct", Required = false, EnumValues = new List<string> { "inspect", "build", "dig", "cancel", "sweep", "mop", "disinfect", "harvest", "deconstruct" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "select_tool tool=build 时的建筑 prefabId", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "select_tool tool=build 时的材料 Tag；默认/auto 自动选择", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "select_tool tool=build 时的外观 ID", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "select_tool 的优先级 1-9，默认 5", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "say 要显示的短消息，最长 160 字符；留空或 clear=true 清除气泡", Required = false },
                    ["durationSeconds"] = new McpToolParameter { Type = "number", Description = "say 显示秒数，默认 8，范围 1-60", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "say clear=true 清除气泡；action=clear 则删除指针", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "left_click/hold_left 执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "left_click/hold_left 仅预检，传给支持 dryRun 的子工具", Required = false },
                    ["length"] = new McpToolParameter { Type = "integer", Description = "hold_left 覆盖格数，包含起点", Required = false },
                    ["allowFootprintDrag"] = new McpToolParameter { Type = "boolean", Description = "hold_left 默认 false；多格 footprint 拖拽需显式设为 true", Required = false },
                    ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "build 工具默认 true；遇到可挖自然固体时先自动标记挖掘", Required = false },
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "build 工具单次最多自动标记多少个挖掘格，默认 100，最大 500", Required = false },
                    ["code"] = new McpToolParameter { Type = "string", Description = "jump/jump_point 的跳转点代号，如 p1、p2；code=mouse 可跳到玩家鼠标格", Required = false },
                    ["moveCamera"] = new McpToolParameter { Type = "boolean", Description = "jump 是否同时移动相机，默认 false", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "jump moveCamera=true 时的相机缩放，默认保持当前缩放", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(action))
                        return CallToolResult.Error("action is required");

                    if (action == "jump_point")
                    {
                        var childArgs = CloneWithoutControlAction(args);
                        string jumpPointAction = (args["jumpPointAction"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(jumpPointAction))
                            return CallToolResult.Error("jumpPointAction is required when action=jump_point");
                        childArgs["action"] = jumpPointAction;
                        childArgs.Remove("jumpPointAction");
                        return ControlJumpPoint().Handler(childArgs);
                    }

                    var forwarded = CloneWithoutControlAction(args);
                    switch (action)
                    {
                        case "get": return GetPointerState().Handler(forwarded);
                        case "user_mouse": return GetUserMouse().Handler(forwarded);
                        case "aim_cell": return AimCell().Handler(forwarded);
                        case "aim_world": return AimWorld().Handler(forwarded);
                        case "nudge": return Nudge().Handler(forwarded);
                        case "select_tool": return SelectTool().Handler(forwarded);
                        case "say": return Say().Handler(forwarded);
                        case "left_click": return LeftClick().Handler(forwarded);
                        case "hold_left": return HoldLeft().Handler(forwarded);
                        case "jump": return Jump().Handler(forwarded);
                        case "clear": return ClearPointer().Handler(forwarded);
                        default:
                            return CallToolResult.Error("action must be one of get, user_mouse, aim_cell, aim_world, nudge, select_tool, say, left_click, hold_left, jump, jump_point, clear");
                    }
                }
            };
        }

        public static McpTool GetPointerState()
        {
            return new McpTool
            {
                Name = "agent_pointer_get",
                Group = "camera",
                Mode = "read",
                Risk = "none",
                Description = "读取或创建当前 agent 的可视指针状态；适合作为多步操作前的第一步，用稳定 agentId 建立并记住后续要复用的指针",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false }
                },
                Handler = args =>
                {
                    string sessionId = ToolSessionContext.SessionId;
                    var pointer = AgentPointerRegistry.GetOrCreate(sessionId, args["agentId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetUserMouse()
        {
            return new McpTool
            {
                Name = "agent_pointer_user_mouse_get",
                Group = "camera",
                Mode = "read",
                Risk = "none",
                Description = "读取玩家当前鼠标所在屏幕位置、世界坐标和格子；可配合 navigation_control action=jump code=mouse 让 agent 指针跳到玩家鼠标处",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "可选世界 ID；默认当前激活世界，仅用于校验 cell 是否属于该世界", Required = false }
                },
                Handler = args =>
                {
                    if (!TryGetUserMouseCell(ToolUtil.GetInt(args, "worldId"), out var payload, out string error))
                        return CallToolResult.Error(error);
                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AimCell()
        {
            return new McpTool
            {
                Name = "agent_pointer_aim_cell",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "把可视 agent 指针对准一个格子中心，后续所有动作都围绕这个指针进行；多步操作请传稳定 agentId，并用 displayText 告诉玩家你正在指向哪里",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int cell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Target cell is outside the grid");
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Target cell is not in worldId={worldId}");
                    var pointer = AgentPointerRegistry.SetCell(
                        ToolSessionContext.SessionId,
                        args["agentId"]?.ToString(),
                        worldId,
                        x.Value,
                        y.Value,
                        args["label"]?.ToString());
                    ApplyDisplayText(args, args["agentId"]?.ToString());

                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AimWorld()
        {
            return new McpTool
            {
                Name = "agent_pointer_aim_world",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "把可视 agent 指针对准一个世界坐标；多步操作请传稳定 agentId，并用 displayText 给玩家一个短提示",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "number", Description = "世界 X 坐标", Required = true },
                    ["y"] = new McpToolParameter { Type = "number", Description = "世界 Y 坐标", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    float? x = ToolUtil.GetFloat(args, "x");
                    float? y = ToolUtil.GetFloat(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var worldPos = new Vector3(x.Value, y.Value, -100f);
                    var pointer = AgentPointerRegistry.SetWorldPosition(
                        ToolSessionContext.SessionId,
                        args["agentId"]?.ToString(),
                        worldId,
                        worldPos,
                        args["label"]?.ToString());
                    ApplyDisplayText(args, args["agentId"]?.ToString());

                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool Nudge()
        {
            return new McpTool
            {
                Name = "agent_pointer_nudge",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "按相对方向移动当前 agent 指针，适合像鼠标一样逐格微调；继续传同一个 agentId，并用 displayText 说明微调意图",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["direction"] = new McpToolParameter { Type = "string", Description = "方向：right、left、up、down；也可省略并直接传 dx/dy", Required = false, EnumValues = new List<string> { "right", "left", "up", "down" } },
                    ["steps"] = new McpToolParameter { Type = "integer", Description = "移动格数，默认 1", Required = false },
                    ["dx"] = new McpToolParameter { Type = "integer", Description = "相对 X 偏移；direction 为空时使用", Required = false },
                    ["dy"] = new McpToolParameter { Type = "integer", Description = "相对 Y 偏移；direction 为空时使用", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = AgentIdDescription, Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false },
                    ["displayText"] = new McpToolParameter { Type = "string", Description = DisplayTextDescription, Required = false }
                },
                Handler = args =>
                {
                    string agentId = args["agentId"]?.ToString();
                    var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
                    if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                        return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

                    Grid.CellToXY(pointer.Cell, out int x, out int y);
                    int steps = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "steps") ?? 1, 100));
                    int dx = ToolUtil.GetInt(args, "dx") ?? 0;
                    int dy = ToolUtil.GetInt(args, "dy") ?? 0;
                    string direction = args["direction"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(direction))
                    {
                        if (!TryDirection(direction, out dx, out dy))
                            return CallToolResult.Error("direction must be right, left, up or down");
                        dx *= steps;
                        dy *= steps;
                    }
                    else if (dx == 0 && dy == 0)
                    {
                        return CallToolResult.Error("direction or dx/dy is required");
                    }

                    int targetX = x + dx;
                    int targetY = y + dy;
                    int targetCell = Grid.XYToCell(targetX, targetY);
                    if (!Grid.IsValidCell(targetCell))
                        return CallToolResult.Error("Target cell is outside the grid");
                    if (pointer.WorldId >= 0 && !ToolUtil.CellMatchesWorld(targetCell, pointer.WorldId))
                        return CallToolResult.Error($"Target cell is not in worldId={pointer.WorldId}");

                    var moved = AgentPointerRegistry.SetCell(
                        ToolSessionContext.SessionId,
                        agentId,
                        pointer.WorldId,
                        targetX,
                        targetY,
                        args["label"]?.ToString());
                    ApplyDisplayText(args, agentId);
                    return CallToolResult.Text(JsonConvert.SerializeObject(moved.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

    }
}
