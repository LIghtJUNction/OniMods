using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string ReadLogDiagnosticsMarkdown(JObject args)
        {
            var forwarded = new JObject(args ?? new JObject())
            {
                ["domain"] = "state",
                ["action"] = "current",
                ["includeLogs"] = true,
                ["includeLogErrors"] = true,
                ["profile"] = "minimal"
            };
            forwarded.Remove("command");
            forwarded.Remove("path");

            if (forwarded["logLimit"] == null)
                forwarded["logLimit"] = 220;

            var result = CurrentStateReadTools.ReadCurrent(forwarded);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("# Log Diagnostics");
            sb.AppendLine();
            sb.AppendLine("- Purpose: low-token crash/stability audit for tester agents.");
            sb.AppendLine("- Source: Player.log tail via `read_control domain=state action=current includeLogs=true`.");
            sb.AppendLine("- Tip: increase `logLimit` only when the recent tail is not enough.");
            sb.AppendLine();

            if (result.IsError)
            {
                sb.AppendLine("## Error");
                sb.AppendLine("```text");
                sb.AppendLine(TrimDiagnosticsText(text, 4000));
                sb.AppendLine("```");
                return sb.ToString();
            }

            var root = TryParseDiagnosticsJson(text);
            var logs = root?["logErrors"] as JObject;
            if (logs == null)
            {
                sb.AppendLine("## Summary");
                sb.AppendLine("- ok: false");
                sb.AppendLine("- reason: logErrors missing from current-state response");
                return sb.ToString();
            }

            sb.AppendLine("## Summary");
            sb.AppendLine("- ok: " + (logs["ok"]?.ToString() ?? "unknown"));
            sb.AppendLine("- scannedTailLines: " + (logs["scannedTailLines"]?.ToString() ?? "0"));
            sb.AppendLine("- matches: " + (logs["matches"]?.ToString() ?? "0"));
            sb.AppendLine("- path: " + (logs["path"]?.ToString() ?? "unknown"));
            sb.AppendLine();

            var lines = logs["lines"] as JArray;
            if (lines == null || lines.Count == 0)
            {
                sb.AppendLine("## Recent Matches");
                sb.AppendLine("- none");
                return sb.ToString();
            }

            sb.AppendLine("## Recent Matches");
            sb.AppendLine("```text");
            foreach (var line in lines.Take(80))
                sb.AppendLine(TrimDiagnosticsLine(line?.ToString() ?? string.Empty));
            sb.AppendLine("```");
            return sb.ToString();
        }

        private static JObject TryParseDiagnosticsJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JObject.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private static string TrimDiagnosticsText(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "\n... truncated";
        }

        private static string TrimDiagnosticsLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line.Length <= 240)
                return line ?? string.Empty;
            return line.Substring(0, 240) + " ...";
        }
    }
}
