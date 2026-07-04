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
        private static Dictionary<string, object> BuildPatternResult(SearchRequest request)
        {
            var terms = ParsePatternTerms(request.Pattern).ToList();
            var matches = new List<Dictionary<string, object>>();
            int scannedStarts = 0;

            if (terms.Count == 0)
            {
                return new Dictionary<string, object>
                {
                    ["v"] = 1,
                    ["tool"] = "world_search",
                    ["returnMode"] = "pattern",
                    ["pattern"] = request.Pattern,
                    ["error"] = "pattern/sequence did not contain any terms"
                };
            }

            var directions = PatternDirections(request.PatternDirection).ToList();
            for (int y = request.Rect["y1"]; y <= request.Rect["y2"]; y++)
            {
                for (int x = request.Rect["x1"]; x <= request.Rect["x2"]; x++)
                {
                    foreach (var direction in directions)
                    {
                        scannedStarts++;
                        Dictionary<string, object> match;
                        if (TryMatchPatternAt(request, terms, x, y, direction.dx, direction.dy, out match))
                        {
                            matches.Add(match);
                            if (matches.Count >= request.Limit)
                                return PatternResultDictionary(request, terms, scannedStarts, matches, truncated: true);
                        }
                    }
                }
            }

            return PatternResultDictionary(request, terms, scannedStarts, matches, truncated: false);
        }

        private static Dictionary<string, object> PatternResultDictionary(SearchRequest request, List<string> terms, int scannedStarts, List<Dictionary<string, object>> matches, bool truncated)
        {
            return new Dictionary<string, object>
            {
                ["v"] = 1,
                ["tool"] = "world_search",
                ["returnMode"] = "pattern",
                ["pattern"] = request.Pattern,
                ["terms"] = terms,
                ["direction"] = request.PatternDirection,
                ["matchMode"] = request.MatchMode,
                ["worldId"] = request.WorldId,
                ["rect"] = new[] { request.Rect["x1"], request.Rect["y1"], request.Rect["x2"], request.Rect["y2"] },
                ["visibleOnly"] = request.VisibleOnly,
                ["scannedStarts"] = scannedStarts,
                ["matched"] = matches.Count,
                ["returned"] = matches.Count,
                ["truncated"] = truncated,
                ["items"] = matches,
                ["syntax"] = "separators: -, >, ->, comma, slash, whitespace; term: element/state/id/name; wildcard: * or ?; alternatives: A|B; repeat: term{N}; matchMode: exact/smart/fuzzy; direction scans forward and reverse."
            };
        }

        private static bool TryMatchPatternAt(SearchRequest request, List<string> terms, int startX, int startY, int dx, int dy, out Dictionary<string, object> match)
        {
            match = null;
            var cells = new List<Dictionary<string, object>>();
            for (int i = 0; i < terms.Count; i++)
            {
                int x = startX + dx * i;
                int y = startY + dy * i;
                if (x < request.Rect["x1"] || x > request.Rect["x2"] || y < request.Rect["y1"] || y > request.Rect["y2"])
                    return false;
                int cell = Grid.XYToCell(x, y);
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                    return false;
                if (request.WorldId >= 0 && Grid.WorldIdx[cell] != request.WorldId)
                    return false;
                if (request.VisibleOnly && !Grid.IsVisible(cell))
                    return false;

                var element = Grid.Element[cell];
                string elementId = element?.id.ToString() ?? "Unknown";
                string elementName = ToolUtil.CleanName(element?.name ?? elementId);
                string state = ToolUtil.GetElementState(element);
                if (!PatternTermMatches(terms[i], request.MatchMode, elementId, elementName, state))
                    return false;

                cells.Add(new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["cell"] = cell,
                    ["elementId"] = elementId,
                    ["elementName"] = elementName,
                    ["state"] = state,
                    ["kg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3)
                });
            }

            int endX = startX + dx * (terms.Count - 1);
            int endY = startY + dy * (terms.Count - 1);
            var rect = new Dictionary<string, int>
            {
                ["x1"] = Math.Min(startX, endX),
                ["y1"] = Math.Min(startY, endY),
                ["x2"] = Math.Max(startX, endX),
                ["y2"] = Math.Max(startY, endY)
            };
            int worldId = Grid.WorldIdx[Grid.XYToCell(startX, startY)];
            var area = AreaHandleRegistry.Define(rect, worldId, "pattern:" + request.Pattern);
            match = new Dictionary<string, object>
            {
                ["areaId"] = area.Id,
                ["worldId"] = worldId,
                ["direction"] = PatternDirectionName(dx, dy),
                ["start"] = new Dictionary<string, int> { ["x"] = startX, ["y"] = startY },
                ["end"] = new Dictionary<string, int> { ["x"] = endX, ["y"] = endY },
                ["rect"] = rect,
                ["length"] = terms.Count,
                ["anchor"] = new Dictionary<string, int> { ["x"] = startX, ["y"] = startY },
                ["cells"] = cells
            };
            return true;
        }

        private static IEnumerable<string> ParsePatternTerms(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                yield break;

            string normalized = pattern
                .Replace("->", "-")
                .Replace(">", "-")
                .Replace("→", "-")
                .Replace("，", ",")
                .Replace("/", "-");

            foreach (string raw in normalized.Split(new[] { '-', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string term = raw.Trim();
                if (string.IsNullOrWhiteSpace(term))
                    continue;

                int repeat = ParseRepeatSuffix(ref term);
                for (int i = 0; i < repeat; i++)
                    yield return term;
            }
        }

        private static int ParseRepeatSuffix(ref string term)
        {
            int open = term.EndsWith("}", StringComparison.Ordinal) ? term.LastIndexOf('{') : -1;
            if (open < 0 || open >= term.Length - 2)
                return 1;

            string countText = term.Substring(open + 1, term.Length - open - 2);
            int count;
            if (!int.TryParse(countText, out count))
                return 1;

            term = term.Substring(0, open).Trim();
            return Math.Max(1, Math.Min(count, 32));
        }

        private static IEnumerable<(int dx, int dy)> PatternDirections(string direction)
        {
            string value = (direction ?? "both").Trim().ToLowerInvariant();
            if (value == "horizontal" || value == "x" || value == "row" || value == "both")
            {
                yield return (1, 0);
                yield return (-1, 0);
            }
            if (value == "vertical" || value == "y" || value == "column" || value == "both")
            {
                yield return (0, 1);
                yield return (0, -1);
            }
        }

        private static string PatternDirectionName(int dx, int dy)
        {
            if (dx > 0) return "right";
            if (dx < 0) return "left";
            if (dy > 0) return "up";
            if (dy < 0) return "down";
            return "none";
        }

        private static bool PatternTermMatches(string term, string matchMode, params string[] values)
        {
            if (IsWildcardPatternTerm(term))
                return true;

            foreach (string alternative in SplitPatternAlternatives(term))
            {
                foreach (string value in values)
                {
                    if (PatternValueMatches(alternative, value, matchMode))
                        return true;
                }
            }
            return false;
        }

        private static bool IsWildcardPatternTerm(string term)
        {
            string value = (term ?? "").Trim();
            return value == "*" || value == "?" || value == "." || value.Equals("any", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SplitPatternAlternatives(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                yield break;

            string value = term.Trim();
            if (value.Length >= 2 && value[0] == '(' && value[value.Length - 1] == ')')
                value = value.Substring(1, value.Length - 2);

            foreach (string part in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string alternative = part.Trim();
                if (!string.IsNullOrWhiteSpace(alternative))
                    yield return alternative;
            }
        }

        private static bool PatternValueMatches(string term, string value, string matchMode)
        {
            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(value))
                return false;
            string mode = (matchMode ?? "smart").Trim().ToLowerInvariant();
            if (string.Equals(term.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
            string termNorm = NormalizePatternText(term);
            string valueNorm = NormalizePatternText(value);
            if (termNorm.Length == 0 || valueNorm.Length == 0)
                return false;
            if (termNorm == valueNorm)
                return true;
            if (mode == "exact")
                return false;
            if (valueNorm.Contains(termNorm) || termNorm.Contains(valueNorm))
                return true;
            if (mode == "fuzzy")
            {
                int maxDistance = termNorm.Length <= 4 ? 1 : termNorm.Length <= 8 ? 2 : 3;
                return BoundedEditDistance(valueNorm, termNorm, maxDistance) >= 0;
            }
            return false;
        }

        private static string NormalizePatternText(string value)
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

    }
}
