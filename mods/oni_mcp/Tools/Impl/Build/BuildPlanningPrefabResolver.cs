using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static readonly Dictionary<string, string> PrefabAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PneumaticDoor"] = "Door",
            ["Airlock"] = "ManualPressureDoor",
            ["ManualAirlock"] = "ManualPressureDoor",
            ["Tile"] = "Tile",
            ["BasicTile"] = "Tile",
            ["Ladder"] = "Ladder",
            ["Battery"] = "Battery",
            ["ManualGenerator"] = "ManualGenerator",
            ["Outhouse"] = "Outhouse",
            ["WashBasin"] = "WashBasin",
            ["ResearchStation"] = "ResearchCenter"
        };

        private static BuildingDef ResolveBuildingDef(string requested, out string resolvedPrefabId, out string error)
        {
            resolvedPrefabId = requested;
            error = null;
            if (string.IsNullOrWhiteSpace(requested))
            {
                error = "prefabId is required";
                return null;
            }

            string alias;
            if (PrefabAliases.TryGetValue(requested.Trim(), out alias))
                resolvedPrefabId = alias;

            var def = Assets.GetBuildingDef(resolvedPrefabId);
            if (def != null)
                return def;

            var candidates = FindBuildingDefCandidates(requested).Take(5).ToList();
            error = candidates.Count == 0
                ? "Building def not found: " + requested
                : "Building def not found: " + requested + ". Did you mean: " + string.Join(", ", candidates.Select(item => item.PrefabID).ToArray()) + "?";
            return null;
        }

        private static IEnumerable<BuildingDef> FindBuildingDefCandidates(string requested)
        {
            string query = NormalizePrefabSearch(requested);
            if (string.IsNullOrEmpty(query) || Assets.BuildingDefs == null)
                yield break;

            foreach (var def in Assets.BuildingDefs
                .Where(item => item != null && !string.IsNullOrEmpty(item.PrefabID))
                .Select(item => new { Def = item, Score = PrefabMatchScore(item, query) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Def.PrefabID))
            {
                yield return def.Def;
            }
        }

        private static int PrefabMatchScore(BuildingDef def, string query)
        {
            return Math.Max(
                PrefabTextScore(def.PrefabID, query),
                PrefabTextScore(ToolUtil.CleanName(def.Name), query));
        }

        private static int PrefabTextScore(string value, string query)
        {
            string normalized = NormalizePrefabSearch(value);
            if (normalized == query)
                return 1000;
            if (normalized.Contains(query))
                return 700;
            if (query.Contains(normalized))
                return 500;
            return 0;
        }

        private static string NormalizePrefabSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
