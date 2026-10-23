using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class ObservationAdmissionTests
{
    private sealed class Peer : IDisposable
    {
        internal RoomSession Session;
        internal WorldInterestSession Interest;
        internal WorldCellDetailsSession Details;
        internal WorldReplication Worlds;
        internal readonly HashSet<string> Blocked = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, long> Generations = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly List<WorldInterestSessionMessage> InterestMessages = new List<WorldInterestSessionMessage>();
        internal readonly List<WorldCellDetailsMessage> DetailMessages = new List<WorldCellDetailsMessage>();
        internal readonly string Planet = Guid.NewGuid().ToString("N"), Epoch = Guid.NewGuid().ToString("N");
        internal int InterestCaptures, DetailCaptures, WorldCaptures, Value = 0;
        internal bool Throw;
        internal bool Allows(string peer) { if (Throw) throw new InvalidOperationException("fixture_policy_failed"); return !Blocked.Contains(peer); }
        internal long Generation(string peer) { return Generations.TryGetValue(peer, out var generation) ? generation : 1; }
        public void Dispose() { Interest.Dispose(); Details.Dispose(); Worlds.Dispose(); Session.Dispose(); }
    }
    private sealed class Source : IWorldSource
    {
        private readonly Peer peer;
        internal Source(Peer peer) { this.peer = peer; }
        public bool IsAvailable { get { return true; } }
        public WorldSnapshot Capture(string owner, long sequence, bool shared, bool includeCells = true)
        {
            peer.WorldCaptures++;
            return new WorldSnapshot { World = new WorldDescriptor { Id = owner + ":0", OwnerId = owner, Epoch = peer.Epoch,
                Name = "Admission", Width = 8, Height = 8, NativeWorldId = 0, Shared = shared, ControlRevision = 1 },
                Sequence = sequence, Cells = new WorldCell[0], Dupes = new WorldDupe[0] };
        }
    }
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(19317);
        internal readonly Peer[] Peers = new Peer[3];
        internal Room()
        {
            for (int index = 0; index < Peers.Length; index++)
            {
                var peer = new Peer(); Peers[index] = peer;
                peer.Session = new RoomSession(index == 0 ? Network.Host : Network.Connect(), new SessionProfile
                    { Name = "Admission" + index, GameVersion = "744825", SpacedOut = true }, "", "Admission");
            }
            Until(() => Peers.All(peer => peer.Session.Joined && peer.Session.Players.Length == Peers.Length), "authentication", false);
            foreach (var peer in Peers)
            {
                peer.Interest = new WorldInterestSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    planet => Find(planet) == null ? null : new PlanetManifest { PlanetId = planet, Width = 8, Height = 8 },
                    (observer, planet, subscription) => {
                        var source = Find(planet); source.InterestCaptures++;
                        return new WorldInterestFrame { Epoch = source.Epoch, Width = 8, Height = 8, Tick = (long)(Network.Now * 20),
                            Cells = new List<WorldInterestCell> { Cell(source, 0) }, Entities = new WorldInterestEntity[0] };
                    }, (observer, planet) => true, peer.Allows, peer.Generation);
                peer.Details = new WorldCellDetailsSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    (observer, planet, index) => { var source = Find(planet); source.DetailCaptures++;
                        return new WorldCellDetailResult { Epoch = source.Epoch, Width = 8, Height = 8, Cell = Cell(source, index) }; },
                    (observer, planet) => true, peer.Allows, peer.Generation);
                peer.Worlds = new WorldReplication(peer.Session, new Source(peer), canUsePeer: peer.Allows, admissionGeneration: peer.Generation);
                peer.Session.ApplicationMessageReceived += (sender, bytes) => {
                    WorldInterestSessionMessage interest;
                    if (WorldInterestSessionWire.TryDecode(bytes, out interest)) peer.InterestMessages.Add(interest);
                    WorldCellDetailsMessage detail;
                    if (WorldCellDetailsWire.TryDecode(bytes, out detail)) peer.DetailMessages.Add(detail);
                };
            }
        }
        internal Peer Find(string planet) { return Peers.FirstOrDefault(peer => peer.Planet == planet); }
        private static WorldInterestCell Cell(Peer peer, int index)
        { return new WorldInterestCell { Index = index, Explored = true, Fields = WorldInterestChannels.Visual | WorldInterestChannels.Mass |
            WorldInterestChannels.Temperature, ElementHash = 1, Color = 0xffffffff, Mass = 10 + peer.Value, Temperature = 300 + peer.Value }; }
        internal void Step(bool modules = true)
        {
            Network.Advance(.05);
            foreach (var peer in Peers) peer.Session.Pump(Network.Now);
            if (modules) foreach (var peer in Peers) { peer.Interest?.Pump(Network.Now); peer.Details?.Pump(Network.Now); peer.Worlds?.Pump(Network.Now); }
        }
        internal void Advance(double seconds, bool modules = true)
        { double until = Network.Now + seconds; while (Network.Now < until) Step(modules); }
        internal void Until(Func<bool> condition, string label, bool modules = true)
        {
            double until = Network.Now + 10;
            while (Network.Now < until) { Step(modules); if (condition()) return; }
            throw new Exception("Observation admission timeout: " + label);
        }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }
    private static WorldInterestSubscription Subscription()
    { return new WorldInterestSubscription { Revision = 1, Region = new WorldInterestRegion { Width = 1, Height = 1 }, Channels = WorldInterestChannels.Visual }; }
    internal static void Run(Action<bool, string> check)
    {
        LocalAndFailureGates(check); InterestRevocation(check); DetailsRevocation(check); RelayAndCatalogGates(check); GenerationReplacement(check);
    }
    private static void LocalAndFailureGates(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var observer = room.Peers[2]; var owner = room.Peers[1];
            observer.Blocked.Add(observer.Session.LocalPlayerId);
            check(!observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()), "unready local peer cannot start viewport subscription");
            check(!observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, owner.Epoch, 0), "unready local peer cannot start hover subscription");
            room.Advance(2);
            check(observer.WorldCaptures == 0 && owner.InterestCaptures == 0 && owner.DetailCaptures == 0,
                "unready local peer cannot sample or trigger native observations");
            observer.Blocked.Clear(); observer.Throw = true;
            room.Advance(2);
            check(observer.WorldCaptures == 0 && !observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()),
                "admission callback failure denies observations without breaking RoomSession");
            check(observer.Session.Joined && !observer.Session.Closed, "policy exception leaves authenticated basic connection alive");
        }
    }
    private static void InterestRevocation(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var observer = room.Peers[2]; var owner = room.Peers[1];
            check(observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()), "ready observer subscribes to guest owner");
            room.Until(() => !observer.Interest.Stale, "viewport baseline");
            var old = owner.InterestMessages.Last(message => message.Kind == WorldInterestSessionKind.Subscribe);
            owner.Blocked.Add(observer.Session.LocalPlayerId);
            room.Peers[0].Session.ForwardApplication(observer.Session.LocalPlayerId, owner.Session.LocalPlayerId,
                WorldInterestSessionWire.Encode(old)); room.Advance(.5, false);
            int captures = owner.InterestCaptures;
            check(owner.Interest.ActiveObservers == 0, "receive boundary revokes observer before next module Pump");
            room.Advance(3);
            check(owner.InterestCaptures == captures && owner.Interest.QueuedBytes == 0, "revoked viewport stops sampling and removes queued frames");
            owner.Blocked.Clear(); observer.Session.SendApplication(WorldInterestSessionWire.Encode(old)); room.Advance(1);
            check(owner.Interest.ActiveObservers == 0 && owner.InterestCaptures == captures, "old viewport nonce cannot revive after readmission");
            check(observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()), "fresh viewport subscription allowed after readmission");
            room.Until(() => owner.InterestCaptures > captures && !observer.Interest.Stale, "fresh viewport after readmission");
            check(owner.InterestMessages.Last(message => message.Kind == WorldInterestSessionKind.Subscribe).Nonce != old.Nonce,
                "readmitted viewport binds a fresh request nonce");
        }
    }
    private static void DetailsRevocation(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var observer = room.Peers[2]; var owner = room.Peers[1];
            check(observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, owner.Epoch, 0), "ready hover subscribes to guest owner");
            room.Until(() => !observer.Details.Stale, "hover baseline");
            var old = owner.DetailMessages.Last(message => message.Kind == WorldCellDetailsKind.Watch);
            owner.Blocked.Add(observer.Session.LocalPlayerId);
            room.Peers[0].Session.ForwardApplication(observer.Session.LocalPlayerId, owner.Session.LocalPlayerId,
                WorldCellDetailsWire.Encode(old)); room.Advance(.5, false);
            int captures = owner.DetailCaptures;
            check(owner.Details.ActiveObservers == 0, "hover receive boundary revokes before module Pump");
            room.Advance(2); owner.Blocked.Clear();
            observer.Session.SendApplication(WorldCellDetailsWire.Encode(old)); room.Advance(1);
            check(owner.DetailCaptures == captures && owner.Details.ActiveObservers == 0 && owner.Details.QueuedBytes == 0,
                "old hover request and queued values cannot resume after admission revocation");
            observer.Details.Stop();
            check(observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, owner.Epoch, 0), "fresh hover requires explicit Watch after readmission");
            room.Until(() => !observer.Details.Stale && owner.DetailCaptures > captures, "new hover after readmission");
        }
    }
    private static void RelayAndCatalogGates(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var host = room.Peers[0]; var owner = room.Peers[1]; var observer = room.Peers[2];
            room.Advance(2);
            check(observer.Worlds.Worlds.Any(world => world.Descriptor.OwnerId == owner.Session.LocalPlayerId), "ready world catalog crosses authenticated host relay");
            observer.Blocked.Add(owner.Session.LocalPlayerId); room.Advance(.1);
            var entry = observer.Worlds.Worlds.First(world => world.Descriptor.OwnerId == owner.Session.LocalPlayerId);
            check(!entry.Connected && entry.Snapshot == null, "catalog admission revoke marks owner unavailable and clears cached snapshot");
            host.Blocked.Add(observer.Session.LocalPlayerId);
            check(owner.Interest.Subscribe(host.Session.LocalPlayerId, host.Planet, Subscription()), "unrelated ready pair still operates");
            check(observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()) == false,
                "blocked remote authority cannot start observer-side subscription");
            observer.Blocked.Clear();
            check(observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()), "observer may queue fresh authorized request locally");
            room.Advance(3);
            check(owner.InterestMessages.All(message => message.Kind != WorldInterestSessionKind.Subscribe || message.TargetId != owner.Session.LocalPlayerId),
                "host relay rejects subscriptions from unready authenticated seat");
            check(owner.InterestCaptures == 0, "blocked relay never reaches guest owner source callback");
        }
    }
    private static void GenerationReplacement(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var host = room.Peers[0]; var owner = room.Peers[1]; var observer = room.Peers[2];
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, owner.Epoch, 0);
            room.Until(() => !observer.Interest.Stale && !observer.Details.Stale, "generation baseline");
            var oldInterest = owner.InterestMessages.Last(message => message.Kind == WorldInterestSessionKind.Subscribe);
            var oldDetail = owner.DetailMessages.Last(message => message.Kind == WorldCellDetailsKind.Watch);
            owner.Generations[observer.Session.LocalPlayerId] = 2;
            host.Session.ForwardApplication(observer.Session.LocalPlayerId, owner.Session.LocalPlayerId, WorldInterestSessionWire.Encode(oldInterest));
            host.Session.ForwardApplication(observer.Session.LocalPlayerId, owner.Session.LocalPlayerId, WorldCellDetailsWire.Encode(oldDetail));
            room.Advance(.5, false);
            check(owner.Interest.ActiveObservers == 0 && owner.Details.ActiveObservers == 0,
                "true-to-true admission replacement invalidates old observers at receive boundary");
            int interestReads = owner.InterestCaptures, detailReads = owner.DetailCaptures;
            room.Advance(2);
            check(owner.InterestCaptures == interestReads && owner.DetailCaptures == detailReads,
                "old subscription renewals cannot survive an admission generation change");
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            observer.Details.Stop(); observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, owner.Epoch, 0);
            room.Until(() => owner.InterestCaptures > interestReads && owner.DetailCaptures > detailReads,
                "fresh requests use replaced admission");
            room.Advance(1);
            string epoch = owner.Worlds.Worlds.First(world => world.IsLocal).Descriptor.Epoch;
            owner.Generations[owner.Session.LocalPlayerId] = 2;
            room.Advance(1.2);
            check(owner.Worlds.Worlds.First(world => world.IsLocal).Descriptor.Epoch != epoch,
                "local catalog replacement publishes a fresh observation epoch without false-ready gap");
        }
    }
}
