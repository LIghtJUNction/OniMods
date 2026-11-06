using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // ONI 744825: OnAcceptDelivery synchronously calls MinionStartingStats.Deliver.
    // Activation precedes positioning, so only its returned object can be a candidate.
    internal static class NativeDupeBirthObserver
    {
        internal sealed class Scope
        {
            internal Game Game;
            internal SaveGame Save;
            internal OnlineColonyState Colony;
            internal ClusterManager Cluster;
            internal WorldContainer World;
            internal Telepad Pad;
            internal MinionStartingStats Delivery;
            internal NativeDupeOrigins Adapter;
            internal string PlanetId, OwnerId;
            internal int WorldId, NativeId, Calls;
            internal bool Delivering, Delivered, Failed, Finished;
            internal bool AcceptReturned, DeliveryReturned;
            internal MinionIdentity Dupe;
            internal MinionIdentity[] Before;
            internal readonly HashSet<int> BeforeIds = new HashSet<int>();
        }
        private static Scope current;
        internal static Scope Begin(Telepad pad, ITelepadDeliverable delivery)
        {
            if (current != null) { current.Failed = true; return null; }
            var stats = delivery as MinionStartingStats;
            var colony = OnlineColonyState.Current;
            if (pad == null || stats == null || stats.GetType() != typeof(MinionStartingStats) || colony == null) return null;
            var scope = new Scope { Game = Game.Instance, Save = SaveGame.Instance, Colony = colony,
                Cluster = ClusterManager.Instance, Pad = pad, Delivery = stats };
            current = scope; // Fault/finally must own even a partially captured scope.
            scope.WorldId = PadWorld(pad);
            if (scope.Cluster == null || scope.WorldId < 0 ||
                !colony.TryGetBirthContext(scope.WorldId, out scope.Adapter, out scope.PlanetId, out scope.OwnerId))
            { scope.Failed = true; return scope; }
            scope.World = scope.Cluster.GetWorld(scope.WorldId);
            scope.Before = Components.LiveMinionIdentities.Items.ToArray();
            foreach (var dupe in scope.Before)
            {
                var id = dupe == null ? null : dupe.GetComponent<KPrefabID>();
                if (id != null) scope.BeforeIds.Add(id.InstanceID);
            }
            scope.Adapter.ObserveNativeIds(scope.BeforeIds);
            return scope;
        }
        internal static Scope BeginDelivery(MinionStartingStats delivery)
        {
            var scope = current;
            if (scope == null) return null;
            if (!ReferenceEquals(scope.Delivery, delivery) || scope.Delivering || ++scope.Calls != 1)
                scope.Failed = true;
            scope.Delivering = true; return scope;
        }
        internal static void EndDelivery(Scope scope, GameObject result, Exception error)
        {
            if (scope == null) return;
            if (error != null || !ReferenceEquals(scope, current) || !scope.Delivering || scope.Delivered ||
                !scope.DeliveryReturned || result == null)
                scope.Failed = true;
            scope.Delivering = false;
            if (scope.Failed) return;
            var dupe = result.GetComponent<MinionIdentity>();
            var id = dupe == null ? null : dupe.GetComponent<KPrefabID>();
            if (id == null || scope.Before.Any(d => ReferenceEquals(d, dupe)) || scope.BeforeIds.Contains(id.InstanceID))
            { scope.Failed = true; return; }
            scope.Dupe = dupe; scope.NativeId = id.InstanceID; scope.Delivered = true;
        }
        // Postfix is not reached on an original exception; __runOriginal excludes skipped originals.
        internal static void Returned(Scope scope, bool delivery, bool ranOriginal)
        {
            if (scope == null) return;
            if (!ReferenceEquals(current, scope) || !ranOriginal) scope.Failed = true;
            if (delivery) scope.DeliveryReturned = ranOriginal;
            else scope.AcceptReturned = ranOriginal;
        }
        internal static void End(Scope scope, Exception error)
        {
            if (scope == null || scope.Finished) return;
            scope.Finished = true;
            try
            {
                if (error != null || scope.Failed || !ReferenceEquals(current, scope) || scope.Calls != 1 ||
                    !scope.AcceptReturned || !scope.Delivered || scope.Delivering || !IsCurrent(scope)) return;
                string reason;
                if (!scope.Adapter.AcceptBirth(scope.Dupe, scope.NativeId, scope.WorldId, scope.PlanetId, scope.OwnerId, out reason))
                    Debug.LogWarning("[OniOnline] Native birth ownership rejected: " + reason);
            }
            catch (Exception failure)
            { Debug.LogWarning("[OniOnline] Native birth observer rejected: " + failure.GetType().Name); }
            finally { if (ReferenceEquals(current, scope)) current = null; }
        }
        private static bool IsCurrent(Scope scope)
        {
            NativeDupeOrigins adapter; string planet, owner;
            return scope.Game != null && scope.Save != null && scope.Colony != null && scope.Pad != null && scope.World != null &&
                ReferenceEquals(scope.Game, Game.Instance) && ReferenceEquals(scope.Save, SaveGame.Instance) &&
                ReferenceEquals(scope.Cluster, ClusterManager.Instance) && ReferenceEquals(scope.Colony, OnlineColonyState.Current) &&
                scope.Game.baseAlreadyCreated && !scope.Game.IsLoading() && PadWorld(scope.Pad) == scope.WorldId &&
                ReferenceEquals(scope.World, scope.Cluster.GetWorld(scope.WorldId)) &&
                scope.Colony.TryGetBirthContext(scope.WorldId, out adapter, out planet, out owner) &&
                ReferenceEquals(adapter, scope.Adapter) && planet == scope.PlanetId && owner == scope.OwnerId;
        }
        private static int PadWorld(Telepad pad)
        {
            int cell = Grid.PosToCell(pad);
            return Grid.IsValidCell(cell) && Grid.WorldIdx != null && cell < Grid.WorldIdx.Length &&
                Grid.WorldIdx[cell] < Core.DupeOriginCodec.RetiredWorldId ? Grid.WorldIdx[cell] : -1;
        }
        internal static Scope Fault(Exception error)
        { Invalidate(); return current; }
        internal static void Invalidate()
        { if (current != null) current.Failed = true; }
        internal static void SuspendForLoad()
        {
            Invalidate();
            var save = SaveGame.Instance;
            if (save != null) save.GetComponent<OnlineColonyState>()?.SuspendNativeBirths();
        }
        internal static void Cancel(OnlineColonyState colony)
        {
            if (current == null || !ReferenceEquals(current.Colony, colony)) return;
            current.Failed = true; current = null;
        }
    }
}
