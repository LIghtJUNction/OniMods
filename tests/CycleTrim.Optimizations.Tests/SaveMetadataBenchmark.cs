using System;
using System.Diagnostics;
using CycleTrim.Core;

namespace CycleTrim.Optimizations.Tests
{
    internal static class SaveMetadataBenchmark
    {
        // Informational paired measurements, never a noisy CI timing gate.
        // Both versions execute the original workload's two checks + one name.
        internal static void Run()
        {
            const int iterations = 200000;
            for (int warmup = 0; warmup < 3; warmup++)
            {
                Measure(false, iterations, -1);
                Measure(true, iterations, -1);
            }
            for (int sample = 0; sample < 9; sample++)
            {
                var first = Measure((sample & 1) != 0, iterations, sample);
                var second = Measure((sample & 1) == 0, iterations, sample);
                if (first != second)
                    throw new InvalidOperationException("save metadata benchmark checksum differs");
            }
        }

        private static long Measure(bool cached, int iterations, int sample)
        {
            var types = new[] { typeof(SyntheticSaveComponent), typeof(SaveMetadataRegression.Skipped),
                typeof(SaveMetadataRegression.Inherited), typeof(SaveMetadataRegression.Generic<int>),
                typeof(SaveMetadataRegression.Generic<string>) };
            long checksum = 0;
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++)
            {
                var type = types[index % types.Length];
                bool first = cached ? SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false)
                    : type.IsDefined(typeof(SkipSaveFileSerialization), false);
                bool second = cached ? SaveTypeMetadataCache.IsDefined(type, typeof(SkipSaveFileSerialization), false)
                    : type.IsDefined(typeof(SkipSaveFileSerialization), false);
                checksum += first ? 1 : 0;
                checksum += second ? 1 : 0;
                if (!second)
                    checksum += (cached ? SaveTypeMetadataCache.TypeName(type) : type.ToString()).Length;
            }
            var ticks = Stopwatch.GetTimestamp() - start;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            if (sample >= 0)
                Console.WriteLine("BENCH save-metadata sample=" + sample + " variant=" + (cached ? "candidate" : "baseline")
                    + " iterations=" + iterations + " ticks=" + ticks + " frequency=" + Stopwatch.Frequency
                    + " allocated=" + allocated + " checksum=" + checksum);
            return checksum;
        }
    }
}
