using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using OniOnline.Multiplayer;
using OniOnline.Network;

// Exercises the shipped viewport, motion and hover lanes over real TCP. The
// sampled planet and frame consumer replace Unity; this is not an FPS test.
internal static class ReadOnlyLatencyTests
{
    private const int Width = 32, Height = 32, ViewWidth = 16, ViewHeight = 12;

    private sealed class Peer : IDisposable
    {
        internal RoomSession Session;
        internal WorldInterestSession Interest;
        internal WorldCellDetailsSession Details;
        internal readonly string Planet = Guid.NewGuid().ToString("N"), Epoch = Guid.NewGuid().ToString("N");
        internal int Value, Reconnects;
        public void Dispose() { Details.Dispose(); Interest.Dispose(); Session.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly TcpFaultProxy Proxy;
        internal readonly Peer[] Peers = new Peer[2];
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly List<double> PumpMilliseconds = new List<double>();
        internal int MaxInterestQueue, MaxDetailQueue, MaxInterestMemory, MaxDetailMemory, Samples;

        internal Fixture()
        {
            var transport = TcpRoomTransport.Host(IPAddress.Loopback, 0);
            Proxy = new TcpFaultProxy(transport.Port, 20437)
            { DelayMilliseconds = 500, JitterMilliseconds = 250, BytesPerSecond = 2048 };
            for (int i = 0; i < Peers.Length; i++)
            {
                var peer = new Peer(); Peers[i] = peer;
                peer.Session = new RoomSession(i == 0 ? transport : TcpRoomTransport.Join("127.0.0.1", Proxy.Port),
                    new SessionProfile { Name = "ReadOnly" + i, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true },
                    "", "Read-only latency", options: new SessionOptions { RetryJitter = () => .5 });
                peer.Session.Changed += () => { if (peer.Session.Reconnecting) peer.Reconnects++; };
                peer.Interest = new WorldInterestSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    planet => Find(planet) == null ? null : new PlanetManifest { PlanetId = planet, Width = Width, Height = Height },
                    (observer, planet, sub) => Capture(Find(planet), sub),
                    (observer, planet) => observer == Peers[1]?.Session.LocalPlayerId && planet == Peers[0].Planet);
                peer.Details = new WorldCellDetailsSession(peer.Session, planet => Find(planet)?.Session.LocalPlayerId,
                    (observer, planet, index) => Detail(Find(planet), index),
                    (observer, planet) => observer == Peers[1]?.Session.LocalPlayerId && planet == Peers[0].Planet);
            }
        }

        private Peer Find(string planet) { return Peers.FirstOrDefault(p => p != null && p.Planet == planet); }
        private WorldInterestFrame Capture(Peer peer, WorldInterestSubscription subscription)
        {
            var cells = new List<WorldInterestCell>();
            for (int y = subscription.Region.Y; y < subscription.Region.Y + subscription.Region.Height; y++)
                for (int x = subscription.Region.X; x < subscription.Region.X + subscription.Region.Width; x++)
                {
                    int index = y * Width + x;
                    if (index == 9) continue;
                    cells.Add(new WorldInterestCell { Index = index, Explored = true,
                        Fields = subscription.Channels & ~WorldInterestChannels.Entities,
                        ElementHash = 5, Color = 0xffaa0000u + (uint)peer.Value });
                }
            return new WorldInterestFrame { Epoch = peer.Epoch, Width = Width, Height = Height,
                Tick = (long)(Clock.Elapsed.TotalSeconds * 20), Cells = cells,
                Entities = (subscription.Channels & WorldInterestChannels.Entities) == 0 ? new WorldInterestEntity[0] :
                    new[] { new WorldInterestEntity { Id = 42, CellIndex = 0, Kind = 1, X = .2f + peer.Value / 100f, Y = .2f } } };
        }

        private static WorldCellDetailResult Detail(Peer peer, int index)
        {
            if (index < 0 || index >= Width * Height || index == 9) return null;
            return new WorldCellDetailResult { Epoch = peer.Epoch, Width = Width, Height = Height,
                Cell = new WorldInterestCell { Index = index, Explored = true, ElementHash = 5,
                    Fields = WorldInterestChannels.Visual | WorldInterestChannels.Mass | WorldInterestChannels.Temperature,
                    Color = 0xffaa0000u + (uint)peer.Value, Mass = 10 + peer.Value, Temperature = 300 + peer.Value } };
        }

        internal void Pump()
        {
            double now = Clock.Elapsed.TotalSeconds;
            long started = Stopwatch.GetTimestamp();
            foreach (var peer in Peers) peer.Session.Pump(now);
            foreach (var peer in Peers) { peer.Interest.Pump(now); peer.Details.Pump(now); }
            var replica = Peers[1].Interest.MotionReplica;
            WorldInterestEntity entity;
            if (replica != null && replica.SampleEntity(42, replica.LatestTick, out entity)) Samples++;
            PumpMilliseconds.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            foreach (var peer in Peers)
            {
                MaxInterestQueue = Math.Max(MaxInterestQueue, peer.Interest.QueuedBytes);
                MaxDetailQueue = Math.Max(MaxDetailQueue, peer.Details.QueuedBytes);
                MaxInterestMemory = Math.Max(MaxInterestMemory, peer.Interest.EstimatedObserverBytes);
                MaxDetailMemory = Math.Max(MaxDetailMemory, peer.Details.EstimatedObserverBytes);
            }
        }

