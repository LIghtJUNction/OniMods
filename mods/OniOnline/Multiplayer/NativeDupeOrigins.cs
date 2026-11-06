using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OniOnline.Core;
using UnityEngine;

namespace OniOnline.Multiplayer
{
    // Local profile identity is independent of whichever player's save this process opens.
    // This cooperative identifier is not an assertion of Steam account authentication.
    internal static class LocalOwnerProfile
    {
        internal static string PersistentOwnerId
        {
            get
            {
                string id = PlayerPrefs.GetString("OniOnline.PlayerId", "");
                if (!DupeOriginCodec.ValidId(id))
                { id = Guid.NewGuid().ToString("N"); PlayerPrefs.SetString("OniOnline.PlayerId", id); PlayerPrefs.Save(); }
                return id;
            }
        }
    }

    // A closed, successful normal whole-save load supplies these exact references.
    // SavedWorldId comes from the root's serialized position after world-grid initialization.
    internal sealed class NativeDupeLoadEntry
    {
        internal MinionIdentity Dupe;
        internal int NativeId;
        internal int SavedWorldId;
    }

    // References are deliberately transient. An unknown new native object cannot inherit
    // ownership merely by reusing an old integer ID in this running simulation.
    internal sealed class NativeDupeOrigins
    {
        private static readonly FieldInfo trackerInstance = typeof(KPrefabIDTracker).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly DupeOriginLedger ledger;
        private readonly System.Func<int, string> planetForWorld;
        private Dictionary<string, MinionIdentity> live = new Dictionary<string, MinionIdentity>(StringComparer.Ordinal);
        private readonly HashSet<int> unknownReported = new HashSet<int>();
        private readonly HashSet<int> seenNativeIds = new HashSet<int>();
        private string birthOwner;
        private ClusterManager birthCluster;
        private Dictionary<int, WorldContainer> birthWorlds = new Dictionary<int, WorldContainer>();
        internal bool BirthReady { get; private set; }
        internal NativeDupeOrigins(DupeOriginLedger ledger, System.Func<int, string> planetForWorld)
        { this.ledger = ledger; this.planetForWorld = planetForWorld; }
        internal bool InitializeLegacy(string ownerId, out string error)
        {
            error = null;
            if (ledger.Blocked) { error = ledger.Error; return false; }
            if (!ledger.LegacyPending) return true;
            // A nonempty Live list can still be a partially spawned population. Neither
            // first online access nor new-game OnSpawn proves the full native batch.
            error = "dupe_origin_legacy_native_snapshot_incomplete"; return false;
        }
        internal bool AcceptNormalLoad(NativeDupeLoadEntry[] entries, string ownerId, out string error)
        { return AcceptBatch(entries, ownerId, false, out error); }
        internal bool AcceptNewGame(NativeDupeLoadEntry[] entries, string ownerId, out string error)
        { return AcceptBatch(entries, ownerId, true, out error); }
        private bool AcceptBatch(NativeDupeLoadEntry[] entries, string ownerId, bool newGame, out string error)
        {
            SuspendBirths();
            error = null;
            if (ledger.Blocked) { error = ledger.Error; return false; }
            if ((newGame && (!ledger.LegacyPending || entries == null || entries.Length == 0)) || entries == null || entries.Length > DupeOriginCodec.MaxRecords)
            { error = "dupe_origin_load_batch"; return false; }
            var byId = new Dictionary<int, NativeDupeLoadEntry>();
            foreach (var entry in entries)
            {
                KPrefabID id;
                if (entry == null || !TryLoadedIdentity(entry.Dupe, out id) || id.InstanceID != entry.NativeId ||
                    !ValidWorld(entry.SavedWorldId) || byId.ContainsKey(entry.NativeId))
                { error = "dupe_origin_load_native_conflict"; return false; }
                if (newGame)
                {
                    KPrefabID actual; int world; MinionIdentity unique;
                    if (!TryNative(entry.Dupe, out actual, out world) || !TryUniqueLive(entry.NativeId, out unique) || !ReferenceEquals(unique, entry.Dupe))
                    { error = "dupe_origin_newgame_native_incomplete"; return false; }
                }
                byId.Add(entry.NativeId, entry);
            }
            var loadedPlanets = new Dictionary<int, string>();
            var cluster = ClusterManager.Instance;
            var preparedWorlds = new Dictionary<int, WorldContainer>();
            foreach (var entry in entries)
            {
                if (loadedPlanets.ContainsKey(entry.SavedWorldId)) continue;
                string planet = planetForWorld(entry.SavedWorldId);
                if (!DupeOriginCodec.ValidId(planet))
                { error = "dupe_origin_legacy_planet_unresolved"; return false; }
                loadedPlanets.Add(entry.SavedWorldId, planet);
                preparedWorlds.Add(entry.SavedWorldId, cluster.GetWorld(entry.SavedWorldId));
            }
            var prepared = new Dictionary<string, MinionIdentity>(StringComparer.Ordinal);
            if (ledger.LegacyPending)
            {
                var candidates = new List<NativeDupeOriginBinding>();
                foreach (var entry in entries)
                {
                    if (entry.Dupe.GetComponent<KPrefabID>().HasTag(GameTags.Dead)) continue;
                    string planet = loadedPlanets[entry.SavedWorldId];
                    candidates.Add(new NativeDupeOriginBinding { NativeInstanceId = entry.NativeId,
                        NativeWorldId = entry.SavedWorldId, CurrentPlanetId = planet,
                        AdmissionKind = newGame ? DupeAdmissionKind.NativeLocal : DupeAdmissionKind.LegacyLocal, AdmissionTransferId = "" });
                }
                // The ledger validates the complete candidate array before its one commit.
                bool bootstrapped = newGame ? ledger.TryBootstrapVerifiedNewGame(ownerId, candidates.ToArray(), out error) :
                    ledger.TryBootstrapLegacyLocal(ownerId, candidates.ToArray(), out error);
                if (!bootstrapped) return false;
            }
            foreach (var binding in ledger.CaptureDocument().Bindings)
            {
                NativeDupeLoadEntry entry;
                // A saved binding may describe an already absent/dead object. Preserve it;
                // absence never authorizes an unrelated replacement or a later ID reuse.
                if (!byId.TryGetValue(binding.NativeInstanceId, out entry)) continue;
                if (entry.SavedWorldId != binding.NativeWorldId || loadedPlanets[entry.SavedWorldId] != binding.CurrentPlanetId)
                { error = "dupe_origin_load_saved_tuple_conflict"; return false; }
                prepared.Add(binding.StableDupeId, entry.Dupe);
            }
            // No partial reference registration on a rejected batch. No later Live scan
            // can recover an ID that was not observed in this exact native load.
            live = prepared;
            birthOwner = ownerId;
            birthCluster = cluster; birthWorlds = preparedWorlds;
            seenNativeIds.UnionWith(byId.Keys);
            seenNativeIds.UnionWith(ledger.CaptureDocument().Bindings.Select(b => b.NativeInstanceId));
            BirthReady = true;
            return true;
        }
        internal void SuspendBirths()
        {
            BirthReady = false;
            birthOwner = null;
            birthCluster = null; birthWorlds.Clear();
            live.Clear();
            unknownReported.Clear();
        }
        internal bool BirthWorldReady(int worldId)
        {
            WorldContainer world;
            return BirthReady && birthCluster != null && ReferenceEquals(birthCluster, ClusterManager.Instance) &&
                birthWorlds.TryGetValue(worldId, out world) && world != null && ReferenceEquals(world, birthCluster.GetWorld(worldId));
        }
        internal void ObserveNativeIds(IEnumerable<int> ids)
        { seenNativeIds.UnionWith(ids); }
        // Only the closed synchronous recruitment observer supplies this admission.
        internal bool AcceptBirth(MinionIdentity dupe, int nativeId, int worldId, string planetId, string ownerId, out string error)
        {
            error = "dupe_origin_birth_native_conflict";
            KPrefabID id; int world; MinionIdentity unique; DupeOriginRecord existing;
            if (!BirthWorldReady(worldId) || ledger.Blocked || ledger.LegacyPending || !DupeOriginCodec.ValidId(ownerId) ||
                ownerId != birthOwner || ownerId != LocalOwnerProfile.PersistentOwnerId ||
                !DupeOriginCodec.ValidId(planetId) || !TryNative(dupe, out id, out world) ||
                id.InstanceID != nativeId || world != worldId || planetForWorld(world) != planetId ||
                !TryUniqueLive(nativeId, out unique) || !ReferenceEquals(unique, dupe)) return false;
            if (ledger.TryResolve(nativeId, world, planetId, out existing))
            {
                var binding = ledger.GetBinding(existing.StableDupeId);
                if (binding.AdmissionKind != DupeAdmissionKind.NativeLocal || existing.Kind != DupeOriginKind.NativeLocal ||
                    existing.OriginalOwnerId != ownerId || existing.OriginPlanetId != planetId ||
                    !IsCurrentKnownObject(existing.StableDupeId, nativeId, world)) return false;
                error = null; return true;
            }
            if (seenNativeIds.Contains(nativeId)) return false;
            // Allocate the replacement live index before committing the durable pair.
            var prepared = new Dictionary<string, MinionIdentity>(live.Count + 1, StringComparer.Ordinal);
            foreach (var pair in live) prepared.Add(pair.Key, pair.Value);
            DupeOriginRecord origin;
            if (!ledger.TryRegisterVerifiedNativeBirth(ownerId, new NativeDupeOriginBinding {
                NativeInstanceId = nativeId, NativeWorldId = world, CurrentPlanetId = planetId,
                AdmissionKind = DupeAdmissionKind.NativeLocal, AdmissionTransferId = "" }, out origin, out error)) return false;
            prepared.Add(origin.StableDupeId, dupe);
            live = prepared;
            seenNativeIds.Add(nativeId);
            return true;
        }
        private static bool Tracked(KPrefabID id)
        {
            // Get() creates the native singleton. Observational origin reads must not.
            var tracker = trackerInstance == null ? null : trackerInstance.GetValue(null) as KPrefabIDTracker;
            return tracker != null && ReferenceEquals(tracker.GetInstance(id.InstanceID), id);
        }
        private static bool TryLoadedIdentity(MinionIdentity dupe, out KPrefabID id)
        {
            id = null;
            if (dupe == null) return false;
            id = dupe.GetComponent<KPrefabID>();
            return id != null && id.InstanceID >= 0 && !id.pendingDestruction && !id.conflicted &&
                Tracked(id);
        }
        private static bool ValidWorld(int world)
        {
            if (world < 0 || world >= 255 || ClusterManager.Instance == null) return false;
            try { return ClusterManager.Instance.GetWorld(world) != null; }
            catch (Exception error) when (error is ArgumentException || error is KeyNotFoundException || error is InvalidOperationException)
            { return false; }
        }
        internal void RefreshKnownObjects()
        {
            if (ledger.Blocked || ledger.LegacyPending) return;
            foreach (var binding in ledger.CaptureDocument().Bindings)
            {
                if (binding.NativeWorldId == DupeOriginCodec.RetiredWorldId) continue;
                MinionIdentity dupe, unique; KPrefabID id; int world;
                if (!live.TryGetValue(binding.StableDupeId, out dupe) || !TryNative(dupe, out id, out world)) continue;
                if (!TryUniqueLive(id.InstanceID, out unique) || !ReferenceEquals(unique, dupe)) continue;
                string planet = planetForWorld(world);
                if (!DupeOriginCodec.ValidId(planet)) continue;
                var next = binding.Copy(); next.NativeInstanceId = id.InstanceID; next.NativeWorldId = world; next.CurrentPlanetId = planet;
                string error;
                if (!next.SameAs(binding) && !ledger.TryRebindVerifiedNative(binding, next, out error))
                    Debug.LogWarning("[OniOnline] Native duplicant binding rejected: " + error);
            }
        }
        // Prepare/audit callers may inspect an existing exact binding without bootstrap,
        // native rebind, reference registration, or any durable journal mutation.
        internal bool TryReadExisting(int nativeId, int worldId, string planetId, out DupeOriginRecord origin)
        {
            origin = null;
            if (ledger.Blocked || ledger.LegacyPending || !ledger.TryResolve(nativeId, worldId, planetId, out origin)) return false;
            if (IsCurrentKnownObject(origin.StableDupeId, nativeId, worldId)) return true;
            origin = null; return false;
        }
        internal bool TryResolve(WorkPlanet planet, int nativeId, out DupeOriginRecord origin)
        {
            origin = null; RefreshKnownObjects();
            if (planet == null || !ledger.TryResolve(nativeId, planet.NativeWorldId, planet.PlanetId, out origin))
            { ReportUnknown(nativeId); return false; }
            if (!IsCurrentKnownObject(origin.StableDupeId, nativeId, planet.NativeWorldId))
            { origin = null; ReportUnknown(nativeId); return false; }
            return true;
        }
        private bool IsCurrentKnownObject(string stableId, int nativeId, int worldId)
        {
            MinionIdentity known, current; KPrefabID id; int world;
            return live.TryGetValue(stableId, out known) && TryUniqueLive(nativeId, out current) &&
                ReferenceEquals(known, current) && TryNative(current, out id, out world) && world == worldId;
        }
        private static bool TryUniqueLive(int nativeId, out MinionIdentity match)
        {
            match = null;
            foreach (MinionIdentity candidate in Components.LiveMinionIdentities.Items)
            {
                if (candidate == null) continue;
                var id = candidate.GetComponent<KPrefabID>();
                // Count IDs before validating position/world: a duplicate in an unknown
                // world must still invalidate this identity, rather than disappear.
                if (id == null || id.InstanceID != nativeId) continue;
                if (!ReferenceEquals(match, null)) { match = null; return false; }
                match = candidate;
            }
            if (ReferenceEquals(match, null)) return false;
            KPrefabID actual; int world;
            if (TryNative(match, out actual, out world)) return true;
            match = null; return false;
        }
        internal void CaptureKnownObjectsForSave()
        {
            RefreshKnownObjects();
            if (ledger.Blocked || ledger.LegacyPending) return;
            foreach (var binding in ledger.CaptureDocument().Bindings)
            {
                MinionIdentity dupe;
                bool everConfirmed = live.TryGetValue(binding.StableDupeId, out dupe);
                bool exactObjectDestroyed = everConfirmed && !ReferenceEquals(dupe, null) && dupe == null;
                bool matchingLiveNativeId = Components.LiveMinionIdentities.Items.Any(candidate =>
                {
                    if (candidate == null) return false;
                    var candidateId = candidate.GetComponent<KPrefabID>();
                    return candidateId != null && candidateId.InstanceID == binding.NativeInstanceId;
                });
                // Unknown/missing index, an unexplained location, and duplicate IDs are not
                // evidence of death. Preserve those mappings; their online commands fail closed.
                if (DupeOriginRetentionPolicy.MayRemoveBinding(everConfirmed, exactObjectDestroyed, matchingLiveNativeId))
                {
                    // Keep the native ID reserved across saves; 255 is never a live world.
                    var retired = binding.Copy(); retired.NativeWorldId = DupeOriginCodec.RetiredWorldId;
                    string error;
                    if (ledger.TryRebindVerifiedNative(binding, retired, out error)) live.Remove(binding.StableDupeId);
                }
            }
        }
        private void ReportUnknown(int nativeId)
        {
            if (unknownReported.Add(nativeId)) Debug.LogWarning("[OniOnline] Online duplicant command denied: missing verified origin/binding. " +
                "Only verified closed recruitment/load batches establish ownership; unknown objects are not assigned to the computation host.");
        }
        private static bool TryNative(MinionIdentity dupe, out KPrefabID id, out int world)
        {
            id = null; world = -1;
            if (dupe == null || !dupe.isSpawned) return false;
            id = dupe.GetComponent<KPrefabID>(); int cell = Grid.PosToCell(dupe);
            if (id == null || id.InstanceID < 0 || id.pendingDestruction || id.conflicted || id.HasTag(GameTags.Dead) ||
                !Tracked(id) || !Grid.IsValidCell(cell)) return false;
            if (Grid.WorldIdx == null || cell >= Grid.WorldIdx.Length) return false;
            world = Grid.WorldIdx[cell];
            if (world < 0 || world >= 255 || ClusterManager.Instance == null) return false;
            try { return ClusterManager.Instance.GetWorld(world) != null; }
            catch (Exception error) when (error is ArgumentException || error is KeyNotFoundException || error is InvalidOperationException)
            { return false; }
        }
    }
}
