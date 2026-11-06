using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class TeamCoordinatorTests
{
    private static System.Action<bool, string> check;
    private sealed class Peer : IDisposable
    {
        internal readonly RoomSession Session;
        internal readonly TeamColonyCoordinator Team;
        internal readonly DeterministicRoomNetwork.Endpoint InitialTransport;
        internal readonly Dictionary<string, TeamPermissionFlags> Applied = new Dictionary<string, TeamPermissionFlags>();
        internal readonly WorkPlanet Planet;
        internal bool NativeReady = true;
        internal Peer(IRoomTransport transport, System.Func<IRoomTransport> reconnect)
        {
            InitialTransport = (DeterministicRoomNetwork.Endpoint)transport;
            Session = new RoomSession(transport, new SessionProfile { Name = "Team player", GameVersion = "744825",
                CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Team system", reconnect,
                new SessionOptions { RetryJitter = () => .5, PeerTimeoutSeconds = 20, ReconnectWindowSeconds = 90,
                    SeatReservationSeconds = 120, HeartbeatIntervalSeconds = 1, HandshakeTimeoutSeconds = 15 });
            Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"), AuthorityEpoch = Guid.NewGuid().ToString("N"),
                NativeWorldId = 0, OwnerPeerId = Session.LocalPlayerId, AuthorityPeerId = Session.LocalPlayerId };
            Team = new TeamColonyCoordinator(Session, () => NativeReady ? Planet : null,
                (actor, flags) => Applied[actor] = flags);
        }
        internal void Pump(double now) { Session.Pump(now); Team.Pump(now); }
        public void Dispose() { Team.Dispose(); Session.Dispose(); }
    }
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(95233);
        internal readonly List<Peer> Peers = new List<Peer>();
        internal Peer Host { get { return Peers[0]; } }
        internal Room() { Peers.Add(new Peer(Network.Host, null)); }
        internal Peer Join() { var peer = new Peer(Network.Connect(), Network.Connect); Peers.Add(peer); return peer; }
        internal void Step(double seconds = .05)
        { Network.Advance(seconds); foreach (var peer in Peers) peer.Pump(Network.Now); }
        internal void Until(System.Func<bool> condition, string message, double seconds = 60)
        {
            double end = Network.Now + seconds;
            do { Step(); if (condition()) { check(true, message); return; } } while (Network.Now <= end);
            throw new Exception("Team network timeout: " + message + "; " + string.Join(";", Peers.Select(p => p.Session.LastError + "/" + p.Team.LastError)));
        }
        internal void InviteAndAccept(Peer peer)
        {
            check(Host.Team.Invite(peer.Session.LocalPlayerId), "administrator invites authenticated room peer");
            Until(() => peer.Team.Snapshot != null && peer.Team.Snapshot.Invited, "only invitee receives pending invitation");
            check(peer.Team.AcceptInvitation(), "invitee actively queues acceptance");
            Until(() => peer.Team.PendingCount == 0 && peer.Team.Snapshot.IsMember, "host confirms explicit membership acceptance");
        }
        public void Dispose() { foreach (var peer in Peers) peer.Dispose(); Network.Dispose(); }
    }
    internal static void Run(System.Action<bool, string> assertion)
    {
        check = assertion;
        NativeAuthorityAndPrivacy();
        DelegationAndHandoff();
        LostReceiptsAndReconnect();
        PagedMembership();
        WireValidation();
    }
    private static void NativeAuthorityAndPrivacy()
    {
        using (var room = new Room())
        {
            var bob = room.Join(); var eve = room.Join();
            room.Until(() => bob.Session.Joined && eve.Session.Joined, "authenticated production sessions join");
            check(!bob.Team.CreateTeam(), "guest cannot claim computation or create a host-native team");
            room.Host.NativeReady = false;
            check(!room.Host.Team.CreateTeam(), "unloaded or unadmitted host world cannot create team");
            room.Host.NativeReady = true;
            string owner = room.Host.Planet.OwnerPeerId; room.Host.Planet.OwnerPeerId = bob.Session.LocalPlayerId;
            check(!room.Host.Team.CreateTeam(), "guest-owned planet cannot pretend its computation migrated to host");
            room.Host.Planet.OwnerPeerId = owner;
            check(room.Host.Team.CreateTeam(), "loaded admitted host-owned planet creates team");
            room.Until(() => eve.Team.Snapshot != null, "outsider receives only neutral team availability response");
            check(eve.Team.Snapshot.TeamId == "" && eve.Team.Snapshot.Entries.Length == 0 && !eve.Team.Snapshot.Invited,
                "outsider sees no membership, invitation, planet, or dupe details");
            check(room.Host.Team.Invite(bob.Session.LocalPlayerId), "host issues target-specific invitation");
            room.Until(() => bob.Team.Snapshot != null && bob.Team.Snapshot.Invited, "invitation delivered over room application protocol");
            check(!bob.Team.Snapshot.IsMember && bob.Team.Snapshot.Entries.Length == 0,
                "invited nonmember cannot see member or dupe registry");
            check(!eve.Team.Snapshot.Invited && eve.Team.Snapshot.TeamId == "", "other player cannot see someone else's invitation");
            check(bob.Team.AcceptInvitation(), "only invited authenticated actor accepts");
            room.Until(() => bob.Team.PendingCount == 0 && bob.Team.Snapshot.IsMember, "accepted member gets atomic snapshot");
            check(!room.Host.Team.CanIssue(bob.Session.LocalPlayerId) && room.Host.Applied[bob.Session.LocalPlayerId] == TeamPermissionFlags.None,
                "membership defaults to denied command permissions");
            check(bob.Team.Invite(eve.Session.LocalPlayerId), "nonadministrator request can be queued for authority decision");
            room.Until(() => bob.Team.PendingCount == 0, "unauthorized invite gets explicit authority receipt");
            check(bob.Team.LastError == "team_denied_or_stale" && !eve.Team.Snapshot.Invited,
                "ordinary member cannot invite another player");
            check(room.Host.Team.SetPermissions(bob.Session.LocalPlayerId, TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks),
                "host administrator grants independent plan and task permissions");
            room.Until(() => bob.Team.Snapshot.Members.Any(m => m.ActorId == bob.Session.LocalPlayerId &&
                m.Permissions == (TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks)), "permissions propagate in team snapshot");
            check(room.Host.Team.CanIssue(bob.Session.LocalPlayerId) && room.Host.Applied[bob.Session.LocalPlayerId] ==
                (TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks), "effective team permission callback feeds existing execution gate");
            check(room.Host.Team.SetPermissions(bob.Session.LocalPlayerId, TeamPermissionFlags.ViewPlans) &&
                !room.Host.Team.CanIssue(bob.Session.LocalPlayerId) && room.Host.Applied[bob.Session.LocalPlayerId] == TeamPermissionFlags.ViewPlans,
                "explicit role downgrade revokes task grant immediately, unlike temporary disconnect");
            check(room.Host.Team.SetPermissions(bob.Session.LocalPlayerId, TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks),
                "administrator may explicitly restore task publication right");
            var state = bob.Team.Snapshot;
            check(bob.Session.SendApplication(TeamColonyWire.Encode(new TeamColonyMessage { Kind = TeamColonyMessageKind.Permissions,
                RoomId = state.RoomId, TeamId = state.TeamId, PlanetId = state.PlanetId, AuthorityEpoch = "old-epoch",
                RequestId = Guid.NewGuid().ToString("N"), TargetId = bob.Session.LocalPlayerId, Permissions = (TeamPermissionFlags)7 })),
                "stale authority request reaches authenticated application boundary");
            for (int i = 0; i < 50; i++) room.Step();
            check(room.Host.Applied[bob.Session.LocalPlayerId] == (TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks),
                "wrong native epoch cannot elevate permissions");
            room.Host.NativeReady = false; room.Step();
            check(!room.Host.Team.Snapshot.HostAvailable && !room.Host.Team.Snapshot.CanContinue &&
                !room.Host.Team.CanIssue(bob.Session.LocalPlayerId) && room.Host.Applied[bob.Session.LocalPlayerId] ==
                    (TeamPermissionFlags.ViewPlans | TeamPermissionFlags.IssueTasks),
                "native unload closes new-command gate without cancelling previously accepted task layers");
            room.Until(() => !bob.Team.Snapshot.HostAvailable && !bob.Team.Snapshot.CanContinue, "members learn native host unavailable");
            room.Host.NativeReady = true; room.Step();
            check(room.Host.Team.CanIssue(bob.Session.LocalPlayerId), "same loaded planet and epoch restore explicit permissions");
            room.Host.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N"); room.Step();
            check(!room.Host.Team.CanIssue(bob.Session.LocalPlayerId) && !room.Host.Team.CreateTeam(),
                "new native epoch freezes old team instead of silently claiming state migration");
        }
    }
    private static void DelegationAndHandoff()
    {
        using (var room = new Room())
        {
            var bob = room.Join(); var eve = room.Join();
            room.Until(() => bob.Session.Joined && eve.Session.Joined, "dupe delegation peers join");
            check(room.Host.Team.CreateTeam(), "host creates native authority team");
            room.InviteAndAccept(bob); room.InviteAndAccept(eve);
            check(room.Host.Team.RegisterDuplicant("native-dupe-1", room.Host.Session.LocalPlayerId), "host registers already existing native dupe ownership");
            check(!room.Host.Team.RegisterDuplicant("native-dupe-1", bob.Session.LocalPlayerId), "registration cannot transfer existing dupe owner");
            check(room.Host.Team.SetPermissions(bob.Session.LocalPlayerId, TeamPermissionFlags.CommandDuplicants), "grant dupe command permission independently");
            check(!room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1"), "command flag without dupe owner's consent is insufficient");
            check(room.Host.Team.DelegateDuplicantToTeam("native-dupe-1", true), "owner volunteers dupe for team control");
            room.Until(() => bob.Team.Snapshot.Entries.Any(e => e.Kind == TeamColonyEntryKind.Duplicant && e.EntityId == "native-dupe-1" && e.Enabled),
                "authorized controller sees scoped delegated dupe metadata");
            check(room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1") &&
                !room.Host.Team.CanControl(eve.Session.LocalPlayerId, "native-dupe-1"), "team dupe delegation still requires individual command permission");
            check(!eve.Team.Snapshot.Entries.Any(e => e.Kind == TeamColonyEntryKind.Duplicant), "member without dupe rights sees no other owner's dupe registry");
            check(bob.Team.DelegateDuplicantToTeam("native-dupe-1", false), "nonowner revocation is only proposed to authority");
            room.Until(() => bob.Team.PendingCount == 0, "authority responds to nonowner dupe mutation");
            check(bob.Team.LastError == "team_denied_or_stale" && room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1"),
                "administrator or controller cannot replace original owner's dupe consent");
            check(room.Host.Team.DelegateDuplicantToTeam("native-dupe-1", false) &&
                !room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1"), "owner revocation is effective immediately");
            check(room.Host.Team.DelegateDuplicant("native-dupe-1", bob.Session.LocalPlayerId, true) &&
                room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1"), "owner delegates exactly one target member");
            check(!room.Host.Team.LeaveTeam(), "last administrator cannot leave without explicit successor");
            check(room.Host.Team.TransferAdministration(bob.Session.LocalPlayerId), "host hands team administration to online accepted guest");
            check(!room.Host.Team.Invite(Guid.NewGuid().ToString("N")), "host no longer has implied team administration after handoff");
            check(bob.Team.SetPermissions(eve.Session.LocalPlayerId, TeamPermissionFlags.IssueTasks), "guest administrator queues permission assignment");
            room.Until(() => bob.Team.PendingCount == 0 && room.Host.Team.CanIssue(eve.Session.LocalPlayerId), "guest administrator's authenticated request applies on host computation authority");
            check(room.Host.Team.LeaveTeam(), "former administrator voluntarily leaves team");
            check(room.Host.Applied[room.Host.Session.LocalPlayerId] == TeamPermissionFlags.None,
                "true departure withdraws static role grants instead of leaving stale execution access");
            room.Until(() => bob.Team.Snapshot.Members.Length == 2 && bob.Team.Snapshot.CanContinue, "remaining online team continues with host simulation process alive");
            check(!room.Host.Team.CanControl(bob.Session.LocalPlayerId, "native-dupe-1"), "owner departure withdraws dupe consent without gifting ownership");
            check(room.Host.Team.CanIssue(eve.Session.LocalPlayerId), "host need not remain team member for another member to command");
            room.Host.NativeReady = false; room.Step();
            check(!room.Host.Team.CanIssue(eve.Session.LocalPlayerId), "remaining team never simulates after host native service stops");
        }
    }
    private static void LostReceiptsAndReconnect()
    {
        using (var room = new Room())
        {
            var bob = room.Join();
            room.Until(() => bob.Session.Joined, "weak-network member joins");
            check(room.Host.Team.CreateTeam(), "weak-network team created");
            check(room.Host.Team.Invite(bob.Session.LocalPlayerId), "weak-network invitation starts");
            room.Until(() => bob.Team.Snapshot != null && bob.Team.Snapshot.Invited, "weak-network invite delivered");
            bool droppedReceipt = false;
            room.Network.Settings.DropBeforeDelivery = (fromHost, frame) =>
            {
                var packet = Protocol.Decode(frame);
                if (!fromHost || packet.Kind != PacketKind.Application || !TeamColonyWire.IsMessage(packet.Payload)) return false;
                var message = TeamColonyWire.Decode(packet.Payload);
                if (message.Kind != TeamColonyMessageKind.Receipt || droppedReceipt) return false;
                droppedReceipt = true; return true;
            };
            check(bob.Team.AcceptInvitation(), "membership acceptance queued despite receipt loss");
            room.Until(() => bob.Team.PendingCount == 0 && bob.Team.Snapshot.IsMember, "lost acceptance receipt recovered by idempotent retry");
            check(droppedReceipt && room.Host.Team.Snapshot.Members.Length == 2, "receipt retransmission does not duplicate membership");
            room.Network.Settings.DropBeforeDelivery = null;
            check(room.Host.Team.SetPermissions(bob.Session.LocalPlayerId, TeamPermissionFlags.IssueTasks), "grant tasks before link failure");
            room.Until(() => bob.Team.Snapshot.Members.Any(m => m.ActorId == bob.Session.LocalPlayerId && m.Permissions == TeamPermissionFlags.IssueTasks), "guest sees its pre-disconnect rights");
            string peerId = bob.Session.LocalPlayerId; string teamId = bob.Team.Snapshot.TeamId;
            bob.InitialTransport.Disconnect("host"); room.Step();
            check(bob.Session.Reconnecting && !bob.Team.Snapshot.CanContinue && !bob.Team.Snapshot.HostAvailable,
                "local reconnect immediately marks cached team unavailable");
            check(!room.Host.Team.CanIssue(peerId) && room.Host.Applied[peerId] == TeamPermissionFlags.IssueTasks,
                "host disconnect denies new commands while preserving role grants and existing task layers");
            room.Network.Settings.Delay = 1.5; room.Network.Settings.Jitter = .4;
            room.Network.Settings.Drop = .2; room.Network.Settings.ReliableOrdered = true;
            room.Until(() => bob.Session.Joined && !bob.Session.Reconnecting && bob.Team.Snapshot.CanContinue && room.Host.Team.CanIssue(peerId),
                "1.5 second delay and retransmission restore original authorized team member", 90);
            check(bob.Session.LocalPlayerId == peerId && bob.Team.Snapshot.TeamId == teamId && bob.Team.Snapshot.Members.Length == 2,
                "resume preserves stable room identity and team membership without duplication");
            check(room.Host.Team.SetAdministrator(peerId, true), "guest may become administrator under weak network");
            room.Until(() => bob.Team.Snapshot.Members.Any(m => m.ActorId == peerId && m.Administrator), "role update eventually converges under weak network");
            check(bob.Team.OfferContribution("voluntary-offer", "oxygen", 10), "member queues voluntary offer under latency");
            room.Until(() => bob.Team.PendingCount == 0, "resource offer receives authoritative receipt without generating native inventory", 90);
            check(bob.Team.LastError == null, "valid voluntary offer accepted");
            room.Host.Team.Dispose(); room.Step();
            check(room.Host.Applied[peerId] == TeamPermissionFlags.None, "team service disposal releases effective remote-work grants");
        }
    }
    private static void PagedMembership()
    {
        using (var room = new Room())
        {
            var guests = Enumerable.Range(0, 35).Select(i => room.Join()).ToArray();
            room.Until(() => guests.All(p => p.Session.Joined) && room.Host.Session.Players.Length == 36, "36 authenticated peers join without four-player limit");
            check(room.Host.Team.CreateTeam(), "large team created on host's native planet");
            foreach (var guest in guests) check(room.Host.Team.Invite(guest.Session.LocalPlayerId), "large team member invitation accepted by host");
            room.Until(() => guests.All(p => p.Team.Snapshot != null && p.Team.Snapshot.Invited), "individual invitations delivered to 35 peers without leaking other invites", 120);
            foreach (var guest in guests) check(guest.Team.AcceptInvitation(), "large team guest explicitly accepts");
            room.Until(() => guests.All(p => p.Team.PendingCount == 0 && p.Team.Snapshot.IsMember && p.Team.Snapshot.Members.Length == 36),
                "small snapshot pages atomically converge for all 36 members", 120);
            check(guests.All(p => p.Team.Snapshot.Members.Select(m => m.ActorId).Distinct().Count() == 36),
                "paged team membership contains every peer exactly once");
            foreach (var guest in guests)
                check(room.Host.Team.SetPermissions(guest.Session.LocalPlayerId, TeamPermissionFlags.CommandDuplicants),
                    "large team explicitly receives dupe metadata permission");
            for (int i = 0; i < 80; i++)
                check(room.Host.Team.RegisterDuplicant("existing-native-dupe-" + i, room.Host.Session.LocalPlayerId),
                    "native authority records an already existing dupe; test does not spawn a game entity");
            room.Until(() => guests.All(p => p.Team.Snapshot.Entries.Count(e => e.Kind == TeamColonyEntryKind.Duplicant) == 80),
                "snapshots taking longer than replay interval finish without query-induced page starvation", 120);
            long stableRevision = room.Host.Team.Snapshot.Revision;
            for (int i = 0; i < 120; i++) room.Step();
            check(room.Host.Team.Snapshot.Revision == stableRevision,
                "unchanged periodic replay preserves revision so slow partial snapshots can finish");
            TeamColonySnapshot last = guests[0].Team.Snapshot;
            last.Entries[0].ActorId = "altered-local-copy";
            check(!guests[0].Team.Snapshot.Members.Any(m => m.ActorId == "altered-local-copy"), "public snapshot copies cannot mutate canonical client team view");
        }
    }
    private static void WireValidation()
    {
        var request = new TeamColonyMessage { Kind = TeamColonyMessageKind.Invite, RoomId = "room", TeamId = "team", PlanetId = "planet",
            AuthorityEpoch = "epoch", RequestId = "request", TargetId = "target" };
        byte[] bytes = TeamColonyWire.Encode(request);
        check(TeamColonyWire.Decode(bytes).TargetId == "target", "production team command roundtrip");
        bool invalid = false;
        try { TeamColonyWire.Decode(bytes.Concat(new byte[] { 1 }).ToArray()); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "trailing command bytes rejected");
        invalid = false;
        try { TeamColonyWire.Decode(bytes.Take(10).ToArray()); } catch (IOException) { invalid = true; }
        check(invalid, "truncated team wire rejected");
        invalid = false;
        try { request.TargetId = "bad\nactor"; TeamColonyWire.Encode(request); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "control characters rejected in wire identities");
        invalid = false;
        try
        {
            var s = new TeamColonySnapshot { Revision = 1, IsMember = false, Entries = new[] {
                new TeamColonyEntry { Kind = TeamColonyEntryKind.Duplicant, ActorId = "owner", EntityId = "dupe" } } };
            var leaked = TeamColonyWire.Encode(new TeamColonyMessage { Kind = TeamColonyMessageKind.Snapshot, Snapshot = s, Total = 1 });
            TeamColonyWire.Decode(leaked);
        }
        catch (InvalidDataException) { invalid = true; }
        check(invalid, "nonmember snapshot with private entries rejected");
    }
}
