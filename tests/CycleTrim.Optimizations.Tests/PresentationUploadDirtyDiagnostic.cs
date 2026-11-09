using System;
using System.Diagnostics;
using System.Linq;
using CycleTrim.Core;
using OniSim.Compatibility;

namespace CycleTrim.Optimizations.Tests
{
    // Independent causal diagnostic, not an acceptance benchmark. The fixed
    // nine-scene benchmark and its warmup/sample rules are left unchanged.
    internal static class PresentationUploadDirtyDiagnostic
    {
        private const int Blocks = 9;

        internal static void Run(string path)
        {
            var harmony = PresentationUploadRegression.Install();
            try
            {
                Console.WriteLine("scope=diagnostic only; dirty upload with frozen native pixels and changing synthetic revision; metadata-only query; separately timed real native command/upload phases");
                Console.WriteLine("runtime=" + Environment.Version + ";frequency=" + Stopwatch.Frequency
                    + ";tiered=" + Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"));
                Console.WriteLine("diagnostic,mode,variant,block,position,iterations,total_ticks,native_ticks,upload_ticks,loads,applies,revision_delta,last_handover_frames");
                using (var fixture = new PresentationUploadNativeFixture(path, 96, 96))
                {
                    var textures = new PropertyTextures(96, 96);
                    PresentationUploadGate.SetReader(fixture.Reader);
                    for (int i = 0; i < 8; i++) { fixture.Step(); textures.LateUpdate(); }
                    PresentationUpdate snapshot = fixture.Metadata();
                    ulong syntheticRevision = snapshot.Revision;
                    PresentationUploadGate.ReadPublication cached = delegate(out PresentationUpdate update)
                    {
                        update = snapshot;
                        return true;
                    };
                    PresentationUploadGate.SetReader(delegate(out PresentationUpdate update)
                    {
                        update = snapshot;
                        update.Revision = ++syntheticRevision;
                        return true;
                    });
                    Uploads(fixture, textures, false);
                    Queries(cached, fixture.Reader.TryGetPresentationUpdate);
                    PresentationUploadGate.SetReader(fixture.Reader);
                    Uploads(fixture, textures, true);
                }
            }
            finally
            {
                PresentationUploadGate.SetReader((OniSimPublicationReader)null);
                harmony.UnpatchAll(harmony.Id);
            }
        }

        private static bool Original(int block, int position)
        {
            return (position == 0 || position == 3) == (block % 2 == 0);
        }

        private static void Render(PropertyTextures textures, bool original)
        {
            if (original) textures.RenderUnpatched(); else textures.LateUpdate();
        }

        private static void Uploads(PresentationUploadNativeFixture fixture, PropertyTextures textures, bool commands)
        {
            int iterations = commands ? 32 : 1024;
            for (int block = 0; block < Blocks; block++)
                for (int position = 0; position < 4; position++)
                {
                    bool original = Original(block, position);
                    for (int i = 0; i < 16; i++)
                    {
                        if (commands) fixture.ChangeAndStep(i);
                        Render(textures, original);
                    }
                    int loads = textures.Textures.Sum(texture => texture.LoadCalls);
                    int applies = textures.Textures.Sum(texture => texture.ApplyCalls);
                    ulong revision = fixture.Metadata().Revision;
                    long nativeTicks = 0, uploadTicks = 0, ticks;
                    int lastFrames = 0;
                    if (commands)
                    {
                        for (int i = 0; i < iterations; i++)
                        {
                            long start = Stopwatch.GetTimestamp();
                            fixture.ChangeAndStep(i);
                            long middle = Stopwatch.GetTimestamp();
                            Render(textures, original);
                            long end = Stopwatch.GetTimestamp();
                            nativeTicks += middle - start;
                            uploadTicks += end - middle;
                            lastFrames += fixture.LastFramesProcessed;
                        }
                        ticks = nativeTicks + uploadTicks;
                    }
                    else
                    {
                        long start = Stopwatch.GetTimestamp();
                        for (int i = 0; i < iterations; i++) Render(textures, original);
                        ticks = Stopwatch.GetTimestamp() - start;
                        uploadTicks = ticks;
                    }
                    loads = textures.Textures.Sum(texture => texture.LoadCalls) - loads;
                    applies = textures.Textures.Sum(texture => texture.ApplyCalls) - applies;
                    PresentationUploadRegression.Check(loads == iterations * 5 && applies == iterations * 5,
                        "dirty diagnostic must execute every original upload");
                    fixture.EqualPixels(textures);
                    ulong revisionDelta = fixture.Metadata().Revision - revision;
                    Console.WriteLine(string.Join(",", new object[] { "diagnostic",
                        commands ? "command_phases" : "dirty_upload_no_ffi", original ? "original" : "revision_gate",
                        block, position, iterations, ticks, nativeTicks, uploadTicks, loads, applies, revisionDelta, lastFrames }));
                }
        }

        private static void Queries(PresentationUploadGate.ReadPublication cached, PresentationUploadGate.ReadPublication native)
        {
            const int iterations = 32768;
            for (int block = 0; block < Blocks; block++)
                for (int position = 0; position < 4; position++)
                {
                    bool original = Original(block, position);
                    var read = original ? cached : native;
                    PresentationUpdate update;
                    ulong checksum = 0;
                    for (int i = 0; i < 1024; i++)
                        PresentationUploadRegression.Check(read(out update), "metadata diagnostic warmup is valid");
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < iterations; i++)
                    {
                        if (!read(out update)) throw new InvalidOperationException("metadata query became unavailable");
                        checksum ^= update.Revision + update.CellCount;
                    }
                    long ticks = Stopwatch.GetTimestamp() - start;
                    GC.KeepAlive(checksum);
                    Console.WriteLine(string.Join(",", new object[] { "diagnostic", "metadata_only",
                        original ? "cached_struct" : "native_query", block, position, iterations, ticks,
                        0, 0, 0, 0, 0, 0 }));
                }
        }
    }
}
