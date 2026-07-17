using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldSearchTools
    {
        private sealed class SearchRequest
        {
            public string Query;
            public HashSet<string> Kinds;
            public Dictionary<string, int> Rect;
            public int WorldId;
            public bool VisibleOnly;
            public int Limit;
            public string Sort;
            public int? NearX;
            public int? NearY;
            public string ReturnMode;
            public string State;
            public bool? Solid;
            public float? MinMassKg;
            public float? MaxMassKg;
            public float? MinTempC;
            public float? MaxTempC;
            public string Pattern;
            public string PatternDirection;
            public string MatchMode;

            public bool HasNear => NearX.HasValue && NearY.HasValue;
            public bool HasPattern => !string.IsNullOrWhiteSpace(Pattern);

            public static SearchRequest From(JObject args, string defaultReturnMode)
            {
                int maxCells = Math.Max(100, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 2500, MaxSearchCells));
                var rect = WorldEditor.ResolveRectOrCamera(args, maxCells);
                var request = new SearchRequest
                {
                    Query = args["query"]?.ToString()?.Trim(),
                    Kinds = ParseKinds(args["kinds"]),
                    Rect = rect,
                    WorldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? 0),
                    VisibleOnly = ToolUtil.GetBool(args, "visibleOnly", true),
                    Limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 300)),
                    Sort = NormalizeSort(args["sort"]?.ToString(), ToolUtil.GetInt(args, "nearX").HasValue && ToolUtil.GetInt(args, "nearY").HasValue),
                    NearX = ToolUtil.GetInt(args, "nearX"),
                    NearY = ToolUtil.GetInt(args, "nearY"),
                    ReturnMode = NormalizeReturnMode(args["returnMode"]?.ToString(), defaultReturnMode),
                    State = NormalizeState(args["state"]?.ToString()),
                Solid = args["solid"] == null ? (bool?)null : ToolUtil.GetBool(args, "solid", false),
                MinMassKg = ToolUtil.GetFloat(args, "minMassKg"),
                MaxMassKg = ToolUtil.GetFloat(args, "maxMassKg"),
                MinTempC = ToolUtil.GetFloat(args, "minTempC"),
                MaxTempC = ToolUtil.GetFloat(args, "maxTempC"),
                Pattern = FirstNonEmpty(args["pattern"], args["sequence"]),
                PatternDirection = NormalizePatternDirection(args["direction"]?.ToString()),
                MatchMode = NormalizeMatchMode(args["matchMode"]?.ToString())
            };
                return request;
            }

            public IEnumerable<int> Cells()
            {
                for (int y = Rect["y1"]; y <= Rect["y2"]; y++)
                {
                    for (int x = Rect["x1"]; x <= Rect["x2"]; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (MatchesCell(cell))
                            yield return cell;
                    }
                }
            }

            public bool IncludeKind(string kind)
            {
                return Kinds.Contains("all") || Kinds.Contains(kind);
            }

            public bool MatchesQuery(params string[] values)
            {
                if (string.IsNullOrWhiteSpace(Query))
                    return true;
                return values.Any(value => Contains(value, Query));
            }

            public bool MatchesState(string state)
            {
                return State == "any" || string.Equals(State, state, StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesCell(int cell, int fallbackWorldId = -1)
            {
                int world = fallbackWorldId;
                if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                    world = Grid.WorldIdx[cell];
                if (WorldId >= 0 && world >= 0 && world != WorldId)
                    return false;
                if (Grid.IsValidCell(cell))
                {
                    int x = Grid.CellColumn(cell);
                    int y = Grid.CellRow(cell);
                    if (x < Rect["x1"] || x > Rect["x2"] || y < Rect["y1"] || y > Rect["y2"])
                        return false;
                    if (VisibleOnly && !Grid.IsVisible(cell))
                        return false;
                }
                return true;
            }

            public double DistanceSquared(int x, int y)
            {
                if (!HasNear || x < 0 || y < 0)
                    return double.MaxValue;
                double dx = x - NearX.Value;
                double dy = y - NearY.Value;
                return dx * dx + dy * dy;
            }

            private static HashSet<string> ParseKinds(JToken value)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (value == null || value.Type == JTokenType.Null)
                {
                    result.Add("all");
                    return result;
                }
                if (value.Type == JTokenType.Array)
                {
                    foreach (var item in value.Children())
                        AddKind(result, item?.ToString());
                }
                else
                {
                    foreach (string item in value.ToString().Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddKind(result, item);
                }
                if (result.Count == 0)
                    result.Add("all");
                return result;
            }

            private static void AddKind(HashSet<string> result, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                string kind = value.Trim().ToLowerInvariant();
                if (kind == "element")
                    kind = "elements";
                if (kind == "resource")
                    kind = "resources";
                if (kind == "duplicant")
                    kind = "dupes";
                result.Add(kind);
            }

            private static string NormalizeSort(string value, bool hasNear)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return hasNear ? "nearest" : "kind";
                string sort = value.Trim().ToLowerInvariant();
                return sort == "nearest" || sort == "mass" || sort == "temperature" ? sort : "kind";
            }

            private static string NormalizeReturnMode(string value, string defaultReturnMode)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return defaultReturnMode == "hits" || defaultReturnMode == "summary" ? defaultReturnMode : "clusters";
                string mode = value.Trim().ToLowerInvariant();
                return mode == "hits" || mode == "summary" || mode == "clusters" ? mode : "clusters";
            }

            private static string NormalizeState(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "any";
                string state = value.Trim().ToLowerInvariant();
                return state == "gas" || state == "liquid" || state == "solid" || state == "vacuum" ? state : "any";
            }

            private static string FirstNonEmpty(params JToken[] values)
            {
                foreach (var value in values)
                {
                    string text = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
                return null;
            }

            private static string NormalizePatternDirection(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "both";
                string direction = value.Trim().ToLowerInvariant();
                if (direction == "horizontal" || direction == "x" || direction == "row")
                    return "horizontal";
                if (direction == "vertical" || direction == "y" || direction == "column")
                    return "vertical";
                return "both";
            }

            private static string NormalizeMatchMode(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return "smart";
                string mode = value.Trim().ToLowerInvariant();
                return mode == "exact" || mode == "fuzzy" ? mode : "smart";
            }
        }
    }
}
