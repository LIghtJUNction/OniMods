using System;
using System.Collections.Generic;
using System.Globalization;
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
            // '?' is unknown data and '*' is a disconnected utility segment.
            // Only the explicit wildcard may match a different map token.
            if (pattern == ".*")
                return true;
            if (pattern.Length >= 2 && pattern[0] == '/' && pattern[pattern.Length - 1] == '/')
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1, pattern.Length - 2), RegexOptions.None, RegexMatchTimeout);
            if (pattern.StartsWith("~", StringComparison.Ordinal) && pattern.Length > 1)
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1), RegexOptions.None, RegexMatchTimeout);
            return MapTokensEquivalent(actual, pattern);
        }

        private static string NormalizeMapCompareToken(string token)
        {
            token = (token ?? string.Empty).Trim();
            Match annotation = Regex.Match(token, @"@\((-?[0-9]+),(-?[0-9]+)\)$",
                RegexOptions.CultureInvariant, RegexMatchTimeout);
            if (!annotation.Success
                || !int.TryParse(annotation.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                || !int.TryParse(annotation.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                return token;
            return token.Substring(0, annotation.Index).TrimEnd();
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
                if (def == null || string.IsNullOrEmpty(def.PrefabID)
                    || GetUniqueChar(def.PrefabID, def.Name) != symbol)
                    continue;
                if (prefabId != null && !string.Equals(prefabId, def.PrefabID, StringComparison.OrdinalIgnoreCase))
                {
                    prefabId = null;
                    return false;
                }
                prefabId = def.PrefabID;
            }
            return prefabId != null;
        }

        private static bool TryResolveBuildPrefabFromToken(string token, char symbol, out string prefabId)
        {
            prefabId = null;
            string name = ExtractBuildTokenName(token);
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Stable IDs take precedence over localized names and generated glyphs.
            foreach (var def in Assets.BuildingDefs)
            {
                if (def != null && !string.IsNullOrEmpty(def.PrefabID)
                    && string.Equals(def.PrefabID, name, StringComparison.OrdinalIgnoreCase))
                {
                    prefabId = def.PrefabID;
                    return true;
                }
            }

            foreach (var def in Assets.BuildingDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.PrefabID)
                    || !string.Equals(MapTokenPart(def.Name), name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (prefabId != null && !string.Equals(prefabId, def.PrefabID, StringComparison.OrdinalIgnoreCase))
                {
                    prefabId = null;
                    return false;
                }
                prefabId = def.PrefabID;
            }
            if (prefabId != null)
                return true;
            return name.Length == 1 && TryResolveBuildPrefabFromSymbol(symbol, out prefabId);
        }

        private static string ExtractBuildTokenName(string token)
        {
            token = NormalizeMapCompareToken(token);
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
            token = NormalizeMapCompareToken(token);
            buildSymbol = token.Length > 0 ? token[0] : '?';
            priority = null;
            material = null;
            if (token.Length == 0 || token.IndexOf('@') >= 0 || string.IsNullOrWhiteSpace(ExtractBuildTokenName(token)))
                return false;

            int colon = token.IndexOf(':');
            int hash = token.IndexOf('#');
            if ((colon >= 0 && token.IndexOf(':', colon + 1) >= 0)
                || (hash >= 0 && token.IndexOf('#', hash + 1) >= 0)
                || (colon >= 0 && hash >= 0 && hash < colon))
                return false;
            if (colon >= 0)
            {
                priority = ParsePriority(token);
                if (!priority.HasValue)
                    return false;
            }
            if (hash >= 0)
            {
                string requested = token.Substring(hash + 1).Trim();
                if (requested.Length == 0)
                    return false;
                if (requested.Length == 1)
                {
                    if (!TryResolveElementFromSymbol(requested[0], out material))
                        return false;
                }
                else
                {
                    // Preserve the complete ID/category for the native material planner.
                    material = requested;
                }
            }
            return true;
        }

        private static int? ParsePriority(string token)
        {
            token = NormalizeMapCompareToken(token);
            int colon = token.IndexOf(':');
            if (colon < 0)
                return null;
            int end = token.IndexOf('#', colon + 1);
            if (end < 0)
                end = token.Length;
            int parsed;
            return int.TryParse(token.Substring(colon + 1, end - colon - 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out parsed) && parsed >= 1 && parsed <= 9
                ? parsed : (int?)null;
        }

        private static bool TryResolveElementFromSymbol(char symbol, out string elementId)
        {
            elementId = null;
            foreach (var item in UniqueCharMap)
            {
                if (item.Value != symbol)
                    continue;
                SimHashes hash;
                if (!Enum.TryParse(item.Key, out hash) || !Enum.IsDefined(typeof(SimHashes), hash))
                    continue;
                if (elementId != null && !string.Equals(elementId, item.Key, StringComparison.Ordinal))
                {
                    elementId = null;
                    return false;
                }
                elementId = item.Key;
            }
            return elementId != null;
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
