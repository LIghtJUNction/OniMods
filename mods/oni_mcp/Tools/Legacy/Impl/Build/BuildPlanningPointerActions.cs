using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static CallToolResult PlanAtPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            string prefabId = args["prefabId"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefabId))
                return CallToolResult.Error("prefabId is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

            Grid.CellToXY(pointer.Cell, out int x, out int y);
            if (args["worldId"] == null && pointer.WorldId >= 0)
                args["worldId"] = pointer.WorldId;
            var result = TryPlanOne(prefabId, x, y, args);
            result["pointer"] = pointer.ToDictionary();
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        internal static CallToolResult DragLineFromPointer(JObject args)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required");

            var pointer = AgentPointerRegistry.Get(ToolSessionContext.SessionId, args["agentId"]?.ToString());
            if (pointer == null || !Grid.IsValidCell(pointer.Cell))
                return CallToolResult.Error("Pointer is not aimed at a valid cell; call navigation_control action=aim_cell first");

            int? requestedLength = ToolUtil.GetInt(args, "length");
            if (!requestedLength.HasValue || requestedLength.Value <= 0)
                return CallToolResult.Error("length must be a positive integer");
            int length = Math.Max(1, Math.Min(requestedLength.Value, 200));

            string direction = (args["direction"]?.ToString() ?? "").Trim().ToLowerInvariant();
            int dx;
            int dy;
            if (!TryDirection(direction, out dx, out dy))
                return CallToolResult.Error("direction must be right, left, up or down");

            string prefabId = string.IsNullOrWhiteSpace(args["prefabId"]?.ToString()) ? "Wire" : args["prefabId"].ToString();
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
                return CallToolResult.Error("Building def not found");

            var dragPolicy = BuildDragPolicy(def, args);
            if (!dragPolicy.Allowed)
                return CallToolResult.Error(JsonConvert.SerializeObject(dragPolicy.ToDictionary(), McpJsonUtil.Settings));

            Grid.CellToXY(pointer.Cell, out int startX, out int startY);
            int endX = startX + dx * (length - 1);
            int endY = startY + dy * (length - 1);
            int worldId = pointer.WorldId >= 0 ? pointer.WorldId : ToolUtil.ResolveWorldId(args);
            int endCell = Grid.XYToCell(endX, endY);
            if (!Grid.IsValidCell(endCell))
                return CallToolResult.Error("Drag end cell is outside the grid");
            if (!ToolUtil.CellMatchesWorld(endCell, worldId))
                return CallToolResult.Error($"Drag end cell is not in worldId={worldId}");
            if (args["worldId"] == null && worldId >= 0)
                args["worldId"] = worldId;

            var results = new List<Dictionary<string, object>>();
            var errors = new List<Dictionary<string, object>>();
            var plannedSupportCells = new HashSet<int>();
            var autoDigContext = AutoDigContext.FromArgs(args);
            AgentPointerRegistry.BeginDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), worldId, pointer.Cell, prefabId);

            int planned = 0;
            int valid = 0;
            int autoDigQueued = 0;
            foreach (var cell in StraightLineCells(startX, startY, endX, endY))
            {
                AgentPointerRegistry.UpdateDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), Grid.XYToCell(cell.x, cell.y));
                var result = TryPlanOne(prefabId, cell.x, cell.y, args, plannedSupportCells, autoDigContext);
                bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                bool alreadyPresent = result.ContainsKey("alreadyPresent") && (bool)result["alreadyPresent"];
                bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                autoDigQueued += GetAutoDigInt(result, "marked");
                if (ok || alreadyPresent || (IsDryRun(args) && validPlacement))
                {
                    valid++;
                    if (ok)
                        planned++;
                    RegisterSupportBlueprint(prefabId, cell.x, cell.y, plannedSupportCells);
                }
                else if (IsAutoDigResult(result))
                {
                    valid++;
                }
                else
                {
                    errors.Add(result);
                }
                results.Add(result);
            }

            var finalPointer = AgentPointerRegistry.EndDrag(ToolSessionContext.SessionId, args["agentId"]?.ToString(), endCell);
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["prefabId"] = prefabId,
                ["dragPolicy"] = dragPolicy.ToDictionary(),
                ["dryRun"] = IsDryRun(args),
                ["drag"] = new Dictionary<string, object>
                {
                    ["from"] = new { x = startX, y = startY },
                    ["to"] = new { x = endX, y = endY },
                    ["direction"] = direction,
                    ["length"] = length,
                    ["mouseButton"] = "left",
                    ["gesture"] = "long_press_drag_line"
                },
                ["valid"] = valid,
                ["planned"] = planned,
                ["autoDigQueued"] = autoDigQueued,
                ["autoDigLimitReached"] = autoDigContext.LimitReached,
                ["failed"] = errors.Count,
                ["errors"] = errors.Take(50).ToList(),
                ["pointer"] = finalPointer.ToDictionary(),
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

    }
}
