using System;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class ViewportOnlyWorldReplicationTests
{
    private sealed class Source : IWorldSource
    {
        private readonly string epoch = Guid.NewGuid().ToString("N");
        internal int FullCaptures;
        internal double Time;
        public bool IsAvailable { get { return true; } }
        public WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true)
        {
            if (includeCells) FullCaptures++;
            return new WorldSnapshot
            {
                World = new WorldDescriptor { Id = ownerId + ":0", OwnerId = ownerId, Name = "Own colony",
                    Epoch = epoch, ControlRevision = 1, Width = 8, Height = 8, NativeWorldId = 0, Shared = shared, SimulationTime = Time },
                Sequence = sequence,
                Cells = shared && includeCells ? new[] { new WorldCell { Index = 0, ElementHash = 1, Mass = 10, Temperature = 300 } }
                    : Array.Empty<WorldCell>()
            };
        }
    }

    private sealed class Peer : IDisposable
    {
        internal readonly Source Source = new Source();
        internal readonly RoomSession Session;
        internal readonly WorldReplication Worlds;
        internal int ReceivedRequests, ReceivedChunks, ReceivedDescriptors;
        internal Peer(IRoomTransport transport)
        {
            Session = new RoomSession(transport, new SessionProfile
            { Name = "Viewport only", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Viewport only");
            Worlds = new WorldReplication(Session, Source);
            Session.ApplicationMessageReceived += (sender, data) =>
            {
                WorldMessage message;
                if (!WorldWire.TryDecode(data, out message)) return;
                if (message.Kind == WorldMessageKind.SnapshotRequest) ReceivedRequests++;
                if (message.Kind == WorldMessageKind.SnapshotChunk) ReceivedChunks++;
                if (message.Kind == WorldMessageKind.Descriptor) ReceivedDescriptors++;
            };
        }
        public void Dispose() { Worlds.Dispose(); Session.Dispose(); }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(45191);
        internal readonly Peer Host, Guest;
        internal Peer LateGuest;
        internal Room()
        {
            Host = new Peer(Network.Host); Guest = new Peer(Network.Connect());
            Until(() => Host.Session.Joined && Guest.Session.Joined && Host.Session.Players.Length == 2, "authenticated room");
        }
        internal void Step()
        {
            Network.Advance(.05); Host.Session.Pump(Network.Now); Guest.Session.Pump(Network.Now);
            LateGuest?.Session.Pump(Network.Now);
            Host.Source.Time += .05; Guest.Source.Time += .05;
            if (LateGuest != null) LateGuest.Source.Time += .05;
            Host.Worlds.Pump(Network.Now); Guest.Worlds.Pump(Network.Now);
            LateGuest?.Worlds.Pump(Network.Now);
        }
        internal void Advance(double seconds)
        { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition, string message)
        {
            double until = Network.Now + 8;
            while (Network.Now < until) { Step(); if (condition()) return; }
            throw new Exception("Viewport-only replication timeout: " + message);
        }
        public void Dispose() { LateGuest?.Dispose(); Guest.Dispose(); Host.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            room.Host.Worlds.SetSharing(true); room.Guest.Worlds.SetSharing(true);
            room.Until(() => room.Host.Worlds.Worlds.Count == 2 && room.Guest.Worlds.Worlds.Count == 2,
                "planet metadata remains available without bulk state");
            room.Advance(3);
            check(room.Host.Source.FullCaptures == 0 && room.Guest.Source.FullCaptures == 0,
                "sharing a planet without a viewport never captures a whole-world snapshot");
            check(room.Host.ReceivedChunks == 0 && room.Guest.ReceivedChunks == 0,
                "host does not receive a guest's whole-world state merely because sharing is enabled");
            check(room.Host.Worlds.Worlds.All(entry => entry.Snapshot == null) &&
                room.Guest.Worlds.Worlds.All(entry => entry.Snapshot == null),
                "metadata catalog contains no unseen remote cell cache");
            int descriptors = room.Host.ReceivedDescriptors + room.Guest.ReceivedDescriptors;
            double previousTime = room.Host.Source.Time;
            room.Advance(10);
            check(room.Host.Source.Time >= previousTime + 9.9 &&
                room.Host.ReceivedDescriptors + room.Guest.ReceivedDescriptors == descriptors,
                "independent local clocks advance without idle descriptor or catalog retransmission");

            room.Guest.Worlds.RequestWorld(room.Host.Session.LocalPlayerId); room.Advance(2);
            check(room.Host.ReceivedRequests == 0 && room.Host.Source.FullCaptures == 0,
                "missing viewport metadata cannot fall back to a remote bulk request");
            room.Guest.Session.SendApplication(WorldWire.EncodeRequest(room.Host.Session.LocalPlayerId)); room.Advance(2);
            check(room.Host.ReceivedRequests == 1 && room.Host.Source.FullCaptures == 0 && room.Guest.ReceivedChunks == 0,
                "an authenticated older peer cannot trigger whole-world capture with a legacy request");

            room.Host.Worlds.RequestWorld(room.Host.Session.LocalPlayerId);
            check(room.Host.Source.FullCaptures > 0 && room.Host.Worlds.Worlds.First(entry => entry.IsLocal).Snapshot?.Cells.Length == 1,
                "own-planet preview remains local and can inspect its explored state");
            room.Advance(3);
            check(room.Guest.ReceivedChunks == 0 && room.Host.Worlds.PendingTransfers == 0,
                "own-planet preview never uploads its cells to another player");

            var guestDescriptor = room.Guest.Worlds.Worlds.First(entry => entry.IsLocal).Descriptor;
            var unexpected = new WorldSnapshot { World = guestDescriptor, Sequence = 100,
                Cells = new[] { new WorldCell { Index = 1, ElementHash = 2, Mass = 12, Temperature = 310 } } };
            foreach (var packet in WorldWire.EncodeSnapshot(unexpected)) room.Guest.Session.SendApplication(packet);
            room.Advance(2);
            check(room.Host.ReceivedChunks > 0 && room.Host.Worlds.Worlds.First(entry => !entry.IsLocal).Snapshot == null,
                "unsolicited authenticated legacy chunks are rejected rather than assembled into a remote cache");
            check(room.Host.Worlds.PendingTransfers == 0 && room.Guest.Worlds.PendingTransfers == 0,
                "viewport-only mode never leaves a bulk transfer waiting for acknowledgement");
            room.LateGuest = new Peer(room.Network.Connect());
            room.Until(() => room.LateGuest.Session.Joined && room.LateGuest.Worlds.Worlds.Count == 3,
                "a new member receives the catalog without periodic full-room publication");
            check(room.LateGuest.Worlds.Worlds.Count(entry => !entry.IsLocal) == 2 && room.LateGuest.ReceivedChunks == 0,
                "roster changes replay current planet metadata to late joiners without cells");
        }
    }
}
