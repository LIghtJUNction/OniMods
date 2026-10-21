using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static partial class RemoteWorkTests
{
    private static Action<bool, string> check;
    private sealed class Context : IWorldWorkContext
    {
        internal bool Available = true;
        internal WorkPlanet Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"), AuthorityEpoch = Guid.NewGuid().ToString("N"), NativeWorldId = 0 };
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return Available; }
    }
    private sealed class Executor : IWorkExecutor, IWorkCapabilities
    {
        internal bool BuildExecution = true;
        public bool SupportsBuildingExecution { get { return BuildExecution; } }
        internal int Applied;
        internal int Cancelled;
        internal int Calls;
        internal bool Explored = true;
        internal bool FailNext;
        internal bool FailureHasNoMutation;
        internal bool FailAfterMutation;
        internal bool ThrowAfterMutation;
        internal WorkTaskIntent[] Native = Array.Empty<WorkTaskIntent>();
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request)
        {
            if (!Explored) return WorkExecution.Reject("work_unexplored");
            if (request.Cells.Any(c => c.X > 100 || c.Y > 100)) return WorkExecution.Reject("work_outside_world");
            return WorkExecution.Accept(request.Cells.Length);
        }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] before, WorkTaskIntent[] desired)
        {
            Calls++;
            if (FailNext)
            {
                FailNext = false;
                if (!FailAfterMutation) return FailureHasNoMutation ? WorkExecution.RejectBeforeMutation("native_failure") : WorkExecution.Reject("native_failure");
                Native = desired.Select(p => p.Copy()).ToArray();
                if (ThrowAfterMutation) throw new InvalidOperationException("native_exception_after_partial_work");
                return WorkExecution.Reject("native_failure_after_partial_work");
            }
            Applied += desired.Count(d => !Native.Any(n => n.Key == d.Key && n.RequestId == d.RequestId));
            Cancelled += Native.Count(n => !desired.Any(d => d.Key == n.Key));
            Native = desired.Select(p => p.Copy()).ToArray();
            return WorkExecution.Accept(desired.Length);
        }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        BuildingAtomicMergeAndReplay();
        BuildingWireAndJournal();
        BuildingRecoveryIsolation();
        BuildingOptimisticRouting();
        BuildingWeakNetwork();
        CancellationAuthorityAndReplay();
        CancellationNativeFailureBoundaries();
        CancellationWireAndRecovery();
        CancellationRoutingAndLostReceipt();
        CancellationTimeoutAndCompletion();
        AuthorityIsolationAndMerge();
        ValidationAndReplay();
        RejectedPlansNeverBecomeExecutable();
        PersistentJournalAndHistoricalRecovery();
        SynchronousLocalDraftStatus();
        ProductionRoutingAndWeakNetwork();
        MapVisibilityDoesNotRevealPlans();
        AuthorityEpochChangeInvalidatesRemotePlanCache();
        ScopeNotificationsAreAuthenticatedAndMonotonic();
        FirstScopeAdoptionPreservesMatchingPending();
        WireBounds();
        Console.WriteLine("Remote work: production authority, whole-building footprint merge, authenticated routing, private drafts, priority/revocation, native journal contracts and reconnect replay. Native runtime validation is reported separately.");
    }

    private static RemoteWorkRequest Request(Context context, RemoteWorkGrant grant, int priority = 5, int x = 10)
    { return new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId, AuthorityEpoch = context.Planet.AuthorityEpoch,
        GrantToken = grant == null ? "" : grant.Token, DryRun = true, Kind = RemoteWorkKind.Dig, Priority = priority, Cells = new[] { new WorkCell(x, 10) } }; }
    private static RemoteWorkReceipt Commit(RemoteWorkAuthority authority, string actor, RemoteWorkRequest request, double now = 1)
    {
        var preview = authority.Handle(actor, request, now);
        check(preview.Accepted && preview.DryRun && preview.Count == request.Cells.Length, "native preflight receipt precedes confirmation");
        request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
        return authority.Handle(actor, request, now + 1);
    }

    private static RemoteWorkRequest BuildRequest(Context context, RemoteWorkGrant grant, string prefab = "Outhouse", int x = 10, int y = 10, int priority = 5)
    {
        var request = Request(context, grant, priority, x);
        request.Kind = RemoteWorkKind.Build; request.Layer = RemoteTaskLayer.Building; request.Cells = new[] { new WorkCell(x, y) };
        request.Build = new WorkBuildSpec { PrefabId = prefab, MaterialTags = new[] { "Granite" } };
        return request;
    }
    private static void BuildingAtomicMergeAndReplay()
    {
        var context = new Context(); var native = new Executor { BuildExecution = false }; var authority = new RemoteWorkAuthority("owner", context, native);
        var gated = BuildRequest(context, null);
        check(authority.Handle("owner", gated, 0).Reason == "work_native_build_validation_required" && !authority.IsBlocked && authority.ConfirmedRevision == 0 &&
            authority.GetMergedPlans().Length == 0 && native.Calls == 0, "closed production capability gate refuses building before candidate acceptance or native reconciliation");
        check(Commit(authority, "owner", Request(context, null)).Accepted && native.Native.Single().Kind == RemoteWorkKind.Dig,
            "refusing unvalidated buildings never freezes the available Dig path");
        context = new Context(); native = new Executor(); authority = new RemoteWorkAuthority("owner", context, native);
        var grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        var owner = BuildRequest(context, null);
        check(Commit(authority, "owner", owner).Accepted && WorkPlanGeometry.Footprint(authority.GetMergedPlans().Single()).Length == 6,
            "two-by-three building is one accepted plan with six authority-derived footprint cells");
        var visitor = BuildRequest(context, grant, "StorageLocker", 11, 11, 6);
        check(Commit(authority, "visitor", visitor, 5).Accepted && authority.GetMergedPlans().Length == 1 &&
            authority.GetMergedPlans().Single().ActorId == "visitor" && WorkPlanGeometry.Footprint(authority.GetMergedPlans().Single()).Length == 2,
            "overlap at a non-anchor cell suppresses the entire lower-priority building rather than producing a half-blueprint");
        check(authority.GetOwnPlans("owner").Single().Build.PrefabId == "Outhouse" && authority.GetOwnPlans("visitor").Length == 1,
            "covered building remains only in its actor candidate view");
        var copy = authority.GetOwnPlans("visitor").Single(); copy.Build.MaterialTags[0] = "Sandstone";
        check(authority.GetOwnPlans("visitor").Single().Build.MaterialTags[0] == "Granite", "query copies cannot mutate accepted material selection");
        long revision = authority.ConfirmedRevision; int applied = native.Applied;
        check(authority.Handle("visitor", visitor, 10).Accepted && authority.ConfirmedRevision == revision && native.Applied == applied,
            "building acknowledgement retry creates neither another whole order nor a new tie-break revision");
        visitor.Build.MaterialTags[0] = "Sandstone";
        check(authority.Handle("visitor", visitor, 11).Reason == "work_request_id_conflict", "same building request ID with different materials is rejected");
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        check(authority.GetMergedPlans().Length == 1 && authority.GetMergedPlans().Single().ActorId == "owner" &&
            WorkPlanGeometry.Footprint(authority.GetMergedPlans().Single()).Length == 6, "revocation restores an entire eligible owner building");
        grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        var tie = BuildRequest(context, grant, "StorageLocker", 11, 11, 5);
        check(Commit(authority, "visitor", tie, 12).Accepted && authority.GetMergedPlans().Single().ActorId == "visitor", "equal-priority overlapping buildings use authority confirmation order");
        var dig = Request(context, null, 9, 11); dig.Cells[0] = new WorkCell(11, 12);
        check(Commit(authority, "owner", dig, 16).Accepted && authority.GetMergedPlans().Single().Kind == RemoteWorkKind.Dig,
            "terrain/build footprint conflicts suppress the whole incompatible construction plan");
        authority.ForgetNativePlan(authority.GetMergedPlans().Single());
        check(authority.GetMergedPlans().Length == 0 && authority.GetOwnPlans("owner").Length == 0 && authority.GetOwnPlans("visitor").Length == 0,
            "native terminal removal retires covered overlapping candidates instead of resurrecting old construction");
        var bad = BuildRequest(context, null); bad.Build.PrefabId = "Wire";
        check(authority.Handle("owner", bad, 20).Reason == "work_unsupported_kind", "utility network placement is not falsely claimed by simple TryPlace bridge");
        bad = BuildRequest(context, null); bad.Cells = new[] { new WorkCell(1, 1), new WorkCell(4, 1) };
        check(authority.Handle("owner", bad, 20).Reason == "work_invalid_cells", "one building request cannot masquerade as independent cell tasks");
        bad = BuildRequest(context, null); bad.Build.Orientation = 1;
        check(authority.Handle("owner", bad, 20).Reason == "work_unsupported_kind", "unreviewed rotation is explicitly refused");
        bad = BuildRequest(context, null, x: 8192);
        check(authority.Handle("owner", bad, 20).Reason == "work_invalid_cells", "derived multi-cell extent must remain inside bounded coordinate domain");
    }
    private static void BuildingWireAndJournal()
    {
        var context = new Context(); var request = BuildRequest(context, null);
        var encoded = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = request });
        var restoredRequest = RemoteWorkWire.Decode(encoded).Request;
        check(encoded[4] == 5 && WorkPlanGeometry.SameBuild(restoredRequest.Build, request.Build), "building wire v5 retains exact ordered material and prefab fields");
        var legacy = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = Request(context, null) });
        check(legacy[4] == 4 && RemoteWorkWire.Decode(legacy).Request.Kind == RemoteWorkKind.Dig, "Dig-only requests retain byte-compatible wire v4");
        var plan = new WorkTaskIntent { PlanetId = context.Planet.PlanetId, ActorId = "owner", RequestId = request.RequestId, Kind = request.Kind, Layer = request.Layer,
            Cell = request.Cells[0], Build = request.Build.Copy(), Priority = 5, ConfirmedRevision = 1 };
        var response = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, TargetId = "visitor", PlanetId = plan.PlanetId, PlanActorId = "owner", AuthorityEpoch = context.Planet.AuthorityEpoch,
            Generation = 1, GrantRevision = 1, Revision = 1, Total = 1, Plans = new[] { plan } };
        check(RemoteWorkWire.Decode(RemoteWorkWire.Encode(response)).Plans.Single().Build.PrefabId == "Outhouse", "authorized candidate page retains whole building identity");
        string persistentOwner = Guid.NewGuid().ToString("N");
        var saved = new RemoteWorkSavedPlanet { PlanetId = plan.PlanetId, OwnerPeerId = "owner", NativeWorldId = 0, Plans = new[] { plan },
            Orders = new[] { new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, NativeObjectId = 123, Winner = plan } } };
        var document = new RemoteWorkSavedDocument { PersistentOwnerId = persistentOwner }; document.Planets.Add(saved);
        string payload = RemoteWorkPersistence.Encode(document); var decoded = RemoteWorkPersistence.Decode(payload);
        check(Convert.FromBase64String(payload)[4] == 2 && decoded.Planets.Single().Orders.Single().NativeObjectId == 123 &&
            WorkPlanGeometry.SameBuild(decoded.Planets.Single().Plans.Single().Build, request.Build), "building journal v2 retains full spec plus exact native object identity");
        var old = plan.Copy(); old.Kind = RemoteWorkKind.Dig; old.Layer = RemoteTaskLayer.Terrain; old.Build = null;
        var oldSaved = new RemoteWorkSavedPlanet { PlanetId = plan.PlanetId, OwnerPeerId = "owner", NativeWorldId = 0, Plans = new[] { old },
            Orders = new[] { new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, Winner = old } } };
        var oldDoc = new RemoteWorkSavedDocument { PersistentOwnerId = persistentOwner }; oldDoc.Planets.Add(oldSaved);
        string oldPayload = RemoteWorkPersistence.Encode(oldDoc);
        check(Convert.FromBase64String(oldPayload)[4] == 1 && RemoteWorkPersistence.Decode(oldPayload).Planets.Single().Plans.Single().Kind == RemoteWorkKind.Dig,
            "existing Dig journal format v1 is still emitted and readable unchanged");
        saved.Orders[0].NativeObjectId = 0; bool rejected = false;
        try { RemoteWorkPersistence.Encode(document); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "building checkpoint cannot adopt a blueprint without exact native identity"); saved.Orders[0].NativeObjectId = 123;
        var covered = plan.Copy(); covered.ActorId = "visitor"; covered.Cell = new WorkCell(11, 11); covered.Build.PrefabId = "StorageLocker";
        covered.RequestId = Guid.NewGuid().ToString("N"); saved.Plans = new[] { plan, covered };
        RemoteWorkPersistence.RemovePlan(saved, covered);
        check(saved.Plans.Length == 0 && saved.Orders.Length == 0, "terminal whole-footprint checkpoint pruning removes covered conflicting history");
        request.Build.MaterialTags = new[] { "Granite", "Sandstone" }; rejected = false;
        try { RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = request }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "unbounded or unreviewed material slots are rejected before transmission");
    }
    private static void BuildingRecoveryIsolation()
    {
        var context = new Context(); var request = BuildRequest(context, null);
        var build = new WorkTaskIntent { PlanetId = request.PlanetId, ActorId = "old-owner", RequestId = request.RequestId,
            Cell = request.Cells[0], Build = request.Build.Copy(), Kind = RemoteWorkKind.Build, Layer = RemoteTaskLayer.Building, Priority = 5, ConfirmedRevision = 1 };
        var dig = new WorkTaskIntent { PlanetId = request.PlanetId, ActorId = "old-owner", RequestId = Guid.NewGuid().ToString("N"),
            Cell = new WorkCell(40, 10), Kind = RemoteWorkKind.Dig, Layer = RemoteTaskLayer.Terrain, Priority = 5, ConfirmedRevision = 2 };
        var buildOrder = new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, NativeObjectId = 33, Winner = build };
        var digOrder = new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, Winner = dig };
        var saved = new RemoteWorkSavedPlanet { PlanetId = request.PlanetId, NativeWorldId = 0, OwnerPeerId = "old-owner", Plans = new[] { build, dig }, Orders = new[] { buildOrder, digOrder } };
        var closed = RemoteWorkPersistence.FilterBuildRecovery(saved, false, saved.Orders);
        check(closed.Plans.Length == 1 && closed.Orders.Length == 1 && closed.Plans.Single().Kind == RemoteWorkKind.Dig,
            "closed gate leaves saved construction outside executable authority even when native provenance used to match");
        check(saved.Plans.Length == 2 && saved.Orders.Length == 2, "recovery filtering never modifies its supplied journal snapshot");
        var restored = RemoteWorkPersistence.RestorePlans(closed, "owner");
        check(restored.Single().Kind == RemoteWorkKind.Dig && restored.Single().HistoricalActive, "isolating construction preserves the verified Dig recovery path");
        var executor = new PersistentExecutor { Saved = closed }; var authority = new RemoteWorkAuthority("owner", context, executor);
        check(authority.Synchronize() && !authority.IsBlocked && authority.GetMergedPlans().Single().Kind == RemoteWorkKind.Dig,
            "closed-gate recovery does not manufacture an uncertain whole-planet block");
        authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        check(executor.Calls == 0 && !authority.IsBlocked, "grant changes after isolated restore cannot cancel or reprioritize the preserved physical blueprint");
        var missing = RemoteWorkPersistence.FilterBuildRecovery(saved, true, Array.Empty<RemoteWorkSavedOrder>());
        check(RemoteWorkPersistence.RestorePlans(missing, "owner").Single(p => p.Kind == RemoteWorkKind.Build).HistoricalActive == false &&
            missing.Orders.Single().Winner.Kind == RemoteWorkKind.Dig,
            "missing or rejected native marker leaves building as inactive historical data, never a replay candidate");
        var accepted = RemoteWorkPersistence.FilterBuildRecovery(saved, true, new[] { buildOrder });
        check(RemoteWorkPersistence.RestorePlans(accepted, "owner").Single(p => p.Kind == RemoteWorkKind.Build).HistoricalActive,
            "enabled recovery retains only a separately verified exact marker identity");
        var differentId = new RemoteWorkSavedOrder { Token = buildOrder.Token, OriginEpoch = buildOrder.OriginEpoch, NativeObjectId = 34, Winner = build.Copy() };
        check(RemoteWorkPersistence.FilterBuildRecovery(saved, true, new[] { differentId }).Orders.All(o => o.Winner.Kind != RemoteWorkKind.Build),
            "matching cell and token cannot replace the saved native blueprint ID");
        differentId.NativeObjectId = 33; differentId.Winner.Build.MaterialTags[0] = "Sandstone";
        check(RemoteWorkPersistence.FilterBuildRecovery(saved, true, new[] { differentId }).Orders.All(o => o.Winner.Kind != RemoteWorkKind.Build),
            "matching native ID cannot override exact prefab/material intent during restore");
    }

    private static void BuildingOptimisticRouting()
    {
        using (var network = new DeterministicRoomNetwork(3021))
        {
            Peer peer = null;
            using (peer = new Peer(network.Host, null, planet => peer != null && peer.Context.Planet.PlanetId == planet ? peer.Session.LocalPlayerId : null))
            {
                peer.Pump(0);
                var spec = new WorkBuildSpec { PrefabId = "StorageLocker", MaterialTags = new[] { "Granite" } };
                var draft = peer.Work.DraftBuild(peer.Context.Planet.PlanetId, new WorkCell(10, 10), spec, 6);
                spec.MaterialTags[0] = "Sandstone";
                check(peer.Work.OwnDrafts(peer.Context.Planet.PlanetId).Single().Build.MaterialTags[0] == "Granite" && peer.Executor.Applied == 0,
                    "building appears immediately as a private detached draft without native work");
                check(peer.Work.SubmitDraft(peer.Session.LocalPlayerId, draft, true) && peer.Work.GetSubmission(draft.RequestId).Accepted && peer.Executor.Applied == 1,
                    "building uses optimistic preview/confirm chain and one canonical whole-order acknowledgement");
                check(peer.Work.GetPlans(peer.Session.LocalPlayerId, peer.Context.Planet.PlanetId, true).Single().Build.PrefabId == "StorageLocker",
                    "confirmed whole building reaches merged view through production coordinator");
            }
        }
    }

    private static void BuildingWeakNetwork()
    {
        using (var network = new DeterministicRoomNetwork(7321))
        {
            network.Settings.ReliableOrdered = true; network.Settings.Delay = .8; network.Settings.Jitter = .25; network.Settings.Drop = .06;
            network.Settings.Duplicate = .08; network.Settings.Reorder = .08;
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet).Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 100;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Building work timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), "three protocol sessions join concurrently for building fault scenario");
                Peer host = peers[0], owner = peers[1], actor = peers[2];
                var draft = actor.Work.DraftBuild(owner.Context.Planet.PlanetId, new WorkCell(10, 10),
                    new WorkBuildSpec { PrefabId = "Outhouse", MaterialTags = new[] { "Granite" } }, 7);
                check(actor.Work.OwnDrafts(owner.Context.Planet.PlanetId).Length == 1 && owner.Executor.Applied == 0 && !actor.Work.SubmitDraft(owner.Session.LocalPlayerId, draft, true),
                    "optimistic building draft remains private and cannot execute before a grant");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions == RemoteWorkPermission.IssueTasks,
                    "building author receives explicit independent task grant under high latency and packet loss");
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    var message = RemoteWorkWire.Decode(packet.Payload);
                    return message.Kind == RemoteWorkMessageKind.Receipt && !message.Receipt.DryRun && message.TargetId == actor.Session.LocalPlayerId;
                };
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, draft, true), "building optimistic draft starts existing asynchronous preflight-confirm chain");
                until(() => owner.Executor.Native.Length == 1, "owner simulation alone receives one whole building while ACK is withheld");
                string requestId = actor.Work.LastSubmittedRequestId; int applied = owner.Executor.Applied;
                check(!actor.Receipts.ContainsKey(requestId) && actor.Executor.Applied == 0 && host.Executor.Applied == 0,
                    "unconfirmed building is not reported executed locally or to an unprivileged room host");
                network.CutAll();
                until(() => peers.Skip(1).All(p => p.Session.Reconnecting), "building fault run detects disconnected authenticated links");
                until(() => peers.All(p => p.Session.Joined && !p.Session.Reconnecting), "building client reconnects using original coordinator and identity");
                network.Settings.DropBeforeDelivery = null;
                until(() => actor.Receipts.ContainsKey(requestId), "same building request ID receives canonical result after reconnect");
                check(actor.Work.GetSubmission(draft.RequestId).Accepted && owner.Executor.Applied == applied &&
                    WorkPlanGeometry.Footprint(owner.Executor.Native.Single()).Length == 6,
                    "lost building acknowledgement and network replay never split or duplicate a six-cell order");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true);
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 1,
                    "authorized merged snapshot carries whole building spec after reconnect");
                check(actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Single().Build.MaterialTags.Single() == "Granite" &&
                    host.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 0,
                    "building material and footprint identity survive wire routing without host visibility privilege");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.None);
                check(owner.Executor.Native.Length == 0, "revocation cancels one managed pending building through executor reconciliation");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void AuthorityIsolationAndMerge()
    {
        var context = new Context(); var native = new Executor();
        var authority = new RemoteWorkAuthority("owner", context, native);
        var noGrant = Request(context, null);
        check(authority.Handle("host", noGrant, 0).Reason == "work_not_authorized", "room host has no default planet task permission");
        check(!authority.HasPermission("visitor", RemoteWorkPermission.ViewMap), "map visibility defaults isolated");
        var grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        check(!authority.HasPermission("visitor", RemoteWorkPermission.ViewMap) && !authority.HasPermission("visitor", RemoteWorkPermission.CommandDupes), "task permission grants neither map nor dupe command");
        var owned = Request(context, null, 5);
        check(Commit(authority, "owner", owned).Accepted, "owner plan executes without room-host privilege");
        var visitor = Request(context, grant, 4);
        check(Commit(authority, "visitor", visitor).Accepted && authority.GetOwnPlans("visitor").Length == 1, "visitor keeps its own independently queryable plan");
        check(authority.GetMergedPlans().Single().ActorId == "owner", "lower priority visitor plan does not override owner");
        visitor = Request(context, grant, 6);
        check(Commit(authority, "visitor", visitor).Accepted && authority.GetMergedPlans().Single().ActorId == "visitor", "higher priority plan wins real merge");
        long permissionRevision = authority.GetGrant("visitor").Revision;
        check(authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks).Revision == permissionRevision && authority.GetOwnPlans("visitor").Length == 1,
            "repeating identical permission settings preserves tasks and grant generation");
        authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewMap);
        check(authority.GetOwnPlans("visitor").Length == 1, "changing map permission preserves existing task layer while task grant remains active");
        var latestOwner = Request(context, null, 6);
        check(Commit(authority, "owner", latestOwner).Accepted && authority.GetMergedPlans().Single().ActorId == "owner", "equal priority uses authority confirmation order");
        long revision = authority.ConfirmedRevision; int calls = native.Calls;
        check(authority.Handle("owner", latestOwner, 3).Accepted && authority.ConfirmedRevision == revision && native.Calls == calls, "same request replay never wins a second ordering revision or executes twice");
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        check(authority.GetOwnPlans("visitor").Length == 0 && authority.GetMergedPlans().Single().ActorId == "owner", "revoking visitor removes its unexecuted candidates while retaining owner plan");
        grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        var otherCell = Request(context, grant, 5, 20);
        check(Commit(authority, "visitor", otherCell).Accepted && native.Native.Length == 2, "authorized disjoint dig participates in native desired orders");
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        check(native.Cancelled == 1 && native.Native.Length == 1, "revocation reconciles removal of only visitor pending native dig");
    }

    private static void ValidationAndReplay()
    {
        var context = new Context(); var native = new Executor(); var authority = new RemoteWorkAuthority("owner", context, native);
        var grant = authority.SetPermissions("actor", RemoteWorkPermission.IssueTasks);
        var request = Request(context, grant);
        var preview = authority.Handle("actor", request, 0);
        authority.SetPermissions("actor", RemoteWorkPermission.None);
        request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
        check(authority.Handle("actor", request, 1).Reason == "work_not_authorized" && native.Applied == 0, "queued confirmation fails after permission revocation");
        var replacement = authority.SetPermissions("actor", RemoteWorkPermission.IssueTasks);
        check(authority.Handle("actor", request, 2).Reason == "work_not_authorized", "regrant does not reactivate an old grant token");
        request = Request(context, replacement); request.DryRun = false;
        check(authority.Handle("actor", request, 2).Reason == "work_preview_required", "native execution cannot skip preview");
        request = Request(context, replacement); preview = authority.Handle("actor", request, 2);
        request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
        check(authority.Handle("actor", request, 33).Reason == "work_preview_required", "expired preview cannot execute");
        request = Request(context, replacement);
        check(Commit(authority, "actor", request, 40).Accepted, "fresh authorized preview can commit");
        request.Priority = 9;
        check(authority.Handle("actor", request, 43).Reason == "work_request_id_conflict", "same request id with altered body is rejected");
        native.Explored = false;
        check(authority.Handle("actor", Request(context, replacement), 44).Reason == "work_unexplored", "authority executor rejects unexplored cells even with permission");
        native.Explored = true;
        check(authority.Handle("actor", Request(context, replacement, 5, 101), 44).Reason == "work_outside_world", "world boundary checked on actual owner");
        var build = Request(context, replacement); build.Kind = RemoteWorkKind.Build; build.Layer = RemoteTaskLayer.Building;
        check(authority.Handle("actor", build, 44).Reason == "work_unsupported_kind", "unsupported construction is explicitly rejected");
        var stale = Request(context, replacement);
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(authority.Handle("actor", stale, 45).Reason == "work_stale_world" && authority.GetMergedPlans().Length == 0, "save load resets authority generation and rejects prior queued requests");
        context.Planet.OwnerPeerId = "another-owner";
        check(!authority.Synchronize(), "compute delegation without implemented owner authority protocol fails closed");
    }

    private sealed class Peer : IDisposable
    {
        internal readonly Context Context = new Context();
        internal readonly Executor Executor = new Executor();
        internal readonly RoomSession Session;
        internal readonly RemoteWorkCoordinator Work;
        internal readonly Dictionary<string, RemoteWorkReceipt> Receipts = new Dictionary<string, RemoteWorkReceipt>();
        internal Peer(IRoomTransport transport, Func<IRoomTransport> reconnect, Func<string, string> lookup)
        {
            Session = new RoomSession(transport, new SessionProfile { Name = "Player", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true },
                "", "Work test", reconnect: reconnect, options: new SessionOptions { RetryJitter = () => 0.5 });
            Work = new RemoteWorkCoordinator(Session, Context, Executor, lookup);
            Work.ReceiptReceived += (owner, receipt) => Receipts[receipt.RequestId] = receipt;
        }
        internal void Pump(double now) { Session.Pump(now); Work.Pump(now); }
        public void Dispose() { Work.Dispose(); Session.Dispose(); }
    }

    private static void ProductionRoutingAndWeakNetwork()
    {
        using (var network = new DeterministicRoomNetwork(632178))
        {
            network.Settings.ReliableOrdered = true; network.Settings.Delay = 1.5; network.Settings.Jitter = .4; network.Settings.Drop = .12;
            network.Settings.Duplicate = .05; network.Settings.Reorder = .05; network.Settings.Blackhole = false;
            network.Settings.CutBeforeDelivery = null; network.Settings.DropBeforeDelivery = null;
            network.Host.AccountIdentity = "host-account";
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet).Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var p in peers) p.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 100;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Remote work timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), "three authenticated production sessions join under delayed packet loss");
                Peer host = peers[0], owner = peers[1], actor = peers[2];
                var draft = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(10, 10) }, 7);
                check(!actor.Work.Preview(owner.Session.LocalPlayerId, draft) && owner.Executor.Applied == 0, "private actor draft sends no tasks without permission");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId) != null, "guest owner grants another guest without host authority");
                check((host.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions ?? RemoteWorkPermission.None) == RemoteWorkPermission.None, "targeted grant does not grant room host visibility");
                check(actor.Work.Preview(owner.Session.LocalPlayerId, draft), "actor sends native preflight over authenticated guest-to-guest forwarding");
                until(() => actor.Receipts.ContainsKey(draft.RequestId), "preflight receipt survives delayed production routing");
                check(owner.Executor.Applied == 0, "preview does not create real dig orders");
                var preview = actor.Receipts[draft.RequestId];
                check(actor.Work.Confirm(owner.Session.LocalPlayerId, draft, preview), "actor confirms returned preview");
                string confirmedId = actor.Work.LastSubmittedRequestId;
                until(() => actor.Receipts.ContainsKey(confirmedId), "confirmation returns owner execution receipt");
                check(owner.Executor.Applied == 1 && actor.Executor.Applied == 0 && host.Executor.Applied == 0, "only destination simulation executor receives the actual merged task");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true);
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 1, "authorized actor retrieves merged execution view");
                check(host.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 0, "uninvited host cannot read guest personal plan");
                var ownerCandidate = Request(owner.Context, null, 5);
                check(Commit(owner.Work.Authority, owner.Session.LocalPlayerId, ownerCandidate, network.Now).Accepted, "owner retains covered personal candidate beside guest winner");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false, owner.Session.LocalPlayerId);
                double privacyEnd = network.Now + 12;
                while (network.Now < privacyEnd) step();
                check(actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false, owner.Session.LocalPlayerId).Length == 0,
                    "task permission alone cannot reveal another actor's covered candidates");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans);
                until(() => (actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions & RemoteWorkPermission.ViewPlans) != 0,
                    "owner explicitly grants candidate-plan visibility independently from map and tasks");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false, owner.Session.LocalPlayerId);
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false, owner.Session.LocalPlayerId).Length == 1,
                    "explicit ViewPlans permits inspecting designated actor's covered candidate layer");
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    Packet packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    RemoteWorkMessage message = RemoteWorkWire.Decode(packet.Payload);
                    return message.Kind == RemoteWorkMessageKind.Receipt && !message.Receipt.DryRun;
                };
                var optimistic = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(20, 10) }, 8);
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, optimistic, true), "ordinary task starts asynchronous preflight-confirm chain after local draft echo");
                until(() => owner.Executor.Native.Length == 2, "automatic confirmation enters authority merge while result acknowledgement is withheld");
                string unacknowledgedId = actor.Work.LastSubmittedRequestId;
                check(!actor.Receipts.ContainsKey(unacknowledgedId) && actor.Work.OwnDrafts(owner.Context.Planet.PlanetId).Length == 2,
                    "unacknowledged local draft remains available without falsely claiming server confirmation");
                int applied = owner.Executor.Applied;
                string actorId = actor.Session.LocalPlayerId;
                network.CutAll();
                until(() => peers.Skip(1).All(p => p.Session.Reconnecting), "link cut triggers client reconnect");
                until(() => peers.All(p => p.Session.Joined && !p.Session.Reconnecting), "same controller instances recover authenticated sessions");
                network.Settings.DropBeforeDelivery = null;
                until(() => actor.Receipts.ContainsKey(unacknowledgedId), "pending confirmation resumes with the same request id after reconnect");
                check(actor.Work.GetSubmission(optimistic.RequestId).Accepted && actor.Work.GetSubmission(optimistic.RequestId).RequestId == unacknowledgedId,
                    "final authority receipt maps back to original optimistic draft after confirmation id changes");
                check(actor.Session.LocalPlayerId == actorId && owner.Executor.Applied == applied, "reconnect preserves identity and never reexecutes committed physical task");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.None);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions == RemoteWorkPermission.None, "revocation reaches guest after reconnect");
                check(owner.Executor.Cancelled == 1 && owner.Executor.Native.Single().ActorId == owner.Session.LocalPlayerId && actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 0,
                    "revocation cancels guest-only dig, restores owner winner and clears remote candidate caches");
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions == RemoteWorkPermission.IssueTasks,
                    "actor receives a fresh grant for execution-failure notification test");
                owner.Executor.FailNext = true; owner.Executor.FailAfterMutation = true;
                var uncertainDraft = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(30, 10) });
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, uncertainDraft, true), "new draft enters asynchronous chain before simulated partial native failure");
                until(() => actor.Work.ExecutionError(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId) == "work_execution_uncertain",
                    "authority pushes explicit blocked execution state across authenticated wire");
                check(owner.Work.Authority.IsBlocked && actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, true).Length == 0,
                    "remote cached canonical merge is removed when native execution becomes uncertain");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void MapVisibilityDoesNotRevealPlans()
    {
        using (var network = new DeterministicRoomNetwork(40839))
        {
            network.Settings.ReliableOrdered = true;
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet)
                .Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 15;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Plan visibility timeout: " + label);
            };
            Action settle = () => { double end = network.Now + 2; while (network.Now < end) step(); };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 2), "map privacy production sessions join");
                Peer owner = peers[0], observer = peers[1];
                string ownerId = owner.Session.LocalPlayerId, observerId = observer.Session.LocalPlayerId;
                string planetId = owner.Context.Planet.PlanetId;
                check(Commit(owner.Work.Authority, ownerId, Request(owner.Context, null)).Accepted,
                    "map privacy fixture contains a real owner candidate in the merged view");
                int mergedPackets = 0;
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind == PacketKind.Application && RemoteWorkWire.IsMessage(packet.Payload))
                    {
                        var message = RemoteWorkWire.Decode(packet.Payload);
                        if (message.Kind == RemoteWorkMessageKind.Plans && message.Merged && message.TargetId == observerId)
                            mergedPackets++;
                    }
                    return false;
                };
                Action<RemoteWorkPermission> grant = permissions =>
                {
                    owner.Work.SetPermissions(observerId, permissions);
                    until(() => observer.Work.GetReceivedGrant(ownerId, planetId)?.Permissions == permissions,
                        "observer receives explicit " + permissions + " grant");
                };
                grant(RemoteWorkPermission.ViewMap);
                check(observer.Work.RequestPlans(ownerId, planetId, true), "map-only observer can send a query without implied authorization");
                settle();
                check(mergedPackets == 0 && observer.Work.GetPlans(ownerId, planetId, true).Length == 0,
                    "map-only authorization sends neither merged plan packets nor private task data");
                var delayed = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans,
                    TargetId = observerId, PlanetId = planetId, Merged = true, PlanActorId = "",
                    AuthorityEpoch = owner.Context.Planet.AuthorityEpoch, Generation = owner.Work.Authority.Generation, GrantRevision = owner.Work.Authority.GetGrant(observerId).Revision, Revision = owner.Work.Authority.StateRevision,
                    Total = 1, Offset = 0, Plans = owner.Work.Authority.GetMergedPlans() });
                check(owner.Session.SendApplicationTo(observerId, delayed), "owner can transmit a late previously authorized plan packet");
                settle();
                check(observer.Work.GetPlans(ownerId, planetId, true).Length == 0,
                    "client independently rejects merged plans while it holds only map permission");
                grant(RemoteWorkPermission.ViewPlans);
                observer.Work.RequestPlans(ownerId, planetId, true);
                until(() => observer.Work.GetPlans(ownerId, planetId, true).Length == 1,
                    "explicit plan-view permission permits the merged task view without map or task grants");
                grant(RemoteWorkPermission.ViewMap);
                check(observer.Work.GetPlans(ownerId, planetId, true).Length == 0,
                    "downgrading to map-only immediately clears an existing merged plan cache");
                owner.Session.SendApplicationTo(observerId, delayed);
                settle();
                check(observer.Work.GetPlans(ownerId, planetId, true).Length == 0,
                    "late merged packets cannot restore task visibility after downgrade");
                grant(RemoteWorkPermission.IssueTasks);
                observer.Work.RequestPlans(ownerId, planetId, true);
                until(() => observer.Work.GetPlans(ownerId, planetId, true).Length == 1,
                    "task contributors retain the final execution view without broader candidate-plan visibility");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void AuthorityEpochChangeInvalidatesRemotePlanCache()
    {
        using (var network = new DeterministicRoomNetwork(83475))
        {
            network.Settings.ReliableOrdered = true;
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet)
                .Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 15;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Authority epoch cache timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 2), "epoch cache production sessions join");
                Peer owner = peers[0], observer = peers[1];
                string ownerId = owner.Session.LocalPlayerId, observerId = observer.Session.LocalPlayerId;
                string planetId = owner.Context.Planet.PlanetId;
                string oldEpoch = owner.Context.Planet.AuthorityEpoch;
                owner.Work.SetPermissions(observerId, RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans);
                until(() => observer.Work.GetReceivedGrant(ownerId, planetId)?.AuthorityEpoch == oldEpoch,
                    "epoch cache observer receives original scope grant");
                check(Commit(owner.Work.Authority, ownerId, Request(owner.Context, null)).Accepted,
                    "epoch cache owner has one confirmed execution candidate");
                observer.Work.RequestPlans(ownerId, planetId, true);
                until(() => observer.Work.GetPlans(ownerId, planetId, true).Length == 1,
                    "epoch cache observer receives the original canonical merge");

                var oldGrant = observer.Work.GetReceivedGrant(ownerId, planetId);
                // Same authenticated room owner and planet, but a different native-load scope.
                owner.Context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
                step();
                check(owner.Work.Authority.GetMergedPlans().Length == 0 && owner.Work.Authority.GetGrant(observerId) == null,
                    "new native epoch has neither the previous plan nor restored room permissions");
                int nativeCalls = owner.Executor.Calls;
                var stale = observer.Work.Draft(planetId, RemoteWorkKind.Dig, new[] { new WorkCell(20, 10) });
                var oldInput = Request(owner.Context, oldGrant, x: 20); oldInput.AuthorityEpoch = oldEpoch; oldInput.RequestId = stale.RequestId;
                check(observer.Work.Submit(ownerId, oldInput), "already queued old-scope input can still arrive at the real authority");
                until(() => observer.Receipts.ContainsKey(stale.RequestId), "old-epoch preview receives an authenticated rejection");
                check(!observer.Receipts[stale.RequestId].Accepted && observer.Receipts[stale.RequestId].Reason == "work_stale_world" &&
                    owner.Executor.Calls == nativeCalls, "old-epoch input never reaches the native executor");
                observer.Work.RequestPlans(ownerId, planetId, true);
                double deadline = network.Now + 20;
                while (network.Now < deadline) step();
                Console.WriteLine("Epoch cache regression: owner actual=" + owner.Work.Authority.GetMergedPlans().Length +
                    ", observer canonical=" + observer.Work.GetPlans(ownerId, planetId, true).Length +
                    ", observer retains old grant=" + (observer.Work.GetReceivedGrant(ownerId, planetId)?.AuthorityEpoch == oldEpoch));
                check(observer.Work.GetPlans(ownerId, planetId, true).Length == 0,
                    "a known stale epoch cannot remain displayed as the current canonical execution view");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void ScopeNotificationsAreAuthenticatedAndMonotonic()
    {
        using (var network = new DeterministicRoomNetwork(83476))
        {
            network.Settings.ReliableOrdered = true;
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet)
                .Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action settle = () => { double end = network.Now + 1; while (network.Now < end) step(); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 15;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Work scope timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), "scope test three authenticated sessions join");
                Peer owner = peers[0], observer = peers[1], foreign = peers[2];
                string ownerId = owner.Session.LocalPlayerId, observerId = observer.Session.LocalPlayerId;
                string planet = owner.Context.Planet.PlanetId;
                Action<Peer, RemoteWorkMessage> inject = (source, message) =>
                {
                    message.TargetId = observerId;
                    byte[] bytes = RemoteWorkWire.Encode(message);
                    check(source.Session.IsHost ? source.Session.SendApplicationTo(observerId, bytes) : source.Session.SendApplication(bytes),
                        "scope regression message sent through production session");
                    settle();
                };
                owner.Work.SetPermissions(observerId, RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans);
                until(() => observer.Work.GetReceivedGrant(ownerId, planet)?.Permissions.HasFlag(RemoteWorkPermission.IssueTasks) == true,
                    "scope observer receives original task grant");
                var originalGrant = observer.Work.GetReceivedGrant(ownerId, planet);
                check(Commit(owner.Work.Authority, ownerId, Request(owner.Context, null)).Accepted, "scope owner candidate is accepted");
                observer.Work.RequestPlans(ownerId, planet, true);
                until(() => observer.Work.GetPlans(ownerId, planet, true).Length == 1, "scope observer caches actual merge");
                var originalPlans = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = planet, Merged = true,
                    AuthorityEpoch = originalGrant.AuthorityEpoch, Generation = originalGrant.Generation, GrantRevision = originalGrant.Revision,
                    Revision = owner.Work.Authority.StateRevision, Total = 1, Plans = owner.Work.Authority.GetMergedPlans() };
                var fake = RemoteWorkAuthority.CopyGrant(originalGrant); fake.Generation += 100; fake.AuthorityEpoch = Guid.NewGuid().ToString("N");
                inject(foreign, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, Grant = fake });
                check(observer.Work.GetPlans(ownerId, planet, true).Length == 1 &&
                    observer.Work.GetReceivedGrant(ownerId, planet).Generation == originalGrant.Generation,
                    "foreign owner claim cannot invalidate canonical cache or raise generation high-water");

                var visitor = Request(owner.Context, owner.Work.Authority.GetGrant(observerId), priority: 9);
                check(Commit(owner.Work.Authority, observerId, visitor).Accepted, "grant-revision fixture has an accepted visitor winner");
                observer.Work.RequestPlans(ownerId, planet, true);
                until(() => observer.Work.GetPlans(ownerId, planet, true).SingleOrDefault()?.ActorId == observerId,
                    "visitor winner reaches the authenticated canonical view before revocation");
                var beforeRevocation = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = planet, Merged = true,
                    AuthorityEpoch = originalGrant.AuthorityEpoch, Generation = originalGrant.Generation, GrantRevision = originalGrant.Revision,
                    Revision = owner.Work.Authority.StateRevision, Total = 1, Plans = owner.Work.Authority.GetMergedPlans() };
                owner.Work.SetPermissions(observerId, RemoteWorkPermission.ViewPlans);
                until(() => observer.Work.GetReceivedGrant(ownerId, planet)?.Permissions == RemoteWorkPermission.ViewPlans,
                    "task permission is revoked while execution-view permission remains");
                check(owner.Work.Authority.GetMergedPlans().Single().ActorId == ownerId,
                    "task revocation actually removes visitor winner and restores owner task in production authority");
                inject(owner, beforeRevocation);
                check(observer.Work.GetPlans(ownerId, planet, true).All(p => p.ActorId != observerId),
                    "late page from a previous grant revision cannot resurrect the revoked visitor winner");
                owner.Work.SetPermissions(observerId, RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans);
                until(() => observer.Work.GetReceivedGrant(ownerId, planet)?.Permissions.HasFlag(RemoteWorkPermission.IssueTasks) == true,
                    "task permission can be regranted for subsequent scope tests");
                originalGrant = observer.Work.GetReceivedGrant(ownerId, planet);
                var zeroRevisionPage = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = planet,
                    PlanActorId = observerId, Merged = false, AuthorityEpoch = originalGrant.AuthorityEpoch,
                    Generation = originalGrant.Generation, GrantRevision = 0, Revision = owner.Work.Authority.StateRevision,
                    Total = 1, Plans = beforeRevocation.Plans };
                inject(owner, zeroRevisionPage);
                check(observer.Work.GetPlans(ownerId, planet, false).Length == 0,
                    "a revision-zero None-period plan page cannot populate an actor's current real-grant view");
                originalPlans = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = planet, Merged = true,
                    AuthorityEpoch = originalGrant.AuthorityEpoch, Generation = originalGrant.Generation, GrantRevision = originalGrant.Revision,
                    Revision = owner.Work.Authority.StateRevision, Total = 1, Plans = owner.Work.Authority.GetMergedPlans() };

                // Withhold both a preview receipt and a confirmation receipt to exercise both pending stages.
                var previewDraft = observer.Work.Draft(planet, RemoteWorkKind.Dig, new[] { new WorkCell(21, 10) });
                check(observer.Work.Preview(ownerId, previewDraft), "manual preview enters the old scope");
                until(() => observer.Receipts.ContainsKey(previewDraft.RequestId), "manual preview has a confirmation capability");
                var preview = observer.Receipts[previewDraft.RequestId];
                var automatic = observer.Work.Draft(planet, RemoteWorkKind.Dig, new[] { new WorkCell(22, 10) });
                var withheld = new List<RemoteWorkMessage>();
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    var message = RemoteWorkWire.Decode(packet.Payload);
                    if (message.Kind != RemoteWorkMessageKind.Receipt || message.TargetId != observerId) return false;
                    withheld.Add(message); return true;
                };
                check(observer.Work.Confirm(ownerId, previewDraft, preview), "confirmation enters old scope before reload");
                string confirmationId = observer.Work.LastSubmittedRequestId;
                check(observer.Work.SubmitDraft(ownerId, automatic, true), "automatic preview enters old scope before reload");
                until(() => withheld.Any(m => m.Receipt.RequestId == confirmationId) && withheld.Any(m => m.Receipt.RequestId == automatic.RequestId),
                    "both old confirmation and auto-preview receipts are withheld at the real session boundary");
                int calls = owner.Executor.Calls;
                var oldOwnPlans = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = planet,
                    PlanActorId = observerId, Merged = false, AuthorityEpoch = originalGrant.AuthorityEpoch,
                    Generation = originalGrant.Generation, GrantRevision = originalGrant.Revision, Revision = owner.Work.Authority.StateRevision,
                    Total = owner.Work.Authority.GetOwnPlans(observerId).Length, Plans = owner.Work.Authority.GetOwnPlans(observerId) };
                owner.Context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
                // No Interest or query is active. The proactive scope notice must converge by itself.
                until(() => observer.Work.GetReceivedGrant(ownerId, planet)?.Generation > originalGrant.Generation,
                    "passive observer receives a new-generation None scope without a map or query");
                check(observer.Work.GetPlans(ownerId, planet, true).Length == 0 &&
                    observer.Work.GetSubmission(previewDraft.RequestId)?.Reason == "work_stale_world" &&
                    observer.Work.GetSubmission(automatic.RequestId)?.Reason == "work_stale_world",
                    "new scope atomically clears canonical cache and terminates preview/confirmation submissions");
                check(owner.Work.Authority.GetGrant(observerId) == null && owner.Executor.Calls == calls,
                    "synthetic None notification does not author grants or reconcile/cancel historical native work");
                network.Settings.DropBeforeDelivery = null;
                foreach (var delayed in withheld.ToArray()) inject(owner, delayed);
                inject(owner, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, Grant = originalGrant });
                inject(owner, originalPlans);
                inject(owner, oldOwnPlans);
                check(observer.Work.GetPlans(ownerId, planet, false).Length == 0, "late old private candidate pages cannot bypass the scope boundary");
                check(observer.Work.GetPlans(ownerId, planet, true).Length == 0 && owner.Executor.Calls == calls &&
                    observer.Work.GetReceivedGrant(ownerId, planet).Permissions == RemoteWorkPermission.None,
                    "late old grants, plans and accepted receipts cannot revive UI or auto-confirm native work");
                owner.Work.SetPermissions(observerId, RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans);
                until(() => observer.Work.GetReceivedGrant(ownerId, planet)?.Permissions.HasFlag(RemoteWorkPermission.IssueTasks) == true,
                    "new-generation real grant supersedes its revision-zero tombstone");
                var current = observer.Work.GetReceivedGrant(ownerId, planet);
                inject(owner, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, Grant = new RemoteWorkGrant {
                    PlanetId = planet, AuthorityEpoch = current.AuthorityEpoch, ActorId = observerId, Generation = current.Generation,
                    Token = "", Revision = 0, Permissions = RemoteWorkPermission.None } });
                check(observer.Work.GetReceivedGrant(ownerId, planet).Token == current.Token,
                    "late same-generation revision-zero tombstone cannot revoke a real grant");
                check(!observer.Work.Confirm(ownerId, previewDraft, preview) && owner.Executor.Calls == calls,
                    "old preview capability is never rebound or resubmitted into the new scope");
                double deadline = network.Now + 95; while (network.Now < deadline) step();
                check(owner.Executor.Calls == calls && observer.Work.GetSubmission(automatic.RequestId).Reason == "work_stale_world",
                    "retired pending work neither retries nor changes its terminal reason after its old deadline");

                long lastGeneration = owner.Work.Authority.Generation;
                owner.Work.Dispose();
                var historical = originalPlans.Plans[0].Copy();
                var persistent = new PersistentExecutor { Saved = new RemoteWorkSavedPlanet { PlanetId = planet,
                    NativeWorldId = owner.Context.Planet.NativeWorldId, OwnerPeerId = ownerId, Plans = new[] { historical },
                    Orders = new[] { new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = originalGrant.AuthorityEpoch, Winner = historical } } } };
                using (var replacement = new RemoteWorkCoordinator(owner.Session, owner.Context, persistent, lookup))
                {
                    replacement.Pump(network.Now);
                    check(replacement.Authority.Generation > lastGeneration,
                        "recreating coordinator under the same RoomSession never resets authority generation");
                    settle();
                    check(observer.Work.GetReceivedGrant(ownerId, planet).Generation == replacement.Authority.Generation,
                        "same-session replacement scope reaches existing observers");
                    peers.Add(new Peer(network.Connect(), network.Connect, lookup));
                    until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 4), "late peer joins the existing room");
                    Peer late = peers.Last();
                    deadline = network.Now + 6;
                    while (network.Now < deadline) { step(); replacement.Pump(network.Now); }
                    var lateScope = late.Work.GetReceivedGrant(ownerId, planet);
                    check(lateScope != null && lateScope.Generation == replacement.Authority.Generation && lateScope.Permissions == RemoteWorkPermission.None,
                        "late peer receives periodic current None scope without a new scope transition");
                    check(replacement.Authority.GetMergedPlans().Length == 1 && replacement.Authority.GetMergedPlans()[0].Historical && persistent.Calls == 0 &&
                        replacement.Authority.GetGrant(observerId) == null && replacement.Authority.GetGrant(late.Session.LocalPlayerId) == null,
                        "repeated synthetic None notifications preserve restored historical candidates and native orders without authoring peer grants");
                }
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void FirstScopeAdoptionPreservesMatchingPending()
    {
        using (var network = new DeterministicRoomNetwork(83477))
        {
            network.Settings.ReliableOrdered = true;
            var peers = new List<Peer>();
            string otherPlanet = Guid.NewGuid().ToString("N");
            Func<string, string> lookup = planet => planet == otherPlanet ? peers[0].Session.LocalPlayerId :
                peers.Where(p => p.Context.Planet.PlanetId == planet).Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 15;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("First Work scope timeout: " + label);
            };
            try
            {
                // Join only the sessions. Neither Work coordinator has received a scope yet.
                for (int i = 0; i < 200 && !peers.All(p => p.Session.Joined && p.Session.Players.Length == 2); i++)
                { network.Advance(.05); foreach (var peer in peers) peer.Session.Pump(network.Now); }
                check(peers.All(p => p.Session.Joined && p.Session.Players.Length == 2), "first-scope test uses authenticated production sessions");
                Peer owner = peers[0], actor = peers[1];
                string ownerId = owner.Session.LocalPlayerId;
                owner.Work.Pump(network.Now); // Queues/sends None revision zero before the real grant exists.
                var grant = owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                var valid = Request(owner.Context, grant);
                var wrongEpoch = Request(owner.Context, grant, x: 11); wrongEpoch.AuthorityEpoch = Guid.NewGuid().ToString("N");
                var wrongPlanet = Request(owner.Context, grant, x: 12); wrongPlanet.PlanetId = otherPlanet;
                check(actor.Work.Submit(ownerId, valid) && actor.Work.Submit(ownerId, wrongEpoch) && actor.Work.Submit(ownerId, wrongPlanet),
                    "matching and mismatched pre-first-scope inputs are queued together");
                until(() => actor.Receipts.ContainsKey(valid.RequestId) && actor.Receipts.ContainsKey(wrongEpoch.RequestId) && actor.Receipts.ContainsKey(wrongPlanet.RequestId),
                    "first scope retires only mismatched inputs while receiving the actual matching preview ACK");
                check(actor.Receipts[valid.RequestId].Accepted && actor.Receipts[valid.RequestId].DryRun &&
                    actor.Receipts[wrongEpoch.RequestId].Reason == "work_stale_world" && actor.Receipts[wrongPlanet.RequestId].Reason == "work_stale_world",
                    "first None notification preserves same-epoch pending and retires other-epoch/planet entries independently");
                check(actor.Work.GetReceivedGrant(ownerId, owner.Context.Planet.PlanetId).Token == grant.Token,
                    "the initial queued None notification does not suppress the real same-generation grant");

                // Unlike initial adoption, a known generation change must retire even same-epoch work.
                bool withheld = false;
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    var message = RemoteWorkWire.Decode(packet.Payload);
                    if (message.Kind != RemoteWorkMessageKind.Receipt) return false;
                    withheld = true; return true;
                };
                var pending = Request(owner.Context, grant, x: 13);
                check(actor.Work.Submit(ownerId, pending), "known-generation matching preview queues before coordinator replacement");
                until(() => withheld, "matching preview ACK is withheld before same-epoch generation replacement");
                owner.Work.Dispose();
                using (var replacement = new RemoteWorkCoordinator(owner.Session, owner.Context, owner.Executor, lookup))
                {
                    replacement.Pump(network.Now);
                    until(() => actor.Receipts.ContainsKey(pending.RequestId), "same-epoch coordinator replacement terminates old-generation pending work");
                    check(actor.Receipts[pending.RequestId].Reason == "work_stale_world" &&
                        actor.Work.GetReceivedGrant(ownerId, owner.Context.Planet.PlanetId).Generation == replacement.Authority.Generation,
                        "only the first adoption preserves same-epoch pending; subsequent generation changes still fail closed");
                }
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void WireBounds()
    {
        var request = Request(new Context(), null);
        var encoded = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = request });
        check(RemoteWorkWire.Decode(encoded).Request.Cells.Length == 1, "production work wire round trip");
        bool rejected = false;
        try { RemoteWorkWire.Decode(encoded.Concat(new byte[] { 0 }).ToArray()); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "work wire rejects trailing bytes");
        var grant = new RemoteWorkGrant { ActorId = "actor", PlanetId = "planet", AuthorityEpoch = "epoch",
            Token = "", Generation = 1, Revision = 0, Permissions = RemoteWorkPermission.None };
        Func<bool> invalidGrant = () =>
        {
            try { RemoteWorkWire.Decode(RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, TargetId = "actor", Grant = grant })); return false; }
            catch (InvalidDataException) { return true; }
        };
        check(!invalidGrant(), "revision-zero None scope notification round-trips without authoring a real grant");
        grant.Generation = 0; check(invalidGrant(), "grant generation zero is rejected"); grant.Generation = 1;
        grant.Revision = -1; check(invalidGrant(), "negative grant revision is rejected"); grant.Revision = 0;
        grant.Token = "capability"; check(invalidGrant(), "scope notification cannot carry an authorizing token"); grant.Token = "";
        grant.Permissions = RemoteWorkPermission.IssueTasks; check(invalidGrant(), "revision-zero scope cannot authorize tasks");
        var nonePlans = new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, TargetId = "actor", PlanetId = "planet",
            AuthorityEpoch = "epoch", Generation = 1, GrantRevision = 0, Plans = Array.Empty<WorkTaskIntent>() };
        check(RemoteWorkWire.Decode(RemoteWorkWire.Encode(nonePlans)).GrantRevision == 0,
            "None-period private empty plans explicitly round-trip grant revision zero");
        nonePlans.GrantRevision = -1; rejected = false;
        try { RemoteWorkWire.Decode(RemoteWorkWire.Encode(nonePlans)); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "negative plan grant revision is rejected");
        encoded[4] = 3; rejected = false;
        try { RemoteWorkWire.Decode(encoded); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "old work wire version is rejected rather than reinterpreting generation bytes");
        request.Cells = new WorkCell[65]; rejected = false;
        try { RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = request }); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "work requests have bounded cell counts");
    }

    private static void RejectedPlansNeverBecomeExecutable()
    {
        var context = new Context(); var native = new Executor(); var authority = new RemoteWorkAuthority("owner", context, native);
        native.FailNext = true; native.FailureHasNoMutation = true;
        var rejected = Request(context, null);
        var result = Commit(authority, "owner", rejected);
        check(!result.Accepted && result.Reason == "native_failure" && !authority.IsBlocked, "explicit mutation-free rejection can remain available");
        check(authority.GetOwnPlans("owner").Length == 0 && authority.GetMergedPlans().Length == 0 && authority.ConfirmedRevision == 0,
            "failed reconcile commits neither candidate plan nor confirmation ordering revision");
        int calls = native.Calls;
        authority.SetPermissions("other", RemoteWorkPermission.ViewPlans);
        check(native.Calls == calls && native.Native.Length == 0, "unrelated grant cannot execute a previously rejected candidate");
        var accepted = Request(context, null, 5, 20);
        check(Commit(authority, "owner", accepted, 5).Accepted && native.Native.Single().RequestId == accepted.RequestId,
            "later successful reconcile contains only freshly accepted candidates");
        native.FailNext = true; native.FailureHasNoMutation = true;
        var failedReplacement = Request(context, null, 9, 20);
        result = Commit(authority, "owner", failedReplacement, 8);
        check(!result.Accepted && authority.GetMergedPlans().Single().RequestId == accepted.RequestId && authority.GetOwnPlans("owner").Single().Priority == 5 && authority.ConfirmedRevision == 1,
            "rejected replacement preserves original accepted actor plan and tie-order revision");
        calls = native.Calls;
        check(!authority.Handle("owner", failedReplacement, 11).Accepted && native.Calls == calls,
            "replaying a known rejected replacement cannot retry or replace accepted native work");

        context = new Context(); native = new Executor { FailNext = true, FailAfterMutation = true, ThrowAfterMutation = false };
        authority = new RemoteWorkAuthority("owner", context, native);
        rejected = Request(context, null);
        result = Commit(authority, "owner", rejected);
        check(!result.Accepted && result.Reason == "work_execution_uncertain" && authority.IsBlocked && native.Native.Length == 1,
            "partial native failure is explicitly uncertain rather than claiming physical rollback");
        check(authority.GetOwnPlans("owner").Length == 0 && authority.GetMergedPlans().Length == 0 && authority.ConfirmedRevision == 0,
            "uncertain native side effect is never exposed as an accepted canonical candidate");
        calls = native.Calls;
        authority.SetPermissions("other", RemoteWorkPermission.IssueTasks);
        check(native.Calls == calls && authority.Handle("owner", Request(context, null, 5, 30), 10).Reason == "work_execution_uncertain",
            "uncertain authority freezes subsequent grants from reconciling and rejects further task mutations");
        check(authority.Handle("owner", rejected, 11).Reason == "work_execution_uncertain" && native.Calls == calls,
            "replaying uncertain confirmation returns cached uncertainty without retrying native work");

        context = new Context(); native = new Executor(); authority = new RemoteWorkAuthority("owner", context, native);
        var grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        var visit = Request(context, grant);
        check(Commit(authority, "visitor", visit).Accepted, "revocation fixture begins with accepted visitor native work");
        native.FailNext = true; native.FailureHasNoMutation = true;
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        check(authority.IsBlocked && authority.BlockReason == "work_execution_uncertain" && !authority.HasPermission("visitor", RemoteWorkPermission.IssueTasks),
            "failed native revocation removes access immediately and declares physical execution uncertain");
        check(native.Native.Length == 1 && authority.GetMergedPlans().Length == 0,
            "failed cancellation does not present an empty canonical execution view as confirmed success");
        calls = native.Calls;
        authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        check(native.Calls == calls, "regrant after failed cancellation cannot silently resume native reconciliation");

        context = new Context(); native = new Executor { FailNext = true, FailAfterMutation = true, ThrowAfterMutation = true };
        authority = new RemoteWorkAuthority("owner", context, native);
        result = Commit(authority, "owner", Request(context, null));
        check(!result.Accepted && authority.IsBlocked && authority.GetMergedPlans().Length == 0,
            "native exception after mutation freezes authority and never commits candidate state");
    }

    private static void SynchronousLocalDraftStatus()
    {
        using (var network = new DeterministicRoomNetwork(1257))
        {
            Peer peer = null;
            using (peer = new Peer(network.Host, null, planet => peer != null && peer.Context.Planet.PlanetId == planet ? peer.Session.LocalPlayerId : null))
            {
                peer.Pump(0);
                var events = new List<string>();
                var draft = peer.Work.Draft(peer.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(5, 5) });
                peer.Work.SubmissionChanged += (id, receipt) => { check(id == draft.RequestId, "synchronous phase event retains original draft identity"); events.Add(receipt.Reason); };
                check(peer.Work.SubmitDraft(peer.Session.LocalPlayerId, draft, true), "local owner submits through the same asynchronous-chain API");
                var final = peer.Work.GetSubmission(draft.RequestId);
                check(final.Accepted && !final.DryRun && final.RequestId != draft.RequestId && final.RequestId == peer.Work.LastSubmittedRequestId,
                    "local synchronous confirmation is mapped before reentrant receipt callback");
                check(events.Contains("work_queued") && events.Contains("work_confirming") && peer.Executor.Applied == 1,
                    "local optimistic state exposes phases and executes exactly one canonical task");
            }
        }
    }

    private sealed class PersistentExecutor : IWorkExecutor, IRemoteWorkPersistence
    {
        internal RemoteWorkSavedPlanet Saved;
        internal int Calls;
        public void Attach(RemoteWorkAuthority value) { }
        public RemoteWorkSavedPlanet Restore(WorkPlanet planet, string ownerPeerId) { return Saved; }
        public void Publish(WorkPlanet planet, string ownerPeerId) { }
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request) { return WorkExecution.Accept(request.Cells.Length); }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired) { Calls++; return WorkExecution.Accept(desired.Length); }
    }

    private static void PersistentJournalAndHistoricalRecovery()
    {
        string planetId = Guid.NewGuid().ToString("N"), ownerId = Guid.NewGuid().ToString("N"), oldEpoch = Guid.NewGuid().ToString("N");
        var covered = new WorkTaskIntent { PlanetId = planetId, ActorId = "old-owner", RequestId = Guid.NewGuid().ToString("N"),
            Cell = new WorkCell(10, 10), Kind = RemoteWorkKind.Dig, Layer = RemoteTaskLayer.Terrain, Priority = 5, ConfirmedRevision = 1 };
        var guest = covered.Copy(); guest.ActorId = "old-guest"; guest.RequestId = Guid.NewGuid().ToString("N"); guest.Priority = 7; guest.ConfirmedRevision = 2;
        var ownPending = covered.Copy(); ownPending.Cell = new WorkCell(20, 10); ownPending.RequestId = Guid.NewGuid().ToString("N"); ownPending.ConfirmedRevision = 3;
        var saved = new RemoteWorkSavedPlanet { PlanetId = planetId, NativeWorldId = 0, OwnerPeerId = "old-owner", Plans = new[] { covered, guest, ownPending },
            Orders = new[] { new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = oldEpoch, Winner = guest },
                new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = oldEpoch, Winner = ownPending } } };
        var document = new RemoteWorkSavedDocument { PersistentOwnerId = ownerId }; document.Planets.Add(saved);
        string payload = RemoteWorkPersistence.Encode(document);
        var restored = RemoteWorkPersistence.Decode(payload);
        check(restored.PersistentOwnerId == ownerId && restored.Planets.Single().Plans.Length == 3 && restored.Planets.Single().Orders.Length == 2,
            "bounded native journal round-trips candidate history and exact pending order tokens");
        var context = new Context { Planet = new WorkPlanet { PlanetId = planetId, PersistentOwnerId = ownerId, AuthorityEpoch = Guid.NewGuid().ToString("N"), NativeWorldId = 0 } };
        var executor = new PersistentExecutor { Saved = restored.Planets.Single() };
        var authority = new RemoteWorkAuthority("new-owner", context, executor);
        check(authority.Synchronize() && authority.GetMergedPlans().Length == 2 && executor.Calls == 0,
            "restoring accepted pending winners never reissues native tasks");
        check(authority.GetMergedPlans().All(p => p.Historical && p.HistoricalActive) && authority.GetPlansByActor("new-owner").Length == 2,
            "old owner identity maps explicitly to current local owner while preserving historical labels");
        check(!authority.HasPermission("old-guest", RemoteWorkPermission.IssueTasks) && authority.GetGrant("old-guest") == null && authority.GetGrant("new-guest") == null,
            "restoring historical guest work never restores room grants to any temporary peer");
        var stale = Request(context, null); stale.AuthorityEpoch = oldEpoch;
        check(authority.Handle("new-owner", stale, 0).Reason == "work_stale_world", "native reload rotates network authority generation independently from saved pending provenance");
        authority.SetPermissions("new-guest", RemoteWorkPermission.IssueTasks);
        check(executor.Calls == 0 && authority.GetMergedPlans().All(p => p.RequestId != covered.RequestId),
            "new-room permission changes cannot activate covered historical candidates");
        authority.ForgetNativeCell(guest.Key);
        authority.SetPermissions("another", RemoteWorkPermission.ViewPlans);
        check(authority.GetMergedPlans().Length == 1 && authority.GetPlansByActor("old-guest").Length == 0 && executor.Calls == 0,
            "native completion or user cancellation tombstones all candidates for that cell without reentrant reconciliation");
        RemoteWorkPersistence.RemoveCell(saved, guest.Key);
        check(saved.Plans.Length == 1 && saved.Orders.Length == 1, "offline native removal prunes both durable candidates and pending order references");
        bool rejected = false;
        var duplicate = new RemoteWorkSavedOrder { Token = saved.Orders[0].Token, OriginEpoch = oldEpoch, Winner = guest };
        saved.Plans = new[] { guest, ownPending }; saved.Orders = new[] { saved.Orders[0], duplicate };
        try { RemoteWorkPersistence.Encode(document); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "journal rejects duplicate native tokens rather than claiming unrelated objects");
        rejected = false;
        try { RemoteWorkPersistence.Decode(Convert.ToBase64String(Convert.FromBase64String(payload).Concat(new byte[] { 0 }).ToArray())); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "journal rejects trailing bytes before native object adoption");
    }
}
