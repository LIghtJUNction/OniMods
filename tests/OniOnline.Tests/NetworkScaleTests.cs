using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;

// These are protocol peers running production session/coordinator code. They are
// intentionally not described as native ONI clients or entity-migration tests.
internal static class NetworkScaleTests
{
    private static Action<bool, string> check;

    private sealed class Source : IPlanetSource
    {
        internal bool Ready = true;
        internal readonly PlanetManifest Planet = new PlanetManifest
        {
            PlanetId = Guid.NewGuid().ToString("N"), LineageId = Guid.NewGuid().ToString("N"),
            OwnerId = Guid.NewGuid().ToString("N"), HomeSystemId = Guid.NewGuid().ToString("N"),
            Name = "Saved native planet", Width = 128, Height = 256
        };
        public PlanetManifest CapturePlanet() { return Ready ? Planet.Copy() : null; }
    }

    private sealed class Peer : IDisposable
    {
        internal readonly Source Source = new Source();
        internal readonly RoomSession Session;
        internal readonly StarSystemCoordinator System;
        internal readonly IRoomTransport InitialTransport;
        internal Peer(IRoomTransport transport, Func<IRoomTransport> reconnect, SessionOptions options = null)
        {
            InitialTransport = transport;
            Session = new RoomSession(transport, new SessionProfile
            { Name = "Protocol peer", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true },
                "", "Network matrix system", reconnect, options ?? new SessionOptions { RetryJitter = () => 0.5 });
            System = new StarSystemCoordinator(Session, Source);
        }
        internal void Pump(double now) { Session.Pump(now); System.Pump(now); }
        public void Dispose() { System.Dispose(); Session.Dispose(); }
    }

    // Cut one real TCP connection precisely after the host has applied Leave,
    // before its acknowledgement can enter that connection's write queue.
    private sealed class AckCutTransport : IRoomTransport
    {
        private readonly IRoomTransport inner;
        internal bool CutNextAcknowledgement;
        internal int Cuts;
        internal AckCutTransport(IRoomTransport inner) { this.inner = inner; }
        public bool IsHost { get { return inner.IsHost; } }
        public string HostPeer { get { return inner.HostPeer; } }
        public bool TryReceive(out TransportEvent item) { return inner.TryReceive(out item); }
        public bool Send(string peer, byte[] data)
        {
            if (CutNextAcknowledgement && Protocol.Decode(data).Kind == PacketKind.LeaveAck)
            { CutNextAcknowledgement = false; Cuts++; inner.Disconnect(peer); return false; }
            return inner.Send(peer, data);
        }
        public void Disconnect(string peer) { inner.Disconnect(peer); }
        public void Poll() { inner.Poll(); }
        public void Dispose() { inner.Dispose(); }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network;
        internal readonly List<Peer> Peers = new List<Peer>();
        internal Peer Host { get { return Peers[0]; } }
        internal Room(int seed)
        {
            Network = new DeterministicRoomNetwork(seed);
            Peers.Add(new Peer(Network.Host, null,
                new SessionOptions { SeatReservationSeconds = 1200, RetryJitter = () => 0.5 }));
        }
        internal Peer Join()
        { var peer = new Peer(Network.Connect(), Network.Connect); Peers.Add(peer); return peer; }
        internal void Step(double seconds = 0.1)
        { Network.Advance(seconds); foreach (var peer in Peers) peer.Pump(Network.Now); }
        internal void Advance(double seconds)
        { double end = Network.Now + seconds; while (Network.Now < end) Step(); }
        internal void Until(Func<bool> condition, string description, double seconds = 90)
        {
            double end = Network.Now + seconds;
            while (Network.Now < end)
            {
                Step();
                if (condition()) { check(true, description); return; }
            }
            throw new Exception("Network matrix timeout: " + description + "; " +
                string.Join(";", Peers.Select(p => p.Session.LastError + "/" + p.System.LastError)));
        }
        internal void Reserve(Peer peer, int q, int r)
        {
            check(peer.System.ChooseLanding(q, r), "map accepts a landing within the system");
            Until(() => peer.System.State != null && peer.System.State.Members.Any(m =>
                m.PeerId == peer.Session.LocalPlayerId && m.Planet == null && m.Q == q && m.R == r),
                "reservation acknowledgement matches player and chosen coordinates");
        }
        internal void Admit(Peer peer, int q, int r)
        {
            Reserve(peer, q, r);
            check(peer.System.ConfirmTravel(), "owner explicitly confirms its selected saved planet");
            Until(() => peer.System.Admitted, "selected planet becomes admitted");
        }
        public void Dispose() { foreach (var peer in Peers) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        PagedPublicationIsAtomic();
        LargeRoomLosesTailPages();
        FaultMatrix();
        ReservationExpiresDuringCreation();
        CreationSurvivesReconnect();
        DepartureAcknowledgementLoss();
        DepartureSocketDiesBeforeAcknowledgement();
        DepartureDuringOneWayFailure();
        RealTcpReconnectIsIsolated();
        Console.WriteLine("Network matrix: production protocol peers; 40-player paging, deterministic latency/loss/reordering/duplication/one-way failure, reservation expiry, leave acknowledgements, and 3 simultaneous real TCP clients through independent proxies. No full ONI clients or native migration claimed.");
    }

