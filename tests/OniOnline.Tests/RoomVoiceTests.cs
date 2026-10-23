using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Core;
using OniOnline.Network;

internal static class RoomVoiceTests
{
    private sealed class Network : IDisposable
    {
        internal sealed class Endpoint : IRoomTransport, IRealtimeRoomTransport
        {
            internal readonly Queue<TransportEvent> Inbox = new Queue<TransportEvent>();
            internal readonly Dictionary<string, Endpoint> Peers = new Dictionary<string, Endpoint>();
            internal readonly List<byte[]> RealtimeSent = new List<byte[]>();
            internal readonly string Key;
            private readonly bool host;
            private bool disposed;
            internal Endpoint(string key, bool host) { Key = key; this.host = host; }
            public bool IsHost { get { return host; } }
            public string HostPeer { get { return "host"; } }
            public bool Send(string peer, byte[] data) { return Send(peer, data, false); }
            public bool SendRealtime(string peer, byte[] data) { return Send(peer, data, true); }
            private bool Send(string peer, byte[] data, bool realtime)
            {
                Endpoint target;
                if (disposed || data == null || !Peers.TryGetValue(peer, out target) || target.disposed) return false;
                if (realtime) RealtimeSent.Add((byte[])data.Clone());
                target.Inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Message,
                    Peer = target.IsHost ? Key : "host", Data = (byte[])data.Clone(), Realtime = realtime });
                return true;
            }
            public bool TryReceive(out TransportEvent item)
            { item = Inbox.Count == 0 ? null : Inbox.Dequeue(); return item != null; }
            public void Poll() { }
            public void Disconnect(string peer)
            {
                Endpoint target;
                if (!Peers.TryGetValue(peer, out target)) return;
                Peers.Remove(peer); target.Peers.Remove(target.IsHost ? Key : "host");
                Inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = peer });
                target.Inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = target.IsHost ? Key : "host" });
            }
            public void Dispose()
            {
                if (disposed) return;
                foreach (string peer in Peers.Keys.ToArray()) Disconnect(peer);
                disposed = true; Inbox.Clear();
            }
        }

        internal readonly Endpoint Host = new Endpoint("host", true);
        private readonly List<Endpoint> clients = new List<Endpoint>();
        internal Endpoint Connect()
        {
            var client = new Endpoint("client-" + (clients.Count + 1), false); clients.Add(client);
            client.Peers.Add("host", Host); Host.Peers.Add(client.Key, client);
            client.Inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Peer = "host" });
            Host.Inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Peer = client.Key });
            return client;
        }
        public void Dispose() { foreach (var client in clients) client.Dispose(); Host.Dispose(); }
    }

    private sealed class Peer : IDisposable
    {
        internal Network.Endpoint Transport;
        internal RoomSession Room;
        internal RoomVoiceSession Voice;
        public void Dispose() { Voice.Dispose(); Room.Dispose(); }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Network Network = new Network();
        internal readonly Peer[] Peers;
        internal double Now;
        internal Fixture(int count = 3)
        {
            Peers = new Peer[count];
            for (int i = 0; i < count; i++)
            {
                var peer = new Peer(); Peers[i] = peer;
                peer.Transport = i == 0 ? Network.Host : Network.Connect();
                peer.Room = new RoomSession(peer.Transport, new SessionProfile { Name = "Voice" + i,
                    GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Voice tests",
                    reconnect: i == 0 ? null : () => Network.Connect(),
                    options: new SessionOptions { RetryJitter = () => .5 });
                peer.Voice = new RoomVoiceSession(peer.Room);
            }
            Until(() => Peers.All(p => p.Room.Joined && p.Room.Players.Length == count), "voice fixture authentication");
        }
        internal void Step(double seconds = .01)
        {
            Now += seconds;
            foreach (var peer in Peers) peer.Voice.Pump(Now);
            foreach (var peer in Peers) peer.Room.Pump(Now);
        }
        internal void Advance(double seconds)
        { double end = Now + seconds; while (Now < end) Step(Math.Min(.01, end - Now)); }
        internal void Until(Func<bool> condition, string message)
        {
            for (int i = 0; i < 1000; i++) { Step(); if (condition()) return; }
            throw new Exception("Voice fixture timeout: " + message);
        }
        internal void Raw(int sender, Guid stream, uint sequence, int bytes = 20, string roomId = null, string claimedSender = null)
        {
            byte[] voice = RoomVoiceWire.Encode(roomId ?? Peers[sender].Room.RoomId, stream, sequence, new byte[bytes], bytes);
            if (!Peers[sender].Transport.SendRealtime("host", Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = claimedSender ?? Peers[sender].Room.LocalPlayerId, Payload = voice })))
                throw new Exception("Voice raw test transport send rejected.");
        }
        internal void ClearQueues()
        { foreach (var peer in Peers) { RoomVoiceFrame frame; while (peer.Voice.TryReceive(out frame)) { } } }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        Codec(check); RoutingAndAuthentication(check); OrderingAndQueueBounds(check);
        RateAndSpeakerBounds(check); DisconnectAndLeave(check); PcmBuffer(check);
    }

    private static void Codec(Action<bool, string> check)
    {
        string room = Guid.NewGuid().ToString("N"); var stream = Guid.NewGuid();
        byte[] source = new byte[] { 1, 3, 5, 7 };
        byte[] bytes = RoomVoiceWire.Encode(room, stream, 1, source, source.Length);
        RoomVoiceFrame frame;
        check(RoomVoiceWire.TryDecode(bytes, room, out frame) && frame.StreamId == stream && frame.Sequence == 1 &&
            frame.Compressed.SequenceEqual(source), "compressed voice codec preserves room stream sequence and payload");
        source[0] = 99;
        check(frame.Compressed[0] == 1, "voice frame owns its compressed bytes instead of capture buffer reference");
        for (int length = 0; length < bytes.Length; length++)
            check(!RoomVoiceWire.TryDecode(bytes.Take(length).ToArray(), room, out frame), "truncated voice payload rejected at length " + length);
        check(!RoomVoiceWire.TryDecode(bytes.Concat(new byte[] { 0 }).ToArray(), room, out frame), "trailing voice bytes rejected");
        check(!RoomVoiceWire.TryDecode(bytes, Guid.NewGuid().ToString("N"), out frame), "voice from different room rejected");
        var bad = (byte[])bytes.Clone(); bad[41] = 0; bad[42] = 0;
        check(!RoomVoiceWire.TryDecode(bad, room, out frame), "zero compressed length rejected");
        bad = (byte[])bytes.Clone(); bad[37] = bad[38] = bad[39] = bad[40] = 0;
        check(!RoomVoiceWire.TryDecode(bad, room, out frame), "zero voice sequence rejected");
        check(!RoomVoiceWire.IsVoicePayload(new byte[43 + RoomVoiceWire.MaxCompressedBytes + 1]), "oversized compressed voice rejected before allocation");
        bool failed = false;
        try { RoomVoiceWire.Encode(room, stream, 1, new byte[RoomVoiceWire.MaxCompressedBytes + 1], RoomVoiceWire.MaxCompressedBytes + 1); }
        catch (ArgumentException) { failed = true; }
        check(failed, "capture cannot encode oversized compressed frame");
        byte[] protocol = Protocol.Encode(new Packet { Kind = PacketKind.Ping, Sequence = 1, MessageId = 1 });
        check(protocol[4] == (byte)Protocol.Version && protocol[5] == (byte)(Protocol.Version >> 8), "realtime framing uses the current protocol version");
        protocol[4] = 8;
        failed = false; try { Protocol.Decode(protocol); } catch (InvalidDataException) { failed = true; }
        check(failed, "old protocol eight rejected before realtime interpretation");
        using (var network = new Network())
        {
            var transport = network.Connect();
            using (var guest = new RoomSession(transport, new SessionProfile { Name = "New voice client",
                GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, ""))
            {
                byte[] oldChallenge = Protocol.Encode(new Packet { Kind = PacketKind.Challenge,
                    Room = Guid.NewGuid().ToString("N"), Nonce = Protocol.NewNonce() });
                oldChallenge[4] = 8;
                network.Host.Send(transport.Key, oldChallenge); guest.Pump(.01);
                check(guest.Closed && guest.LastError == "protocol_mismatch",
                    "new client reports protocol mismatch for old room instead of generic invalid packet");
            }
        }
    }

    private static void RoutingAndAuthentication(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            var host = fixture.Peers[0]; var sender = fixture.Peers[1]; var receiver = fixture.Peers[2];
            check(sender.Voice.Send(new byte[] { 2, 4 }, 2), "authenticated participant can submit compressed speech");
            fixture.Step();
            RoomVoiceFrame hostFrame, receiverFrame, echo;
            check(host.Voice.TryReceive(out hostFrame) && receiver.Voice.TryReceive(out receiverFrame) &&
                hostFrame.PlayerId == sender.Room.LocalPlayerId && receiverFrame.PlayerId == sender.Room.LocalPlayerId,
                "host relays authenticated speaker identity to another guest");
            check(!sender.Voice.TryReceive(out echo), "sender does not receive its own relayed speech");
            fixture.Raw(1, Guid.NewGuid(), 1, claimedSender: receiver.Room.LocalPlayerId); fixture.Step();
            check(receiver.Voice.TryReceive(out receiverFrame) && receiverFrame.PlayerId == sender.Room.LocalPlayerId,
                "guest cannot impersonate another speaker with application PlayerId");
            fixture.ClearQueues();
            fixture.Raw(1, Guid.NewGuid(), 1, roomId: Guid.NewGuid().ToString("N")); fixture.Step();
            check(host.Voice.QueuedFrames == 0 && receiver.Voice.QueuedFrames == 0, "foreign room voice is neither queued nor relayed");
            int applications = 0; host.Room.ApplicationMessageReceived += (_, data) => applications++;
            int chats = host.Room.Chat.Length;
            sender.Transport.SendRealtime("host", Protocol.Encode(new Packet { Kind = PacketKind.ChatSubmit, MessageId = 1, Text = "forged realtime control" }));
            sender.Transport.SendRealtime("host", Protocol.Encode(new Packet { Kind = PacketKind.Application, PlayerId = sender.Room.LocalPlayerId, Payload = new byte[] { 1, 2 } }));
            fixture.Step();
            check(applications == 0 && host.Room.Chat.Length == chats && sender.Room.Joined,
                "realtime gate drops control and nonvoice application without disconnecting reliable room");
            check(sender.Room.SendChat("reliable chat beside voice"), "text chat submission still accepted beside realtime traffic");
            fixture.Until(() => receiver.Room.Chat.Any(c => c.Text == "reliable chat beside voice"), "reliable chat delivery");
            check(receiver.Room.Chat.Last().PlayerId == sender.Room.LocalPlayerId, "reliable chat remains authenticated and independent");
        }
        using (var fixture = new Fixture(1))
        {
            var unjoined = fixture.Network.Connect();
            var voice = RoomVoiceWire.Encode(fixture.Peers[0].Room.RoomId, Guid.NewGuid(), 1, new byte[] { 1 }, 1);
            unjoined.SendRealtime("host", Protocol.Encode(new Packet { Kind = PacketKind.Application, PlayerId = Guid.NewGuid().ToString("N"), Payload = voice }));
            fixture.Step();
            check(fixture.Peers[0].Voice.QueuedFrames == 0 && fixture.Peers[0].Room.Players.Length == 1,
                "transport connection alone cannot inject voice before room authentication");
        }
    }

    private static void OrderingAndQueueBounds(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            var stream = Guid.NewGuid();
            for (uint seq = 1; seq <= 5; seq++) fixture.Raw(1, stream, seq);
            fixture.Step();
            check(fixture.Peers[0].Voice.QueuedFrames == 3 && fixture.Peers[2].Voice.QueuedFrames == 3,
                "receiver and host retain at most three recent frames per speaker");
            var sequences = new List<uint>(); RoomVoiceFrame frame;
            while (fixture.Peers[2].Voice.TryReceive(out frame)) sequences.Add(frame.Sequence);
            check(sequences.SequenceEqual(new uint[] { 3, 4, 5 }), "backlogged voice drops oldest speech instead of latest speech");
            fixture.ClearQueues();
            fixture.Raw(1, stream, 5); fixture.Raw(1, stream, 4); fixture.Step();
            check(fixture.Peers.All(p => p.Voice.QueuedFrames == 0), "duplicate and reordered old voice are not replayed");
            var replacement = Guid.NewGuid(); fixture.Raw(1, replacement, 1); fixture.Step();
            check(fixture.Peers[2].Voice.TryReceive(out frame) && frame.StreamId == replacement, "new capture stream starts at a fresh sequence");
            fixture.ClearQueues(); fixture.Raw(1, stream, 6); fixture.Step();
            check(fixture.Peers.All(p => p.Voice.QueuedFrames == 0), "late retired stream cannot revive old speech");
            fixture.Raw(1, replacement, 2); fixture.Step(); fixture.Advance(.31);
            check(fixture.Peers.All(p => p.Voice.QueuedFrames == 0), "unconsumed voice expires after three hundred milliseconds");
        }
    }

    private static void RateAndSpeakerBounds(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            var stream = Guid.NewGuid(); int before = fixture.Peers[0].Transport.RealtimeSent.Count;
            for (uint seq = 1; seq <= 30; seq++) fixture.Raw(1, stream, seq);
            fixture.Step();
            check(fixture.Peers[0].Transport.RealtimeSent.Count - before == RoomVoiceSession.MaxPacketsPerSecond,
                "host relays at most twelve compressed voice packets per speaker per second");
            fixture.Advance(1.01); before = fixture.Peers[0].Transport.RealtimeSent.Count;
            for (uint seq = 31; seq <= 60; seq++) fixture.Raw(1, stream, seq, RoomVoiceWire.MaxCompressedBytes);
            fixture.Step();
            check(fixture.Peers[0].Transport.RealtimeSent.Count - before == RoomVoiceSession.MaxBytesPerSecond / RoomVoiceWire.MaxCompressedBytes,
                "host enforces thirty two KiB per speaker per second independently of packet count");
            fixture.Advance(1.01);
            var sender = fixture.Peers[1]; int sent = 0;
            for (int i = 0; i < 100; i++)
            { if (sender.Voice.Send(new byte[RoomVoiceWire.MaxCompressedBytes], RoomVoiceWire.MaxCompressedBytes)) sent++; fixture.Step(.005); }
            check(sent <= RoomVoiceSession.MaxBytesPerSecond / RoomVoiceWire.MaxCompressedBytes && sent > 0,
                "capture side rate budget limits its own traffic before transport enqueue");
        }
        using (var fixture = new Fixture(RoomVoiceSession.MaxSpeakers + 2))
        {
            for (int i = 1; i < fixture.Peers.Length; i++) fixture.Raw(i, Guid.NewGuid(), 1);
            fixture.Step();
            check(fixture.Peers[0].Voice.QueuedFrames == RoomVoiceSession.MaxSpeakers,
                "simultaneous authenticated speakers cannot exceed eight bounded voice states");
        }
    }

    private static void DisconnectAndLeave(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            var receiver = fixture.Peers[2];
            check(fixture.Peers[0].Voice.Send(new byte[] { 1 }, 1), "host voice sends to connected guests"); fixture.Step();
            check(receiver.Voice.QueuedFrames == 1, "disconnect fixture has pending received speech");
            receiver.Transport.Disconnect("host"); fixture.Step();
            check(!receiver.Voice.Ready && receiver.Voice.QueuedFrames == 0 && !receiver.Voice.Send(new byte[] { 1 }, 1),
                "connection loss immediately clears queued speech and blocks capture submission");
            fixture.Until(() => receiver.Room.Joined && !receiver.Room.Reconnecting, "room resume");
            check(receiver.Voice.QueuedFrames == 0, "rejoining room does not replay captured audio history");
            check(receiver.Room.RequestLeave(), "explicit leave request accepted");
            check(!receiver.Voice.Ready && receiver.Voice.QueuedFrames == 0 && !receiver.Voice.Send(new byte[] { 1 }, 1),
                "leaving stops voice immediately without waiting for leave acknowledgement");
        }
    }

    private static void PcmBuffer(Action<bool, string> check)
    {
        var buffer = new VoicePcmBuffer(4);
        buffer.Write(Pcm(100, 200, 300), 6); buffer.Write(Pcm(400, 500, 600), 6);
        check(buffer.Count == 4, "PCM playback ring retains only its bounded sample capacity");
        var output = new float[6]; buffer.Read(output);
        check(output.Take(4).Select(value => (int)Math.Round(value * 32768)).SequenceEqual(new int[] { 300, 400, 500, 600 }) &&
            output[4] == 0 && output[5] == 0, "PCM overflow drops old samples and underrun produces silence");
        buffer.Write(Pcm(-32768, 32767, 0, 10, 20, 30), 12); output = new float[4]; buffer.Read(output);
        check(output.Select(value => (int)Math.Round(value * 32768)).SequenceEqual(new int[] { 0, 10, 20, 30 }),
            "one oversized decoded block keeps newest tail rather than delayed prefix");
        buffer.Write(Pcm(-32768, 32767), 4); output = new float[2]; buffer.Read(output);
        check(output[0] == -1 && output[1] > .999f && output[1] < 1, "signed mono sixteen bit PCM maps safely to Unity sample range");
        buffer.Write(Pcm(123), 2); buffer.Clear(); output = new float[] { 1, 1 }; buffer.Read(output);
        check(buffer.Count == 0 && output.All(value => value == 0), "room teardown erases decoded sample data instead of replaying it");
        bool failed = false; try { buffer.Write(new byte[3], 3); } catch (ArgumentException) { failed = true; }
        check(failed, "odd PCM byte length rejected before sample conversion");
    }

    private static byte[] Pcm(params short[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) { bytes[i * 2] = (byte)values[i]; bytes[i * 2 + 1] = (byte)(values[i] >> 8); }
        return bytes;
    }
}
