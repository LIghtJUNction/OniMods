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
                if (string.Equals(code.Trim(), "mouse", StringComparison.OrdinalIgnoreCase) || string.Equals(code.Trim(), "cursor", StringComparison.OrdinalIgnoreCase))
                {
                    int? mouseWorldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId;
                    if (!TryGetUserMouseCell(mouseWorldId, out var mouse, out string error))
                        return CallToolResult.Error(error);
                    targetX = Convert.ToInt32(mouse["x"]);
                    targetY = Convert.ToInt32(mouse["y"]);
                    worldId = Convert.ToInt32(mouse["worldId"]);
                }
                else if (string.Equals(code.Trim(), "home", StringComparison.OrdinalIgnoreCase))
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
            if (ToolUtil.GetBool(args, "moveCamera", false))
            {
                float zoom = ToolUtil.GetFloat(args, "zoom") ?? (Camera.main != null ? Camera.main.orthographicSize : 8f);
                CameraController.Instance?.SnapTo(new Vector3(targetX + 0.5f, targetY + 0.5f, -100f), zoom);
            }
            pointer.LastAction = "jump";
            ApplyDisplayText(args, agentId);
            return CallToolResult.Text(JsonConvert.SerializeObject(pointer.ToDictionary(), McpJsonUtil.Settings));
        }

        private static void ApplyDisplayText(JObject args, string agentId)
        {
            if (args == null)
                return;

            string displayText = args["displayText"]?.ToString();
            if (string.IsNullOrWhiteSpace(displayText))
                return;

            AgentPointerRegistry.SetMessage(ToolSessionContext.SessionId, agentId, displayText, DisplayTextDurationSeconds);
        }

        private static bool TryGetUserMouseCell(int? requestedWorldId, out Dictionary<string, object> payload, out string error)
        {
            payload = null;
            error = null;
            var camera = Camera.main;
            if (camera == null)
            {
                error = "Camera.main is not available";
                return false;
            }

            Vector3 mouse = Input.mousePosition;
            bool withinScreen = mouse.x >= 0f && mouse.y >= 0f && mouse.x <= Screen.width && mouse.y <= Screen.height;
            var reference = camera.WorldToScreenPoint(new Vector3(0f, 0f, -100f));
            var world = camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, reference.z));
            world.z = -100f;
            int cell = Grid.PosToCell(world);
            int worldId = requestedWorldId ?? ClusterManager.Instance?.activeWorldId ?? 0;
            bool validCell = Grid.IsValidCell(cell);
            bool matchesWorld = validCell && ToolUtil.CellMatchesWorld(cell, worldId);
            int x = -1;
            int y = -1;
            if (validCell)
                Grid.CellToXY(cell, out x, out y);

            payload = new Dictionary<string, object>
            {
                ["screen"] = new { x = Math.Round(mouse.x, 2), y = Math.Round(mouse.y, 2), topLeftY = Math.Round(Screen.height - mouse.y, 2), width = Screen.width, height = Screen.height, withinScreen },
                ["worldPosition"] = new { x = Math.Round(world.x, 2), y = Math.Round(world.y, 2), z = Math.Round(world.z, 2) },
                ["worldId"] = worldId,
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["validCell"] = validCell,
                ["matchesWorld"] = matchesWorld
            };

            if (!withinScreen)
            {
                error = "User mouse is outside the game window";
                return false;
            }
            if (!validCell)
            {
                error = "User mouse is not over a valid grid cell";
                return false;
            }
            if (!matchesWorld)
            {
                error = $"User mouse cell is not in worldId={worldId}";
                return false;
            }
            return true;
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
}
