using System;
using System.Diagnostics;

namespace CycleTrim.Optimizations.Tests
{
    internal static class AnimationSymbolBenchmark
    {
        private sealed class Scenario
        {
            internal readonly string Name;
            internal readonly int Range;
            internal readonly bool Change;
            internal readonly bool ChangeAtEnd;
            internal Scenario(string name, int range, bool change, bool changeAtEnd = false)
            { Name = name; Range = range; Change = change; ChangeAtEnd = changeAtEnd; }
        }

        private struct Sample
        {
            internal long Checksum, Copies, CopiedBytes;
        }

        internal static void Run()
        {
            AnimationSymbolFixture.InstallOriginalCopies();
            AnimationSymbolRegression.Enable();
            var scenarios = new[]
            {
                new Scenario("repeat-single", 1, false),
                new Scenario("change-single", 1, true),
                new Scenario("repeat-range4", 4, false),
                new Scenario("repeat-range256", 256, false),
                new Scenario("change-range4", 4, true),
                new Scenario("change-range256", 256, true),
                new Scenario("change-last-range256", 256, true, changeAtEnd: true)
            };
            Console.WriteLine("BENCH animation-config warmups=3 pairs=7 symbols=256"
                + " updates=30000 settersPerUpdate=1 consumer=managed-byte-copy"
                + " firstUpload=excluded timingGate=none");
            foreach (var scenario in scenarios)
            {
                foreach (bool consume in new[] { false, true })
                {
                    for (int warmup = 0; warmup < 3; warmup++)
                    {
                        Measure(scenario, consume, false, 5000, -1);
                        Measure(scenario, consume, true, 5000, -1);
                    }
                    for (int pair = 0; pair < 7; pair++)
                    {
                        var first = Measure(scenario, consume, (pair & 1) != 0, 30000, pair);
                        var second = Measure(scenario, consume, (pair & 1) == 0, 30000, pair);
                        if (first.Checksum != second.Checksum)
                            throw new InvalidOperationException("Animation benchmark payload mismatch");
                        if (consume)
                        {
                            var baseline = (pair & 1) == 0 ? first : second;
                            var candidate = (pair & 1) == 0 ? second : first;
                            long expectedCopies = scenario.Change ? 30000 : 0;
                            if (baseline.Copies != 30000 || candidate.Copies != expectedCopies
                                || baseline.CopiedBytes != 30000L * 256 * 48
                                || candidate.CopiedBytes != expectedCopies * 256 * 48)
                                throw new InvalidOperationException("Animation benchmark copy contract mismatch");
                        }
                    }
                }
            }
        }

        private static Sample Measure(Scenario scenario, bool consume, bool candidate, int updates, int sample)
        {
            const int symbols = 256;
            var data = new SymbolOverrideInfoGpuData(symbols);
            var frames = new KAnim.Build.SymbolFrameInstance[scenario.Range];
            for (int index = 0; index < frames.Length; index++)
                frames[index] = AnimationSymbolFixture.Frame(index + 1);
            var source = new KBatchGroupData(frames);
            var frame = frames[scenario.ChangeAtEnd ? frames.Length - 1 : 0];
            if (scenario.Change && scenario.Range == 1) frame.uvMin.x = 0;
            int atlas = 3;
            if (scenario.Range == 1)
                AnimationSymbolFixture.OriginalOne(data, 0, ref frame);
            else
                AnimationSymbolFixture.OriginalRange(data, 0, scenario.Range, 3, source, 0, frames.Length);
            var texture = new AnimationSymbolFixture.TextureConsumer(symbols);
            texture.Update(data);
            texture.Copies = texture.BytesCopied = 0;

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int update = 0; update < updates; update++)
            {
                if (scenario.Change)
                {
                    if (scenario.Range == 1)
                        frame.uvMin.x = (update & 1) + 1;
                    else if (scenario.ChangeAtEnd)
                    {
                        frame.uvMax.y = (update & 1) + 1;
                        source.symbolFrameInstances[frames.Length - 1] = frame;
                    }
                    else
                        atlas = (update & 1) + 1;
                }
                if (scenario.Range == 1)
                {
                    if (candidate) data.SetSymbolOverrideInfo(0, ref frame);
                    else AnimationSymbolFixture.OriginalOne(data, 0, ref frame);
                }
                else
                {
                    if (candidate) data.SetSymbolOverrideInfo(0, scenario.Range, atlas, source, 0, frames.Length);
                    else AnimationSymbolFixture.OriginalRange(data, 0, scenario.Range, atlas, source, 0, frames.Length);
                }
                if (consume) texture.Update(data);
            }
            long ticks = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            var bytes = data.Snapshot();
            if (consume && !bytes.AsSpan().SequenceEqual(texture.Bytes))
                throw new InvalidOperationException("Animation consumer does not contain current bytes");
            long checksum = 0;
            foreach (byte value in bytes) checksum = unchecked(checksum * 31 + value);
            if (sample >= 0)
                Console.WriteLine("BENCH animation-symbols scenario=" + scenario.Name
                    + " mode=" + (consume ? "consumer" : "setter") + " sample=" + sample
                    + " variant=" + (candidate ? "candidate" : "baseline")
                    + " symbols=" + symbols + " range=" + scenario.Range + " settersPerUpdate=1"
                    + " updates=" + updates + " ticks=" + ticks + " frequency=" + Stopwatch.Frequency
                    + " allocated=" + allocated + " copies=" + texture.Copies
                    + " copiedBytes=" + texture.BytesCopied + " checksum=" + checksum);
            return new Sample { Checksum = checksum, Copies = texture.Copies, CopiedBytes = texture.BytesCopied };
        }
    }
}
