using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Sdk
{
    public sealed class ModStateBinding
    {
        public string SessionId { get; }
        public string ModId { get; }
        public string Channel { get; }
        public string AuthorityId { get; }
        public string Epoch { get; }
        public string Generation { get; }
        public int Schema { get; }
        public ModStateBinding(string sessionId, string modId, string channel, string authorityId, string epoch, string generation, int schema)
        {
            if (!ModSyncValidation.Text(modId, 128) || !GameplayFramework.ValidName(channel) || schema < 1)
                throw new ArgumentException("invalid_mod_state_binding");
            SessionId = Nonce(sessionId); ModId = modId; Channel = channel; AuthorityId = Nonce(authorityId);
            Epoch = Nonce(epoch); Generation = Nonce(generation); Schema = schema;
        }
        public ModStateBinding WithGeneration(string generation)
        { return new ModStateBinding(SessionId, ModId, Channel, AuthorityId, Epoch, generation, Schema); }
        internal bool SameScope(ModStateBinding other, bool schema = true, bool generation = true)
        { return other != null && SessionId == other.SessionId && ModId == other.ModId && Channel == other.Channel &&
            AuthorityId == other.AuthorityId && Epoch == other.Epoch && (!generation || Generation == other.Generation) && (!schema || Schema == other.Schema); }
        internal static string Nonce(string value)
        {
            if (value == null || value.Length != 32 || !Guid.TryParseExact(value, "N", out var parsed) || parsed == Guid.Empty)
                throw new ArgumentException("invalid_mod_state_identity");
            return parsed.ToString("N");
        }
    }

    public sealed class ModStateFrame
    {
        public const int MaximumSnapshotBytes = 64 * 1024;
        public const int MaximumDeltaBytes = 3072;
        private readonly byte[] payload;
        public ModStateBinding Binding { get; }
        public long Revision { get; }
        public long BaseRevision { get; }
        public bool IsSnapshot { get; }
        public byte[] Payload { get { return (byte[])payload.Clone(); } }
        internal int PayloadLength { get { return payload.Length; } }
        public ModStateFrame(ModStateBinding binding, long revision, long baseRevision, bool isSnapshot, byte[] payload)
        {
            if (binding == null || revision < 1 || payload == null || payload.Length > (isSnapshot ? MaximumSnapshotBytes : MaximumDeltaBytes) ||
                (isSnapshot ? baseRevision != 0 : baseRevision < 1 || baseRevision == long.MaxValue || revision != baseRevision + 1))
                throw new ArgumentException("invalid_mod_state_frame");
            Binding = binding; Revision = revision; BaseRevision = baseRevision; IsSnapshot = isSnapshot; this.payload = (byte[])payload.Clone();
        }
        internal ModStateFrame ForGeneration(string generation)
        { return new ModStateFrame(Binding.WithGeneration(generation), Revision, BaseRevision, IsSnapshot, payload); }
    }

    // Implementations calculate bytes only. Applying accepted bytes to Unity/native objects is a separate transaction.
    public interface IModStateReducer
    {
        bool ValidateSnapshot(byte[] snapshot);
        byte[] ApplyDelta(byte[] baseline, byte[] delta);
    }

    public sealed class ModStateRecovery
    {
        private readonly ReadOnlyCollection<ModStateFrame> frames;
        public IReadOnlyList<ModStateFrame> Frames { get { return frames; } }
        public bool UpToDate { get { return frames.Count == 0; } }
        public bool UsesSnapshot { get { return frames.Count != 0 && frames[0].IsSnapshot; } }
        internal ModStateRecovery(IEnumerable<ModStateFrame> frames) { this.frames = new List<ModStateFrame>(frames).AsReadOnly(); }
    }

    public sealed class ModStatePublisher
    {
        public const int MaximumHistoryFrames = 32;
        private const int MaximumHistoryBytes = 64 * 1024;
        private readonly IModStateReducer reducer;
        private readonly Queue<ModStateFrame> history = new Queue<ModStateFrame>();
        private int historyBytes;
        private byte[] current;
        public ModStateBinding Binding { get; }
        public long Revision { get; private set; }
        public bool HasSnapshot { get { return current != null; } }
        public event Action<ModStateFrame> Published;
        public ModStatePublisher(ModStateBinding binding, IModStateReducer reducer)
        { Binding = binding ?? throw new ArgumentNullException(nameof(binding)); this.reducer = reducer ?? throw new ArgumentNullException(nameof(reducer)); }
        public static ModStatePublisher Restore(ModStateBinding trustedBinding, IModStateReducer reducer, long revision, byte[] snapshot)
        {
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            var publisher = new ModStatePublisher(trustedBinding, reducer);
            publisher.current = publisher.Validated(snapshot); publisher.Revision = revision; return publisher;
        }
        public ModStateFrame PublishSnapshot(byte[] snapshot)
        {
            byte[] next = Validated(snapshot); long revision = checked(Revision + 1);
            var frame = new ModStateFrame(Binding, revision, 0, true, next);
            current = next; Revision = revision; history.Clear(); historyBytes = 0; Notify(frame); return frame;
        }
        public ModStateFrame PublishDelta(byte[] delta)
        {
            if (current == null) throw new InvalidOperationException("mod_state_baseline_required");
            if (delta == null || delta.Length > ModStateFrame.MaximumDeltaBytes) throw new ArgumentException("mod_state_delta_budget");
            long revision = checked(Revision + 1);
            byte[] immutableDelta = (byte[])delta.Clone();
            byte[] next = Validated(reducer.ApplyDelta((byte[])current.Clone(), (byte[])immutableDelta.Clone()));
            var frame = new ModStateFrame(Binding, revision, Revision, false, immutableDelta);
            current = next; Revision = revision; history.Enqueue(frame); historyBytes += frame.PayloadLength;
            while (history.Count > MaximumHistoryFrames || historyBytes > MaximumHistoryBytes) historyBytes -= history.Dequeue().PayloadLength;
            Notify(frame); return frame;
        }
        public ModStateFrame CaptureSnapshot()
        {
            if (current == null) throw new InvalidOperationException("mod_state_baseline_required");
            return new ModStateFrame(Binding, Revision, 0, true, current);
        }
        public ModStateRecovery Recover(long knownRevision)
        {
            if (knownRevision < 0 || knownRevision > Revision) throw new ArgumentOutOfRangeException(nameof(knownRevision));
            if (current == null) throw new InvalidOperationException("mod_state_baseline_required");
            if (knownRevision == Revision) return new ModStateRecovery(new ModStateFrame[0]);
            var available = history.Where(frame => frame.Revision > knownRevision).ToArray();
            return new ModStateRecovery(knownRevision > 0 && available.Length > 0 && available[0].BaseRevision == knownRevision
                ? available : new[] { CaptureSnapshot() });
        }
        private byte[] Validated(byte[] snapshot)
        {
            if (snapshot == null || snapshot.Length > ModStateFrame.MaximumSnapshotBytes) throw new ArgumentException("mod_state_snapshot_budget");
            byte[] copy = (byte[])snapshot.Clone();
            if (!reducer.ValidateSnapshot((byte[])copy.Clone())) throw new ArgumentException("invalid_mod_state_snapshot");
            return copy;
        }
        private void Notify(ModStateFrame frame)
        {
            var callbacks = Published; if (callbacks == null) return;
            foreach (Action<ModStateFrame> callback in callbacks.GetInvocationList()) try { callback(frame); } catch (Exception) { }
        }
    }

    public enum ModStateApplyResult { Applied, Duplicate, Stale, WrongAuthority, WrongScope, NeedSnapshot, Conflict, UnknownSchema, QuarantineFull, ReducerFailed, Disconnected }

    public sealed class ModStateReplica
    {
        private readonly IModStateReducer reducer;
        private byte[] current;
        private string lastFrameDigest;
        private bool baseline;
        private long highestObserved;
        private ModStateBinding checkpointBinding;
        private long checkpointRevision;
        public const int MaximumQuarantinedFrames = 4;
        private readonly List<ModStateFrame> quarantines = new List<ModStateFrame>();
        public ModStateBinding Binding { get; private set; }
        public long Revision { get; private set; }
        public byte[] Current { get { return current == null ? null : (byte[])current.Clone(); } }
        public ModStateFrame Checkpoint { get { return current == null ? null : new ModStateFrame(checkpointBinding, checkpointRevision, 0, true, current); } }
        public bool Connected { get; private set; } = true;
        public bool NeedsSnapshot { get; private set; } = true;
        public bool BlockedOnSchema { get; private set; }
        public ModStateFrame Quarantined { get; private set; }
        public IReadOnlyList<ModStateFrame> QuarantinedFrames { get { return quarantines.AsReadOnly(); } }
        public ModStateReplica(ModStateBinding binding, IModStateReducer reducer)
        { Binding = binding ?? throw new ArgumentNullException(nameof(binding)); this.reducer = reducer ?? throw new ArgumentNullException(nameof(reducer)); }
        public ModStateApplyResult Accept(string authenticatedSenderId, ModStateFrame frame)
        {
            if (!Connected) return ModStateApplyResult.Disconnected;
            if (authenticatedSenderId != Binding.AuthorityId) return ModStateApplyResult.WrongAuthority;
            if (frame == null || !Binding.SameScope(frame.Binding, schema: false)) return ModStateApplyResult.WrongScope;
            if (frame.Binding.Schema != Binding.Schema || BlockedOnSchema)
            {
                BlockedOnSchema = true; NeedsSnapshot = true;
                if (Quarantined == null)
                {
                    if (quarantines.Count >= MaximumQuarantinedFrames) return ModStateApplyResult.QuarantineFull;
                    Quarantined = frame; quarantines.Add(frame);
                }
                return ModStateApplyResult.UnknownSchema;
            }
            if (baseline && frame.Revision < Revision) return ModStateApplyResult.Stale;
            string digest = ModSyncValidation.Digest(ModStateCodec.Encode(frame));
            if (baseline && frame.Revision == Revision)
            {
                if (digest == lastFrameDigest || frame.IsSnapshot && frame.Payload.SequenceEqual(current))
                {
                    if (frame.IsSnapshot) NeedsSnapshot = Revision < highestObserved;
                    return ModStateApplyResult.Duplicate;
                }
                NeedsSnapshot = true; return ModStateApplyResult.Conflict;
            }
            highestObserved = Math.Max(highestObserved, frame.Revision);
            if (!frame.IsSnapshot && (!baseline || frame.BaseRevision != Revision))
            { NeedsSnapshot = true; return ModStateApplyResult.NeedSnapshot; }
            try
            {
                byte[] next = frame.IsSnapshot ? frame.Payload : reducer.ApplyDelta((byte[])current.Clone(), frame.Payload);
                if (next == null || next.Length > ModStateFrame.MaximumSnapshotBytes) throw new ArgumentException("mod_state_snapshot_budget");
                next = (byte[])next.Clone();
                if (!reducer.ValidateSnapshot((byte[])next.Clone())) throw new ArgumentException("invalid_mod_state_snapshot");
                current = next; Revision = frame.Revision; baseline = true; lastFrameDigest = digest;
                checkpointBinding = frame.Binding; checkpointRevision = frame.Revision;
                NeedsSnapshot = Revision < highestObserved; return ModStateApplyResult.Applied;
            }
            catch (Exception) { NeedsSnapshot = true; return ModStateApplyResult.ReducerFailed; }
        }
        public void Disconnect() { Connected = false; baseline = false; NeedsSnapshot = true; }
        public void Rebind(ModStateBinding binding)
        {
            if (binding == null || binding.Generation == Binding.Generation || binding.ModId != Binding.ModId || binding.Channel != Binding.Channel)
                throw new ArgumentException("mod_state_fresh_binding_required");
            Binding = binding; Connected = true; baseline = false; Revision = 0; highestObserved = 0;
            NeedsSnapshot = true; BlockedOnSchema = false; lastFrameDigest = null; Quarantined = null;
        }
    }

    public static class ModStateCodec
    {
        public const int MaximumEncodedBytes = ModStateFrame.MaximumSnapshotBytes + 1024;
        private const int Magic = 0x53534d4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static byte[] Encode(ModStateFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); WriteBinding(writer, frame.Binding);
                writer.Write(frame.Revision); writer.Write(frame.BaseRevision); writer.Write((byte)(frame.IsSnapshot ? 1 : 0));
                byte[] payload = frame.Payload; writer.Write(payload.Length); writer.Write(payload); return stream.ToArray();
            }
        }
        public static ModStateFrame Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 5 || bytes.Length > MaximumEncodedBytes) throw new InvalidDataException("mod_state_budget");
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadByte() != 1) throw new InvalidDataException("mod_state_version");
                    ModStateBinding binding = ReadBinding(reader); long revision = reader.ReadInt64(), basis = reader.ReadInt64(); byte snapshot = reader.ReadByte();
                    int length = reader.ReadInt32(); if (snapshot > 1 || length < 0 || length > (snapshot == 1 ? ModStateFrame.MaximumSnapshotBytes : ModStateFrame.MaximumDeltaBytes) || length != stream.Length - stream.Position)
                        throw new InvalidDataException("mod_state_payload_budget");
                    return new ModStateFrame(binding, revision, basis, snapshot == 1, reader.ReadBytes(length));
                }
            }
            catch (Exception error) when (error is ArgumentException || error is EndOfStreamException || error is FormatException)
            { throw new InvalidDataException("invalid_mod_state", error); }
        }
        internal static void WriteBinding(BinaryWriter writer, ModStateBinding binding)
        {
            WriteText(writer, binding.SessionId); WriteText(writer, binding.ModId); WriteText(writer, binding.Channel);
            WriteText(writer, binding.AuthorityId); WriteText(writer, binding.Epoch); WriteText(writer, binding.Generation); writer.Write(binding.Schema);
        }
        internal static ModStateBinding ReadBinding(BinaryReader reader)
        { return new ModStateBinding(ReadText(reader, 32), ReadText(reader, 128), ReadText(reader, 64), ReadText(reader, 32), ReadText(reader, 32), ReadText(reader, 32), reader.ReadInt32()); }
        internal static void WriteText(BinaryWriter writer, string value)
        { byte[] bytes = Utf8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        internal static string ReadText(BinaryReader reader, int maximum)
        {
            int length = reader.ReadUInt16(); if (length > maximum * 4) throw new InvalidDataException("mod_state_text_budget");
            byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
            string value = Utf8.GetString(bytes); if (value.Length > maximum) throw new InvalidDataException("mod_state_text_budget"); return value;
        }
    }
}
