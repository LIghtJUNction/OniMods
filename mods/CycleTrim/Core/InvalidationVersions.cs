using System.Threading;

namespace CycleTrim.Core
{
    public static class InvalidationVersions
    {
        private static long fetchVersion;
        private static long choreVersion;

        public static long FetchVersion
        {
            get { return Interlocked.Read(ref fetchVersion); }
        }

        public static long ChoreVersion
        {
            get { return Interlocked.Read(ref choreVersion); }
        }

        public static long BumpFetch()
        {
            return Interlocked.Increment(ref fetchVersion);
        }

        public static long BumpChore()
        {
            return Interlocked.Increment(ref choreVersion);
        }

        public static RefreshStamp Capture(
            long navigationVersion,
            int cell,
            int context)
        {
            return new RefreshStamp(
                FetchVersion,
                navigationVersion,
                ChoreVersion,
                cell,
                context);
        }
    }
}
