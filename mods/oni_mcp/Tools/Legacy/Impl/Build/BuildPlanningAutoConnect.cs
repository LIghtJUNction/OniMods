using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static List<CellCoord> ResolveUtilityPath(JObject args, int maxCells, out string error)
        {
            error = null;
            var points = ParsePathPoints(args["points"]);
            if (points.Count == 0)
            {
                int? fromX = ToolUtil.GetInt(args, "fromX");
                int? fromY = ToolUtil.GetInt(args, "fromY");
                int? toX = ToolUtil.GetInt(args, "toX");
                int? toY = ToolUtil.GetInt(args, "toY");
                if (!fromX.HasValue || !fromY.HasValue || !toX.HasValue || !toY.HasValue)
                {
                    if (!TryResolveAutoUtilityEndpoints(args, out points, out error))
                        return new List<CellCoord>();
                }
                else
                {
                    points.Add(new CellCoord(fromX.Value, fromY.Value));
                    points.Add(new CellCoord(toX.Value, toY.Value));
                }
            }

            if (points.Count < 2)
            {
                error = "At least two path points are required";
                return new List<CellCoord>();
            }

            var path = new List<CellCoord>();
            for (int i = 0; i < points.Count - 1; i++)
            {
                if (!AddManhattanSegment(path, points[i], points[i + 1], maxCells, out error))
                    return path;
            }
            return path;
        }

        private static bool TryResolveAutoUtilityEndpoints(JObject args, out List<CellCoord> points, out string error)
        {
            points = new List<CellCoord>();
            error = null;

            string prefabId = args["prefabId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(prefabId) && prefabId.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = "Provide either points or fromX/fromY/toX/toY for non-wire utility auto_connect";
                return false;
            }

            int worldId = ToolUtil.ResolveWorldId(args);
            string toQuery = FirstNonEmpty(args["toQuery"], args["targetQuery"], args["query"], args["target"], args["search"], args["name"]);
            if (string.IsNullOrWhiteSpace(toQuery))
            {
                error = "Provide points, fromX/fromY/toX/toY, or query/toQuery for one-call wire auto_connect";
                return false;
            }

            int toCell;
            string toError;
            if (!TryResolvePowerEndpointCell(toQuery, worldId, preferOutput: false, out toCell, out toError))
            {
                error = toError;
                return false;
            }

            int fromCell;
            string fromQuery = FirstNonEmpty(args["fromQuery"], args["sourceQuery"], args["source"], args["from"]);
            if (!string.IsNullOrWhiteSpace(fromQuery))
            {
                string fromError;
                if (!TryResolvePowerEndpointCell(fromQuery, worldId, preferOutput: true, out fromCell, out fromError))
                {
                    error = fromError;
                    return false;
                }
            }
            else if (!TryFindNearestWireOrPowerOutput(toCell, worldId, ToolUtil.GetInt(args, "maxAutoConnectRadius") ?? 80, out fromCell))
            {
                error = $"No connected power output found near '{toQuery}'. Provide fromQuery/fromX/fromY for a powered source.";
                return false;
            }

            points.Add(CellCoordFromCell(fromCell));
            points.Add(CellCoordFromCell(toCell));
            return true;
        }

        private static bool TryResolvePowerEndpointCell(string query, int worldId, bool preferOutput, out int cell, out string error)
        {
            cell = Grid.InvalidCell;
            error = null;
            GameObject best = null;
            int bestScore = int.MinValue;

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;

                var def = building.Def;
                if (def == null || (!def.RequiresPowerInput && !def.RequiresPowerOutput))
                    continue;

                int score = PowerEndpointScore(building.gameObject, def, query, preferOutput);
                if (score > bestScore)
                {
                    best = building.gameObject;
                    bestScore = score;
                }
            }

            if (best == null || bestScore <= 0)
            {
                error = $"No built power endpoint instance matched '{query}'. Use read_control domain=buildings action=list query={query} to inspect existing targets, or provide explicit fromX/fromY/toX/toY.";
                return false;
            }

            var bestBuilding = best.GetComponent<Building>();
            var bestDef = bestBuilding?.Def;
            if (bestBuilding == null || bestDef == null)
            {
                error = $"Matched '{query}' but could not inspect building ports";
                return false;
            }

            int preferred = preferOutput && bestDef.RequiresPowerOutput
                ? bestBuilding.GetPowerOutputCell()
                : bestBuilding.GetPowerInputCell();
            int fallback = preferOutput ? bestBuilding.GetPowerInputCell() : bestBuilding.GetPowerOutputCell();
            cell = Grid.IsValidCell(preferred) ? preferred : fallback;
            if (!Grid.IsValidCell(cell))
            {
                error = $"Matched '{query}' but it has no valid power port cell";
                return false;
            }
            return true;
        }

        private static int PowerEndpointScore(GameObject go, BuildingDef def, string query, bool preferOutput)
        {
            if (go == null || def == null || string.IsNullOrWhiteSpace(query))
                return 0;

            int score = 0;
            var kpid = go.GetComponent<KPrefabID>();
            score = Math.Max(score, SimpleMatchScore(def.PrefabID, query));
            score = Math.Max(score, SimpleMatchScore(kpid?.PrefabTag.Name, query));
            score = Math.Max(score, SimpleMatchScore(ToolUtil.CleanName(go.GetProperName()), query));
            score = Math.Max(score, SimpleMatchScore(go.name, query));
            if (score > 0)
            {
                if (preferOutput && def.RequiresPowerOutput)
                    score += 40;
                if (!preferOutput && def.RequiresPowerInput)
                    score += 40;
            }
            return score;
        }

        private static int SimpleMatchScore(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
                return 0;
            if (string.Equals(value.Trim(), query.Trim(), StringComparison.OrdinalIgnoreCase))
                return 1000;
            if (value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return 700;

            string normalizedValue = NormalizeBuildSearchText(value);
            string normalizedQuery = NormalizeBuildSearchText(query);
            if (normalizedValue == normalizedQuery)
                return 950;
            return normalizedValue.Contains(normalizedQuery) ? 650 : 0;
        }

        private static string NormalizeBuildSearchText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var chars = new List<char>(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }
            return new string(chars.ToArray());
        }

        private static bool TryFindNearestWireOrPowerOutput(int targetCell, int worldId, int radius, out int cell)
        {
            cell = Grid.InvalidCell;
            if (!Grid.IsValidCell(targetCell))
                return false;

            radius = Math.Max(1, Math.Min(radius, 200));
            int targetX = Grid.CellColumn(targetCell);
            int targetY = Grid.CellRow(targetCell);
            int bestDistance = int.MaxValue;

            foreach (var layer in UtilityLayersForPrefab("Wire"))
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int distance = Math.Abs(dx) + Math.Abs(dy);
                        if (distance > radius || distance >= bestDistance)
                            continue;

                        int candidate = Grid.XYToCell(targetX + dx, targetY + dy);
                        if (!Grid.IsValidCell(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                            continue;

                        var wire = Grid.Objects[candidate, (int)layer];
                        if (wire == null)
                            continue;

                        var kpid = wire.GetComponent<KPrefabID>();
                        string prefabName = kpid?.PrefabTag.Name;
                        bool isWire = prefabName != null && prefabName.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isWire)
                            continue;

                        bestDistance = distance;
                        cell = candidate;
                    }
                }
            }

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.Def == null || !building.Def.RequiresPowerOutput)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                if (!TryGetConnectedCircuitId(building.gameObject, out _))
                    continue;

                int candidate = building.GetPowerOutputCell();
                if (!Grid.IsValidCell(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                int x = Grid.CellColumn(candidate);
                int y = Grid.CellRow(candidate);
                int distance = Math.Abs(x - targetX) + Math.Abs(y - targetY);
                if (distance > radius)
                    continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    cell = candidate;
                }
            }

            return Grid.IsValidCell(cell);
        }

        private static bool TryGetConnectedCircuitId(GameObject go, out ushort circuitId)
        {
            circuitId = ushort.MaxValue;
            if (go == null)
                return false;

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null)
                    continue;
                var type = component.GetType();
                var property = type.GetProperty("CircuitID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (TryReadCircuitId(property == null ? null : property.GetValue(component, null), out circuitId))
                    return true;
                var field = type.GetField("CircuitID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (TryReadCircuitId(field == null ? null : field.GetValue(component), out circuitId))
                    return true;
            }
            return false;
        }

        private static bool TryReadCircuitId(object value, out ushort circuitId)
        {
            circuitId = ushort.MaxValue;
            if (value == null)
                return false;
            try
            {
                circuitId = Convert.ToUInt16(value);
                return circuitId != ushort.MaxValue;
            }
            catch
            {
                return false;
            }
        }

        private static CellCoord CellCoordFromCell(int cell)
        {
            return new CellCoord(Grid.CellColumn(cell), Grid.CellRow(cell));
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

        private static bool TryBuildAutoConnectArgs(JObject args, string prefabId, out JObject connectArgs)
        {
            connectArgs = args == null ? new JObject() : (JObject)args.DeepClone();
            connectArgs["prefabId"] = prefabId;
            connectArgs["action"] = "auto_connect";

            if (connectArgs["points"] != null)
                return true;

            var anchors = args?["anchors"] as JArray;
            if (anchors != null && anchors.Count >= 2)
            {
                connectArgs["points"] = anchors.DeepClone();
                return true;
            }

            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? x1 = ToolUtil.GetInt(args, "x1");
            int? y1 = ToolUtil.GetInt(args, "y1");
            int? x2 = ToolUtil.GetInt(args, "x2");
            int? y2 = ToolUtil.GetInt(args, "y2");

            if (x.HasValue && y.HasValue && x2.HasValue && y2.HasValue)
            {
                connectArgs["fromX"] = x.Value;
                connectArgs["fromY"] = y.Value;
                connectArgs["toX"] = x2.Value;
                connectArgs["toY"] = y2.Value;
                return true;
            }

            if (x1.HasValue && y1.HasValue && x2.HasValue && y2.HasValue)
            {
                connectArgs["fromX"] = x1.Value;
                connectArgs["fromY"] = y1.Value;
                connectArgs["toX"] = x2.Value;
                connectArgs["toY"] = y2.Value;
                return true;
            }

            if (HasAutoConnectQuery(args))
                return true;

            return false;
        }

        private static bool HasAutoConnectQuery(JObject args)
        {
            return !string.IsNullOrWhiteSpace(FirstNonEmpty(
                args?["toQuery"],
                args?["targetQuery"],
                args?["fromQuery"],
                args?["sourceQuery"],
                args?["query"],
                args?["target"],
                args?["search"],
                args?["name"]));
        }

        private static List<CellCoord> ParsePathPoints(JToken token)
        {
            var result = new List<CellCoord>();
            var array = token as JArray;
            if (array == null)
                return result;

            foreach (var item in array)
            {
                int? x = null;
                int? y = null;
                var pair = item as JArray;
                if (pair != null && pair.Count >= 2)
                {
                    int parsedX;
                    int parsedY;
                    if (int.TryParse(pair[0]?.ToString(), out parsedX) && int.TryParse(pair[1]?.ToString(), out parsedY))
                    {
                        x = parsedX;
                        y = parsedY;
                    }
                }
                else
                {
                    var obj = item as JObject;
                    if (obj != null)
                    {
                        int parsedX;
                        int parsedY;
                        if (int.TryParse(obj["x"]?.ToString(), out parsedX) && int.TryParse(obj["y"]?.ToString(), out parsedY))
                        {
                            x = parsedX;
                            y = parsedY;
                        }
                    }
                }

                if (x.HasValue && y.HasValue)
                    result.Add(new CellCoord(x.Value, y.Value));
            }
            return result;
        }

        private static bool AddManhattanSegment(List<CellCoord> path, CellCoord from, CellCoord to, int maxCells, out string error)
        {
            error = null;

            long dx = Math.Abs((long)to.x - from.x);
            long dy = Math.Abs((long)to.y - from.y);
            long cellsToAdd = dx + dy + 1;
            if (path.Count > 0 && path[path.Count - 1].x == from.x && path[path.Count - 1].y == from.y)
                cellsToAdd--;

            long resultingCount = (long)path.Count + cellsToAdd;
            if (resultingCount > maxCells)
            {
                error = $"Path too large: {resultingCount} cells, maxCells={maxCells}";
                return false;
            }

            int x = from.x;
            int y = from.y;
            AddPathPoint(path, x, y);
            while (x != to.x)
            {
                x += to.x > x ? 1 : -1;
                AddPathPoint(path, x, y);
            }
            while (y != to.y)
            {
                y += to.y > y ? 1 : -1;
                AddPathPoint(path, x, y);
            }
            return true;
        }

        private static void AddPathPoint(List<CellCoord> path, int x, int y)
        {
            if (path.Count > 0 && path[path.Count - 1].x == x && path[path.Count - 1].y == y)
                return;
            path.Add(new CellCoord(x, y));
        }

        private static List<Dictionary<string, object>> BuildPathSegments(List<CellCoord> path)
        {
            var segments = new List<Dictionary<string, object>>();
            if (path == null || path.Count == 0)
                return segments;

            CellCoord start = path[0];
            CellCoord previous = path[0];
            int dx = 0;
            int dy = 0;

            for (int i = 1; i < path.Count; i++)
            {
                var current = path[i];
                int nextDx = Math.Sign(current.x - previous.x);
                int nextDy = Math.Sign(current.y - previous.y);
                if (i > 1 && (nextDx != dx || nextDy != dy))
                {
                    segments.Add(PathSegment(start, previous));
                    start = previous;
                }

                dx = nextDx;
                dy = nextDy;
                previous = current;
            }

            segments.Add(PathSegment(start, previous));
            return segments;
        }

        private static Dictionary<string, object> PathSegment(CellCoord start, CellCoord end)
        {
            return new Dictionary<string, object>
            {
                ["from"] = new { x = start.x, y = start.y },
                ["to"] = new { x = end.x, y = end.y },
                ["length"] = Math.Abs(end.x - start.x) + Math.Abs(end.y - start.y) + 1
            };
        }
    }
}
