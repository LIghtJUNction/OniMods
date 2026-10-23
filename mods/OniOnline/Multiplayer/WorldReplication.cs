using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    // Native simulations remain local. Only authenticated, explicitly shared observations travel.
    internal sealed class WorldReplication : IDisposable
    {
        private readonly RoomSession session;
        private readonly IWorldSource source;
        private readonly bool allowRemoteSnapshots;
        private readonly SnapshotAssembler assembler = new SnapshotAssembler();
        private readonly SnapshotTransferSender sender;
        private readonly Dictionary<string, WorldViewEntry> worlds = new Dictionary<string, WorldViewEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> interests = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> interestExpiry = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> knownPlayers = new HashSet<string>(StringComparer.Ordinal);
        private bool observing;
        private bool disposed;
        private bool wasJoined;
        private string rosterKey = "";
        private string sourceEpoch;
        private string publicationEpoch;
        private WorldDescriptor lastPublishedDescriptor;
        private string selectedOwner;
        private long controlRevision;
        private long snapshotSequence;
        private double now;
        private double nextMetadata;
        private double nextFrame;
        private double nextRequest;
        private double nextCatalog;
        private double remoteDemandUntil;
        private bool hadDemand;

        public long Revision { get; private set; }
        public bool ShareOwnWorld { get; private set; }
        public string LastError { get; private set; }
        public int PendingTransfers { get { return sender.PendingCount; } }
        public event System.Action Changed;
        public IReadOnlyList<WorldViewEntry> Worlds
        {
            get
            {
                var players = session.Players;
                return worlds.Values.OrderBy(w => Array.FindIndex(players, p => p.Id == w.Descriptor.OwnerId)).ToArray();
            }
        }

        internal WorldReplication(RoomSession session, IWorldSource source, bool allowRemoteSnapshots = false)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.source = source ?? throw new ArgumentNullException("source");
            this.allowRemoteSnapshots = allowRemoteSnapshots;
            sender = new SnapshotTransferSender(SendTo);
            session.ApplicationMessageReceived += Handle;
        }

        public void Pump(double seconds)
        {
            if (disposed) return;
            now = seconds;
            UpdateRoster();
            assembler.Prune(now);
            if (!session.Joined || session.Reconnecting || session.Leaving) return;
            foreach (var expired in interestExpiry.Where(p => p.Value <= now).Select(p => p.Key).ToArray())
            { interestExpiry.Remove(expired); interests.Remove(expired); sender.ClearTarget(expired); }
            bool demand = (observing && selectedOwner == session.LocalPlayerId) ||
                (session.IsHost ? interests.Values.Any(owner => owner == session.LocalPlayerId) : remoteDemandUntil > now);
            if (hadDemand && !demand) sender.ClearOwner(session.LocalPlayerId);
            hadDemand = demand;
            if (source.IsAvailable && now >= nextMetadata)
            {
                bool full = ShareOwnWorld && demand && now >= nextFrame;
                PublishLocal(full);
                nextMetadata = now + 1;
                if (full) nextFrame = now + 2;
            }
            else if (!source.IsAvailable && worlds.ContainsKey(session.LocalPlayerId)) WithdrawLocal();
            if (session.IsHost && now >= nextCatalog)
            {
                bool sent = true;
                foreach (var entry in worlds.Values)
                    sent &= BroadcastDescriptor(entry.Descriptor);
                nextCatalog = allowRemoteSnapshots ? now + 5 : sent ? double.MaxValue : now + 1;
            }
            if (observing && selectedOwner != null && selectedOwner != session.LocalPlayerId && now >= nextRequest)
            { SendRequest(selectedOwner); nextRequest = now + 10; }
            sender.Pump(now);
        }

        public void StopObserving()
        {
            observing = false;
            selectedOwner = null;
        }

        public void SetSharing(bool value)
        {
            if (ShareOwnWorld == value) return;
            ShareOwnWorld = value;
            sender.ClearOwner(session.LocalPlayerId ?? "");
            nextMetadata = nextFrame = 0;
            if (source.IsAvailable && session.Joined)
                PublishLocal(value && (allowRemoteSnapshots || (observing && selectedOwner == session.LocalPlayerId)));
            Notify();
        }

        public void RequestWorld(string ownerId)
        {
            if (!session.Players.Any(p => p.Id == ownerId)) return;
            if (!allowRemoteSnapshots && ownerId != session.LocalPlayerId) return;
            observing = true;
            selectedOwner = ownerId;
            if (ownerId == session.LocalPlayerId)
            { if (source.IsAvailable) PublishLocal(ShareOwnWorld); }
            else SendRequest(ownerId);
            nextRequest = now + 10;
        }

        private void SendRequest(string ownerId)
        {
            if (!session.Joined) return;
            var data = WorldWire.EncodeRequest(ownerId);
            if (session.IsHost) OnRequest(session.LocalPlayerId, ownerId, data);
            else session.SendApplication(data);
        }

        private void PublishLocal(bool full)
        {
            try
            {
                var snapshot = source.Capture(session.LocalPlayerId, ++snapshotSequence, ShareOwnWorld, full);
                if (sourceEpoch != snapshot.World.Epoch)
                {
                    WorldViewEntry old;
                    if (worlds.TryGetValue(session.LocalPlayerId, out old) && old.Descriptor.Id != snapshot.World.Id)
                        SendWithdraw(old.Descriptor);
                    sourceEpoch = snapshot.World.Epoch;
                    publicationEpoch = Guid.NewGuid().ToString("N");
                    controlRevision++;
                    sender.ClearOwner(session.LocalPlayerId);
                }
                snapshot.World.Epoch = publicationEpoch;
                snapshot.World.ControlRevision = controlRevision;
                UpdateDescriptor(snapshot.World);
                // The native viewport path has no use for a remote simulation clock.
                // Catalog state changes and new/resumed members trigger publication instead.
                if ((allowRemoteSnapshots || !SameCatalogDescriptor(lastPublishedDescriptor, snapshot.World)) &&
                    BroadcastDescriptor(snapshot.World))
                    lastPublishedDescriptor = WorldWire.CopyDescriptor(snapshot.World);
                if (full && snapshot.World.Shared)
                {
                    StoreSnapshot(snapshot);
                    if (allowRemoteSnapshots && session.IsHost) QueueForObservers(snapshot);
                    else if (allowRemoteSnapshots)
                    {
                        var host = HostId();
                        if (host != null) SetTransferPressure(!sender.TryQueue(host, snapshot, now));
                    }
                }
                if (LastError != "world.transfer_busy") LastError = null;
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is InvalidDataException)
            {
                LastError = "world.invalid_snapshot";
                Notify();
            }
        }

        private void WithdrawLocal()
        {
            WorldViewEntry old;
            if (!worlds.TryGetValue(session.LocalPlayerId, out old)) return;
            SendWithdraw(old.Descriptor);
            worlds.Remove(session.LocalPlayerId);
            sender.ClearOwner(session.LocalPlayerId);
            sourceEpoch = null;
            lastPublishedDescriptor = null;
            Notify();
        }

        private void SendWithdraw(WorldDescriptor world)
        {
            var data = WorldWire.EncodeWithdraw(world.Id, world.Epoch, world.ControlRevision);
            if (session.IsHost) Broadcast(world.OwnerId, data);
            else session.SendApplication(data);
        }

        private bool BroadcastDescriptor(WorldDescriptor world)
        {
            var data = WorldWire.EncodeDescriptor(world);
            if (session.IsHost) return Broadcast(world.OwnerId, data);
            return world.OwnerId != session.LocalPlayerId || session.SendApplication(data);
        }

        private bool Broadcast(string owner, byte[] data)
        {
            bool sent = true;
            foreach (var player in session.Players)
                if (player.Connected && player.Id != session.LocalPlayerId && player.Id != owner)
                    sent &= SendTo(player.Id, owner, data);
            return sent;
        }

        private static bool SameCatalogDescriptor(WorldDescriptor a, WorldDescriptor b)
        {
            return a != null && b != null && a.Id == b.Id && a.OwnerId == b.OwnerId && a.Name == b.Name &&
                a.Epoch == b.Epoch && a.ControlRevision == b.ControlRevision && a.NativeWorldId == b.NativeWorldId &&
                a.Width == b.Width && a.Height == b.Height && a.Shared == b.Shared;
        }

        private bool SendTo(string target, string owner, byte[] data)
        {
            if (!session.Joined || session.Reconnecting) return false;
            if (!session.IsHost) return target == HostId() && owner == session.LocalPlayerId && session.SendApplication(data);
            return owner == session.LocalPlayerId ? session.SendApplicationTo(target, data) : session.ForwardApplication(owner, target, data);
        }

        private void Handle(string owner, byte[] data)
        {
            if (disposed) return;
            if (SnapshotTransferSender.IsAck(data))
            { sender.AcceptAck(owner, data, now); return; }
            WorldMessage message;
            if (!WorldWire.TryDecode(data, out message) || !session.Players.Any(p => p.Id == owner && p.Connected)) return;
            // The native viewport path exchanges world descriptors here, never whole-world state.
            // An older peer cannot opt this instance back into bulk snapshots with a request.
            if (!allowRemoteSnapshots && (message.Kind == WorldMessageKind.SnapshotRequest ||
                message.Kind == WorldMessageKind.SnapshotChunk)) return;
            if (message.Kind == WorldMessageKind.SnapshotRequest)
            { OnRequest(owner, message.OwnerId, data); return; }
            if (message.OwnerId != owner || owner == session.LocalPlayerId) return;
            WorldSnapshot snapshot;
            bool complete = assembler.Accept(owner, data, now, out snapshot);
            if (message.Kind == WorldMessageKind.SnapshotChunk)
            {
                if (assembler.LastAccepted)
                {
                    // A completed receiver may be replaying an old frame after
                    // reconnect. Confirm this chunk as well: a narrow sender
                    // window cannot send the remaining chunks on Complete alone.
                    SendAck(owner, message.Chunk, false);
                    if (assembler.LastComplete) SendAck(owner, message.Chunk, true);
                }
                if (complete)
                {
                    StoreSnapshot(snapshot);
                    if (session.IsHost) QueueForObservers(snapshot);
                }
                return;
            }
            if (!assembler.LastAccepted) return;
            if (message.Kind == WorldMessageKind.Descriptor)
            {
                UpdateDescriptor(assembler.GetDescriptor(message.WorldId));
                if (session.IsHost && !Broadcast(owner, data)) nextCatalog = 0;
            }
            else if (message.Kind == WorldMessageKind.Withdraw)
            {
                WorldViewEntry entry;
                if (worlds.TryGetValue(owner, out entry) && entry.Descriptor.Id == message.WorldId &&
                    entry.Descriptor.Epoch == message.Epoch && entry.Descriptor.ControlRevision == message.ControlRevision)
                { worlds.Remove(owner); sender.ClearOwner(owner); Notify(); }
                if (session.IsHost) Broadcast(owner, data);
            }
        }

        private void SendAck(string owner, WorldChunk chunk, bool complete)
        {
            var ack = SnapshotTransferSender.EncodeAck(chunk, complete);
            if (session.IsHost) session.SendApplicationTo(owner, ack);
            else session.SendApplication(ack);
        }

        private void OnRequest(string requester, string requestedOwner, byte[] payload)
        {
            if (!allowRemoteSnapshots) return;
            if (!session.Players.Any(p => p.Id == requestedOwner && p.Connected)) return;
            if (session.IsHost)
            {
                interests[requester] = requestedOwner;
                interestExpiry[requester] = now + 25;
                WorldViewEntry entry;
                // Local demand can capture a current frame on the next Pump.
                // Replaying a pre-disconnect cache first would occupy the bulk
                // sender until that entire stale frame crosses a slow link.
                if (requestedOwner != session.LocalPlayerId && requester != session.LocalPlayerId && worlds.TryGetValue(requestedOwner, out entry) &&
                    entry.Descriptor.Shared && entry.Snapshot != null)
                    SetTransferPressure(!sender.TryQueue(requester, entry.Snapshot, now));
                if (requestedOwner != session.LocalPlayerId)
                    session.ForwardApplication(requester, requestedOwner, payload);
                else nextFrame = nextMetadata = 0;
            }
            else if (requestedOwner == session.LocalPlayerId && ShareOwnWorld)
            { remoteDemandUntil = now + 25; nextFrame = nextMetadata = 0; }
        }

        private void QueueForObservers(WorldSnapshot snapshot)
        {
            if (!snapshot.World.Shared) return;
            bool busy = false;
            foreach (var player in session.Players)
            {
                string owner;
                if (player.Connected && player.Id != session.LocalPlayerId && player.Id != snapshot.World.OwnerId &&
                    interests.TryGetValue(player.Id, out owner) && owner == snapshot.World.OwnerId)
                    busy |= !sender.TryQueue(player.Id, snapshot, now);
            }
            SetTransferPressure(busy);
        }

        private void SetTransferPressure(bool busy)
        {
            string next = busy ? "world.transfer_busy" : LastError == "world.transfer_busy" ? null : LastError;
            if (next == LastError) return;
            LastError = next;
            Notify();
        }

        private void UpdateDescriptor(WorldDescriptor descriptor)
        {
            if (descriptor == null) return;
            var player = session.Players.FirstOrDefault(p => p.Id == descriptor.OwnerId);
            if (player == null) return;
            WorldViewEntry entry;
            if (!worlds.TryGetValue(descriptor.OwnerId, out entry))
            { entry = new WorldViewEntry(); worlds[descriptor.OwnerId] = entry; }
            bool changedEpoch = entry.Descriptor != null && (entry.Descriptor.Id != descriptor.Id ||
                entry.Descriptor.Epoch != descriptor.Epoch || entry.Descriptor.ControlRevision != descriptor.ControlRevision);
            if (changedEpoch || !descriptor.Shared)
            { entry.Snapshot = null; sender.ClearOwner(descriptor.OwnerId); }
            entry.Descriptor = WorldWire.CopyDescriptor(descriptor);
            entry.OwnerName = player.Name;
            entry.Connected = player.Connected && !session.Reconnecting;
            entry.IsLocal = player.Id == session.LocalPlayerId;
            Notify();
        }

        private void StoreSnapshot(WorldSnapshot snapshot)
        {
            WorldViewEntry entry;
            if (!worlds.TryGetValue(snapshot.World.OwnerId, out entry)) UpdateDescriptor(snapshot.World);
            if (!worlds.TryGetValue(snapshot.World.OwnerId, out entry) || !entry.Descriptor.Shared ||
                entry.Descriptor.Epoch != snapshot.World.Epoch || entry.Descriptor.ControlRevision != snapshot.World.ControlRevision) return;
            if (entry.Snapshot != null && entry.Snapshot.Sequence >= snapshot.Sequence) return;
            entry.Snapshot = snapshot;
            entry.LastReceivedUtc = System.DateTime.UtcNow;
            Notify();
        }

        private void UpdateRoster()
        {
            var players = session.Players;
            string key = string.Join(";", players.Select(p => p.Id + ":" + p.Connected)) + ":" + session.Joined;
            if (key == rosterKey) return;
            rosterKey = key;
            foreach (var id in knownPlayers.Where(id => !players.Any(p => p.Id == id)).ToArray())
            { sender.ClearTarget(id); interests.Remove(id); }
            knownPlayers.Clear();
            foreach (var player in players) knownPlayers.Add(player.Id);
            foreach (var entry in worlds.Values)
            {
                var player = players.FirstOrDefault(p => p.Id == entry.Descriptor.OwnerId);
                entry.Connected = player != null && player.Connected && !session.Reconnecting;
                if (player != null) entry.OwnerName = player.Name;
            }
            foreach (var owner in worlds.Keys.Where(id => !players.Any(p => p.Id == id)).ToArray())
            { worlds.Remove(owner); sender.ClearOwner(owner); assembler.ResetOwner(owner); }
            foreach (var player in players)
                if (!player.Connected) { sender.ClearTarget(player.Id); assembler.ResetOwner(player.Id); }
            if (!session.Joined || !wasJoined)
            {
                sender.Reset();
                nextMetadata = nextFrame = nextCatalog = nextRequest = 0;
                lastPublishedDescriptor = null;
                foreach (var player in players) assembler.ResetOwner(player.Id);
            }
            wasJoined = session.Joined;
            if (session.IsHost) nextCatalog = 0;
            Notify();
        }

        private string HostId() { var players = session.Players; return players.Length == 0 ? null : players[0].Id; }
        private void Notify() { Revision++; if (Changed != null) Changed(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.ApplicationMessageReceived -= Handle;
            sender.Reset();
            worlds.Clear();
        }
    }
}
