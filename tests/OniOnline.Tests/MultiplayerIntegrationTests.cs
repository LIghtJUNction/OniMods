using System;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Threading;
using OniOnline.Multiplayer;
using OniOnline.Network;

// Exercises production session, world routing and native-clock controller together.
// Only the Unity boundary is replaced; this does not establish native simulation correctness.
internal static class MultiplayerIntegrationTests
{
    private static Action<bool, string> check;

    private sealed class Source : IWorldSource
    {
        public bool IsAvailable { get; set; } = true;
        internal string Epoch = Guid.NewGuid().ToString("N");
        internal bool LastSharing;
        internal int Value = 1;
        internal double Time;
        internal int CellCount = 500;
        public WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true)
        {
            if (LastSharing != shared) { LastSharing = shared; Epoch = Guid.NewGuid().ToString("N"); }
            return new WorldSnapshot
            {
                World = new WorldDescriptor { Id = ownerId + ":0", OwnerId = ownerId, Name = "Colony",
                    Epoch = Epoch, ControlRevision = 1, Width = 64, Height = 64, NativeWorldId = 0,
                    Shared = shared, SimulationTime = Time },
                Sequence = sequence,
                Cells = !shared || !includeCells ? new WorldCell[0] : Enumerable.Range(0, CellCount)
                    .Select(i => new WorldCell { Index = i, Mass = Value, Temperature = 300, Color = 0xffaaffff }).ToArray()
            };
        }
    }

    private sealed class NativeClock : INativeTimeControl
    {
        public int Generation { get; set; }
        public bool Available { get; set; } = true;
        public int PauseCount { get; private set; }
        public bool IsPaused { get { return PauseCount > 0; } }
        public int Speed { get; private set; }
        public void Refresh() { }
        public void Pause() { PauseCount++; }
        public void Unpause() { PauseCount = Math.Max(0, PauseCount - 1); }
        public void SetSpeed(int index) { Speed = index; }
    }

    private sealed class Peer : IDisposable
    {
        internal readonly RoomSession Session;
        internal readonly Source Source = new Source();
        internal readonly NativeClock Native = new NativeClock();
        internal readonly WorldReplication Worlds;
        internal readonly SharedTimeController Time;
        internal Peer(IRoomTransport transport, Func<IRoomTransport> reconnect = null)
        {
            Session = new RoomSession(transport, new SessionProfile { Name = "Player", GameVersion = "744825",
                CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Integration", reconnect: reconnect,
                options: new SessionOptions { RetryJitter = () => 0.5 });
            Time = new SharedTimeController(Session, Source, Native);
            Worlds = new WorldReplication(Session, Source);
        }
        internal void Pump(double now)
        {
            Session.Pump(now);
            Time.Pump(now);
            Worlds.Pump(now);
            if (!Native.IsPaused) Source.Time += 0.05;
        }
        public void Dispose() { Worlds.Dispose(); Time.Dispose(); Session.Dispose(); }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(8831);
        internal readonly Peer[] Peers;
        internal Room(int count, bool delayed)
        {
            if (delayed)
            {
                Network.Settings.Delay = 1.5;
                Network.Settings.Jitter = 0.5;
                Network.Settings.Drop = 0.2;
                Network.Settings.Reorder = 0.3;
                Network.Settings.ReliableOrdered = true;
            }
            Peers = new Peer[count];
            Peers[0] = new Peer(Network.Host);
            for (int i = 1; i < count; i++) Peers[i] = new Peer(Network.Connect(), Network.Connect);
            Until(() => Peers.All(p => p.Session.Joined), 40, "all integration peers authenticate");
            Peers[0].Session.StartGame();
            Until(() => Peers.All(p => p.Session.Started && !p.Time.WaitingForWorlds), 40,
                "shared native clock becomes ready through the real authenticated session");
        }
        internal void Step()
        {
            Network.Advance(0.05);
            foreach (var peer in Peers) peer.Pump(Network.Now);
        }
        internal void Advance(double seconds)
        { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition, double seconds, string description)
        {
            double until = Network.Now + seconds;
            while (Network.Now <= until)
            {
                Step();
                if (condition()) { check(true, description); return; }
            }
            throw new Exception("Integrated multiplayer timeout: " + description + "; " +
                string.Join(";", Peers.Select(PeerState)));
        }
        private static string PeerState(Peer peer)
        {
            return peer.Session.LastError + ":waiting=" + peer.Time.WaitingForWorlds +
                ":pending=" + peer.Worlds.PendingTransfers + ":worlds=" + string.Join(",", peer.Worlds.Worlds.Select(w =>
                    (w.IsLocal ? "local" : w.Descriptor.OwnerId.Substring(0, 4)) + "=" +
                    (w.Snapshot == null ? "none" : w.Snapshot.Sequence + "/" + w.Snapshot.Cells[0].Mass)));
        }

        internal WorldViewEntry View(int observer, int owner)
        { return Peers[observer].Worlds.Worlds.FirstOrDefault(w => w.Descriptor.OwnerId == Peers[owner].Session.LocalPlayerId); }
        internal void ResumeAll()
        {
            foreach (var peer in Peers) peer.Time.VoteResume();
            Until(() => Peers.All(p => !p.Time.Paused && !p.Native.IsPaused), 25,
                "unanimous resume releases only each controller's native pause ticket");
        }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        FourPlayerRoutingAndReconnect();
        LoadingGapAndRevocation();
        NativeDialogPauseAndOneWayOutage();
        LateJoinReceivesCurrentWorld();
        RenewedLocalInterestStartsWithCurrentSnapshot();
        Console.WriteLine("Integrated multiplayer: real RoomSession + WorldReplication + SharedTimeController; fake Unity source/time boundary.");
    }

    private static void LateJoinReceivesCurrentWorld()
    {
        using (var room = new Room(2, true))
        {
            room.Peers[0].Source.Value = 31;
            room.Peers[0].Worlds.SetSharing(true);
            room.ResumeAll();
            using (var late = new Peer(room.Network.Connect(), room.Network.Connect))
            {
                double deadline = room.Network.Now + 60;
                bool requested = false;
                while (room.Network.Now <= deadline)
                {
                    room.Step();
                    late.Pump(room.Network.Now);
                    if (late.Session.Joined && !requested)
                    {
                        late.Worlds.RequestWorld(room.Peers[0].Session.LocalPlayerId);
                        requested = true;
                    }
                    if (late.Session.Started && !late.Time.WaitingForWorlds && late.Worlds.Worlds.Any(w =>
                        w.Descriptor.OwnerId == room.Peers[0].Session.LocalPlayerId && w.Snapshot?.Cells[0].Mass == 31)) break;
                }
                check(late.Session.Joined && late.Session.Started, "late join inherits the running room without asking host to start again");
                check(late.Worlds.Worlds.Any(w => !w.IsLocal && w.Snapshot?.Cells[0].Mass == 31),
                    "late join receives an existing owner's latest full authorized observation");
                check(room.Peers[0].Native.IsPaused && late.Native.IsPaused,
                    "membership change invalidates old resume votes while late player gets ready");
                check(!late.Time.WaitingForWorlds && late.Time.Voters == 3, "late join becomes a current pause/speed voter");
            }
        }
    }

    internal static void RunTcp(Action<bool, string> assertion)
    {
        check = assertion;
        using (var transport = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var proxy = new TcpFaultProxy(transport.Port, 8831))
        using (var host = new Peer(transport))
        using (var client = new Peer(TcpRoomTransport.Join("127.0.0.1", proxy.Port)))
        {
            proxy.DelayMilliseconds = 160;
            proxy.JitterMilliseconds = 70;
            proxy.BytesPerSecond = 2048;
            var timer = Stopwatch.StartNew();
            double offset = 0;
            int interruptions = 0;
            client.Session.Changed += () => { if (client.Session.Reconnecting) interruptions++; };
            Action pump = () => { double now = timer.Elapsed.TotalSeconds + offset; host.Pump(now); client.Pump(now); };
            Action<Func<bool>, double, string> until = (condition, seconds, description) =>
            {
                double deadline = timer.Elapsed.TotalSeconds + seconds;
                while (timer.Elapsed.TotalSeconds <= deadline)
                {
                    pump();
                    if (condition()) { check(true, description); return; }
                    Thread.Sleep(5);
                }
                throw new Exception("TCP multiplayer timeout: " + description + "; reconnects=" + interruptions +
                    "; host=" + host.Session.LastError + "; client=" + client.Session.LastError);
            };
            until(() => client.Session.Joined, 15, "bandwidth-limited TCP world fixture authenticates");
            string identity = client.Session.LocalPlayerId;
            host.Session.StartGame();
            until(() => client.Session.Started && !host.Time.WaitingForWorlds && !client.Time.WaitingForWorlds, 15,
                "native clock readiness crosses the throttled TCP stream");
            host.Source.CellCount = 2000;
            host.Worlds.SetSharing(true);
            client.Worlds.RequestWorld(host.Session.LocalPlayerId);
            until(() => client.Worlds.Worlds.Any(w => !w.IsLocal && w.Snapshot != null), 75,
                "40KB world snapshot crosses actual 2KB/s TCP with latency and jitter");
            check(interruptions == 0, "world stream does not force transport reconnection under low bandwidth");
            check(client.Session.SendChat("chat alongside slow snapshot"), "TCP chat accepted during sustained map transfer");
            until(() => host.Session.Chat.Length == 1 && client.Session.Chat.Length == 1 && client.Session.PendingChatCount == 0,
                15, "chat converges while slow snapshots continue");

            proxy.Blackhole = true;
            until(() => proxy.StalledChunks > 0, 4, "world stream encounters an actual TCP forwarding stall");
            offset += 31;
            pump();
            until(() => client.Session.Reconnecting, 4, "world/clock connection enters reconnect after TCP blackhole");
            proxy.DisconnectAll();
            proxy.Blackhole = false;
            host.Source.Value = 88;
            until(() => client.Session.Joined && !client.Session.Reconnecting, 30, "TCP world client reconnects automatically");
            until(() => client.Worlds.Worlds.Any(w => !w.IsLocal && w.Snapshot != null && w.Snapshot.Cells[0].Mass == 88), 80,
                "TCP reconnect discards partial old transfer and publishes a fresh complete map");
            check(client.Session.LocalPlayerId == identity && host.Session.Players.Length == 2,
                "real TCP world recovery retains the original world owner's observer identity");
            check(host.Native.IsPaused && client.Native.IsPaused, "TCP recovery retains explicit shared pause");
            Console.WriteLine("TCP multiplayer: 2048 bytes/s per direction, 160ms delay +/-70ms per proxy chunk, 2000-cell maps and reconnect passed.");
        }
    }

    private static void FourPlayerRoutingAndReconnect()
    {
        using (var room = new Room(4, true))
        {
            foreach (var peer in room.Peers) peer.Worlds.SetSharing(true);
            room.Until(() => room.Peers.All(p => p.Worlds.Worlds.Count == 4), 30, "four owners publish independent world identities");
            room.Peers[2].Worlds.RequestWorld(room.Peers[1].Session.LocalPlayerId);
            room.Peers[3].Worlds.RequestWorld(room.Peers[1].Session.LocalPlayerId);
            room.Until(() => room.View(2, 1)?.Snapshot != null && room.View(3, 1)?.Snapshot != null, 45,
                "owner-to-host-to-two-guests transfers complete with 3s RTT, loss and jitter");
            check(room.View(2, 1).Snapshot.Cells.Length == 500 && room.View(2, 1).Snapshot.Cells[0].Mass == 1,
                "forwarded snapshot retains the authenticated original publisher and exact cells");
            room.ResumeAll();
            check(room.Peers[3].Session.SendChat("chat while maps transfer"), "chat queues alongside world streams");
            room.Until(() => room.Peers.All(p => p.Session.Chat.Length == 1), 25, "chat and shared-time heartbeat survive concurrent world transfers");

            string[] identities = room.Peers.Select(p => p.Session.LocalPlayerId).ToArray();
            room.Network.CutAll();
            room.Network.Settings.Blackhole = true;
            room.Advance(30);
            check(room.Peers.Skip(1).All(p => p.Session.Reconnecting && p.Native.IsPaused),
                "30-second outage holds every disconnected native simulation");
            room.Peers[1].Source.Value = 77;
            room.Network.Settings.Blackhole = false;
            room.Until(() => room.Peers.All(p => p.Session.Joined && !p.Session.Reconnecting && !p.Time.WaitingForWorlds), 70,
                "world/time controllers recover alongside all three authenticated reconnects");
            room.Until(() => room.View(2, 1)?.Snapshot?.Cells[0].Mass == 77 && room.View(3, 1)?.Snapshot?.Cells[0].Mass == 77, 50,
                "both observers converge on fresh post-reconnect cells");
            check(room.Peers.Select(p => p.Session.LocalPlayerId).SequenceEqual(identities), "world ownership survives reconnect without duplicate player IDs");
            check(room.Peers.All(p => p.Time.Paused), "network recovery cannot revive a stale resume vote");
            room.ResumeAll();
        }
    }

    private static void RenewedLocalInterestStartsWithCurrentSnapshot()
    {
        using (var room = new Room(2, false))
        {
            var host = room.Peers[0]; var client = room.Peers[1];
            host.Worlds.SetSharing(true);
            client.Worlds.RequestWorld(host.Session.LocalPlayerId);
            room.Until(() => room.View(1, 0)?.Snapshot != null, 15, "observer first receives the host's shared snapshot");
            client.Worlds.StopObserving();
            room.Advance(27);
            // Keep a newer authorized local cache that the disconnected observer
            // has never received. It is legitimate data, but no longer current
            // when the source changes before renewed remote interest.
            host.Worlds.RequestWorld(host.Session.LocalPlayerId);
            host.Worlds.StopObserving();
            check(room.View(0, 0).Snapshot.Sequence > room.View(1, 0).Snapshot.Sequence,
                "local cache is newer than the observer's completed watermark before reconnect");
            room.Network.CutAll(); room.Step();
            room.Until(() => room.Peers.All(p => p.Session.Joined && !p.Session.Reconnecting && !p.Time.WaitingForWorlds),
                20, "observer reconnects while its map subscription remains closed");
            host.Source.Value = 88;
            room.Advance(2);
            check(room.View(0, 0).Snapshot.Cells[0].Mass == 1,
                "metadata-only idle publication retains the old cache instead of capturing cells");
            long previous = room.View(1, 0).Snapshot.Sequence;
            float firstMass = -1;
            client.Worlds.Changed += () =>
            {
                var snapshot = room.View(1, 0)?.Snapshot;
                if (firstMass < 0 && snapshot != null && snapshot.Sequence > previous)
                    firstMass = snapshot.Cells[0].Mass;
            };
            client.Worlds.RequestWorld(host.Session.LocalPlayerId);
            room.Until(() => firstMass >= 0, 15, "renewed interest publishes its first completed frame");
            check(firstMass == 88, "renewed local interest streams current cells before any stale cached frame");
        }
    }

    private static void LoadingGapAndRevocation()
    {
        using (var room = new Room(3, false))
        {
            room.Peers[1].Worlds.SetSharing(true);
            room.Peers[2].Worlds.RequestWorld(room.Peers[1].Session.LocalPlayerId);
            room.Until(() => room.View(2, 1)?.Snapshot != null, 15, "guest observes an explicitly shared world");
            var old = room.View(2, 1).Snapshot;
            room.Peers[1].Source.IsAvailable = false;
            room.Until(() => room.View(2, 1) == null, 10, "loading gap withdraws the no-longer-available world");
            // Same native object/epoch after a transient publication gap.
            room.Peers[1].Source.IsAvailable = true;
            room.Peers[1].Source.Value = 12;
            room.Until(() => room.View(2, 1)?.Snapshot?.Cells[0].Mass == 12, 20,
                "same native world republishes after loading rather than remaining tombstoned forever");
            check(room.View(2, 1).Descriptor.Epoch != old.World.Epoch, "republished observation has a fresh publication epoch");
            foreach (byte[] chunk in WorldWire.EncodeSnapshot(old)) room.Peers[1].Session.SendApplication(chunk);
            room.Advance(2);
            check(room.View(2, 1).Snapshot.Cells[0].Mass == 12, "pre-loading chunks cannot overwrite the fresh world");

            room.Peers[1].Worlds.SetSharing(false);
            room.Until(() => room.View(2, 1) != null && !room.View(2, 1).Descriptor.Shared && room.View(2, 1).Snapshot == null,
                10, "revocation clears the host and observer's retained world view");
            room.Peers[1].Session.SendApplication(WorldWire.EncodeDescriptor(old.World));
            foreach (byte[] chunk in WorldWire.EncodeSnapshot(old)) room.Peers[1].Session.SendApplication(chunk);
            room.Advance(3);
            check(room.View(2, 1).Snapshot == null && room.View(0, 1).Snapshot == null,
                "queued old descriptors and cells cannot re-enable revoked sharing");
        }
    }

    private static void NativeDialogPauseAndOneWayOutage()
    {
        using (var room = new Room(2, false))
        {
            room.ResumeAll();
            room.Peers[1].Native.Pause(); // Native save/dialog ticket belongs to the game.
            room.Until(() => room.Peers.All(p => p.Time.Paused && p.Native.IsPaused), 8,
                "native dialog pause holds the other player's clock");
            check(room.Peers[1].Native.PauseCount == 2, "public and native dialog pauses retain separate tickets");
            room.Peers[1].Native.Unpause();
            room.Until(() => room.Peers.All(p => !p.Time.WaitingForWorlds), 8, "closing a native dialog restores readiness");
            room.ResumeAll();

            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                fromHost && Protocol.Decode(bytes).Kind == PacketKind.Application;
            room.Until(() => room.Peers.All(p => p.Native.IsPaused), 20,
                "one-way application outage holds both simulations despite healthy room heartbeats");
            check(room.Peers.All(p => p.Session.Joined), "one-way state timeout is distinguished from transport disconnection");
            room.Network.Settings.DropBeforeDelivery = null;
            room.Until(() => room.Peers.All(p => !p.Time.WaitingForWorlds), 12, "readiness recovers after one-way state delivery resumes");
            check(room.Peers.All(p => p.Native.IsPaused), "state recovery requires fresh unanimous resume");
            room.ResumeAll();
        }
    }
}
