using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class DupeCommandTests
{
    private sealed class Context : IWorldWorkContext
    {
        internal readonly WorkPlanet Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"), AuthorityEpoch = Guid.NewGuid().ToString("N"), OwnerPeerId = "owner", AuthorityPeerId = "owner" };
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return true; }
    }
    private sealed class Executor : IDupeCommandExecutor
    {
        internal int Calls;
        internal bool Explored = true;
        internal bool Reachable = true;
        internal bool ThrowAfterExecute;
        internal Action OnPreflight;
        internal readonly List<string> Executed = new List<string>();
        public WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request)
        {
            if (OnPreflight != null) OnPreflight();
            return !Explored ? WorkExecution.Reject("dupe_unexplored") : !Reachable ? WorkExecution.Reject("dupe_unreachable") : WorkExecution.Accept(1);
        }
        public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
        { Calls++; Executed.Add(request.RequestId); if (ThrowAfterExecute) throw new InvalidOperationException("native callback interrupted"); return WorkExecution.Accept(1); }
    }

    internal static void Run(Action<bool, string> check)
    {
        AuthorityAndReplay(check);
        SubmitBeforeFirstPump(check);
        RemoteWorkBeforeFirstPump(check);
        SessionRouting(check);
        WireBounds(check);
        Console.WriteLine("Dupe commands: authenticated peer routing, immediate pending, authority preflight, navigation rejection, revocation, replay and stale epochs. Native MoveTo bridge compiled separately; game runtime validation remains gated.");
    }
    private static DupeMoveRequest Request(Context context)
    { return new DupeMoveRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId, AuthorityEpoch = context.Planet.AuthorityEpoch, DupeId = 42, Target = new WorkCell(10, 11) }; }
    private static void SubmitBeforeFirstPump(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(7719))
        using (var session = new RoomSession(network.Host, new SessionProfile { Name = "Player", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Clock fixture"))
        {
            var context = new Context(); var executor = new Executor();
            context.Planet.OwnerPeerId = context.Planet.AuthorityPeerId = session.LocalPlayerId;
            using (var commands = new DupeCommandCoordinator(session, context, executor, planet => session.LocalPlayerId, (actor, dupe) => true))
            {
                string id = commands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                check(commands.GetSubmission(id).Pending, "submission before first pump immediately exposes pending");
                commands.Pump(1000);
                check(commands.GetSubmission(id).Accepted && executor.Calls == 1, "first pump at nonzero game uptime anchors submission instead of instantly timing out");
            }
            using (var commands = new DupeCommandCoordinator(session, context, executor, planet => "offline-authority", (actor, dupe) => true))
            {
                string id = commands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                commands.Pump(1000); commands.Pump(1089.99);
                check(commands.GetSubmission(id).Pending, "first-pump anchored timeout remains pending before ninety elapsed seconds");
                commands.Pump(1090);
                check(commands.GetSubmission(id).OutcomeUnknown && !commands.GetSubmission(id).Pending, "first-pump anchoring preserves the original ninety-second deadline");
            }
        }
    }
    private sealed class WorkExecutor : IWorkExecutor
    {
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request) { return WorkExecution.Accept(request.Cells.Length); }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired) { return WorkExecution.Accept(desired.Length); }
    }
    private static void RemoteWorkBeforeFirstPump(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(7720))
        using (var host = new RoomSession(network.Host, new SessionProfile { Name = "Host", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, "", "Work clock fixture"))
        using (var guest = new RoomSession(network.Connect(), new SessionProfile { Name = "Guest", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, ""))
        {
            for (int i = 0; i < 100 && (!guest.Joined || guest.Players.Length != 2); i++)
            { network.Advance(.05); host.Pump(network.Now); guest.Pump(network.Now); }
            check(guest.Joined && guest.Players.Length == 2, "work clock fixture uses two authenticated production sessions");
            var ownerContext = new Context(); var actorContext = new Context();
            ownerContext.Planet.OwnerPeerId = ownerContext.Planet.AuthorityPeerId = host.LocalPlayerId;
            actorContext.Planet.OwnerPeerId = actorContext.Planet.AuthorityPeerId = guest.LocalPlayerId;
            Func<string, string> lookup = planet => planet == ownerContext.Planet.PlanetId ? host.LocalPlayerId : guest.LocalPlayerId;
            using (var owner = new RemoteWorkCoordinator(host, ownerContext, new WorkExecutor(), lookup))
            using (var actor = new RemoteWorkCoordinator(guest, actorContext, new WorkExecutor(), lookup))
            {
                owner.Pump(1000);
                var grant = owner.SetPermissions(guest.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = ownerContext.Planet.PlanetId,
                    AuthorityEpoch = ownerContext.Planet.AuthorityEpoch, GrantToken = grant.Token, Kind = RemoteWorkKind.Dig,
                    DryRun = true, Cells = new[] { new WorkCell(10, 11) } };
                var receipts = new List<RemoteWorkReceipt>();
                actor.ReceiptReceived += (peer, receipt) => receipts.Add(receipt);
                check(actor.Submit(host.LocalPlayerId, request), "remote work can submit before its first clock sample");
                actor.Pump(1000);
                check(receipts.Count == 0 && actor.LastError != "work_response_unconfirmed", "remote work first pump at uptime one thousand does not consume ninety-second response budget");
                for (int i = 0; i < 100 && receipts.Count == 0; i++)
                {
                    network.Advance(.05); host.Pump(network.Now); guest.Pump(network.Now);
                    owner.Pump(1000 + network.Now); actor.Pump(1000 + network.Now);
                }
                check(receipts.Count == 1 && receipts[0].Accepted && receipts[0].DryRun, "anchored remote work receives actual authority preflight acknowledgement");
            }
            using (var actor = new RemoteWorkCoordinator(guest, actorContext, new WorkExecutor(), lookup))
            {
                var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = ownerContext.Planet.PlanetId,
                    AuthorityEpoch = ownerContext.Planet.AuthorityEpoch, GrantToken = "", Kind = RemoteWorkKind.Dig, DryRun = true, Cells = new[] { new WorkCell(10, 11) } };
                var receipts = new List<RemoteWorkReceipt>();
                actor.ReceiptReceived += (peer, receipt) => receipts.Add(receipt);
                actor.Submit(host.LocalPlayerId, request); actor.Pump(1000); actor.Pump(1089.99);
                check(receipts.Count == 0, "anchored remote work remains pending before ninety elapsed seconds");
                actor.Pump(1090);
                check(receipts.Count == 1 && receipts[0].Reason == "work_response_unconfirmed", "remote work anchoring keeps exact ninety-second deadline");
            }
        }
    }
    private static void AuthorityAndReplay(Action<bool, string> check)
    {
        var context = new Context(); var executor = new Executor(); bool grant = false;
        var authority = new DupeCommandAuthority("owner", context, executor, (actor, dupe) => actor == "owner" || grant && actor == "actor" && dupe == 42);
        var request = Request(context);
        check(authority.Handle("host", request).Reason == "dupe_not_authorized" && executor.Calls == 0, "room host cannot command another owner's duplicant");
        check(authority.Handle("actor", request).Reason == "dupe_not_authorized", "no implicit actor grant");
        grant = true;
        check(authority.Handle("actor", request).Reason == "dupe_not_authorized" && executor.Calls == 0, "old rejected command does not become executable after a later grant");
        request = Request(context);
        check(authority.Handle("actor", request).Accepted && executor.Calls == 1, "granted actor receives native execution acknowledgement");
        check(authority.Handle("actor", request).Accepted && executor.Calls == 1, "same request replay acknowledges history without a second chore");
        request.Target.X++;
        check(authority.Handle("actor", request).Reason == "dupe_request_id_conflict" && executor.Calls == 1, "request ID cannot be rebound to another target");
        request = Request(context); executor.Explored = false;
        check(authority.Handle("actor", request).Reason == "dupe_unexplored" && executor.Calls == 1, "authority rejects unexplored target before execution");
        executor.Explored = true; executor.Reachable = false;
        check(authority.Handle("actor", Request(context)).Reason == "dupe_unreachable" && executor.Calls == 1, "authority rejects unreachable target before execution");
        executor.Reachable = true; executor.OnPreflight = () => grant = false;
        check(authority.Handle("actor", Request(context)).Reason == "dupe_not_authorized" && executor.Calls == 1, "revocation between preflight and execute prevents move");
        executor.OnPreflight = null; grant = true; request = Request(context);
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(authority.Handle("actor", request).Reason == "dupe_stale_world" && executor.Calls == 1, "previous load epoch rejects queued move");
        executor.OnPreflight = () => context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(authority.Handle("actor", Request(context)).Reason == "dupe_stale_world" && executor.Calls == 1, "world replacement during preflight cannot execute stale command");
        executor.OnPreflight = null; context.Planet.OwnerPeerId = "guest";
        check(authority.Handle("actor", Request(context)).Reason == "dupe_not_local_authority", "guest-owned hosted world stays closed before handover protocol exists");
        context.Planet.OwnerPeerId = "owner"; executor.ThrowAfterExecute = true; request = Request(context);
        DupeCommandReceipt unknown = authority.Handle("actor", request);
        int calls = executor.Calls;
        check(!unknown.Accepted && unknown.OutcomeUnknown, "exception after native callback reports unknown rather than fabricated success");
        check(authority.Handle("actor", request).OutcomeUnknown && executor.Calls == calls, "unknown native result is retained and never blindly reexecuted");
        context = new Context(); executor = new Executor(); authority = new DupeCommandAuthority("owner", context, executor, (actor, dupe) => true);
        var first = Request(context);
        authority.Handle("owner", first);
        for (int i = 1; i < DupeCommandAuthority.MaxLedger; i++) authority.Handle("owner", Request(context));
        check(authority.Handle("owner", Request(context)).Reason == "dupe_ledger_full", "full dedup ledger refuses new work instead of evicting successful IDs");
        check(authority.Handle("owner", first).Accepted && executor.Calls == DupeCommandAuthority.MaxLedger, "oldest ID remains idempotent at ledger capacity");
    }

    private sealed class Peer : IDisposable
    {
        internal readonly Context Context = new Context();
        internal readonly Executor Executor = new Executor();
        internal readonly RoomSession Session;
        internal readonly DupeCommandCoordinator Commands;
        internal bool Grant;
        internal Peer(IRoomTransport transport, Func<IRoomTransport> reconnect, Func<string, string> lookup)
        {
            Session = new RoomSession(transport, new SessionProfile { Name = "Player", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true },
                "", "Dupe command test", reconnect: reconnect, options: new SessionOptions { RetryJitter = () => .5 });
            Commands = new DupeCommandCoordinator(Session, Context, Executor, lookup, (actor, dupe) => actor == Session.LocalPlayerId || Grant && dupe == 42);
        }
        internal void Pump(double now)
        {
            Session.Pump(now);
            Context.Planet.OwnerPeerId = Context.Planet.AuthorityPeerId = Session.LocalPlayerId;
            Commands.Pump(now);
        }
        public void Dispose() { Commands.Dispose(); Session.Dispose(); }
    }

    private static void SessionRouting(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(482217))
        {
            network.Settings.ReliableOrdered = true; network.Settings.Delay = 1.5; network.Settings.Jitter = .3;
            network.Settings.Drop = .15; network.Settings.Reorder = .05; network.Settings.Duplicate = .1;
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet).Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var p in peers) p.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double deadline = network.Now + 100;
                while (network.Now < deadline) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Dupe command timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), "three actual protocol sessions authenticate under delayed lossy network");
                Peer host = peers[0], owner = peers[1], actor = peers[2];
                string planet = owner.Context.Planet.PlanetId, epoch = owner.Context.Planet.AuthorityEpoch;
                string id = actor.Commands.SubmitMove(planet, epoch, 42, new WorkCell(10, 11));
                check(id != null && actor.Commands.GetSubmission(id).Pending && owner.Executor.Calls == 0, "local pending feedback appears immediately before first network delivery");
                until(() => !actor.Commands.GetSubmission(id).Pending, "denied remote move receives rejection");
                check(actor.Commands.GetSubmission(id).Reason == "dupe_not_authorized" && owner.Executor.Calls == 0, "guest-to-guest relay preserves actor identity for permission checks");
                owner.Grant = true;
                bool dropReceipts = true;
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    Packet packet;
                    try { packet = Protocol.Decode(bytes); } catch { return false; }
                    if (!dropReceipts || packet.Payload == null || !DupeCommandWire.IsMessage(packet.Payload)) return false;
                    return DupeCommandWire.Decode(packet.Payload).Receipt != null;
                };
                id = actor.Commands.SubmitMove(planet, epoch, 42, new WorkCell(12, 11));
                until(() => owner.Executor.Calls == 1, "authorized guest-to-guest move executes on actual authority");
                check(actor.Commands.GetSubmission(id).Pending, "lost acknowledgement remains pending despite native execution");
                string actorId = actor.Session.LocalPlayerId;
                string ownerId = owner.Session.LocalPlayerId;
                network.CutAll();
                until(() => peers.Skip(1).All(p => p.Session.Joined && !p.Session.Reconnecting), "guests reconnect with preserved session identity");
                dropReceipts = false;
                until(() => !actor.Commands.GetSubmission(id).Pending, "same request retries after reconnect recover authority receipt");
                check(actor.Commands.GetSubmission(id).Accepted && owner.Executor.Calls == 1 && actor.Session.LocalPlayerId == actorId,
                    "reconnection never reissues accepted native move: receipt=" + actor.Commands.GetSubmission(id).Reason + ", calls=" + owner.Executor.Calls + ", sameActor=" + (actor.Session.LocalPlayerId == actorId) + ", sameOwner=" + (owner.Session.LocalPlayerId == ownerId) + ", IDs=" + string.Join(",", owner.Executor.Executed));
                owner.Grant = false;
                id = actor.Commands.SubmitMove(planet, epoch, 42, new WorkCell(13, 11));
                until(() => !actor.Commands.GetSubmission(id).Pending, "revoked grant returns denied receipt");
                check(!actor.Commands.GetSubmission(id).Accepted && owner.Executor.Calls == 1, "revocation is checked on authority after local echo");
                string hostId = host.Commands.SubmitMove(planet, epoch, 42, new WorkCell(14, 11));
                until(() => !host.Commands.GetSubmission(hostId).Pending, "room host move also goes through guest authority");
                check(!host.Commands.GetSubmission(hostId).Accepted && owner.Executor.Calls == 1, "relay host has no implicit dupe command privilege");
                string localId = owner.Commands.SubmitMove(planet, epoch, 42, new WorkCell(15, 11));
                check(owner.Commands.GetSubmission(localId).Pending && owner.Executor.Calls == 1, "local owner uses same immediate pending contract");
                step();
                check(owner.Commands.GetSubmission(localId).Accepted && owner.Executor.Calls == 2, "local owner executes on next pump after native preflight");
            }
            finally { foreach (Peer peer in peers) peer.Dispose(); }
        }
    }

    private static void WireBounds(Action<bool, string> check)
    {
        var context = new Context(); var request = Request(context);
        byte[] encoded = DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "target", Request = request });
        check(DupeCommandWire.Decode(encoded).Request.SameAs(request), "bounded move wire round trips exact ID, epoch, dupe and coordinate");
        var trailing = encoded.Concat(new byte[] { 0 }).ToArray(); bool rejected = false;
        try { DupeCommandWire.Decode(trailing); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "move wire rejects trailing data");
        rejected = false; request.Target.X = -1;
        try { DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "target", Request = request }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "move wire rejects negative targets before queuing");
    }
}
