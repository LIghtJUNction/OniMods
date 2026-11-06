using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace OniOnline.Multiplayer
{
    // Call from the room's main-thread pump. No partial snapshot escapes this class.
    public sealed class SnapshotAssembler
    {
        public const int MaxWorlds = 64;
        public const int MaxWorldsPerOwner = 16;
        public const int MaxPendingSnapshots = 4;
        public const int MaxPendingBytes = 24 * 1024 * 1024;
        public const double SnapshotTimeout = 60;
        private readonly Dictionary<string, WorldState> worlds = new Dictionary<string, WorldState>(StringComparer.Ordinal);
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        private int pendingBytes;

        private sealed class WorldState
        {
            public WorldDescriptor Descriptor;
            public string Epoch;
            public string OwnerId;
            public long ControlRevision;
            public long PublishedSequence;
            public WorldChunk PublishedHeader;
            public bool Withdrawn;
        }

        private sealed class Pending
        {
            public WorldChunk Header;
            public string OwnerId;
            public byte[][] Parts;
            public int Received;
            public double LastProgress;
        }

        public int PendingBytes { get { return pendingBytes; } }
        public int PendingCount { get { return pending.Count; } }
        // True means a valid observation packet was retained, or duplicates an already
        // retained/completed frame. A sender may acknowledge it without republishing it.
        public bool LastAccepted { get; private set; }
        public bool LastComplete { get; private set; }

        public WorldDescriptor GetDescriptor(string worldId)
        {
            WorldState state;
            return worldId != null && worlds.TryGetValue(worldId, out state) && !state.Withdrawn && state.Descriptor != null
                ? WorldWire.CopyDescriptor(state.Descriptor) : null;
        }

        public WorldDescriptor[] Descriptors
        {
            get { return worlds.Values.Where(value => !value.Withdrawn && value.Descriptor != null)
                .Select(value => WorldWire.CopyDescriptor(value.Descriptor)).ToArray(); }
        }

        public bool Accept(string senderId, byte[] payload, double now, out WorldSnapshot snapshot)
        {
            LastAccepted = false;
            LastComplete = false;
            snapshot = null;
            if (!WorldWire.Finite(now) || now < 0) return false;
            Prune(now);
            WorldMessage message;
            if (!WorldWire.TryDecode(payload, out message)) return false;
            // Requests name the requested publisher, not the requesting sender.
            if (message.Kind == WorldMessageKind.SnapshotRequest) return false;
            if (!string.Equals(message.OwnerId, senderId, StringComparison.Ordinal)) return false;
            if (message.Kind == WorldMessageKind.Descriptor)
            {
                LastAccepted = AcceptDescriptor(message.Descriptor);
                return false;
            }
            if (message.Kind == WorldMessageKind.Withdraw)
            {
                WorldState withdrawn;
                if (!worlds.TryGetValue(message.WorldId, out withdrawn))
                {
                    if (!HasWorldCapacity(message.OwnerId)) return false;
                    withdrawn = new WorldState
                    {
                        OwnerId = message.OwnerId, Epoch = message.Epoch, ControlRevision = message.ControlRevision
                    };
                    worlds.Add(message.WorldId, withdrawn);
                }
                else if (message.ControlRevision < withdrawn.ControlRevision ||
                    (message.ControlRevision == withdrawn.ControlRevision && !string.Equals(withdrawn.Epoch, message.Epoch, StringComparison.OrdinalIgnoreCase)))
                    return false;
                else if (message.ControlRevision > withdrawn.ControlRevision)
                {
                    if (string.Equals(withdrawn.Epoch, message.Epoch, StringComparison.OrdinalIgnoreCase)) return false;
                    DropPending(message.WorldId);
                    withdrawn.Epoch = message.Epoch;
                    withdrawn.ControlRevision = message.ControlRevision;
                    withdrawn.PublishedSequence = 0;
                    withdrawn.PublishedHeader = null;
                    withdrawn.Descriptor = null;
                }
                Remove(message.WorldId);
                LastAccepted = true;
                return false;
            }
            WorldChunk chunk = message.Chunk;
            WorldState state;
            if (!worlds.TryGetValue(chunk.WorldId, out state) || state.Withdrawn || state.Descriptor == null ||
                !string.Equals(state.Epoch, chunk.Epoch, StringComparison.OrdinalIgnoreCase) || chunk.ControlRevision != state.ControlRevision)
                return false;
            if (chunk.Sequence <= state.PublishedSequence)
            {
                // A relay re-encodes its cached observation after reconnect and
                // obtains a new transfer ID. The immutable body hash/sequence,
                // world, epoch and control revision identify the observation.
                LastAccepted = chunk.Sequence == state.PublishedSequence && state.PublishedHeader != null &&
                    SameBody(state.PublishedHeader, chunk);
                LastComplete = LastAccepted;
                return false;
            }
            Pending building;
            if (pending.TryGetValue(chunk.WorldId, out building))
            {
                if (chunk.Sequence < building.Header.Sequence) return false;
                if (chunk.Sequence > building.Header.Sequence)
                {
                    DropPending(chunk.WorldId);
                    building = null;
                }
                else if (!SameHeader(building.Header, chunk)) return false;
            }
            if (building == null)
            {
                if (pending.Count >= MaxPendingSnapshots || pendingBytes + chunk.TotalBytes > MaxPendingBytes ||
                    pending.Values.Any(value => value.OwnerId == senderId)) return false;
                building = new Pending
                {
                    Header = chunk, OwnerId = senderId, Parts = new byte[chunk.Count][], LastProgress = now
                };
                pending.Add(chunk.WorldId, building);
                // Reserve the entire declared size before accepting the first part.
                pendingBytes += chunk.TotalBytes;
            }
            byte[] previous = building.Parts[chunk.Index];
            if (previous != null)
            {
                if (!EqualBytes(previous, chunk.Data)) DropPending(chunk.WorldId);
                else LastAccepted = true;
                return false;
            }
            building.Parts[chunk.Index] = chunk.Data;
            building.Received++;
            building.LastProgress = now;
            if (building.Received != chunk.Count) { LastAccepted = true; return false; }
            DropPending(chunk.WorldId);
            var bytes = new byte[chunk.TotalBytes];
            for (int index = 0; index < building.Parts.Length; index++)
                Buffer.BlockCopy(building.Parts[index], 0, bytes, index * WorldWire.ChunkDataBytes, building.Parts[index].Length);
            using (var sha = new SHA256Managed()) if (!EqualBytes(sha.ComputeHash(bytes), chunk.Hash)) return false;
            WorldSnapshot complete;
            try { complete = WorldWire.DecodeSnapshotBody(bytes); }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException) { return false; }
            if (complete.World.OwnerId != senderId || complete.World.Id != chunk.WorldId || complete.Sequence != chunk.Sequence ||
                !string.Equals(complete.World.Epoch, chunk.Epoch, StringComparison.OrdinalIgnoreCase) ||
                complete.World.ControlRevision != chunk.ControlRevision ||
                complete.World.NativeWorldId != state.Descriptor.NativeWorldId ||
                complete.World.Width != state.Descriptor.Width || complete.World.Height != state.Descriptor.Height ||
                state.Descriptor.Shared != complete.World.Shared)
                return false;
            state.PublishedSequence = complete.Sequence;
            state.PublishedHeader = new WorldChunk
            {
                SnapshotId = chunk.SnapshotId, Count = chunk.Count, TotalBytes = chunk.TotalBytes, Hash = chunk.Hash
            };
            // The descriptor heartbeat can legitimately be newer than a large snapshot
            // still travelling over a slow link. Sequence orders observed contents;
            // newer metadata must neither reject that observation nor move backwards.
            if (complete.World.SimulationTime >= state.Descriptor.SimulationTime)
                state.Descriptor = WorldWire.CopyDescriptor(complete.World);
            snapshot = complete;
            LastAccepted = true;
            LastComplete = true;
            return true;
        }

        // Reconnecting clears incomplete work, not the anti-rollback watermark.
        public void ResetOwner(string ownerId)
        {
            foreach (var id in pending.Where(item => item.Value.OwnerId == ownerId).Select(item => item.Key).ToArray()) DropPending(id);
        }

        // Keep version tombstones so delayed packets cannot resurrect a withdrawn world.
        public void Remove(string worldId)
        {
            if (worldId == null) return;
            DropPending(worldId);
            WorldState state;
            if (worlds.TryGetValue(worldId, out state)) state.Withdrawn = true;
        }

        public void Prune(double now)
        {
            if (!WorldWire.Finite(now) || now < 0) return;
            foreach (var id in pending.Where(item => now - item.Value.LastProgress >= SnapshotTimeout || now < item.Value.LastProgress)
                .Select(item => item.Key).ToArray()) DropPending(id);
        }

        private bool AcceptDescriptor(WorldDescriptor descriptor)
        {
            WorldState state;
            if (!worlds.TryGetValue(descriptor.Id, out state))
            {
                if (!HasWorldCapacity(descriptor.OwnerId)) return false;
                state = new WorldState { OwnerId = descriptor.OwnerId, Epoch = descriptor.Epoch, ControlRevision = descriptor.ControlRevision };
                worlds.Add(descriptor.Id, state);
            }
            else if (descriptor.ControlRevision < state.ControlRevision ||
                (descriptor.ControlRevision == state.ControlRevision && !string.Equals(state.Epoch, descriptor.Epoch, StringComparison.OrdinalIgnoreCase)))
                return false;
            else if (descriptor.ControlRevision > state.ControlRevision)
            {
                if (string.Equals(state.Epoch, descriptor.Epoch, StringComparison.OrdinalIgnoreCase)) return false;
                state.Epoch = descriptor.Epoch;
                state.ControlRevision = descriptor.ControlRevision;
                state.PublishedSequence = 0;
                state.PublishedHeader = null;
                state.Descriptor = null;
                DropPending(descriptor.Id);
            }
            else if (state.Withdrawn) return false;
            if (state.Descriptor != null && (descriptor.SimulationTime < state.Descriptor.SimulationTime ||
                descriptor.Width != state.Descriptor.Width || descriptor.Height != state.Descriptor.Height ||
                state.Descriptor.Shared != descriptor.Shared)) return false;
            if (!descriptor.Shared) DropPending(descriptor.Id);
            state.Descriptor = WorldWire.CopyDescriptor(descriptor);
            state.Withdrawn = false;
            return true;
        }

        private bool HasWorldCapacity(string ownerId)
        {
            return worlds.Count < MaxWorlds && worlds.Values.Count(value => value.OwnerId == ownerId) < MaxWorldsPerOwner;
        }

        private void DropPending(string worldId)
        {
            Pending value;
            if (!pending.TryGetValue(worldId, out value)) return;
            pendingBytes -= value.Header.TotalBytes;
            pending.Remove(worldId);
        }

        private static bool SameHeader(WorldChunk left, WorldChunk right)
        {
            return left.SnapshotId == right.SnapshotId && SameBody(left, right);
        }

        private static bool SameBody(WorldChunk left, WorldChunk right)
        { return left.Count == right.Count && left.TotalBytes == right.TotalBytes && EqualBytes(left.Hash, right.Hash); }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
