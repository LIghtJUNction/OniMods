using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static string NormalizeTextMapView(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "base";
            string view = value.Trim().ToLowerInvariant();
            switch (view)
            {
                case "terrain":
                case "normal":
                case "none":
                    return "base";
                case "power":
                case "electric":
                case "electrical":
                    return "power";
                case "gas":
                case "gas_pipe":
                case "gas_pipes":
                case "gas_conduit":
                    return "gas_conduits";
                case "liquid":
                case "liquid_pipe":
                case "liquid_pipes":
                case "liquid_conduit":
                    return "liquid_conduits";
                case "solid":
                case "shipping":
                case "conveyor":
                case "solid_conduit":
                    return "solid_conveyor";
                case "automation":
                case "logic":
                    return "logic";
                case "temperature":
                case "temp":
                case "thermal":
                case "heat":
                    return "temperature";
                default:
                    return "base";
            }
        }

        private static bool IsUtilityOverlayView(string view)
        {
            return view == "power"
                || view == "gas_conduits"
                || view == "liquid_conduits"
                || view == "solid_conveyor"
                || view == "logic";
        }

        private static bool IsAnalysisView(string view)
        {
            return view == "temperature";
        }

        private static string ShortLegend(char symbol)
        {
            switch (symbol)
            {
                case '?': return "unk";
                case '.': return "vac";
                case 'O': return "oxy";
                case 'P': return "po2";
                case 'C': return "co2";
                case 'H': return "h2";
                case 'L': return "liq";
                case 'S': return "solid";
                case 'T': return "tile";
                case '@': return "bp_anchor";
                case 'b': return "blueprint";
                case 'A': return "bld_anchor";
                case 'B': return "bld";
                case 'D': return "dupe";
                case 'i': return "item";
                case 'w': return "wire";
                case 'g': return "gas_pipe";
                case 'l': return "liq_pipe";
                case 's': return "solid_rail";
                case 'a': return "logic";
                case 'p': return "power_dev";
                case 'F': return "freeze";
                case 'c': return "cold";
                case 'm': return "mild";
                case 'h': return "hot";
                case 'X': return "extreme";
                default: return symbol.ToString();
            }
        }

        private static Dictionary<char, string> BuildLegend(string view)
        {
            if (view == "power")
                return new Dictionary<char, string>
                {
                    ['?'] = "unknown/unrevealed/outside-world",
                    ['.'] = "empty/no power overlay object",
                    ['w'] = "wire/conductive wire",
                    ['p'] = "power device: generator/battery/consumer"
                };
            if (view == "gas_conduits")
                return SparseLegend('g', "gas conduit");
            if (view == "liquid_conduits")
                return SparseLegend('l', "liquid conduit");
            if (view == "solid_conveyor")
                return SparseLegend('s', "solid conveyor rail");
            if (view == "logic")
                return SparseLegend('a', "automation wire");
            if (view == "temperature")
                return new Dictionary<char, string>
                {
                    ['?'] = "unknown/unrevealed/outside-world",
                    ['F'] = "freezing < -20C",
                    ['c'] = "cold -20..5C",
                    ['m'] = "mild 5..35C",
                    ['h'] = "hot 35..75C",
                    ['X'] = "extreme >= 75C"
                };

            return new Dictionary<char, string>
            {
                ['?'] = "unknown/unrevealed/outside-world",
                ['.'] = "vacuum",
                ['O'] = "oxygen",
                ['P'] = "polluted oxygen",
                ['C'] = "carbon dioxide gas, not constructed tile",
                ['H'] = "hydrogen",
                ['L'] = "liquid",
                ['S'] = "solid natural tile",
                ['T'] = "constructed tile/foundation",
                ['@'] = "construction blueprint anchor/lower-left footprint cell",
                ['b'] = "construction blueprint footprint overlay",
                ['A'] = "building anchor/lower-left footprint cell",
                ['B'] = "building footprint overlay",
                ['D'] = "duplicant overlay",
                ['i'] = "loose item/debris overlay"
            };
        }

        private static void AppendColumnGuide(StringBuilder text, Dictionary<string, int> rect)
        {
            const string prefix = "       ";
            int x1 = rect["x1"];
            int x2 = rect["x2"];
            var marks = new StringBuilder();
            var ones = new StringBuilder();

            for (int x = x1; x <= x2; x++)
            {
                if (x == x1 || x == x2 || x % 10 == 0)
                    marks.Append('|');
                else if (x % 5 == 0)
                    marks.Append('+');
                else
                    marks.Append('.');
                ones.Append(Math.Abs(x % 10));
            }

            text.AppendLine(prefix + "x " + x1 + ".." + x2 + "  | = x1/x2/10s, + = 5s");
            text.AppendLine(prefix + marks.ToString());
            text.AppendLine(prefix + ones.ToString());
        }

        private static void AppendReadableColumnGuide(StringBuilder text, Dictionary<string, int> rect)
        {
            int width = rect["x2"] - rect["x1"] + 1;
            var relative = new StringBuilder();
            var absolute = new StringBuilder();

            for (int rx = 0; rx < width; rx++)
            {
                if (rx > 0)
                {
                    relative.Append(' ');
                    absolute.Append(' ');
                }
                relative.Append(rx.ToString().PadLeft(4));
                absolute.Append((rect["x1"] + rx).ToString().PadLeft(4));
            }

            text.AppendLine("rx     | " + relative.ToString());
            text.AppendLine("absX   | " + absolute.ToString());
        }

        private static Dictionary<string, string> BuildTokenLegend(string view)
        {
            if (view == "power")
                return new Dictionary<string, string>
                {
                    ["unk"] = "unknown/unrevealed/outside-world",
                    ["empty"] = "no power overlay object",
                    ["wire"] = "wire/conductive wire",
                    ["pwr"] = "power device"
                };
            if (view == "gas_conduits")
                return OverlayTokenLegend("gasp", "gas conduit");
            if (view == "liquid_conduits")
                return OverlayTokenLegend("liqp", "liquid conduit");
            if (view == "solid_conveyor")
                return OverlayTokenLegend("rail", "solid conveyor rail");
            if (view == "logic")
                return OverlayTokenLegend("auto", "automation wire");
            if (view == "temperature")
                return new Dictionary<string, string>
                {
                    ["unk"] = "unknown/unrevealed/outside-world",
                    ["frz"] = "freezing < -20C",
                    ["cold"] = "cold -20..5C",
                    ["mild"] = "mild 5..35C",
                    ["hot"] = "hot 35..75C",
                    ["xhot"] = "extreme >= 75C"
                };

            return new Dictionary<string, string>
            {
                ["unk"] = "unknown/unrevealed/outside-world",
                ["vac"] = "vacuum",
                ["oxy"] = "oxygen",
                ["po2"] = "polluted oxygen",
                ["co2"] = "carbon dioxide gas",
                ["hyd"] = "hydrogen",
                ["liq"] = "liquid",
                ["sol"] = "solid natural tile",
                ["tile"] = "constructed tile/foundation",
                ["bp_anchor"] = "construction blueprint anchor/lower-left footprint cell",
                ["bp"] = "construction blueprint footprint overlay",
                ["bld_anchor"] = "building anchor/lower-left footprint cell",
                ["bld"] = "building footprint overlay",
                ["dup"] = "duplicant overlay",
                ["itm"] = "loose item/debris overlay"
            };
        }

        private static Dictionary<string, string> OverlayTokenLegend(string token, string name)
        {
            return new Dictionary<string, string>
            {
                ["unk"] = "unknown/unrevealed/outside-world",
                ["empty"] = "no overlay object",
                [token] = name
            };
        }

        private static string TokenForCell(CellSummary summary, string view)
        {
            return PadToken(TokenForSymbol(summary.Symbol, view, summary));
        }

        private static string TokenForSymbol(char symbol, string view, CellSummary summary = null)
        {
            switch (symbol)
            {
                case '?': return "unk";
                case '.': return IsUtilityOverlayView(view) ? "empty" : "vac";
                case 'O': return "oxy";
                case 'P': return "po2";
                case 'C': return "co2";
                case 'H': return "hyd";
                case 'L': return "liq";
                case 'S': return "sol";
                case 'T': return "tile";
                case '@': return "bp_anchor";
                case 'b': return "bp";
                case 'A': return "bld_anchor";
                case 'B': return "bld";
                case 'D': return "dup";
                case 'i': return "itm";
                case 'w': return "wire";
                case 'g': return "gasp";
                case 'l': return "liqp";
                case 's': return "rail";
                case 'a': return "auto";
                case 'p': return "pwr";
                case 'F': return "frz";
                case 'c': return "cold";
                case 'm': return "mild";
                case 'h': return "hot";
                case 'X': return "xhot";
                default:
                    if (summary != null && !string.IsNullOrWhiteSpace(summary.ElementId))
                        return AbbrevToken(summary.ElementId);
                    return symbol.ToString();
            }
        }

        private static string PadToken(string token)
        {
            token = string.IsNullOrWhiteSpace(token) ? "unk" : token.Trim();
            return token.Length >= 4 ? token.Substring(0, 4) : token.PadRight(4);
        }

        private static string AbbrevToken(string value)
        {
            var text = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Length == 0)
                return "unk";
            return text.Length <= 4 ? text : text.Substring(0, 4);
        }

        private static Dictionary<char, string> SparseLegend(char symbol, string name)
        {
            return new Dictionary<char, string>
            {
                ['?'] = "unknown/unrevealed/outside-world",
                ['.'] = "empty/no overlay object",
                [symbol] = name
            };
        }

        private static string RleEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var encoded = new StringBuilder();
            char current = value[0];
            int count = 1;
            for (int i = 1; i < value.Length; i++)
            {
                if (value[i] == current)
                {
                    count++;
                    continue;
                }

                AppendRun(encoded, current, count);
                current = value[i];
                count = 1;
            }
            AppendRun(encoded, current, count);
            return encoded.ToString();
        }

        private static void AppendRun(StringBuilder encoded, char symbol, int count)
        {
            if (count > 1)
                encoded.Append(count);
            encoded.Append(symbol);
        }

        private static Dictionary<string, object> MapRow(int y, int originY, string symbols, string encoding)
        {
            var row = new Dictionary<string, object>
            {
                ["y"] = y,
                ["ry"] = y - originY
            };
            if (encoding == "plain" || encoding == "both")
                row["p"] = symbols;
            if (encoding == "rle" || encoding == "both")
                row["r"] = RleEncode(symbols);
            return row;
        }

        private static string MarkdownRowRunLine(int y, int originY, int originX, List<string> rowTokens)
        {
            var runs = new List<string>();
            string current = null;
            int start = 0;
            int end = 0;

            for (int i = 0; i < rowTokens.Count; i++)
            {
                string token = string.IsNullOrWhiteSpace(rowTokens[i]) ? "unk" : rowTokens[i].Trim();
                if (current == null)
                {
                    current = token;
                    start = i;
                    end = i;
                    continue;
                }

                if (token == current)
                {
                    end = i;
                    continue;
                }

                runs.Add(MarkdownCellRun(originX, start, end, current));
                current = token;
                start = i;
                end = i;
            }

            if (current != null)
                runs.Add(MarkdownCellRun(originX, start, end, current));

            return "| `" + y + "` | `" + (y - originY) + "` | " + EscapeMarkdown(string.Join("; ", runs.ToArray())) + " |";
        }

        private static string MarkdownCellRun(int originX, int start, int end, string token)
        {
            int x1 = originX + start;
            int x2 = originX + end;
            string xRange = x1 == x2 ? x1.ToString() : x1 + ".." + x2;
            string rxRange = start == end ? start.ToString() : start + ".." + end;
            return "x=" + xRange + " rx=" + rxRange + " `" + token + "`";
        }
    }
}