    private static void PagedPublicationIsAtomic()
    {
        var pages = new PagedCollection<int>(); int[] complete;
        var first = Enumerable.Range(0, 12).ToArray();
        var tail = Enumerable.Range(12, 12).ToArray();
        check(!pages.Accept(10, 24, 12, tail, 512, out complete), "tail page alone cannot publish partial membership");
        check(!pages.Accept(10, 24, 12, tail, 512, out complete), "duplicate tail does not count members twice");
        check(pages.Accept(10, 24, 0, first, 512, out complete) && complete.SequenceEqual(Enumerable.Range(0, 24)),
            "reversed pages publish one ordered complete revision");
        check(!pages.Accept(12, 25, 24, new[] { 24 }, 128, out complete), "newer incomplete revision stays unpublished");
        check(!pages.Accept(11, 24, 0, first, 512, out complete), "late previous revision cannot contaminate a newer assembly");
        check(!pages.Accept(12, 25, 0, first, 512, out complete), "missing middle page keeps the newer revision incomplete");
        check(pages.Accept(12, 25, 12, tail, 512, out complete) && complete.SequenceEqual(Enumerable.Range(0, 25)),
            "recovery of missing page completes the exact newer collection");
        bool rejected = false;
        try { pages.Accept(13, 24, 0, first, 512, out complete); pages.Accept(13, 25, 12, tail, 512, out complete); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "inconsistent total within the same revision is rejected");
    }

    private static void LargeRoomLosesTailPages()
    {
        using (var room = new Room(4012))
        {
            for (int i = 1; i < 40; i++)
            {
                var peer = room.Join();
                room.Until(() => peer.Session.Joined && peer.System.State != null, "scale peer previews the host system");
                room.Admit(peer, i % 8 - 4, i / 8 + 1);
            }
            room.Until(() => room.Peers.All(p => p.Session.Players.Length == 40 && p.System.State.Members.Length == 40),
                "all forty protocol peers receive full roster and membership");
            var leaving = room.Peers[39];
            var watching = room.Peers[1];
            bool loseTail = true; int rosterTail = 0, systemTail = 0;
            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
            {
                if (!fromHost || !loseTail) return false;
                var packet = Protocol.Decode(bytes);
                if (packet.Kind == PacketKind.RosterPart && packet.RosterOffset >= Protocol.RosterPageSize)
                { rosterTail++; return true; }
                if (packet.Kind == PacketKind.Application && SystemWire.IsMessage(packet.Payload))
                {
                    var message = SystemWire.Decode(packet.Payload);
                    if (message.Kind == SystemMessageKind.State && message.Offset >= SystemWire.PageSize)
                    { systemTail++; return true; }
                }
                return false;
            };
            check(leaving.Session.RequestLeave(), "fortieth player requests explicit departure");
            room.Until(() => leaving.Session.Closed && room.Host.System.State.Members.Length == 39,
                "departure changes authoritative membership beyond both page boundaries");
            room.Advance(12);
            check(rosterTail > 0 && systemTail > 0, "both roster and system tail pages were actually dropped");
            check(watching.Session.Players.Length == 40 && watching.System.State.Members.Length == 40,
                "observers retain their complete old view while newer tail pages are missing");
            loseTail = false;
            room.Until(() => room.Peers.Where(p => !p.Session.Closed).All(p =>
                p.Session.Players.Length == 39 && p.System.State.Members.Length == 39),
                "periodic metadata replay repairs lost pages for every surviving peer");
            check(room.Host.Session.Players.Select(p => p.Number).Distinct().Count() == 39,
                "paged scale preserves unique player numbers after departure");
        }
    }

