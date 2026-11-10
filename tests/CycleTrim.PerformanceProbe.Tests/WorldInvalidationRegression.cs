using System;
using System.Threading.Tasks;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class WorldInvalidationRegression
    {
        internal static void Run()
        {
            var versions = new WorldInvalidationVersions();
            versions.Bump(7);
            Check(versions.Get(7) == 1 && versions.Get(8) == 0,
                "unrelated world must retain its cached work");
            versions.Bump(-1);
            Check(versions.Get(7) == 2 && versions.Get(8) == 1,
                "unknown world invalidates every reader");
            versions.Bump(1000);
            Check(versions.Get(8) == 2 && versions.Get(-1) == 3,
                "unsupported world cannot silently miss changes");
            Parallel.For(0, 10000, i => versions.Bump(7));
            Check(versions.Get(7) == 10003 && versions.Get(8) == 2,
                "concurrent increments remain local and cannot be lost");
            Check(versions.Total == 10003, "global diagnostic counts all events");
            Console.WriteLine("PASS scoped world invalidation");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
