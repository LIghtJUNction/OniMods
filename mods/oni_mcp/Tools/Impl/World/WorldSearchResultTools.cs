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
        private sealed class SearchHit
        {
            public string Kind;
            public string Id;
            public string Name;
            public string PrefabId;
            public string ElementId;
            public int X;
            public int Y;
            public int WorldId;
            public float? MassKg;
            public float? TemperatureC;
            public string State;
            public bool? Solid;
            public bool? Stored;
            public bool? Operational;
            public bool Visible;
            public int Scanned;

            public Dictionary<string, object> ToDictionary(SearchRequest request)
            {
                var result = new Dictionary<string, object>
                {
                    ["kind"] = Kind,
                    ["id"] = Id,
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["elementId"] = ElementId,
                    ["x"] = X,
                    ["y"] = Y,
                    ["worldId"] = WorldId,
                    ["visible"] = Visible
                };
                if (MassKg.HasValue)
                    result["massKg"] = Math.Round(MassKg.Value, 3);
                if (TemperatureC.HasValue)
                    result["temperatureC"] = Math.Round(TemperatureC.Value, 2);
                if (!string.IsNullOrWhiteSpace(State))
                    result["state"] = State;
                if (Solid.HasValue)
                    result["solid"] = Solid.Value;
                if (Stored.HasValue)
                    result["stored"] = Stored.Value;
                if (Operational.HasValue)
                    result["operational"] = Operational.Value;
                if (request.HasNear)
                    result["distance"] = Math.Round(Math.Sqrt(request.DistanceSquared(X, Y)), 2);
                return result;
            }
        }

        private static Dictionary<string, object> BuildResult(string tool, SearchRequest request, IEnumerable<SearchHit> rawHits)
        {
            var hits = rawHits.ToList();
            var result = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["tool"] = tool,
                ["returnMode"] = request.ReturnMode,
                ["query"] = request.Query,
                ["kinds"] = request.Kinds.ToArray(),
                ["worldId"] = request.WorldId,
                ["rect"] = new[] { request.Rect["x1"], request.Rect["y1"], request.Rect["x2"], request.Rect["y2"] },
                ["visibleOnly"] = request.VisibleOnly,
                ["matched"] = hits.Count,
                ["sort"] = request.Sort,
                ["near"] = request.HasNear ? (object)new[] { request.NearX.Value, request.NearY.Value } : null,
                ["summary"] = hits.GroupBy(hit => hit.Kind).ToDictionary(group => group.Key, group => group.Count()),
                ["recommendedFollowUp"] = "Use world_area_snapshot or world_text_map around a cluster bbox when terrain context is needed; use camera_focus_cell for visual inspection."
            };

            if (request.ReturnMode == "summary")
            {
                result["returned"] = 0;
                return result;
            }

            if (request.ReturnMode == "clusters")
            {
                var clusters = BuildClusters(hits, request).Take(request.Limit).ToList();
                result["returned"] = clusters.Count;
                result["clusters"] = clusters;
                return result;
            }

            var sorted = SortHits(hits, request).Take(request.Limit).Select(hit => hit.ToDictionary(request)).ToList();
            result["returned"] = sorted.Count;
            result["items"] = sorted;
            return result;
        }

        private static IEnumerable<Dictionary<string, object>> BuildClusters(List<SearchHit> hits, SearchRequest request)
        {
            foreach (var cluster in BuildCellClusters(hits.Where(hit => hit.Kind == "cell").ToList(), request))
                yield return cluster;
            foreach (var cluster in BuildObjectGroups(hits.Where(hit => hit.Kind != "cell").ToList(), request))
                yield return cluster;
        }

        private static IEnumerable<Dictionary<string, object>> BuildCellClusters(List<SearchHit> cellHits, SearchRequest request)
        {
            var orderedGroups = cellHits
                .GroupBy(hit => $"{hit.WorldId}|{hit.ElementId}|{hit.State}")
                .OrderByDescending(group => group.Count());

            int clusterIndex = 0;
            foreach (var group in orderedGroups)
            {
                var remaining = group.ToDictionary(hit => CellKey(hit.WorldId, hit.X, hit.Y), hit => hit);
                while (remaining.Count > 0)
                {
                    var first = remaining.Values.First();
                    var queue = new Queue<SearchHit>();
                    var members = new List<SearchHit>();
                    remaining.Remove(CellKey(first.WorldId, first.X, first.Y));
                    queue.Enqueue(first);

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        members.Add(current);
                        TryEnqueueNeighbor(current.WorldId, current.X + 1, current.Y, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X - 1, current.Y, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X, current.Y + 1, remaining, queue);
                        TryEnqueueNeighbor(current.WorldId, current.X, current.Y - 1, remaining, queue);
                    }

                    yield return CellClusterInfo($"cell_cluster_{clusterIndex++}", members, request);
                }
            }
        }

        private static IEnumerable<Dictionary<string, object>> BuildObjectGroups(List<SearchHit> objectHits, SearchRequest request)
        {
            int groupIndex = 0;
            foreach (var group in objectHits.GroupBy(hit => $"{hit.WorldId}|{hit.Kind}|{hit.PrefabId}").OrderByDescending(group => group.Count()))
            {
                var members = group.ToList();
                yield return new Dictionary<string, object>
                {
                    ["id"] = $"object_group_{groupIndex++}",
                    ["kind"] = members[0].Kind,
                    ["prefabId"] = members[0].PrefabId,
                    ["name"] = members[0].Name,
                    ["worldId"] = members[0].WorldId,
                    ["bbox"] = BBox(members),
                    ["count"] = members.Count,
                    ["nearestDistance"] = request.HasNear ? (object)Math.Round(Math.Sqrt(members.Min(hit => request.DistanceSquared(hit.X, hit.Y))), 2) : null,
                    ["sampleItems"] = SortHits(members, request).Take(8).Select(hit => hit.ToDictionary(request)).ToList()
                };
            }
        }

        private static Dictionary<string, object> CellClusterInfo(string id, List<SearchHit> members, SearchRequest request)
        {
            float mass = members.Sum(hit => hit.MassKg ?? 0f);
            var temps = members.Where(hit => hit.TemperatureC.HasValue).Select(hit => hit.TemperatureC.Value).ToList();
            var sorted = SortHits(members, request).Take(12).ToList();
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["kind"] = "cell_cluster",
                ["elementId"] = members[0].ElementId,
                ["state"] = members[0].State,
                ["worldId"] = members[0].WorldId,
                ["bbox"] = BBox(members),
                ["cells"] = members.Count,
                ["estimatedMassKg"] = Math.Round(mass, 2),
                ["avgMassKg"] = Math.Round(mass / Math.Max(1, members.Count), 3),
                ["avgTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Average(), 2),
                ["minTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Min(), 2),
                ["maxTemperatureC"] = temps.Count == 0 ? (object)null : Math.Round(temps.Max(), 2),
                ["nearestDistance"] = request.HasNear ? (object)Math.Round(Math.Sqrt(members.Min(hit => request.DistanceSquared(hit.X, hit.Y))), 2) : null,
                ["suggestedCells"] = sorted.Select(hit => new[] { hit.X, hit.Y }).ToList()
            };
        }

        private static int[] BBox(List<SearchHit> members)
        {
            return new[] { members.Min(hit => hit.X), members.Min(hit => hit.Y), members.Max(hit => hit.X), members.Max(hit => hit.Y) };
        }

        private static string CellKey(int worldId, int x, int y)
        {
            return worldId + ":" + x + ":" + y;
        }

        private static void TryEnqueueNeighbor(int worldId, int x, int y, Dictionary<string, SearchHit> remaining, Queue<SearchHit> queue)
        {
            string key = CellKey(worldId, x, y);
            SearchHit hit;
            if (!remaining.TryGetValue(key, out hit))
                return;
            remaining.Remove(key);
            queue.Enqueue(hit);
        }

        private static IEnumerable<SearchHit> SortHits(List<SearchHit> hits, SearchRequest request)
        {
            string sort = request.Sort;
            if (sort == "nearest" && request.HasNear)
                return hits.OrderBy(hit => request.DistanceSquared(hit.X, hit.Y)).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            if (sort == "mass")
                return hits.OrderByDescending(hit => hit.MassKg ?? 0f).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            if (sort == "temperature")
                return hits.OrderByDescending(hit => hit.TemperatureC ?? -9999f).ThenBy(hit => hit.Kind).ThenBy(hit => hit.Name);
            return hits.OrderBy(hit => hit.Kind).ThenBy(hit => hit.Name).ThenBy(hit => hit.X).ThenBy(hit => hit.Y);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && !string.IsNullOrEmpty(query)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
