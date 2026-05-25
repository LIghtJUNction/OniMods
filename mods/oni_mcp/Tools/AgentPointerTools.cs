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
    internal sealed class AgentPointerState
    {
        public string SessionId { get; set; }
        public string AgentId { get; set; }
        public bool Visible { get; set; } = true;
        public string Label { get; set; } = "agent";
        public Color Color { get; set; } = new Color(0.2f, 0.95f, 1f, 1f);
        public int WorldId { get; set; } = -1;
        public int Cell { get; set; } = -1;
        public Vector3 WorldPosition { get; set; }
        public Vector2 ScreenPosition { get; set; }
        public bool IsDragging { get; set; }
        public int DragStartCell { get; set; } = -1;
        public int DragCurrentCell { get; set; } = -1;
        public string DragMode { get; set; }
        public string DragTool { get; set; }
        public string CurrentTool { get; set; } = "inspect";
        public string ToolLabel { get; set; } = "Inspect";
        public string ToolIcon { get; set; } = "inspect";
        public string BuildPrefabId { get; set; }
        public string BuildMaterial { get; set; }
        public string BuildFacade { get; set; }
        public int Priority { get; set; } = 5;
        public string LastAction { get; set; }
        public System.DateTime UpdatedAt { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["sessionId"] = SessionId,
                ["agentId"] = AgentId,
                ["visible"] = Visible,
                ["label"] = Label,
                ["color"] = new { r = Math.Round(Color.r, 3), g = Math.Round(Color.g, 3), b = Math.Round(Color.b, 3), a = Math.Round(Color.a, 3) },
                ["worldId"] = WorldId,
                ["cell"] = Cell,
                ["worldPosition"] = new { x = Math.Round(WorldPosition.x, 2), y = Math.Round(WorldPosition.y, 2), z = Math.Round(WorldPosition.z, 2) },
                ["screenPosition"] = new { x = Math.Round(ScreenPosition.x, 2), y = Math.Round(ScreenPosition.y, 2) },
                ["dragging"] = IsDragging,
                ["dragStartCell"] = DragStartCell,
                ["dragCurrentCell"] = DragCurrentCell,
                ["dragMode"] = DragMode,
                ["dragTool"] = DragTool,
                ["currentTool"] = CurrentTool,
                ["toolLabel"] = ToolLabel,
                ["toolIcon"] = ToolIcon,
                ["buildPrefabId"] = BuildPrefabId,
                ["buildMaterial"] = BuildMaterial,
                ["buildFacade"] = BuildFacade,
                ["priority"] = Priority,
                ["lastAction"] = LastAction,
                ["updatedAt"] = UpdatedAt.ToString("o")
            };
        }
    }

    internal sealed class AgentPointerJumpPoint
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public int WorldId { get; set; }
        public int Cell { get; set; }
        public System.DateTime UpdatedAt { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            int x = -1;
            int y = -1;
            if (Grid.IsValidCell(Cell))
                Grid.CellToXY(Cell, out x, out y);
            return new Dictionary<string, object>
            {
                ["code"] = Code,
                ["label"] = Label,
                ["worldId"] = WorldId,
                ["cell"] = Cell,
                ["x"] = x,
                ["y"] = y,
                ["updatedAt"] = UpdatedAt.ToString("o")
            };
        }
    }

    internal static class AgentPointerRegistry
    {
        private const string DefaultSessionId = "global";
        private const string DefaultAgentId = "agent";
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, AgentPointerState> SessionPointers = new Dictionary<string, AgentPointerState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, AgentPointerJumpPoint>> JumpPoints = new Dictionary<string, Dictionary<string, AgentPointerJumpPoint>>(StringComparer.Ordinal);
        private static readonly Color[] Palette =
        {
            new Color(0.16f, 0.92f, 1f, 1f),
            new Color(1f, 0.62f, 0.16f, 1f),
            new Color(0.54f, 1f, 0.25f, 1f),
            new Color(1f, 0.32f, 0.72f, 1f),
            new Color(0.72f, 0.55f, 1f, 1f),
            new Color(1f, 0.9f, 0.18f, 1f),
            new Color(0.28f, 0.7f, 1f, 1f),
            new Color(1f, 0.24f, 0.24f, 1f)
        };
        public static AgentPointerState GetOrCreate(string sessionId, string agentId = null)
        {
            string key = ResolveKey(sessionId, agentId);
            string normalizedSessionId = NormalizeSessionId(sessionId);
            string normalizedAgentId = NormalizeAgentId(agentId);
            lock (Lock)
            {
                AgentPointerState pointer;
                if (SessionPointers.TryGetValue(key, out pointer))
                {
                    pointer.SessionId = normalizedSessionId;
                    return pointer;
                }

                pointer = new AgentPointerState
                {
                    SessionId = normalizedSessionId,
                    AgentId = normalizedAgentId,
                    Label = normalizedAgentId,
                    Color = Palette[SessionPointers.Count % Palette.Length],
                    UpdatedAt = System.DateTime.UtcNow
                };
                SessionPointers[key] = pointer;
                return pointer;
            }
        }

        public static AgentPointerState SelectTool(string sessionId, string agentId, string tool, string prefabId, string material, string facade, int priority)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.CurrentTool = NormalizeTool(tool);
            pointer.ToolLabel = ToolLabel(pointer.CurrentTool, prefabId);
            pointer.ToolIcon = ToolIcon(pointer.CurrentTool, prefabId);
            pointer.BuildPrefabId = pointer.CurrentTool == "build" ? prefabId : null;
            pointer.BuildMaterial = pointer.CurrentTool == "build" ? material : null;
            pointer.BuildFacade = pointer.CurrentTool == "build" ? facade : null;
            pointer.Priority = Math.Max(1, Math.Min(priority, 9));
            pointer.LastAction = "select_tool";
            pointer.UpdatedAt = System.DateTime.UtcNow;
            return pointer;
        }

        public static AgentPointerJumpPoint SetJumpPoint(string sessionId, string agentId, string code, int worldId, int cell, string label)
        {
            string key = ResolveKey(sessionId, agentId);
            code = NormalizeJumpCode(code);
            lock (Lock)
            {
                Dictionary<string, AgentPointerJumpPoint> points;
                if (!JumpPoints.TryGetValue(key, out points))
                {
                    points = new Dictionary<string, AgentPointerJumpPoint>(StringComparer.OrdinalIgnoreCase);
                    JumpPoints[key] = points;
                }

                var point = new AgentPointerJumpPoint
                {
                    Code = code,
                    Label = string.IsNullOrWhiteSpace(label) ? code : label.Trim(),
                    WorldId = worldId,
                    Cell = cell,
                    UpdatedAt = System.DateTime.UtcNow
                };
                points[code] = point;
                return point;
            }
        }

        public static bool TryGetJumpPoint(string sessionId, string agentId, string code, out AgentPointerJumpPoint point)
        {
            point = null;
            string key = ResolveKey(sessionId, agentId);
            code = NormalizeJumpCode(code);
            lock (Lock)
            {
                Dictionary<string, AgentPointerJumpPoint> points;
                return JumpPoints.TryGetValue(key, out points) && points.TryGetValue(code, out point);
            }
        }

        public static bool ClearJumpPoint(string sessionId, string agentId, string code)
        {
            string key = ResolveKey(sessionId, agentId);
            code = NormalizeJumpCode(code);
            lock (Lock)
            {
                Dictionary<string, AgentPointerJumpPoint> points;
                return JumpPoints.TryGetValue(key, out points) && points.Remove(code);
            }
        }

        public static List<Dictionary<string, object>> ListJumpPoints(string sessionId, string agentId)
        {
            string key = ResolveKey(sessionId, agentId);
            lock (Lock)
            {
                Dictionary<string, AgentPointerJumpPoint> points;
                var result = new List<Dictionary<string, object>>();
                if (!JumpPoints.TryGetValue(key, out points))
                    return result;
                foreach (var point in points.Values)
                    result.Add(point.ToDictionary());
                return result;
            }
        }

        public static AgentPointerState Get(string sessionId, string agentId = null)
        {
            lock (Lock)
            {
                AgentPointerState pointer;
                return SessionPointers.TryGetValue(ResolveKey(sessionId, agentId), out pointer) ? pointer : null;
            }
        }

        public static List<Dictionary<string, object>> List()
        {
            lock (Lock)
            {
                var pointers = new List<Dictionary<string, object>>();
                foreach (var pointer in SessionPointers.Values)
                    pointers.Add(pointer.ToDictionary());
                return pointers;
            }
        }

        public static List<AgentPointerState> States()
        {
            lock (Lock)
            {
                return new List<AgentPointerState>(SessionPointers.Values);
            }
        }

        public static AgentPointerState SetCell(string sessionId, string agentId, int worldId, int x, int y, string label = null, Color? color = null, bool visible = true)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.WorldId = worldId;
            pointer.Cell = Grid.XYToCell(x, y);
            pointer.WorldPosition = new Vector3(x + 0.5f, y + 0.5f, -100f);
            pointer.ScreenPosition = WorldToScreen(pointer.WorldPosition);
            pointer.Visible = visible;
            if (!string.IsNullOrWhiteSpace(label))
                pointer.Label = label;
            if (color.HasValue)
                pointer.Color = color.Value;
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "aim_cell";
            return pointer;
        }

        public static AgentPointerState SetWorldPosition(string sessionId, string agentId, int worldId, Vector3 worldPosition, string label = null, Color? color = null, bool visible = true)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.WorldId = worldId;
            pointer.Cell = Grid.PosToCell(worldPosition);
            pointer.WorldPosition = worldPosition;
            pointer.ScreenPosition = WorldToScreen(worldPosition);
            pointer.Visible = visible;
            if (!string.IsNullOrWhiteSpace(label))
                pointer.Label = label;
            if (color.HasValue)
                pointer.Color = color.Value;
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "aim_world";
            return pointer;
        }

        public static AgentPointerState BeginDrag(string sessionId, string agentId, int worldId, int cell, string dragTool, string label = null)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.WorldId = worldId;
            pointer.Cell = cell;
            pointer.DragStartCell = cell;
            pointer.DragCurrentCell = cell;
            pointer.IsDragging = true;
            pointer.DragTool = dragTool;
            pointer.DragMode = "line";
            pointer.WorldPosition = CellToWorld(cell);
            pointer.ScreenPosition = WorldToScreen(pointer.WorldPosition);
            if (!string.IsNullOrWhiteSpace(label))
                pointer.Label = label;
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "drag_begin";
            return pointer;
        }

        public static AgentPointerState UpdateDrag(string sessionId, string agentId, int cell)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.DragCurrentCell = cell;
            pointer.Cell = cell;
            pointer.WorldPosition = CellToWorld(cell);
            pointer.ScreenPosition = WorldToScreen(pointer.WorldPosition);
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "drag_update";
            return pointer;
        }

        public static AgentPointerState EndDrag(string sessionId, string agentId, int cell)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.DragCurrentCell = cell;
            pointer.Cell = cell;
            pointer.WorldPosition = CellToWorld(cell);
            pointer.ScreenPosition = WorldToScreen(pointer.WorldPosition);
            pointer.IsDragging = false;
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "drag_end";
            return pointer;
        }

        private static string ResolveKey(string sessionId, string agentId)
        {
            return "agent:" + NormalizeAgentId(agentId);
        }

        private static string NormalizeSessionId(string sessionId)
        {
            return string.IsNullOrWhiteSpace(sessionId) ? DefaultSessionId : sessionId.Trim();
        }

        private static string NormalizeAgentId(string agentId)
        {
            return string.IsNullOrWhiteSpace(agentId) ? DefaultAgentId : agentId.Trim();
        }

        internal static string NormalizeJumpCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "p1";
            code = code.Trim().ToLowerInvariant().Replace("+", "");
            if (code == "p")
                return "p1";
            if (code.StartsWith("p", StringComparison.Ordinal))
                return code;
            return "p" + code;
        }

        private static string NormalizeTool(string tool)
        {
            tool = (tool ?? "inspect").Trim().ToLowerInvariant();
            if (tool == "build" || tool == "dig" || tool == "cancel" || tool == "sweep" || tool == "mop" || tool == "disinfect" || tool == "harvest" || tool == "deconstruct")
                return tool;
            return "inspect";
        }

        private static string ToolLabel(string tool, string prefabId)
        {
            return tool == "build" && !string.IsNullOrWhiteSpace(prefabId) ? "Build " + prefabId.Trim() : char.ToUpper(tool[0]) + tool.Substring(1);
        }

        private static string ToolIcon(string tool, string prefabId)
        {
            return tool == "build" && !string.IsNullOrWhiteSpace(prefabId) ? prefabId.Trim() : tool;
        }

        private static Vector3 CellToWorld(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return Vector3.zero;
            Grid.CellToXY(cell, out int x, out int y);
            return new Vector3(x + 0.5f, y + 0.5f, -100f);
        }

        private static Vector2 WorldToScreen(Vector3 world)
        {
            var camera = Camera.main;
            if (camera == null)
                return Vector2.zero;
            var screen = camera.WorldToScreenPoint(world);
            return new Vector2(screen.x, Screen.height - screen.y);
        }
    }

    public static class AgentPointerTools
    {
        public static McpTool GetPointerState()
        {
            return new McpTool
            {
                Name = "agent_pointer_get",
                Group = "camera",
                Mode = "read",
                Risk = "none",
                Description = "读取当前 agent 的指针状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识；默认使用稳定的 agent 指针", Required = false }
                },
                Handler = args =>
                {
                    string sessionId = ToolSessionContext.SessionId;
                    var pointer = AgentPointerRegistry.GetOrCreate(sessionId, args["agentId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
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
                Description = "把指针对准一个格子中心，后续所有动作都围绕这个指针进行",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false }
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

                    CameraController.Instance?.SnapTo(new Vector3(x.Value + 0.5f, y.Value + 0.5f, -100f), Camera.main != null ? Camera.main.orthographicSize : 8f);
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
                Description = "把指针对准一个世界坐标",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "number", Description = "世界 X 坐标", Required = true },
                    ["y"] = new McpToolParameter { Type = "number", Description = "世界 Y 坐标", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false }
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

                    CameraController.Instance?.SnapTo(worldPos, Camera.main != null ? Camera.main.orthographicSize : 8f);
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
                Description = "按相对方向移动当前 agent 指针，适合像鼠标一样逐格微调",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["direction"] = new McpToolParameter { Type = "string", Description = "方向：right、left、up、down；也可省略并直接传 dx/dy", Required = false, EnumValues = new List<string> { "right", "left", "up", "down" } },
                    ["steps"] = new McpToolParameter { Type = "integer", Description = "移动格数，默认 1", Required = false },
                    ["dx"] = new McpToolParameter { Type = "integer", Description = "相对 X 偏移；direction 为空时使用", Required = false },
                    ["dy"] = new McpToolParameter { Type = "integer", Description = "相对 Y 偏移；direction 为空时使用", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选指针标签", Required = false }
                },
                Handler = args =>
                {
                    string agentId = args["agentId"]?.ToString();
                    var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
                    if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                        return CallToolResult.Error("Pointer is not aimed at a valid cell; call agent_pointer_aim_cell first");

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
                    return CallToolResult.Text(JsonConvert.SerializeObject(moved.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SelectTool()
        {
            return new McpTool
            {
                Name = "agent_pointer_select_tool",
                Group = "camera",
                Mode = "execute",
                Risk = "low",
                Description = "切换当前 agent 指针选中的工具；build 工具可同时选择建筑蓝图、材料、外观和优先级，并会在鼠标旁可视化显示",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["tool"] = new McpToolParameter { Type = "string", Description = "工具类型：inspect、build、dig、cancel、sweep、mop、disinfect、harvest、deconstruct", Required = true, EnumValues = new List<string> { "inspect", "build", "dig", "cancel", "sweep", "mop", "disinfect", "harvest", "deconstruct" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "tool=build 时的建筑 prefabId，例如 Wire、Tile、Ladder", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "tool=build 时的材料 Tag；默认/auto 自动选择", Required = false },
                    ["facade"] = new McpToolParameter { Type = "string", Description = "tool=build 时的外观 ID", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "优先级 1-9，默认 5", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false }
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
                Description = "在当前指针格子执行一次左键确认，按当前选中工具触发建造/挖掘/取消/清扫等操作",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，传给支持 dryRun 的子工具", Required = false }
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
                Description = "模拟按住左键向上下左右拖拽若干格，并按当前选中工具执行直线操作",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["direction"] = new McpToolParameter { Type = "string", Description = "方向：right、left、up、down", Required = true, EnumValues = new List<string> { "right", "left", "up", "down" } },
                    ["length"] = new McpToolParameter { Type = "integer", Description = "覆盖格数，包含起点；例如 5 表示 5 格", Required = true },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，传给支持 dryRun 的子工具", Required = false },
                    ["allowFootprintDrag"] = new McpToolParameter { Type = "boolean", Description = "默认 false。拖拽建造只允许 1x1 footprint；床、厕所、机器等多格建筑需逐个 left_click，除非显式设为 true", Required = false }
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
                Description = "跳转 agent 指针和相机。支持绝对 x/y、相对 dx/dy、方向 steps，或跳转到 p1/p2 等标点",
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
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false },
                    ["zoom"] = new McpToolParameter { Type = "number", Description = "跳转后相机缩放，默认保持当前缩放", Required = false }
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
                Description = "设置 AI 跳转点，代号为 p1、p2、p+数字；未给 x/y 时保存当前指针位置",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["code"] = new McpToolParameter { Type = "string", Description = "跳转点代号，如 p1、p2；p 等价 p1", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "可选绝对 X；留空使用当前指针", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "可选绝对 Y；留空使用当前指针", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "可选世界 ID；默认当前或指针世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选标签", Required = false },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false }
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
                Description = "列出当前 agent 的 AI 跳转点",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false }
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
                Description = "取消指定 AI 跳转点",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["code"] = new McpToolParameter { Type = "string", Description = "跳转点代号，如 p1、p2", Required = true },
                    ["agentId"] = new McpToolParameter { Type = "string", Description = "可选 agent 标识", Required = false }
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

        private sealed class PointerLookup
        {
            public AgentPointerState State;
            public string Error;
        }

        private static PointerLookup RequirePointer(string agentId)
        {
            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return new PointerLookup { Error = "Pointer is not aimed at a valid cell; call agent_pointer_aim_cell first" };
            return new PointerLookup { State = pointer };
        }

        private static string NormalizeActionTool(string tool)
        {
            tool = (tool ?? "inspect").Trim().ToLowerInvariant();
            switch (tool)
            {
                case "build":
                case "dig":
                case "cancel":
                case "sweep":
                case "mop":
                case "disinfect":
                case "harvest":
                case "deconstruct":
                case "inspect":
                    return tool;
                case "mine":
                case "excavate":
                    return "dig";
                case "erase":
                case "remove":
                    return "cancel";
                case "clean":
                    return "sweep";
                case "destruct":
                case "拆除":
                    return "deconstruct";
                default:
                    return "inspect";
            }
        }

        private static CallToolResult ExecuteSelectedTool(AgentPointerState pointer, int x1, int y1, int x2, int y2, JObject sourceArgs, bool isDrag)
        {
            string tool = NormalizeActionTool(pointer.CurrentTool);
            if (tool == "inspect")
            {
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["action"] = isDrag ? "hold_left" : "left_click",
                    ["tool"] = "inspect",
                    ["message"] = "No modifying tool selected"
                }, McpJsonUtil.Settings));
            }

            var args = CloneActionArgs(sourceArgs);
            int worldId = pointer.WorldId >= 0 ? pointer.WorldId : ToolUtil.ResolveWorldId(args);
            args["worldId"] = worldId;
            args["confirm"] = true;
            if (args["priority"] == null)
                args["priority"] = pointer.Priority;

            if (tool == "build")
            {
                if (string.IsNullOrWhiteSpace(pointer.BuildPrefabId))
                    return CallToolResult.Error("Current pointer tool is build but no prefabId is selected; call agent_pointer_select_tool first");

                args["prefabId"] = pointer.BuildPrefabId;
                if (args["material"] == null && !string.IsNullOrWhiteSpace(pointer.BuildMaterial))
                    args["material"] = pointer.BuildMaterial;
                if (args["facade"] == null && !string.IsNullOrWhiteSpace(pointer.BuildFacade))
                    args["facade"] = pointer.BuildFacade;

                return isDrag
                    ? BuildPlanningTools.DragLineFromPointer(args)
                    : BuildPlanningTools.PlanAtPointer(args);
            }

            SetRect(args, x1, y1, x2, y2);
            switch (tool)
            {
                case "dig":
                    return OrdersTools.DigArea().Handler(args);
                case "cancel":
                    return OrdersTools.CancelArea().Handler(args);
                case "sweep":
                    return OrdersTools.SweepArea().Handler(args);
                case "mop":
                    return OrdersTools.MopArea().Handler(args);
                case "disinfect":
                    return OrdersTools.DisinfectArea().Handler(args);
                case "harvest":
                    return OrdersTools.HarvestArea().Handler(args);
                case "deconstruct":
                    return ExecuteDeconstructLine(x1, y1, x2, y2, args, isDrag);
                default:
                    return CallToolResult.Error("Unsupported pointer tool: " + tool);
            }
        }

        private static JObject CloneActionArgs(JObject sourceArgs)
        {
            var args = sourceArgs == null ? new JObject() : (JObject)sourceArgs.DeepClone();
            args.Remove("agentId");
            return args;
        }

        private static void SetRect(JObject args, int x1, int y1, int x2, int y2)
        {
            args["x1"] = Math.Min(x1, x2);
            args["y1"] = Math.Min(y1, y2);
            args["x2"] = Math.Max(x1, x2);
            args["y2"] = Math.Max(y1, y2);
        }

        private static CallToolResult ExecuteDeconstructLine(int x1, int y1, int x2, int y2, JObject sourceArgs, bool isDrag)
        {
            if (!isDrag)
            {
                var single = CloneActionArgs(sourceArgs);
                single["x"] = x1;
                single["y"] = y1;
                single["confirm"] = true;
                return OrdersTools.DeconstructBuilding().Handler(single);
            }

            int count = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;
            if (count > 200)
                return CallToolResult.Error("Refusing to deconstruct more than 200 cells");

            int queued = 0;
            int failed = 0;
            var results = new List<Dictionary<string, object>>();
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0f : (float)i / (count - 1);
                int x = Mathf.RoundToInt(Mathf.Lerp(x1, x2, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y1, y2, t));
                var args = CloneActionArgs(sourceArgs);
                args["x"] = x;
                args["y"] = y;
                args["confirm"] = true;
                var result = OrdersTools.DeconstructBuilding().Handler(args);
                if (result.IsError)
                    failed++;
                else
                    queued++;
                results.Add(new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["ok"] = !result.IsError,
                    ["text"] = ResultText(result)
                });
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["tool"] = "deconstruct",
                ["queued"] = queued,
                ["failed"] = failed,
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult WrapActionResult(AgentPointerState pointer, CallToolResult result)
        {
            var payload = new Dictionary<string, object>
            {
                ["ok"] = result != null && !result.IsError,
                ["pointer"] = pointer != null ? pointer.ToDictionary() : null,
                ["result"] = new Dictionary<string, object>
                {
                    ["isError"] = result == null || result.IsError,
                    ["text"] = ResultText(result)
                }
            };
            string text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings);
            return result != null && result.IsError ? CallToolResult.Error(text) : CallToolResult.Text(text);
        }

        private static string ResultText(CallToolResult result)
        {
            if (result == null || result.Content == null || result.Content.Count == 0 || result.Content[0] == null)
                return "";
            return result.Content[0].Text ?? "";
        }

        private static CallToolResult JumpPointer(JObject args)
        {
            string agentId = args["agentId"]?.ToString();
            AgentPointerState current = AgentPointerRegistry.Get(ToolSessionContext.SessionId, agentId);
            string code = args["code"]?.ToString();
            int worldId = ToolUtil.GetInt(args, "worldId") ?? (current != null && current.WorldId >= 0 ? current.WorldId : ToolUtil.ResolveWorldId(args));
            int targetX;
            int targetY;

            if (!string.IsNullOrWhiteSpace(code))
            {
                if (string.Equals(code.Trim(), "home", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryFindHomeCell(worldId, out targetX, out targetY, out worldId))
                        return CallToolResult.Error("Could not resolve home marker");
                }
                else
                {
                    AgentPointerJumpPoint point;
                    if (!AgentPointerRegistry.TryGetJumpPoint(ToolSessionContext.SessionId, agentId, code, out point))
                        return CallToolResult.Error("Jump point not found: " + AgentPointerRegistry.NormalizeJumpCode(code));
                    if (!Grid.IsValidCell(point.Cell))
                        return CallToolResult.Error("Jump point cell is no longer valid");
                    Grid.CellToXY(point.Cell, out targetX, out targetY);
                    worldId = point.WorldId;
                }
            }
            else
            {
                bool relative = ToolUtil.GetBool(args, "relative", false) || ToolUtil.GetBool(args, "rel", false);
                int? x = ToolUtil.GetInt(args, "x");
                int? y = ToolUtil.GetInt(args, "y");
                int dx = ToolUtil.GetInt(args, "dx") ?? 0;
                int dy = ToolUtil.GetInt(args, "dy") ?? 0;
                if (!string.IsNullOrWhiteSpace(args["direction"]?.ToString()))
                {
                    if (!TryDirection(args["direction"]?.ToString(), out dx, out dy))
                        return CallToolResult.Error("direction must be right, left, up or down");
                    int steps = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "steps") ?? 1, 500));
                    dx *= steps;
                    dy *= steps;
                    relative = true;
                }

                if (relative || dx != 0 || dy != 0)
                {
                    if (current == null || !Grid.IsValidCell(current.Cell))
                        return CallToolResult.Error("Relative jump requires an aimed pointer");
                    Grid.CellToXY(current.Cell, out int cx, out int cy);
                    targetX = cx + (x ?? dx);
                    targetY = cy + (y ?? dy);
                }
                else
                {
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("code, x/y, dx/dy, or direction is required");
                    targetX = x.Value;
                    targetY = y.Value;
                }
            }

            int targetCell = Grid.XYToCell(targetX, targetY);
            if (!Grid.IsValidCell(targetCell))
                return CallToolResult.Error("Target cell is outside the grid");
            if (!ToolUtil.CellMatchesWorld(targetCell, worldId))
                return CallToolResult.Error($"Target cell is not in worldId={worldId}");

            var pointer = AgentPointerRegistry.SetCell(ToolSessionContext.SessionId, agentId, worldId, targetX, targetY);
            float zoom = ToolUtil.GetFloat(args, "zoom") ?? (Camera.main != null ? Camera.main.orthographicSize : 8f);
            CameraController.Instance?.SnapTo(new Vector3(targetX + 0.5f, targetY + 0.5f, -100f), zoom);
            pointer.LastAction = "jump";
            return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
        }

        private static bool TryFindHomeCell(int preferredWorldId, out int x, out int y, out int worldId)
        {
            foreach (var telepad in Components.Telepads.Items)
            {
                if (telepad == null || telepad.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(telepad.gameObject);
                if (!Grid.IsValidCell(cell))
                    continue;
                int itemWorldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;
                if (preferredWorldId >= 0 && itemWorldId >= 0 && itemWorldId != preferredWorldId)
                    continue;
                Grid.CellToXY(cell, out x, out y);
                worldId = itemWorldId >= 0 ? itemWorldId : preferredWorldId;
                return true;
            }

            var camera = Camera.main;
            if (camera != null)
            {
                x = Mathf.RoundToInt(camera.transform.position.x);
                y = Mathf.RoundToInt(camera.transform.position.y);
                worldId = preferredWorldId;
                return true;
            }

            x = 0;
            y = 0;
            worldId = preferredWorldId;
            return false;
        }

        private static bool TryDirection(string direction, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            switch ((direction ?? "").Trim().ToLowerInvariant())
            {
                case "right":
                case "east":
                case "e":
                    dx = 1;
                    return true;
                case "left":
                case "west":
                case "w":
                    dx = -1;
                    return true;
                case "up":
                case "north":
                case "n":
                    dy = 1;
                    return true;
                case "down":
                case "south":
                case "s":
                    dy = -1;
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class ToolSessionContext
    {
        public static string SessionId
        {
            get { return McpHttpServer.CurrentSessionId ?? "global"; }
        }
    }
}
