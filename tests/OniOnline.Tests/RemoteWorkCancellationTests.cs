using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static partial class RemoteWorkTests
{
    private static RemoteWorkRequest Cancellation(Context context, RemoteWorkGrant grant, WorkTaskIntent plan)
    {
        return new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId,
            AuthorityEpoch = context.Planet.AuthorityEpoch, GrantToken = grant == null ? "" : grant.Token,
            Kind = plan.Kind, Layer = plan.Layer, Priority = plan.Priority, Cells = new[] { plan.Cell },
            Build = plan.Build == null ? null : plan.Build.Copy(), CancelTargetRequestId = plan.RequestId,
            CancelTargetRevision = plan.ConfirmedRevision };
    }

    private static void CancellationAuthorityAndReplay()
    {
        var context = new Context(); var native = new Executor(); var authority = new RemoteWorkAuthority("owner", context, native);
        var grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        var original = Request(context, grant);
        check(Commit(authority, "visitor", original).Accepted, "cancellation fixture confirms the visitor's own dig");
        var plan = authority.GetOwnPlans("visitor").Single();
        var cancel = Cancellation(context, grant, plan);
        long stateRevision = authority.StateRevision, confirmedRevision = authority.ConfirmedRevision;
        var result = authority.Handle("visitor", cancel, 4);
        check(result.Accepted && !result.DryRun && result.Reason == "work_cancelled" && result.Count == 1,
            "one exact confirmed plan has an explicit successful cancellation receipt");
        check(authority.GetOwnPlans("visitor").Length == 0 && authority.GetMergedPlans().Length == 0 && native.Native.Length == 0 && native.Cancelled == 1,
            "cancellation reconciles the pending designation and then removes the author's candidate");
        check(authority.StateRevision == stateRevision + 1 && authority.ConfirmedRevision == confirmedRevision,
            "cancellation publishes a new state without promoting any candidate's confirmation order");
        int calls = native.Calls;
        check(authority.Handle("visitor", cancel, 5).Accepted && native.Calls == calls && native.Cancelled == 1,
            "a lost cancellation acknowledgement replays its result without another native call");
        check(authority.Handle("visitor", original, 6).Accepted && authority.GetOwnPlans("visitor").Length == 0 && native.Calls == calls,
            "replaying the original confirmed request cannot resurrect a cancelled task");

        var replacement = Request(context, grant, 8);
        check(Commit(authority, "visitor", replacement, 7).Accepted, "author can submit a fresh plan at the cancelled cell");
        calls = native.Calls;
        check(authority.Handle("visitor", cancel, 10).Accepted && native.Calls == calls && native.Native.Single().RequestId == replacement.RequestId,
            "replayed old cancellation cannot kill a newly confirmed task at the same cell");
        cancel.RequestId = Guid.NewGuid().ToString("N");
        check(authority.Handle("visitor", cancel, 11).Reason == "work_cancel_stale" && native.Calls == calls,
            "a new cancellation ID still cannot target a superseded confirmed plan");
        var current = authority.GetOwnPlans("visitor").Single();
        cancel.CancelTargetRequestId = current.RequestId; cancel.CancelTargetRevision = current.ConfirmedRevision; cancel.Priority = current.Priority;
        check(authority.Handle("visitor", cancel, 12).Reason == "work_request_id_conflict" && native.Calls == calls,
            "reusing a rejected cancellation ID for a different target cannot change its recorded outcome");
        var wrongRevision = Cancellation(context, grant, current); wrongRevision.CancelTargetRevision--;
        check(authority.Handle("visitor", wrongRevision, 13).Reason == "work_cancel_stale" && native.Calls == calls,
            "matching request and cell alone cannot bypass the confirmed revision check");
        var stranger = authority.SetPermissions("stranger", RemoteWorkPermission.IssueTasks);
        check(authority.Handle("stranger", Cancellation(context, stranger, current), 14).Reason == "work_cancel_stale" && native.Calls == calls,
            "a valid task grant never lets a different author withdraw this visitor's plan");
        check(authority.Handle("owner", Cancellation(context, null, current), 15).Reason == "work_cancel_stale" && native.Calls == calls,
            "the per-plan cancellation operation is scoped to its authenticated author's own plans");

        var owner = Request(context, null, 4);
        check(Commit(authority, "owner", owner, 16).Accepted, "covered owner proposal remains available in its private plan list");
        calls = native.Calls;
        check(authority.Handle("owner", Cancellation(context, null, authority.GetOwnPlans("owner").Single()), 20).Accepted && native.Calls == calls,
            "withdrawing a covered proposal never cancels or reprioritizes the winning native order");
        var staleGrantCancel = Cancellation(context, grant, current);
        authority.SetPermissions("visitor", RemoteWorkPermission.None);
        authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        calls = native.Calls;
        check(authority.Handle("visitor", staleGrantCancel, 21).Reason == "work_not_authorized" && native.Calls == calls,
            "revocation and regrant invalidate queued cancellation tokens before any mutation");

        context = new Context(); native = new Executor(); authority = new RemoteWorkAuthority("owner", context, native);
        grant = authority.SetPermissions("visitor", RemoteWorkPermission.IssueTasks);
        check(Commit(authority, "owner", BuildRequest(context, null), 1).Accepted &&
            Commit(authority, "visitor", BuildRequest(context, grant, "StorageLocker", 11, 11, 8), 4).Accepted,
            "overlapping whole-building candidates have a confirmed visitor winner");
        check(authority.Handle("visitor", Cancellation(context, grant, authority.GetOwnPlans("visitor").Single()), 8).Accepted &&
            native.Native.Single().ActorId == "owner" && WorkPlanGeometry.Footprint(native.Native.Single()).Length == 6,
            "withdrawing one whole building reconciles the eligible owner's entire footprint without splitting plans");
        var buildCancel = Cancellation(context, null, authority.GetOwnPlans("owner").Single());
        native.BuildExecution = false; calls = native.Calls;
        check(authority.Handle("owner", buildCancel, 9).Reason == "work_native_build_validation_required" && native.Calls == calls && !authority.IsBlocked,
            "a building cancellation cannot reopen the closed production native building capability");
    }

    private static void CancellationNativeFailureBoundaries()
    {
        foreach (bool afterMutation in new[] { false, true })
        foreach (bool throwAfterMutation in new[] { false, true })
        {
            if (!afterMutation && throwAfterMutation) continue;
            var context = new Context(); var native = new Executor(); var authority = new RemoteWorkAuthority("owner", context, native);
            check(Commit(authority, "owner", Request(context, null)).Accepted, "native cancellation failure fixture starts with a confirmed task");
            var cancel = Cancellation(context, null, authority.GetOwnPlans("owner").Single());
            native.FailNext = true; native.FailureHasNoMutation = !afterMutation;
            native.FailAfterMutation = afterMutation; native.ThrowAfterMutation = throwAfterMutation;
            var result = authority.Handle("owner", cancel, 5);
            check(!result.Accepted && authority.IsBlocked == afterMutation &&
                result.Reason == (afterMutation ? "work_execution_uncertain" : "native_failure"),
                "cancellation failure distinguishes untouched native state from an uncertain partial result");
            check(authority.GetOwnPlans("owner").Length == 1 && authority.GetMergedPlans().Length == (afterMutation ? 0 : 1),
                "failed cancellation does not falsely publish removal of the previously confirmed candidate");
            int calls = native.Calls;
            check(!authority.Handle("owner", cancel, 6).Accepted && native.Calls == calls,
                "both failed and uncertain cancellation receipts are replayed without executing again");
            if (afterMutation)
                check(authority.Handle("owner", Cancellation(context, null, authority.GetOwnPlans("owner").Single()), 7).Reason == "work_execution_uncertain" && native.Calls == calls,
                    "a fresh request cannot continue physical work after a partially failed cancellation");
            else
                check(authority.Handle("owner", Cancellation(context, null, authority.GetOwnPlans("owner").Single()), 7).Accepted && native.Native.Length == 0,
                    "after a guaranteed mutation-free failure the user can make a fresh cancellation request");
        }
    }

    private static void CancellationWireAndRecovery()
    {
        var context = new Context(); var native = new Executor(); var authority = new RemoteWorkAuthority("owner", context, native);
        check(Commit(authority, "owner", BuildRequest(context, null)).Accepted, "wire cancellation fixture confirms a whole building");
        var plan = authority.GetOwnPlans("owner").Single();
        var cancel = Cancellation(context, null, plan);
        byte[] encoded = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = "owner", Request = cancel });
        var decoded = RemoteWorkWire.Decode(encoded).Request;
        check(encoded[4] == 6 && decoded.IsCancellation && decoded.CancelTargetRequestId == plan.RequestId &&
            decoded.CancelTargetRevision == plan.ConfirmedRevision && WorkPlanGeometry.SameBuild(decoded.Build, plan.Build),
            "cancel wire v6 round-trips the full building identity and exact confirmed revision");
        foreach (byte legacy in new byte[] { 4, 5 })
        {
            byte[] downgraded = (byte[])encoded.Clone(); downgraded[4] = legacy;
            bool rejected = false;
            try { RemoteWorkWire.Decode(downgraded); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "cancellation bytes cannot be reinterpreted as a legacy work creation request");
        }
        bool truncated = false;
        try { RemoteWorkWire.Decode(encoded.Take(encoded.Length - 1).ToArray()); } catch (EndOfStreamException) { truncated = true; }
        check(truncated, "truncated cancellation revision is refused before authority execution");
        int calls = native.Calls;
        cancel.DryRun = true;
        check(authority.Handle("owner", cancel, 4).Reason == "work_cancel_request", "cancellation cannot masquerade as a work creation preview");
        cancel.DryRun = false; cancel.PreviewToken = "unexpected";
        check(authority.Handle("owner", cancel, 5).Reason == "work_cancel_request", "creation preview tokens cannot alter the cancellation operation");
        cancel.PreviewToken = null; cancel.CancelTargetRevision = 0;
        check(authority.Handle("owner", cancel, 6).Reason == "work_cancel_request" && native.Calls == calls,
            "unconfirmed cancellation targets never reach the native executor");
        cancel.CancelTargetRevision = plan.ConfirmedRevision; cancel.AuthorityEpoch = Guid.NewGuid().ToString("N");
        check(authority.Handle("owner", cancel, 7).Reason == "work_stale_world" && native.Calls == calls,
            "a cancellation cannot cross the native reload authority boundary");

        var savedPlan = plan.Copy(); savedPlan.ActorId = "old-owner";
        var guestPlan = plan.Copy(); guestPlan.ActorId = "old-guest"; guestPlan.RequestId = Guid.NewGuid().ToString("N"); guestPlan.Cell = new WorkCell(30, 10);
        var covered = savedPlan.Copy(); covered.RequestId = Guid.NewGuid().ToString("N"); covered.Cell = new WorkCell(40, 10);
        var saved = new RemoteWorkSavedPlanet { PlanetId = plan.PlanetId, NativeWorldId = 0, OwnerPeerId = "old-owner", Plans = new[] { savedPlan, guestPlan, covered },
            Orders = new[] { new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, NativeObjectId = 31, Winner = savedPlan },
                new RemoteWorkSavedOrder { Token = Guid.NewGuid().ToString("N"), OriginEpoch = context.Planet.AuthorityEpoch, NativeObjectId = 32, Winner = guestPlan } } };
        var restoredNative = new PersistentExecutor { Saved = saved };
        authority = new RemoteWorkAuthority("new-owner", context, restoredNative);
        check(authority.Synchronize() && authority.GetMergedPlans().Length == 2, "two verified pending historical winners restore without native replay");
        check(authority.Handle("new-owner", Cancellation(context, null, authority.GetOwnPlans("new-owner").Single(p => p.HistoricalActive)), 10).Accepted &&
            authority.GetMergedPlans().Single().ActorId == "old-guest" && restoredNative.Calls == 1,
            "the remapped local owner can withdraw its precise restored plan while historical guest work remains intact");
        check(authority.GetOwnPlans("new-owner").Single().HistoricalActive == false,
            "historical covered proposals never become executable merely because a restored plan was cancelled");

        using (var network = new DeterministicRoomNetwork(2268))
        {
            Peer peer = null;
            using (peer = new Peer(network.Host, null, planet => peer != null && peer.Context.Planet.PlanetId == planet ? peer.Session.LocalPlayerId : null))
            {
                peer.Pump(0);
                var draft = peer.Work.Draft(peer.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(5, 5) });
                check(peer.Work.SubmitDraft(peer.Session.LocalPlayerId, draft, true), "local cancellation UI fixture confirms a plan");
                check(peer.Work.CancelPlan(peer.Session.LocalPlayerId, peer.Work.Authority.GetOwnPlans(peer.Session.LocalPlayerId).Single()),
                    "local cancellation API accepts the owner's exact plan");
                var status = peer.Work.GetSubmission(peer.Work.LastSubmittedRequestId);
                check(status != null && status.Accepted && status.Reason == "work_cancelled",
                    "synchronous cancellation receipt is retained rather than overwritten by a queued UI state");
            }
        }
    }

    private static void CancellationRoutingAndLostReceipt()
    {
        using (var network = new DeterministicRoomNetwork(9462))
        {
            network.Settings.ReliableOrdered = true; network.Settings.Delay = .8; network.Settings.Jitter = .2;
            network.Settings.Drop = .05; network.Settings.Duplicate = .05;
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
                throw new Exception("Cancellation routing timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 3), "three production protocol sessions join the cancellation routing test");
                Peer host = peers[0], owner = peers[1], actor = peers[2];
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions == RemoteWorkPermission.IssueTasks,
                    "remote cancellation author has the owner's current task grant");
                var draft = actor.Work.DraftBuild(owner.Context.Planet.PlanetId, new WorkCell(10, 10), new WorkBuildSpec { PrefabId = "Outhouse", MaterialTags = new[] { "Granite" } });
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, draft, true), "remote whole-building draft enters the existing confirm path");
                until(() => actor.Work.GetSubmission(draft.RequestId)?.Accepted == true && !actor.Work.GetSubmission(draft.RequestId).DryRun,
                    "the building plan is confirmed before cancellation is offered");
                check(actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false), "author requests its confirmed plan list");
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 1,
                    "the author receives the exact whole-building cancellation target");
                var plan = actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Single();
                check(!host.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "room host cannot submit the author's cancel API as itself");
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    var message = RemoteWorkWire.Decode(packet.Payload);
                    return message.Kind == RemoteWorkMessageKind.Receipt && message.Receipt.Reason == "work_cancelled";
                };
                check(actor.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "author submits its precise confirmed building cancellation");
                string cancellationId = actor.Work.LastSubmittedRequestId;
                check(actor.Work.GetSubmission(cancellationId)?.Reason == "work_queued",
                    "remote cancellation has a tracked pending state before any acknowledgement");
                check(actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 1,
                    "sending cancellation does not optimistically erase the still-unconfirmed displayed plan");
                until(() => owner.Executor.Native.Length == 0, "only the planet owner's executor reconciles the cancellation");
                int calls = owner.Executor.Calls;
                check(owner.Executor.Cancelled == 1 && !actor.Receipts.ContainsKey(cancellationId) && host.Executor.Calls == 0 && actor.Executor.Calls == 0,
                    "withheld cancel receipt leaves other processes untouched and no false client confirmation");
                network.CutAll();
                until(() => peers.Skip(1).All(p => p.Session.Reconnecting), "cancel receipt loss followed by disconnect preserves pending request identity");
                until(() => peers.All(p => p.Session.Joined && !p.Session.Reconnecting), "cancellation sessions reconnect with their original identities");
                network.Settings.DropBeforeDelivery = null;
                until(() => actor.Receipts.ContainsKey(cancellationId), "retried cancellation receives the stored authoritative receipt");
                check(actor.Receipts[cancellationId].Accepted && owner.Executor.Calls == calls && owner.Executor.Cancelled == 1,
                    "reconnect retries cancel the blueprint exactly once");
                check(actor.Work.GetSubmission(cancellationId)?.Reason == "work_cancelled" && actor.Work.GetSubmission(cancellationId).Accepted,
                    "authoritative cancellation result replaces the pending UI state by its request ID");
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 0,
                    "successful cancellation automatically refreshes the author's confirmed plan cache");
                var replacement = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { plan.Cell });
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, replacement, true), "author can issue a new task after acknowledged cancellation");
                until(() => owner.Executor.Native.Length == 1, "fresh task reaches the owner's native boundary");
                check(actor.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "a stale UI target can be sent but is not trusted by the owner");
                string staleId = actor.Work.LastSubmittedRequestId;
                until(() => actor.Receipts.ContainsKey(staleId), "stale cancellation receives an explicit response");
                check(actor.Receipts[staleId].Reason == "work_cancel_stale" && owner.Executor.Native.Single().Kind == RemoteWorkKind.Dig,
                    "delayed UI cancellation of an old building preserves the newly confirmed dig at the same location");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }

    private static void CancellationTimeoutAndCompletion()
    {
        using (var network = new DeterministicRoomNetwork(6713))
        {
            var peers = new List<Peer>();
            Func<string, string> lookup = planet => peers.Where(p => p.Context.Planet.PlanetId == planet).Select(p => p.Session.LocalPlayerId).FirstOrDefault();
            peers.Add(new Peer(network.Host, null, lookup));
            peers.Add(new Peer(network.Connect(), network.Connect, lookup));
            Action step = () => { network.Advance(.05); foreach (var peer in peers) peer.Pump(network.Now); };
            Action<Func<bool>, string> until = (condition, label) =>
            {
                double end = network.Now + 100;
                while (network.Now < end) { step(); if (condition()) { check(true, label); return; } }
                throw new Exception("Cancellation status timeout: " + label);
            };
            try
            {
                until(() => peers.All(p => p.Session.Joined && p.Session.Players.Length == 2), "two production sessions join cancellation status test");
                Peer owner = peers[0], actor = peers[1];
                owner.Work.SetPermissions(actor.Session.LocalPlayerId, RemoteWorkPermission.IssueTasks);
                until(() => actor.Work.GetReceivedGrant(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId)?.Permissions == RemoteWorkPermission.IssueTasks,
                    "status fixture receives the current task grant");
                var draft = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(8, 8) });
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, draft, true), "status fixture submits a native-boundary dig");
                until(() => actor.Work.GetSubmission(draft.RequestId)?.Accepted == true && !actor.Work.GetSubmission(draft.RequestId).DryRun,
                    "status fixture dig is confirmed");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false);
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 1,
                    "status fixture caches the exact confirmed cancellation target");
                var plan = actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Single();
                network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !RemoteWorkWire.IsMessage(packet.Payload)) return false;
                    var message = RemoteWorkWire.Decode(packet.Payload);
                    return message.Kind == RemoteWorkMessageKind.Request && message.Request.IsCancellation;
                };
                check(actor.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "cancellation can be queued while its requests are deliberately withheld");
                string firstId = actor.Work.LastSubmittedRequestId;
                int changes = 0, changesAtTimeout = -1;
                actor.Work.Changed += () => changes++;
                actor.Work.ReceiptReceived += (sender, receipt) =>
                {
                    if (receipt.RequestId == firstId && receipt.Reason == "work_response_unconfirmed") changesAtTimeout = changes;
                };
                until(() => actor.Work.GetSubmission(firstId)?.Reason == "work_response_unconfirmed",
                    "withheld cancellation reaches the bounded ninety-second response deadline");
                check(changesAtTimeout >= 0 && changes > changesAtTimeout,
                    "a timeout receipt notifies the view model after storing its new submission status");
                check(owner.Executor.Native.Length == 1 && owner.Executor.Cancelled == 0 &&
                    actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Single().RequestId == plan.RequestId,
                    "an unconfirmed timeout neither removes the displayed plan nor invents native cancellation");

                network.Settings.DropBeforeDelivery = null;
                check(actor.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "an exact still-current plan can be cancelled again after a transport response timeout");
                string retryId = actor.Work.LastSubmittedRequestId;
                check(retryId != firstId && actor.Work.GetSubmission(retryId)?.Reason == "work_queued",
                    "an explicit timeout retry gets its own tracked request without rewriting the unknown earlier outcome");
                until(() => actor.Work.GetSubmission(retryId)?.Accepted == true, "timeout retry receives the authoritative cancellation result");
                check(owner.Executor.Cancelled == 1 && owner.Executor.Native.Length == 0 &&
                    actor.Work.GetSubmission(firstId)?.Reason == "work_response_unconfirmed",
                    "timeout retry cancels once while preserving the prior request's honest unknown status");
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 0,
                    "acknowledged timeout retry refreshes the confirmed plan cache");

                var completed = actor.Work.Draft(owner.Context.Planet.PlanetId, RemoteWorkKind.Dig, new[] { new WorkCell(9, 8) });
                check(actor.Work.SubmitDraft(owner.Session.LocalPlayerId, completed, true), "completion race fixture submits a new dig");
                until(() => actor.Work.GetSubmission(completed.RequestId)?.Accepted == true && !actor.Work.GetSubmission(completed.RequestId).DryRun,
                    "completion race fixture dig is confirmed");
                actor.Work.RequestPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false);
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 1,
                    "completion race fixture has a cached confirmed plan");
                plan = actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Single();
                owner.Executor.Native = Array.Empty<WorkTaskIntent>();
                owner.Work.Authority.ForgetNativePlan(plan);
                int calls = owner.Executor.Calls;
                check(actor.Work.CancelPlan(owner.Session.LocalPlayerId, plan), "a cached target may race the native completion notification");
                string completedId = actor.Work.LastSubmittedRequestId;
                int changesAtRejection = -1;
                actor.Work.ReceiptReceived += (sender, receipt) =>
                {
                    if (receipt.RequestId == completedId) changesAtRejection = changes;
                };
                until(() => actor.Work.GetSubmission(completedId)?.Reason == "work_cancel_stale",
                    "a completed native task returns a stale cancellation result instead of false success");
                check(changesAtRejection >= 0 && changes > changesAtRejection && owner.Executor.Calls == calls,
                    "a rejected cancellation notifies its view model without any native reconciliation");
                until(() => actor.Work.GetPlans(owner.Session.LocalPlayerId, owner.Context.Planet.PlanetId, false).Length == 0,
                    "stale cancellation refreshes the authoritative cache and removes the already-completed row");
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }
    }
}