    private static void FaultMatrix()
    {
        for (int profile = 0; profile < 3; profile++)
        {
            using (var room = new Room(91200 + profile))
            {
                var a = room.Join(); var b = room.Join();
                room.Until(() => a.System.State != null && b.System.State != null, "fault-matrix preview handshake completes");
                room.Network.Settings.Delay = profile == 2 ? 1.5 : 0.15;
                room.Network.Settings.Jitter = profile == 2 ? 0.5 : 0.08;
                room.Network.Settings.Drop = profile == 0 ? 0 : 0.2;
                room.Network.Settings.Duplicate = profile == 1 ? 0.4 : 0;
                room.Network.Settings.Reorder = profile == 1 ? 0.4 : 0;
                room.Network.Settings.ReliableOrdered = profile == 2;
                room.Admit(a, 1, -1); room.Admit(b, 2, -1);
                string message = "matrix-" + profile;
                check(a.Session.SendChat(message), "chat is accepted alongside planet metadata traffic");
                room.Until(() => room.Peers.All(p => p.Session.Chat.Count(c => c.Text == message) == 1),
                    "faulted transport delivers chat exactly once to all peers");
                room.Advance(15);
                check(room.Peers.All(p => p.System.State.Members.Length == 3), "faulted admission retries never duplicate a planet");
                check(room.Host.System.State.Members.Select(m => m.Planet.PlanetId).Distinct().Count() == 3,
                    "planet identity stays unique under retries and duplicates");
                if (profile == 1) check(room.Network.Dropped > 0 && room.Network.Reordered > 0 && room.Network.Duplicated > 0,
                    "raw-frame profile actually exercises loss, reordering and duplication");
                if (profile == 2) check(room.Network.Retransmissions > 0 && a.Session.SmoothedRttMilliseconds > 1500,
                    "ordered retransmission profile actually exercises high measured latency");
            }
        }
    }

    private static void ReservationExpiresDuringCreation()
    {
        using (var room = new Room(60010))
        {
            var a = room.Join(); a.Source.Ready = false;
            var b = room.Join();
            room.Until(() => a.System.State != null && b.System.State != null, "two visitors preview for reservation expiry");
            room.Reserve(a, 1, 0); check(a.System.ConfirmTravel(), "creation flow confirms its reserved landing");
            string id = a.Session.LocalPlayerId;
            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
            {
                if (fromHost) return false;
                var packet = Protocol.Decode(bytes);
                return packet.Kind == PacketKind.Application && packet.PlayerId == id && SystemWire.IsMessage(packet.Payload) &&
                    SystemWire.Decode(packet.Payload).Kind == SystemMessageKind.Reserve;
            };
            room.Advance(605);
            check(a.Session.Joined && !room.Host.System.State.Members.Any(m => m.PeerId == id),
                "unrenewed reservation expires while communication and colony creation remain alive");
            room.Admit(b, 1, 0);
            room.Network.Settings.DropBeforeDelivery = null; a.Source.Ready = true;
            room.Until(() => a.System.LastError == "planet_position_taken", "late colony completion cannot steal a reassigned landing");
            check(!a.System.Admitted && room.Host.System.State.Members.Single(m => m.Q == 1 && m.R == 0).PeerId == b.Session.LocalPlayerId,
                "expiry and replacement retain exactly one landing owner");
            room.Reserve(a, 2, 0); check(a.System.ConfirmTravel(), "owner can select and confirm a different landing after expiry");
            room.Until(() => a.System.Admitted, "late-created local colony arrives after explicit reselection");
        }
    }

