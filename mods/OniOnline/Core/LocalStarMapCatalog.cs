using System;
using System.Collections.Generic;
using System.Globalization;

namespace OniOnline.Core
{
    internal enum NativeStarVisibility
    {
        Hidden,
        Unexplored,
        Discovered
    }

    internal sealed class NativeStarMapCandidate
    {
        internal int WorldId;
        internal int Q;
        internal int R;
        internal string Name;
        internal NativeStarVisibility Visibility;
    }

    internal sealed class LocalStarMapEntry
    {
        internal string Id;
        internal int NativeWorldId;
        internal int NativeQ;
        internal int NativeR;
        internal int Q;
        internal int R;
        internal string Name;
        internal bool Discovered;
    }

    // A local read-only projection of native worlds, separate from room membership.
    internal static class LocalStarMapCatalog
    {
        internal static LocalStarMapEntry[] Project(string homeSystemId, int selectedWorldId, int anchorQ, int anchorR,
            IEnumerable<NativeStarMapCandidate> candidates, IEnumerable<int> registeredWorldIds)
        {
            if (string.IsNullOrWhiteSpace(homeSystemId) || selectedWorldId < 0 || candidates == null)
                return new LocalStarMapEntry[0];

            var worlds = new Dictionary<int, NativeStarMapCandidate>();
            var duplicates = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.WorldId < 0) continue;
                if (worlds.ContainsKey(candidate.WorldId))
                {
                    duplicates.Add(candidate.WorldId);
                    continue;
                }
                worlds.Add(candidate.WorldId, new NativeStarMapCandidate
                {
                    WorldId = candidate.WorldId,
                    Q = candidate.Q,
                    R = candidate.R,
                    Name = candidate.Name,
                    Visibility = candidate.Visibility
                });
            }

            NativeStarMapCandidate selected;
            if (!worlds.TryGetValue(selectedWorldId, out selected) || duplicates.Contains(selectedWorldId) ||
                selected.Visibility != NativeStarVisibility.Discovered)
                return new LocalStarMapEntry[0];

            var registered = registeredWorldIds == null ? new HashSet<int>() : new HashSet<int>(registeredWorldIds);
            var entries = new List<LocalStarMapEntry>();
            foreach (var world in worlds.Values)
            {
                if (world.WorldId == selectedWorldId || registered.Contains(world.WorldId) || duplicates.Contains(world.WorldId) ||
                    (world.Visibility != NativeStarVisibility.Unexplored && world.Visibility != NativeStarVisibility.Discovered))
                    continue;

                long q = (long)world.Q - selected.Q + anchorQ;
                long r = (long)world.R - selected.R + anchorR;
                if (q < int.MinValue || q > int.MaxValue || r < int.MinValue || r > int.MaxValue) continue;

                bool discovered = world.Visibility == NativeStarVisibility.Discovered;
                entries.Add(new LocalStarMapEntry
                {
                    Id = homeSystemId + ":" + world.WorldId.ToString(CultureInfo.InvariantCulture),
                    NativeWorldId = world.WorldId,
                    NativeQ = world.Q,
                    NativeR = world.R,
                    Q = (int)q,
                    R = (int)r,
                    Name = discovered ? world.Name ?? "" : "",
                    Discovered = discovered
                });
            }
            entries.Sort((a, b) => a.NativeWorldId.CompareTo(b.NativeWorldId));
            return entries.ToArray();
        }
    }
}
