using System;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class ScopedInvalidationVersionsAllocationRegression
    {
        private const int ReadOnlyScopeCount = 2048;
        private const long MaxReadOnlyAllocatedBytes = 4096;

        [ModuleInitializer]
        internal static void Run()
        {
            var versions = new ScopedInvalidationVersions<object>();
            var warmScope = new object();
            if (versions.Get(warmScope) != 0)
            {
                throw new InvalidOperationException(
                    "Scoped invalidation warm read did not start at generation zero.");
            }

            var scopes = new object[ReadOnlyScopeCount];
            for (var index = 0; index < scopes.Length; index++)
            {
                scopes[index] = new object();
            }

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long checksum = 0;
            for (var index = 0; index < scopes.Length; index++)
            {
                checksum += versions.Get(scopes[index]);
            }
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

            if (checksum != 0)
            {
                throw new InvalidOperationException(
                    "Read-only scopes must remain at generation zero.");
            }
            if (allocatedBytes > MaxReadOnlyAllocatedBytes)
            {
                throw new InvalidOperationException(
                    "Read-only scoped invalidation lookups allocated "
                    + allocatedBytes
                    + " bytes; expected at most "
                    + MaxReadOnlyAllocatedBytes
                    + ".");
            }

            if (versions.Bump(scopes[0]) != 1 || versions.Get(scopes[0]) != 1)
            {
                throw new InvalidOperationException(
                    "A scope must still create and retain its counter on the first bump.");
            }
        }
    }
}
