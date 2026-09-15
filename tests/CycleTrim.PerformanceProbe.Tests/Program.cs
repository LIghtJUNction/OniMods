using System;
using System.Threading.Tasks;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                RecordsCountTotalMeanAndMax();
                ComputesIntervalDeltaWithoutResettingTheCounter();
                ClampsNegativeElapsedTicks();
                AggregatesConcurrentWriters();
                Console.WriteLine("PASS CycleTrim performance probe counter regressions");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception.Message);
                return 1;
            }
        }

        private static void RecordsCountTotalMeanAndMax()
        {
            var counter = new PerformanceProbeCounter();
            counter.Record(10);
            counter.Record(30);
            counter.Record(20);

            var snapshot = counter.Snapshot();
            AssertEqual(3, snapshot.Calls, "call count");
            AssertEqual(60, snapshot.TotalTicks, "total ticks");
            AssertEqual(30, snapshot.MaxTicks, "max ticks");
            AssertNear(20d, snapshot.MeanTicks, "mean ticks");
        }

        private static void ComputesIntervalDeltaWithoutResettingTheCounter()
        {
            var counter = new PerformanceProbeCounter();
            counter.Record(10);
            counter.Record(30);
            var previous = counter.Snapshot();

            counter.Record(20);
            counter.Record(40);
            var current = counter.Snapshot();
            var interval = current.DeltaSince(previous);

            AssertEqual(2, interval.Calls, "interval call count");
            AssertEqual(60, interval.TotalTicks, "interval total ticks");
            AssertNear(30d, interval.MeanTicks, "interval mean ticks");
            AssertEqual(4, current.Calls, "cumulative count remains intact");
            AssertEqual(100, current.TotalTicks, "cumulative total remains intact");
        }

        private static void ClampsNegativeElapsedTicks()
        {
            var counter = new PerformanceProbeCounter();
            counter.Record(-5);
            var snapshot = counter.Snapshot();

            AssertEqual(1, snapshot.Calls, "negative sample call count");
            AssertEqual(0, snapshot.TotalTicks, "negative sample total");
            AssertEqual(0, snapshot.MaxTicks, "negative sample max");
        }

        private static void AggregatesConcurrentWriters()
        {
            const int workers = 8;
            const int writesPerWorker = 10000;
            var counter = new PerformanceProbeCounter();

            Parallel.For(0, workers, worker =>
            {
                var elapsed = worker + 1;
                for (var index = 0; index < writesPerWorker; index++)
                {
                    counter.Record(elapsed);
                }
            });

            var snapshot = counter.Snapshot();
            AssertEqual(workers * writesPerWorker, snapshot.Calls, "concurrent calls");
            AssertEqual(
                writesPerWorker * (workers * (workers + 1) / 2),
                snapshot.TotalTicks,
                "concurrent total");
            AssertEqual(workers, snapshot.MaxTicks, "concurrent max");
        }

        private static void AssertEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + ", got " + actual);
            }
        }

        private static void AssertNear(double expected, double actual, string name)
        {
            if (Math.Abs(expected - actual) > 0.0001d)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + ", got " + actual);
            }
        }
    }
}
