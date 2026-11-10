using System.Threading;

namespace CycleTrim.Core
{
    public static class InvalidationVersions
    {
        private static readonly WorldInvalidationVersions Fetch = new WorldInvalidationVersions();
        private static readonly WorldInvalidationVersions Chore = new WorldInvalidationVersions();

        public static long FetchVersion
        {
            get { return Fetch.Total; }
        }

        public static long ChoreVersion
        {
            get { return Chore.Total; }
        }

        public static long GetFetch(int world) { return Fetch.Get(world); }
        public static long GetChore(int world) { return Chore.Get(world); }

        public static long BumpFetch(int world = -1)
        {
            return Fetch.Bump(world);
        }

        public static long BumpChore(int world = -1)
        {
            return Chore.Bump(world);
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
