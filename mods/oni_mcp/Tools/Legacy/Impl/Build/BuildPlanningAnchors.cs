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
        private static bool IsDryRun(JObject args)
        {
            return ToolUtil.GetBool(args, "dryRun", false) || ToolUtil.GetBool(args, "validateOnly", false);
        }

        private static Dictionary<string, McpToolParameter> BuildPlacementParameters(bool includeConfirm, bool includeArea)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["prefabId"] = new McpToolParameter { Type = "string", Description = "建筑 prefabId，例如 MedicalCot、Tile、Wire；也可省略并用 plan/blueprint/sequence 解析", Required = false },
                ["plan"] = new McpToolParameter { Type = "string", Description = "文字建造序列/短语，解析成 prefabId/material/query，例如 粉砂岩砖@氧气、Wire-小型冰箱、用铜矿造手动发电机在电池旁", Required = false },
                ["blueprint"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                ["sequence"] = new McpToolParameter { Type = "string", Description = "plan 的别名；用于搜索/行动一体化时传文字序列", Required = false },
                ["text"] = new McpToolParameter { Type = "string", Description = "plan 的别名", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "lowerLeftCell anchor X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "lowerLeftCell anchor Y", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "坐标省略时按对象/元素/复制人名称搜索定位 anchor", Required = false },
                ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名：搜索目标名称、prefabId、元素或复制人", Required = false },
                ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名：搜索目标名称、prefabId、元素或复制人", Required = false },
                ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
                ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
                ["offsetX"] = new McpToolParameter { Type = "integer", Description = "搜索/x,y 解析出 anchor 后的 X 偏移；别名 dx。用于一次调用内按搜索结果相对放置蓝图", Required = false },
                ["offsetY"] = new McpToolParameter { Type = "integer", Description = "搜索/x,y 解析出 anchor 后的 Y 偏移；别名 dy。用于一次调用内按搜索结果相对放置蓝图", Required = false },
                ["dx"] = new McpToolParameter { Type = "integer", Description = "offsetX 的短别名", Required = false },
                ["dy"] = new McpToolParameter { Type = "integer", Description = "offsetY 的短别名", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界或 areaId 绑定世界", Required = false },
                ["material"] = new McpToolParameter { Type = "string", Description = "建造材料 tag；auto/default 自动选择", Required = false },
                ["facade"] = new McpToolParameter { Type = "string", Description = "可选建筑外观/permit id", Required = false },
                ["orientation"] = new McpToolParameter { Type = "string", Description = "可选朝向，默认 Neutral", Required = false },
                ["priority"] = new McpToolParameter { Type = "integer", Description = "建造优先级 1..9，默认 5", Required = false },
                ["allowUnsupported"] = new McpToolParameter { Type = "boolean", Description = "默认 false；OnFloor 建筑无支撑时拒绝", Required = false },
                ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true。建造 footprint 遇到可挖自然固体时，执行建造会先自动标记挖掘，并继续尝试在同一格放置建造蓝图", Required = false },
                ["autoUprootObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true。建造 footprint 遇到可铲植物时自动标记铲除，并继续尝试放置建造蓝图", Required = false },
                ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "单次命令最多自动标记多少个挖掘/铲除格，默认 100，最大 500", Required = false },
                ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，不生成蓝图", Required = false }
            };

            if (includeConfirm)
                parameters["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false };
            if (includeArea)
            {
                parameters["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；relative=true 时坐标按区域 rx/ry 解释", Required = false };
                parameters["relative"] = new McpToolParameter { Type = "boolean", Description = "配合 areaId 使用相对坐标", Required = false };
            }

            return parameters;
        }

        private static bool TryResolvePoint(JObject args, out int x, out int y, out string error)
        {
            x = 0;
            y = 0;
            error = null;
            int? requestedX = ToolUtil.GetInt(args, "x");
            int? requestedY = ToolUtil.GetInt(args, "y");
            if (!requestedX.HasValue || !requestedY.HasValue)
            {
                if (!ToolUtil.TryResolveSearchCell(args, out x, out y, out error))
                    return false;
                ApplyAnchorOffset(args, ref x, ref y);
                return true;
            }

            var area = WorldEditor.ResolveRelativeArea(args);
            if (area != null)
            {
                var absolute = WorldEditor.ToAbsoluteCell(requestedX.Value, requestedY.Value, area);
                x = absolute.x;
                y = absolute.y;
            }
            else
            {
                x = requestedX.Value;
                y = requestedY.Value;
            }

            ApplyAnchorOffset(args, ref x, ref y);
            return true;
        }

        private static void ApplyAnchorOffset(JObject args, ref int x, ref int y)
        {
            x += ToolUtil.GetInt(args, "offsetX") ?? ToolUtil.GetInt(args, "dx") ?? 0;
            y += ToolUtil.GetInt(args, "offsetY") ?? ToolUtil.GetInt(args, "dy") ?? 0;
        }

        private static Dictionary<string, object> BuildAnchorResolution(JObject args, List<CellCoord> anchors)
        {
            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["search"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["name"]?.ToString();

            int offsetX = ToolUtil.GetInt(args, "offsetX") ?? ToolUtil.GetInt(args, "dx") ?? 0;
            int offsetY = ToolUtil.GetInt(args, "offsetY") ?? ToolUtil.GetInt(args, "dy") ?? 0;
            var first = anchors != null && anchors.Count > 0
                ? new Dictionary<string, object> { ["x"] = anchors[0].x, ["y"] = anchors[0].y }
                : null;

            return new Dictionary<string, object>
            {
                ["mode"] = anchors != null && anchors.Count > 1
                    ? "anchors"
                    : string.IsNullOrWhiteSpace(query) ? "coordinate_or_area" : "search_anchor",
                ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                ["offset"] = new Dictionary<string, int> { ["x"] = offsetX, ["y"] = offsetY },
                ["anchorCount"] = anchors?.Count ?? 0,
                ["firstAnchor"] = first,
                ["oneCallSearchAndAction"] = !string.IsNullOrWhiteSpace(query)
            };
        }

        private static List<CellCoord> ResolveAnchors(JObject args, BuildingDef def, out string error)
        {
            error = null;
            var anchors = ParseAnchorArray(args, out error);
            if (error != null)
                return anchors;
            if (anchors.Count > 0)
                return anchors;

            int x;
            int y;
            if (TryResolvePoint(args, out x, out y, out error))
                return new List<CellCoord> { new CellCoord(x, y) };
            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["search"]?.ToString();
            if (!string.IsNullOrWhiteSpace(query))
            {
                error = $"query '{query}' did not resolve to a visible anchor; provide anchors, x/y, areaId, or x1/y1/x2/y2";
                return anchors;
            }
            error = null;

            bool hasRect = !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || ToolUtil.GetInt(args, "x1").HasValue
                || ToolUtil.GetInt(args, "y1").HasValue
                || ToolUtil.GetInt(args, "x2").HasValue
                || ToolUtil.GetInt(args, "y2").HasValue;
            if (!hasRect)
            {
                error = "anchors, x/y, areaId, or x1/y1/x2/y2 are required";
                return anchors;
            }

            var rect = ToolUtil.GetRect(args);
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            bool dense = ToolUtil.GetBool(args, "dense", width == 1 && height == 1);
            int stepX = Math.Max(1, ToolUtil.GetInt(args, "stepX") ?? (dense ? 1 : width));
            int stepY = Math.Max(1, ToolUtil.GetInt(args, "stepY") ?? (dense ? 1 : height));

            for (int ay = rect["y1"]; ay <= rect["y2"]; ay += stepY)
                for (int ax = rect["x1"]; ax <= rect["x2"]; ax += stepX)
                    anchors.Add(new CellCoord(ax, ay));

            return anchors;
        }

        private static List<CellCoord> ParseAnchorArray(JObject args, out string error)
        {
            error = null;
            var result = new List<CellCoord>();
            var anchors = args["anchors"] as JArray;
            if (anchors == null)
                return result;

            var area = WorldEditor.ResolveRelativeArea(args);
            foreach (var item in anchors)
            {
                int? x = null;
                int? y = null;
                var obj = item as JObject;
                if (obj != null)
                {
                    x = ParseInt(obj["x"]);
                    y = ParseInt(obj["y"]);
                }
                else
                {
                    var pair = item as JArray;
                    if (pair != null && pair.Count >= 2)
                    {
                        x = ParseInt(pair[0]);
                        y = ParseInt(pair[1]);
                    }
                }

                if (!x.HasValue || !y.HasValue)
                {
                    error = "Each anchors item must be {x,y} or [x,y]";
                    return result;
                }

                if (area != null)
                {
                    var absolute = WorldEditor.ToAbsoluteCell(x.Value, y.Value, area);
                    result.Add(new CellCoord(absolute.x, absolute.y));
                }
                else
                {
                    result.Add(new CellCoord(x.Value, y.Value));
                }
            }

            return result;
        }

        private static int? ParseInt(JToken token)
        {
            int value;
            return token != null && int.TryParse(token.ToString(), out value) ? value : (int?)null;
        }
    }
}
