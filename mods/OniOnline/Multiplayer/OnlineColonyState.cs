using System;
using System.Collections.Generic;
using System.Linq;
using KSerialization;
using OniOnline.Core;
using OniOnline.Network;
using OniOnline.Runtime;
using UnityEngine;
using System.Runtime.Serialization;

namespace OniOnline.Multiplayer
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class PlanetIdentity
    {
        [Serialize] public int NativeWorldId;
        [Serialize] public string PlanetId;
        [Serialize] public string LineageId;
    }

    // Attached to SaveGame before native deserialization. Planet/home IDs and the
    // selected world travel with native saves, rather than changing on every room join.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class OnlineColonyState : KMonoBehaviour, ISaveLoadable
    {
        [Serialize] private int schema = 1;
        [Serialize] private string ownerId;
        [Serialize] private string homeSystemId;
        [Serialize] private string homeSystemName;
        [Serialize] private int selectedNativeWorldId = -1;
        [Serialize] private List<PlanetIdentity> planets = new List<PlanetIdentity>();
        [Serialize] private NativeWorkSavedState workJournal = new NativeWorkSavedState();
        [Serialize] private string accountBanPayload;
        [Serialize] private int dupeOriginSchema;
        [Serialize] private string dupeOriginPayload;
        private DupeOriginLedger dupeOrigins;
        private NativeDupeOrigins nativeDupeOrigins;
        private bool originFailureReported;
        private string originInitializationError;
        private bool initialized;
        internal bool OwnershipBlocked { get { EnsureOriginState(); return dupeOrigins.Blocked || originInitializationError != null; } }
        internal string OwnershipError { get { EnsureOriginState(); return dupeOrigins.Error ?? originInitializationError; } }
        internal string PersistentOwnerId { get { Ensure(); return ownerId; } }
        internal string HomeSystemId { get { Ensure(); return homeSystemId; } }
        internal string HomeSystemName
        {
            get { Ensure(); return homeSystemName; }
            set { Ensure(); if (!Network.Protocol.ValidText(value, 64)) throw new ArgumentException("invalid_room_name"); homeSystemName = value; }
        }
        internal int SelectedNativeWorldId { get { Ensure(); return selectedNativeWorldId; } }
        internal string SavedAccountBans { get { Ensure(); return accountBanPayload; } }
        internal void StoreAccountBans(string payload)
        {
            Ensure();
            RoomModerationPersistence.Decode(homeSystemId, payload);
            accountBanPayload = payload;
        }

        internal static OnlineColonyState Current
        {
            get
            {
                return SaveGame.Instance == null || Game.Instance == null || !Game.Instance.baseAlreadyCreated ||
                    Game.Instance.IsLoading() ? null : SaveGame.Instance.GetComponent<OnlineColonyState>();
            }
        }

        private void Ensure()
        {
            if (initialized) return;
            if (schema != 1) throw new InvalidOperationException("planet_identity_version");
            if (string.IsNullOrEmpty(ownerId))
            {
                ownerId = LocalOwnerProfile.PersistentOwnerId;
            }
            if (string.IsNullOrEmpty(homeSystemId)) homeSystemId = Guid.NewGuid().ToString("N");
            if (!SystemWire.Id(ownerId) || !SystemWire.Id(homeSystemId) || planets == null || planets.Count > 256 ||
                planets.Any(p => p == null || p.NativeWorldId < 0 || !SystemWire.Id(p.PlanetId) || !SystemWire.Id(p.LineageId)) ||
                planets.Select(p => p.NativeWorldId).Distinct().Count() != planets.Count ||
                planets.Select(p => p.PlanetId).Distinct().Count() != planets.Count)
                throw new InvalidOperationException("planet_invalid_manifest");
            if (string.IsNullOrEmpty(homeSystemName)) homeSystemName = OnlineRuntime.Instance?.DefaultHomeName ?? OnlineText.DefaultSystemName(Environment.UserName);
            initialized = true;
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            NativeWorkJournal.BeginLoad(this);
        }

        protected override void OnCleanUp()
        {
            NativeDupeNewGameObserver.Cancel(this);
            base.OnCleanUp();
        }

        [OnDeserialized]
        private void AfterLoad()
        {
            EnsureOriginState();
            NativeWorkJournal.LoadSavedState(this, workJournal, ownerId);
        }

        [OnSerializing]
        private void BeforeSave()
        {
            Ensure();
            if (ClusterManager.Instance != null) CapturePlanet();
            workJournal = NativeWorkJournal.CaptureSavedState(this, ownerId);
            if (EnsureNativeOrigins()) nativeDupeOrigins.CaptureKnownObjectsForSave();
            if (dupeOrigins != null)
            {
                dupeOriginSchema = dupeOrigins.SchemaToSave;
                dupeOriginPayload = dupeOrigins.CapturePayload();
            }
        }

        private void EnsureOriginState()
        {
            Ensure();
            if (dupeOrigins != null) return;
            dupeOrigins = DupeOriginLedger.Load(dupeOriginSchema, dupeOriginPayload, homeSystemId);
            nativeDupeOrigins = new NativeDupeOrigins(dupeOrigins, PlanetIdentityForNativeWorld);
            if (dupeOrigins.Blocked && !originFailureReported)
            {
                originFailureReported = true;
                Debug.LogWarning("[OniOnline] Duplicant ownership rejected; online commands fail closed and the original payload is retained: " + dupeOrigins.Error);
            }
        }
        // Called only by the successful, closed normal whole-save load observer.
        // It does not depend on Current/baseAlreadyCreated or on partial OnSpawn lists.
        internal bool AcceptNormalLoadOrigins(NativeDupeLoadEntry[] entries, out string error)
        {
            EnsureOriginState();
            int priorPlanets = planets.Count;
            bool accepted = false;
            try { accepted = nativeDupeOrigins.AcceptNormalLoad(entries, ownerId, out error); return accepted; }
            finally { if (!accepted && planets.Count > priorPlanets) planets.RemoveRange(priorPlanets, planets.Count - priorPlanets); }
        }
        internal bool AcceptNewGameOrigins(NativeDupeLoadEntry[] entries, out string error)
        {
            EnsureOriginState();
            int priorPlanets = planets.Count;
            bool accepted = false;
            try { accepted = nativeDupeOrigins.AcceptNewGame(entries, ownerId, out error); return accepted; }
            finally { if (!accepted && planets.Count > priorPlanets) planets.RemoveRange(priorPlanets, planets.Count - priorPlanets); }
        }
        private void Update()
        {
            // Only an already closed exact starter batch is retried here. This is not a
            // timing inference about arbitrary population completeness or native births.
            NativeDupeNewGameObserver.TryComplete(this);
        }
        private bool EnsureNativeOrigins()
        {
            NativeDupeNewGameObserver.TryComplete(this);
            EnsureOriginState();
            if (dupeOrigins.Blocked || originInitializationError != null || Game.Instance == null ||
                !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading() || ClusterManager.Instance == null) return false;
            string error;
            if (!nativeDupeOrigins.InitializeLegacy(ownerId, out error))
            {
                // Loading a native world can temporarily expose no live minions or no
                // resolved world. Preserve legacy-pending and retry; never save an empty
                // ownership table or turn an incomplete frame into a permanent failure.
                if (error == "dupe_origin_legacy_native_snapshot_incomplete" || error == "dupe_origin_legacy_planet_unresolved") return false;
                originInitializationError = error;
                Debug.LogWarning("[OniOnline] Legacy duplicant ownership bootstrap rejected: " + error);
                return false;
            }
            nativeDupeOrigins.RefreshKnownObjects();
            return true;
        }
        private string PlanetIdentityForNativeWorld(int worldId)
        {
            Ensure();
            if (worldId < 0 || worldId >= 255 || ClusterManager.Instance == null) return null;
            try { if (ClusterManager.Instance.GetWorld(worldId) == null) return null; }
            catch (Exception error) when (error is ArgumentException || error is KeyNotFoundException || error is InvalidOperationException)
            { return null; }
            var identity = planets.FirstOrDefault(p => p.NativeWorldId == worldId);
            if (identity == null)
            {
                identity = new PlanetIdentity { NativeWorldId = worldId, PlanetId = Guid.NewGuid().ToString("N"), LineageId = Guid.NewGuid().ToString("N") };
                planets.Add(identity);
            }
            return identity.PlanetId;
        }
        // Strictly observational counterpart for round-trip preparation and audits.
        internal bool TryReadDuplicantOrigin(int nativeId, int nativeWorldId, out DupeOriginRecord origin)
        {
            origin = null;
            if (dupeOrigins == null || nativeDupeOrigins == null || dupeOrigins.Blocked || dupeOrigins.LegacyPending || Game.Instance == null || !Game.Instance.baseAlreadyCreated || Game.Instance.IsLoading()) return false;
            var identity = planets.FirstOrDefault(p => p.NativeWorldId == nativeWorldId);
            return identity != null && nativeDupeOrigins.TryReadExisting(nativeId, nativeWorldId, identity.PlanetId, out origin);
        }
        // Native archive metadata may read origin, but may never infer it from the current room host.
        internal bool TryResolveDuplicantOrigin(int nativeId, int nativeWorldId, out DupeOriginRecord origin)
        {
            origin = null;
            if (!EnsureNativeOrigins()) return false;
            return nativeDupeOrigins.TryResolve(new WorkPlanet { NativeWorldId = nativeWorldId,
                PlanetId = PlanetIdentityForNativeWorld(nativeWorldId) }, nativeId, out origin);
        }
        internal bool TryResolveDuplicantOrigin(WorkPlanet planet, int nativeId, out DupeOriginRecord origin)
        {
            origin = null;
            return planet != null && EnsureNativeOrigins() && PlanetIdentityForNativeWorld(planet.NativeWorldId) == planet.PlanetId &&
                nativeDupeOrigins.TryResolve(planet, nativeId, out origin);
        }
        internal WorldContainer[] AvailablePlanets()
        {
            Ensure();
            if (ClusterManager.Instance == null) return new WorldContainer[0];
            return ClusterManager.Instance.WorldContainers.Where(w => w != null && !w.IsModuleInterior && w.IsDiscovered)
                .OrderBy(w => w.id).ToArray();
        }

        internal bool Select(int nativeWorldId)
        {
            if (!AvailablePlanets().Any(w => w.id == nativeWorldId)) return false;
            selectedNativeWorldId = nativeWorldId; return true;
        }

        internal PlanetManifest CapturePlanet()
        {
            Ensure();
            var worlds = AvailablePlanets();
            if (selectedNativeWorldId < 0)
            {
                var first = worlds.FirstOrDefault(w => ClusterManager.Instance.activeWorldId == w.id) ?? worlds.FirstOrDefault();
                if (first == null) return null;
                selectedNativeWorldId = first.id;
            }
            var world = worlds.FirstOrDefault(w => w.id == selectedNativeWorldId);
            if (world == null) return null;
            var identity = planets.FirstOrDefault(p => p.NativeWorldId == world.id);
            if (identity == null)
            {
                identity = new PlanetIdentity { NativeWorldId = world.id, PlanetId = Guid.NewGuid().ToString("N"),
                    LineageId = Guid.NewGuid().ToString("N") };
                planets.Add(identity);
            }
            string name = world.GetProperName();
            if (!Network.Protocol.ValidText(name, 64)) name = "Asteroid " + world.id;
            var manifest = new PlanetManifest { PlanetId = identity.PlanetId, LineageId = identity.LineageId, OwnerId = ownerId,
                HomeSystemId = homeSystemId, NativeWorldId = world.id, Name = name, Width = world.WorldSize.x, Height = world.WorldSize.y };
            SystemWire.ValidatePlanet(manifest); return manifest;
        }
    }

    internal sealed class NativePlanetSource : IPlanetSource
    {
        public PlanetManifest CapturePlanet()
        { var colony = OnlineColonyState.Current; return colony == null ? null : colony.CapturePlanet(); }
    }
}
