using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class WorldCellDetailsTests
{
    private sealed class Peer : IDisposable
    {
        internal RoomSession Session;
        internal WorldCellDetailsSession Details;
        internal string Planet = Guid.NewGuid().ToString("N"), Epoch = Guid.NewGuid().ToString("N");
        internal int Captures, Value;
        internal bool Pump = true;
        internal readonly List<WorldCellDetailsMessage> Received = new List<WorldCellDetailsMessage>();
        public void Dispose() { Details.Dispose(); Session.Dispose(); }
    }
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(5471);
        internal readonly Peer[] Peers = new Peer[3];
        internal readonly HashSet<string> Grants = new HashSet<string>();
        internal Room(bool weak = false)
        {
            if (weak) { Network.Settings.Delay = 1.5; Network.Settings.Jitter = .5; Network.Settings.Drop = .2;
                Network.Settings.Reorder = .3; Network.Settings.ReliableOrdered = true; }
            for (int i = 0; i < Peers.Length; i++)
            {
                var peer = new Peer(); Peers[i] = peer;
                peer.Session = new RoomSession(i == 0 ? Network.Host : Network.Connect(), new SessionProfile
                { Name = "Detail" + i, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Details",
                    reconnect: i == 0 ? null : () => Network.Connect(), options: new SessionOptions { RetryJitter = () => .5 });
                peer.Details = new WorldCellDetailsSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    (observer, planet, index) => Capture(Find(planet), index), (observer, planet) => Grants.Contains(observer + ":" + planet));
                peer.Session.ApplicationMessageReceived += (sender, bytes) =>
                { WorldCellDetailsMessage message; if (WorldCellDetailsWire.TryDecode(bytes, out message)) peer.Received.Add(message); };
            }
            Until(() => Peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), 60, "authentication");
        }
        internal Peer Find(string planet) { return Peers.FirstOrDefault(p => p != null && p.Planet == planet); }
        internal void Grant(int observer, int authority) { Grants.Add(Peers[observer].Session.LocalPlayerId + ":" + Peers[authority].Planet); }
        private WorldCellDetailResult Capture(Peer owner, int index)
        {
            owner.Captures++;
            if (index < 0 || index >= 64 || index == 9) return null;
            return new WorldCellDetailResult { Epoch = owner.Epoch, Width = 8, Height = 8,
                Cell = new WorldInterestCell { Index = index, Explored = true,
                    Fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass,
                    ElementHash = 5, Color = 0xffaa00ff, Temperature = 300 + owner.Value, Mass = index + 10 + owner.Value } };
        }
        internal void Watch(int observer, int authority, int index)
        { var value = Peers[authority]; if (!Peers[observer].Details.Watch(value.Session.LocalPlayerId, value.Planet, value.Epoch, index)) throw new Exception("watch rejected"); }
        internal void Step()
        {
            Network.Advance(.05);
            foreach (var peer in Peers) peer.Session.Pump(Network.Now);
            foreach (var peer in Peers) if (peer.Pump) peer.Details.Pump(Network.Now);
        }
        internal void Advance(double seconds) { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition, double seconds, string name)
        {
            double until = Network.Now + seconds;
            while (Network.Now < until) { Step(); if (condition()) return; }
            throw new Exception("Cell details timeout " + name + "; " + string.Join(";", Peers.Select(p => p.Session.LastError + "/" +
                p.Details.LastError + "/" + p.Details.Current?.Sequence + "/" + p.Details.ActiveObservers)));
        }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        RoutingAndHover(check); WeakNetwork(check); RevocationAndEpoch(check); Codec(check);
    }

    private static void RoutingAndHover(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var observer = room.Peers[2]; var owner = room.Peers[1];
            room.Advance(2); check(room.Peers.All(p => p.Captures == 0), "idle detail channels never sample");
            check(!observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, null, 0), "hover requires previously trusted epoch");
            room.Watch(2, 1, 0);
            room.Until(() => observer.Details.LastError == "world_cell_details_denied", 5, "unauthorized hover");
            check(owner.Captures == 0 && observer.Details.Current == null, "private hover denied before source callback");
            room.Grant(2, 1); room.Watch(2, 1, 0);
            room.Until(() => !observer.Details.Stale, 5, "guest owner through host relay");
            check(observer.Details.Current.Cell.Mass == 10 && observer.Details.Current.Epoch == owner.Epoch, "authenticated detail has requested cell and trusted epoch");
            var original = observer.Received.Last(m => m.Kind == WorldCellDetailsKind.Value);
            int messages = observer.Received.Count(m => m.Kind == WorldCellDetailsKind.Value);
            long sequence = observer.Details.Current.Sequence;
            room.Advance(4);
            check(observer.Details.Current.Sequence == sequence && observer.Received.Count(m => m.Kind == WorldCellDetailsKind.Value) == messages,
                "unchanged numbers do not create push messages or increment header sequence");
            owner.Value = 7;
            room.Until(() => observer.Details.Current.Cell.Temperature == 307, 5, "owner changes push");
            check(observer.Details.Current.Sequence == sequence + 1, "numerical change creates one new sequence");
            room.Watch(2, 1, 1); room.Watch(2, 1, 2);
            room.Until(() => observer.Details.Current?.Cell.Index == 2, 5, "latest pointer wins");
            original.Sequence = 1000; original.Result.Sequence = 1000;
            owner.Session.SendApplication(WorldCellDetailsWire.Encode(original)); room.Advance(1);
            check(observer.Details.Current.Cell.Index == 2, "late previous nonce response cannot overwrite new hover");
            var current = observer.Received.Last(m => m.Kind == WorldCellDetailsKind.Value && m.Index == 2);
            current.Result.Cell.Mass = 999; current.Sequence += 100;
            room.Peers[0].Session.SendApplicationTo(observer.Session.LocalPlayerId, WorldCellDetailsWire.Encode(current)); room.Advance(1);
            check(observer.Details.Current.Cell.Mass != 999, "host transport cannot impersonate other planet authority");
            var latestWatch = owner.Received.Last(m => m.Kind == WorldCellDetailsKind.Watch && m.Index == 2);
            observer.Details.Stop(); room.Advance(1);
            observer.Session.SendApplication(WorldCellDetailsWire.Encode(latestWatch)); room.Advance(1);
            int captures = owner.Captures; room.Advance(2);
            check(owner.Captures == captures && owner.Details.ActiveObservers == 0, "cancel tombstone rejects delayed renewal and stops reads");
            room.Watch(2, 1, 9);
            room.Until(() => observer.Details.LastError == "world_cell_details_denied", 5, "unexplored cell");
            check(observer.Details.Current == null && observer.Details.Stale, "unexplored cell never enters client cache");
            room.Grant(0, 1); room.Watch(0, 1, 3);
            room.Until(() => room.Peers[0].Details.Current?.Cell.Index == 3, 5, "host observes guest");
            check(room.Peers[0].Details.Current.Cell.Mass == 20, "host detail uses same authenticated guest callback");
        }
    }

    private static void WeakNetwork(Action<bool, string> check)
    {
        using (var room = new Room(true))
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1];
            room.Watch(2, 1, 4);
            room.Until(() => observer.Details.Current?.Cell.Index == 4, 60, "1.5s loss initial hover");
            owner.Value = 6;
            room.Until(() => observer.Details.Current?.Cell.Temperature == 306, 60, "loss changed detail");
            check(observer.Details.Current.Cell.Mass == 20, "1.5s latency with loss eventually delivers current single-cell values");
            room.Watch(2, 1, 5); room.Watch(2, 1, 6);
            room.Until(() => observer.Details.Current?.Cell.Index == 6, 60, "loss latest hover");
            check(room.Peers.All(p => p.Details.QueuedBytes <= WorldCellDetailsSession.OutboxByteBudget &&
                p.Details.EstimatedObserverBytes <= WorldCellDetailsSession.ObserverByteBudget), "hover relay and owner state obey byte budgets under loss");
            observer.Pump = false; room.Advance(15);
            int captures = owner.Captures; room.Advance(4);
            check(owner.Details.ActiveObservers == 0 && owner.Captures == captures, "expired hover lease releases native sampling while game stays online");
            observer.Pump = true; owner.Value = 10;
            room.Until(() => observer.Details.Current?.Cell.Temperature == 310, 60, "lease restart");
            check(observer.Details.Current.Sequence > 1, "expired same-nonce lease preserves monotonic changed-value sequence");
        }
    }

    private static void RevocationAndEpoch(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Grant(2, 1); var observer = room.Peers[2]; var owner = room.Peers[1]; room.Watch(2, 1, 0);
            room.Until(() => !observer.Details.Stale, 5, "revocation baseline");
            var old = observer.Received.Last(m => m.Kind == WorldCellDetailsKind.Value);
            room.Grants.Clear(); room.Advance(2);
            check(observer.Details.Stale && observer.Details.Current == null && owner.Details.ActiveObservers == 0,
                "permission revoke sends denial and purges cached numerical detail");
            owner.Session.SendApplication(WorldCellDetailsWire.Encode(old)); room.Advance(1);
            check(observer.Details.Current == null, "late value cannot revive revoked nonce");
            room.Grant(2, 1); owner.Epoch = Guid.NewGuid().ToString("N");
            check(observer.Details.Watch(owner.Session.LocalPlayerId, owner.Planet, old.Epoch, 0), "request can carry old trusted epoch but cannot learn a new one");
            room.Until(() => observer.Details.LastError == "world_cell_details_denied", 5, "source epoch replacement");
            check(observer.Details.Current == null, "native source epoch mismatch does not silently rebind");
            room.Watch(2, 1, 0); room.Until(() => !observer.Details.Stale, 5, "new primary trusted epoch");
            check(observer.Details.Current.Epoch == owner.Epoch, "fresh primary binding allows detail from replacement source");
        }
    }

    private static void Codec(Action<bool, string> check)
    {
        var message = new WorldCellDetailsMessage { Kind = WorldCellDetailsKind.Value, RoomId = new string('a', 32),
            TargetId = new string('b', 32), PlanetId = new string('c', 32), Nonce = new string('d', 32), Epoch = new string('e', 32),
            Index = 3, Request = 1, Sequence = 1, Result = new WorldCellDetailResult { Epoch = new string('e', 32), Width = 8, Height = 8,
                Cell = new WorldInterestCell { Index = 3, Explored = true,
                    Fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass, Temperature = 300, Mass = 2 } } };
        byte[] bytes = WorldCellDetailsWire.Encode(message); WorldCellDetailsMessage decoded;
        check(bytes.Length <= WorldCellDetailsWire.MaxPacketBytes && WorldCellDetailsWire.TryDecode(bytes, out decoded) && decoded.Result.Cell.Mass == 2,
            "single-cell production codec roundtrips in small packet budget");
        for (int i = 0; i < bytes.Length; i++)
        { byte[] truncated = bytes.Take(i).ToArray(); check(!WorldCellDetailsWire.TryDecode(truncated, out decoded), "truncated single-cell packet denied"); }
        check(!WorldCellDetailsWire.TryDecode(bytes.Concat(new byte[] { 0 }).ToArray(), out decoded), "trailing single-cell packet data denied");
        check(!WorldCellDetailsWire.TryDecode(new byte[WorldCellDetailsWire.MaxPacketBytes + 1], out decoded), "oversized cell detail denied before parse");
        message.Result.Cell.Mass = float.NaN;
        bool rejected = false; try { WorldCellDetailsWire.Encode(message); } catch (System.IO.InvalidDataException) { rejected = true; }
        check(rejected, "nonfinite native detail rejected by encoder");
        message.Result.Cell.Mass = 1; message.Result.Width = 1; message.Result.Height = 1;
        rejected = false; try { WorldCellDetailsWire.Encode(message); } catch (System.IO.InvalidDataException) { rejected = true; }
        check(rejected, "detail index must satisfy provider world dimensions");
    }
}
