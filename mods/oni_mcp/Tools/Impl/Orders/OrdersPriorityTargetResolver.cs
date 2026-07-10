using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        private static bool TryFindPriorityTarget(JObject args, out GameObject target, out string error)
        {
            target = null;
            error = null;
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            if (x.HasValue != y.HasValue)
            {
                error = "x and y must be provided together";
                return false;
            }

            int? cell = x.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            if (cell.HasValue && !Grid.IsValidCell(cell.Value))
            {
                error = "x/y does not resolve to a valid cell";
                return false;
            }

            string query = FirstNonEmpty(args, "query", "target", "search");
            bool hasStrongSelector = id.HasValue || cell.HasValue;
            if (!hasStrongSelector && string.IsNullOrEmpty(query))
            {
                error = "id, x/y, or query is required";
                return false;
            }

            int? explicitWorldId = ToolUtil.GetInt(args, "worldId");
            int worldId = explicitWorldId ?? (hasStrongSelector ? -1 : ToolUtil.ResolveWorldId(args));
            var candidates = PriorityTargetCandidates(worldId);
            if (hasStrongSelector)
            {
                var selected = candidates
                    .Where(go => !id.HasValue || PriorityTargetId(go) == id.Value)
                    .Where(go => !cell.HasValue || Grid.PosToCell(go) == cell.Value)
                    .ToList();
                if (selected.Count == 0)
                {
                    error = id.HasValue && cell.HasValue
                        ? "id and x/y selectors do not resolve to the same target"
                        : "Target not found for the supplied selector";
                    return false;
                }
                if (selected.Count > 1)
                {
                    error = "Target selector is ambiguous. Candidates: " + PriorityTargetHints(selected);
                    return false;
                }
                if (!string.IsNullOrEmpty(query) && !MatchesQuery(selected[0], query))
                {
                    error = "query does not match the target selected by id/x/y";
                    return false;
                }

                target = selected[0];
                return true;
            }

            var matches = candidates.Where(go => MatchesQuery(go, query)).ToList();
            if (matches.Count == 0)
            {
                error = "Target not found for query in world " + worldId;
                return false;
            }
            if (matches.Count > 1)
            {
                error = "Target query is ambiguous. Candidates: " + PriorityTargetHints(matches);
                return false;
            }

            target = matches[0];
            return true;
        }

        private static List<GameObject> PriorityTargetCandidates(int worldId)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<int>();
            Action<GameObject> add = go =>
            {
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId) || !seen.Add(go.GetInstanceID()))
                    return;
                result.Add(go);
            };

            foreach (var prioritizable in Components.Prioritizables.Items)
                add(prioritizable?.gameObject);
            foreach (var building in Components.BuildingCompletes.Items)
                add(building?.gameObject);
            return result.OrderBy(PriorityTargetId).ToList();
        }

        private static int PriorityTargetId(GameObject go)
        {
            return go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID();
        }

        private static string PriorityTargetHints(IEnumerable<GameObject> candidates)
        {
            return string.Join("; ", candidates.Take(5).Select(go =>
            {
                int cell = Grid.PosToCell(go);
                int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
                int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
                string prefabId = go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                return $"id={PriorityTargetId(go)}, name={ToolUtil.CleanName(go.GetProperName())}, prefabId={prefabId}, x={x}, y={y}";
            }));
        }

        private static string FirstNonEmpty(JObject args, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = args[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }
    }
}
