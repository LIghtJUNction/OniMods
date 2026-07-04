using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryParseMapEditChangesFromPatchCoordinates(
            string search,
            string replacement,
            out List<MapEditCell> changes,
            out string error)
        {
            changes = null;
            error = null;

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

            int width = Math.Min(hundreds.Length, Math.Min(tens.Length, ones.Length));
            var result = new List<MapEditCell>();
            foreach (var row in searchRows)
            {
                string[] replacementSymbols;
                if (!replacementRows.TryGetValue(row.Key, out replacementSymbols))
                {
                    error = "REPLACE missing row Y=" + row.Key;
                    changes = null;
                    return true;
                }

                int count = Math.Min(row.Value.Length, replacementSymbols.Length);
                if (count > width)
                    count = width;
                for (int i = 0; i < count; i++)
                {
                    if (ReplacementKeepsOriginal(replacementSymbols[i]))
                        continue;

                    int x = hundreds[i] * 100 + tens[i] * 10 + ones[i];
                    result.Add(new MapEditCell
                    {
                        X = x,
                        Y = row.Key,
                        FromToken = row.Value[i],
                        ToToken = replacementSymbols[i]
                    });
                }
            }

            changes = result;
            return true;
        }
    }
}
