using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class LogDiagnosticsSummary
    {
        public static JObject Analyze(JArray lines)
        {
            LogCounts counts = Count(lines ?? new JArray());
            int lineCount = lines?.Count ?? 0;
            bool critical = counts.NativeCrash > 0 || counts.Assertions > 0 || counts.ThreadOrDriver > 0;
            string severity = critical ? "critical" : lineCount > 0 ? "inspect" : "none";

            return new JObject
            {
                ["status"] = critical ? "critical" : lineCount > 0 ? "warning" : "clean",
                ["severity"] = severity,
                ["categories"] = new JObject
                {
                    ["nativeCrash"] = counts.NativeCrash,
                    ["exceptions"] = counts.Exceptions,
                    ["nullReference"] = counts.NullReference,
                    ["assertions"] = counts.Assertions,
                    ["threadOrDriver"] = counts.ThreadOrDriver,
                    ["oniMcpLines"] = counts.OniMcp,
                    ["genericErrors"] = counts.Errors
                },
                ["recommendation"] = Recommendation(counts, lineCount)
            };
        }

        private static LogCounts Count(JArray lines)
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

        private sealed class LogCounts
        {
            public int NativeCrash { get; set; }
            public int Exceptions { get; set; }
            public int NullReference { get; set; }
            public int Assertions { get; set; }
            public int ThreadOrDriver { get; set; }
            public int OniMcp { get; set; }
            public int Errors { get; set; }
        }
    }
}