    private static void CreationSurvivesReconnect()
    {
        using (var room = new Room(60200))
        {
            var peer = room.Join(); peer.Source.Ready = false;
            room.Until(() => peer.System.State != null, "no-save visitor previews before creating a colony");
            room.Reserve(peer, 1, 0); check(peer.System.ConfirmTravel(), "native initialization is requested only after confirmation");
            string id = peer.Session.LocalPlayerId; string planet = peer.Source.Planet.PlanetId;
            peer.InitialTransport.Disconnect("host"); room.Step();
            check(peer.Session.Reconnecting && !peer.System.Admitted, "disconnect during colony creation does not activate an absent planet");
            room.Until(() => peer.Session.Joined && !peer.Session.Reconnecting, "creation reconnect recovers the same communication session");
            peer.Source.Ready = true;
            room.Until(() => peer.System.Admitted, "colony becoming available after reconnect completes the reservation");
            check(peer.Session.LocalPlayerId == id && room.Host.System.State.Members.Single(m => m.PeerId == id).Planet.PlanetId == planet,
                "creation reconnect preserves owner and selected planet identity");
        }
    }

    private static void DepartureAcknowledgementLoss()
    {
        foreach (bool allAcksLost in new[] { false, true })
        {
            using (var room = new Room(allAcksLost ? 71002 : 71001))
            {
                var peer = room.Join(); room.Until(() => peer.System.State != null, "departure peer previews"); room.Admit(peer, 1, 0);
                int acknowledgements = 0;
                room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    if (!fromHost || Protocol.Decode(bytes).Kind != PacketKind.LeaveAck) return false;
                    acknowledgements++; return allAcksLost || acknowledgements == 1;
                };
                check(peer.Session.RequestLeave() && peer.Session.RequestLeave(), "repeated leave action is idempotent");
                room.Until(() => peer.Session.Closed && room.Host.System.State.Members.Length == 1,
                    "leave terminates and releases the planet even if acknowledgements are lost", 40);
                check(acknowledgements >= 2, "lost leave acknowledgement triggers a real retry");
                check(peer.Session.LastError == (allAcksLost ? "leave_unconfirmed" : "left_room"),
                    "departure distinguishes confirmed receipt from bounded uncertainty");
                long revision = room.Host.System.State.Revision;
                room.Advance(40);
                check(peer.Session.Closed && !peer.Session.Reconnecting && room.Host.System.State.Revision == revision,
                    "departed peer never reconnects indefinitely or releases membership twice");
            }
        }
    }

