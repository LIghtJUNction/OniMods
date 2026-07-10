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
        public string Scope { get; set; } = "agent";
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
        public System.DateTime DragFeedbackUntil { get; set; }
        public string CurrentTool { get; set; } = "inspect";
        public string ToolLabel { get; set; } = "Inspect";
        public string ToolIcon { get; set; } = "inspect";
        public string BuildPrefabId { get; set; }
        public string BuildMaterial { get; set; }
        public string BuildFacade { get; set; }
        public string Message { get; set; }
        public System.DateTime MessageCreatedAt { get; set; }
        public System.DateTime MessageExpiresAt { get; set; }
        public int Priority { get; set; } = 5;
        public string LastAction { get; set; }
        public System.DateTime UpdatedAt { get; set; }

        public Dictionary<string, object> ToDictionary()
        {
            RefreshScreenPosition();
            bool isPositioned = Grid.IsValidCell(Cell);
            int x = -1;
            int y = -1;
            if (isPositioned)
                Grid.CellToXY(Cell, out x, out y);

            return new Dictionary<string, object>
            {
                ["sessionId"] = SessionId,
                ["agentId"] = AgentId,
                ["scope"] = Scope,
                ["visible"] = Visible,
                ["label"] = Label,
                ["color"] = new { r = Math.Round(Color.r, 3), g = Math.Round(Color.g, 3), b = Math.Round(Color.b, 3), a = Math.Round(Color.a, 3) },
                ["worldId"] = WorldId,
                ["cell"] = Cell,
                ["isPositioned"] = isPositioned,
                ["worldPosition"] = isPositioned ? (object)new { x = Math.Round(WorldPosition.x, 2), y = Math.Round(WorldPosition.y, 2), z = Math.Round(WorldPosition.z, 2) } : null,
                ["screenPosition"] = isPositioned ? (object)new { x = Math.Round(ScreenPosition.x, 2), y = Math.Round(ScreenPosition.y, 2) } : null,
                ["dragging"] = IsDragging,
                ["dragStartCell"] = DragStartCell,
                ["dragCurrentCell"] = DragCurrentCell,
                ["dragMode"] = DragMode,
                ["dragTool"] = DragTool,
                ["dragFeedbackVisible"] = DragFeedbackUntil > System.DateTime.UtcNow,
                ["dragFeedbackUntil"] = DragFeedbackUntil == default(System.DateTime) ? null : DragFeedbackUntil.ToString("o"),
                ["currentTool"] = CurrentTool,
                ["toolLabel"] = ToolLabel,
                ["toolIcon"] = ToolIcon,
                ["buildPrefabId"] = BuildPrefabId,
                ["buildMaterial"] = BuildMaterial,
                ["buildFacade"] = BuildFacade,
                ["message"] = Message,
                ["messageVisible"] = !string.IsNullOrWhiteSpace(Message) && MessageExpiresAt > System.DateTime.UtcNow,
                ["messageCreatedAt"] = MessageCreatedAt == default(System.DateTime) ? null : MessageCreatedAt.ToString("o"),
                ["messageExpiresAt"] = MessageExpiresAt == default(System.DateTime) ? null : MessageExpiresAt.ToString("o"),
                ["priority"] = Priority,
                ["lastAction"] = LastAction,
                ["updatedAt"] = UpdatedAt.ToString("o"),
                ["x"] = isPositioned ? (object)x : null,
                ["y"] = isPositioned ? (object)y : null
            };
        }

        private void RefreshScreenPosition()
        {
            var camera = Camera.main;
            if (camera == null)
                return;
            var screen = camera.WorldToScreenPoint(WorldPosition);
            ScreenPosition = new Vector2(screen.x, Screen.height - screen.y);
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

    internal static class ToolSessionContext
    {
        public static string SessionId
        {
            get { return McpHttpServer.CurrentSessionId ?? "global"; }
        }
    }
}
