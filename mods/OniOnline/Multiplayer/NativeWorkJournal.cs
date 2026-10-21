using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSerialization;

namespace OniOnline.Multiplayer
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class NativeWorkSavedState
    {
        [Serialize] public string Payload = "";
    }

    // SaveGame owns the durable payload. This registry only indexes exact marked native objects.
    internal static class NativeWorkJournal
    {
        private static object owner;
        private static RemoteWorkSavedDocument document = new RemoteWorkSavedDocument();
        private static readonly Dictionary<string, RemoteWorkDigProvenance> live = new Dictionary<string, RemoteWorkDigProvenance>();
        private static readonly Dictionary<string, RemoteWorkBuildProvenance> liveBuilds = new Dictionary<string, RemoteWorkBuildProvenance>();
        private static readonly Dictionary<string, RemoteWorkBuildProvenance> stagedBuilds = new Dictionary<string, RemoteWorkBuildProvenance>();
        private static readonly Dictionary<string, Registration> authorities = new Dictionary<string, Registration>();
        private sealed class Registration { internal WorkPlanet Planet; internal string OwnerPeer; internal RemoteWorkAuthority Authority; internal NativeWorkExecutor Executor; }
        internal static void BeginLoad(object saveOwner)
        { owner = saveOwner; document = new RemoteWorkSavedDocument(); live.Clear(); liveBuilds.Clear(); stagedBuilds.Clear(); authorities.Clear(); }
        internal static void LoadSavedState(object saveOwner, NativeWorkSavedState state, string persistentOwnerId)
        {
            if (!ReferenceEquals(owner, saveOwner)) BeginLoad(saveOwner);
            try
            {
                document = state == null || string.IsNullOrEmpty(state.Payload) ? new RemoteWorkSavedDocument { PersistentOwnerId = persistentOwnerId } : RemoteWorkPersistence.Decode(state.Payload);
                if (document.PersistentOwnerId != persistentOwnerId) throw new InvalidDataException("work_journal_owner_mismatch");
            }
            catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException)
            { document = new RemoteWorkSavedDocument { PersistentOwnerId = persistentOwnerId }; Debug.LogWarning("[OniOnline] Work journal rejected; marked orders fail closed: " + error.Message); }
        }
        internal static NativeWorkSavedState CaptureSavedState(object saveOwner, string persistentOwnerId)
        {
            if (!ReferenceEquals(owner, saveOwner)) throw new InvalidOperationException("work_journal_save_owner");
            document.PersistentOwnerId = persistentOwnerId;
            foreach (var registration in authorities.Values.ToArray()) Refresh(registration);
            document.Planets.RemoveAll(p => p.Plans.Length == 0 && p.Orders.Length == 0);
            return new NativeWorkSavedState { Payload = RemoteWorkPersistence.Encode(document) };
        }
        internal static RemoteWorkSavedPlanet Restore(WorkPlanet planet, string ownerPeer, RemoteWorkAuthority authority, NativeWorkExecutor executor)
        {
            var saved = document.Planets.FirstOrDefault(p => p.PlanetId == planet.PlanetId && p.NativeWorldId == planet.NativeWorldId);
            // One authority/executor follows the selected world. Its previous planet keeps
            // the last published snapshot, but must no longer call into that moving scope.
            foreach (string key in authorities.Where(p => ReferenceEquals(p.Value.Authority, authority) ||
                ReferenceEquals(p.Value.Executor, executor)).Select(p => p.Key).ToArray()) authorities.Remove(key);
            authorities[planet.PlanetId] = new Registration { Planet = planet, OwnerPeer = ownerPeer, Authority = authority, Executor = executor };
            if (saved == null || string.IsNullOrEmpty(planet.PersistentOwnerId) || document.PersistentOwnerId != planet.PersistentOwnerId) return null;
            foreach (var marker in stagedBuilds.Values.Where(m => m != null && m.PlanetId == planet.PlanetId).ToArray())
            {
                if (!executor.SupportsBuildingExecution || !MatchesSavedBuild(marker)) marker.Quarantine("work_native_build_validation_required");
                else
                {
                    stagedBuilds.Remove(marker.OrderToken); marker.ClaimRestored(); Register(marker);
                    Debug.Log("[OniOnline] Restored managed build retained with exact journal token: " + marker.OrderToken);
                }
            }
            return saved;
        }
        internal static void Publish(string planetId)
        { Registration entry; if (authorities.TryGetValue(planetId, out entry)) Refresh(entry); }
        private static void Refresh(Registration entry)
        {
            if (!MatchesCurrentScope(entry)) return;
            var saved = new RemoteWorkSavedPlanet { PlanetId = entry.Planet.PlanetId, NativeWorldId = entry.Planet.NativeWorldId,
                OwnerPeerId = entry.OwnerPeer, Blocked = entry.Authority.IsBlocked, Plans = entry.Authority.ExportConfirmedPlans() };
            saved.Orders = live.Values.Where(m => m != null && m.PlanetId == saved.PlanetId).Select(m => m.CaptureOrder())
                .Concat(liveBuilds.Values.Where(m => m != null && m.PlanetId == saved.PlanetId).Select(m => m.CaptureOrder()))
                .Where(o => saved.Plans.Any(p => RemoteWorkPersistence.SameIntent(p, o.Winner))).ToArray();
            document.Planets.RemoveAll(p => p.PlanetId == saved.PlanetId); document.Planets.Add(saved);
        }
        private static bool MatchesCurrentScope(Registration entry)
        {
            WorkPlanet current = entry.Authority.Planet;
            return current != null && current.PlanetId == entry.Planet.PlanetId &&
                current.NativeWorldId == entry.Planet.NativeWorldId && current.AuthorityEpoch == entry.Planet.AuthorityEpoch &&
                current.PersistentOwnerId == entry.Planet.PersistentOwnerId && current.OwnerPeerId == entry.OwnerPeer &&
                current.AuthorityPeerId == entry.OwnerPeer;
        }
        internal static void Register(RemoteWorkDigProvenance marker)
        {
            if (liveBuilds.ContainsKey(marker.OrderToken) || stagedBuilds.ContainsKey(marker.OrderToken)) throw new InvalidOperationException("work_journal_duplicate_native_token");
            RemoteWorkDigProvenance previous;
            if (live.TryGetValue(marker.OrderToken, out previous) && previous != marker && previous != null)
                throw new InvalidOperationException("work_journal_duplicate_native_token");
            live[marker.OrderToken] = marker;
        }
        internal static bool TryRetain(RemoteWorkDigProvenance marker)
        {
            RemoteWorkSavedOrder candidate = marker.CaptureOrder();
            var planet = document.Planets.FirstOrDefault(p => p.PlanetId == marker.PlanetId);
            if (planet == null || planet.Blocked || !planet.Orders.Any(o => o.Token == candidate.Token && o.OriginEpoch == candidate.OriginEpoch &&
                    RemoteWorkPersistence.SameIntent(o.Winner, candidate.Winner)) || !marker.MatchesNativeCell(planet.NativeWorldId)) return false;
            if (live.ContainsKey(marker.OrderToken) || liveBuilds.ContainsKey(marker.OrderToken) || stagedBuilds.ContainsKey(marker.OrderToken)) return false;
            Register(marker); return true;
        }
        internal static RemoteWorkDigProvenance[] Markers(string planetId)
        { return live.Values.Where(m => m != null && m.PlanetId == planetId).ToArray(); }
        internal static void Removed(RemoteWorkDigProvenance marker, bool intentional)
        {
            RemoteWorkDigProvenance known;
            if (!live.TryGetValue(marker.OrderToken, out known) || known != marker) return;
            live.Remove(marker.OrderToken);
            if (intentional) return;
            var saved = document.Planets.FirstOrDefault(p => p.PlanetId == marker.PlanetId);
            if (saved != null) RemoteWorkPersistence.RemoveCell(saved, marker.TaskKey);
            Registration registration;
            if (authorities.TryGetValue(marker.PlanetId, out registration) && MatchesCurrentScope(registration))
            {
                registration.Executor.ForgetMarker(marker);
                registration.Authority.ForgetNativeCell(marker.TaskKey);
            }
        }
        internal static void Register(RemoteWorkBuildProvenance marker)
        {
            if (!marker.Managed) throw new InvalidOperationException("work_build_marker_quarantined");
            RemoteWorkBuildProvenance staged;
            if (stagedBuilds.TryGetValue(marker.OrderToken, out staged) && staged != marker) throw new InvalidOperationException("work_journal_duplicate_native_token");
            RemoteWorkBuildProvenance previous;
            if (live.ContainsKey(marker.OrderToken) || liveBuilds.TryGetValue(marker.OrderToken, out previous) && previous != null && previous != marker)
                throw new InvalidOperationException("work_journal_duplicate_native_token");
            liveBuilds[marker.OrderToken] = marker;
        }
        internal static bool TryRetain(RemoteWorkBuildProvenance marker)
        {
            // OnSpawn never claims a blueprint by itself. A prior authority registration
            // closes this restore window, so a late Start cannot bypass a closed capability
            // or join an already-confirmed canonical snapshot without reconciliation.
            if (string.IsNullOrEmpty(marker.PlanetId) || string.IsNullOrEmpty(marker.OrderToken) || !marker.HasRestoredProvenance || authorities.ContainsKey(marker.PlanetId) || !MatchesSavedBuild(marker) ||
                live.ContainsKey(marker.OrderToken) || liveBuilds.ContainsKey(marker.OrderToken) || stagedBuilds.ContainsKey(marker.OrderToken)) return false;
            stagedBuilds[marker.OrderToken] = marker;
            return true;
        }
        private static bool MatchesSavedBuild(RemoteWorkBuildProvenance marker)
        {
            RemoteWorkSavedOrder candidate = marker.CaptureOrder();
            var planet = document.Planets.FirstOrDefault(p => p.PlanetId == marker.PlanetId);
            return planet != null && !planet.Blocked && planet.Orders.Any(o => o.Token == candidate.Token && o.OriginEpoch == candidate.OriginEpoch &&
                o.NativeObjectId == candidate.NativeObjectId && RemoteWorkPersistence.SameIntent(o.Winner, candidate.Winner)) && marker.MatchesNativeCell(planet.NativeWorldId);
        }
        internal static void ReleaseBuildMarker(RemoteWorkBuildProvenance marker)
        {
            if (string.IsNullOrEmpty(marker.OrderToken)) return;
            RemoteWorkBuildProvenance known;
            if (liveBuilds.TryGetValue(marker.OrderToken, out known) && known == marker) liveBuilds.Remove(marker.OrderToken);
            if (stagedBuilds.TryGetValue(marker.OrderToken, out known) && known == marker) stagedBuilds.Remove(marker.OrderToken);
        }
        internal static RemoteWorkBuildProvenance[] BuildMarkers(string planetId)
        { return liveBuilds.Values.Where(m => m != null && m.PlanetId == planetId).ToArray(); }
        internal static void Removed(RemoteWorkBuildProvenance marker, bool intentional)
        {
            RemoteWorkBuildProvenance known;
            if (!liveBuilds.TryGetValue(marker.OrderToken, out known) || known != marker) return;
            liveBuilds.Remove(marker.OrderToken);
            if (intentional) return;
            WorkTaskIntent removed = marker.CaptureOrder().Winner;
            var saved = document.Planets.FirstOrDefault(p => p.PlanetId == marker.PlanetId);
            if (saved != null) RemoteWorkPersistence.RemovePlan(saved, removed);
            Registration registration;
            if (authorities.TryGetValue(marker.PlanetId, out registration) && MatchesCurrentScope(registration))
            {
                registration.Executor.ForgetBuildMarker(marker);
                registration.Authority.ForgetNativePlan(removed);
            }
        }
    }
}
