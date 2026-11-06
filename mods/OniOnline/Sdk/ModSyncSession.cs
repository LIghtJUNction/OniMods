using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;

namespace OniOnline.Sdk
{
    // A network seat is not permission to play. Each authenticated connection
    // proves that both endpoints compared the same immutable loaded manifests.
    public sealed class ModSyncSession : IDisposable
    {
        private const int Magic = 0x59534d4f;
        private const int ChunkBytes = 3072;
        private const int AssemblyBudget = 8 * 1024 * 1024;
        private const int MaximumRetiredNonces = 64;
        private const int AdmissionPageEntries = 128, MaximumAdmissionEntries = 4096;
        private enum Kind : byte { Offer = 1, Page = 2, Accepted = 3, Admission = 4 }
        private sealed class Peer
        {
            internal readonly string LocalNonce = Guid.NewGuid().ToString("N");
            internal readonly long ConnectionGeneration;
            internal Peer(long generation) { ConnectionGeneration = generation; }
            internal readonly HashSet<string> Retired = new HashSet<string>(StringComparer.Ordinal);
            internal string RemoteNonce;
            internal bool Blocked, Ready, Sent, NeedAck;
            internal int Total, Offset, SendOffset;
            internal byte[] Buffer;
            internal ModManifest Manifest;
            internal ModSyncPlan Plan;
            internal double RetryAt, NextOfferAt, NextAckAt, Window;
            internal int ReceivedCount, ReceivedBytes;
            internal long AdmissionRevision, AdmissionGeneration;
            internal int AdmissionOffset;
            internal bool AdmissionDone;
            internal double AdmissionRetryAt;
        }
        private sealed class AdmissionEntry
        {
            internal string Id;
            internal long Generation;
            internal byte NativeCapabilities;
        }
        private readonly Dictionary<string, AdmissionEntry> announced = new Dictionary<string, AdmissionEntry>(StringComparer.Ordinal);
        private AdmissionEntry[] admissionSnapshot = new AdmissionEntry[0], admissionAssembly;
        private bool[] admissionPages;
        private int admissionReceived;
        private long admissionRevision, receivedAdmissionRevision;
        private string admissionIdentity;
        private readonly long localAdmissionGeneration;
        private readonly RoomSession room;
        private readonly ModManifest local;
        private readonly byte[] encoded;
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>(StringComparer.Ordinal);
        private string lastTarget;
        private bool disposed, wasReady;
        private double now;
        public ModSyncPlan PendingPlan { get; private set; }
        public bool LocalReady
        {
            get
            {
                if (!Active) return false;
                if (room.IsHost) return true;
                string host = HostId;
                return host != null && PeerReady(host);
            }
        }
        private bool Active { get { return !disposed && room.Joined && !room.Closed && !room.Reconnecting && !room.Leaving; } }
        private string HostId { get { var players = room.Players; return players.Length == 0 ? null : players[0].Id; } }
        public event System.Action Changed;

