using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class CandidateLayoutBenchmark
    {
        private readonly struct Pickup
        {
            internal Pickup(long key, int pathCost, int quality, float freshness, int identity)
            {
                Key = key; PathCost = pathCost; Quality = quality;
                Freshness = freshness; Identity = identity; Reference = null;
            }
            internal readonly object Reference;
            internal readonly long Key;
            internal readonly int PathCost;
            internal readonly int Quality;
            internal readonly float Freshness;
            internal readonly int Identity;
        }

        private static readonly Func<Pickup, Pickup, bool> Better = IsBetter;

        private static bool IsBetter(Pickup a, Pickup b)
        {
            if (a.PathCost != b.PathCost) return a.PathCost < b.PathCost;
            if (a.Quality != b.Quality) return a.Quality > b.Quality;
            return a.Freshness > b.Freshness;
        }

        internal static void Verify()
        {
            VerifyInputCardinality();
            var data = Input(257);
            var expected = ExpectedWinners(data);
            var buffer = new FetchCandidateBuffer<long, Pickup>();
            var result = new List<Pickup>();
            foreach (var compact in new[] { false, true, true, false })
            {
                Collect(buffer, result, data, compact);
                VerifyWinners(result, expected);
            }
            // Equal-cost ties keep the first object. Equal hash codes remain distinct keys.
            var ties = new[] { new Pickup(0, 1, 2, float.NaN, 1),
                new Pickup(0, 1, 2, float.NaN, 2), new Pickup(0x100000001L, 1, 2, 3, 3) };
            foreach (var compact in new[] { false, true })
            {
                Collect(buffer, result, ties, compact);
                VerifyWinners(result, ExpectedWinners(ties));
                if (result.Count != 2 || !result.Any(p => p.Identity == 1) || !result.Any(p => p.Identity == 3))
                    throw new InvalidOperationException("Candidate ties or hash collision semantics changed");
                if (compact && (result[0].Identity != 1 || result[1].Identity != 3))
                    throw new InvalidOperationException("Compact candidate insertion order changed");
                Collect(buffer, result, Array.Empty<Pickup>(), compact);
                if (result.Count != 0) throw new InvalidOperationException("Candidate buffer leaked previous entries");
            }
            Console.WriteLine("PASS candidate layouts preserve winners, ties, colliding keys and reuse");
        }

        private static void VerifyInputCardinality()
        {
            // Include multiples of the shuffle stride and the supported endpoints.
            foreach (var keys in new[] { 1, 2, 30, 31, 62, 257, 961, 992, 65536 })
            {
                var data = Input(keys);
                var counts = data.GroupBy(p => p.Key).Select(g => g.Count()).ToArray();
                if (counts.Length != keys || counts.Any(count => count != 4))
                    throw new InvalidOperationException(
                        $"Candidate fixture requested {keys} keys but generated {counts.Length}");
                var expected = ExpectedWinners(data);
                var buffer = new FetchCandidateBuffer<long, Pickup>();
                var result = new List<Pickup>();
                foreach (var compact in new[] { false, true })
                {
                    Collect(buffer, result, data, compact);
                    VerifyWinners(result, expected);
                }
            }
        }

        private static Dictionary<long, Pickup> ExpectedWinners(Pickup[] data)
        {
            var expected = new Dictionary<long, Pickup>();
            foreach (var value in data)
                if (!expected.TryGetValue(value.Key, out var current) || IsBetter(value, current))
                    expected[value.Key] = value;
            return expected;
        }

        private static void VerifyWinners(List<Pickup> result, Dictionary<long, Pickup> expected)
        {
            var seen = new HashSet<long>();
            if (result.Count != expected.Count || result.Any(p =>
                !seen.Add(p.Key) || !expected.TryGetValue(p.Key, out var winner) ||
                winner.Identity != p.Identity))
                throw new InvalidOperationException("Candidate layout changed selected pickup identity");
        }

        private static Pickup[] Input(int keys)
        {
            var data = new Pickup[keys * 4];
            // 31 is prime: retain the original order except for multiples of 31.
            // keys - 1 is always coprime with keys, so every pass visits every key.
            var stride = keys % 31 == 0 ? keys - 1 : 31;
            for (var i = 0; i < data.Length; i++)
            {
                var key = (int)(((long)i * stride) % keys);
                data[i] = new Pickup(((long)key << 32) | (uint)(key % 9),
                    (i * 17) % 127, i % 5, (i % 101) / 100f, i);
            }
            return data;
        }

        private static void Collect(FetchCandidateBuffer<long, Pickup> buffer,
            List<Pickup> result, Pickup[] data, bool compact)
        {
            result.Clear();
            try
            {
                foreach (var value in data) buffer.Consider(compact, value.Key, value, result, Better);
                buffer.Complete(compact, result);
            }
            finally { buffer.Clear(); }
        }

        internal static void Run(string[] args)
        {
            if (args.Length != 4 || (args[1] != "baseline" && args[1] != "compact"))
                throw new ArgumentException("--candidate-layout-benchmark baseline|compact KEYS ITERATIONS");
            var keys = int.Parse(args[2], CultureInfo.InvariantCulture);
            var iterations = int.Parse(args[3], CultureInfo.InvariantCulture);
            if (keys < 1 || keys > 65536 || iterations < 1 || iterations > 100000)
                throw new ArgumentOutOfRangeException(nameof(args));
            var compact = args[1] == "compact";
            var data = Input(keys);
            var expected = ExpectedWinners(data);
            if (expected.Count != keys)
                throw new InvalidOperationException("Candidate fixture key count does not match requested workload");
            var buffer = new FetchCandidateBuffer<long, Pickup>();
            var result = new List<Pickup>(keys);
            for (var i = 0; i < 100; i++) Collect(buffer, result, data, compact);
            VerifyWinners(result, expected);
            Console.WriteLine("READY");
            if (Console.ReadLine() != "GO") throw new InvalidOperationException("No measurement handshake");
            long checksum = 0;
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < iterations; i++)
            {
                Collect(buffer, result, data, compact);
                foreach (var value in result) checksum = unchecked(checksum + value.Identity);
            }
            watch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Console.WriteLine("DONE");
            if (Console.ReadLine() != "REPORT") throw new InvalidOperationException("No report handshake");
            VerifyWinners(result, expected);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                layout = args[1], keys, inputsPerIteration = data.Length, iterations,
                wallSeconds = watch.Elapsed.TotalSeconds, allocatedBytes = allocated,
                checksum, winners = result.Count,
                limits = "Synthetic .NET 10 main-thread collection; production generic helper, not ONI Mono or whole-game FPS."
            }));
        }
    }
}
