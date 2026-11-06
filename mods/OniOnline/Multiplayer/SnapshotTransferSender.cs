using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Multiplayer
{
    // Main-thread, bounded stop-and-window sender. Transport success only means
    // queued locally; only the canonical recipient's acknowledgement advances it.
    public sealed class SnapshotTransferSender
    {
        public const int MaxEncodedBytes = 32 * 1024 * 1024;
        public const int MaxLaneBytes = 2 * 1024 * 1024;
        public const int MaxInFlightBytes = 1024 * 1024;
        // Bound queued bulk data per recipient to ~9KB. Eight 4.4KB chunks
        // delayed chat/time control for >15 seconds on a 2KB/s connection.
        public const int TargetWindow = 2;
        public const int PumpBudget = 4;
        public const double RetransmitSeconds = 45;
        private const int LaneStateBytes = 1024;
        private const int FrameStateBytes = 256;
        private const uint AckMagic = 0x41494E4F; // ONIA, little endian.
        private const byte AckVersion = 1;
        private const ushort CompleteIndex = ushort.MaxValue;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private sealed class EncodedFrame
        {
            internal string Owner;
            internal string SnapshotId;
            internal string WorldId;
            internal string Epoch;
            internal long Sequence;
            internal long ControlRevision;
            internal byte[] Descriptor;
            internal byte[][] Chunks;
            internal int Bytes;
        }

        private sealed class Frame
        {
            internal EncodedFrame Data;
            internal bool DescriptorSent;
            internal bool[] Sent;
            internal bool[] EverSent;
            internal bool[] Acked;
            internal double LastProgress;
            internal int StalledRounds;
            internal int MissingCompleteRounds;
            internal int InFlight;
            internal int InFlightBytes;
        }

        private sealed class Lane
        {
            internal string Target;
            internal string Owner;
            internal Frame Active;
            internal EncodedFrame Standby;
            internal long LastSequence;
            internal string Epoch;
            internal long ControlRevision;
        }

        private readonly Func<string, string, byte[], bool> send;
        private readonly int encodedBudget;
        private readonly int laneBudget;
        private readonly int inFlightBudget;
        private readonly Dictionary<string, Dictionary<string, Lane>> targets = new Dictionary<string, Dictionary<string, Lane>>(StringComparer.Ordinal);
        private readonly Dictionary<string, EncodedFrame> latestEncoding = new Dictionary<string, EncodedFrame>(StringComparer.Ordinal);
        private double clock;
        private int cursor;
        private int inFlightBytes;
        public event Action<string, string> TransferExpired;
        public int PendingCount { get { return targets.Values.Sum(owners => owners.Values.Sum(lane => (lane.Active == null ? 0 : 1) + (lane.Standby == null ? 0 : 1))); } }
        public string BusyReason { get; private set; }
        public long EncodedBytes { get { long encoded, lanes; Measure(null, null, out encoded, out lanes); return encoded; } }
        public long LaneBytes { get { long encoded, lanes; Measure(null, null, out encoded, out lanes); return lanes; } }
        public int InFlightBytes { get { return inFlightBytes; } }


        public SnapshotTransferSender(Func<string, string, byte[], bool> send,
            int encodedBudgetBytes = MaxEncodedBytes, int laneBudgetBytes = MaxLaneBytes, int inFlightBudgetBytes = MaxInFlightBytes)
        {
            this.send = send ?? throw new ArgumentNullException("send");
            if (encodedBudgetBytes < 1024 || laneBudgetBytes < 1024 || inFlightBudgetBytes < WorldWire.ChunkDataBytes + 512)
                throw new ArgumentOutOfRangeException("transfer_budget");
            encodedBudget = encodedBudgetBytes; laneBudget = laneBudgetBytes; inFlightBudget = inFlightBudgetBytes;
        }

        public void Queue(string targetId, WorldSnapshot snapshot, double now)
        { TryQueue(targetId, snapshot, now); }

        // Resource pressure is retryable. A failed admission never advances the
        // sequence watermark, replaces standby data or retains a new target.
        public bool TryQueue(string targetId, WorldSnapshot snapshot, double now)
        {
            SetTime(now);
            BusyReason = null;
            ValidateId(targetId);
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            WorldWire.ValidateDescriptor(snapshot.World);
            if (snapshot.Sequence < 1) throw new InvalidDataException("invalid_snapshot");
            string owner = snapshot.World.OwnerId;
            Dictionary<string, Lane> owners;
            Lane lane;
            if (targets.TryGetValue(targetId, out owners) && owners.TryGetValue(owner, out lane))
            {
                if (lane.Epoch != snapshot.World.Epoch || lane.ControlRevision != snapshot.World.ControlRevision)
                    throw new InvalidDataException("clear_owner_before_epoch_change");
                if (snapshot.Sequence <= lane.LastSequence) return true;
            }
            else lane = null;
            EncodedFrame encoded;
            if (!latestEncoding.TryGetValue(owner, out encoded) || encoded.Sequence != snapshot.Sequence ||
                encoded.WorldId != snapshot.World.Id || encoded.Epoch != snapshot.World.Epoch || encoded.ControlRevision != snapshot.World.ControlRevision)
            {
                var chunks = WorldWire.EncodeSnapshot(snapshot).ToArray();
                WorldMessage first;
                if (chunks.Length == 0 || !WorldWire.TryDecode(chunks[0], out first)) throw new InvalidDataException("invalid_snapshot");
                encoded = new EncodedFrame { Owner = owner, WorldId = snapshot.World.Id, SnapshotId = first.Chunk.SnapshotId, Epoch = snapshot.World.Epoch,
                    ControlRevision = snapshot.World.ControlRevision, Sequence = snapshot.Sequence,
                    Descriptor = WorldWire.EncodeDescriptor(snapshot.World), Chunks = chunks };
                encoded.Bytes = checked(encoded.Descriptor.Length + chunks.Sum(chunk => chunk.Length + 32) + 256);
            }
            long encodedBytes, laneBytes;
            Measure(lane, encoded, out encodedBytes, out laneBytes);
            if (laneBytes > laneBudget)
            {
                // Completed watermarks are useful but may not keep new observers
                // out forever. The receiver still guards against replayed frames.
                foreach (var target in targets.ToArray())
                {
                    foreach (var old in target.Value.Where(pair => pair.Value != lane && pair.Value.Active == null && pair.Value.Standby == null).Select(pair => pair.Key).ToArray())
                        target.Value.Remove(old);
                    if (target.Value.Count == 0) targets.Remove(target.Key);
                }
                targets.TryGetValue(targetId, out owners);
                Measure(lane, encoded, out encodedBytes, out laneBytes);
            }
            if (encodedBytes > encodedBudget) { BusyReason = "snapshot_encoding_budget"; return false; }
            if (laneBytes > laneBudget) { BusyReason = "snapshot_lane_budget"; return false; }
            if (lane == null)
            {
                if (owners == null) { owners = new Dictionary<string, Lane>(StringComparer.Ordinal); targets.Add(targetId, owners); }
                lane = new Lane { Target = targetId, Owner = owner, Epoch = snapshot.World.Epoch, ControlRevision = snapshot.World.ControlRevision };
                owners.Add(owner, lane);
            }
            lane.LastSequence = snapshot.Sequence;
            if (lane.Active == null) lane.Active = Begin(encoded, now);
            else lane.Standby = encoded;
            latestEncoding[owner] = encoded;
            PruneEncoding();
            return true;
        }

        public void Pump(double now)
        {
            SetTime(now);
            var lanes = targets.Values.SelectMany(value => value.Values).ToArray();
            foreach (var lane in lanes)
            {
                Frame frame = lane.Active;
                if (frame == null || now - frame.LastProgress < RetransmitSeconds) continue;
                bool completeAckMissing = frame.Acked.All(value => value);
                if (completeAckMissing) frame.MissingCompleteRounds++;
                else frame.StalledRounds++;
                frame.LastProgress = now;
                if (frame.StalledRounds >= 3 || frame.MissingCompleteRounds >= 3)
                {
                    Finish(lane, now);
                    if (TransferExpired != null) TransferExpired(lane.Target, lane.Owner);
                    continue;
                }
                frame.DescriptorSent = false;
                for (int i = 0; i < frame.Sent.Length; i++)
                {
                    if (completeAckMissing) frame.Acked[i] = false;
                    if (!frame.Acked[i]) frame.Sent[i] = false;
                }
                ReleaseInFlight(frame);
            }
            int attempts = 0;
            int withoutWork = 0;
            while (lanes.Length > 0 && attempts < PumpBudget && withoutWork < lanes.Length)
            {
                Lane lane = lanes[cursor++ % lanes.Length];
                if (cursor == int.MaxValue) cursor = 0;
                Frame frame = lane.Active;
                if (frame == null) { withoutWork++; continue; }
                byte[] payload;
                int index = -1;
                if (!frame.DescriptorSent) payload = frame.Data.Descriptor;
                else
                {
                    int inFlight = targets[lane.Target].Values.Where(value => value.Active != null)
                        .Sum(value => value.Active.InFlight);
                    if (inFlight >= TargetWindow) { withoutWork++; continue; }
                    for (int i = 0; i < frame.Sent.Length; i++)
                        if (!frame.Sent[i] && !frame.Acked[i]) { index = i; break; }
                    if (index < 0) { withoutWork++; continue; }
                    payload = frame.Data.Chunks[index];
                    if (inFlightBytes > inFlightBudget - payload.Length) { withoutWork++; continue; }
                }
                withoutWork = 0;
                attempts++;
                // Mark before callback so a synchronous test/adapter ACK is valid.
                bool wasEverSent = index >= 0 && frame.EverSent[index];
                if (index < 0) frame.DescriptorSent = true;
                else
                {
                    frame.Sent[index] = true; frame.EverSent[index] = true; frame.InFlight++;
                    frame.InFlightBytes += payload.Length; inFlightBytes += payload.Length;
                }
                bool accepted = send(lane.Target, lane.Owner, payload);
                if (!accepted && lane.Active == frame)
                {
                    if (index < 0) frame.DescriptorSent = false;
                    else
                    {
                        if (frame.Sent[index] && !frame.Acked[index])
                        { frame.InFlight--; frame.InFlightBytes -= payload.Length; inFlightBytes -= payload.Length; }
                        frame.Sent[index] = false;
                        frame.EverSent[index] = wasEverSent;
                    }
                }
            }
        }

        public bool AcceptAck(string canonicalSenderId, byte[] payload, double now)
        {
            if (!WorldWire.Finite(now) || now < clock) return false;
            string owner, snapshotId;
            ushort index;
            if (!ReadAck(payload, out owner, out snapshotId, out index)) return false;
            Dictionary<string, Lane> owners;
            Lane lane;
            if (canonicalSenderId == null || !targets.TryGetValue(canonicalSenderId, out owners) || !owners.TryGetValue(owner, out lane)) return false;
            Frame frame = lane.Active;
            if (frame == null || frame.Data.SnapshotId != snapshotId) return false;
            if (index == CompleteIndex)
            {
                if (!frame.EverSent.All(value => value)) return false;
                clock = now;
                Finish(lane, now);
                return true;
            }
            if (index >= frame.Acked.Length || !frame.EverSent[index]) return false;
            clock = now;
            if (!frame.Acked[index])
            {
                if (frame.Sent[index])
                {
                    frame.InFlight--;
                    int bytes = frame.Data.Chunks[index].Length;
                    frame.InFlightBytes -= bytes; inFlightBytes -= bytes;
                }
                frame.Acked[index] = true;
                // A receiver may have timed out and discarded its earlier parts.
                // Unique ACKs in a full replay are real rebuilding progress. Keep
                // the missing-complete rounds separately so duplicate completions
                // can never prolong an unconfirmed frame forever.
                frame.LastProgress = now;
                frame.StalledRounds = 0;
            }
            return true;
        }

        public static byte[] EncodeAck(WorldChunk chunk, bool complete)
        {
            if (chunk == null) throw new ArgumentNullException("chunk");
            string owner = WorldWire.OwnerOf(chunk.WorldId);
            ValidateSnapshotId(chunk.SnapshotId);
            if (chunk.Index < 0 || chunk.Index >= CompleteIndex || chunk.Count < 1 || chunk.Index >= chunk.Count)
                throw new InvalidDataException("invalid_chunk_ack");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(AckMagic); writer.Write(AckVersion);
                WriteId(writer, owner); WriteId(writer, chunk.SnapshotId);
                writer.Write(complete ? CompleteIndex : (ushort)chunk.Index);
                return stream.ToArray();
            }
        }

        public static bool IsAck(byte[] payload)
        { string owner, snapshot; ushort index; return ReadAck(payload, out owner, out snapshot, out index); }

        public bool HasPending(string ownerId)
        { return ownerId != null && targets.Values.Any(value => value.ContainsKey(ownerId) && value[ownerId].Active != null); }

        public void ClearOwner(string ownerId)
        {
            if (ownerId == null) return;
            foreach (var entry in targets.ToArray())
            {
                Lane lane;
                if (entry.Value.TryGetValue(ownerId, out lane)) { ReleaseInFlight(lane.Active); lane.Active = null; lane.Standby = null; }
                entry.Value.Remove(ownerId);
                if (entry.Value.Count == 0) targets.Remove(entry.Key);
            }
            latestEncoding.Remove(ownerId);
        }

        public void ClearTarget(string targetId)
        {
            if (targetId == null) return;
            Dictionary<string, Lane> owners;
            if (targets.TryGetValue(targetId, out owners))
                foreach (var lane in owners.Values) { ReleaseInFlight(lane.Active); lane.Active = null; lane.Standby = null; }
            targets.Remove(targetId);
            PruneEncoding();
        }

        public void Reset()
        {
            foreach (var lane in targets.Values.SelectMany(owners => owners.Values)) { lane.Active = null; lane.Standby = null; }
            targets.Clear(); latestEncoding.Clear(); cursor = 0; clock = 0; inFlightBytes = 0; BusyReason = null;
        }

        private static Frame Begin(EncodedFrame encoded, double now)
        {
            return new Frame { Data = encoded, Sent = new bool[encoded.Chunks.Length], EverSent = new bool[encoded.Chunks.Length],
                Acked = new bool[encoded.Chunks.Length], LastProgress = now };
        }

        private void Finish(Lane lane, double now)
        {
            ReleaseInFlight(lane.Active);
            lane.Active = lane.Standby == null ? null : Begin(lane.Standby, now);
            lane.Standby = null;
            PruneEncoding();
        }

        private void PruneEncoding()
        {
            foreach (var entry in latestEncoding.ToArray())
                if (!targets.Values.SelectMany(value => value.Values).Any(lane => lane.Active?.Data == entry.Value || lane.Standby == entry.Value))
                    latestEncoding.Remove(entry.Key);
        }

        private void ReleaseInFlight(Frame frame)
        {
            if (frame == null) return;
            inFlightBytes -= frame.InFlightBytes;
            frame.InFlightBytes = 0; frame.InFlight = 0;
        }

        private void Measure(Lane replacing, EncodedFrame candidate, out long encodedBytes, out long laneBytes)
        {
            var frames = new HashSet<EncodedFrame>();
            laneBytes = 0;
            bool found = false;
            foreach (var lane in targets.Values.SelectMany(value => value.Values))
            {
                bool replace = lane == replacing && candidate != null;
                if (replace) found = true;
                var active = replace && lane.Active == null ? candidate : lane.Active?.Data;
                var standby = replace && lane.Active != null ? candidate : lane.Standby;
                // Conservative managed bookkeeping allowance per lane and per
                // frame's three ACK/sent arrays; payloads are counted once even
                // when forty observers share the same encoded observation.
                laneBytes += LaneStateBytes;
                if (active != null) { frames.Add(active); laneBytes += FrameStateBytes + active.Chunks.Length * 3L; }
                if (standby != null) { frames.Add(standby); laneBytes += FrameStateBytes + standby.Chunks.Length * 3L; }
            }
            if (candidate != null && !found)
            { frames.Add(candidate); laneBytes += LaneStateBytes + FrameStateBytes + candidate.Chunks.Length * 3L; }
            encodedBytes = frames.Sum(frame => (long)frame.Bytes);
        }

        private void SetTime(double now)
        {
            if (!WorldWire.Finite(now) || now < clock) throw new ArgumentOutOfRangeException("now");
            clock = now;
        }

        private static bool ReadAck(byte[] payload, out string owner, out string snapshot, out ushort index)
        {
            owner = null; snapshot = null; index = 0;
            if (payload == null || payload.Length < 13 || payload.Length > 75) return false;
            try
            {
                using (var stream = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadUInt32() != AckMagic || reader.ReadByte() != AckVersion) return false;
                    owner = ReadId(reader); snapshot = ReadId(reader); ValidateSnapshotId(snapshot);
                    index = reader.ReadUInt16();
                    return stream.Position == stream.Length;
                }
            }
            catch (Exception error) when (error is IOException || error is ArgumentException) { return false; }
        }

        private static void ValidateId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) throw new InvalidDataException("invalid_transfer_id");
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.')
                    throw new InvalidDataException("invalid_transfer_id");
        }

        private static void ValidateSnapshotId(string value)
        {
            if (value == null || value.Length != 32) throw new InvalidDataException("invalid_snapshot_id");
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F') && !(c >= '0' && c <= '9')) throw new InvalidDataException("invalid_snapshot_id");
        }

        private static void WriteId(BinaryWriter writer, string value)
        { ValidateId(value); byte[] bytes = Utf8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadId(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count < 1 || count > 32) throw new InvalidDataException("invalid_transfer_id");
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            string value = Utf8.GetString(bytes); ValidateId(value); return value;
        }
    }
}