        public ModSyncSession(RoomSession room, ModManifest manifest)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            local = manifest ?? throw new ArgumentNullException(nameof(manifest));
            localAdmissionGeneration = room.NextModAdmissionGeneration();
            encoded = ModManifestCodec.Encode(local);
            room.Changed += ConnectionsChanged;
            room.ApplicationMessageReceived += Receive;
        }
        public bool IsReady(string playerId)
        {
            if (!LocalReady) return false;
            if (playerId == room.LocalPlayerId || PeerReady(playerId)) return true;
            return !room.IsHost && playerId != null && announced.ContainsKey(playerId) &&
                room.Players.Any(p => p.Id == playerId && p.Connected);
        }
        /// <summary>
        /// Returns a local invalidation token for an admitted player, or zero.
        /// Bind it to this RoomId; it is not a cross-endpoint identity or wire
        /// generation. Local/host tokens belong to this endpoint's admission.
        /// </summary>
        public long AdmissionGenerationFor(string playerId)
        {
            if (!IsReady(playerId)) return 0;
            if (room.IsHost)
                return playerId == room.LocalPlayerId ? localAdmissionGeneration : peers[playerId].AdmissionGeneration;
            if (playerId == room.LocalPlayerId || playerId == HostId) return peers[HostId].AdmissionGeneration;
            return announced.TryGetValue(playerId, out var entry) ? entry.Generation : 0;
        }
        public bool SupportsNativeCapability(string playerId, string capability)
        {
            byte bit = NativeCapabilityBit(capability);
            if (bit == 0 || !IsReady(playerId) || !room.CapabilitiesFor(room.LocalPlayerId).Contains(capability, StringComparer.Ordinal)) return false;
            if (playerId == room.LocalPlayerId) return true;
            if (room.IsHost || playerId == HostId)
                return room.PeerCapabilitiesFor(playerId).Contains(capability, StringComparer.Ordinal);
            return announced.TryGetValue(playerId, out var entry) && (entry.NativeCapabilities & bit) != 0;
        }
        private static byte NativeCapabilityBit(string capability)
        {
            return capability == SessionCapabilities.NativePlanetV1 ? (byte)1
                : capability == SessionCapabilities.NativeObservationV1 ? (byte)2
                : capability == SessionCapabilities.NativeStarMapV1 ? (byte)4 : (byte)0;
        }
        private byte NativeCapabilityBits(string playerId)
        {
            byte bits = 0;
            // Cross-guest native traffic is relayed by the host. Advertise the
            // negotiated path (host/peer intersection), not the peer's raw
            // declaration, so a host without an adapter cannot authorize a
            // capability it cannot carry.
            foreach (string capability in room.CapabilitiesFor(playerId)) bits |= NativeCapabilityBit(capability);
            return bits;
        }
        public ModSyncPlan GetPlan(string playerId)
        {
            return Active && IsTarget(playerId) && peers.TryGetValue(playerId, out var peer) &&
                peer.ConnectionGeneration == room.ConnectionGenerationFor(playerId) ? peer.Plan : null;
        }
        private bool PeerReady(string playerId)
        {
            return IsTarget(playerId) && peers.TryGetValue(playerId, out var peer) &&
                peer.ConnectionGeneration == room.ConnectionGenerationFor(playerId) && !peer.Blocked && peer.Ready;
        }
        private bool IsTarget(string playerId)
        {
            return playerId != null && playerId != room.LocalPlayerId &&
                (room.IsHost || playerId == HostId) && room.Players.Any(p => p.Id == playerId && p.Connected) &&
                room.CanUseCapabilityWith(playerId, SessionCapabilities.ModSynchronizationV1);
        }
        private void ConnectionsChanged()
        {
            if (disposed) return;
            bool removed = false;
            foreach (string id in peers.Keys.Where(id => !Active || !IsTarget(id) || peers[id].ConnectionGeneration != room.ConnectionGenerationFor(id)).ToArray())
            { peers.Remove(id); removed = true; }
            if (!Active || !room.IsHost && !peers.ContainsKey(HostId ?? ""))
            { PendingPlan = null; ClearAnnouncements(); }
            if (!room.IsHost)
                foreach (string id in announced.Keys.Where(id => !room.Players.Any(p => p.Id == id && p.Connected)).ToArray())
                { announced.Remove(id); removed = true; }
            RefreshAdmissionSnapshot();
            if (removed) Notify();
            NotifyIfChanged();
        }
        public void Pump(double seconds)
        {
            if (disposed) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now) throw new ArgumentOutOfRangeException(nameof(seconds));
            now = seconds;
            ConnectionsChanged();
            if (!Active) return;
            var targets = room.Players.Where(p => IsTarget(p.Id)).Select(p => p.Id).ToArray();
            int previous = Array.IndexOf(targets, lastTarget), budget = 4;
            for (int index = 0; index < targets.Length && budget > 0; index++)
            {
                string target = targets[(previous + index + 1) % targets.Length];
                if (!peers.TryGetValue(target, out var peer)) peers.Add(target, peer = new Peer(room.ConnectionGenerationFor(target)));
                if (peer.Blocked) continue;
                if (!peer.Ready && now >= peer.NextOfferAt)
                {
                    if (Send(target, Encode(peer, Kind.Offer, null)))
                    { budget--; lastTarget = target; peer.NextOfferAt = now + 1; }
                }
                if (budget == 0 || peer.RemoteNonce == null) continue;
                if (!peer.Ready)
                {
                    if (peer.Sent && now >= peer.RetryAt) { peer.Sent = false; peer.SendOffset = 0; }
                    if (!peer.Sent && SendPage(target, peer))
                    {
                        budget--; lastTarget = target;
                        peer.SendOffset = Math.Min(encoded.Length, peer.SendOffset + ChunkBytes);
                        if (peer.SendOffset == encoded.Length) { peer.Sent = true; peer.RetryAt = now + 3; }
                    }
                }
                if (budget > 0 && peer.Plan?.CanJoin == true && (!peer.Ready || peer.NeedAck) && SendAccepted(target, peer))
                { budget--; lastTarget = target; }
                if (budget > 0 && room.IsHost && peer.Ready && SendAdmission(target, peer))
                { budget--; lastTarget = target; }
            }
            NotifyIfChanged();
        }
        private bool Send(string target, byte[] bytes)
        { return room.IsHost ? room.SendApplicationTo(target, bytes) : room.SendApplication(bytes); }
        private byte[] Encode(Peer peer, Kind kind, System.Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write((byte)2); writer.Write((byte)kind);
                writer.Write(new Guid(room.RoomId).ToByteArray()); writer.Write(new Guid(peer.LocalNonce).ToByteArray());
                if (kind != Kind.Offer) writer.Write(new Guid(peer.RemoteNonce).ToByteArray());
                write?.Invoke(writer);
                return stream.ToArray();
            }
        }
        private bool SendPage(string target, Peer peer)
        {
            return Send(target, Encode(peer, Kind.Page, writer => {
                writer.Write(encoded.Length); writer.Write(peer.SendOffset);
                int count = Math.Min(ChunkBytes, encoded.Length - peer.SendOffset);
                writer.Write((ushort)count); writer.Write(encoded, peer.SendOffset, count);
            }));
        }
        private bool SendAccepted(string target, Peer peer)
        {
            if (now < peer.NextAckAt) return false;
            bool sent = Send(target, Encode(peer, Kind.Accepted, writer => {
                writer.Write(local.Fingerprint); writer.Write(peer.Manifest.Fingerprint);
            }));
            if (sent) { peer.NextAckAt = now + 1; peer.NeedAck = false; }
            return sent;
        }
        private void Receive(string sender, byte[] bytes)
        {
            if (!Active || !IsTarget(sender) || bytes == null || bytes.Length < 38 || bytes.Length > ChunkBytes + 256 ||
                BitConverter.ToInt32(bytes, 0) != Magic) return;
            if (!peers.TryGetValue(sender, out var peer) || peer.ConnectionGeneration != room.ConnectionGenerationFor(sender))
            {
                peer = new Peer(room.ConnectionGenerationFor(sender)); peers[sender] = peer;
                if (!room.IsHost) { PendingPlan = null; ClearAnnouncements(); }
                Notify();
            }
            if (peer.Blocked) return;
            if (now - peer.Window >= 1) { peer.Window = now; peer.ReceivedCount = 0; peer.ReceivedBytes = 0; }
            if (++peer.ReceivedCount > 128 || (peer.ReceivedBytes += bytes.Length) > 512 * 1024) return;
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, new UTF8Encoding(false, true)))
                {
                    reader.ReadInt32(); if (reader.ReadByte() != 2) return;
                    var kind = (Kind)reader.ReadByte();
                    if (new Guid(reader.ReadBytes(16)).ToString("N") != room.RoomId) return;
                    string remoteNonce = new Guid(reader.ReadBytes(16)).ToString("N");
                    if (kind == Kind.Offer)
                    {
                        if (stream.Position != stream.Length || peer.Retired.Contains(remoteNonce)) return;
                        if (peer.RemoteNonce != remoteNonce)
                        {
                            if (peer.RemoteNonce != null)
                            {
                                if (peer.Retired.Count >= MaximumRetiredNonces)
                                {
                                    peer.Blocked = true; peer.Ready = false; peer.Buffer = null;
                                    if (!room.IsHost) { PendingPlan = null; ClearAnnouncements(); }
                                    Notify(); return;
                                }
                                peer.Retired.Add(peer.RemoteNonce);
                            }
                            peer.RemoteNonce = remoteNonce; peer.Buffer = null; peer.Manifest = null; peer.Plan = null;
                            peer.Ready = false; peer.Sent = false; peer.SendOffset = 0; peer.NextAckAt = 0;
                            if (!room.IsHost) { PendingPlan = null; ClearAnnouncements(); }
                            Notify();
                        }
                        if (peer.Plan?.CanJoin == true) peer.NeedAck = true;
                        return;
                    }
                    string recipientNonce = new Guid(reader.ReadBytes(16)).ToString("N");
                    if (peer.RemoteNonce != remoteNonce || peer.LocalNonce != recipientNonce) return;
                    if (kind == Kind.Page) ReceivePage(sender, peer, reader, stream);
                    else if (kind == Kind.Admission)
                    {
                        if (!room.IsHost && peer.Ready) ReceiveAdmission(reader, stream);
                    }
                    else if (kind == Kind.Accepted)
                    {
                        string from = reader.ReadString(), accepted = reader.ReadString();
                        if (stream.Position != stream.Length || peer.Plan?.CanJoin != true ||
                            from != peer.Manifest.Fingerprint || accepted != local.Fingerprint) return;
                        if (!peer.Ready) { peer.AdmissionGeneration = room.NextModAdmissionGeneration(); peer.Ready = true; peer.NeedAck = true; Notify(); }
                    }
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is FormatException) { }
        }
        private void ReceivePage(string sender, Peer peer, BinaryReader reader, MemoryStream stream)
        {
            int total = reader.ReadInt32(), offset = reader.ReadInt32(), count = reader.ReadUInt16();
            if (total < 7 || total > ModManifestCodec.MaximumEncodedBytes || offset < 0 || count < 1 || count > ChunkBytes ||
                offset > total - count || count != stream.Length - stream.Position) return;
            if (peer.Manifest != null) { peer.NeedAck = peer.Plan?.CanJoin == true; return; }
            if (peer.Buffer == null)
            {
                if (offset != 0 || peers.Values.Sum(p => p.Buffer?.Length ?? 0) > AssemblyBudget - total) return;
                peer.Buffer = new byte[total]; peer.Total = total; peer.Offset = 0;
            }
            if (peer.Total != total || offset != peer.Offset) return;
            byte[] chunk = reader.ReadBytes(count); Array.Copy(chunk, 0, peer.Buffer, offset, count); peer.Offset += count;
            if (peer.Offset != total) return;
            byte[] complete = peer.Buffer; peer.Buffer = null; peer.Offset = 0;
            peer.Manifest = ModManifestCodec.Decode(complete);
            peer.Plan = room.IsHost ? ModSyncPlanner.Compare(local, peer.Manifest) : ModSyncPlanner.Compare(peer.Manifest, local);
            if (!room.IsHost) PendingPlan = peer.Plan;
            peer.NeedAck = peer.Plan.CanJoin;
            Notify();
        }
        private void RefreshAdmissionSnapshot()
        {
            if (!room.IsHost || !Active) return;
            var entries = room.Players.Where(p => p.Connected && (p.Id == room.LocalPlayerId || PeerReady(p.Id)))
                .Select(p => new AdmissionEntry { Id = p.Id, Generation = AdmissionGenerationFor(p.Id), NativeCapabilities = NativeCapabilityBits(p.Id) })
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
            string identity = string.Join(";", entries.Select(p => p.Id + ":" + p.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + p.NativeCapabilities.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (identity == admissionIdentity) return;
            admissionIdentity = identity; admissionSnapshot = entries;
            admissionRevision = checked(admissionRevision + 1);
        }
        private bool SendAdmission(string target, Peer peer)
        {
            if (admissionSnapshot.Length < 1 || admissionSnapshot.Length > MaximumAdmissionEntries) return false;
            if (peer.AdmissionRevision != admissionRevision)
            { peer.AdmissionRevision = admissionRevision; peer.AdmissionOffset = 0; peer.AdmissionDone = false; }
            if (peer.AdmissionDone && now >= peer.AdmissionRetryAt)
            { peer.AdmissionOffset = 0; peer.AdmissionDone = false; }
            if (peer.AdmissionDone) return false;
            int offset = peer.AdmissionOffset, count = Math.Min(AdmissionPageEntries, admissionSnapshot.Length - offset);
            bool sent = Send(target, Encode(peer, Kind.Admission, writer => {
                writer.Write(admissionRevision); writer.Write(admissionSnapshot.Length); writer.Write(offset); writer.Write((ushort)count);
                for (int i = offset; i < offset + count; i++)
                { writer.Write(new Guid(admissionSnapshot[i].Id).ToByteArray()); writer.Write(admissionSnapshot[i].Generation); writer.Write(admissionSnapshot[i].NativeCapabilities); }
            }));
            if (sent)
            {
                peer.AdmissionOffset += count;
                if (peer.AdmissionOffset == admissionSnapshot.Length)
                { peer.AdmissionDone = true; peer.AdmissionRetryAt = now + 3; }
            }
            return sent;
        }
        private void ReceiveAdmission(BinaryReader reader, MemoryStream stream)
        {
            long revision = reader.ReadInt64(); int total = reader.ReadInt32(), offset = reader.ReadInt32(), count = reader.ReadUInt16();
            if (revision < 1 || total < 1 || total > MaximumAdmissionEntries || offset < 0 || offset >= total ||
                offset % AdmissionPageEntries != 0 || count != Math.Min(AdmissionPageEntries, total - offset) ||
                stream.Length - stream.Position != count * 25 || revision < receivedAdmissionRevision) return;
            var page = new AdmissionEntry[count]; var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string id = new Guid(reader.ReadBytes(16)).ToString("N"); long generation = reader.ReadInt64(); byte capabilities = reader.ReadByte();
                if (generation < 1 || capabilities > 7 || !ids.Add(id)) return;
                page[i] = new AdmissionEntry { Id = id, Generation = generation, NativeCapabilities = capabilities };
            }
            if (revision > receivedAdmissionRevision)
            {
                receivedAdmissionRevision = revision; admissionAssembly = new AdmissionEntry[total];
                admissionPages = new bool[(total + AdmissionPageEntries - 1) / AdmissionPageEntries]; admissionReceived = 0;
                announced.Clear(); Notify();
            }
            if (admissionAssembly == null || admissionAssembly.Length != total || admissionPages[offset / AdmissionPageEntries]) return;
            Array.Copy(page, 0, admissionAssembly, offset, count); admissionPages[offset / AdmissionPageEntries] = true; admissionReceived += count;
            if (admissionReceived != total) return;
            var complete = admissionAssembly; admissionAssembly = null; admissionPages = null;
            ids.Clear();
            if (complete.Any(p => !ids.Add(p.Id)) || !ids.Contains(HostId) || !ids.Contains(room.LocalPlayerId)) return;
            foreach (var entry in complete)
                if (room.Players.Any(p => p.Id == entry.Id && p.Connected)) announced[entry.Id] = entry;
            Notify();
        }
        private void ClearAnnouncements()
        {
            announced.Clear(); admissionAssembly = null; admissionPages = null; admissionReceived = 0; receivedAdmissionRevision = 0;
        }
        private void NotifyIfChanged()
        { bool ready = LocalReady; if (ready != wasReady) { wasReady = ready; Notify(); } }
        private void Notify()
        {
            RefreshAdmissionSnapshot();
            var callbacks = Changed; if (callbacks == null) return;
            foreach (System.Action callback in callbacks.GetInvocationList()) try { callback(); } catch (Exception) { }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; room.Changed -= ConnectionsChanged; room.ApplicationMessageReceived -= Receive;
            peers.Clear(); PendingPlan = null; ClearAnnouncements(); Changed = null;
        }
    }
}
