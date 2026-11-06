using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static readonly Dictionary<int, BuildStockSnapshot> BuildStockSnapshots = new Dictionary<int, BuildStockSnapshot>();
        private static int BuildStockSnapshotFrame = -1;

        private static int PickupableWorldId(Pickupable pickupable)
        {
            int cell = pickupable.cachedCell;
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];
            return pickupable.GetMyWorldId();
        }

        private static BuildStockSnapshot GetBuildStockSnapshot(int worldId)
        {
            int resolvedWorldId = worldId >= 0 ? worldId : (ClusterManager.Instance?.activeWorldId ?? -1);
            int frame = Time.frameCount;
            if (BuildStockSnapshotFrame != frame)
            {
                BuildStockSnapshots.Clear();
                BuildStockSnapshotFrame = frame;
            }
            if (BuildStockSnapshots.TryGetValue(resolvedWorldId, out var cached) && cached.Frame == frame)
                return cached;

            var navigators = ActiveBuildNavigators(resolvedWorldId);
            var snapshot = new BuildStockSnapshot(resolvedWorldId, frame, navigators.Count);
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                int itemWorldId = PickupableWorldId(pickupable);
                if (resolvedWorldId >= 0 && itemWorldId != resolvedWorldId)
                    continue;
                var kpid = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                if (kpid == null || primary == null)
                    continue;
                int cell = pickupable.cachedCell;
                bool stored = pickupable.storage != null || kpid.HasTag(GameTags.Stored);
                bool reachable = !stored && Grid.IsValidCell(cell)
                    && navigators.Any(navigator => SafeBuildCanReach(navigator, cell));
                foreach (var tag in new[] { kpid.PrefabTag, new Tag(primary.ElementID.ToString()) }
                    .Where(tag => tag.IsValid).Distinct())
                {
                    snapshot.Tags.Add(tag);
                    snapshot.Add(tag, ToolUtil.SafeFloat(primary.Mass), stored, reachable);
                }
            }
            BuildStockSnapshots[resolvedWorldId] = snapshot;
            return snapshot;
        }

        private sealed class BuildStockSnapshot
        {
            public readonly int WorldId;
            public readonly int Frame;
            public readonly int ActiveNavigatorCount;
            public readonly HashSet<Tag> Tags = new HashSet<Tag>();
            private readonly Dictionary<string, BuildMaterialStock> byTag =
                new Dictionary<string, BuildMaterialStock>(StringComparer.Ordinal);

            public BuildStockSnapshot(int worldId, int frame, int activeNavigatorCount)
            {
                WorldId = worldId;
                Frame = frame;
                ActiveNavigatorCount = activeNavigatorCount;
            }

            public void Add(Tag tag, float massKg, bool stored, bool reachable)
            {
                string key = tag.Name;
                if (!byTag.TryGetValue(key, out var stock))
                    byTag[key] = stock = new BuildMaterialStock();
                if (stored)
                    stock.StoredKg += massKg;
                else
                {
                    stock.LooseKg += massKg;
                    if (reachable)
                        stock.ReachableLooseKg += massKg;
                }
            }

            public BuildMaterialStock For(Tag tag)
            {
                if (!tag.IsValid)
                    return new BuildMaterialStock();
                string key = tag.Name;
                if (!byTag.TryGetValue(key, out var stock))
                    byTag[key] = stock = new BuildMaterialStock();
                if (!stock.InventoryLoaded)
                {
                    var world = ClusterManager.Instance?.GetWorld(WorldId);
                    if (world?.worldInventory != null)
                    {
                        stock.LocalWorldInventoryKg = ToolUtil.SafeFloat(
                            world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: false));
                        stock.RelatedWorldInventoryKg = ToolUtil.SafeFloat(
                            world.worldInventory.GetTotalAmount(tag, includeRelatedWorlds: true));
                    }
                    stock.InventoryLoaded = true;
                }
                return stock;
            }
        }

        private sealed class BuildMaterialStock
        {
            public float LocalWorldInventoryKg;
            public float RelatedWorldInventoryKg;
            public float LooseKg;
            public float ReachableLooseKg;
            public float StoredKg;
            public bool InventoryLoaded;
        }

        private static List<Navigator> ActiveBuildNavigators(int worldId)
        {
            var navigators = new List<Navigator>();
            foreach (var minion in Components.LiveMinionIdentities.Items)
            {
                if (minion == null || (worldId >= 0 && minion.GetMyWorldId() != worldId))
                    continue;
                var navigator = minion.GetComponent<Navigator>();
                if (navigator != null)
                    navigators.Add(navigator);
            }
            return navigators;
        }

        private static bool SafeBuildCanReach(Navigator navigator, int cell)
        {
            try { return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell); }
            catch { return false; }
        }
    }
}
