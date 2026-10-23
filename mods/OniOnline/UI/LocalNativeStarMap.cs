using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;
using UnityEngine;

namespace OniOnline.UI
{
    // This local display layer never enters the room protocol or changes a world.
    internal sealed class LocalNativeStarMap
    {
        internal bool Available;
        internal LocalStarMapEntry[] Entries = new LocalStarMapEntry[0];
        internal readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        internal string Signature { get { return Available + ":" + string.Join("|", Entries.Select(e =>
            e.Id + ":" + e.Q + ":" + e.R + ":" + e.Discovered + ":" + e.Name)); } }

        internal static LocalNativeStarMap Capture(StarSystemState system, string localPeerId)
        {
            NativeAdapterAvailability.Require(SessionCapabilities.NativeStarMapV1);
            var result = new LocalNativeStarMap();
            var colony = OnlineColonyState.Current;
            var manager = ClusterManager.Instance;
            if (colony == null || manager == null || ClusterGrid.Instance == null ||
                system == null || system.SystemId != colony.HomeSystemId) return result;
            var own = system.Members.FirstOrDefault(m => m.PeerId == localPeerId && m.Planet != null &&
                m.Planet.HomeSystemId == colony.HomeSystemId && m.Planet.OwnerId == colony.PersistentOwnerId);
            if (own == null) return result;

            result.Available = true;
            var candidates = new List<NativeStarMapCandidate>();
            var entities = new Dictionary<int, ClusterGridEntity>();
            foreach (var world in manager.WorldContainers)
            {
                if (world == null || world.IsModuleInterior) continue;
                var entity = world.GetComponent<AsteroidGridEntity>();
                if (entity == null || !entity.IsVisible || !ClusterGrid.Instance.IsValidCell(entity.Location)) continue;
                var reveal = ClusterMapScreen.GetRevealLevel(entity);
                if (reveal == ClusterRevealLevel.Hidden) continue;
                bool discovered = reveal == ClusterRevealLevel.Visible && world.IsDiscovered;
                candidates.Add(new NativeStarMapCandidate { WorldId = world.id, Q = entity.Location.q, R = entity.Location.r,
                    Name = discovered ? world.GetProperName() : "", Visibility = discovered
                        ? NativeStarVisibility.Discovered : NativeStarVisibility.Unexplored });
                entities[world.id] = entity;
            }
            var registered = system.Members.Where(m => m.Planet != null && m.Planet.HomeSystemId == colony.HomeSystemId)
                .Select(m => m.Planet.NativeWorldId);
            result.Entries = LocalStarMapCatalog.Project(colony.HomeSystemId, own.Planet.NativeWorldId,
                own.Q, own.R, candidates, registered);
            foreach (var entry in result.Entries.Where(e => e.Discovered))
            {
                ClusterGridEntity entity;
                if (!entities.TryGetValue(entry.NativeWorldId, out entity)) continue;
                var sprite = entity.GetUISprite();
                if (sprite != null) result.Icons[entry.Id] = sprite;
            }
            return result;
        }

        internal LocalStarMapEntry At(int q, int r)
        { return Entries.FirstOrDefault(e => e.Q == q && e.R == r); }

        internal int Radius
        { get { return Entries.Length == 0 ? 0 : Entries.Max(e => Math.Max(Math.Max(Math.Abs(e.Q), Math.Abs(e.R)), Math.Abs(e.Q + e.R))); } }
    }
}
