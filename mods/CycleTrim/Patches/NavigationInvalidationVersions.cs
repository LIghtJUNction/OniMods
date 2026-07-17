using CycleTrim.Core;

namespace CycleTrim.Patches
{
    internal static class NavigationInvalidationVersions
    {
        private static readonly ScopedInvalidationVersions<NavGrid> Versions =
            new ScopedInvalidationVersions<NavGrid>();

        internal static long Get(NavGrid navGrid)
        {
            return Versions.Get(navGrid);
        }

        internal static long Bump(NavGrid navGrid)
        {
            return Versions.Bump(navGrid);
        }
    }
}
