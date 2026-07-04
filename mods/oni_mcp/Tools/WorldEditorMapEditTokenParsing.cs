using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
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
            token = (token ?? string.Empty).Trim();
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
