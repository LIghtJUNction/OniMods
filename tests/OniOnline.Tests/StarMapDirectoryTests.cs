using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class StarMapDirectoryTests
{
    private sealed class Peer : IDisposable
    {
        internal IRoomTransport Transport;
        internal RoomSession Session;
        internal StarMapDirectorySession Directory;
        internal StarMapEntry[] Source;
        internal int Captures;
        internal bool PumpDirectory = true;
        internal readonly List<StarMapDirectoryMessage> Messages = new List<StarMapDirectoryMessage>();
        internal readonly List<int> Sizes = new List<int>();
        public void Dispose() { Directory.Dispose(); Session.Dispose(); }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(50123);
        internal readonly Peer[] Peers;
        internal Peer Host { get { return Peers[0]; } }
        internal Peer Guest { get { return Peers[1]; } }
        internal Room(bool weak = false, int count = 3)
        {
            Peers = new Peer[count];
            if (weak) { Network.Settings.Delay = .7; Network.Settings.Jitter = .2;
                Network.Settings.Drop = .1; Network.Settings.ReliableOrdered = true; }
            for (int i = 0; i < Peers.Length; i++)
            {
                var peer = new Peer(); Peers[i] = peer;
                peer.Transport = i == 0 ? Network.Host : Network.Connect();
                peer.Session = new RoomSession(peer.Transport, new SessionProfile
                { Name = "Map" + i, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Chart",
                    reconnect: i == 0 ? null : () => peer.Transport = Network.Connect(),
                    options: new SessionOptions { RetryJitter = () => .5 });
                peer.Source = new[] { new StarMapEntry { Q = 1, R = 0, Name = "原生星球", Discovered = true } };
                peer.Directory = new StarMapDirectorySession(peer.Session, () => { peer.Captures++; return peer.Source; });
                peer.Session.ApplicationMessageReceived += (sender, data) =>
                {
                    if (!StarMapDirectoryWire.IsMessage(data)) return;
                    peer.Messages.Add(StarMapDirectoryWire.Decode(data)); peer.Sizes.Add(data.Length);
                };
            }
            Until(() => Peers.All(p => p.Session.Joined && p.Session.Players.Length == count), "authenticate");
        }
        internal void Step()
        {
            Network.Advance(.05);
            foreach (var peer in Peers) peer.Session.Pump(Network.Now);
            foreach (var peer in Peers) if (peer.PumpDirectory) peer.Directory.Pump(Network.Now);
        }
        internal void Advance(double seconds)
        { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition, string message, double timeout = 60)
        {
            double until = Network.Now + timeout;
            while (Network.Now < until) { Step(); if (condition()) return; }
            throw new Exception("Star-map directory timeout: " + message);
        }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        DemandAndVisibility(check);
        SenderAndLifetime(check);
        DisconnectAndExpiry(check);
        WireLimits(check);
        FairDeliveryDuringStalls(check);
        Console.WriteLine("Star-map directory: host-owned visible metadata, paged native labels, idle suppression, authenticated sender and weak-network reconnect. No Unity rendering claim.");
    }

    private static void FairDeliveryDuringStalls(Action<bool, string> check)
    {
        using (var room = new Room(count: 5))
        {
            room.Host.PumpDirectory = false;
            foreach (var peer in room.Peers.Skip(1)) peer.Directory.Start();
            room.Advance(.2);
            // A busy native game may only pump once per capture interval. Each
            // sample changes, so restarting at the first observer starves others.
            for (int revision = 0; revision < 8; revision++)
            {
                room.Advance(2.1);
                room.Host.Source[0].Name = "Chart " + revision;
                room.Host.Directory.Pump(room.Network.Now);
                room.Step();
            }
            foreach (var peer in room.Peers.Skip(1))
                check(!peer.Directory.Stale &&
                    (peer.Directory.Entries[0].Name == "Chart 7" || peer.Directory.Entries[0].Name == "Chart 6"),
                    "every observer receives recent chart changes despite a two-page budget and stalled host");
            check(room.Peers.Skip(1).Sum(p => p.Messages.Count(m => m.Kind == StarMapDirectoryKind.State)) == 16,
                "fair chart delivery retains the two-page per-pump budget");
        }
    }

    private static void DemandAndVisibility(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Advance(5);
            check(room.Host.Captures == 0, "closed native charts perform no native metadata sampling");
            room.Host.Source = Enumerable.Range(0, 64).Select(i => new StarMapEntry
            { Q = i, R = -i, Name = i == 3 ? "secret unscouted name" : new string('星', 64), Discovered = i != 3 }).ToArray();
            room.Guest.Directory.Start();
            room.Until(() => !room.Guest.Directory.Stale && room.Guest.Directory.Entries.Length == 64, "four-page chart");
            check(room.Guest.Messages.Count(m => m.Kind == StarMapDirectoryKind.State) == 4 &&
                room.Guest.Sizes.All(size => size <= Protocol.MaxApplicationBytes),
                "64 chart labels use four bounded application pages rather than any world snapshot");
            check(room.Guest.Directory.Entries[3].Name == "" && !room.Guest.Directory.Entries[3].Discovered,
                "unscouted chart positions never carry a private native asteroid name");
            var copy = room.Guest.Directory.Entries; copy[0].Name = "local mutation"; copy[0].Q = -20;
            check(room.Guest.Directory.Entries[0].Q == 0 && room.Guest.Directory.Entries[0].Name != "local mutation",
                "map consumers cannot mutate the published directory cache");
            int packets = room.Guest.Messages.Count(m => m.Kind == StarMapDirectoryKind.State), samples = room.Host.Captures;
            room.Advance(45);
            check(room.Guest.Messages.Count(m => m.Kind == StarMapDirectoryKind.State) == packets,
                "unchanged chart labels send zero state pages, including across lease renewal");
            check(room.Host.Captures - samples <= 23 && room.Peers[2].Captures == 0 && room.Peers[2].Directory.Entries.Length == 0,
                "native sampling stays at most once per two seconds and is never requested from a guest");
            room.Host.Source[0].Name = "名字变了";
            room.Until(() => room.Guest.Directory.Entries.FirstOrDefault()?.Name == "名字变了", "changed native label");
            room.Guest.Directory.Stop(); room.Advance(1);
            samples = room.Host.Captures; room.Advance(5);
            check(room.Host.Captures == samples && room.Guest.Directory.Stale && room.Guest.Directory.Entries.Length == 0,
                "closing the native map clears its local data and stops the host sampler");
        }
    }

    private static void SenderAndLifetime(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Guest.Directory.Start(); room.Until(() => !room.Guest.Directory.Stale, "initial chart");
            var oldRequest = room.Host.Messages.Last(m => m.Kind == StarMapDirectoryKind.Subscribe);
            var oldPage = room.Guest.Messages.Last(m => m.Kind == StarMapDirectoryKind.State);
            room.Guest.Directory.Stop(); room.Advance(1);
            int samples = room.Host.Captures;
            room.Guest.Session.SendApplication(StarMapDirectoryWire.Encode(oldRequest)); room.Advance(3);
            check(room.Host.Captures == samples, "delayed old subscribe cannot resurrect a stopped chart lease");
            room.Host.Source[0].Name = "fresh chart";
            room.Guest.Directory.Start(); room.Until(() => !room.Guest.Directory.Stale &&
                room.Guest.Directory.Entries[0].Name == "fresh chart", "new chart request");
            room.Host.Session.SendApplicationTo(room.Guest.Session.LocalPlayerId, StarMapDirectoryWire.Encode(oldPage));
            room.Advance(1);
            check(room.Guest.Directory.Entries[0].Name == "fresh chart", "old request nonce cannot replace the reopened native chart");
            var request = room.Host.Messages.Last(m => m.Kind == StarMapDirectoryKind.Subscribe);
            var forged = new StarMapDirectoryMessage { Kind = StarMapDirectoryKind.State, Room = room.Host.Session.RoomId,
                Nonce = request.Nonce, Request = request.Request, Sequence = 9999, Total = 1,
                Entries = new[] { new StarMapEntry { Q = 4, R = 0, Name = "forged", Discovered = true } } };
            check(room.Host.Session.ForwardApplication(room.Peers[2].Session.LocalPlayerId, room.Guest.Session.LocalPlayerId,
                StarMapDirectoryWire.Encode(forged)), "third-peer forgery traverses the real authenticated relay");
            room.Advance(1);
            check(room.Guest.Directory.Entries[0].Name == "fresh chart", "a connected guest cannot impersonate the chart-owning host");
            forged.Room = Guid.NewGuid().ToString("N");
            room.Host.Session.SendApplicationTo(room.Guest.Session.LocalPlayerId, StarMapDirectoryWire.Encode(forged)); room.Advance(1);
            check(room.Guest.Directory.Entries[0].Name == "fresh chart", "a different room identity cannot replace native map metadata");
            room.Host.Source = null;
            room.Until(() => room.Guest.Directory.Stale, "host loading removes chart");
            check(room.Guest.Directory.Entries.Length == 0 && room.Guest.Directory.LastError == "star_map_unavailable",
                "unavailable native source clears previous map information rather than presenting a stale chart");
            room.Host.Source = new[] { new StarMapEntry { Q = 0, R = 1, Name = "after load", Discovered = true } };
            room.Until(() => !room.Guest.Directory.Stale && room.Guest.Directory.Entries[0].Name == "after load", "source reload");
            check(room.Guest.Directory.Entries[0].R == 1, "new native chart replaces the old source after loading");
        }
    }

    private static void DisconnectAndExpiry(Action<bool, string> check)
    {
        using (var room = new Room(weak: true))
        {
            room.Guest.Directory.Start(); room.Until(() => !room.Guest.Directory.Stale, "weak chart baseline");
            room.Guest.Transport.Disconnect("host"); room.Step();
            check(room.Guest.Directory.Stale && room.Guest.Directory.Entries.Length == 0,
                "disconnect immediately hides native chart data");
            room.Host.Source[0].Name = "after reconnect";
            room.Until(() => room.Guest.Session.Joined && !room.Guest.Session.Reconnecting &&
                !room.Guest.Directory.Stale && room.Guest.Directory.Entries[0].Name == "after reconnect", "reconnect fresh chart");
            check(room.Guest.Directory.Entries.Length == 1, "reconnected native map uses fresh metadata under reliable delay and loss");
            room.Guest.Transport.Disconnect("host"); room.Step(); room.Guest.Directory.Stop();
            room.Until(() => room.Guest.Session.Joined && !room.Guest.Session.Reconnecting, "rejoin after close"); room.Advance(5);
            check(room.Guest.Directory.Stale && room.Guest.Directory.Entries.Length == 0,
                "closing while offline prevents automatic observation after rejoining");
            room.Guest.Directory.Start(); room.Until(() => !room.Guest.Directory.Stale, "open again");
            room.Guest.PumpDirectory = false; room.Advance(34);
            int captures = room.Host.Captures; room.Advance(6);
            check(room.Host.Captures == captures, "an abandoned native map lease expires while room chat remains connected");
        }
    }

    private static void WireLimits(Action<bool, string> check)
    {
        var basic = new StarMapEntry { Q = 0, R = 0, Name = "Native", Discovered = true };
        foreach (var invalid in new[] {
            new[] { basic, basic },
            new[] { new StarMapEntry { Q = int.MinValue, Name = "Native", Discovered = true } },
            new[] { new StarMapEntry { Q = 128, R = 1, Name = "Native", Discovered = true } },
            new[] { new StarMapEntry { Name = new string('x', 65), Discovered = true } },
            new[] { new StarMapEntry { Name = "\ud800", Discovered = true } },
            Enumerable.Range(0, 65).Select(i => new StarMapEntry { Q = i, R = -i, Name = "Native", Discovered = true }).ToArray()
        })
        {
            bool rejected = false;
            try { StarMapDirectoryWire.Snapshot(invalid); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid, duplicate, oversized or hidden chart metadata fails closed");
        }
        var message = new StarMapDirectoryMessage { Kind = StarMapDirectoryKind.State, Room = Guid.NewGuid().ToString("N"),
            Nonce = Guid.NewGuid().ToString("N"), Request = 1, Sequence = 1, Total = 1, Entries = new[] { basic } };
        var bytes = StarMapDirectoryWire.Encode(message);
        check(StarMapDirectoryWire.Decode(bytes).Entries[0].Name == "Native", "bounded native chart page roundtrips");
        bool trailingRejected = false;
        try { StarMapDirectoryWire.Decode(bytes.Concat(new byte[] { 0 }).ToArray()); } catch (InvalidDataException) { trailingRejected = true; }
        check(trailingRejected, "chart codec rejects trailing data");
        message.Entries[0] = new StarMapEntry { Q = 1, R = 2, Discovered = false, Name = "never publish" };
        check(StarMapDirectoryWire.Decode(StarMapDirectoryWire.Encode(message)).Entries[0].Name == "",
            "source-provided private name is erased before the chart packet is emitted");
    }
}
