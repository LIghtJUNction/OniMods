using System;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                RunTest(
                    nameof(VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick),
                    VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick);
                RunTest(
                    nameof(CandidateCallRateIsIndependentOfHighRenderFps),
                    CandidateCallRateIsIndependentOfHighRenderFps);
                RunTest(
                    nameof(PriorityRunsImmediatelyWithoutConsumingNormalBudget),
                    PriorityRunsImmediatelyWithoutConsumingNormalBudget);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception.Message);
                return 1;
            }
        }

        private static void PriorityRunsImmediatelyWithoutConsumingNormalBudget()
        {
            var rateCap = new BrainRateCap();
            rateCap.BeginFrame(1.0 / 240.0);
            var priorityCalls = 0;

            rateCap.RunPriority(delegate { priorityCalls++; });

            AssertEqual(1L, priorityCalls, "priority calls");
            AssertEqual(0L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "normal budget after priority");
            rateCap.BeginFrame(1.0 / 240.0);
            AssertEqual(0L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "priority must not add normal tokens");
            rateCap.BeginFrame(1.0 / 240.0);
            AssertEqual(1L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "normal tokens retain their own cadence");
        }

        private static void CandidateCallRateIsIndependentOfHighRenderFps()
        {
            foreach (var framesPerSecond in new[] { 80, 120, 240 })
            {
                var calls = RunCandidate(framesPerSecond, 30);
                AssertEqual(2400L, calls.DupeCalls, framesPerSecond + " FPS dupe calls");
                AssertEqual(12000L, calls.CreatureCalls, framesPerSecond + " FPS creature calls");
            }
        }

        private static BrainCallCounts RunCandidate(int framesPerSecond, int seconds)
        {
            var rateCap = new BrainRateCap();
            long dupeCalls = 0;
            long creatureCalls = 0;
            var frameDurationSeconds = 1.0 / framesPerSecond;

            for (var frame = 0; frame < framesPerSecond * seconds; frame++)
            {
                rateCap.BeginFrame(frameDurationSeconds);
                while (rateCap.TryAcquireNormal(BrainGroup.Dupe))
                {
                    dupeCalls++;
                }

                while (rateCap.TryAcquireNormal(BrainGroup.Creature))
                {
                    creatureCalls++;
                }
            }

            return new BrainCallCounts(dupeCalls, creatureCalls);
        }

        private static void VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick()
        {
            var scheduler = new VanillaBrainSchedulerSimulator();
            var result = scheduler.Run(120, 30);

            AssertEqual(3600L, result.DupeCalls, "dupe calls");
            AssertEqual(18000L, result.CreatureCalls, "creature calls");
        }

        private static void AssertEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + ": expected " + expected + ", actual " + actual);
            }
        }

        private static void RunTest(string name, Action test)
        {
            test();
            Console.WriteLine("PASS " + name);
        }
    }
}
