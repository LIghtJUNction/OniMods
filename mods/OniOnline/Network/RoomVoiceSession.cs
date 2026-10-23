using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OniOnline.Network
{
    internal sealed class RoomVoiceFrame
    {
        internal string PlayerId;
        internal Guid StreamId;
        internal uint Sequence;
        internal byte[] Compressed;
        internal double ReceivedAt;
    }

    internal static class RoomVoiceWire
    {
        internal const int MaxCompressedBytes = 4096;
        private const int HeaderBytes = 43;

        internal static bool IsVoicePayload(byte[] data)
        {
            return data != null && data.Length > HeaderBytes && data.Length <= HeaderBytes + MaxCompressedBytes &&
                data[0] == (byte)'O' && data[1] == (byte)'N' && data[2] == (byte)'I' && data[3] == (byte)'V' && data[4] == 1;
        }

        internal static byte[] Encode(string roomId, Guid streamId, uint sequence, byte[] compressed, int count)
        {
            Guid room;
            if (!Guid.TryParseExact(roomId, "N", out room) || streamId == Guid.Empty || sequence == 0 ||
                compressed == null || count < 1 || count > MaxCompressedBytes || count > compressed.Length)
                throw new ArgumentException("invalid_voice_frame");
            using (var stream = new MemoryStream(HeaderBytes + count))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'O', (byte)'N', (byte)'I', (byte)'V', 1 });
                writer.Write(room.ToByteArray()); writer.Write(streamId.ToByteArray());
                writer.Write(sequence); writer.Write((ushort)count); writer.Write(compressed, 0, count);
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] data, string roomId, out RoomVoiceFrame frame)
        {
            frame = null;
            if (!IsVoicePayload(data)) return false;
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream))
            {
                reader.ReadBytes(5);
                if (new Guid(reader.ReadBytes(16)).ToString("N") != roomId) return false;
                var streamId = new Guid(reader.ReadBytes(16));
                uint sequence = reader.ReadUInt32();
                int count = reader.ReadUInt16();
                if (streamId == Guid.Empty || sequence == 0 || count < 1 || count > MaxCompressedBytes ||
                    count != stream.Length - stream.Position) return false;
                frame = new RoomVoiceFrame { StreamId = streamId, Sequence = sequence, Compressed = reader.ReadBytes(count) };
                return true;
            }
        }
    }

    // Compressed speech is transient room data: no history, acknowledgements or retries.
    // The transport authenticates the speaker; the payload cannot choose its sender.
    internal sealed class RoomVoiceSession : IDisposable
    {
        internal const int MaxSpeakers = 8;
        internal const int MaxPacketsPerSecond = 12;
        internal const int MaxBytesPerSecond = 32 * 1024;
        internal const double QueueLifetimeSeconds = .3;
        private sealed class Speaker
        {
            internal Guid Stream;
            internal uint Highest;
            internal readonly HashSet<Guid> Retired = new HashSet<Guid>();
            internal readonly Queue<Guid> RetiredOrder = new Queue<Guid>();
            internal double Window;
            internal int Packets;
            internal int Bytes;
            internal double LastReceived;
        }

        private readonly RoomSession room;
        private readonly Dictionary<string, Speaker> speakers = new Dictionary<string, Speaker>(StringComparer.Ordinal);
        private readonly Queue<RoomVoiceFrame> incoming = new Queue<RoomVoiceFrame>();
        private Guid streamId = Guid.NewGuid();
        private uint sequence;
        private double now;
        private double sendWindow;
        private double nextSend;
        private int sentPackets;
        private int sentBytes;
        private bool wasReady;
        private bool disposed;

        internal bool Ready { get { return !disposed && room.Joined && !room.Closed && !room.Reconnecting && !room.Leaving; } }
        internal int QueuedFrames { get { return incoming.Count; } }

        internal RoomVoiceSession(RoomSession room)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            wasReady = Ready;
            room.ApplicationMessageReceived += Receive;
            room.Changed += Synchronize;
        }

        internal void Pump(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now) throw new ArgumentOutOfRangeException(nameof(seconds));
            now = seconds;
            Synchronize();
            while (incoming.Count != 0 && now - incoming.Peek().ReceivedAt > QueueLifetimeSeconds) incoming.Dequeue();
        }

        private void Synchronize()
        {
            bool ready = Ready;
            if (!ready)
            {
                incoming.Clear(); speakers.Clear();
                if (wasReady) { streamId = Guid.NewGuid(); sequence = 0; }
            }
            else
            {
                var connected = new HashSet<string>(room.Players.Where(p => p.Connected).Select(p => p.Id), StringComparer.Ordinal);
                foreach (string peer in speakers.Keys.Where(p => !connected.Contains(p)).ToArray()) speakers.Remove(peer);
                TrimIncoming(frame => connected.Contains(frame.PlayerId));
            }
            wasReady = ready;
        }

        internal bool Send(byte[] compressed, int count)
        {
            if (!Ready || compressed == null || count < 1 || count > RoomVoiceWire.MaxCompressedBytes || count > compressed.Length || now < nextSend)
                return false;
            if (now - sendWindow >= 1) { sendWindow = now; sentPackets = 0; sentBytes = 0; }
            if (sentPackets >= MaxPacketsPerSecond || sentBytes + count > MaxBytesPerSecond) return false;
            if (sequence == uint.MaxValue) { streamId = Guid.NewGuid(); sequence = 0; }
            byte[] data = RoomVoiceWire.Encode(room.RoomId, streamId, ++sequence, compressed, count);
            sentPackets++; sentBytes += count; nextSend = now + .09;
            if (!room.IsHost) return room.SendRealtimeApplication(data);
            bool sent = false;
            foreach (var peer in room.Players)
                if (peer.Connected && peer.Id != room.LocalPlayerId)
                    sent |= room.ForwardRealtimeApplication(room.LocalPlayerId, peer.Id, data);
            return sent;
        }

        private void Receive(string sender, byte[] data)
        {
            if (!Ready || sender == room.LocalPlayerId || !room.Players.Any(p => p.Id == sender && p.Connected)) return;
            RoomVoiceFrame frame;
            if (!RoomVoiceWire.TryDecode(data, room.RoomId, out frame)) return;
            Speaker speaker;
            if (!speakers.TryGetValue(sender, out speaker))
            {
                foreach (string expired in speakers.Where(p => now - p.Value.LastReceived > 2).Select(p => p.Key).ToArray()) speakers.Remove(expired);
                if (speakers.Count >= MaxSpeakers) return;
                speakers.Add(sender, speaker = new Speaker { Stream = frame.StreamId, Window = now });
            }
            if (now - speaker.Window >= 1) { speaker.Window = now; speaker.Packets = 0; speaker.Bytes = 0; }
            if (speaker.Packets >= MaxPacketsPerSecond || speaker.Bytes + frame.Compressed.Length > MaxBytesPerSecond) return;
            if (speaker.Stream != frame.StreamId)
            {
                if (speaker.Retired.Contains(frame.StreamId) || frame.Sequence > 4) return;
                speaker.Retired.Add(speaker.Stream); speaker.RetiredOrder.Enqueue(speaker.Stream);
                while (speaker.RetiredOrder.Count > 8) speaker.Retired.Remove(speaker.RetiredOrder.Dequeue());
                speaker.Stream = frame.StreamId; speaker.Highest = 0;
                TrimIncoming(old => old.PlayerId != sender);
            }
            if (frame.Sequence <= speaker.Highest) return;
            speaker.Highest = frame.Sequence; speaker.Packets++; speaker.Bytes += frame.Compressed.Length; speaker.LastReceived = now;
            frame.PlayerId = sender; frame.ReceivedAt = now;
            int queued = incoming.Count(f => f.PlayerId == sender);
            if (queued >= 3)
            {
                bool removed = false;
                TrimIncoming(old =>
                {
                    if (old.PlayerId != sender || removed) return true;
                    removed = true; return false;
                });
            }
            while (incoming.Count >= MaxSpeakers * 3) incoming.Dequeue();
            incoming.Enqueue(frame);
            if (room.IsHost)
                foreach (var peer in room.Players)
                    if (peer.Connected && peer.Id != sender && peer.Id != room.LocalPlayerId)
                        room.ForwardRealtimeApplication(sender, peer.Id, data);
        }

        private void TrimIncoming(Func<RoomVoiceFrame, bool> keep)
        {
            int count = incoming.Count;
            for (int i = 0; i < count; i++)
            {
                var frame = incoming.Dequeue();
                if (keep(frame)) incoming.Enqueue(frame);
            }
        }

        internal bool TryReceive(out RoomVoiceFrame frame)
        {
            while (incoming.Count != 0)
            {
                frame = incoming.Dequeue();
                if (Ready && now - frame.ReceivedAt <= QueueLifetimeSeconds) return true;
            }
            frame = null; return false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; room.ApplicationMessageReceived -= Receive; room.Changed -= Synchronize;
            incoming.Clear(); speakers.Clear();
        }
    }
}
