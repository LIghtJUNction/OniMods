using System;
using System.Collections.Generic;
using CycleTrim.Core;

namespace CycleTrim.NavGridProbe.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                SparseAndDenseShapesLandInDifferentDensityBuckets();
                RangeDirectionAndDirtyCountRemainVisible();
                ResetClearsCaptureState();
                SummaryIsAggregateAndHumanReadable();
                Console.WriteLine("PASS CycleTrim NavGrid workload probe regressions");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception.Message);
                return 1;
            }
        }

        private static void SparseAndDenseShapesLandInDifferentDensityBuckets()
        {
            var probe = new NavGridWorkloadProbe();
            var sparse = new List<int>
            {
                0, 20, 40, 60,
                2000, 2020, 2040, 2060
            };
            var dense = new List<int>
            {
                1000, 1001,
                1100, 1101,
                1200, 1201,
                1300, 1301
            };

            probe.Record(sparse, gridWidth: 100, rangeX: 2, rangeY: 4);
            probe.Record(dense, gridWidth: 100, rangeX: 2, rangeY: 4);

            AssertEqual(2, probe.CallCount, "call count");
            AssertEqual(16, probe.TotalDirtyCells, "total dirty cells");
            AssertEqual(
                1,
                probe.GetBucketCount(
                    NavGridWorkloadProbe.DirtyBucket(8),
                    NavGridWorkloadProbe.RangeBucket(2),
                    NavGridWorkloadProbe.RangeBucket(4),
                    NavGridWorkloadProbe.DensityBucket(8, 1281)),
                "sparse density bucket");
            AssertEqual(
                1,
                probe.GetBucketCount(
                    NavGridWorkloadProbe.DirtyBucket(8),
                    NavGridWorkloadProbe.RangeBucket(2),
                    NavGridWorkloadProbe.RangeBucket(4),
                    NavGridWorkloadProbe.DensityBucket(8, 8)),
                "dense density bucket");
        }

        private static void RangeDirectionAndDirtyCountRemainVisible()
        {
            var probe = new NavGridWorkloadProbe();
            var cells = new List<int>();
            for (var index = 0; index < 20; index++)
            {
                cells.Add(5000 + index);
            }

            probe.Record(cells, gridWidth: 100, rangeX: 2, rangeY: 4);
            probe.Record(cells, gridWidth: 100, rangeX: 4, rangeY: 2);
            probe.Record(null, gridWidth: 100, rangeX: 0, rangeY: 0);

            AssertEqual(
                1,
                probe.GetBucketCount(
                    NavGridWorkloadProbe.DirtyBucket(20),
                    NavGridWorkloadProbe.RangeBucket(2),
                    NavGridWorkloadProbe.RangeBucket(4),
                    NavGridWorkloadProbe.DensityBucket(20, 20)),
                "2x4 bucket");
            AssertEqual(
                1,
                probe.GetBucketCount(
                    NavGridWorkloadProbe.DirtyBucket(20),
                    NavGridWorkloadProbe.RangeBucket(4),
                    NavGridWorkloadProbe.RangeBucket(2),
                    NavGridWorkloadProbe.DensityBucket(20, 20)),
                "4x2 bucket");
            AssertEqual(1, probe.EmptyCallCount, "empty call bucket");
        }

        private static void ResetClearsCaptureState()
        {
            var probe = new NavGridWorkloadProbe();
            probe.Record(
                new List<int> { 1000, 1001, 1100, 1101 },
                gridWidth: 100,
                rangeX: 2,
                rangeY: 4);

            probe.Reset();

            AssertEqual(0, probe.CallCount, "reset call count");
            AssertEqual(0, probe.EmptyCallCount, "reset empty call count");
            AssertEqual(0, probe.TotalDirtyCells, "reset dirty cells");
            AssertEqual(0, probe.TotalBoundingBoxCells, "reset bounding box cells");
            AssertEqual(
                0,
                probe.GetBucketCount(
                    NavGridWorkloadProbe.DirtyBucket(4),
                    NavGridWorkloadProbe.RangeBucket(2),
                    NavGridWorkloadProbe.RangeBucket(4),
                    NavGridWorkloadProbe.DensityBucket(4, 4)),
                "reset histogram bucket");
        }

        private static void SummaryIsAggregateAndHumanReadable()
        {
            var probe = new NavGridWorkloadProbe();
            probe.Record(
                new List<int> { 1000, 1001, 1100, 1101 },
                gridWidth: 100,
                rangeX: 2,
                rangeY: 4);

            var summary = probe.FormatSummary(maxBuckets: 4);
            AssertContains(summary, "calls=1", "summary call count");
            AssertContains(summary, "rx=2", "summary X range");
            AssertContains(summary, "ry=4", "summary Y range");
            AssertContains(summary, "density=>1/2", "summary density");
        }

        private static void AssertEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + ", got " + actual);
            }
        }

        private static void AssertContains(string value, string expected, string name)
        {
            if (value == null || value.IndexOf(expected, StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException(
                    name + " missing '" + expected + "' in '" + value + "'");
            }
        }
    }
}
