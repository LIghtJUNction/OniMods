using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class CommandAdmissionTests
{
    private sealed class Context : IWorldWorkContext
    {
        internal readonly WorkPlanet Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"),
            AuthorityEpoch = Guid.NewGuid().ToString("N"), NativeWorldId = 0 };
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return true; }
    }
    private sealed class Executor : IWorkExecutor, IDupeCommandExecutor
    {
        internal int Preflights, Applied, Moves;
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request)
        { Preflights++; return WorkExecution.Accept(request.Cells.Length); }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] before, WorkTaskIntent[] desired)
        { Applied++; return WorkExecution.Accept(desired.Length); }
        public WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request)
        { Preflights++; return WorkExecution.Accept(1); }
        public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
        { Moves++; return WorkExecution.Accept(1); }
    }
    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Net = new DeterministicRoomNetwork(81621);
        internal readonly RoomSession Host, Guest;
        internal readonly Context Context = new Context();
        internal readonly Executor Native = new Executor();
        internal readonly RemoteWorkCoordinator Work, GuestWork;
        internal readonly DupeCommandCoordinator Dupes, GuestDupes;
        internal readonly TeamColonyCoordinator Team, GuestTeam;
        internal readonly Dictionary<string, RemoteWorkReceipt> Receipts = new Dictionary<string, RemoteWorkReceipt>();
        internal bool GuestReady = true, ThrowGate;
        internal long Admission = 1;
        internal Room()
        {
            Host = new RoomSession(Net.Host, new SessionProfile { Name = "Authority" }, "", "Admission");
            Guest = new RoomSession(Net.Connect(), new SessionProfile { Name = "Observer" }, "", "Admission", Net.Connect,
                new SessionOptions { RetryJitter = () => .5 });
            Context.Planet.OwnerPeerId = Context.Planet.AuthorityPeerId = Host.LocalPlayerId;
            Func<string, string> owner = planet => planet == Context.Planet.PlanetId ? Host.LocalPlayerId : null;
            Func<string, bool> gate = peer => { if (ThrowGate) throw new InvalidOperationException("plugin fault"); return peer != Guest.LocalPlayerId || GuestReady; };
            Func<string, long> generation = peer => gate(peer) ? Admission * 100000 + Host.ConnectionGenerationFor(peer) : 0;
            Work = new RemoteWorkCoordinator(Host, Context, Native, owner, gate, generation);
            GuestWork = new RemoteWorkCoordinator(Guest, new Context(), new Executor(), owner, gate, generation);
            Dupes = new DupeCommandCoordinator(Host, Context, Native, owner, (actor, dupe) => true, gate, generation);
            GuestDupes = new DupeCommandCoordinator(Guest, new Context(), new Executor(), owner, (actor, dupe) => true, gate, generation);
            Team = new TeamColonyCoordinator(Host, () => Context.Planet, canUsePeer: gate, admissionGeneration: generation);
            GuestTeam = new TeamColonyCoordinator(Guest, () => null, canUsePeer: gate, admissionGeneration: generation);
            Guest.ApplicationMessageReceived += (sender, bytes) => {
                if (!RemoteWorkWire.IsMessage(bytes)) return;
                var message = RemoteWorkWire.Decode(bytes);
                if (message.Kind == RemoteWorkMessageKind.Receipt) Receipts[message.Receipt.RequestId] = message.Receipt;
            };
            Until(() => Guest.Joined, "guest authenticated");
        }
        internal void Step()
        {
            Net.Advance(.05); Host.Pump(Net.Now); Guest.Pump(Net.Now);
            Work.Pump(Net.Now); GuestWork.Pump(Net.Now); Dupes.Pump(Net.Now); GuestDupes.Pump(Net.Now);
            Team.Pump(Net.Now); GuestTeam.Pump(Net.Now);
        }
        internal void Until(Func<bool> test, string message)
        { for (int i = 0; i < 1600; i++) { Step(); if (test()) return; } throw new Exception("admission timeout: " + message); }
        internal void Send(RemoteWorkRequest request)
        { Guest.SendApplication(RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = Host.LocalPlayerId, Request = request })); }
        internal RemoteWorkRequest Request(RemoteWorkGrant grant)
        { return new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = Context.Planet.PlanetId,
            AuthorityEpoch = Context.Planet.AuthorityEpoch, GrantToken = grant.Token, DryRun = true,
            Kind = RemoteWorkKind.Dig, Priority = 5, Cells = new[] { new WorkCell(10, 10) } }; }
        public void Dispose()
        { GuestTeam.Dispose(); Team.Dispose(); GuestDupes.Dispose(); Dupes.Dispose(); GuestWork.Dispose(); Work.Dispose(); Guest.Dispose(); Host.Dispose(); Net.Dispose(); }
    }
    internal static void Run(Action<bool, string> check)
    {
        using (var room = new Room())
        {
            var grant = room.Work.SetPermissions(room.Guest.LocalPlayerId, RemoteWorkPermission.IssueTasks);
            var preview = room.Request(grant); room.Send(preview);
            room.Until(() => room.Receipts.ContainsKey(preview.RequestId), "preview reply");
            check(room.Receipts[preview.RequestId].Accepted, "ready authenticated peer reaches production preflight");
            var commit = room.Request(grant); commit.DryRun = false; commit.PreviewToken = room.Receipts[preview.RequestId].PreviewToken;
            room.Send(commit); room.Until(() => room.Receipts.ContainsKey(commit.RequestId), "commit reply");
            check(room.Receipts[commit.RequestId].Accepted && room.Native.Applied == 1, "ready peer confirms one native task layer");
            int preflights = room.Native.Preflights;
            room.GuestReady = false;
            room.Send(room.Request(grant));
            for (int i = 0; i < 40; i++) room.Step();
            check(room.Native.Preflights == preflights, "in-room peer without mod admission cannot reach preflight");
            check(room.Work.Authority.GetMergedPlans().Length == 1 && room.Native.Applied == 1, "admission loss preserves accepted native task layer");
            check(room.Work.Authority.GetGrant(room.Guest.LocalPlayerId).Token != grant.Token, "admission loss retires prior grant token");
            room.GuestReady = true; room.Admission++;
            var old = room.Request(grant); room.Send(old);
            room.Until(() => room.Receipts.ContainsKey(old.RequestId), "stale token reply");
            check(!room.Receipts[old.RequestId].Accepted && room.Native.Preflights == preflights, "renewed admission rejects old grant before native preflight");
            grant = room.Work.Authority.GetGrant(room.Guest.LocalPlayerId);
            long connection = room.Host.ConnectionGenerationFor(room.Guest.LocalPlayerId);
            room.Net.CutAll(); room.Until(() => room.Guest.Joined && room.Host.ConnectionGenerationFor(room.Guest.LocalPlayerId) > connection, "same seat reconnected");
            old = room.Request(grant); room.Send(old); room.Until(() => room.Receipts.ContainsKey(old.RequestId), "connection token reply");
            check(!room.Receipts[old.RequestId].Accepted && room.Work.Authority.GetMergedPlans().Length == 1,
                "same authenticated seat new connection cannot reuse old grant and keeps accepted plan");
            grant = room.Work.Authority.GetGrant(room.Guest.LocalPlayerId);
            room.Admission++; // No intervening false observation: compare actual generation as well.
            old = room.Request(grant); room.Send(old); room.Until(() => room.Receipts.ContainsKey(old.RequestId), "generation token reply");
            check(!room.Receipts[old.RequestId].Accepted && room.Native.Applied == 1, "nonzero admission generation change retires old token without a false transition");
        }
        using (var room = new Room())
        {
            var grant = room.Work.SetPermissions(room.Guest.LocalPlayerId, RemoteWorkPermission.IssueTasks);
            room.Until(() => room.GuestWork.GetReceivedGrant(room.Host.LocalPlayerId, room.Context.Planet.PlanetId)?.Token == grant.Token, "grant received");
            var draft = room.GuestWork.Draft(room.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(10, 10) });
            check(room.GuestWork.SubmitDraft(room.Host.LocalPlayerId, draft, true), "ready guest queues preview and confirmation intent");
            room.Admission++;
            for (int i = 0; i < 50; i++) room.Step();
            check(room.Native.Applied == 0 && room.Native.Preflights == 0,
                "retired admission never automatically replays a queued work confirmation");
            string move = room.GuestDupes.SubmitMove(room.Context.Planet.PlanetId, room.Context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
            check(move != null, "ready guest may draft pending move");
            room.Admission++;
            for (int i = 0; i < 50; i++) room.Step();
            check(room.Native.Moves == 0 && room.GuestDupes.GetSubmission(move).OutcomeUnknown,
                "a move queued under retired admission never executes after renewal");
            room.GuestReady = false;
            var forged = new DupeMoveRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = room.Context.Planet.PlanetId,
                AuthorityEpoch = room.Context.Planet.AuthorityEpoch, DupeId = 42, Target = new WorkCell(10, 11) };
            room.Guest.SendApplication(DupeCommandWire.Encode(new DupeCommandMessage { TargetId = room.Host.LocalPlayerId, Request = forged }));
            for (int i = 0; i < 30; i++) room.Step();
            check(room.Native.Moves == 0, "raw authenticated move frame cannot bypass current mod admission");
            room.ThrowGate = true;
            check(room.GuestDupes.SubmitMove(room.Context.Planet.PlanetId, room.Context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11)) == null,
                "admission provider fault refuses native move");
        }
        using (var room = new Room())
        {
            var permissions = RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans;
            room.Work.SetPermissions(room.Guest.LocalPlayerId, permissions);
            room.GuestReady = false;
            var same = room.Work.SetPermissions(room.Guest.LocalPlayerId, permissions);
            check(same.Permissions == permissions, "offline permission callback may reaffirm its existing grant");
            var reduced = room.Work.SetPermissions(room.Guest.LocalPlayerId, RemoteWorkPermission.ViewPlans);
            check(reduced.Permissions == RemoteWorkPermission.ViewPlans, "offline permission callback may explicitly reduce an existing grant");
            bool rejected = false;
            try { room.Work.SetPermissions(room.Guest.LocalPlayerId, permissions); }
            catch (InvalidOperationException error) { rejected = error.Message == "mod_sync_not_ready"; }
            check(rejected, "offline permission callback cannot add new privileges");
            room.Work.SetPermissions(room.Guest.LocalPlayerId, RemoteWorkPermission.None);
            check(room.Work.Authority.GetGrant(room.Guest.LocalPlayerId).Permissions == RemoteWorkPermission.None,
                "offline cleanup may revoke the remaining grant");
        }
        using (var room = new Room())
        {
            check(room.Team.CreateTeam(), "ready native host creates team");
            check(room.Team.Invite(room.Guest.LocalPlayerId), "ready peer receives invitation");
            room.Until(() => room.GuestTeam.Snapshot?.Invited == true, "invitation delivered");
            check(room.GuestTeam.AcceptInvitation(), "ready peer queues team acceptance");
            room.Admission++;
            for (int i = 0; i < 50; i++) room.Step();
            check(room.GuestTeam.PendingCount == 0 && room.GuestTeam.Snapshot?.IsMember == false,
                "retired admission does not automatically replay queued team mutation");
            room.GuestReady = false;
            check(!room.Team.Invite(room.Guest.LocalPlayerId) && !room.GuestTeam.AcceptInvitation(),
                "host and guest team entry points both enforce mod admission");
            var old = room.GuestTeam.Snapshot;
            room.Guest.SendApplication(TeamColonyWire.Encode(new TeamColonyMessage { Kind = TeamColonyMessageKind.Accept,
                RoomId = room.Guest.RoomId, TeamId = old.TeamId, PlanetId = old.PlanetId, AuthorityEpoch = old.AuthorityEpoch,
                RequestId = Guid.NewGuid().ToString("N") }));
            for (int i = 0; i < 30; i++) room.Step();
            room.GuestReady = true;
            for (int i = 0; i < 110; i++) room.Step();
            check(room.GuestTeam.Snapshot?.IsMember == false, "raw authenticated team mutation cannot bypass current mod admission");
        }
    }
}
