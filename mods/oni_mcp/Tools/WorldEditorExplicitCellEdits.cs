using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult ApplyExplicitMapEditCells(JObject args, string path, string relative, JArray editCells, JArray editLines)
        {
            if (!IsEditableMapMarkdown(relative))
                return CallToolResult.Error("editCells/editLines are only supported for editable map markdown files such as /active/map/viewport.md or /active/infrastructure/power.md");

            var changes = new List<MapEditCell>();
            string error = AppendExplicitMapEditCells(editCells, changes);
            if (error != null)
                return CallToolResult.Error(error);

            error = AppendExplicitMapEditLines(editLines, changes);
            if (error != null)
                return CallToolResult.Error(error);

            if (changes.Count == 0)
                return CallToolResult.Error("editCells/editLines is empty");

            var routed = CopyPayload(args);
            routed.Remove("content");
            routed.Remove("editCells");
            routed.Remove("editLines");
            routed["sourcePath"] = path;
            return ApplyExplicitMapEditChanges(routed, changes);
        }

        private static string AppendExplicitMapEditCells(JArray editCells, List<MapEditCell> changes)
        {
            if (editCells == null)
                return null;

            foreach (var token in editCells)
            {
                var item = token as JObject;
                if (item == null)
                    return "editCells items must be objects: {x,y,value}";

                int? x = ToolUtil.GetInt(item, "x");
                int? y = ToolUtil.GetInt(item, "y");
                string value = FirstNonEmptyCellEditValue(item, "value", "to", "token", "action");
                if (!x.HasValue || !y.HasValue || string.IsNullOrWhiteSpace(value))
                    return "editCells items require x, y, and value";

                changes.Add(CellChange(x.Value, y.Value, item, value));
            }

            return null;
        }

        private static string AppendExplicitMapEditLines(JArray editLines, List<MapEditCell> changes)
        {
            if (editLines == null)
                return null;

            foreach (var token in editLines)
            {
                var item = token as JObject;
                if (item == null)
                    return "editLines items must be objects";

                string value = FirstNonEmptyCellEditValue(item, "value", "to", "token", "action");
                if (string.IsNullOrWhiteSpace(value))
                    return "editLines items require value";

                int startX;
                int startY;
                int stepX;
                int stepY;
                int count;
                string error = ResolveEditLine(item, out startX, out startY, out stepX, out stepY, out count);
                if (error != null)
                    return error;

                for (int i = 0; i < count; i++)
                    changes.Add(CellChange(startX + stepX * i, startY + stepY * i, item, value));
            }

            return null;
        }

        private static string ResolveEditLine(JObject item, out int startX, out int startY, out int stepX, out int stepY, out int count)
        {
            startX = startY = stepX = stepY = count = 0;
            int? x = ToolUtil.GetInt(item, "x");
            int? y = ToolUtil.GetInt(item, "y");
            int? x1 = ToolUtil.GetInt(item, "x1");
            int? y1 = ToolUtil.GetInt(item, "y1");
            int? x2 = ToolUtil.GetInt(item, "x2");
            int? y2 = ToolUtil.GetInt(item, "y2");

            if (x.HasValue && y1.HasValue && y2.HasValue)
                return ResolveEndpointLine(x.Value, y1.Value, x.Value, y2.Value, out startX, out startY, out stepX, out stepY, out count);

            if (y.HasValue && x1.HasValue && x2.HasValue)
                return ResolveEndpointLine(x1.Value, y.Value, x2.Value, y.Value, out startX, out startY, out stepX, out stepY, out count);

            if (x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue)
                return ResolveEndpointLine(x1.Value, y1.Value, x2.Value, y2.Value, out startX, out startY, out stepX, out stepY, out count);

            if (!x.HasValue || !y.HasValue)
                return "editLines items require either x/y1/y2, y/x1/x2, x1/y1/x2/y2, or x/y/direction/length";

            int? length = ToolUtil.GetInt(item, "length") ?? ToolUtil.GetInt(item, "count");
            if (!length.HasValue || length.Value <= 0)
                return "editLines x/y/direction form requires positive length";

            string direction = (item["direction"]?.ToString() ?? item["dir"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!ResolveDirection(direction, out stepX, out stepY))
                return "editLines direction must be up, down, left, or right";

            startX = x.Value;
            startY = y.Value;
            count = length.Value;
            return null;
        }

        private static string ResolveEndpointLine(int x1, int y1, int x2, int y2, out int startX, out int startY, out int stepX, out int stepY, out int count)
        {
            startX = x1;
            startY = y1;
            stepX = Math.Sign(x2 - x1);
            stepY = Math.Sign(y2 - y1);
            count = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1;

            if (x1 != x2 && y1 != y2)
                return "editLines endpoint form only supports vertical or horizontal lines";

            return null;
        }

        private static bool ResolveDirection(string direction, out int stepX, out int stepY)
        {
            stepX = 0;
            stepY = 0;

            switch (direction)
            {
                case "up":
                case "north":
                    stepY = 1;
                    return true;
                case "down":
                case "south":
                    stepY = -1;
                    return true;
                case "right":
                case "east":
                    stepX = 1;
                    return true;
                case "left":
                case "west":
                    stepX = -1;
                    return true;
                default:
                    return false;
            }
        }

        private static MapEditCell CellChange(int x, int y, JObject item, string value)
        {
            return new MapEditCell
            {
                X = x,
                Y = y,
                FromToken = FirstNonEmptyCellEditValue(item, "from", "old", "current"),
                ToToken = value.Trim()
            };
        }

        private static CallToolResult ApplyExplicitMapEditChanges(JObject args, List<MapEditCell> changes)
        {
            int writeBudget = MapEditWriteBudget(args);
            bool partial = changes.Count > writeBudget;
            var executableChanges = partial ? changes.Take(writeBudget).ToList() : changes;

            var results = new JArray();
            bool anyError = false;
            foreach (var group in executableChanges.GroupBy(ChangeKind))
            {
                CallToolResult result;
                if (group.Key == "deconstruct")
                    result = ApplyExplicitDeconstructCells(args, group);
                else if (group.Key == "build")
                    result = ApplyBuildMapEdit(args, group);
                else if (group.Key == "wire")
                    result = ApplyConnectionMapEdit(args, group);
                else if (IsOrderAction(group.Key))
                    result = ApplyOrderMapEdit(args, group.Key, group);
                else
                    result = UnsupportedMapEdit(group);

                anyError = anyError || result.IsError;
                results.Add(new JObject
                {
                    ["action"] = group.Key,
                    ["cells"] = group.Count(),
                    ["ok"] = !result.IsError,
                    ["error"] = result.IsError ? result.Content?.FirstOrDefault()?.Text ?? string.Empty : string.Empty,
                    ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                });
            }

            return JsonResult(new JObject
            {
                ["source"] = args["sourcePath"]?.ToString(),
                ["requested"] = changes.Count,
                ["applied"] = executableChanges.Count,
                ["deferred"] = Math.Max(0, changes.Count - executableChanges.Count),
                ["status"] = anyError ? "partial_or_failed" : partial ? "partial_budget" : "complete",
                ["results"] = results
            });
        }

        private static CallToolResult ApplyExplicitDeconstructCells(JObject parentArgs, IEnumerable<MapEditCell> cells)
        {
            var results = new JArray();
            bool anyError = false;
            foreach (var cell in cells)
            {
                var orderArgs = CopyPayload(parentArgs);
                orderArgs["domain"] = "designation";
                orderArgs["action"] = "deconstruct";
                orderArgs["x"] = cell.X;
                orderArgs["y"] = cell.Y;
                orderArgs["confirm"] = ToolUtil.GetBool(parentArgs, "confirm", false);
                if (parentArgs["type"] != null)
                    orderArgs["type"] = parentArgs["type"];

                var result = OrdersControlEntryTools.ControlOrders().Handler(orderArgs);
                anyError = anyError || result.IsError;
                results.Add(new JObject
                {
                    ["x"] = cell.X,
                    ["y"] = cell.Y,
                    ["ok"] = !result.IsError,
                    ["error"] = result.IsError ? result.Content?.FirstOrDefault()?.Text ?? string.Empty : string.Empty,
                    ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                });
            }

            return JsonResult(new JObject
            {
                ["ok"] = !anyError,
                ["action"] = "deconstruct",
                ["cells"] = cells.Count(),
                ["results"] = results
            });
        }

        private static string FirstNonEmptyCellEditValue(JObject item, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = item[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return string.Empty;
        }
    }
}
