using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class WorldInterestSessionTests
{
    private sealed class Peer : IDisposable
    {
        internal RoomSession Session;
        internal IRoomTransport Transport;
        internal WorldInterestSession Interest;
        internal string Planet = Guid.NewGuid().ToString("N"), Epoch = Guid.NewGuid().ToString("N");
        internal int Captures, VisualCaptures, MotionOnlyCaptures, PermissionChecks, Value;
        internal bool MotionOnlyHadHover;
        internal bool PumpInterest = true;
        internal float X = .2f;
        internal List<WorldInterestSessionMessage> Binds = new List<WorldInterestSessionMessage>();
        internal List<WorldInterestSessionMessage> Subscriptions = new List<WorldInterestSessionMessage>();
        internal List<string> Removed = new List<string>();
        public void Dispose() { Interest.Dispose(); Session.Dispose(); }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(9731);
        internal readonly Peer[] Peers;
        internal readonly HashSet<string> Permissions = new HashSet<string>();
        internal Room(bool weak = false)
        {
            if (weak) { Network.Settings.Delay = 1.5; Network.Settings.Jitter = .5; Network.Settings.Drop = .2;
                Network.Settings.Reorder = .3; Network.Settings.ReliableOrdered = true; }
            Peers = new Peer[3];
            for (int i = 0; i < Peers.Length; i++)
            {
                var peer = new Peer(); Peers[i] = peer;
                peer.Transport = i == 0 ? Network.Host : Network.Connect();
                peer.Session = new RoomSession(peer.Transport, new SessionProfile
                { Name = "Interest" + i, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Interest",
                    reconnect: i == 0 ? null : () => peer.Transport = Network.Connect(), options: new SessionOptions { RetryJitter = () => .5 });
                peer.Interest = new WorldInterestSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    planet => Find(planet) == null ? null : new PlanetManifest { PlanetId = planet, Width = 8, Height = 8 },
                    (observer, planet, sub) => Capture(Find(planet), sub), (observer, planet) =>
                    { peer.PermissionChecks++; return Permissions.Contains(observer + ":" + planet); });
                peer.Interest.ObserverRemoved += id => peer.Removed.Add(id);
                peer.Session.ApplicationMessageReceived += (sender, data) =>
                {
                    WorldInterestSessionMessage message;
                    if (WorldInterestSessionWire.TryDecode(data, out message))
                    {
                        if (message.Kind == WorldInterestSessionKind.Bind) peer.Binds.Add(message);
                        if (message.Kind == WorldInterestSessionKind.Subscribe) peer.Subscriptions.Add(message);
                    }
                };
            }
            Until(() => Peers.All(p => p.Session.Joined) && Peers.All(p => p.Session.Players.Length == Peers.Length), 60, "session authentication");
        }
        internal Peer Find(string planet) { return Peers.FirstOrDefault(p => p != null && p.Planet == planet); }
        internal void Grant(int observer, int authority) { Permissions.Add(Peers[observer].Session.LocalPlayerId + ":" + Peers[authority].Planet); }
        private WorldInterestFrame Capture(Peer peer, WorldInterestSubscription sub)
        {
            peer.Captures++;
            if (sub.Channels == WorldInterestChannels.Entities)
            { peer.MotionOnlyCaptures++; peer.MotionOnlyHadHover |= sub.HoverIndex != -1; }
            else peer.VisualCaptures++;
            var cells = new List<WorldInterestCell>();
            for (int y = sub.Region.Y; y < sub.Region.Y + sub.Region.Height; y++)
                for (int x = sub.Region.X; x < sub.Region.X + sub.Region.Width; x++)
                {
                    int index = y * 8 + x; if (index == 9) continue;
                    var fields = sub.Channels & ~WorldInterestChannels.Entities;
                    if (index == sub.HoverIndex) fields |= WorldInterestChannels.Mass | WorldInterestChannels.Temperature;
                    cells.Add(new WorldInterestCell { Index = index, Explored = true, Fields = fields,
                        ElementHash = 1, Color = (uint)(0xffaa0000 + peer.Value), Temperature = 300 + peer.Value, Mass = 10 + peer.Value });
                }
            return new WorldInterestFrame { Epoch = peer.Epoch, Width = 8, Height = 8, Tick = (long)(Network.Now * 20), Cells = cells,
                Entities = (sub.Channels & WorldInterestChannels.Entities) != 0 && sub.Region.Contains(0, 8) ?
                    new[] { new WorldInterestEntity { Id = 42, CellIndex = 0, X = peer.X, Y = .2f, Kind = 1 } } : new WorldInterestEntity[0] };
        }
        internal void Step(double seconds = .05)
        {
            Network.Advance(seconds);
            foreach (var peer in Peers) peer.Session.Pump(Network.Now);
            foreach (var peer in Peers) if (peer.PumpInterest) peer.Interest.Pump(Network.Now);
        }
        internal void Advance(double seconds) { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition, double timeout, string message)
        {
            double until = Network.Now + timeout;
            while (Network.Now < until) { Step(); if (condition()) return; }
            throw new Exception("Interest session timeout " + message + "; " + string.Join(";", Peers.Select(p => p.Session.LastError + "/" +
                p.Interest.LastError + "/obs=" + p.Interest.ActiveObservers + "/visual=" + p.Interest.VisualReplica?.Cells.Count + "/stale=" + p.Interest.Stale)));
        }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }

    private static WorldInterestSubscription Subscription(long revision = 1)
    { return new WorldInterestSubscription { Revision = revision, Region = new WorldInterestRegion { Width = 4, Height = 4 } }; }
    private static bool Ready(Peer peer) { return !peer.Interest.Stale && peer.Interest.VisualReplica?.Cells.Count == 15 && peer.Interest.MotionReplica?.Entities.Count == 1; }

    internal static void Run(Action<bool, string> check)
    {
        PermissionsRoutingAndIdle(check);
        WeakNetworkAndReconnect(check);
        AuthorityOnlyReconnect(check);
        EpochNonceAndSenderGates(check);
        MotionWithoutVisualAck(check);
        CancelCannotBeResurrected(check);
        LaneSamplingCadence(check);
        WireBoundary(check);
    }

    private static void PermissionsRoutingAndIdle(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Advance(2); check(room.Peers.All(p => p.Captures == 0), "no observer means no source sampling");
            var observer = room.Peers[2]; var owner = room.Peers[1];
            check(observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription()), "guest can request other guest planet through host");
            room.Until(() => observer.Interest.LastError == "world_interest_denied", 5, "private denial");
            check(owner.Captures == 0 && observer.Interest.VisualReplica == null, "unauthorized request does not read or expose native data");
            room.Grant(2, 1);
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription(2));
            room.Until(() => Ready(observer), 8, "authorized guest relay");
            check(observer.Interest.VisualReplica.Cells[0].Temperature == 0 && observer.Interest.VisualReplica.Cells[0].Mass == 0 &&
                !observer.Interest.VisualReplica.Cells.ContainsKey(9), "real session visual payload omits numerical and unexplored data");
            var sub = Subscription(3); sub.HoverIndex = 0; sub.Channels |= WorldInterestChannels.Temperature;
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, sub);
            room.Until(() => observer.Interest.VisualReplica?.Cells.ContainsKey(0) == true && observer.Interest.VisualReplica.Cells[0].Mass == 10, 8, "hover detail");
            check(observer.Interest.VisualReplica.Cells[1].Temperature == 300 && observer.Interest.VisualReplica.Cells[1].Mass == 0,
                "temperature overlay and hover remain separate field requests over session");
            observer.Interest.Stop(); room.Advance(2);
            int captures = owner.Captures; room.Advance(3);
            check(owner.Captures == captures && owner.Interest.ActiveObservers == 0, "cancel stops native sampling and releases observer state");
            check(owner.Removed.Contains(observer.Session.LocalPlayerId), "observer removal callback releases matching native sampler cache");
            room.Grant(0, 1); room.Peers[0].Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(room.Peers[0]), 8, "host observes guest");
            check(room.Peers[0].Interest.MotionReplica.Entities[42].X == .2f, "host receives authenticated guest-owned movement");
        }
    }

    private static void WeakNetworkAndReconnect(Action<bool, string> check)
    {
        using (var room = new Room(true))
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(observer), 60, "1.5s delayed 20pct loss relay");
            string identity = observer.Session.LocalPlayerId; var oldReplica = observer.Interest.VisualReplica;
            room.Network.CutAll();
            room.Until(() => observer.Interest.Stale, 5, "reconnect marks retained cache stale");
            check(ReferenceEquals(oldReplica, observer.Interest.VisualReplica), "disconnect retains stale image until a valid new binding");
            owner.Value = 7;
            room.Until(() => Ready(observer) && observer.Interest.VisualReplica.Cells[0].Color == 0xffaa0007, 90, "reconnect resubscription obtains latest viewport");
            check(observer.Session.LocalPlayerId == identity && !ReferenceEquals(oldReplica, observer.Interest.VisualReplica), "resume keeps identity but fresh nonce rebuilds trusted interest cache");
            check(room.Peers.All(p => p.Interest.QueuedBytes <= WorldInterestSession.OutboxByteBudget &&
                p.Interest.EstimatedObserverBytes <= WorldInterestSession.ObserverMemoryBudget), "weak network obeys production outbox and observer memory budgets");
            observer.PumpInterest = false; room.Advance(35);
            int reads = owner.Captures; room.Advance(5);
            check(owner.Captures == reads && owner.Interest.ActiveObservers == 0, "dead subscription lease stops source reads while sessions stay connected");
            var beforeLeaseRecovery = observer.Interest.VisualReplica;
            var oldLeaseBind = observer.Binds.Last();
            observer.PumpInterest = true;
            room.Until(() => Ready(observer) && !ReferenceEquals(beforeLeaseRecovery, observer.Interest.VisualReplica), 60, "same nonce renews expired lease with fresh binding");
            var afterLeaseRecovery = observer.Interest.VisualReplica;
            owner.Session.SendApplication(WorldInterestSessionWire.Encode(oldLeaseBind)); room.Advance(12);
            check(ReferenceEquals(afterLeaseRecovery, observer.Interest.VisualReplica), "late pre-expiry Bind cannot undo newer same-epoch binding");
        }
    }

    private static void AuthorityOnlyReconnect(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(observer), 8, "owner-only restart baseline");
            var previous = observer.Interest.VisualReplica;
            owner.Transport.Disconnect("host");
            room.Until(() => owner.Session.Reconnecting, 5, "owner alone reconnects");
            check(!observer.Session.Reconnecting, "other observer remains continuously authenticated during owner reconnect");
            owner.Value = 19;
            room.Until(() => Ready(observer) && observer.Interest.VisualReplica.Cells[0].Color == 0xffaa0013, 20, "continuous observer rebinds resumed source authority");
            check(!ReferenceEquals(previous, observer.Interest.VisualReplica), "owner-only resume resets sequence baseline without replacing observer session");
        }
    }

    private static void EpochNonceAndSenderGates(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(observer), 8, "initial epoch");
            var oldBind = observer.Binds.Last(); string oldEpoch = owner.Epoch;
            owner.Epoch = Guid.NewGuid().ToString("N"); owner.Value = 5;
            room.Until(() => observer.Interest.VisualReplica?.Cells.ContainsKey(0) == true && observer.Interest.VisualReplica.Cells[0].Color == 0xffaa0005, 8, "authority epoch refresh");
            var current = observer.Interest.VisualReplica;
            owner.Session.SendApplication(WorldInterestSessionWire.Encode(oldBind)); room.Advance(2);
            check(ReferenceEquals(current, observer.Interest.VisualReplica) && observer.Interest.VisualReplica.Cells[0].Color == 0xffaa0005,
                "host rejects old owner SourceGeneration instead of reviving stale epoch");
            var newBind = observer.Binds.Last();
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription(2));
            room.Until(() => Ready(observer), 8, "new request nonce"); current = observer.Interest.VisualReplica;
            owner.Session.SendApplication(WorldInterestSessionWire.Encode(newBind)); room.Advance(1);
            check(ReferenceEquals(current, observer.Interest.VisualReplica), "old request nonce cannot reset current viewport");
            newBind.RoomId = Guid.NewGuid().ToString("N"); owner.Session.SendApplication(WorldInterestSessionWire.Encode(newBind)); room.Advance(1);
            check(ReferenceEquals(current, observer.Interest.VisualReplica), "old room token cannot mix observation state");
            // The observer cannot impersonate the authority merely by copying its authenticated-looking payload.
            newBind.RoomId = owner.Session.RoomId; newBind.TargetId = room.Peers[0].Session.LocalPlayerId;
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(newBind)); room.Advance(1);
            check(room.Peers[0].Interest.VisualReplica == null, "host derives authority from authenticated manifest and sender");
            room.Permissions.Clear(); room.Advance(2);
            check(observer.Interest.Stale && observer.Interest.LastError == "world_interest_denied" && owner.Interest.ActiveObservers == 0 &&
                observer.Interest.VisualReplica == null && observer.Interest.MotionReplica == null,
                "permission revocation stops publication and explicitly marks client stale");
        }
    }

    private static void MotionWithoutVisualAck(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
            {
                Packet packet;
                try { packet = Protocol.Decode(bytes); } catch { return false; }
                WorldInterestSessionMessage message;
                return packet.Kind == PacketKind.Application && WorldInterestSessionWire.TryDecode(packet.Payload, out message) &&
                    message.Kind == WorldInterestSessionKind.Ack && message.Sequence > 0;
            };
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(observer), 8, "initial view with all data ACKs dropped");
            owner.X = .8f;
            room.Until(() => observer.Interest.MotionReplica.Entities[42].X == .8f, 5, "latest motion despite dropped visual and motion ACK");
            check(observer.Interest.MotionReplica.Entities[42].X == .8f && owner.Interest.QueuedBytes <= WorldInterestSession.OutboxByteBudget,
                "movement fast lane keeps advancing while visual transfer waits for ACK");
        }
    }

    private static void WireBoundary(Action<bool, string> check)
    {
        var message = new WorldInterestSessionMessage { Kind = WorldInterestSessionKind.Subscribe, RoomId = new string('a', 32),
            TargetId = new string('b', 32), PlanetId = new string('c', 32), Nonce = new string('d', 32), RequestNumber = 1, Subscription = Subscription() };
        var bytes = WorldInterestSessionWire.Encode(message); WorldInterestSessionMessage decoded;
        check(bytes.Length <= 6144 && WorldInterestSessionWire.TryDecode(bytes, out decoded) && decoded.Subscription.Region.Width == 4,
            "interest session envelope production codec roundtrips");
        for (int i = 0; i < bytes.Length; i++)
        { var partial = new byte[i]; Array.Copy(bytes, partial, i); check(!WorldInterestSessionWire.TryDecode(partial, out decoded), "truncated envelope rejected"); }
        check(!WorldInterestSessionWire.TryDecode(new byte[6145], out decoded), "session envelope rejects oversized payload");
    }

    private static void CancelCannotBeResurrected(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, Subscription());
            room.Until(() => Ready(observer), 8, "cancel replay baseline");
            var original = owner.Subscriptions.Last();
            observer.Interest.Stop();
            room.Until(() => owner.Interest.ActiveObservers == 0, 3, "explicit cancellation delivery");
            int reads = owner.Captures;
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(original));
            room.Advance(3);
            check(owner.Interest.ActiveObservers == 0 && owner.Captures == reads,
                "authenticated duplicate Subscribe after Cancel cannot resurrect observer lease or native sampling");
            var cancelledBeforeArrival = new WorldInterestSessionMessage { Kind = WorldInterestSessionKind.Cancel,
                RoomId = original.RoomId, TargetId = original.TargetId, PlanetId = original.PlanetId,
                RequestNumber = original.RequestNumber + 100, Nonce = Guid.NewGuid().ToString("N") };
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(cancelledBeforeArrival));
            room.Advance(.5);
            var delayed = new WorldInterestSessionMessage { Kind = WorldInterestSessionKind.Subscribe,
                RoomId = original.RoomId, TargetId = original.TargetId, PlanetId = original.PlanetId,
                RequestNumber = cancelledBeforeArrival.RequestNumber, Nonce = cancelledBeforeArrival.Nonce, Subscription = Subscription(2) };
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(delayed)); room.Advance(3);
            check(owner.Interest.ActiveObservers == 0 && owner.Captures == reads,
                "Cancel arriving before its authenticated Subscribe records cancellation instead of allowing delayed allocation");
            delayed.RequestNumber++; delayed.Nonce = Guid.NewGuid().ToString("N");
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(delayed));
            room.Until(() => owner.Interest.ActiveObservers == 1, 3, "fresh subscription after cancelled request");
            check(owner.Interest.ActiveObservers == 1, "new higher request remains legal after explicit Cancel");
            observer.Session.SendApplication(WorldInterestSessionWire.Encode(cancelledBeforeArrival)); room.Advance(1);
            check(owner.Interest.ActiveObservers == 1, "old Cancel cannot remove the newer active subscription");
        }
    }

    private static void LaneSamplingCadence(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            var requested = Subscription(); requested.HoverIndex = 0;
            requested.Channels |= WorldInterestChannels.Temperature;
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, requested);
            room.Until(() => !observer.Interest.Stale && observer.Interest.VisualReplica?.Cells.Count == 15 &&
                observer.Interest.MotionReplica?.Entities.Count == 1, 8, "sampling cadence baseline bind");
            room.Advance(.2); // Initial source/Bind work is deliberately outside the measured one-second window.
            int captures = owner.Captures, visuals = owner.VisualCaptures, motions = owner.MotionOnlyCaptures, permissions = owner.PermissionChecks;
            for (int i = 0; i < 1000; i++) room.Step(.001);
            check(owner.Captures - captures <= 12, "1000Hz Pump samples native source at most 12 times per second after initial Bind");
            check(owner.VisualCaptures - visuals <= 3, "1000Hz Pump reads visual and numerical fields at most three times per second");
            check(owner.MotionOnlyCaptures - motions >= 7 && !owner.MotionOnlyHadHover,
                "intermediate movement samples request only Entities and never hover numerical fields");
            check(owner.PermissionChecks - permissions <= owner.Captures - captures + 1,
                "planet-wide authorization is checked once per sampled frame instead of once per visible cell or Pump");
            Console.WriteLine("Interest cadence 1000Hz/1s (initial Bind excluded): captures=" + (owner.Captures - captures) +
                ", visual=" + (owner.VisualCaptures - visuals) + ", motionOnly=" + (owner.MotionOnlyCaptures - motions) +
                ", planetPermissionChecks=" + (owner.PermissionChecks - permissions));
            requested = Subscription(2); requested.Channels = WorldInterestChannels.Visual;
            observer.Interest.Subscribe(owner.Session.LocalPlayerId, owner.Planet, requested);
            room.Until(() => !observer.Interest.Stale && observer.Interest.VisualReplica?.Cells.Count == 15, 8, "visual-only subscription bind");
            room.Advance(.2);
            captures = owner.Captures; motions = owner.MotionOnlyCaptures;
            for (int i = 0; i < 1000; i++) room.Step(.001);
            check(owner.Captures - captures <= 3 && owner.MotionOnlyCaptures == motions,
                "visual-only observers do not schedule an unused movement sampling cadence");
            Console.WriteLine("Interest visual-only cadence 1000Hz/1s: captures=" + (owner.Captures - captures) +
                ", motionOnly=" + (owner.MotionOnlyCaptures - motions));
        }
    }
}
