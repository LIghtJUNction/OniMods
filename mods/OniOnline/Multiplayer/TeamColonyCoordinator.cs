using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OniOnline.Core;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    /// <summary>
    /// Network policy for a team on the host's already loaded, admitted native planet.
    /// The resolver must return null for unloaded/unadmitted worlds. Guest-world computation
    /// transfer and native dupe orders are not implemented by this coordinator.
    /// </summary>
    internal sealed class TeamColonyCoordinator : IDisposable
    {
        private sealed class Pending
        { internal TeamColonyMessage Message; internal byte[] Bytes; internal double RetryAt; internal long LocalAdmission, HostAdmission; }
        private sealed class CachedReceipt
        { internal byte[] Hash; internal TeamColonyMessage Receipt; }
        private sealed class Cursor
        { internal TeamColonySnapshot Snapshot; internal int Offset; }
        private sealed class VerifiedOwnerStamp
        { internal string StableDupeId; internal string OriginalOwnerId; internal string OwnerPeer; }
        private readonly RoomSession session;
        private readonly System.Func<WorkPlanet> nativeHostPlanet;
        private readonly System.Action<string, TeamPermissionFlags> applyPermissions;
        private readonly System.Func<string, string, bool> verifyRegisteredOwner;
        private readonly System.Func<string, bool> canUsePeer;
        private readonly System.Func<string, long> admissionGeneration;
        private readonly Dictionary<string, VerifiedOwnerStamp> verifiedDupeOwners = new Dictionary<string, VerifiedOwnerStamp>(StringComparer.Ordinal);
        private bool projectionChanged;
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private readonly Dictionary<string, CachedReceipt> receipts = new Dictionary<string, CachedReceipt>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> receiptCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private int receiptBytes;
        private readonly Dictionary<string, Cursor> outbound = new Dictionary<string, Cursor>(StringComparer.Ordinal);
        private readonly Dictionary<string, TeamPermissionFlags> applied = new Dictionary<string, TeamPermissionFlags>(StringComparer.Ordinal);
        private readonly Dictionary<string, TeamColonyEntry> duplicants = new Dictionary<string, TeamColonyEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> delegates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly PagedCollection<TeamColonyEntry> assembly = new PagedCollection<TeamColonyEntry>();
        private TeamColonySnapshot snapshot;
        private TeamColonySnapshot partialHeader;
        private TeamColonyState authority;
        private double now;
        private double nextReplay;
        private double nextQuery;
        private long publishedRevision;
        private long observedModelRevision;
        private bool disposed;
        private bool lastConnected;
        private int sendCursor;
        internal string LastError { get; private set; }
        internal int PendingCount { get { return pending.Count; } }
        internal event System.Action Changed;
        internal TeamColonySnapshot Snapshot
        {
            get
            {
                if (snapshot == null) return null;
                var copy = snapshot.Copy();
                if (!Connected) { copy.HostAvailable = false; copy.CanContinue = false; }
                return copy;
            }
        }
        private bool Connected { get { return !disposed && session.Joined && !session.Reconnecting && !session.Closed && !session.Leaving &&
            Allowed(session.LocalPlayerId) && (session.IsHost || Allowed(session.Players.FirstOrDefault()?.Id)); } }
        private bool ConnectedPeer(string peer)
        { return Connected && Allowed(peer) && session.Players.Any(p => p.Id == peer && p.Connected); }
        private bool Allowed(string peer)
        { return PeerAdmission.Allows(canUsePeer, peer) && PeerAdmission.Generation(admissionGeneration, peer) > 0; }
        private long AdmissionStamp(string peer)
        { return Allowed(peer) ? PeerAdmission.Generation(admissionGeneration, peer) : 0; }

        internal TeamColonyCoordinator(RoomSession session, System.Func<WorkPlanet> nativeHostPlanet,
            System.Action<string, TeamPermissionFlags> applyPermissions = null,
            System.Func<string, string, bool> verifyRegisteredOwner = null, System.Func<string, bool> canUsePeer = null,
            System.Func<string, long> admissionGeneration = null)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.nativeHostPlanet = nativeHostPlanet ?? throw new ArgumentNullException("nativeHostPlanet");
            this.applyPermissions = applyPermissions ?? ((actor, flags) => { });
            this.verifyRegisteredOwner = verifyRegisteredOwner;
            this.canUsePeer = canUsePeer;
            this.admissionGeneration = admissionGeneration;
            session.ApplicationMessageReceived += Receive;
        }

        internal void Pump(double time)
        {
            now = time;
            if (disposed) return;
            bool connected = Connected;
            if (!connected && (canUsePeer != null || admissionGeneration != null)) { pending.Clear(); outbound.Clear(); }
            if (lastConnected != connected) { lastConnected = connected; Notify(); }
            if (session.IsHost)
            {
                RefreshAuthority();
                if (authority != null && (authority.Revision != observedModelRevision || now >= nextReplay)) Publish();
                if (!connected) return;
                string[] peers = outbound.Keys.ToArray();
                int budget = 8;
                while (peers.Length > 0 && budget-- > 0)
                {
                    sendCursor %= peers.Length;
                    string peer = peers[sendCursor];
                    sendCursor = (sendCursor + 1) % peers.Length;
                    Cursor cursor;
                    if (!outbound.TryGetValue(peer, out cursor)) continue;
                    if (!ConnectedPeer(peer)) { outbound.Remove(peer); continue; }
                    var page = cursor.Snapshot.Copy(false);
                    page.Entries = cursor.Snapshot.Entries.Skip(cursor.Offset).Take(TeamColonyWire.PageSize).ToArray();
                    var message = Header(TeamColonyMessageKind.Snapshot, cursor.Snapshot);
                    message.Snapshot = page; message.Offset = cursor.Offset; message.Total = cursor.Snapshot.Entries.Length;
                    if (session.SendApplicationTo(peer, TeamColonyWire.Encode(message)))
                    {
                        cursor.Offset += page.Entries.Length;
                        if (cursor.Offset >= cursor.Snapshot.Entries.Length) outbound.Remove(peer);
                    }
                }
            }
            else if (connected)
            {
                if (canUsePeer != null || admissionGeneration != null)
                    while (pending.Count > 0 && (pending.Peek().LocalAdmission != AdmissionStamp(session.LocalPlayerId) ||
                        pending.Peek().HostAdmission != AdmissionStamp(session.Players.FirstOrDefault()?.Id)))
                    { pending.Dequeue(); LastError = "mod_sync_not_ready"; Notify(); }
                if (now >= nextQuery)
                {
                    session.SendApplication(TeamColonyWire.Encode(new TeamColonyMessage { Kind = TeamColonyMessageKind.Query, RoomId = session.RoomId }));
                    nextQuery = now + 5;
                }
                if (pending.Count > 0 && now >= pending.Peek().RetryAt)
                { var item = pending.Peek(); session.SendApplication(item.Bytes); item.RetryAt = now + 1; }
            }
        }

        internal bool CreateTeam()
        {
            if (!session.IsHost || !Connected) return Reject("team_host_native_only");
            WorkPlanet planet = nativeHostPlanet();
            if (!ValidNativePlanet(planet)) return Reject("team_host_native_only");
            if (authority != null && authority.PlanetId == planet.PlanetId && authority.AuthorityEpoch == planet.AuthorityEpoch) return true;
            if (authority != null) return Reject("team_existing_authority_changed");
            authority = new TeamColonyState(Guid.NewGuid().ToString("N"), planet.PlanetId,
                session.LocalPlayerId, planet.AuthorityEpoch, session.LocalPlayerId);
            RefreshAuthority();
            authority.SetPermissions(session.LocalPlayerId, session.LocalPlayerId, (TeamPermissionFlags)7, authority.AuthorityEpoch);
            LastError = null; Publish(); return true;
        }
        internal bool Invite(string peerId) { return Submit(TeamColonyMessageKind.Invite, peerId); }
        internal bool AcceptInvitation() { return Submit(TeamColonyMessageKind.Accept); }
        internal bool LeaveTeam() { return Submit(TeamColonyMessageKind.Leave); }
        internal bool CancelInvitation(string peerId) { return Submit(TeamColonyMessageKind.CancelInvitation, peerId); }
        internal bool SetPermissions(string peerId, TeamPermissionFlags permissions)
        { return Submit(TeamColonyMessageKind.Permissions, peerId, permissions: permissions); }
        internal bool SetAdministrator(string peerId, bool administrator)
        { return Submit(TeamColonyMessageKind.Administrator, peerId, enabled: administrator); }
        internal bool TransferAdministration(string successorId)
        { return Submit(TeamColonyMessageKind.TransferAdministration, successorId); }
        internal bool DelegateDuplicant(string dupeId, string peerId, bool allow)
        { return Submit(TeamColonyMessageKind.Delegate, peerId, dupeId, enabled: allow); }
        internal bool DelegateDuplicantToTeam(string dupeId, bool allow)
        { return Submit(TeamColonyMessageKind.DelegateTeam, entityId: dupeId, enabled: allow); }
        internal bool OfferContribution(string requestId, string resourceId, decimal amount)
        { return Submit(TeamColonyMessageKind.Offer, requestId, resourceId, amount: amount); }

        internal bool RegisterDuplicant(string dupeId, string ownerId)
        {
            if (!session.IsHost || authority == null || verifyRegisteredOwner != null) return false;
            RefreshAuthority();
            if (!authority.RegisterDuplicant(session.LocalPlayerId, dupeId, ownerId, authority.AuthorityEpoch)) return false;
            if (!duplicants.ContainsKey(dupeId))
            {
                duplicants.Add(dupeId, new TeamColonyEntry { Kind = TeamColonyEntryKind.Duplicant, EntityId = dupeId, ActorId = ownerId });
                delegates.Add(dupeId, new HashSet<string>(StringComparer.Ordinal));
            }
            Publish(); return true;
        }
        // No wire message may supply this stamp. The host's native origin resolver must
        // prove the immutable owner and exact stable/native binding before calling it.
        internal bool RegisterVerifiedDuplicant(string nativeDupeId, string stableDupeId, string originalOwnerId, string ownerPeer)
        {
            if (!session.IsHost || authority == null || !DupeOriginCodec.ValidId(stableDupeId) || !DupeOriginCodec.ValidId(originalOwnerId)) return false;
            int nativeId;
            if (!int.TryParse(nativeDupeId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out nativeId) || nativeId < 0) return false;
            RefreshAuthority();
            var previous = verifiedDupeOwners.FirstOrDefault(p => p.Value.StableDupeId == stableDupeId);
            VerifiedOwnerStamp atNative;
            if (verifiedDupeOwners.TryGetValue(nativeDupeId, out atNative) && atNative.StableDupeId != stableDupeId) return false;
            if (previous.Value != null && previous.Value.OriginalOwnerId != originalOwnerId) return false;
            if (duplicants.ContainsKey(nativeDupeId) && !verifiedDupeOwners.ContainsKey(nativeDupeId)) return false;
            string projection = previous.Value?.OwnerPeer ?? "unresolved-owner:" + originalOwnerId;
            if (ownerPeer != null && (verifyRegisteredOwner == null || verifyRegisteredOwner(ownerPeer, originalOwnerId))) projection = ownerPeer;
            bool ownerChanged = previous.Value != null && previous.Value.OwnerPeer != projection;
            if (previous.Value == null)
            {
                if (!authority.RegisterDuplicant(session.LocalPlayerId, stableDupeId, projection, authority.AuthorityEpoch)) return false;
            }
            else if (ownerChanged && !authority.RefreshVerifiedDuplicantOwner(session.LocalPlayerId, stableDupeId, projection, authority.AuthorityEpoch)) return false;
            if (previous.Value != null && previous.Key == nativeDupeId && !ownerChanged) return true;
            TeamColonyEntry metadata;
            HashSet<string> explicitDelegates;
            if (previous.Value != null)
            {
                metadata = duplicants[previous.Key]; explicitDelegates = delegates[previous.Key];
                duplicants.Remove(previous.Key); delegates.Remove(previous.Key); verifiedDupeOwners.Remove(previous.Key);
                if (ownerChanged) { metadata.Enabled = false; explicitDelegates.Clear(); }
            }
            else
            { metadata = new TeamColonyEntry { Kind = TeamColonyEntryKind.Duplicant }; explicitDelegates = new HashSet<string>(StringComparer.Ordinal); }
            metadata.EntityId = nativeDupeId; metadata.ActorId = projection;
            duplicants[nativeDupeId] = metadata; delegates[nativeDupeId] = explicitDelegates;
            verifiedDupeOwners[nativeDupeId] = new VerifiedOwnerStamp { StableDupeId = stableDupeId, OriginalOwnerId = originalOwnerId, OwnerPeer = projection };
            projectionChanged = true; Publish(); return true;
        }
        private string CoreDuplicantId(string nativeId)
        { VerifiedOwnerStamp stamp; return nativeId != null && verifiedDupeOwners.TryGetValue(nativeId, out stamp) ? stamp.StableDupeId : nativeId; }
        private bool VerifiedOwnerCanDelegate(string actor, string nativeId)
        {
            VerifiedOwnerStamp stamp;
            if (nativeId == null || !verifiedDupeOwners.TryGetValue(nativeId, out stamp)) return verifyRegisteredOwner == null;
            return stamp.OwnerPeer == actor && (verifyRegisteredOwner == null || verifyRegisteredOwner(actor, stamp.OriginalOwnerId));
        }
        internal bool CanIssue(string actorId)
        { RefreshAuthority(); return ConnectedPeer(actorId) && session.IsHost && authority != null && authority.CanIssue(actorId, Connected); }
        internal bool CanControl(string actorId, string dupeId)
        { RefreshAuthority(); return ConnectedPeer(actorId) && session.IsHost && authority != null && (verifyRegisteredOwner == null || verifiedDupeOwners.ContainsKey(dupeId ?? "")) &&
            authority.CanControl(actorId, CoreDuplicantId(dupeId), Connected); }

        private bool Submit(TeamColonyMessageKind kind, string targetId = "", string entityId = "",
            TeamPermissionFlags permissions = TeamPermissionFlags.None, bool enabled = false, decimal amount = 0)
        {
            if (!Connected || snapshot == null || string.IsNullOrEmpty(snapshot.TeamId)) return Reject("team_unavailable");
            if (RequiresPeerTarget(kind, permissions, enabled) && !ConnectedPeer(targetId)) return Reject("mod_sync_not_ready");
            var message = Header(kind, snapshot);
            message.RequestId = Guid.NewGuid().ToString("N"); message.TargetId = targetId ?? "";
            message.EntityId = entityId ?? ""; message.Permissions = permissions; message.Enabled = enabled; message.Amount = amount;
            byte[] bytes;
            try { bytes = TeamColonyWire.Encode(message); }
            catch (InvalidDataException) { return Reject("team_invalid_request"); }
            if (session.IsHost)
            {
                TeamColonyMessage receipt = Execute(session.LocalPlayerId, message, bytes);
                LastError = receipt.Accepted ? null : receipt.Reason; Notify(); return receipt.Accepted;
            }
            if (pending.Count >= 64) return Reject("team_pending_budget");
            pending.Enqueue(new Pending { Message = message, Bytes = bytes, LocalAdmission = AdmissionStamp(session.LocalPlayerId),
                HostAdmission = AdmissionStamp(session.Players.FirstOrDefault()?.Id) });
            LastError = null; Notify(); return true;
        }

        private void Receive(string sender, byte[] bytes)
        {
            if (!ConnectedPeer(sender) || !TeamColonyWire.IsMessage(bytes)) return;
            TeamColonyMessage message;
            try { message = TeamColonyWire.Decode(bytes); }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException) { return; }
            if (message.RoomId != session.RoomId || !Connected) return;
            if (session.IsHost)
            {
                if (!session.Players.Any(p => p.Id == sender && p.Connected)) return;
                if (message.Kind == TeamColonyMessageKind.Query)
                { QueueSnapshot(sender); return; }
                if (message.Kind >= TeamColonyMessageKind.Snapshot) return;
                var receipt = Execute(sender, message, bytes);
                session.SendApplicationTo(sender, TeamColonyWire.Encode(receipt));
            }
            else
            {
                var host = session.Players.FirstOrDefault();
                if (host == null || sender != host.Id) return;
                if (message.Kind == TeamColonyMessageKind.Receipt)
                {
                    if (pending.Count == 0) return;
                    var first = pending.Peek().Message;
                    if (message.RequestId != first.RequestId || message.TeamId != first.TeamId ||
                        message.PlanetId != first.PlanetId || message.AuthorityEpoch != first.AuthorityEpoch) return;
                    pending.Dequeue(); LastError = message.Accepted ? null : message.Reason; nextQuery = 0; Notify();
                }
                else if (message.Kind == TeamColonyMessageKind.Snapshot)
                {
                    var incoming = message.Snapshot;
                    if (incoming.AuthorityPeerId != host.Id || (snapshot != null && incoming.Revision <= snapshot.Revision)) return;
                    try
                    {
                        if (partialHeader == null || incoming.Revision > partialHeader.Revision) partialHeader = incoming.Copy(false);
                        else if (!SameHeader(partialHeader, incoming)) return;
                        TeamColonyEntry[] complete;
                        if (!assembly.Accept(incoming.Revision, message.Total, message.Offset, incoming.Entries, bytes.Length, out complete)) return;
                        if (complete.Where(e => e.Kind == TeamColonyEntryKind.Member).Select(e => e.ActorId).Distinct().Count() !=
                            complete.Count(e => e.Kind == TeamColonyEntryKind.Member) ||
                            (incoming.IsMember && !complete.Any(e => e.Kind == TeamColonyEntryKind.Member && e.ActorId == session.LocalPlayerId))) return;
                        incoming.Entries = complete; snapshot = incoming; partialHeader = null; Notify();
                    }
                    catch (InvalidDataException) { return; }
                }
            }
        }

        private TeamColonyMessage Execute(string actorId, TeamColonyMessage message, byte[] bytes)
        {
            var receipt = new TeamColonyMessage { Kind = TeamColonyMessageKind.Receipt, RoomId = message.RoomId,
                TeamId = message.TeamId, PlanetId = message.PlanetId, AuthorityEpoch = message.AuthorityEpoch, RequestId = message.RequestId };
            if (!ConnectedPeer(actorId) || RequiresPeerTarget(message.Kind, message.Permissions, message.Enabled) && !ConnectedPeer(message.TargetId))
            { receipt.Reason = "mod_sync_not_ready"; return receipt; }
            string key = actorId + ":" + message.RequestId;
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            CachedReceipt cached;
            if (receipts.TryGetValue(key, out cached))
            {
                if (cached.Hash.SequenceEqual(hash)) return cached.Receipt;
                receipt.Reason = "team_conflicting_retry"; return receipt;
            }
            RefreshAuthority();
            bool accepted = authority != null && message.TeamId == authority.TeamId && message.PlanetId == authority.PlanetId &&
                message.AuthorityEpoch == authority.AuthorityEpoch && authority.HostAvailable;
            bool eligible = authority != null && (authority.GetMembers().Any(m => m.ActorId == actorId) ||
                (message.Kind == TeamColonyMessageKind.Accept && authority.IsInvited(actorId)));
            if (!accepted || !eligible) { receipt.Reason = "team_denied_or_stale"; return receipt; }
            // Never evict an executed request and later treat its retry as a new operation.
            // Uninvited peers cannot consume this ledger; one member also has its own budget.
            int actorReceipts; receiptCounts.TryGetValue(actorId, out actorReceipts);
            int cost = bytes.Length + 512;
            if (actorReceipts >= 1024 || receiptBytes > 4 * 1024 * 1024 - cost)
            { receipt.Reason = "team_receipt_budget"; return receipt; }
            string epoch = authority.AuthorityEpoch;
            if (accepted)
            {
                switch (message.Kind)
                {
                    case TeamColonyMessageKind.Invite:
                        accepted = session.Players.Any(p => p.Id == message.TargetId && p.Connected) && authority.Invite(actorId, message.TargetId, epoch); break;
                    case TeamColonyMessageKind.Accept: accepted = authority.AcceptInvitation(actorId, epoch); break;
                    case TeamColonyMessageKind.Leave:
                        accepted = authority.Leave(actorId, epoch); if (accepted) ClearDepartureDelegations(actorId); break;
                    case TeamColonyMessageKind.CancelInvitation: accepted = authority.CancelInvitation(actorId, message.TargetId, epoch); break;
                    case TeamColonyMessageKind.Permissions: accepted = authority.SetPermissions(actorId, message.TargetId, message.Permissions, epoch); break;
                    case TeamColonyMessageKind.Administrator: accepted = authority.SetAdministrator(actorId, message.TargetId, message.Enabled, epoch); break;
                    case TeamColonyMessageKind.TransferAdministration: accepted = authority.TransferAdministration(actorId, message.TargetId, epoch); break;
                    case TeamColonyMessageKind.Delegate:
                        accepted = VerifiedOwnerCanDelegate(actorId, message.EntityId) &&
                            authority.DelegateDuplicant(actorId, CoreDuplicantId(message.EntityId), message.TargetId, message.Enabled, epoch);
                        if (accepted) { if (message.Enabled) delegates[message.EntityId].Add(message.TargetId); else delegates[message.EntityId].Remove(message.TargetId); }
                        break;
                    case TeamColonyMessageKind.DelegateTeam:
                        accepted = VerifiedOwnerCanDelegate(actorId, message.EntityId) &&
                            authority.DelegateDuplicantToTeam(actorId, CoreDuplicantId(message.EntityId), message.Enabled, epoch);
                        if (accepted) duplicants[message.EntityId].Enabled = message.Enabled; break;
                    case TeamColonyMessageKind.Offer: accepted = authority.OfferContribution(actorId, message.TargetId, message.EntityId, message.Amount, epoch); break;
                    default: accepted = false; break;
                }
            }
            receipt.Accepted = accepted; receipt.Reason = accepted ? "ok" : "team_denied_or_stale";
            receipts.Add(key, new CachedReceipt { Hash = hash, Receipt = receipt });
            receiptCounts[actorId] = actorReceipts + 1; receiptBytes += cost;
            if (accepted) { LastError = null; Publish(); }
            else QueueSnapshot(actorId);
            return receipt;
        }
        private void ClearDepartureDelegations(string actorId)
        {
            foreach (var dupe in duplicants.Values)
            {
                delegates[dupe.EntityId].Remove(actorId);
                if (dupe.ActorId == actorId) { dupe.Enabled = false; delegates[dupe.EntityId].Clear(); }
            }
        }
        private void RefreshAuthority()
        {
            if (!session.IsHost || authority == null) return;
            WorkPlanet planet = nativeHostPlanet();
            bool available = Connected && ValidNativePlanet(planet) && planet.PlanetId == authority.PlanetId && planet.AuthorityEpoch == authority.AuthorityEpoch;
            authority.SetHostAvailable(session.LocalPlayerId, authority.AuthorityEpoch, available);
            var connectedPeers = new HashSet<string>(session.Players.Where(p => ConnectedPeer(p.Id)).Select(p => p.Id), StringComparer.Ordinal);
            foreach (var member in authority.GetMembers())
                authority.SetMemberOnline(session.LocalPlayerId, member.ActorId, authority.AuthorityEpoch,
                    Connected && connectedPeers.Contains(member.ActorId));
            // A temporary disconnect must not cancel previously accepted task layers.
            // Native availability and online identity are checked again before each new command.
            var desired = authority.GetMembers().ToDictionary(m => m.ActorId,
                m => m.Permissions, StringComparer.Ordinal);
            foreach (string actor in applied.Keys.Union(desired.Keys).ToArray())
            {
                TeamPermissionFlags flags; desired.TryGetValue(actor, out flags);
                TeamPermissionFlags prior;
                if (!applied.TryGetValue(actor, out prior) || prior != flags)
                { applyPermissions(actor, flags); applied[actor] = flags; }
            }
        }
        private bool ValidNativePlanet(WorkPlanet planet)
        {
            Guid planetId, epoch;
            return planet != null && Guid.TryParseExact(planet.PlanetId, "N", out planetId) && planetId != Guid.Empty &&
                Guid.TryParseExact(planet.AuthorityEpoch, "N", out epoch) && epoch != Guid.Empty &&
                planet.NativeWorldId >= 0 && planet.NativeWorldId <= 255 && planet.AuthorityPeerId == session.LocalPlayerId &&
                planet.OwnerPeerId == session.LocalPlayerId;
        }
        private void Publish()
        {
            if (authority == null) return;
            RefreshAuthority();
            if (snapshot == null || projectionChanged || observedModelRevision != authority.Revision)
            { observedModelRevision = authority.Revision; publishedRevision++; projectionChanged = false; }
            nextReplay = now + 5;
            snapshot = BuildSnapshot(session.LocalPlayerId);
            foreach (var player in session.Players.Where(p => p.Connected && p.Id != session.LocalPlayerId)) QueueSnapshot(player.Id);
            Notify();
        }
        private void QueueSnapshot(string peer)
        {
            if (authority == null || peer == session.LocalPlayerId || !ConnectedPeer(peer)) return;
            var next = BuildSnapshot(peer);
            Cursor current;
            // Queries/replays must not repeatedly restart a slow peer before its last page.
            if (outbound.TryGetValue(peer, out current) && SameHeader(current.Snapshot, next)) return;
            outbound[peer] = new Cursor { Snapshot = next };
        }
        private TeamColonySnapshot BuildSnapshot(string recipient)
        {
            var members = authority.GetMembers();
            var viewer = members.FirstOrDefault(m => m.ActorId == recipient);
            bool invited = authority.IsInvited(recipient);
            var result = new TeamColonySnapshot { RoomId = session.RoomId, AuthorityPeerId = session.LocalPlayerId,
                Revision = Math.Max(publishedRevision, 1), Invited = invited, IsMember = viewer != null };
            if (viewer == null && !invited) return result;
            result.TeamId = authority.TeamId; result.PlanetId = authority.PlanetId; result.AuthorityEpoch = authority.AuthorityEpoch;
            result.HostAvailable = authority.HostAvailable; result.CanContinue = authority.CanContinue;
            if (viewer == null) return result;
            var entries = members.Select(m => new TeamColonyEntry { Kind = TeamColonyEntryKind.Member, ActorId = m.ActorId,
                Permissions = m.Permissions, Administrator = m.Administrator, Online = m.Online }).ToList();
            foreach (var dupe in duplicants.Values.OrderBy(d => d.EntityId, StringComparer.Ordinal))
            {
                if (dupe.ActorId != recipient && (viewer.Permissions & TeamPermissionFlags.CommandDuplicants) == 0) continue;
                entries.Add(dupe.Copy());
                entries.AddRange(delegates[dupe.EntityId].OrderBy(id => id, StringComparer.Ordinal).Select(id =>
                    new TeamColonyEntry { Kind = TeamColonyEntryKind.Delegate, EntityId = dupe.EntityId, ActorId = id, Enabled = true }));
            }
            result.Entries = entries.ToArray(); return result;
        }
        private static TeamColonyMessage Header(TeamColonyMessageKind kind, TeamColonySnapshot state)
        { return new TeamColonyMessage { Kind = kind, RoomId = state.RoomId, TeamId = state.TeamId, PlanetId = state.PlanetId, AuthorityEpoch = state.AuthorityEpoch }; }
        private static bool SameHeader(TeamColonySnapshot a, TeamColonySnapshot b)
        {
            return a.Revision == b.Revision && a.TeamId == b.TeamId && a.PlanetId == b.PlanetId && a.AuthorityEpoch == b.AuthorityEpoch &&
                a.AuthorityPeerId == b.AuthorityPeerId && a.HostAvailable == b.HostAvailable && a.CanContinue == b.CanContinue &&
                a.Invited == b.Invited && a.IsMember == b.IsMember;
        }
        private bool Reject(string reason) { LastError = reason; Notify(); return false; }
        private static bool RequiresPeerTarget(TeamColonyMessageKind kind, TeamPermissionFlags permissions, bool enabled)
        { return kind == TeamColonyMessageKind.Invite || kind == TeamColonyMessageKind.TransferAdministration ||
            kind == TeamColonyMessageKind.Permissions && permissions != TeamPermissionFlags.None ||
            (kind == TeamColonyMessageKind.Administrator || kind == TeamColonyMessageKind.Delegate) && enabled; }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; session.ApplicationMessageReceived -= Receive;
            foreach (var actor in applied.Keys.ToArray()) if (applied[actor] != TeamPermissionFlags.None) applyPermissions(actor, TeamPermissionFlags.None);
            pending.Clear(); outbound.Clear(); receipts.Clear(); receiptCounts.Clear(); receiptBytes = 0;
            applied.Clear(); verifiedDupeOwners.Clear(); snapshot = null; authority = null;
        }
    }
}
