using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class DupeCommandLifecycleTests
{
    private sealed class Context : IWorldWorkContext
    {
        internal WorkPlanet Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"), AuthorityEpoch = Guid.NewGuid().ToString("N"),
            OwnerPeerId = "owner", AuthorityPeerId = "owner", NativeWorldId = 7 };
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return planet != null; }
    }

    // Native objects are stand-ins; ownership decisions use the production identity
    // guard. No Unity/game assembly or socket is loaded by these host regressions.
    private sealed class Executor : IDupeCommandExecutor, IDupeCommandLifecycle
    {
        private sealed class Task
        {
            internal DupeCommandTaskIdentity Identity;
            internal DupeMoveRequest Request;
            internal object Handle = new object();
            internal bool Completed;
            internal bool CancelAttempted;
        }
        private readonly object game = new object(), monitor = new object();
        private object world = new object();
        private readonly Dictionary<string, Task> leases = new Dictionary<string, Task>();
        private readonly Dictionary<int, object> current = new Dictionary<int, object>();
        internal int Executions, Cancellations, CancelAttempts;
        internal bool ThrowAfterExecute, ThrowBeforeCancel, ThrowAfterCancel, UnknownState;
        internal Action OnPreflight;
        internal readonly List<string> LeaseIds = new List<string>();
        public WorkExecution Preflight(WorkPlanet planet, DupeMoveRequest request)
        { if (OnPreflight != null) OnPreflight(); return WorkExecution.Accept(1); }
        public WorkExecution Execute(WorkPlanet planet, DupeMoveRequest request)
        {
            var task = new Task { Request = request.Copy() };
            task.Identity = new DupeCommandTaskIdentity(planet, request, game, world, monitor, task.Handle);
            leases.Add(request.NativeLeaseId, task); current[request.DupeId] = task.Handle;
            LeaseIds.Add(request.NativeLeaseId); Executions++;
            if (ThrowAfterExecute) throw new InvalidOperationException("after native chore creation");
            return WorkExecution.Accept(1);
        }
        public DupeCommandState GetState(WorkPlanet planet, DupeMoveRequest request)
        {
            Task task;
            if (!leases.TryGetValue(request.NativeLeaseId, out task) || !task.Identity.OwnsRequest(planet, request))
                return DupeCommandState.None;
            if (!task.Identity.MatchesScope(game, world))
            { leases.Remove(request.NativeLeaseId); return DupeCommandState.WorldChanged; }
            if (UnknownState) return DupeCommandState.None;
            object handle; current.TryGetValue(request.DupeId, out handle);
            DupeCommandState state = task.Completed ? DupeCommandState.Completed :
                task.Identity.MatchesNative(game, world, monitor, handle) ? DupeCommandState.Active :
                task.CancelAttempted ? DupeCommandState.Cancelled : DupeCommandState.Replaced;
            if (state >= DupeCommandState.Completed) leases.Remove(request.NativeLeaseId);
            return state;
        }
        public WorkExecution Cancel(WorkPlanet planet, DupeMoveRequest request)
        {
            CancelAttempts++;
            if (UnknownState) return WorkExecution.RejectBeforeMutation("dupe_cleanup_unknown");
            if (ThrowBeforeCancel) { ThrowBeforeCancel = false; throw new InvalidOperationException("before native cancel"); }
            Task task;
            if (!leases.TryGetValue(request.NativeLeaseId, out task) || !task.Identity.OwnsRequest(planet, request))
                return WorkExecution.RejectBeforeMutation("dupe_cleanup_unknown");
            object handle; current.TryGetValue(request.DupeId, out handle);
            if (!task.Identity.MatchesNative(game, world, monitor, handle)) return WorkExecution.Accept(0);
            task.CancelAttempted = true; current.Remove(request.DupeId); Cancellations++;
            if (ThrowAfterCancel) { ThrowAfterCancel = false; throw new InvalidOperationException("after native cancel"); }
            leases.Remove(request.NativeLeaseId);
            return WorkExecution.Accept(1);
        }
        internal void Complete(int dupe)
        {
            object handle; current.TryGetValue(dupe, out handle);
            foreach (Task task in leases.Values) if (ReferenceEquals(task.Handle, handle)) task.Completed = true;
        }
        internal void LocalMove(int dupe) { current[dupe] = new object(); }
        internal void ReplaceWorld() { world = new object(); current.Clear(); }
        internal bool HasTask(int dupe) { return current.ContainsKey(dupe); }
    }

    internal static void Run(Action<bool, string> check)
    {
        NativeIdentity(check);
        OwnershipAndReplay(check);
        RevocationAndWorldChanges(check);
        UncertainCleanup(check);
        LocalCoordinator(check);
        LifecycleReceipts(check);
        Wire(check);
        Console.WriteLine("Dupe lifecycle: exact native ownership, author cancellation, revoke/disconnect/dispose cleanup, terminal receipt ordering and read-only recovery. Host stand-ins only; no game runtime validation.");
    }

    private static DupeMoveRequest Request(Context context, int dupe = 42)
    { return new DupeMoveRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId,
        AuthorityEpoch = context.Planet.AuthorityEpoch, DupeId = dupe, Target = new WorkCell(10, 11) }; }
    private static DupeMoveRequest Cancel(DupeMoveRequest original)
    {
        var request = original.Copy(); request.RequestId = Guid.NewGuid().ToString("N");
        request.CancelTargetRequestId = original.RequestId; return request;
    }
    private static DupeCommandAuthority Authority(Context context, Executor executor, Func<string, int, bool> allowed = null)
    { return new DupeCommandAuthority(context.Planet.OwnerPeerId, context, executor, allowed ?? ((actor, dupe) => true)); }

    private static void NativeIdentity(Action<bool, string> check)
    {
        var context = new Context(); var request = Request(context); request.NativeLeaseId = Guid.NewGuid().ToString("N");
        var game = new object(); var world = new object(); var monitor = new object(); var chore = new object();
        var identity = new DupeCommandTaskIdentity(context.Planet, request, game, world, monitor, chore);
        check(identity.OwnsRequest(context.Planet, request.Copy()) && identity.MatchesNative(game, world, monitor, chore),
            "same exact native task remains owned across request value copies");
        check(!identity.MatchesNative(game, world, monitor, new object()), "a later local move to the identical cell has a different chore and is protected");
        check(!identity.MatchesNative(new object(), world, monitor, chore) && !identity.MatchesNative(game, new object(), monitor, chore) &&
            !identity.MatchesNative(game, world, new object(), chore), "game reload, reused world ID and replaced monitor cannot inherit native ownership");
        var reservation = new DupeCommandTaskIdentity(context.Planet, request, game, world, monitor, null);
        check(reservation.OwnsRequest(context.Planet, request) && !reservation.MatchesNative(game, world, monitor, null) &&
            !reservation.MatchesNative(game, world, monitor, chore), "unknown native reservation retains request scope without claiming any current task");
        check(reservation.MatchesScope(game, world) && !reservation.MatchesScope(game, new object()),
            "unobserved native reservation still distinguishes an actual world replacement");
        var forged = request.Copy(); forged.NativeLeaseId = Guid.NewGuid().ToString("N");
        check(!identity.OwnsRequest(context.Planet, forged), "same public request ID from another actor cannot inherit the authority's private native lease");
        forged = request.Copy(); forged.Target.X++;
        check(!identity.OwnsRequest(context.Planet, forged), "same private lease cannot be rebound to a changed destination");
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(!identity.OwnsRequest(context.Planet, request), "old task identity never binds to a new save epoch");
    }

    private static void OwnershipAndReplay(Action<bool, string> check)
    {
        var context = new Context(); var executor = new Executor(); var authority = Authority(context, executor);
        var first = Request(context);
        check(authority.Handle("actor", first).State == DupeCommandState.Active && executor.Executions == 1, "authority reports active only after capturing a real task identity");
        check(authority.Handle("actor", first).State == DupeCommandState.Active && executor.Executions == 1, "active move replay reads history without creating another task");
        var outsider = Cancel(first);
        check(authority.Handle("other", outsider).Reason == "dupe_request_unknown" && executor.Cancellations == 0, "even an authorized other actor cannot cancel somebody else's request");
        var mismatch = Cancel(first); mismatch.DupeId++;
        check(authority.Handle("actor", mismatch).Reason == "dupe_cancel_target_mismatch" && executor.Cancellations == 0, "cancellation must match original duplicant and target");
        var cancellation = Cancel(first);
        check(authority.Handle("actor", cancellation).Accepted && executor.Cancellations == 1, "author cancellation withdraws the captured native task");
        check(authority.Query("actor", first).State == DupeCommandState.Cancelled, "original request exposes its final cancelled state");
        var second = Request(context);
        authority.Handle("actor", second);
        check(authority.Handle("actor", cancellation).Accepted && executor.Cancellations == 1 && executor.HasTask(42), "replayed old cancel cannot stop a newer remote move");
        var delayed = Cancel(first);
        check(authority.Handle("actor", delayed).Reason == "dupe_already_finished" && executor.Cancellations == 1, "new cancellation of an old finished ID also protects its replacement");
        executor.LocalMove(42); authority.Reconcile();
        check(authority.Query("actor", second).State == DupeCommandState.Replaced && executor.Cancellations == 1 && executor.HasTask(42),
            "local replacement at the same destination retires ownership without cancelling local work");
        var third = Request(context); authority.Handle("actor", third); executor.Complete(42); authority.Reconcile();
        check(authority.Query("actor", third).State == DupeCommandState.Completed && executor.Cancellations == 1, "native completion retires the lease without cancellation");

        context = new Context(); executor = new Executor(); authority = Authority(context, executor);
        first = Request(context); authority.Handle("first-actor", first); authority.Handle("second-actor", first.Copy());
        check(executor.LeaseIds.Count == 2 && executor.LeaseIds[0] != executor.LeaseIds[1], "actors choosing identical public IDs receive distinct private native identities");
        check(authority.Handle("first-actor", Cancel(first)).Reason == "dupe_already_finished" && executor.Cancellations == 0 && executor.HasTask(42),
            "request-ID collision cannot let the earlier actor cancel the later actor's task");
        check(authority.Handle("second-actor", Cancel(first)).Accepted && executor.Cancellations == 1, "the later actor still controls its own exact task");
        int executions = executor.Executions;
        check(authority.Query("actor", Request(context)).OutcomeUnknown && executor.Executions == executions, "query for an unseen request is read-only and never issues a delayed move");
    }

    private static void RevocationAndWorldChanges(Action<bool, string> check)
    {
        var context = new Context(); var executor = new Executor(); bool grant = true;
        var authority = Authority(context, executor, (actor, dupe) => grant || actor == "owner");
        var guest = Request(context); var owner = Request(context, 43);
        authority.Handle("guest", guest); authority.Handle("owner", owner);
        grant = false; authority.Reconcile();
        check(authority.Query("guest", guest).State == DupeCommandState.Revoked && executor.Cancellations == 1 && executor.HasTask(43), "revocation cancels only the revoked actor's active move");
        grant = true;
        check(authority.Handle("guest", guest).State == DupeCommandState.Revoked && executor.Executions == 2, "restoring permission does not restart a withdrawn request");
        guest = Request(context); authority.Handle("guest", guest);
        authority.Reconcile(true, actor => actor != "guest");
        check(authority.Query("guest", guest).State == DupeCommandState.Disconnected && executor.Cancellations == 2 && executor.HasTask(43), "disconnect withdraws accepted guest command and preserves the connected owner's move");
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N"); authority.Reconcile();
        check(authority.Query("owner", owner).State == DupeCommandState.WorldChanged && executor.Cancellations == 3, "world epoch change cleans captured old task before forgetting its ledger");
        var fresh = Request(context); authority.Handle("owner", fresh); authority.StopAll(); authority.StopAll();
        check(authority.Query("owner", fresh).State == DupeCommandState.Disconnected && executor.Cancellations == 4, "leaving cleanup is idempotent for each owned task");

        context = new Context(); executor = new Executor(); authority = Authority(context, executor);
        var request = Request(context);
        executor.OnPreflight = () => context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(authority.Handle("actor", request).Reason == "dupe_stale_world" && executor.Executions == 0, "mutable context cannot hide an epoch change during native preflight");
        executor.OnPreflight = null; grant = true;
        authority = Authority(context, executor, (actor, dupe) => grant);
        executor.OnPreflight = () => grant = false;
        check(authority.Handle("actor", Request(context)).Reason == "dupe_not_authorized" && executor.Executions == 0, "revocation during preflight prevents native task creation");
    }

    private static void UncertainCleanup(Action<bool, string> check)
    {
        var context = new Context(); var executor = new Executor { ThrowAfterExecute = true }; bool grant = true;
        var authority = Authority(context, executor, (actor, dupe) => grant);
        var move = Request(context);
        check(authority.Handle("actor", move).OutcomeUnknown && executor.Executions == 1, "exception after native creation retains an unknown receipt and exact lease");
        authority.Reconcile();
        check(authority.Query("actor", move).State == DupeCommandState.Active && executor.Executions == 1, "later observation resolves creation without repeating MoveToLocation");
        executor.ThrowBeforeCancel = true;
        var cancel = Cancel(move);
        check(authority.Handle("actor", cancel).OutcomeUnknown && executor.Cancellations == 0, "failed cleanup is never acknowledged as cancelled");
        authority.Reconcile();
        check(authority.Query("actor", cancel).Accepted && authority.Query("actor", move).State == DupeCommandState.Cancelled &&
            executor.Cancellations == 1 && executor.CancelAttempts == 2, "cleanup retry retains the original handle and resolves both receipts");

        executor.ThrowAfterExecute = false; move = Request(context); authority.Handle("actor", move);
        executor.ThrowAfterCancel = true; cancel = Cancel(move);
        check(authority.Handle("actor", cancel).OutcomeUnknown, "exception after cancellation initially preserves uncertainty");
        int attempts = executor.CancelAttempts; authority.Reconcile();
        check(authority.Query("actor", cancel).Accepted && executor.CancelAttempts == attempts && executor.Cancellations == 2,
            "observing a detached chore resolves partial cancellation without cancelling twice");

        move = Request(context); authority.Handle("actor", move); executor.UnknownState = true; grant = false; authority.Reconcile();
        check(authority.Query("actor", move).State == DupeCommandState.Active && executor.HasTask(42), "unobservable cleanup does not fabricate a terminal native state");
        grant = true; executor.UnknownState = false; authority.Reconcile();
        check(authority.Query("actor", move).State == DupeCommandState.Revoked && !executor.HasTask(42), "a restored grant cannot abandon cleanup already requested by revocation");

        context = new Context(); executor = new Executor { ThrowAfterExecute = true, UnknownState = true }; authority = Authority(context, executor);
        move = Request(context);
        check(authority.Handle("actor", move).OutcomeUnknown, "native entry without an observable chore stays uncertain");
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        var fresh = Request(context);
        check(authority.Handle("actor", fresh).Reason == "dupe_cleanup_pending" && executor.Cancellations == 0,
            "epoch metadata alone cannot declare an unknown task finished or guess its cancellation");
        executor.ReplaceWorld(); authority.Reconcile();
        var ended = authority.Query("actor", move);
        check(ended.State == DupeCommandState.WorldChanged && !ended.Accepted && executor.Cancellations == 0,
            "actual native scope replacement retires uncertainty without inventing original acceptance or completion");
        executor.ThrowAfterExecute = executor.UnknownState = false;
        check(authority.Handle("actor", fresh).State == DupeCommandState.Active && executor.Executions == 2,
            "a retired unknown native scope cannot permanently block the new save");
    }

    private static SessionProfile Profile(string name)
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    private static void LocalCoordinator(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(9233))
        using (var session = new RoomSession(network.Host, Profile("Owner"), "", "Movement lifecycle"))
        {
            var context = new Context(); var executor = new Executor();
            context.Planet.OwnerPeerId = context.Planet.AuthorityPeerId = session.LocalPlayerId;
            var commands = new DupeCommandCoordinator(session, context, executor, planet => session.LocalPlayerId, (actor, dupe) => true);
            try
            {
                string move = commands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                check(commands.GetSubmission(move).Pending && !commands.GetMoves(context.Planet.PlanetId).Single().CanCancel, "unaccepted local echo cannot cancel a task that has not been observed");
                commands.Pump(1000);
                check(commands.GetMoves(session.LocalPlayerId, context.Planet.PlanetId).Single().CanCancel &&
                    commands.GetMoves("another-owner", context.Planet.PlanetId).Length == 0, "move rows are scoped to authority as well as planet");
                bool pendingVisible = false;
                Action<DupeCommandReceipt> observePending = receipt =>
                {
                    if (receipt.Pending && receipt.RequestId != move)
                    {
                        var row = commands.GetMoves(context.Planet.PlanetId).Single();
                        pendingVisible = row.Cancellation != null && row.Cancellation.Pending && !row.CanCancel;
                    }
                };
                commands.SubmissionChanged += observePending;
                string cancellation = commands.CancelMove(move);
                check(cancellation != null && pendingVisible && commands.CancelMove(move) == null, "synchronous UI listeners already see cancellation pending and cannot double-submit");
                commands.Pump(1000.1);
                check(commands.GetSubmission(cancellation).Accepted && commands.GetSubmission(move).State == DupeCommandState.Cancelled &&
                    !commands.GetMoves(context.Planet.PlanetId).Single().CanCancel, "local synchronous cancellation keeps final receipt and disables cancellation");
                commands.GetMoves(context.Planet.PlanetId).Single().Request.Target.X++;
                check(commands.GetMoves(context.Planet.PlanetId).Single().Request.Target.X == 10, "UI result copies cannot change the recorded native intent");
                commands.SubmissionChanged -= observePending;
                move = commands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11)); commands.Pump(1000.2);
                commands.Dispose();
                check(executor.Cancellations == 2 && commands.GetSubmission(move).State == DupeCommandState.Disconnected &&
                    commands.GetMoves(context.Planet.PlanetId).All(row => !row.CanCancel), "coordinator disposal cleans native move before dropping its session handler");
            }
            finally { commands.Dispose(); }
        }
    }

    // One small in-memory session pair checks the real routing/receipt boundary.
    // No TCP, Steam clients, full network suite or native simulation are involved.
    private static void LifecycleReceipts(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(9234))
        using (var owner = new RoomSession(network.Host, Profile("Owner"), "", "Movement receipts"))
        {
            var endpoint = (DeterministicRoomNetwork.Endpoint)network.Connect();
            using (var actor = new RoomSession(endpoint, Profile("Actor"), ""))
            {
                for (int i = 0; i < 100 && (!actor.Joined || owner.Players.Length != 2); i++)
                { network.Advance(.05); owner.Pump(network.Now); actor.Pump(network.Now); }
                check(actor.Joined && owner.Players.Length == 2, "lifecycle receipt fixture authenticates two in-memory production sessions");
                var context = new Context(); var actorContext = new Context(); var executor = new Executor();
                context.Planet.OwnerPeerId = context.Planet.AuthorityPeerId = owner.LocalPlayerId;
                actorContext.Planet.OwnerPeerId = actorContext.Planet.AuthorityPeerId = actor.LocalPlayerId;
                Func<string, string> authority = planet => planet == context.Planet.PlanetId ? owner.LocalPlayerId : actor.LocalPlayerId;
                using (var ownerCommands = new DupeCommandCoordinator(owner, context, executor, authority, (peer, dupe) => true))
                using (var actorCommands = new DupeCommandCoordinator(actor, actorContext, new Executor(), authority, (peer, dupe) => true))
                {
                    Action step = () =>
                    {
                        network.Advance(.05); owner.Pump(network.Now); actor.Pump(network.Now);
                        ownerCommands.Pump(network.Now); actorCommands.Pump(network.Now);
                    };
                    Action<Func<bool>> until = condition => { for (int i = 0; i < 160 && !condition(); i++) step(); };
                    string move = actorCommands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                    until(() => actorCommands.GetSubmission(move).State == DupeCommandState.Active);
                    var oldActive = actorCommands.GetSubmission(move);
                    check(oldActive.State == DupeCommandState.Active && executor.Executions == 1, "remote author observes authority's active native lease");
                    string cancel = actorCommands.CancelMove(move);
                    until(() => actorCommands.GetSubmission(move).State == DupeCommandState.Cancelled && actorCommands.GetSubmission(cancel).Accepted);
                    check(actorCommands.GetSubmission(cancel).Accepted && executor.Cancellations == 1, "remote exact-request cancellation reaches production authority and resolves both results");
                    owner.SendApplicationTo(actor.LocalPlayerId, DupeCommandWire.Encode(new DupeCommandMessage { TargetId = actor.LocalPlayerId, Receipt = oldActive }));
                    for (int i = 0; i < 5; i++) step();
                    check(actorCommands.GetSubmission(move).State == DupeCommandState.Cancelled &&
                        !actorCommands.GetMoves(context.Planet.PlanetId).Single().CanCancel, "delayed active receipt cannot resurrect cancelled move");

                    move = actorCommands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                    until(() => actorCommands.GetSubmission(move).State == DupeCommandState.Active);
                    bool dropTerminal = true; int dropped = 0;
                    network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                    {
                        Packet packet = Protocol.Decode(bytes);
                        if (!fromHost || !dropTerminal || packet.Payload == null || !DupeCommandWire.IsMessage(packet.Payload)) return false;
                        DupeCommandReceipt receipt = DupeCommandWire.Decode(packet.Payload).Receipt;
                        if (receipt == null || receipt.RequestId != move || receipt.State != DupeCommandState.Completed) return false;
                        dropped++; return true;
                    };
                    executor.Complete(42); step(); step(); dropTerminal = false;
                    check(dropped > 0 && actorCommands.GetSubmission(move).State == DupeCommandState.Active, "lost terminal notification leaves the last observed state until a fresh receipt");
                    until(() => actorCommands.GetSubmission(move).State == DupeCommandState.Completed);
                    check(actorCommands.GetSubmission(move).State == DupeCommandState.Completed && executor.Executions == 2,
                        "read-only lifecycle polling recovers completion without reissuing movement");
                    network.Settings.DropBeforeDelivery = null;
                    move = actorCommands.SubmitMove(context.Planet.PlanetId, context.Planet.AuthorityEpoch, 42, new WorkCell(10, 11));
                    until(() => actorCommands.GetSubmission(move).State == DupeCommandState.Active);
                    network.CutAll(); for (int i = 0; i < 5; i++) step();
                    check(executor.Cancellations == 2 && !executor.HasTask(42) &&
                        actorCommands.GetMoves(context.Planet.PlanetId).All(row => !row.CanCancel), "session disconnection runs authority cleanup and disables remote cancellation controls");
                }
            }
        }
    }

    private static void Wire(Action<bool, string> check)
    {
        var context = new Context(); var move = Request(context); move.NativeLeaseId = Guid.NewGuid().ToString("N");
        var cancel = Cancel(move);
        DupeCommandMessage decoded = DupeCommandWire.Decode(DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "owner", Request = cancel }));
        check(decoded.Request.SameAs(cancel) && decoded.Request.NativeLeaseId == null, "wire binds cancellation to exact original intent but never accepts a client-supplied native lease");
        decoded = DupeCommandWire.Decode(DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "owner", Query = move }));
        check(decoded.Query.SameAs(move) && decoded.Request == null, "read-only status query has a distinct wire message kind");
        var terminal = new DupeCommandReceipt { RequestId = move.RequestId, Accepted = true, State = DupeCommandState.Cancelled, Revision = 3, Reason = "dupe_cancelled" };
        decoded = DupeCommandWire.Decode(DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "actor", Receipt = terminal }));
        check(decoded.Receipt.State == terminal.State && decoded.Receipt.Revision == 3, "terminal state and monotonic stage survive receipt round trip");
        bool rejected = false;
        terminal.Revision = 2;
        try { DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "actor", Receipt = terminal }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "wire rejects inconsistent state-stage pairs");
        rejected = false;
        try { DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "owner", Request = move, Query = move }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "wire refuses ambiguous execution/query messages");
        byte[] bytes = DupeCommandWire.Encode(new DupeCommandMessage { TargetId = "owner", Request = cancel });
        bytes[4] = 1; rejected = false;
        try { DupeCommandWire.Decode(bytes); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "old wire cannot silently ignore cancellation or lifecycle fields");
    }
}
