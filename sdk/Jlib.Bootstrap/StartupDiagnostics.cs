using System;
using System.Collections.Generic;

namespace OniMods.JlibBootstrap
{
    internal static class StartupDiagnostics
    {
        internal static void Report(string consumer, string phase, Exception error)
        {
            // Exception messages, stack traces, paths and invocation arguments may
            // contain private configuration. Types and HRESULTs identify the failure
            // boundary without copying that data into the game log.
            var failures = new List<string>();
            for (int depth = 0; error != null && depth < 8; depth++, error = error.InnerException)
                failures.Add(error.GetType().FullName + " (0x" + error.HResult.ToString("X8") + ")");
            Console.WriteLine("[Jlib bootstrap] " + consumer + " failed during " + phase + ": " + string.Join(" -> ", failures));
        }
    }
}
