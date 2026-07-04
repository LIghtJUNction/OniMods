using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

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

            CallToolResult result = CurrentStateReadTools.ReadCurrent(forwarded);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("# Log Diagnostics");
            sb.AppendLine();
            sb.AppendLine("- Purpose: low-token crash/stability audit for tester agents.");
            sb.AppendLine("- Source: Player.log tail via current-state log scan.");
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

            JObject root = TryParseDiagnosticsJson(text);
            JObject logs = root?["logErrors"] as JObject;
            if (logs == null)
            {
                sb.AppendLine("## Summary");
                sb.AppendLine("- ok: false");
                sb.AppendLine("- status: unreadable");
                sb.AppendLine("- reason: logErrors missing from current-state response");
                return sb.ToString();
            }

            JArray lines = logs["lines"] as JArray ?? new JArray();
            LogCounts counts = CountLogs(lines);
            string status = counts.HasCritical ? "critical" : lines.Count > 0 ? "warning" : "clean";

            sb.AppendLine("## Summary");
            sb.AppendLine("- ok: " + (logs["ok"]?.ToString() ?? "unknown"));
            sb.AppendLine("- status: " + status);
            sb.AppendLine("- scannedTailLines: " + (logs["scannedTailLines"]?.ToString() ?? "0"));
            sb.AppendLine("- matches: " + (logs["matches"]?.ToString() ?? "0"));
            sb.AppendLine("- path: " + (logs["path"]?.ToString() ?? "unknown"));
            sb.AppendLine();

            sb.AppendLine("## Categories");
            sb.AppendLine("- nativeCrash: " + counts.NativeCrash);
            sb.AppendLine("- exceptions: " + counts.Exceptions);
            sb.AppendLine("- nullReference: " + counts.NullReference);
            sb.AppendLine("- assertions: " + counts.Assertions);
            sb.AppendLine("- threadOrDriver: " + counts.ThreadOrDriver);
            sb.AppendLine("- oniMcpLines: " + counts.OniMcp);
            sb.AppendLine("- genericErrors: " + counts.Errors);
            sb.AppendLine();

            sb.AppendLine("## Decision");
            sb.AppendLine("- severity: " + (counts.HasCritical ? "critical" : lines.Count > 0 ? "inspect" : "none"));
            sb.AppendLine("- recommendation: " + Recommendation(counts, lines.Count));
            sb.AppendLine("- nextRead: `/active/diagnostics/logs.md?logLimit=500` only if the matching tail misses the trigger.");
            sb.AppendLine();

            if (lines.Count == 0)
            {
                sb.AppendLine("## Recent Matches");
                sb.AppendLine("- none");
                return sb.ToString();
            }

            sb.AppendLine("## Recent Matches");
            sb.AppendLine("```text");
            foreach (JToken line in lines.Take(80))
                sb.AppendLine(TrimDiagnosticsLine(line?.ToString()));
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

        private static LogCounts CountLogs(JArray lines)
        {
            var counts = new LogCounts();
            foreach (JToken token in lines)
            {
                string line = token?.ToString() ?? string.Empty;
                if (ContainsAny(line, "SIGSEGV", "Native Crash", "Double Fault", "libnvidia"))
                    counts.NativeCrash++;
                if (ContainsAny(line, "Exception", "Traceback"))
                    counts.Exceptions++;
                if (ContainsAny(line, "NullReferenceException"))
                    counts.NullReference++;
                if (ContainsAny(line, "Assertion", "assertion"))
                    counts.Assertions++;
                if (ContainsAny(line, "mono_thread", "thread", "OpenGL", "EGL", "libnvidia"))
                    counts.ThreadOrDriver++;
                if (ContainsAny(line, "[OniMcp]", "OniMcp"))
                    counts.OniMcp++;
                if (ContainsAny(line, "Error", "error"))
                    counts.Errors++;
            }

            return counts;
        }

        private static bool ContainsAny(string line, params string[] needles)
        {
            return needles.Any(n => line.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Recommendation(LogCounts counts, int lineCount)
        {
            if (counts.NativeCrash > 0 || counts.Assertions > 0 || counts.ThreadOrDriver > 0)
                return "pause source edits, report crash context, avoid broad/high-frequency runtime reads until tester restarts safely";
            if (counts.NullReference > 0)
                return "inspect the named tool path and avoid UI-level game APIs in that handler";
            if (counts.Exceptions > 0 || counts.Errors > 0 || lineCount > 0)
                return "inspect recent matches and fix the named MCP handler before retrying the same tool";
            return "no suspicious tail entries found";
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

        private sealed class LogCounts
        {
            public int NativeCrash { get; set; }
            public int Exceptions { get; set; }
            public int NullReference { get; set; }
            public int Assertions { get; set; }
            public int ThreadOrDriver { get; set; }
            public int OniMcp { get; set; }
            public int Errors { get; set; }

            public bool HasCritical => NativeCrash > 0 || Assertions > 0 || ThreadOrDriver > 0;
        }
    }
}
