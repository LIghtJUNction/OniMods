using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult Grep(JObject args)
        {
            string path = NormalizePath(Text(args, "path"), _cwd);
            string query = Text(args, "query", "target", "search");
            if (string.IsNullOrWhiteSpace(query))
                return CallToolResult.Error("grep requires query/search text");

            var readArgs = CopyPayload(args);
            readArgs["path"] = path;
            readArgs["command"] = "read";
            var read = Read(readArgs);
            if (read == null || read.IsError)
                return read ?? CallToolResult.Error("read failed");

            string text = read.Content?.FirstOrDefault()?.Text ?? string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int context = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "context") ?? 1, 5));
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 80, 500));
            bool regex = ToolUtil.GetBool(args, "regex", false);
            bool ignoreCase = ToolUtil.GetBool(args, "ignoreCase", true);
            var matcher = BuildGrepMatcher(query, regex, ignoreCase, out string regexError);
            if (matcher == null)
                return CallToolResult.Error(regexError);
            var hitLines = new SortedSet<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                if (!matcher(lines[i]))
                    continue;
                for (int j = Math.Max(0, i - context); j <= Math.Min(lines.Length - 1, i + context); j++)
                    hitLines.Add(j);
                if (hitLines.Count >= limit)
                    break;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# grep " + query + " " + path);
            sb.AppendLine("- mode: " + (regex ? "regex" : "text") + ", view=" + Text(args, "view", "activeView", "displayView"));
            foreach (int i in hitLines.Take(limit))
                sb.AppendLine((i + 1).ToString("D4") + ": " + lines[i]);
            if (hitLines.Count == 0)
                sb.AppendLine("(no matches)");
            return CallToolResult.Text(sb.ToString());
        }

        private static Func<string, bool> BuildGrepMatcher(string query, bool regex, bool ignoreCase, out string error)
        {
            error = null;
            if (!regex)
            {
                var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return line => line.IndexOf(query, comparison) >= 0;
            }

            try
            {
                var options = RegexOptions.CultureInvariant;
                if (ignoreCase)
                    options |= RegexOptions.IgnoreCase;
                var compiled = new Regex(query, options);
                return line => compiled.IsMatch(line);
            }
            catch (ArgumentException ex)
            {
                error = "invalid regex: " + ex.Message;
                return null;
            }
        }

        private static CallToolResult SymbolMap(JObject args)
        {
            return SearchGlyphs(args);
        }

        private static string ReadSymbolMarkdown(string path, string query = null)
        {
            var rows = BuildSymbolRows();
            if (!string.IsNullOrWhiteSpace(query))
            {
                rows = rows.Where(row =>
                    row["symbol"].ToString() == query
                    || row["id"].ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || row["name"].ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || row["kind"].ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            var sb = new StringBuilder();
            sb.AppendLine("# " + path);
            sb.AppendLine();
            sb.AppendLine("- Source: generated from ONI source IDs plus official zh strings.");
            sb.AppendLine("- Unknown runtime objects render as `?` and should be added to the generated table.");
            sb.AppendLine();
            sb.AppendLine("| 字 | 类型 | 名称 | 游戏内ID |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var row in rows.OrderBy(r => r["kind"].ToString()).ThenBy(r => r["symbol"].ToString()).ThenBy(r => r["id"].ToString()))
                sb.AppendLine("| `" + row["symbol"] + "` | " + row["kind"] + " | " + row["name"] + " | `" + row["id"] + "` |");
            return sb.ToString();
        }

        private static string FormatLegendLine(char symbol, string fallback)
        {
            if (symbol == '人')
                return "- `人` : Entity | 代表: 复制人 | 游戏内ID: Minion";
            if (symbol == '仿')
                return "- `仿` : Entity | 代表: 仿生人 | 游戏内ID: BionicMinion";
            if (symbol == '物')
                return "- `物` : Entity | 代表: 小动物/实体 | 游戏内ID: Critter";
            if (fallback != "Cell symbol")
                return "- `" + symbol + "` : " + fallback;

            var rows = BuildSymbolRows().Where(row => row["symbol"].ToString() == symbol.ToString()).ToList();
            if (rows.Count == 0)
                return $"- `{symbol}` : {fallback}";

            var first = rows[0];
            string kind = first["kind"].ToString();
            string name = first["name"].ToString();
            string ids = string.Join(", ", rows.Select(row => row["id"].ToString()).Distinct().Take(8).ToArray());
            return $"- `{symbol}` : {kind} | 代表: {name} | 游戏内ID: {ids}";
        }
    }
}
