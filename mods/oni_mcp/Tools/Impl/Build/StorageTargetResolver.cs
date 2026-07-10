using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class StorageTools
    {
        private static bool TryFindStorage(JObject args, out StorageInfo target, out string error)
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

            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["name"]?.ToString();
            query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

            bool hasStrongSelector = id.HasValue || cell.HasValue;
            if (!hasStrongSelector && string.IsNullOrEmpty(query))
            {
                error = "id, x/y, query, or name is required";
                return false;
            }

            int? explicitWorldId = ToolUtil.GetInt(args, "worldId");
            int worldId = explicitWorldId ?? (hasStrongSelector ? -1 : ToolUtil.ResolveWorldId(args));
            var candidates = GetStorageBuildings()
                .Where(item => worldId < 0 || item.WorldId == worldId)
                .ToList();
            if (hasStrongSelector)
            {
                var selected = candidates
                    .Where(item => !id.HasValue || item.Id == id.Value)
                    .Where(item => !cell.HasValue || item.Cell == cell.Value)
                    .ToList();
                if (selected.Count == 0)
                {
                    error = id.HasValue && cell.HasValue
                        ? "id and x/y selectors do not resolve to the same storage building"
                        : "Storage building not found for the supplied selector";
                    return false;
                }
                if (selected.Count > 1)
                {
                    error = "Storage selector is ambiguous. Candidates: " + StorageTargetHints(selected);
                    return false;
                }
                if (!string.IsNullOrEmpty(query) && !selected[0].MatchesIdentity(query))
                {
                    error = "query/name does not match the storage building selected by id/x/y";
                    return false;
                }

                target = selected[0];
                return true;
            }

            var matches = candidates.Where(item => item.MatchesIdentity(query)).ToList();
            if (matches.Count == 0)
            {
                error = "Storage building not found for query in world " + worldId;
                return false;
            }
            if (matches.Count > 1)
            {
                error = "Storage query is ambiguous. Candidates: " + StorageTargetHints(matches);
                return false;
            }

            target = matches[0];
            return true;
        }

        private static string StorageTargetHints(IEnumerable<StorageInfo> candidates)
        {
            return string.Join("; ", candidates.Take(5).Select(item =>
                $"id={item.Id}, name={item.Name}, prefabId={item.PrefabId}, x={item.X}, y={item.Y}"));
        }
    }
}