        internal double Until(Func<bool> condition, double seconds, string description)
        {
            double started = Clock.Elapsed.TotalSeconds, deadline = started + seconds;
            while (Clock.Elapsed.TotalSeconds < deadline)
            {
                Pump();
                if (condition()) return Clock.Elapsed.TotalSeconds - started;
                Thread.Sleep(5);
            }
            throw new Exception("Read-only TCP timeout: " + description + "; " + string.Join("; ", Peers.Select(p =>
                "room=" + p.Session.LastError + ",interest=" + p.Interest.LastError + ",hover=" + p.Details.LastError +
                ",cells=" + p.Interest.VisualReplica?.Cells.Count + ",chat=" + p.Session.Chat.Length + ",reconnects=" + p.Reconnects)));
        }

        internal void Advance(double seconds, Action update = null)
        {
            double deadline = Clock.Elapsed.TotalSeconds + seconds;
            while (Clock.Elapsed.TotalSeconds < deadline) { update?.Invoke(); Pump(); Thread.Sleep(5); }
        }

        public void Dispose()
        { foreach (var peer in Peers.Reverse()) peer?.Dispose(); Proxy.Dispose(); }
    }

    private static bool ViewHas(Peer observer, int value)
    {
        var visual = observer.Interest.VisualReplica; var motion = observer.Interest.MotionReplica;
        return !observer.Interest.Stale && visual?.Cells.Count == ViewWidth * ViewHeight - 1 &&
            visual.Cells.Values.All(c => c.Color == 0xffaa0000u + (uint)value) &&
            motion?.Entities.ContainsKey(42) == true && motion.Entities[42].X == .2f + value / 100f;
    }

    internal static void Run(Action<bool, string> check)
    {
        using (var room = new Fixture())
        {
            var host = room.Peers[0]; var client = room.Peers[1];
            room.Until(() => room.Peers.All(p => p.Session.Joined && p.Session.Players.Length == 2), 25, "authentication");
            check(client.Interest.Subscribe(host.Session.LocalPlayerId, host.Planet, new WorldInterestSubscription
            { Revision = 1, Region = new WorldInterestRegion { Width = ViewWidth, Height = ViewHeight } }),
                "read-only viewport subscribes over delayed, throttled TCP");
            double firstView = room.Until(() => ViewHas(client, 0), 30, "first 192-cell viewport and motion frame");
            check(!client.Interest.VisualReplica.Cells.ContainsKey(9) && client.Interest.VisualReplica.Cells.Values.All(c => c.Mass == 0 && c.Temperature == 0),
                "viewport contains only explored visual cells; numerical hover uses its separate channel");
            check(client.Details.Watch(host.Session.LocalPlayerId, host.Planet, client.Interest.BoundEpoch, 0),
                "hover watch uses the primary viewport's authenticated epoch");
            room.Until(() => !client.Details.Stale && client.Details.Current?.Cell.Mass == 10, 15, "first numerical hover");
            check(host.Session.SendChat("owner: viewport is read-only") && client.Session.SendChat("observer: viewing your colony"),
                "both directions accept chat while viewport and hover subscriptions are active");
            double chat = room.Until(() => room.Peers.All(p => p.Session.Chat.Length == 2 && p.Session.PendingChatCount == 0), 15, "initial bidirectional chat");

            room.Proxy.Blackhole = true;
            int samples = room.Samples;
            check(client.Session.SendChat("observer: queued during short outage") && host.Session.SendChat("owner: still local and responsive"),
                "short outage accepts both chats without blocking the caller");
            room.Advance(2.5, () => host.Value = 1 + Math.Min(8, (int)(room.Clock.Elapsed.TotalSeconds * 10) % 9));
            check(room.Proxy.StalledChunks > 0 && room.Samples - samples >= 20,
                "actual forwarding blackhole preserves a sampleable retained motion image on the pump thread");
            host.Value = 9;
            room.Proxy.Blackhole = false;
            double recovery = room.Until(() => ViewHas(client, 9) && !client.Details.Stale && client.Details.Current?.Cell.Mass == 19 &&
                room.Peers.All(p => p.Session.Chat.Length == 4 && p.Session.PendingChatCount == 0), 30,
                "latest viewport, movement, hover and both queued chats converge after blackhole");
            check(room.Peers.All(p => p.Reconnects == 0), "short outage and low bandwidth do not force a room reconnection");
            check(host.Session.Chat.Select(c => c.Text).SequenceEqual(client.Session.Chat.Select(c => c.Text)) &&
                host.Session.Chat.Select(c => c.Sequence).Distinct().Count() == 4,
                "both chat histories converge in order without retry duplicates");
            check(room.MaxInterestQueue <= WorldInterestSession.OutboxByteBudget && room.MaxDetailQueue <= WorldCellDetailsSession.OutboxByteBudget &&
                room.MaxInterestMemory <= WorldInterestSession.ObserverMemoryBudget && room.MaxDetailMemory <= WorldCellDetailsSession.ObserverByteBudget,
                "all sampled viewport and hover queues and observer state stay inside production byte budgets");
            double maxPump = room.PumpMilliseconds.Max();
            check(maxPump < 250, "session, viewport, hover and replica sampling return before the proxy's minimum 250ms network delay");
            var sorted = room.PumpMilliseconds.OrderBy(v => v).ToArray();
            Console.WriteLine("Read-only TCP: 500ms +/-250ms/chunk; 2048B/s/direction; 192-cell viewport; 2.5s blackhole; " +
                "first-view=" + firstView.ToString("F2") + "s chat=" + chat.ToString("F2") + "s recovery=" + recovery.ToString("F2") +
                "s pump-p95/max=" + sorted[(int)((sorted.Length - 1) * .95)].ToString("F3") + "/" + maxPump.ToString("F3") +
                "ms queue-max(view/hover)=" + room.MaxInterestQueue + "/" + room.MaxDetailQueue + "B forwarded=" + room.Proxy.ForwardedBytes + "B. " +
                "Host fixture only: no Unity rendering or game-FPS claim.");
        }
    }
}
