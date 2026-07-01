using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class ToolUtil
    {
        private static readonly Regex RichTextTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            int start = name.IndexOf('>');
            int end = name.LastIndexOf("</", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                name = name.Substring(start + 1, end - start - 1);

            name = RichTextTagRegex.Replace(name, "");
            name = WhitespaceRegex.Replace(name, " ");
            return name.Trim();
        }

        public static string GetElementState(Element element)
        {
            if (element == null)
                return "unknown";
            if (element.IsVacuum)
                return "vacuum";
            if (element.IsGas)
                return "gas";
            if (element.IsLiquid)
                return "liquid";
            if (element.IsSolid)
                return "solid";
            return "unknown";
        }

        public static int ClampLimit(JObject args, int defaultValue, int max)
        {
            int value;
            if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out value))
                return Math.Max(1, Math.Min(value, max));
            return defaultValue;
        }

        public static bool GetBool(JObject args, string key, bool defaultValue)
        {
            bool value;
            return args[key] != null && bool.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        public static int? GetInt(JObject args, string key)
        {
            int value;
            return args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : (int?)null;
        }

        public static float? GetFloat(JObject args, string key)
        {
            float value;
            return args[key] != null && float.TryParse(args[key].ToString(), out value) ? value : (float?)null;
        }

        public static int ResolveWorldId(JObject args, int fallbackWorldId = -1)
        {
            return WorldEditor.ResolveWorldId(args, fallbackWorldId);
        }

        public static bool CellMatchesWorld(int cell, int worldId)
        {
            if (worldId < 0)
                return true;
            return Grid.IsWorldValidCell(cell) && Grid.WorldIdx[cell] == worldId;
        }

        public static bool GameObjectMatchesWorld(GameObject go, int worldId)
        {
            if (go == null || worldId < 0)
                return true;

            int cell = Grid.PosToCell(go);
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell] == worldId;

            var component = go.GetComponent<KMonoBehaviour>();
            return component == null || component.GetMyWorldId() == worldId;
        }

        public static MinionIdentity FindDupe(JObject args)
        {
            int id;
            if (args["id"] != null && int.TryParse(args["id"].ToString(), out id))
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    var kpid = minion?.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id)
                        return minion;
                }
            }

            string name = args["name"]?.ToString();
            if (!string.IsNullOrEmpty(name))
            {
                foreach (var minion in Components.LiveMinionIdentities.Items)
                {
                    if (minion != null && string.Equals(minion.GetProperName(), name, StringComparison.OrdinalIgnoreCase))
                        return minion;
                }
            }

            return null;
        }

        public static Dictionary<string, int> GetRect(JObject args)
        {
            return WorldEditor.ResolveRect(args);
        }

        public static bool TryResolveSearchCell(JObject args, out int x, out int y, out string error)
        {
            x = 0;
            y = 0;
            error = null;

            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["target"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["search"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
            {
                error = "query/target/search/name is required when x/y are omitted";
                return false;
            }

            int worldId = ResolveWorldId(args);
            int? nearX = GetInt(args, "nearX");
            int? nearY = GetInt(args, "nearY");
            int bestCell = -1;
            int bestScore = int.MinValue;
            int bestDistance = int.MaxValue;

            Action<int, int> considerCell = (cell, score) =>
            {
                if (score <= 0)
                    return;
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                    return;
                if (worldId >= 0 && Grid.WorldIdx[cell] != worldId)
                    return;
                int cx = Grid.CellColumn(cell);
                int cy = Grid.CellRow(cell);
                int distance = nearX.HasValue && nearY.HasValue
                    ? Math.Abs(cx - nearX.Value) + Math.Abs(cy - nearY.Value)
                    : 0;
                if (bestCell < 0 || score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    bestCell = cell;
                    bestScore = score;
                    bestDistance = distance;
                }
            };

            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.gameObject == null)
                    continue;
                if (!GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                var kpid = building.GetComponent<KPrefabID>();
                string prefabId = building.Def?.PrefabID ?? kpid?.PrefabTag.Name;
                considerCell(Grid.PosToCell(building), BestMatchScore(query, prefabId, CleanName(building.GetProperName()), building.name));
            }

            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (!GameObjectMatchesWorld(pickupable.gameObject, worldId))
                    continue;
                var kpid = pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.GetComponent<PrimaryElement>();
                string elementId = primary != null ? primary.ElementID.ToString() : null;
                considerCell(Grid.PosToCell(pickupable), BestMatchScore(query, kpid?.PrefabTag.Name, elementId, CleanName(pickupable.GetProperName()), pickupable.name));
            }

            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null)
                    continue;
                if (!GameObjectMatchesWorld(dupe.gameObject, worldId))
                    continue;
                var kpid = dupe.GetComponent<KPrefabID>();
                considerCell(Grid.PosToCell(dupe), BestMatchScore(query, kpid?.PrefabTag.Name, CleanName(dupe.GetProperName()), dupe.name));
            }

            if (bestCell < 0)
            {
                error = $"No visible object matched query '{query}'";
                return false;
            }

            x = Grid.CellColumn(bestCell);
            y = Grid.CellRow(bestCell);
            return true;
        }

        private static int BestMatchScore(string query, params string[] values)
        {
            int best = 0;
            foreach (string value in values)
                best = Math.Max(best, MatchScore(value, query));
            return best;
        }

        private static int MatchScore(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query))
                return 0;

            string valueTrimmed = value.Trim();
            string queryTrimmed = query.Trim();
            if (string.Equals(valueTrimmed, queryTrimmed, StringComparison.OrdinalIgnoreCase))
                return 1000;

            string valueNorm = NormalizeSearchText(valueTrimmed);
            string queryNorm = NormalizeSearchText(queryTrimmed);
            if (valueNorm.Length == 0 || queryNorm.Length == 0)
                return 0;
            if (valueNorm == queryNorm)
                return 950;
            if (valueNorm.StartsWith(queryNorm, StringComparison.Ordinal))
                return 850;
            if (valueNorm.Contains(queryNorm))
                return 700;

            int maxDistance = queryNorm.Length <= 4 ? 1 : queryNorm.Length <= 8 ? 2 : 3;
            int distance = BoundedEditDistance(valueNorm, queryNorm, maxDistance);
            return distance >= 0 ? 520 - distance * 40 : 0;
        }

        private static string NormalizeSearchText(string value)
        {
            var chars = new List<char>(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }
            return new string(chars.ToArray());
        }

        private static int BoundedEditDistance(string left, string right, int maxDistance)
        {
            if (Math.Abs(left.Length - right.Length) > maxDistance)
                return -1;

            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowMin = Math.Min(rowMin, current[j]);
                }
                if (rowMin > maxDistance)
                    return -1;

                var temp = previous;
                previous = current;
                current = temp;
            }

            return previous[right.Length] <= maxDistance ? previous[right.Length] : -1;
        }

        public static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
