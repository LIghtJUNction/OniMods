using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using CycleTrim.Core;
using OniSim.Compatibility;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PresentationUploadNativeBenchmark
    {
        private const int Blocks = 17;
        private const int Iterations = 4;
        private const int WarmupPerSample = 4;

        // The regression entry runs the complete fixed matrix without reading
        // a clock. Count/content failures must be resolved before performance.
        internal static void Preflight(string path)
        {
            foreach (var shape in new[] { new[] { 96, 96 }, new[] { 256, 384 }, new[] { 736, 304 } })
                foreach (string scenario in new[] { "render_repeat", "zero_prepare", "command_two_prepares" })
                    using (var fixture = new PresentationUploadNativeFixture(path, shape[0], shape[1]))
                    {
                        var textures = new PropertyTextures(shape[0], shape[1]);
                        PresentationUploadGate.SetReader(fixture.Reader);
                        for (int i = 0; i < 8; i++) { fixture.Step(); textures.LateUpdate(); }
                        string expectedHash = null;
                        foreach (bool original in new[] { true, false })
                        {
                            for (int i = 0; i < WarmupPerSample; i++)
                                Advance(fixture, textures, scenario, original, i);
                            int loads = textures.Textures.Sum(texture => texture.LoadCalls);
                            int applies = textures.Textures.Sum(texture => texture.ApplyCalls);
                            for (int i = 0; i < Iterations; i++)
                                Advance(fixture, textures, scenario, original, i);
                            loads = textures.Textures.Sum(texture => texture.LoadCalls) - loads;
                            applies = textures.Textures.Sum(texture => texture.ApplyCalls) - applies;
                            int expected = original || scenario == "command_two_prepares" ? Iterations * 5 : 0;
                            PresentationUploadRegression.Check(loads == expected && applies == expected,
                                "preflight " + shape[0] + "x" + shape[1] + " " + scenario
                                + " original=" + original + " expected=" + expected
                                + " loads=" + loads + " applies=" + applies);
                            fixture.EqualPixels(textures);
                            string hash = Hash(textures);
                            if (expectedHash == null) expectedHash = hash;
                            PresentationUploadRegression.Check(hash == expectedHash,
                                "preflight original/candidate five-image SHA256 matches");
                            Console.WriteLine("preflight=" + shape[0] + "x" + shape[1] + ";scenario=" + scenario
                                + ";variant=" + (original ? "original" : "revision_gate")
                                + ";loads=" + loads + ";applies=" + applies + ";sha256=" + hash);
                        }
                    }
            Console.WriteLine("PASS nine-scene presentation count/content preflight; no performance timing");
        }

        internal static void Run(string path)
        {
            var harmony = PresentationUploadRegression.Install();
            try
            {
                Console.WriteLine("scope=authored native worker + NewGameFrame/Prepare exports + descriptor pointer publication + production managed upload gate + instrumented Texture2D CPU copies; no Unity graphics driver or game FPS");
                Console.WriteLine("runtime=" + Environment.Version + ";native=" + path + ";blocks=" + Blocks
                    + ";iterations=" + Iterations + ";warmup_per_sample=" + WarmupPerSample
                    + ";order=alternating ABBA/BAAB;gpu=" + Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_GPU")
                    + ";workers=" + Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_WORKERS")
                    + ";stopwatch_frequency=" + Stopwatch.Frequency
                    + ";tiered_compilation=" + Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"));
                Console.WriteLine("sample,shape,scenario,variant,block,position,ticks,iterations,loads,applies,loaded_bytes,managed_allocated_bytes,revision,sha256");
                foreach (var shape in new[] { new[] { 96, 96 }, new[] { 256, 384 }, new[] { 736, 304 } })
                    foreach (string scenario in new[] { "render_repeat", "zero_prepare", "command_two_prepares" })
                        Measure(path, shape[0], shape[1], scenario);
            }
            finally
            {
                PresentationUploadGate.SetReader((OniSimPublicationReader)null);
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static void Measure(string path, int width, int height, string scenario)
        {
            using (var fixture = new PresentationUploadNativeFixture(path, width, height))
            {
                var textures = new PropertyTextures(width, height);
                PresentationUploadGate.SetReader(fixture.Reader);
                for (int i = 0; i < 8; i++) { fixture.Step(); textures.LateUpdate(); }
                var baseline = new List<double>();
                var candidate = new List<double>();
                string expectedHash = null;
                for (int block = 0; block < Blocks; block++)
                {
                    for (int position = 0; position < 4; position++)
                    {
                        bool original = (position == 0 || position == 3) == (block % 2 == 0);
                        for (int i = 0; i < WarmupPerSample; i++)
                            Advance(fixture, textures, scenario, original, i);
                        long loaded = textures.Textures.Sum(texture => texture.LoadedBytes);
                        int loads = textures.Textures.Sum(texture => texture.LoadCalls);
                        int applies = textures.Textures.Sum(texture => texture.ApplyCalls);
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long start = Stopwatch.GetTimestamp();
                        for (int i = 0; i < Iterations; i++)
                            Advance(fixture, textures, scenario, original, i);
                        long ticks = Stopwatch.GetTimestamp() - start;
                        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        loaded = textures.Textures.Sum(texture => texture.LoadedBytes) - loaded;
                        loads = textures.Textures.Sum(texture => texture.LoadCalls) - loads;
                        applies = textures.Textures.Sum(texture => texture.ApplyCalls) - applies;
                        int expectedUploads = original || scenario == "command_two_prepares" ? Iterations * 5 : 0;
                        PresentationUploadRegression.Check(loads == expectedUploads && applies == expectedUploads,
                            "measured sample has the expected five-image upload count");
                        fixture.EqualPixels(textures);
                        string hash = Hash(textures);
                        if (expectedHash == null) expectedHash = hash;
                        PresentationUploadRegression.Check(hash == expectedHash,
                            "paired baseline/candidate samples publish identical five-image bytes");
                        (original ? baseline : candidate).Add(ticks * 1000d / Stopwatch.Frequency / Iterations);
                        Console.WriteLine(string.Join(",", new object[] { "sample", width + "x" + height,
                            scenario, original ? "original" : "revision_gate", block, position,
                            ticks, Iterations, loads, applies, loaded, allocated, fixture.Metadata().Revision, hash }));
                    }
                }
                Summary(width, height, scenario, "original", baseline);
                Summary(width, height, scenario, "revision_gate", candidate);
            }
        }

        private static void Advance(PresentationUploadNativeFixture fixture, PropertyTextures textures,
            string scenario, bool original, int index)
        {
            if (scenario == "zero_prepare") fixture.Step();
            else if (scenario == "command_two_prepares") fixture.ChangeAndStep(index);
            if (original) textures.RenderUnpatched();
            else textures.LateUpdate();
        }

        private static string Hash(PropertyTextures textures)
        {
            using (var hash = SHA256.Create())
            {
                for (int i = 0; i < 5; i++)
                {
                    byte[] bytes = textures.Textures[i].UploadedBytes;
                    hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Summary(int width, int height, string scenario, string variant, List<double> samples)
        {
            samples.Sort();
            double median = (samples[samples.Count / 2 - 1] + samples[samples.Count / 2]) / 2;
            double p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
            Console.WriteLine("summary=" + width + "x" + height + ";scenario=" + scenario + ";variant=" + variant
                + ";samples=" + samples.Count + ";median_ms=" + median.ToString("F6", CultureInfo.InvariantCulture)
                + ";p95_sample_mean_ms=" + p95.ToString("F6", CultureInfo.InvariantCulture)
                + ";min_ms=" + samples[0].ToString("F6", CultureInfo.InvariantCulture)
                + ";max_ms=" + samples[samples.Count - 1].ToString("F6", CultureInfo.InvariantCulture));
        }
    }
}
