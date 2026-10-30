using System;
using CycleTrim.Core;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ConduitVisualRefreshGateRegression
    {
        private static int assertions;

        internal static void Run()
        {
            FirstUseAndCompletion();
            FrameRates();
            FullAndInvalidQuality();
            MaximumAge();
            TimeAndFrameRewinds();
            InvalidClocks();
            InvalidationAndFailedBuilds();
            FrameIntegerBounds();
            ClockPrecision();
            SteadyStateAllocations();
            Console.WriteLine("PASS conduit visual refresh gate: " + assertions
                + " assertions; cadence, age, failed rebuilds, clock/frame resets and zero allocation");
        }

        private static void FirstUseAndCompletion()
        {
            var gate = new ConduitVisualRefreshGate();
            Check(gate.ShouldRebuild(0, 0, ConduitAnimationQuality.Quarter, false),
                "first frame needs a completed mesh");
            Check(gate.ShouldRebuild(0, 0, ConduitAnimationQuality.Quarter, false),
                "an attempted build is not a successful build");
            gate.Complete(0, 0);
            Check(!gate.ShouldRebuild(0, 0, ConduitAnimationQuality.Quarter, false),
                "repeated draw in the completed frame reuses its mesh");
            Check(!gate.ShouldRebuild(0, 0.01, ConduitAnimationQuality.Half, false),
                "same frame with a newer clock still reuses a fresh mesh");
            Check(!gate.ShouldRebuild(1, 0.02, ConduitAnimationQuality.Half, false),
                "half quality reuses the next frame");
            Check(gate.ShouldRebuild(2, 0.03, ConduitAnimationQuality.Half, false),
                "half quality rebuilds on the second frame");
        }

        private static void FrameRates()
        {
            foreach (var quality in new[] { ConduitAnimationQuality.Full,
                ConduitAnimationQuality.Half, ConduitAnimationQuality.Quarter })
            {
                var gate = new ConduitVisualRefreshGate();
                int rebuilds = 0;
                for (int frame = 0; frame < 240; frame++)
                {
                    double now = frame / 120.0;
                    if (gate.ShouldRebuild(frame, now, quality, false))
                    {
                        rebuilds++;
                        gate.Complete(frame, now);
                    }
                }
                Equal(240 / (int)quality, rebuilds, "high-FPS rebuild count: " + quality);
            }

            var frozenClock = new ConduitVisualRefreshGate();
            int frozenRebuilds = 0;
            for (int frame = 0; frame < 12; frame++)
            {
                if (frozenClock.ShouldRebuild(frame, 2, ConduitAnimationQuality.Quarter, false))
                {
                    frozenRebuilds++;
                    frozenClock.Complete(frame, 2);
                }
            }
            Equal(3, frozenRebuilds, "frame cadence still advances when the clock is unchanged");
        }

        private static void FullAndInvalidQuality()
        {
            foreach (var quality in new[] { ConduitAnimationQuality.Full,
                (ConduitAnimationQuality)0, (ConduitAnimationQuality)3,
                (ConduitAnimationQuality)(-1), (ConduitAnimationQuality)int.MaxValue })
            {
                var gate = new ConduitVisualRefreshGate();
                gate.Complete(10, 1);
                Check(gate.ShouldRebuild(10, 1, quality, false),
                    "full or unknown quality always uses the original rebuild: " + quality);
                Check(gate.ShouldRebuild(10, 1, ConduitAnimationQuality.Quarter, false),
                    "full or unknown quality revokes the previous mesh before returning");
                gate.Complete(10, 1);
                Check(gate.ShouldRebuild(10, 1, quality, false),
                    "completing a mesh never throttles full or unknown quality");
            }
        }

        private static void MaximumAge()
        {
            var gate = new ConduitVisualRefreshGate();
            gate.Complete(10, 0);
            double justBefore = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(ConduitVisualRefreshGate.MaximumAgeSeconds) - 1);
            Check(!gate.ShouldRebuild(11, justBefore, ConduitAnimationQuality.Quarter, false),
                "a mesh one representable instant below the age limit can be reused");
            Check(gate.ShouldRebuild(11, ConduitVisualRefreshGate.MaximumAgeSeconds,
                ConduitAnimationQuality.Quarter, false), "the age boundary forces a rebuild");
            gate.Complete(11, ConduitVisualRefreshGate.MaximumAgeSeconds);
            Check(!gate.ShouldRebuild(11, ConduitVisualRefreshGate.MaximumAgeSeconds,
                ConduitAnimationQuality.Quarter, false), "completion resets age in the same frame");
            Check(gate.ShouldRebuild(11, 0.21, ConduitAnimationQuality.Quarter, false),
                "elapsed real time forces a rebuild even without a frame advance");

            foreach (var quality in new[] { ConduitAnimationQuality.Half, ConduitAnimationQuality.Quarter })
            {
                gate = new ConduitVisualRefreshGate();
                int rebuilds = 0;
                for (int frame = 0; frame < 10; frame++)
                {
                    double now = frame * 0.2;
                    if (gate.ShouldRebuild(frame, now, quality, false))
                    {
                        rebuilds++;
                        gate.Complete(frame, now);
                    }
                }
                Equal(10, rebuilds, "low FPS refreshes every visible frame: " + quality);
            }
        }

        private static void TimeAndFrameRewinds()
        {
            var gate = new ConduitVisualRefreshGate();
            gate.Complete(100, 10);
            Check(!gate.ShouldRebuild(101, 10.08, ConduitAnimationQuality.Quarter, false),
                "record a skipped clock observation");
            Check(gate.ShouldRebuild(102, 10.04, ConduitAnimationQuality.Quarter, false),
                "rewind relative to the skipped observation forces a rebuild");
            gate.Complete(102, 10.04);
            Check(!gate.ShouldRebuild(103, 10.05, ConduitAnimationQuality.Quarter, false),
                "successful rebuild establishes the new clock origin");

            gate.Complete(100, 20);
            Check(!gate.ShouldRebuild(103, 20.01, ConduitAnimationQuality.Quarter, false),
                "record a skipped frame observation");
            Check(gate.ShouldRebuild(102, 20.02, ConduitAnimationQuality.Quarter, false),
                "frame rewind relative to a skipped frame forces a rebuild");
            gate.Complete(102, 20.02);
            Check(gate.ShouldRebuild(101, 20.01, ConduitAnimationQuality.Quarter, false),
                "both clocks can reset together");
        }

        private static void InvalidClocks()
        {
            foreach (double now in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var gate = new ConduitVisualRefreshGate();
                gate.Complete(1, 0);
                Check(gate.ShouldRebuild(1, now, ConduitAnimationQuality.Quarter, false),
                    "non-finite time forces a rebuild");
                Check(gate.ShouldRebuild(1, 0.01, ConduitAnimationQuality.Quarter, false),
                    "invalid clock observation cannot preserve an old mesh");
                gate.Complete(1, now);
                Check(gate.ShouldRebuild(1, 0.01, ConduitAnimationQuality.Quarter, false),
                    "a non-finite completion cannot authorize reuse");
                gate.Complete(1, 0.01);
                Check(!gate.ShouldRebuild(1, 0.01, ConduitAnimationQuality.Quarter, false),
                    "a finite completion recovers from invalid clock input");
            }
        }

        private static void InvalidationAndFailedBuilds()
        {
            var gate = new ConduitVisualRefreshGate();
            gate.Complete(0, 0);
            Check(gate.ShouldRebuild(1, 0.01, ConduitAnimationQuality.Quarter, true),
                "changed input forces a rebuild before its cadence");
            Check(gate.ShouldRebuild(1, 0.01, ConduitAnimationQuality.Quarter, false),
                "a forced rebuild without completion revokes the old mesh");
            gate.Complete(1, 0.01);
            Check(!gate.ShouldRebuild(2, 0.02, ConduitAnimationQuality.Quarter, false),
                "a completed forced rebuild can be reused");
            gate.Invalidate();
            Check(gate.ShouldRebuild(2, 0.02, ConduitAnimationQuality.Quarter, false),
                "explicit invalidation needs a successful rebuild");
            Check(gate.ShouldRebuild(2, 0.03, ConduitAnimationQuality.Quarter, false),
                "a failed or empty rebuild remains invalid on the next draw");
            gate.Complete(2, 0.03);
            Check(gate.ShouldRebuild(6, 0.04, ConduitAnimationQuality.Quarter, false),
                "scheduled refresh begins a new build");
            Check(gate.ShouldRebuild(6, 0.05, ConduitAnimationQuality.Quarter, false),
                "scheduled failure cannot reuse an earlier completed mesh");
            gate.Complete(6, 0.05);
            Check(!gate.ShouldRebuild(7, 0.06, ConduitAnimationQuality.Quarter, false),
                "successful retry restores reuse");
        }

        private static void FrameIntegerBounds()
        {
            var gate = new ConduitVisualRefreshGate();
            gate.Complete(int.MaxValue - 1, 0);
            Check(!gate.ShouldRebuild(int.MaxValue, 0.01, ConduitAnimationQuality.Quarter, false),
                "the highest frame is still one step after its predecessor");
            Check(gate.ShouldRebuild(int.MinValue, 0.02, ConduitAnimationQuality.Quarter, false),
                "frame counter wrap rebuilds immediately");
            gate.Complete(int.MinValue, 0.02);
            Check(gate.ShouldRebuild(int.MinValue + 1, 0.03, ConduitAnimationQuality.Quarter, false),
                "a negative completion cannot authorize reuse after a frame counter wrap");
            gate.Complete(0, 0.03);
            Check(gate.ShouldRebuild(int.MaxValue, 0.04, ConduitAnimationQuality.Quarter, false),
                "a large positive frame gap forces a rebuild");

            foreach (int frame in new[] { -1, int.MinValue })
            {
                gate.Complete(0, 0);
                Check(gate.ShouldRebuild(frame, 0, ConduitAnimationQuality.Quarter, false),
                    "negative frame input conservatively rebuilds");
                gate.Complete(frame, 0);
                Check(gate.ShouldRebuild(frame, 0, ConduitAnimationQuality.Quarter, false),
                    "completing a negative frame cannot authorize same-frame reuse");
                Check(gate.ShouldRebuild(0, 0, ConduitAnimationQuality.Quarter, false),
                    "a subsequent valid frame still needs a successful build");
            }
        }

        private static void ClockPrecision()
        {
            var gate = new ConduitVisualRefreshGate();
            const double longSession = 100000000;
            gate.Complete(100, longSession);
            Check(!gate.ShouldRebuild(101, longSession + 0.05, ConduitAnimationQuality.Quarter, false),
                "a double clock retains subsecond precision in a long session");
            Check(gate.ShouldRebuild(102, longSession + 0.12, ConduitAnimationQuality.Quarter, false),
                "the double-clock age cap survives a long session");

            // Promoting an already rounded float cannot recover lost clock
            // precision; integration should supply a monotonic double clock.
            Check((float)longSession == (float)(longSession + 0.12),
                "this case distinguishes double time from a promoted float clock");
            gate.Complete(1, (double)0.25f);
            Check(!gate.ShouldRebuild(2, (double)0.3125f, ConduitAnimationQuality.Quarter, false),
                "finite float input promoted to double follows its actual value");
            Check(gate.ShouldRebuild(3, (double)0.375f, ConduitAnimationQuality.Quarter, false),
                "promoted float times still enforce the age cap when representable");
        }

        private static void SteadyStateAllocations()
        {
            ExerciseGate(10000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int builds = ExerciseGate(100000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Equal(25000, builds, "allocation workload executes normal quarter cadence");
            Check(allocated == 0, "steady-state refresh decisions allocate no managed memory");
        }

        private static int ExerciseGate(int frames)
        {
            var gate = new ConduitVisualRefreshGate();
            int builds = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                double now = frame / 120.0;
                if (gate.ShouldRebuild(frame, now, ConduitAnimationQuality.Quarter, false))
                {
                    builds++;
                    gate.Complete(frame, now);
                }
            }
            return builds;
        }

        private static void Equal(int expected, int actual, string message)
        {
            Check(expected == actual, message + ": expected " + expected + ", got " + actual);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Conduit visual refresh regression: " + message);
        }
    }
}
