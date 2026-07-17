using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TemplateClasses;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SandboxTools
    {
        private static TokenGrid ParseTokenGrid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TokenGrid.Fail("empty pattern");

            var rows = new List<string[]>();
            foreach (string rawLine in value.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line == "```" || line.StartsWith("```", StringComparison.Ordinal))
                    continue;

                line = line.Replace(",", " ").Replace("|", " ");
                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(token => token.Trim())
                    .Where(token => token.Length > 0)
                    .ToArray();
                if (tokens.Length > 0)
                    rows.Add(tokens);
            }

            if (rows.Count == 0)
                return TokenGrid.Fail("no token rows");

            int width = rows[0].Length;
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].Length != width)
                    return TokenGrid.Fail($"row {i} width {rows[i].Length} differs from first row width {width}");
            }

            return new TokenGrid(rows);
        }

        private static List<MapPatternMatch> FindPatternMatches(TokenGrid search, Dictionary<string, int> rect, int worldId, bool visibleOnly)
        {
            var matches = new List<MapPatternMatch>();
            int maxTopY = rect["y2"];
            int minTopY = rect["y1"] + search.Height - 1;
            int maxLeftX = rect["x2"] - search.Width + 1;
            if (maxLeftX < rect["x1"] || minTopY > maxTopY)
                return matches;

            for (int topY = maxTopY; topY >= minTopY; topY--)
            {
                for (int leftX = rect["x1"]; leftX <= maxLeftX; leftX++)
                {
                    if (PatternMatchesAt(search, leftX, topY, worldId, visibleOnly))
                        matches.Add(new MapPatternMatch(leftX, topY, search.Width, search.Height));
                }
            }

            return matches;
        }

        private static bool PatternMatchesAt(TokenGrid search, int leftX, int topY, int worldId, bool visibleOnly)
        {
            for (int row = 0; row < search.Height; row++)
            {
                int y = topY - row;
                for (int col = 0; col < search.Width; col++)
                {
                    int x = leftX + col;
                    int cell = Grid.XYToCell(x, y);
                    if (!SearchTokenMatches(search.Rows[row][col], cell, worldId, visibleOnly))
                        return false;
                }
            }

            return true;
        }

        private static bool SearchTokenMatches(string token, int cell, int worldId, bool visibleOnly)
        {
            token = NormalizeToken(token);
            if (token == "*" || token == "any")
                return Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId);
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return token == "unk" || token == "unknown" || token == "outside";
            if (visibleOnly && !Grid.IsVisible(cell))
                return token == "unk" || token == "unknown" || token == "?";
            if (token == "tile")
                return Grid.Foundation[cell];

            var element = Grid.Element[cell];
            string current = BaseMapToken(cell, element);
            if (token == current)
                return true;
            if (token == "gas")
                return element != null && element.IsGas;
            if (token == "liquid")
                return element != null && element.IsLiquid;
            if (token == "solid")
                return element != null && element.IsSolid && !Grid.Foundation[cell];

            var requested = ResolveElementToken(token);
            return requested != null && element != null && requested.id == element.id;
        }

        private static string BaseMapToken(int cell, Element element)
        {
            if (Grid.Foundation[cell])
                return "tile";
            if (element == null)
                return "unk";
            if (element.IsVacuum)
                return "vac";
            switch (element.id)
            {
                case SimHashes.Oxygen: return "oxy";
                case SimHashes.ContaminatedOxygen: return "po2";
                case SimHashes.CarbonDioxide: return "co2";
                case SimHashes.Hydrogen: return "hyd";
            }
            if (element.IsLiquid)
                return "liq";
            if (element.IsSolid)
                return "sol";
            if (element.IsGas)
                return "gas";
            return NormalizeToken(element.id.ToString());
        }

        private static SelectedMapMatches SelectMatches(List<MapPatternMatch> matches, JObject args)
        {
            int? matchIndex = ToolUtil.GetInt(args, "matchIndex");
            if (matchIndex.HasValue)
            {
                if (matchIndex.Value < 0 || matchIndex.Value >= matches.Count)
                    return SelectedMapMatches.Fail($"matchIndex {matchIndex.Value} is out of range; matched={matches.Count}");
                return new SelectedMapMatches(new List<MapPatternMatch> { matches[matchIndex.Value] });
            }

            string mode = (args["matchMode"]?.ToString() ?? "unique").Trim().ToLowerInvariant();
            if (mode == "first")
                return new SelectedMapMatches(new List<MapPatternMatch> { matches[0] });
            if (mode == "all")
                return new SelectedMapMatches(matches);
            if (mode != "unique")
                return SelectedMapMatches.Fail("matchMode must be unique, first, or all");
            if (matches.Count != 1)
                return SelectedMapMatches.Fail($"Expected exactly one match but found {matches.Count}; set matchIndex, matchMode=first, or matchMode=all.");
            return new SelectedMapMatches(new List<MapPatternMatch> { matches[0] });
        }

        private static ReplacementChangeSet BuildReplacementChanges(List<MapPatternMatch> matches, TokenGrid designate, int worldId, JObject args)
        {
            var changes = new List<MapReplacementChange>();
            var seen = new HashSet<int>();
            foreach (var match in matches)
            {
                for (int row = 0; row < designate.Height; row++)
                {
                    int y = match.TopY - row;
                    for (int col = 0; col < designate.Width; col++)
                    {
                        string rawToken = designate.Rows[row][col];
                        string token = NormalizeToken(rawToken);
                        string keepToken = string.IsNullOrWhiteSpace(rawToken) ? "" : rawToken.Trim().Trim('`').Trim().ToLowerInvariant();
                        if (keepToken == "_" || keepToken == "-" || token == "same" || token == "keep")
                            continue;

                        int x = match.LeftX + col;
                        int cell = Grid.XYToCell(x, y);
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        if (!seen.Add(cell))
                            continue;

                        var element = ResolveElementToken(token);
                        if (element == null)
                            return ReplacementChangeSet.Fail("Designate token cannot be painted as an element: " + rawToken);

                        byte diseaseIdx = ResolveDiseaseIndex(args["disease"]?.ToString());
                        int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                        float mass = ResolveReplacementMass(element, args);
                        float temp = ToolUtil.GetFloat(args, "temperatureK") ?? element.defaultValues.temperature;
                        changes.Add(new MapReplacementChange(cell, x, y, BaseMapToken(cell, Grid.Element[cell]), element, mass, temp, diseaseIdx, diseaseCount));
                    }
                }
            }

            return new ReplacementChangeSet(changes);
        }

        private static float ResolveReplacementMass(Element element, JObject args)
        {
            float? requested = ToolUtil.GetFloat(args, "massKg");
            if (requested.HasValue)
                return Math.Max(0f, requested.Value);
            if (element.IsVacuum)
                return 0f;
            if (element.IsGas)
                return 1f;
            if (element.IsLiquid)
                return 1000f;
            if (element.IsSolid)
                return 1840f;
            return 1f;
        }

        private static Element ResolveElementToken(string token)
        {
            token = NormalizeToken(token);
            switch (token)
            {
                case "vac":
                case "vacuum":
                case "empty":
                    return ElementLoader.FindElementByHash(SimHashes.Vacuum);
                case "oxy":
                case "oxygen":
                case "gas":
                    return ElementLoader.FindElementByHash(SimHashes.Oxygen);
                case "po2":
                case "pollutedoxygen":
                case "contaminatedoxygen":
                    return ElementLoader.FindElementByHash(SimHashes.ContaminatedOxygen);
                case "co2":
                case "carbondioxide":
                    return ElementLoader.FindElementByHash(SimHashes.CarbonDioxide);
                case "hyd":
                case "h2":
                case "hydrogen":
                    return ElementLoader.FindElementByHash(SimHashes.Hydrogen);
                case "water":
                case "liq":
                case "liquid":
                    return ElementLoader.FindElementByHash(SimHashes.Water);
                case "steam":
                    return ElementLoader.FindElementByHash(SimHashes.Steam);
                case "rock":
                case "sol":
                case "solid":
                    return ElementLoader.FindElementByHash(SimHashes.IgneousRock);
            }

            SimHashes hash;
            if (!Enum.TryParse(token, true, out hash))
                return null;
            return ElementLoader.FindElementByHash(hash);
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";
            return token.Trim().Trim('`').Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        private static List<Dictionary<string, object>> MatchPreviews(List<MapPatternMatch> matches, int limit)
        {
            return matches.Take(limit).Select((match, index) => new Dictionary<string, object>
            {
                ["index"] = index,
                ["topLeft"] = new[] { match.LeftX, match.TopY },
                ["bottomLeft"] = new[] { match.LeftX, match.BottomY },
                ["rect"] = new[] { match.LeftX, match.BottomY, match.RightX, match.TopY },
                ["size"] = new[] { match.Width, match.Height }
            }).ToList();
        }

        private static List<Dictionary<string, object>> ChangePreviews(List<MapReplacementChange> changes, int limit)
        {
            return changes.Take(limit).Select(change => new Dictionary<string, object>
            {
                ["x"] = change.X,
                ["y"] = change.Y,
                ["cell"] = change.Cell,
                ["from"] = change.FromToken,
                ["to"] = change.Element.id.ToString(),
                ["massKg"] = change.MassKg,
                ["temperatureK"] = change.TemperatureK
            }).ToList();
        }

    }
}
