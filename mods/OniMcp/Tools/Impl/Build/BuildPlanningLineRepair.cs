using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static CallToolResult RepairUtilityLine(JObject args)
        {
            args = args ?? new JObject();
            JObject connectArgs = (JObject)args.DeepClone();
            connectArgs["action"] = "auto_connect";

            if (string.IsNullOrWhiteSpace(connectArgs["prefabId"]?.ToString())
                && string.IsNullOrWhiteSpace(connectArgs["type"]?.ToString()))
                connectArgs["type"] = "wire";

            if (!HasExplicitUtilityPath(connectArgs)
                && !TryFillRepairPath(connectArgs, out string error))
                return CallToolResult.Error(error);

            return AutoConnectUtility().Handler(connectArgs);
        }

        private static bool HasExplicitUtilityPath(JObject args)
        {
            if (args["points"] != null)
                return true;

            return ToolUtil.GetInt(args, "fromX").HasValue
                && ToolUtil.GetInt(args, "fromY").HasValue
                && ToolUtil.GetInt(args, "toX").HasValue
                && ToolUtil.GetInt(args, "toY").HasValue;
        }

        private static bool TryFillRepairPath(JObject args, out string error)
        {
            error = null;

            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? x1 = ToolUtil.GetInt(args, "x1");
            int? y1 = ToolUtil.GetInt(args, "y1");
            int? x2 = ToolUtil.GetInt(args, "x2");
            int? y2 = ToolUtil.GetInt(args, "y2");

            if (x.HasValue && y.HasValue && x2.HasValue && y2.HasValue)
            {
                SetRepairEndpoints(args, x.Value, y.Value, x2.Value, y2.Value);
                return true;
            }

            if (x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue)
            {
                SetRepairEndpoints(args, x1.Value, y1.Value, x2.Value, y2.Value);
                return true;
            }

            if (x.HasValue && y.HasValue)
            {
                string direction = FirstLineRepairText(args, "direction", "dir", "open", "edge");
                if (TryLineRepairDirection(direction, out int dx, out int dy))
                {
                    int steps = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "steps") ?? 1, 200));
                    SetRepairEndpoints(args, x.Value, y.Value, x.Value + dx * steps, y.Value + dy * steps);
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(args["toQuery"]?.ToString())
                || !string.IsNullOrWhiteSpace(args["query"]?.ToString())
                || !string.IsNullOrWhiteSpace(args["target"]?.ToString()))
                return true;

            error = "repair_line needs points, fromX/fromY/toX/toY, x/y/x2/y2, x/y/direction, or query/toQuery.";
            return false;
        }

        private static void SetRepairEndpoints(JObject args, int fromX, int fromY, int toX, int toY)
        {
            args["fromX"] = fromX;
            args["fromY"] = fromY;
            args["toX"] = toX;
            args["toY"] = toY;
        }

        private static string FirstLineRepairText(JObject args, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = args[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return string.Empty;
        }

        private static bool TryLineRepairDirection(string direction, out int dx, out int dy)
        {
            direction = (direction ?? string.Empty).Trim();
            switch (direction)
            {
                case "R":
                case "r":
                case "右":
                    dx = 1;
                    dy = 0;
                    return true;
                case "L":
                case "l":
                case "左":
                    dx = -1;
                    dy = 0;
                    return true;
                case "U":
                case "u":
                case "上":
                    dx = 0;
                    dy = 1;
                    return true;
                case "D":
                case "d":
                case "下":
                    dx = 0;
                    dy = -1;
                    return true;
                default:
                    return TryDirection(direction, out dx, out dy);
            }
        }
    }
}
