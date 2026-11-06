using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OniOnline.Core
{
    internal sealed class TextCatalog
    {
        private readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>();

        internal TextCatalog(IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] columns = Columns(line);
                if (columns.Length != 4) throw new InvalidDataException("invalid_localization_columns");
                if (columns[0] == "key") continue;
                entries.Add(columns[0], columns);
            }
        }

        private static string[] Columns(string line)
        {
            var columns = new List<string>();
            var value = new StringBuilder();
            bool quoted = false, closed = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') value.Append(c);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else if (c == ',') { columns.Add(value.ToString()); value.Clear(); closed = false; }
                else if (closed) throw new InvalidDataException("invalid_localization_quote");
                else if (c == '"' && value.Length == 0) quoted = true;
                else if (c == '"') throw new InvalidDataException("invalid_localization_quote");
                else value.Append(c);
            }
            if (quoted) throw new InvalidDataException("unterminated_localization_quote");
            columns.Add(value.ToString());
            return columns.ToArray();
        }

        internal string Get(string key, string language)
        {
            string[] row;
            if (!entries.TryGetValue(key, out row)) return key;
            string code = (language ?? "en").ToLowerInvariant();
            int column = code.Contains("hant") || code.Contains("tw") || code.Contains("hk") || code.Contains("tchinese") ? 2
                : code.StartsWith("zh") || code == "schinese" ? 1 : 3;
            return string.IsNullOrEmpty(row[column]) ? row[3] : row[column];
        }
    }
}
