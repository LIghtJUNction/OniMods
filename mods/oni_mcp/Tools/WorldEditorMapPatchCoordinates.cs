using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryParseMapEditChangesFromPatchCoordinates(
            string current,
            string search,
            string replacement,
            out List<MapEditCell> changes,
            out string error)
        {
            changes = null;
            error = null;

            int[] currentHundreds;
            int[] currentTens;
            int[] currentOnes;
            var currentRows = ParseMapRows(current, out currentHundreds, out currentTens, out currentOnes, out error);
            if (currentRows == null)
                return true;

            int[] hundreds;
            int[] tens;
            int[] ones;
            var searchRows = ParseMapRows(search, out hundreds, out tens, out ones, out error, false);
            if (hundreds == null || tens == null || ones == null)
                return false;

            int[] replacementHundreds;
            int[] replacementTens;
            int[] replacementOnes;
            var replacementRows = ParseMapRows(replacement, out replacementHundreds, out replacementTens, out replacementOnes, out error, false);
            if (replacementHundreds == null || replacementTens == null || replacementOnes == null)
            {
                error = "REPLACE must preserve explicit X coordinate headers.";
                return true;
            }
            if (searchRows == null || searchRows.Count == 0)
            {
                error = "SEARCH must include copied Y=... grid rows from the map snapshot.";
                changes = null;
                return true;
            }

            if (replacementRows == null || replacementRows.Count == 0)
            {
                error = "REPLACE must include edited Y=... grid rows.";
                changes = null;
                return true;
            }

            if (!TryBuildAxisCoordinates(hundreds, tens, ones, out int[] searchX, out error)
                || !TryBuildAxisCoordinates(replacementHundreds, replacementTens, replacementOnes, out int[] replacementX, out error)
                || !TryBuildAxisCoordinates(currentHundreds, currentTens, currentOnes, out int[] currentX, out error))
                return true;
            if (!searchX.SequenceEqual(replacementX))
            {
                error = "REPLACE X coordinate headers differ from SEARCH.";
                return true;
            }
            var currentIndex = currentX.Select((x, index) => new { x, index }).ToDictionary(item => item.x, item => item.index);
            var result = new List<MapEditCell>();
            foreach (var row in searchRows)
            {
                if (!currentRows.TryGetValue(row.Key, out string[] currentSymbols))
                {
                    error = "SEARCH row Y=" + row.Key + " is outside the current map snapshot.";
                    return true;
                }
                string[] replacementSymbols;
                if (!replacementRows.TryGetValue(row.Key, out replacementSymbols))
                {
                    error = "REPLACE missing row Y=" + row.Key;
                    changes = null;
                    return true;
                }

                if (row.Value.Length != searchX.Length || replacementSymbols.Length != searchX.Length)
                {
                    error = "SEARCH/REPLACE row Y=" + row.Key + " width must exactly match the X headers after RLE expansion.";
                    return true;
                }
                for (int i = 0; i < searchX.Length; i++)
                {
                    int x = searchX[i];
                    if (!currentIndex.TryGetValue(x, out int currentOffset) || currentOffset >= currentSymbols.Length)
                    {
                        error = "X=" + x + " is outside the current map snapshot.";
                        return true;
                    }
                    string actual = currentSymbols[currentOffset];
                    if (!SearchTokenMatches(actual, row.Value[i]))
                    {
                        error = "Stale map snapshot at (" + x + "," + row.Key + "): expected `" + row.Value[i] + "`, current `" + actual + "`.";
                        return true;
                    }
                    if (ReplacementKeepsOriginal(replacementSymbols[i]))
                        continue;
                    if (string.Equals(actual, replacementSymbols[i], StringComparison.Ordinal))
                        continue;
                    result.Add(new MapEditCell
                    {
                        X = x,
                        Y = row.Key,
                        FromToken = actual,
                        ToToken = replacementSymbols[i]
                    });
                }
            }

            changes = result;
            return true;
        }

        private static bool TryBuildAxisCoordinates(int[] hundreds, int[] tens, int[] ones, out int[] coordinates, out string error)
        {
            coordinates = null;
            error = null;
            if (hundreds == null || tens == null || ones == null || hundreds.Length != tens.Length || tens.Length != ones.Length || ones.Length == 0)
            {
                error = "X coordinate headers must have equal non-zero widths.";
                return false;
            }
            if (hundreds.Any(value => value < 0 || value > 9)
                || tens.Any(value => value < 0 || value > 9)
                || ones.Any(value => value < 0 || value > 9))
            {
                error = "X coordinate headers contain a non-decimal digit.";
                return false;
            }
            coordinates = Enumerable.Range(0, ones.Length)
                .Select(i => hundreds[i] * 100 + tens[i] * 10 + ones[i])
                .ToArray();
            if (coordinates.Distinct().Count() != coordinates.Length)
            {
                error = "X coordinate headers contain duplicate coordinates.";
                coordinates = null;
                return false;
            }
            return true;
        }
    }
}
