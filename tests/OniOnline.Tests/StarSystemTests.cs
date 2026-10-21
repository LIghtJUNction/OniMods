using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class StarSystemTests
{
    private static Action<bool, string> check;
    private sealed class Source : IPlanetSource
    {
        internal bool Ready = true;
        internal PlanetManifest Planet = new PlanetManifest { PlanetId = Guid.NewGuid().ToString("N"),
            LineageId = Guid.NewGuid().ToString("N"), OwnerId = Guid.NewGuid().ToString("N"), HomeSystemId = Guid.NewGuid().ToString("N"),
            Name = "My saved asteroid", NativeWorldId = 0, Width = 256, Height = 384 };
        public PlanetManifest CapturePlanet() { return Ready ? Planet.Copy() : null; }
    }
    private sealed class Peer : IDisposable
    {
        internal readonly Source Source = new Source();
        internal readonly RoomSession Session;
        internal readonly StarSystemCoordinator System;
        internal Peer(IRoomTransport transport, Func<IRoomTransport> reconnect)
        {
            Session = new RoomSession(transport, new SessionProfile { Name = "Player", GameVersion = "744825",
                CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Host named system", reconnect: reconnect,
                options: new SessionOptions { RetryJitter = () => 0.5 });
            System = new StarSystemCoordinator(Session, Source);
        }
        internal void Pump(double now) { Session.Pump(now); System.Pump(now); }
        public void Dispose() { System.Dispose(); Session.Dispose(); }
    }
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(62855);
        internal readonly List<Peer> Peers = new List<Peer>();
        internal Peer Host { get { return Peers[0]; } }
        internal Room() { Peers.Add(new Peer(Network.Host, null)); }
        internal Peer Join()
        { var peer = new Peer(Network.Connect(), Network.Connect); Peers.Add(peer); return peer; }
        internal void Step(double seconds = 0.05)
        { Network.Advance(seconds); foreach (var peer in Peers) peer.Pump(Network.Now); }
        internal void Until(Func<bool> condition, string description, double seconds = 60)
        {
            double until = Network.Now + seconds;
            while (Network.Now <= until)
            {
                Step(); if (condition()) { check(true, description); return; }
            }
            throw new Exception("Star-system timeout: " + description + "; " + string.Join(";", Peers.Select(p => p.Session.LastError + "/" + p.System.LastError)));
        }
        internal void Reserve(Peer peer, int q, int r)
        {
            check(peer.System.ChooseLanding(q, r), "visitor chooses a map position");
            Until(() => peer.System.State.Members.Any(m => m.PeerId == peer.Session.LocalPlayerId && m.Planet == null && m.Q == q && m.R == r),
                "host confirms a reservation before a planet is admitted");
        }
        internal void Arrive(Peer peer, int q, int r)
        { Reserve(peer, q, r); check(peer.System.ConfirmTravel(), "visitor confirms its native planet"); Until(() => peer.System.Admitted, "planet is admitted at reserved coordinates"); }
        public void Dispose() { foreach (var peer in Peers) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        ReservationAndInitialization();
        PositionRaceAndDuplicateBranch();
        LargeRosterAndPagedSystem();
        WeakNetworkAndResume();
        PagedMetadataValidation();
        Console.WriteLine("Star systems: production room/coordinator/wire; reservations, native-source availability boundary, paged metadata, 40 peers and weak-network resume. No native entity migration claimed.");
    }

    private static void ReservationAndInitialization()
    {
        using (var room = new Room())
        {
            var visitor = room.Join(); visitor.Source.Ready = false;
            room.Until(() => visitor.Session.Joined && visitor.System.State != null && room.Host.System.Admitted,
                "visitor can preview a host system without a colony");
            check(visitor.System.State.Members.Length == 1 && !visitor.System.Admitted, "preview does not automatically import a planet");
            string planet = visitor.Source.Planet.PlanetId;
            room.Reserve(visitor, 1, 0);
            check(visitor.System.ConfirmTravel(), "no-save visitor confirms normal colony initialization");
            for (int frame = 0; frame < 100; frame++) room.Step();
            check(!visitor.System.Admitted && room.Host.System.State.Members.Single(m => m.PeerId == visitor.Session.LocalPlayerId).Planet == null,
                "a reserved landing never invents a placeholder colony while generation is pending");
            visitor.Source.Ready = true;
            room.Until(() => visitor.System.Admitted, "newly available native colony completes the original reservation");
            var member = room.Host.System.State.Members.Single(m => m.PeerId == visitor.Session.LocalPlayerId);
            check(member.Planet.PlanetId == planet && member.Q == 1 && member.R == 0, "original planet identity arrives at chosen position");
            long revision = room.Host.System.State.Revision;
            for (int frame = 0; frame < 200; frame++) room.Step();
            check(room.Host.System.State.Revision == revision && room.Host.System.State.Members.Length == 2,
                "repeated admission requests do not duplicate a planet or advance semantic revision");
            check(visitor.Session.RequestLeave(), "visitor explicitly requests a confirmed return home");
            room.Until(() => room.Host.System.State.Members.Length == 1, "explicit departure releases its map position");
            check(visitor.Source.Planet.PlanetId == planet, "returning home does not regenerate or delete local planet identity");
        }
    }

    private static void PositionRaceAndDuplicateBranch()
    {
        using (var room = new Room())
        {
            var a = room.Join(); var b = room.Join();
            room.Until(() => a.System.State != null && b.System.State != null, "two preview visitors receive the system");
            check(a.System.ChooseLanding(1, 0) && b.System.ChooseLanding(1, 0), "two visitors race for the same landing");
            room.Until(() => b.System.LastError == "planet_position_taken", "host rejects the second competing reservation");
            check(!b.System.ConfirmTravel(), "rejected reservation cannot activate a planet");
            room.Until(() => a.System.State.Members.Any(m => m.PeerId == a.Session.LocalPlayerId), "first reservation remains visible");
            check(a.System.ConfirmTravel(), "first visitor confirms"); room.Until(() => a.System.Admitted, "first visitor arrives");
            b.Source.Planet = a.Source.Planet.Copy();
            room.Reserve(b, 2, 0); check(b.System.ConfirmTravel(), "second position is reserved before branch validation");
            room.Until(() => b.System.LastError == "planet_duplicate_branch", "duplicate active planet/lineage is denied");
            check(room.Host.System.State.Members.Count(m => m.Planet != null) == 2 && !b.System.Admitted, "rejected branch never becomes a second active planet");
            var old = a.Source.Planet;
            a.Source.Planet = new Source().Planet;
            room.Until(() => a.System.LastError == "planet_selection_locked", "admitted visitor cannot silently replace its planet");
            a.Source.Planet = old;
            var forged = room.Host.System.State.Copy(); forged.Name = "Forged host name";
            forged.Members = forged.Members.Take(SystemWire.PageSize).ToArray();
            b.Session.SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.State, State = forged }));
            for (int i = 0; i < 10; i++) room.Step();
            check(room.Host.System.State.Name == "Host named system", "guest-authored system state cannot rename the host system");
        }
    }

    private static void LargeRosterAndPagedSystem()
    {
        using (var room = new Room())
        {
            room.Until(() => room.Host.System.Admitted, "large system host publishes a native-source identity");
            for (int i = 1; i < 40; i++)
            {
                var visitor = room.Join();
                room.Until(() => visitor.Session.Joined && visitor.System.State != null, "larger-room preview joins");
                int q = i % 8 - 4, r = i / 8 + 1;
                room.Arrive(visitor, q, r);
            }
            room.Until(() => room.Peers.All(p => p.Session.Players.Length == 40 && p.System.State.Members.Length == 40),
                "forty peers converge on paged roster and paged system membership");
            check(room.Host.Session.Players.Select(p => p.Number).Distinct().Count() == 40,
                "player numbers are unique beyond both the old four-seat and eight-connection limits");
            check(room.Host.System.State.Members.Select(m => m.Q + ":" + m.R).Distinct().Count() == 40,
                "forty planet positions remain distinct");
            check(room.Host.System.State.Members.Select(m => m.Planet.PlanetId).Distinct().Count() == 40,
                "forty unique native-source planets are registered without a gameplay player cap");
        }
    }

    private static void WeakNetworkAndResume()
    {
        using (var room = new Room())
        {
            room.Network.Settings.Delay = 1.5; room.Network.Settings.Jitter = 0.4;
            room.Network.Settings.Drop = 0.2; room.Network.Settings.ReliableOrdered = true;
            var visitor = room.Join();
            room.Until(() => visitor.System.State != null, "high-latency visitor can preview");
            room.Arrive(visitor, 1, -1);
            string identity = visitor.Session.LocalPlayerId;
            int number = visitor.Session.Players.Single(p => p.Id == identity).Number;
            room.Network.CutAll(); room.Step();
            check(visitor.Session.Reconnecting && !visitor.System.Admitted, "disconnected visitor is not falsely shown as actively admitted");
            room.Until(() => visitor.Session.Joined && visitor.System.Admitted, "weak-network reconnect restores original planet membership");
            check(visitor.Session.LocalPlayerId == identity && visitor.Session.Players.Single(p => p.Id == identity).Number == number,
                "reconnect preserves account seat and player number");
            var actual = room.Host.System.State.Members.Single(m => m.PeerId == identity);
            check(actual.Planet.PlanetId == visitor.Source.Planet.PlanetId && actual.Q == 1 && actual.R == -1,
                "reconnect preserves chosen planet and position without a second admission");
            check(room.Network.Retransmissions > 0, "weak system fixture exercised packet retransmission");
        }
    }

    private static void PagedMetadataValidation()
    {
        var collection = new PagedCollection<int>(); int[] complete;
        check(!collection.Accept(1, int.MaxValue, 0, new[] { 1 }, 12, out complete) && complete == null,
            "remote claimed count cannot trigger a giant allocation");
        check(!collection.Accept(2, 3, 2, new[] { 3 }, 12, out complete), "out-of-order metadata page waits for complete revision");
        check(collection.Accept(2, 3, 0, new[] { 1, 2 }, 16, out complete) && complete.SequenceEqual(new[] { 1, 2, 3 }),
            "out-of-order metadata becomes one ordered atomic collection");
        check(!collection.Accept(1, 1, 0, new[] { 9 }, 12, out complete), "old metadata revision cannot roll back a completed collection");
        bool rejected = false;
        try { collection.Accept(3, 3, 0, new[] { 1, 2 }, 16, out complete); collection.Accept(3, 3, 1, new[] { 3, 4 }, 16, out complete); }
        catch (System.IO.InvalidDataException) { rejected = true; }
        check(rejected, "overlapping metadata pages are rejected");
    }
}
