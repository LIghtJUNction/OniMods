using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool SearchTokenMatches(string actual, string pattern)
        {
            pattern = (pattern ?? string.Empty).Trim();
            if (pattern == "?" || pattern == "*" || pattern == ".*")
                return true;
            if (pattern.Length >= 2 && pattern[0] == '/' && pattern[pattern.Length - 1] == '/')
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1, pattern.Length - 2));
            if (pattern.StartsWith("~", StringComparison.Ordinal) && pattern.Length > 1)
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1));
            // Map rendering appends @(x,y) on the first cell of a building run.
            // Agents often strip that suffix when copying SEARCH tokens; treat both forms equal.
            string normalizedActual = NormalizeMapCompareToken(actual);
            string normalizedPattern = NormalizeMapCompareToken(pattern);
            return string.Equals(normalizedActual, normalizedPattern, StringComparison.Ordinal)
                || string.Equals(actual, pattern, StringComparison.Ordinal);
        }

        /// <summary>
        /// Strip trailing map coordinate annotations like <c>建筑:7#壹@(114,138)</c> so SEARCH/REPLACE
        /// matching is stable across re-reads and agent-normalized tokens.
        /// </summary>
        private static string NormalizeMapCompareToken(string token)
        {
            token = (token ?? string.Empty).Trim();
            if (token.Length == 0)
                return token;
            int at = token.IndexOf("@(", StringComparison.Ordinal);
            if (at < 0)
            {
                // Also strip bare @Name dupe/critter forms when comparing pure build tokens is not needed;
                // only @(x,y) is stripped here because agents rewrite that suffix most often.
                return token;
            }
            return token.Substring(0, at).TrimEnd();
        }

        private static bool MapTokensEquivalent(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
                return true;
            return string.Equals(NormalizeMapCompareToken(left), NormalizeMapCompareToken(right), StringComparison.Ordinal);
        }

        private static bool ReplacementKeepsOriginal(string token)
        {
            token = (token ?? string.Empty).Trim();
            return token == "?" || token == "*" || token == ".*";
        }

        private static bool TryResolveBuildPrefabFromSymbol(char symbol, out string prefabId)
        {
            prefabId = null;
            foreach (var def in Assets.BuildingDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.PrefabID))
                    continue;
                if (GetUniqueChar(def.PrefabID, def.Name) == symbol)
                {
                    prefabId = def.PrefabID;
                    return true;
                }
            }
            return false;
        }

        private static bool TryResolveBuildPrefabFromToken(string token, char symbol, out string prefabId)
        {
            prefabId = null;
            string name = ExtractBuildTokenName(token);
            if (!string.IsNullOrWhiteSpace(name) && name.Length > 1)
            {
                foreach (var def in Assets.BuildingDefs)
                {
                    if (def == null || string.IsNullOrEmpty(def.PrefabID))
                        continue;
                    string id = MapTokenPart(def.PrefabID);
                    string proper = MapTokenPart(def.Name);
                    if (string.Equals(id, name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(proper, name, StringComparison.OrdinalIgnoreCase)
                        || id.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        prefabId = def.PrefabID;
                        return true;
                    }
                }
            }
            return TryResolveBuildPrefabFromSymbol(symbol, out prefabId);
        }

        private static string ExtractBuildTokenName(string token)
        {
            token = (token ?? string.Empty).Trim();
            int end = token.Length;
            int at = token.IndexOf('@');
            int colon = token.IndexOf(':');
            int hash = token.IndexOf('#');
            if (at >= 0)
                end = Math.Min(end, at);
            if (colon >= 0)
                end = Math.Min(end, colon);
            if (hash >= 0)
                end = Math.Min(end, hash);
            return end <= 0 ? string.Empty : MapTokenPart(token.Substring(0, end));
        }

        private static bool ParseBuildToken(string token, out char buildSymbol, out int? priority, out string material)
        {
            // Drop map annotations like @(x,y) before parsing priority/material.
            token = NormalizeMapCompareToken(token);
            buildSymbol = token.Length > 0 ? token[0] : '?';
            priority = ParsePriority(token);
            material = null;
            int hash = token.IndexOf('#');
            if (hash >= 0 && hash + 1 < token.Length)
            {
                char materialSymbol = token[hash + 1];
                string elementId;
                material = TryResolveElementFromSymbol(materialSymbol, out elementId) ? elementId : materialSymbol.ToString();
            }
            return !string.IsNullOrWhiteSpace(token);
        }

        private static int? ParsePriority(string token)
        {
            token = NormalizeMapCompareToken(token);
            int colon = (token ?? string.Empty).IndexOf(':');
            if (colon < 0)
                return null;
            int end = token.IndexOf('#', colon + 1);
            if (end < 0)
                end = token.Length;
            int parsed;
            return int.TryParse(token.Substring(colon + 1, end - colon - 1), out parsed)
                ? Math.Max(1, Math.Min(parsed, 9))
                : (int?)null;
        }

        private static bool TryResolveElementFromSymbol(char symbol, out string elementId)
        {
            foreach (var item in UniqueCharMap)
            {
                if (item.Value != symbol)
                    continue;
                SimHashes hash;
                if (Enum.TryParse(item.Key, out hash))
                {
                    elementId = item.Key;
                    return true;
                }
            }
            elementId = null;
            return false;
        }

        private static Tuple<int, int, int, int> Bounds(IEnumerable<MapEditCell> cells)
        {
            return Tuple.Create(cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X), cells.Max(c => c.Y));
        }

        private static JObject RectObject(Tuple<int, int, int, int> bounds)
        {
            return new JObject { ["x1"] = bounds.Item1, ["y1"] = bounds.Item2, ["x2"] = bounds.Item3, ["y2"] = bounds.Item4 };
        }
    }
}
