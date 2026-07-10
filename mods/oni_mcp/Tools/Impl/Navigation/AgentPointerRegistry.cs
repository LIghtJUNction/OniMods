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
    internal static partial class AgentPointerRegistry
    {
        private const string DefaultSessionId = "global";
        private const string DefaultAgentId = "agent";
        private const string AgentScope = "agent";
        private const int MaxAgentPointers = 16;
        private static readonly System.TimeSpan PointerPruneInterval = System.TimeSpan.FromSeconds(15);
        private static readonly System.TimeSpan UnpositionedPointerTtl = System.TimeSpan.FromMinutes(2);
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, AgentPointerState> SessionPointers = new Dictionary<string, AgentPointerState>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, AgentPointerJumpPoint>> JumpPoints = new Dictionary<string, Dictionary<string, AgentPointerJumpPoint>>(StringComparer.Ordinal);
        private static System.DateTime LastPrunedAt;
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
            string scope = PointerScope(agentId);
            lock (Lock)
            {
                RecyclePointersLocked(System.DateTime.UtcNow);
                AgentPointerState pointer;
                if (SessionPointers.TryGetValue(key, out pointer))
                {
                    pointer.SessionId = normalizedSessionId;
                    pointer.Scope = scope;
                    if (string.IsNullOrWhiteSpace(pointer.AgentId))
                        pointer.AgentId = normalizedAgentId;
                    if (scope == AgentScope && IsSessionPrefixedLabel(pointer.Label, pointer.AgentId))
                        pointer.Label = pointer.AgentId;
                    return pointer;
                }

                pointer = new AgentPointerState
                {
                    SessionId = normalizedSessionId,
                    AgentId = normalizedAgentId,
                    Scope = scope,
                    Label = DefaultLabel(normalizedSessionId, normalizedAgentId, scope),
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
                RecyclePointersLocked(System.DateTime.UtcNow);
                AgentPointerState pointer;
                return SessionPointers.TryGetValue(ResolveKey(sessionId, agentId), out pointer) ? pointer : null;
            }
        }

        public static bool Remove(string sessionId, string agentId, out bool jumpPointsRemoved)
        {
            string key = ResolveKey(sessionId, agentId);
            lock (Lock)
            {
                jumpPointsRemoved = JumpPoints.Remove(key);
                return SessionPointers.Remove(key);
            }
        }

        public static List<Dictionary<string, object>> List()
        {
            lock (Lock)
            {
                RecyclePointersLocked(System.DateTime.UtcNow);
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
                RecyclePointersLocked(System.DateTime.UtcNow);
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
            pointer.DragFeedbackUntil = System.DateTime.UtcNow.AddSeconds(0.8);
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
            pointer.DragFeedbackUntil = System.DateTime.UtcNow.AddSeconds(0.8);
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
            pointer.DragFeedbackUntil = System.DateTime.UtcNow.AddSeconds(0.8);
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "drag_end";
            return pointer;
        }

        public static AgentPointerState SetMessage(string sessionId, string agentId, string message, float durationSeconds)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            var now = System.DateTime.UtcNow;
            pointer.Message = NormalizeMessage(message);
            pointer.MessageCreatedAt = now;
            pointer.MessageExpiresAt = now.AddSeconds(Math.Max(1f, Math.Min(durationSeconds, 60f)));
            pointer.UpdatedAt = now;
            pointer.LastAction = "say";
            return pointer;
        }

        public static AgentPointerState ClearMessage(string sessionId, string agentId)
        {
            var pointer = GetOrCreate(sessionId, agentId);
            pointer.Message = null;
            pointer.MessageCreatedAt = default(System.DateTime);
            pointer.MessageExpiresAt = default(System.DateTime);
            pointer.UpdatedAt = System.DateTime.UtcNow;
            pointer.LastAction = "say_clear";
            return pointer;
        }

        private static string NormalizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "";
            message = message.Trim().Replace("\r\n", "\n").Replace('\r', '\n');
            return message.Length <= 160 ? message : message.Substring(0, 159) + ".";
        }

        private static string ResolveKey(string sessionId, string agentId)
        {
            return CanonicalAgentKey(agentId);
        }

        private static string CanonicalAgentKey(string agentId)
        {
            return "agent:" + NormalizeAgentId(agentId);
        }

        private static string PointerScope(string agentId)
        {
            return AgentScope;
        }

        private static string NormalizeSessionId(string sessionId)
        {
            return string.IsNullOrWhiteSpace(sessionId) ? DefaultSessionId : sessionId.Trim();
        }

        private static string NormalizeAgentId(string agentId)
        {
            return string.IsNullOrWhiteSpace(agentId) ? DefaultAgentId : agentId.Trim();
        }

        internal static string PublicAgentId(string agentId)
        {
            return NormalizeAgentId(agentId);
        }

        private static string DefaultLabel(string sessionId, string agentId, string scope)
        {
            if (scope == AgentScope)
                return agentId;
            string prefix = ClientLabelPrefix(sessionId);
            return string.IsNullOrWhiteSpace(prefix) ? agentId : prefix + ":" + agentId;
        }

    }
}