    private static void DepartureDuringOneWayFailure()
    {
        using (var room = new Room(72001))
        {
            var peer = room.Join(); room.Until(() => peer.System.State != null, "one-way-failure peer previews"); room.Admit(peer, 1, 0);
            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) => !fromHost;
            check(peer.Session.RequestLeave(), "owner can request return during an upstream blackhole");
            room.Until(() => peer.Session.Closed, "one-way loss cannot keep return-home UI pending forever", 40);
            check(peer.Session.LastError == "leave_unconfirmed" && !peer.Session.Reconnecting,
                "unreceived departure ends with explicit uncertainty rather than fabricated success");
            check(room.Host.System.State.Members.Any(m => m.PeerId == peer.Session.LocalPlayerId),
                "host retains unconfirmed disconnected ownership instead of claiming the planet vanished");
            room.Network.Settings.DropBeforeDelivery = null;
            room.Advance(40);
            check(peer.Session.Closed && !peer.Session.Reconnecting, "restoring the network does not revive an abandoned leave attempt");
        }
    }

    private static void DepartureSocketDiesBeforeAcknowledgement()
    {
        using (var room = new Room(71501))
        {
            var peer = room.Join(); room.Until(() => peer.System.State != null, "interrupted departure peer previews"); room.Admit(peer, 1, 0);
            bool interrupted = false;
            room.Network.Settings.CutBeforeDelivery = (fromHost, bytes) =>
            {
                if (!fromHost || interrupted || Protocol.Decode(bytes).Kind != PacketKind.LeaveAck) return false;
                interrupted = true; return true;
            };
            check(peer.Session.RequestLeave(), "owner requests return before a failed acknowledgement socket");
            room.Until(() => peer.Session.Closed && room.Host.System.State.Members.Length == 1,
                "socket loss after host release still terminates the departure", 40);
            check(interrupted && peer.Session.LastError != "left_room", "missing acknowledgement cannot fabricate a confirmed return");
            room.Advance(40);
            check(!peer.Session.Reconnecting && room.Host.System.State.Members.Length == 1 && room.Host.Session.Players.Length == 1,
                "retrying a released identity cannot recreate an unowned ghost planet or seat");
        }
    }

    private static void RealTcpReconnectIsIsolated()
    {
        var hostTransport = TcpRoomTransport.Host(IPAddress.Loopback, 0);
        var ackCut = new AckCutTransport(hostTransport);
        using (var host = new Peer(ackCut, null))
        {
            var proxies = new List<TcpFaultProxy>(); var clients = new List<Peer>();
            var watch = Stopwatch.StartNew();
            Action tick = () => { host.Pump(watch.Elapsed.TotalSeconds); foreach (var peer in clients) peer.Pump(watch.Elapsed.TotalSeconds); Thread.Sleep(10); };
            Action<Func<bool>, string> until = (condition, description) =>
            {
                double end = watch.Elapsed.TotalSeconds + 30;
                while (watch.Elapsed.TotalSeconds < end) { tick(); if (condition()) { check(true, description); return; } }
                throw new Exception("TCP network matrix timeout: " + description);
            };
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var proxy = new TcpFaultProxy(hostTransport.Port, 82000 + i)
                    { DelayMilliseconds = 25 + i * 20, JitterMilliseconds = 15, BytesPerSecond = 8192 };
                    proxies.Add(proxy);
                    clients.Add(new Peer(TcpRoomTransport.Join("127.0.0.1", proxy.Port), null));
                }
                until(() => clients.All(p => p.Session.Joined && p.System.State != null), "three real TCP clients are simultaneously joined");
                for (int i = 0; i < clients.Count; i++)
                {
                    var peer = clients[i]; int q = i + 1;
                    check(peer.System.ChooseLanding(q, 0), "TCP owner selects its own map position");
                    until(() => peer.System.State.Members.Any(m => m.PeerId == peer.Session.LocalPlayerId && m.Planet == null),
                        "TCP reservation crosses the real framed connection");
                    check(peer.System.ConfirmTravel(), "TCP owner confirms its local source");
                }
                until(() => clients.All(p => p.System.Admitted && p.Session.Players.Length == 4), "four production sessions simultaneously see admitted peers");
                string id = clients[0].Session.LocalPlayerId;
                proxies[0].DisconnectAll();
                until(() => clients[0].Session.Reconnecting, "only the disrupted TCP client enters reconnect");
                check(clients.Skip(1).All(p => p.Session.Joined && !p.Session.Reconnecting), "single-client outage leaves other TCP sessions connected");
                until(() => clients[0].System.Admitted, "TCP reconnect restores the selected local planet");
                check(clients[0].Session.LocalPlayerId == id && proxies[0].AcceptedConnections >= 2,
                    "TCP reconnect creates a new socket while preserving player identity");
                check(clients[1].Session.SendChat("unaffected-client"), "unaffected TCP peer can still communicate");
                until(() => clients.All(p => p.Session.Chat.Any(c => c.Text == "unaffected-client")),
                    "all concurrent TCP clients receive chat after the isolated reconnect");
                ackCut.CutNextAcknowledgement = true;
                check(clients[0].Session.RequestLeave(), "TCP peer requests departure before an acknowledgement socket cut");
                until(() => clients[0].Session.Closed && host.System.State.Members.Length == 3,
                    "real TCP socket cut after host release ends the leave attempt");
                check(ackCut.Cuts == 1 && clients[0].Session.LastError != "left_room" && !clients[0].Session.Reconnecting,
                    "interrupted real TCP acknowledgement is terminal without falsely confirmed success");
                check(clients.Skip(1).All(p => p.Session.Joined && !p.Session.Reconnecting),
                    "interrupted TCP departure does not disconnect other players");
                foreach (var peer in clients.Skip(1)) check(peer.Session.RequestLeave(), "TCP peer requests acknowledged shutdown");
                until(() => clients.All(p => p.Session.Closed) && host.System.State.Members.Length == 1,
                    "all real TCP peer departures release their memberships");
                until(() => proxies.All(p => p.ActiveConnections == 0), "TCP proxy connection resources are reclaimed");
                check(proxies.All(p => p.ForwardedBytes > 0), "each independent fault proxy forwarded actual TCP bytes");
            }
            finally { foreach (var peer in clients) peer.Dispose(); foreach (var proxy in proxies) proxy.Dispose(); }
        }
    }
}
