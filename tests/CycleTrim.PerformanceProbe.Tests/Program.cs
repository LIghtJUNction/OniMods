using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class Program
    {
        private const double MaxAcceptedMedianOverheadRatio = 2.0d;

        private static int Main()
        {
            try
            {
                RecordsCountTotalMeanAndMax();
                ComputesIntervalDeltaWithoutResettingTheCounter();
                ClampsNegativeElapsedTicks();
                AggregatesConcurrentWriters();
                SnapshotsDoNotSplitConcurrentSamplesAcrossIntervals();
                MeasuresConsistentSnapshotOverhead();
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

        private static void SnapshotsDoNotSplitConcurrentSamplesAcrossIntervals()
        {
            const int workers = 4;
            const int writesPerWorker = 100000;
            var counter = new PerformanceProbeCounter(consistentSnapshots: true);
            var start = new ManualResetEventSlim(false);
            var writers = new Task[workers];

            for (var worker = 0; worker < workers; worker++)
            {
                writers[worker] = Task.Run(() =>
                {
                    start.Wait();
                    for (var index = 0; index < writesPerWorker; index++)
                    {
                        counter.Record(1);
                    }
                });
            }

            var previous = counter.Snapshot();
            start.Set();
            while (!Task.WaitAll(writers, 0))
            {
                var current = counter.Snapshot();
                var interval = current.DeltaSince(previous);
                AssertEqual(current.Calls, current.TotalTicks, "concurrent snapshot calls/ticks");
                AssertEqual(interval.Calls, interval.TotalTicks, "concurrent interval calls/ticks");
                if (current.MaxTicks != 0 && current.MaxTicks != 1)
                {
                    throw new InvalidOperationException(
                        "concurrent snapshot max ticks expected 0 or 1, got " + current.MaxTicks);
                }
                previous = current;
                Thread.Yield();
            }

            Task.WaitAll(writers);
            var final = counter.Snapshot();
            var finalInterval = final.DeltaSince(previous);
            AssertEqual(workers * writesPerWorker, final.Calls, "coordinated concurrent calls");
            AssertEqual(final.Calls, final.TotalTicks, "coordinated concurrent total");
            AssertEqual(finalInterval.Calls, finalInterval.TotalTicks, "final interval calls/ticks");
            AssertEqual(1, final.MaxTicks, "coordinated concurrent max");
        }

        private static void MeasuresConsistentSnapshotOverhead()
        {
            const int warmupIterations = 100000;
            const int iterations = 1000000;
            const int samples = 7;
            const int workers = 4;

            for (var warmup = 0; warmup < 3; warmup++)
            {
                if ((warmup & 1) == 0)
                {
                    MeasureRecords(false, warmupIterations);
                    MeasureRecords(true, warmupIterations);
                    MeasureConcurrentRecords(false, workers, warmupIterations / workers);
                    MeasureConcurrentRecords(true, workers, warmupIterations / workers);
                }
                else
                {
                    MeasureRecords(true, warmupIterations);
                    MeasureRecords(false, warmupIterations);
                    MeasureConcurrentRecords(true, workers, warmupIterations / workers);
                    MeasureConcurrentRecords(false, workers, warmupIterations / workers);
                }
            }

            var serialRatios = new double[samples];
            var concurrentRatios = new double[samples];
            for (var sample = 0; sample < samples; sample++)
            {
                long serialBaseline;
                long serialCandidate;
                long concurrentBaseline;
                long concurrentCandidate;
                if ((sample & 1) == 0)
                {
                    serialBaseline = MeasureRecords(false, iterations);
                    serialCandidate = MeasureRecords(true, iterations);
                    concurrentBaseline = MeasureConcurrentRecords(false, workers, iterations / workers);
                    concurrentCandidate = MeasureConcurrentRecords(true, workers, iterations / workers);
                }
                else
                {
                    serialCandidate = MeasureRecords(true, iterations);
                    serialBaseline = MeasureRecords(false, iterations);
                    concurrentCandidate = MeasureConcurrentRecords(true, workers, iterations / workers);
                    concurrentBaseline = MeasureConcurrentRecords(false, workers, iterations / workers);
                }
                serialRatios[sample] = (double)serialCandidate / serialBaseline;
                concurrentRatios[sample] = (double)concurrentCandidate / concurrentBaseline;
            }

            Array.Sort(serialRatios);
            Array.Sort(concurrentRatios);
            var allocationCounter = new PerformanceProbeCounter(consistentSnapshots: true);
            for (var index = 0; index < 1000; index++)
            {
                allocationCounter.Record(1);
            }
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < iterations; index++)
            {
                allocationCounter.Record(1);
            }
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            AssertEqual(0, allocatedAfter - allocatedBefore, "coordinated record allocations");

            Console.WriteLine(
                "INFO coordinated snapshot serial Record overhead ratio min/median/max: "
                + serialRatios[0].ToString("F3") + "x / "
                + serialRatios[samples / 2].ToString("F3") + "x / "
                + serialRatios[samples - 1].ToString("F3") + "x");
            Console.WriteLine(
                "INFO coordinated snapshot 4-writer Record overhead ratio min/median/max: "
                + concurrentRatios[0].ToString("F3") + "x / "
                + concurrentRatios[samples / 2].ToString("F3") + "x / "
                + concurrentRatios[samples - 1].ToString("F3") + "x; allocations=0 B over "
                + iterations + " serial records");

            if (serialRatios[samples / 2] > MaxAcceptedMedianOverheadRatio
                || concurrentRatios[samples / 2] > MaxAcceptedMedianOverheadRatio)
            {
                throw new InvalidOperationException(
                    "coordinated snapshot median observer overhead exceeded predeclared 2.0x limit");
            }
        }

        private static long MeasureRecords(bool consistentSnapshots, int iterations)
        {
            var counter = new PerformanceProbeCounter(consistentSnapshots);
            var started = Stopwatch.GetTimestamp();
            for (var index = 0; index < iterations; index++)
            {
                counter.Record(1);
            }
            var elapsed = Stopwatch.GetTimestamp() - started;
            var snapshot = counter.Snapshot();
            AssertEqual(iterations, snapshot.Calls, "measured record calls");
            AssertEqual(iterations, snapshot.TotalTicks, "measured record total");
            return elapsed <= 0 ? 1 : elapsed;
        }

        private static long MeasureConcurrentRecords(
            bool consistentSnapshots,
            int workers,
            int writesPerWorker)
        {
            var counter = new PerformanceProbeCounter(consistentSnapshots);
            var start = new ManualResetEventSlim(false);
            var tasks = new Task[workers];
            for (var worker = 0; worker < workers; worker++)
            {
                tasks[worker] = Task.Run(() =>
                {
                    start.Wait();
                    for (var index = 0; index < writesPerWorker; index++)
                    {
                        counter.Record(1);
                    }
                });
            }

            var started = Stopwatch.GetTimestamp();
            start.Set();
            Task.WaitAll(tasks);
            var elapsed = Stopwatch.GetTimestamp() - started;
            var snapshot = counter.Snapshot();
            var expected = workers * writesPerWorker;
            AssertEqual(expected, snapshot.Calls, "concurrent measured record calls");
            AssertEqual(expected, snapshot.TotalTicks, "concurrent measured record total");
            return elapsed <= 0 ? 1 : elapsed;
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
