using System.Threading;

namespace CycleTrim.Core
{
    // Unknown scopes invalidate everyone. Known scopes affect only their parent
    // world (rocket interiors share chores with that world). Monotonic sums keep
    // global and local changes observable without hashing or allocation.
    internal sealed class WorldInvalidationVersions
    {
        private readonly long[] versions = new long[256];
        private long global;
        private long total;

        internal long Total { get { return Interlocked.Read(ref total); } }

        internal long Get(int world)
        {
            return (uint)world < versions.Length
                ? Interlocked.Read(ref global) + Interlocked.Read(ref versions[world])
                : Total;
        }

        internal long Bump(int world)
        {
            if ((uint)world < versions.Length)
                Interlocked.Increment(ref versions[world]);
            else
                Interlocked.Increment(ref global);
            return Interlocked.Increment(ref total);
        }
    }
}
