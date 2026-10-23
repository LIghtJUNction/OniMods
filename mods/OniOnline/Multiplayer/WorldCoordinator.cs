using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Network;
using OniOnline.UI;

namespace OniOnline.Multiplayer
{
    // Unity window lifetime stays outside the replicated room state.
    internal sealed class WorldCoordinator : IWorldViewModel, IDisposable
    {
        private readonly WorldReplication replication;
        internal StarSystemCoordinator StarSystem { get; private set; }
        internal RemoteWorkCoordinator Work { get; private set; }
        internal TeamColonyCoordinator Teams { get; private set; }
        internal DupeCommandCoordinator DupeCommands { get; private set; }
        private readonly RegisteredRoomOwners registeredOwners = new RegisteredRoomOwners();
        private readonly Dictionary<string, string> directGrantIssuers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly RoomSession room;
        private readonly NativeWorkContext nativeWorkContext;
        private double nextDupeRoster;
        public WorldInterestSession Interest { get; private set; }
        public WorldCellDetailsSession CellDetails { get; private set; }
        private readonly Dictionary<string, NativeWorldInterestSource> interestSources = new Dictionary<string, NativeWorldInterestSource>();
        private readonly Dictionary<string, RemoteWorkPermission> directPermissions = new Dictionary<string, RemoteWorkPermission>();
        private readonly Dictionary<string, RemoteWorkPermission> teamPermissions = new Dictionary<string, RemoteWorkPermission>();
        private string permissionEpoch;
        private WorldOverviewWindow window;
        public long Revision { get; private set; }
        public bool ShareOwnWorld { get { return replication.ShareOwnWorld; } }
        public string LastError { get { return replication.LastError; } }
        public int PendingTransfers { get { return replication.PendingTransfers; } }
        public IReadOnlyList<WorldViewEntry> Worlds { get { return replication.Worlds; } }
        public event System.Action Changed;

        internal WorldCoordinator(RoomSession session)
        {
            room = session;
            var planetSource = new NativePlanetSource();
            var source = new NativeWorldSource(() => OnlineColonyState.Current?.SelectedNativeWorldId ?? -1);
            replication = new WorldReplication(session, source, allowRemoteSnapshots: false);
            replication.Changed += Notify;
            StarSystem = new StarSystemCoordinator(session, planetSource);
            StarSystem.Changed += Notify;
            var workContext = new NativeWorkContext(session, StarSystem);
            nativeWorkContext = workContext;
#if ONIONLINE_RUNTIME_PROBE
            var nativeExecutor = new NativeWorkExecutor(true);
#else
            var nativeExecutor = new NativeWorkExecutor(false);
#endif
            Work = new RemoteWorkCoordinator(session, workContext, nativeExecutor,
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.PeerId);
            Work.Changed += Notify;
            Teams = new TeamColonyCoordinator(session, () =>
            {
                WorkPlanet planet;
                return session.IsHost && workContext.TryGetLocalPlanet(out planet) ? planet : null;
            }, (actor, flags) =>
            {
                if (actor == session.LocalPlayerId) return;
                if (!PreparePermissionScope() || Teams?.Snapshot?.AuthorityEpoch != permissionEpoch) return;
                var permissions = RemoteWorkPermission.None;
                if ((flags & TeamPermissionFlags.ViewPlans) != 0) permissions |= RemoteWorkPermission.ViewPlans;
                if ((flags & TeamPermissionFlags.IssueTasks) != 0) permissions |= RemoteWorkPermission.IssueTasks;
                if ((flags & TeamPermissionFlags.CommandDuplicants) != 0) permissions |= RemoteWorkPermission.CommandDupes;
                teamPermissions[actor] = permissions;
                ApplyPermissions(actor);
            }, IsRegisteredOwner);
            Teams.Changed += Notify;
            DupeCommands = new DupeCommandCoordinator(session, workContext, new NativeDupeCommandExecutor(),
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.PeerId,
                CanControlNativeDuplicant);
            DupeCommands.SubmissionChanged += receipt => Notify();
            Interest = new WorldInterestSession(session,
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.PeerId,
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.Planet.Copy(),
                (observer, planetId, subscription) =>
                {
                    WorkPlanet planet;
                    if (!workContext.TryGetLocalPlanet(out planet) || planet.PlanetId != planetId)
                        throw new InvalidOperationException("world_not_ready");
                    NativeWorldInterestSource sourceForObserver;
                    if (!interestSources.TryGetValue(observer, out sourceForObserver))
                        interestSources.Add(observer, sourceForObserver = new NativeWorldInterestSource());
                    // Current visibility grants are whole-planet. Revocation removes the observer
                    // and its source cache; task revisions must never invalidate terrain caches.
                    return sourceForObserver.Capture(planet, subscription, (long)(GameClock.Instance.GetTime() * 5),
                        1, index => true);
                }, (observer, planetId) =>
                {
                    WorkPlanet planet;
                    if (!workContext.TryGetLocalPlanet(out planet) || planet.PlanetId != planetId) return false;
                    Work.Authority.Synchronize();
                    return replication.ShareOwnWorld || Work.Authority.HasPermission(observer, RemoteWorkPermission.ViewMap);
                });
            Interest.Changed += Notify;
            Interest.ObserverRemoved += observer => interestSources.Remove(observer);
            CellDetails = new WorldCellDetailsSession(session,
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.PeerId,
                (observer, planetId, index) =>
                {
                    WorkPlanet planet;
                    if (!workContext.TryGetLocalPlanet(out planet) || planet.PlanetId != planetId) return null;
                    var manifest = StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.Planet;
                    if (manifest == null || index < 0 || index >= (long)manifest.Width * manifest.Height) return null;
                    string sourceKey = "detail:" + observer;
                    NativeWorldInterestSource sourceForObserver;
                    if (!interestSources.TryGetValue(sourceKey, out sourceForObserver))
                        interestSources.Add(sourceKey, sourceForObserver = new NativeWorldInterestSource());
                    var sub = new WorldInterestSubscription { Revision = 1,
                        Region = new WorldInterestRegion { X = index % manifest.Width, Y = index / manifest.Width, Width = 1, Height = 1 },
                        Channels = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass,
                        HoverIndex = index, NumericIntervalTicks = 1 };
                    var frame = sourceForObserver.Capture(planet, sub, (long)(GameClock.Instance.GetTime() * 5),
                        1, cell => cell == index);
                    if (frame.Cells.Count != 1) return null;
                    return new WorldCellDetailResult { Epoch = planet.AuthorityEpoch, Width = manifest.Width, Height = manifest.Height,
                        Cell = frame.Cells[0] };
                }, (observer, planetId) =>
                {
                    WorkPlanet planet;
                    if (!workContext.TryGetLocalPlanet(out planet) || planet.PlanetId != planetId) return false;
                    Work.Authority.Synchronize();
                    return replication.ShareOwnWorld || Work.Authority.HasPermission(observer, RemoteWorkPermission.ViewMap);
                });
            CellDetails.Changed += Notify;
            CellDetails.ObserverRemoved += observer => interestSources.Remove("detail:" + observer);
        }

