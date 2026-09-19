using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CycleTrim.Core
{
    /// <summary>
    /// Allocation-free-on-record aggregate collector for pre-expansion NavGrid workloads.
    /// Formatting is intentionally separated so logging can happen outside UpdateGraph().
    /// </summary>
    internal sealed class NavGridWorkloadProbe
    {
        private const int DirtyBucketCount = 9;
        private const int RangeBucketCount = 7;
        private const int DensityBucketCount = 6;

        private static readonly string[] DirtyLabels =
        {
            "0", "1", "2-3", "4-7", "8-15", "16-23", "24-31", "32-63", "64+"
        };
        private static readonly string[] RangeLabels =
        {
            "0", "1", "2", "3", "4", "5-7", "8+"
        };
        private static readonly string[] DensityLabels =
        {
            "empty", "<=1/16", "<=1/8", "<=1/4", "<=1/2", ">1/2"
        };

        private readonly long[] histogram = new long[
            DirtyBucketCount * RangeBucketCount * RangeBucketCount * DensityBucketCount];

        internal long CallCount { get; private set; }
        internal long EmptyCallCount { get; private set; }
        internal long TotalDirtyCells { get; private set; }
        internal long TotalBoundingBoxCells { get; private set; }

        internal void Reset()
        {
            Array.Clear(histogram, 0, histogram.Length);
            CallCount = 0;
            EmptyCallCount = 0;
            TotalDirtyCells = 0;
            TotalBoundingBoxCells = 0;
        }

        internal void Record(
            List<int> dirtyCells,
            int gridWidth,
            int rangeX,
            int rangeY)
        {
            CallCount++;
            var dirtyCount = dirtyCells == null ? 0 : dirtyCells.Count;
            TotalDirtyCells += dirtyCount;
            var dirtyBucket = DirtyBucket(dirtyCount);
            var rangeXBucket = RangeBucket(rangeX);
            var rangeYBucket = RangeBucket(rangeY);
            if (dirtyCount == 0 || gridWidth <= 0)
            {
                EmptyCallCount++;
                Increment(dirtyBucket, rangeXBucket, rangeYBucket, densityBucket: 0);
                return;
            }

            var validCount = 0;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            for (var index = 0; index < dirtyCells.Count; index++)
            {
                var cell = dirtyCells[index];
                if (cell < 0)
                {
                    continue;
                }

                var x = cell % gridWidth;
                var y = cell / gridWidth;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                validCount++;
            }

            if (validCount == 0)
            {
                EmptyCallCount++;
                Increment(dirtyBucket, rangeXBucket, rangeYBucket, densityBucket: 0);
                return;
            }

            var boundingBoxCells = (long)(maxX - minX + 1) * (maxY - minY + 1);
            TotalBoundingBoxCells += boundingBoxCells;
            Increment(
                dirtyBucket,
                rangeXBucket,
                rangeYBucket,
                DensityBucket(validCount, boundingBoxCells));
        }

        internal long GetBucketCount(
            int dirtyBucket,
            int rangeXBucket,
            int rangeYBucket,
            int densityBucket)
        {
            return histogram[BucketIndex(
                dirtyBucket,
                rangeXBucket,
                rangeYBucket,
                densityBucket)];
        }

        internal string FormatSummary(int maxBuckets)
        {
            maxBuckets = Math.Max(1, maxBuckets);
            var topIndices = new int[maxBuckets];
            var topCounts = new long[maxBuckets];
            for (var index = 0; index < topIndices.Length; index++)
            {
                topIndices[index] = -1;
            }

            var nonZeroBuckets = 0;
            for (var index = 0; index < histogram.Length; index++)
            {
                var count = histogram[index];
                if (count <= 0)
                {
                    continue;
                }

                nonZeroBuckets++;
                InsertTopBucket(topIndices, topCounts, index, count);
            }

            var summary = new StringBuilder();
            summary.Append("calls=").Append(CallCount);
            summary.Append(", empty=").Append(EmptyCallCount);
            if (CallCount > 0)
            {
                summary.Append(", avgDirty=").Append(
                    ((double)TotalDirtyCells / CallCount).ToString(
                        "F2", CultureInfo.InvariantCulture));
            }
            var nonEmptyCalls = CallCount - EmptyCallCount;
            if (nonEmptyCalls > 0)
            {
                summary.Append(", avgSeedBBox=").Append(
                    ((double)TotalBoundingBoxCells / nonEmptyCalls).ToString(
                        "F2", CultureInfo.InvariantCulture));
            }
            summary.Append(", nonzeroBuckets=").Append(nonZeroBuckets);
            summary.Append(", top=[");
            for (var slot = 0; slot < topIndices.Length && topIndices[slot] >= 0; slot++)
            {
                if (slot > 0)
                {
                    summary.Append("; ");
                }
                AppendBucket(summary, topIndices[slot], topCounts[slot]);
            }
            return summary.Append(']').ToString();
        }

        internal static int DirtyBucket(int count)
        {
            if (count <= 0) return 0;
            if (count == 1) return 1;
            if (count <= 3) return 2;
            if (count <= 7) return 3;
            if (count <= 15) return 4;
            if (count <= 23) return 5;
            if (count <= 31) return 6;
            if (count <= 63) return 7;
            return 8;
        }

        internal static int RangeBucket(int range)
        {
            if (range <= 0) return 0;
            if (range <= 4) return range;
            if (range <= 7) return 5;
            return 6;
        }

        internal static int DensityBucket(int validCount, long boundingBoxCells)
        {
            if (validCount <= 0 || boundingBoxCells <= 0) return 0;
            var count = (long)validCount;
            if (count * 16 <= boundingBoxCells) return 1;
            if (count * 8 <= boundingBoxCells) return 2;
            if (count * 4 <= boundingBoxCells) return 3;
            if (count * 2 <= boundingBoxCells) return 4;
            return 5;
        }

        private void Increment(
            int dirtyBucket,
            int rangeXBucket,
            int rangeYBucket,
            int densityBucket)
        {
            histogram[BucketIndex(
                dirtyBucket,
                rangeXBucket,
                rangeYBucket,
                densityBucket)]++;
        }

        private static int BucketIndex(
            int dirtyBucket,
            int rangeXBucket,
            int rangeYBucket,
            int densityBucket)
        {
            return (((dirtyBucket * RangeBucketCount + rangeXBucket)
                * RangeBucketCount + rangeYBucket)
                * DensityBucketCount + densityBucket);
        }

        private static void InsertTopBucket(
            int[] topIndices,
            long[] topCounts,
            int index,
            long count)
        {
            for (var slot = 0; slot < topCounts.Length; slot++)
            {
                if (count <= topCounts[slot])
                {
                    continue;
                }

                for (var shift = topCounts.Length - 1; shift > slot; shift--)
                {
                    topCounts[shift] = topCounts[shift - 1];
                    topIndices[shift] = topIndices[shift - 1];
                }
                topCounts[slot] = count;
                topIndices[slot] = index;
                return;
            }
        }

        private static void AppendBucket(StringBuilder summary, int index, long count)
        {
            var density = index % DensityBucketCount;
            index /= DensityBucketCount;
            var rangeY = index % RangeBucketCount;
            index /= RangeBucketCount;
            var rangeX = index % RangeBucketCount;
            var dirty = index / RangeBucketCount;

            summary.Append("dirty=").Append(DirtyLabels[dirty]);
            summary.Append(",rx=").Append(RangeLabels[rangeX]);
            summary.Append(",ry=").Append(RangeLabels[rangeY]);
            summary.Append(",density=").Append(DensityLabels[density]);
            summary.Append(':').Append(count);
        }
    }
}
