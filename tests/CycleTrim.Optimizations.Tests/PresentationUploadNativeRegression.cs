using System;
using System.Linq;
using CycleTrim.Core;
using OniSim.Compatibility;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PresentationUploadNativeRegression
    {
        internal static void Run(string path, bool benchmark)
        {
            if (benchmark) { PresentationUploadNativeBenchmark.Run(path); return; }
            var harmony = PresentationUploadRegression.Install();
            OniSimPublicationReader reader = null;
            ulong generation;
            var textures = new PropertyTextures(32, 32);
            try
            {
                using (var fixture = new PresentationUploadNativeFixture(path, 32, 32))
                {
                    reader = fixture.Reader;
                    if (fixture.Api.GetPresentationUpdate == null)
                    {
                        PresentationUploadGate.SetReader(reader);
                        for (int i = 0; i < 4; i++) { fixture.Step(); textures.LateUpdate(); }
                        PresentationUploadRegression.Check(textures.Textures.All(texture => texture.ApplyCalls == 4),
                            "a real older native library retains every original upload");
                        fixture.EqualPixels(textures);
                        Console.WriteLine("PASS older native library without presentation export retains all uploads");
                        return;
                    }
                    PresentationUpdate update;
                    PresentationUploadRegression.Check(!reader.TryGetPresentationUpdate(out update),
                        "Start alone does not publish a derived revision");
                    PresentationUploadGate.SetReader(reader);
                    for (int i = 0; i < 8; i++)
                    {
                        fixture.Step(); textures.LateUpdate();
                        Console.WriteLine("warmup=" + i + ";frames=" + fixture.LastFramesProcessed
                            + ";revision=" + fixture.Metadata().Revision);
                    }
                    update = fixture.Metadata();
                    generation = update.Generation;
                    int before = textures.Textures.Sum(texture => texture.ApplyCalls);
                    IntPtr pointer = update.Flow;
                    bool sawAlternatePointer = false;
                    for (int i = 0; i < 12; i++)
                    {
                        if (i % 2 == 0) fixture.Step();
                        else fixture.PrepareOnly();
                        textures.LateUpdate();
                        var next = fixture.Metadata();
                        Console.WriteLine("check=" + i + ";frames=" + fixture.LastFramesProcessed + ";revision=" + next.Revision);
                        PresentationUploadRegression.Check(next.Revision == update.Revision,
                            "warmed zero-time publication revision is stable: expected=" + update.Revision
                            + ", actual=" + next.Revision + ", processed_frames=" + fixture.LastFramesProcessed);
                        sawAlternatePointer |= pointer != next.Flow;
                        fixture.EqualPixels(textures);
                    }
                    PresentationUploadRegression.Check(sawAlternatePointer, "real worker alternates native buffers");
                    PresentationUploadRegression.Check(textures.Textures.Sum(texture => texture.ApplyCalls) == before,
                        "twelve real alternating empty/zero-time handovers require no redundant texture upload");
                    int lerpers = textures.LerpUpdates;
                    for (int i = 0; i < 4; i++) textures.LateUpdate();
                    PresentationUploadRegression.Check(textures.LerpUpdates == lerpers + 12,
                        "render-only frames retain interpolation without native publication");

                    byte[][] previousPixels = textures.Textures.Select(texture => (byte[])texture.UploadedBytes.Clone()).ToArray();
                    fixture.ChangeAndStep(2);
                    PresentationUploadRegression.Check(fixture.Metadata().Revision > update.Revision,
                        "a zero-time cell command produces a new derived revision");
                    textures.LateUpdate();
                    PresentationUploadRegression.Check(textures.Textures.Sum(texture => texture.ApplyCalls) == before + 5,
                        "a changed real publication uploads all five images immediately");
                    fixture.EqualPixels(textures);
                    PresentationUploadRegression.Check(textures.Textures.Select((texture, i) =>
                        !texture.UploadedBytes.SequenceEqual(previousPixels[i])).Any(changed => changed),
                        "the authored command changes visible pixel bytes");

                    reader.Acquire();
                    try
                    {
                        PresentationUploadRegression.Check(!reader.TryGetPresentationUpdate(out update),
                            "managed query declines during an existing species lease");
                        PresentationUploadRegression.Check(fixture.Api.GetPresentationUpdate(out update, 64) == 0,
                            "direct native query declines instead of deadlocking its own State mutex");
                    }
                    finally { reader.Release(); }
                    PresentationUploadRegression.Check(fixture.Api.GetPresentationUpdate(out update, 63) == 0,
                        "native export rejects unknown output layouts");
                    fixture.Metadata();
                }
                PresentationUpdate absent;
                PresentationUploadRegression.Check(!reader.TryGetPresentationUpdate(out absent), "shutdown invalidates publication metadata");

                // Keep the original managed reader and uploaded textures. A
                // recreated session must still invalidate the old upload stamp.
                using (var fixture = new PresentationUploadNativeFixture(path, 32, 32))
                {
                    fixture.Step();
                    PresentationUploadRegression.Check(fixture.Metadata().Generation != generation,
                        "native generation changes after shutdown/reinitialization");
                    int before = textures.Textures.Sum(texture => texture.ApplyCalls);
                    textures.LateUpdate();
                    PresentationUploadRegression.Check(textures.Textures.Sum(texture => texture.ApplyCalls) == before + 5,
                        "recreated session uploads even through the persistent reader");
                    fixture.EqualPixels(textures);
                }
                Console.WriteLine("PASS real native worker -> Prepare descriptor -> managed revision gate -> instrumented Unity uploads; graphics hardware not exercised");
            }
            finally
            {
                PresentationUploadGate.SetReader((OniSimPublicationReader)null);
                harmony.UnpatchAll(harmony.Id);
            }
        }
    }
}
