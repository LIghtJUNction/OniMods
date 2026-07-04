using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendGuidedAxis(StringBuilder sb, int xMin, int xMax, Func<int, int> digit)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                if (x > xMin && (x - xMin) % 5 == 0)
                    sb.Append("┆ ");

                sb.Append(digit(x)).Append(' ');
            }
        }

        private static string InsertGridGuides(string line, bool compact = true)
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || !line.StartsWith("Y=", StringComparison.Ordinal) || line.Contains(".."))
                return line;

            string[] tokens = line.Substring(colon + 1)
                .Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return line;

            var sb = new StringBuilder(line.Substring(0, colon + 1));
            for (int start = 0; start < tokens.Length; start += 5)
            {
                if (start > 0)
                    sb.Append(" ┆");
                if (compact)
                    AppendCompressedSegment(sb, tokens, start, Math.Min(start + 5, tokens.Length));
                else
                    AppendRawSegment(sb, tokens, start, Math.Min(start + 5, tokens.Length));
            }

            return sb.ToString();
        }

        private static void AppendCompressedSegment(StringBuilder sb, string[] tokens, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                string token = tokens[i];
                int count = 1;
                while (i + count < end && tokens[i + count] == token)
                    count++;

                sb.Append(' ').Append(token);
                if (count > 1)
                    sb.Append('x').Append(count);
                i += count;
            }
        }

        private static void AppendRawSegment(StringBuilder sb, string[] tokens, int start, int end)
        {
            for (int i = start; i < end; i++)
                sb.Append(' ').Append(tokens[i]);
        }

        private static IEnumerable<string> CompressBlankGridLines(List<string> gridLines)
        {
            int blankStart = -1;
            var blankLines = new List<string>();
            foreach (string line in gridLines)
            {
                if (IsBlankGridLine(line, out int y))
                {
                    if (blankStart < 0)
                        blankStart = y;

                    blankLines.Add(line);
                    continue;
                }

                FlushBlankGridLines(blankLines, blankStart, out var flushed);
                foreach (string item in flushed)
                    yield return item;

                blankLines.Clear();
                blankStart = -1;
                yield return line;
            }

            FlushBlankGridLines(blankLines, blankStart, out var tail);
            foreach (string item in tail)
                yield return item;
        }

        private static void FlushBlankGridLines(List<string> lines, int startY, out List<string> output)
        {
            output = new List<string>();
            if (lines.Count == 0)
                return;

            if (lines.Count < 3)
            {
                output.AddRange(lines);
                return;
            }

            int endY = startY - lines.Count + 1;
            output.Add("Y=" + startY.ToString("D3") + ".." + endY.ToString("D3")
                + ": . (" + lines.Count + " 行空白省略)");
        }

        private static bool IsBlankGridLine(string line, out int y)
        {
            y = 0;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("Y=", StringComparison.Ordinal))
                return false;

            int colon = line.IndexOf(':');
            if (colon <= 2 || !int.TryParse(line.Substring(2, colon - 2), out y))
                return false;

            string payload = line.Substring(colon + 1).Trim();
            if (payload.Length == 0)
                return false;

            return payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .All(token => token == ".");
        }
    }
}