        // Native pause/speed remain local. Room membership never controls simulation time.
        public void Pump(double seconds)
        {
            StarSystem.Pump(seconds); replication.Pump(seconds); Work.Pump(seconds); Teams.Pump(seconds);
            RegisterTeamDuplicants(seconds);
            DupeCommands.Pump(seconds); Interest.Pump(seconds); CellDetails.Pump(seconds);
        }
        private void RegisterTeamDuplicants(double seconds)
        {
            if (!room.IsHost || seconds < nextDupeRoster) return;
            nextDupeRoster = seconds + 2;
            WorkPlanet planet;
            if (!nativeWorkContext.TryGetLocalPlanet(out planet) || Teams.Snapshot?.AuthorityEpoch != planet.AuthorityEpoch) return;
            var colony = OnlineColonyState.Current;
            if (colony == null || colony.OwnershipBlocked) return;
            RefreshRegisteredOwners();
            foreach (MinionIdentity dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                int cell = Grid.PosToCell(dupe);
                if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != planet.NativeWorldId) continue;
                KPrefabID id = dupe.GetComponent<KPrefabID>();
                if (id == null || id.InstanceID < 0) continue;
                DupeOriginRecord origin;
                if (!colony.TryResolveDuplicantOrigin(planet, id.InstanceID, out origin)) continue;
                Teams.RegisterVerifiedDuplicant(id.InstanceID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    origin.StableDupeId, origin.OriginalOwnerId, registeredOwners.ProjectionPeer(origin.OriginalOwnerId));
            }
        }
        private void RefreshRegisteredOwners()
        {
            var claims = new List<RegisteredOwnerClaim>();
            var players = room.Players;
            // Opening somebody else's save never changes this process's own profile identity.
            if (!string.IsNullOrEmpty(room.LocalPlayerId)) claims.Add(new RegisteredOwnerClaim { PeerId = room.LocalPlayerId,
                OwnerId = LocalOwnerProfile.PersistentOwnerId, Connected = room.Joined && !room.Reconnecting && !room.Leaving && !room.Closed });
            var members = StarSystem.State?.Members ?? Array.Empty<SystemMember>();
            foreach (var member in members)
            {
                if (member.PeerId == room.LocalPlayerId || member.Planet == null) continue;
                claims.Add(new RegisteredOwnerClaim { PeerId = member.PeerId, OwnerId = member.Planet.OwnerId,
                    Connected = member.Connected && players.Any(p => p.Id == member.PeerId && p.Connected) });
            }
            // These are admitted cooperative save claims, not verified Steam account bindings.
            registeredOwners.Update(claims.ToArray());
        }
        private bool IsRegisteredOwner(string peer, string expectedOwner)
        {
            RefreshRegisteredOwners();
            string actual;
            return registeredOwners.TryResolveActor(peer, out actual) && actual == expectedOwner;
        }
        private bool CanControlNativeDuplicant(string actor, int nativeId)
        {
            WorkPlanet planet; DupeOriginRecord origin;
            var colony = OnlineColonyState.Current;
            if (colony == null || !nativeWorkContext.TryGetLocalPlanet(out planet) ||
                !colony.TryResolveDuplicantOrigin(planet, nativeId, out origin)) return false;
            RefreshRegisteredOwners();
            string actorOwner;
            if (!registeredOwners.TryResolveActor(actor, out actorOwner)) return false;
            bool currentEntityProjection = room.IsHost && Teams.RegisterVerifiedDuplicant(nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                origin.StableDupeId, origin.OriginalOwnerId, registeredOwners.ProjectionPeer(origin.OriginalOwnerId));
            string issuer = null;
            bool direct = (GetDirectWorkPermissions(actor) & RemoteWorkPermission.CommandDupes) != 0 && directGrantIssuers.TryGetValue(actor, out issuer);
            if (!direct) issuer = null;
            return DupeOwnershipPolicy.CanControl(origin, actorOwner, issuer, direct,
                currentEntityProjection && Teams.CanControl(actor, nativeId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        public PlanetManifest PlanetForOwner(string ownerId)
        { return StarSystem.State?.Members.FirstOrDefault(m => m.PeerId == ownerId)?.Planet.Copy(); }
        internal void SetWorkPermissions(string actor, RemoteWorkPermission permissions)
        {
            if (!PreparePermissionScope()) throw new InvalidOperationException("work_world_unavailable");
            directPermissions[actor] = permissions;
            directGrantIssuers[actor] = LocalOwnerProfile.PersistentOwnerId;
            ApplyPermissions(actor);
        }
#if ONIONLINE_RUNTIME_PROBE
        // The probe must supply its own disposable-world and exact-cell guards.
        // Normal builds never expose this fixture-executor replacement hook.
        internal void SetProbeWorkExecutor(IWorkExecutor executor)
        {
            if (executor == null || !Work.Authority.Synchronize() || Work.Authority.ExportConfirmedPlans().Length != 0)
                throw new InvalidOperationException("probe_work_requires_available_empty_authority");
            Work.Changed -= Notify;
            Work.Dispose();
            Work = new RemoteWorkCoordinator(room, nativeWorkContext, executor,
                planetId => StarSystem.State?.Members.FirstOrDefault(m => m.Planet?.PlanetId == planetId)?.PeerId);
            Work.Changed += Notify;
            permissionEpoch = null; directPermissions.Clear(); directGrantIssuers.Clear(); teamPermissions.Clear();
            if (!Work.Authority.Synchronize()) throw new InvalidOperationException("probe_work_world_unavailable");
        }
#endif
        internal RemoteWorkPermission GetDirectWorkPermissions(string actor)
        {
            RemoteWorkPermission permissions;
            return actor != null && PreparePermissionScope() && directPermissions.TryGetValue(actor, out permissions)
                ? permissions : RemoteWorkPermission.None;
        }
        private bool PreparePermissionScope()
        {
            if (!Work.Authority.Synchronize()) return false;
            string current = Work.Authority.Planet.AuthorityEpoch;
            if (permissionEpoch != current)
            { permissionEpoch = current; directPermissions.Clear(); directGrantIssuers.Clear(); teamPermissions.Clear(); }
            return true;
        }
        private void ApplyPermissions(string actor)
        {
            if (!PreparePermissionScope()) return;
            RemoteWorkPermission direct, team;
            directPermissions.TryGetValue(actor, out direct); teamPermissions.TryGetValue(actor, out team);
            Work.SetPermissions(actor, direct | team);
            Interest?.RevalidateObservers();
        }
        public void Show(string ownerId = null)
        {
            if (window != null) window.Activate();
            else window = WorldOverviewWindow.Create(this);
            if (ownerId != null) window.SelectWorld(ownerId);
        }
        public void CloseWorldView()
        {
            if (window != null) window.DestroyWindow();
            window = null;
            replication.StopObserving();
            Interest.Stop();
            CellDetails.Stop();
        }
        public void SetSharing(bool value)
        {
            replication.SetSharing(value);
            Interest.RevalidateObservers();
        }
        public void RequestWorld(string ownerId) { replication.RequestWorld(ownerId); }
        internal void StopLegacyObservation() { replication.StopObserving(); }
        private void Notify() { Revision++; if (Changed != null) Changed(); }
        public void Dispose()
        {
            CloseWorldView();
            replication.Changed -= Notify;
            replication.Dispose();
            DupeCommands.Dispose();
            Interest.Changed -= Notify;
            Interest.Dispose();
            CellDetails.Changed -= Notify;
            CellDetails.Dispose();
            interestSources.Clear();
            Teams.Changed -= Notify;
            Teams.Dispose();
            Work.Changed -= Notify;
            Work.Dispose();
            StarSystem.Changed -= Notify;
            StarSystem.Dispose();
        }
    }
}
