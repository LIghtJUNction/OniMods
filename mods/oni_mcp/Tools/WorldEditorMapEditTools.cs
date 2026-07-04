using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private sealed class MapEditCell
        {
            public int X;
            public int Y;
            public string FromToken;
            public string ToToken;
        }

        private static bool IsEditableMapMarkdown(string relative)
        {
            return relative == "map/index.md"
                || relative == "map/viewport.md"
                || (relative.StartsWith("map/layers/", StringComparison.Ordinal) && relative.EndsWith(".md", StringComparison.Ordinal))
                || IsInfrastructureMapMarkdown(relative);
        }

        private static CallToolResult ApplyMapEdit(JObject args, string search, string replacement)
        {
            string error;
            List<MapEditCell> changes;
            if (!TryParseMapEditChangesFromPatchCoordinates(search, replacement, out changes, out error))
            {
                string current;
                string readError;
                if (!TryReadVirtualFileText(args["sourcePath"]?.ToString(), out current, out readError))
                    return CallToolResult.Error("Cannot read current map before applying map edit: " + readError);
                changes = ParseMapEditChanges(current, search, replacement, out error);
            }
            if (changes == null)
                return CallToolResult.Error(error);
            if (changes.Count == 0)
                return CallToolResult.Error("Map edit changed no grid cells; edit copied map characters directly.");

            int writeBudget = MapEditWriteBudget(args);
            bool partial = changes.Count > writeBudget;
            var executableChanges = partial ? changes.Take(writeBudget).ToList() : changes;

            var results = new JArray();
            bool anyError = false;
            foreach (var group in executableChanges.GroupBy(ChangeKind))
            {
                CallToolResult result;
                if (group.Key == "build")
                    result = ApplyBuildMapEdit(args, group);
                else if (group.Key == "wire")
                    result = ApplyConnectionMapEdit(args, group);
                else if (IsOrderAction(group.Key))
                    result = ApplyOrderMapEdit(args, group.Key, group);
                else
                    return UnsupportedMapEdit(group);

                anyError = anyError || result.IsError;
                results.Add(new JObject
                {
                    ["kind"] = group.Key,
                    ["ok"] = !result.IsError,
                    ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                });
            }

            return JsonResult(new JObject
            {
                ["ok"] = !anyError,
                ["sourcePath"] = args["sourcePath"]?.ToString(),
                ["changedCells"] = changes.Count,
                ["executedCells"] = executableChanges.Count,
                ["remainingCells"] = Math.Max(0, changes.Count - executableChanges.Count),
                ["partial"] = partial,
                ["next"] = partial ? "Re-run the same SEARCH/REPLACE edit to continue; already-applied cells will be skipped." : "complete",
                ["results"] = results
            });
        }

        private static CallToolResult ApplyOrderMapEdit(JObject parentArgs, string action, IEnumerable<MapEditCell> cells)
        {
            var byToken = cells.GroupBy(c => c.ToToken);
            var results = new JArray();
            bool anyError = false;
            foreach (var group in byToken)
            {
                int priority = Math.Max(1, Math.Min(ParsePriority(group.Key) ?? ToolUtil.GetInt(parentArgs, "priority") ?? 5, 9));
                foreach (var bounds in ContiguousRuns(group))
                {
                    var orderArgs = CopyPayload(parentArgs);
                    orderArgs["x1"] = bounds.Item1;
                    orderArgs["y1"] = bounds.Item2;
                    orderArgs["x2"] = bounds.Item3;
                    orderArgs["y2"] = bounds.Item4;
                    orderArgs["priority"] = priority;
                    orderArgs["confirm"] = ToolUtil.GetBool(parentArgs, "confirm", false);

                    if (action == "deconstruct" || action == "attack" || action == "capture")
                    {
                        orderArgs["domain"] = "designation";
                        orderArgs["action"] = action;
                        if (action == "attack")
                            orderArgs["mode"] = "mark";
                    }
                    else
                    {
                        orderArgs["domain"] = "area";
                        orderArgs["action"] = action;
                        if (action == "harvest")
                            orderArgs["mode"] = "mark";
                    }

                    var result = OrdersControlEntryTools.ControlOrders().Handler(orderArgs);
                    anyError = anyError || result.IsError;
                    results.Add(new JObject
                    {
                        ["token"] = group.Key,
                        ["action"] = action,
                        ["priority"] = priority,
                        ["area"] = RectObject(bounds),
                        ["cells"] = RectCellCount(bounds),
                        ["ok"] = !result.IsError,
                        ["error"] = result.IsError ? result.Content?.FirstOrDefault()?.Text ?? string.Empty : string.Empty,
                        ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                    });
                }
            }

            return JsonResult(new JObject { ["ok"] = !anyError, ["results"] = results });
        }

        private static int MapEditWriteBudget(JObject args)
        {
            int requested = ToolUtil.GetInt(args, "maxWriteCells") ?? ToolUtil.GetInt(args, "maxCells") ?? 32;
            return Math.Max(1, Math.Min(requested, 64));
        }

        private static IEnumerable<Tuple<int, int, int, int>> ContiguousRuns(IEnumerable<MapEditCell> cells)
        {
            foreach (var row in cells.GroupBy(c => c.Y).OrderByDescending(g => g.Key))
            {
                int? start = null;
                int previous = 0;
                foreach (var cell in row.Select(c => c.X).Distinct().OrderBy(x => x))
                {
                    if (!start.HasValue)
                    {
                        start = cell;
                        previous = cell;
                        continue;
                    }
                    if (cell == previous + 1)
                    {
                        previous = cell;
                        continue;
                    }
                    yield return Tuple.Create(start.Value, row.Key, previous, row.Key);
                    start = previous = cell;
                }
                if (start.HasValue)
                    yield return Tuple.Create(start.Value, row.Key, previous, row.Key);
            }
        }

        private static int RectCellCount(Tuple<int, int, int, int> bounds)
        {
            return Math.Max(0, bounds.Item3 - bounds.Item1 + 1) * Math.Max(0, bounds.Item4 - bounds.Item2 + 1);
        }

        private static CallToolResult ApplyBuildMapEdit(JObject parentArgs, IEnumerable<MapEditCell> cells)
        {
            var results = new JArray();
            bool anyError = false;
            foreach (var group in cells.GroupBy(c => c.ToToken))
            {
                char buildSymbol;
                int? priority;
                string material;
                if (!ParseBuildToken(group.Key, out buildSymbol, out priority, out material))
                    return CallToolResult.Error($"Invalid build token '{group.Key}'. Use 建筑名:优先级 or 建筑名:优先级#材料字.");
                if (!priority.HasValue)
                    return CallToolResult.Error($"Build token '{group.Key}' is missing :priority. Tokens without :priority are treated as existing objects only.");

                string prefabId;
                if (!TryResolveBuildPrefabFromToken(group.Key, buildSymbol, out prefabId))
                    return CallToolResult.Error($"Cannot map building token '{group.Key}' to a buildable prefab.");

                string footprintError;
                JArray anchors;
                if (!TryBuildAnchorsForPrefabFootprints(parentArgs, prefabId, group, out anchors, out footprintError))
                    return CallToolResult.Error(footprintError);

                var buildArgs = CopyPayload(parentArgs);
                buildArgs["domain"] = "planning";
                buildArgs["action"] = "build_area";
                buildArgs["prefabId"] = prefabId;
                buildArgs["anchors"] = anchors;
                buildArgs["priority"] = priority.Value;
                buildArgs["confirm"] = ToolUtil.GetBool(parentArgs, "confirm", false);
                if (!string.IsNullOrWhiteSpace(material))
                    buildArgs["material"] = material;

                var result = BuildingControlTools.ControlBuilding().Handler(buildArgs);
                anyError = anyError || result.IsError;
                results.Add(new JObject
                {
                    ["token"] = group.Key,
                    ["symbol"] = buildSymbol.ToString(),
                    ["prefabId"] = prefabId,
                    ["priority"] = priority,
                    ["material"] = material,
                    ["anchors"] = anchors,
                    ["ok"] = !result.IsError,
                    ["error"] = result.IsError ? result.Content?.FirstOrDefault()?.Text ?? string.Empty : string.Empty,
                    ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                });
            }

            return JsonResult(new JObject { ["ok"] = !anyError, ["results"] = results });
        }

        private static CallToolResult UnsupportedMapEdit(IEnumerable<MapEditCell> cells)
        {
            var sample = cells.Take(8).Select(c => $"({c.X},{c.Y}) {c.FromToken}->{c.ToToken}");
            return CallToolResult.Error("Unsupported map cell transitions. Use empty -> 建筑名:优先级 for build, or replace cells with 挖/拆/擦/扫/毒/杀/收/消/捕 plus optional :priority for orders. Unsupported: " + string.Join(", ", sample));
        }

        private static List<MapEditCell> ParseMapEditChanges(string current, string search, string replacement, out string error)
        {
            error = null;
            int[] hundreds;
            int[] tens;
            int[] ones;
            var currentRows = ParseMapRows(current, out hundreds, out tens, out ones, out error);
            if (currentRows == null)
                return null;

            int[] ignoredHundreds;
            int[] ignoredTens;
            int[] ignoredOnes;
            var searchRows = ParseMapRows(search, out ignoredHundreds, out ignoredTens, out ignoredOnes, out error, false);
            if (searchRows == null || searchRows.Count == 0)
            {
                error = "SEARCH must include copied Y=... grid rows from current map. Use ? for unknown/unimportant cells, /regex/ or ~regex for token regex.";
                return null;
            }

            var replaceRows = ParseMapRows(replacement, out ignoredHundreds, out ignoredTens, out ignoredOnes, out error, false);
            if (replaceRows == null || replaceRows.Count == 0)
            {
                error = "REPLACE must include edited Y=... grid rows";
                return null;
            }

            var changes = new List<MapEditCell>();
            int width = Math.Min(hundreds.Length, Math.Min(tens.Length, ones.Length));
            foreach (var item in searchRows)
            {
                string[] currentSymbols;
                if (!currentRows.TryGetValue(item.Key, out currentSymbols))
                {
                    error = "SEARCH row Y=" + item.Key + " is not present in current map";
                    return null;
                }

                string[] replacementSymbols;
                if (!replaceRows.TryGetValue(item.Key, out replacementSymbols))
                {
                    error = "REPLACE is missing row Y=" + item.Key;
                    return null;
                }

                int offset = FindTokenSequence(currentSymbols, item.Value);
                if (offset < 0)
                {
                    error = "SEARCH row Y=" + item.Key + " no longer matches current map. `?` matches one token; `*` matches one token; `/regex/` or `~regex` matches one token by regex.";
                    return null;
                }

                int count = Math.Min(item.Value.Length, replacementSymbols.Length);
                for (int i = 0; i < count && offset + i < width; i++)
                {
                    string currentToken = currentSymbols[offset + i];
                    if (ReplacementKeepsOriginal(replacementSymbols[i]))
                        continue;
                    if (currentToken == replacementSymbols[i])
                        continue;

                    int x = hundreds[offset + i] * 100 + tens[offset + i] * 10 + ones[offset + i];
                    changes.Add(new MapEditCell { X = x, Y = item.Key, FromToken = currentToken, ToToken = replacementSymbols[i] });
                }
            }

            return changes;
        }

        private static Dictionary<int, string[]> ParseMapRows(string text, out int[] hundreds, out int[] tens, out int[] ones, out string error, bool requireHeaders = true)
        {
            hundreds = null;
            tens = null;
            ones = null;
            error = null;
            var rows = new Dictionary<int, string[]>();
            foreach (var rawLine in NormalizeSearchText(text).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("百位X:", StringComparison.Ordinal))
                    hundreds = ParseDigitRow(line.Substring("百位X:".Length));
                else if (line.StartsWith("十位X:", StringComparison.Ordinal))
                    tens = ParseDigitRow(line.Substring("十位X:".Length));
                else if (line.StartsWith("个位X:", StringComparison.Ordinal))
                    ones = ParseDigitRow(line.Substring("个位X:".Length));
                else
                {
                    int y;
                    string[] symbols;
                    if (TryParseYRow(line, out y, out symbols))
                        rows[y] = symbols;
                }
            }

            if (requireHeaders && (hundreds == null || tens == null || ones == null))
            {
                error = "current map is missing X digit headers";
                return null;
            }

            return rows;
        }

        private static bool TryParseYRow(string line, out int y, out string[] symbols)
        {
            y = 0;
            symbols = null;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("Y=", StringComparison.Ordinal))
                return false;
            int colon = line.IndexOf(':');
            if (colon < 0)
                return false;
            if (!int.TryParse(line.Substring(2, colon - 2), out y))
                return false;
            symbols = line.Substring(colon + 1)
                .Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token != "┆")
                .ToArray();
            return symbols.Length > 0;
        }

        private static int FindTokenSequence(string[] haystack, string[] needle)
        {
            if (needle == null || needle.Length == 0 || haystack == null || haystack.Length < needle.Length)
                return -1;
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (!SearchTokenMatches(haystack[i + j], needle[j]))
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                    return i;
            }
            return -1;
        }

        private static bool MapSearchMatchesCurrent(string current, string search)
        {
            string error;
            int[] hundreds;
            int[] tens;
            int[] ones;
            var currentRows = ParseMapRows(current, out hundreds, out tens, out ones, out error);
            if (currentRows == null)
                return false;

            int[] ignoredHundreds;
            int[] ignoredTens;
            int[] ignoredOnes;
            var searchRows = ParseMapRows(search, out ignoredHundreds, out ignoredTens, out ignoredOnes, out error, false);
            if (searchRows == null || searchRows.Count == 0)
                return false;

            foreach (var row in searchRows)
            {
                string[] currentSymbols;
                if (!currentRows.TryGetValue(row.Key, out currentSymbols))
                    return false;
                if (FindTokenSequence(currentSymbols, row.Value) < 0)
                    return false;
            }

            return true;
        }

        private static bool SearchTokenMatches(string actual, string pattern)
        {
            pattern = (pattern ?? string.Empty).Trim();
            if (pattern == "?" || pattern == "*" || pattern == ".*")
                return true;
            if (pattern.Length >= 2 && pattern[0] == '/' && pattern[pattern.Length - 1] == '/')
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1, pattern.Length - 2));
            if (pattern.StartsWith("~", StringComparison.Ordinal) && pattern.Length > 1)
                return Regex.IsMatch(actual ?? string.Empty, pattern.Substring(1));
            return string.Equals(actual, pattern, StringComparison.Ordinal);
        }

        private static bool ReplacementKeepsOriginal(string token)
        {
            token = (token ?? string.Empty).Trim();
            return token == "?" || token == "*" || token == ".*";
        }

        private static int[] ParseDigitRow(string text)
        {
            return (text ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part != "┆")
                .Select(part => { int value; return int.TryParse(part, out value) ? value : 0; })
                .ToArray();
        }

        private static string ChangeKind(MapEditCell cell)
        {
            string action = ParseOrderAction(cell.ToToken);
            if (!string.IsNullOrEmpty(action))
                return action;
            if (IsConnectionEdit(cell))
                return "wire";
            if (IsBuildPlanToken(cell.ToToken))
                return "build";
            if (IsEmptyMapToken(cell.FromToken) && !IsEmptyMapToken(cell.ToToken))
                return "build";
            return "unsupported";
        }

        private static bool IsBuildPlanToken(string token)
        {
            char symbol;
            int? priority;
            string material;
            return ParseBuildToken(token, out symbol, out priority, out material) && priority.HasValue;
        }

    }
}
