using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    internal sealed class RemoteWorkCoordinator : IDisposable
    {
        private readonly RoomSession session;
        private readonly Func<string, string> ownerOfPlanet;
        private readonly Dictionary<string, RemoteWorkGrant> receivedGrants = new Dictionary<string, RemoteWorkGrant>();
        private readonly Dictionary<string, RemoteWorkRequest> drafts = new Dictionary<string, RemoteWorkRequest>();
        private readonly Dictionary<string, WorkTaskIntent[]> receivedPlans = new Dictionary<string, WorkTaskIntent[]>();
        private readonly Dictionary<string, PlanAssembly> assemblies = new Dictionary<string, PlanAssembly>();
        private readonly Dictionary<string, string> authorityErrors = new Dictionary<string, string>();
        private readonly Dictionary<string, System.Tuple<string, long>> authorityStatuses = new Dictionary<string, System.Tuple<string, long>>();
        private readonly Queue<System.Tuple<string, byte[]>> outgoing = new Queue<System.Tuple<string, byte[]>>();
        private readonly Dictionary<string, PendingRequest> pending = new Dictionary<string, PendingRequest>();
        private readonly Dictionary<string, System.Tuple<string, RemoteWorkRequest>> autoConfirm = new Dictionary<string, System.Tuple<string, RemoteWorkRequest>>();
        private readonly Dictionary<string, string> requestDrafts = new Dictionary<string, string>();
        private readonly Dictionary<string, RemoteWorkReceipt> submissions = new Dictionary<string, RemoteWorkReceipt>();
        private readonly HashSet<string> invalidatedScopes = new HashSet<string>();
        private readonly Dictionary<string, string> ownerScopes = new Dictionary<string, string>();
        private readonly Dictionary<string, long> ownerGenerations = new Dictionary<string, long>();
        private long advertisedGeneration;
        private bool disposed;
        private double now;
        private bool clockStarted;
        private double nextGrants;
        internal RemoteWorkAuthority Authority { get; private set; }
        internal event System.Action Changed;
        internal event System.Action<string, RemoteWorkReceipt> ReceiptReceived;
        internal event System.Action<string, RemoteWorkReceipt> SubmissionChanged;
        internal string LastError { get; private set; }
        internal string LastSubmittedRequestId { get; private set; }
        private sealed class PlanAssembly { internal string Epoch; internal long Revision; internal WorkTaskIntent[] Items; internal bool[] Seen; }
        private sealed class PendingRequest { internal string Owner; internal RemoteWorkRequest Request; internal double RetryAt; internal double Deadline; }

        // ownerOfPlanet must resolve authenticated StarSystemCoordinator manifests, not packet claims.
        internal RemoteWorkCoordinator(RoomSession session, IWorldWorkContext context, IWorkExecutor executor, Func<string, string> ownerOfPlanet)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.ownerOfPlanet = ownerOfPlanet ?? throw new ArgumentNullException("ownerOfPlanet");
            Authority = new RemoteWorkAuthority(session.LocalPlayerId, context, executor, session.NextWorkAuthorityGeneration);
            session.ApplicationMessageReceived += Receive;
        }

        internal void Pump(double seconds)
        {
            if (!clockStarted)
            {
                double offset = seconds - now;
                foreach (var entry in pending.Values) { entry.RetryAt += offset; entry.Deadline += offset; }
                clockStarted = true;
            }
            now = seconds;
            if (disposed || !session.Joined || session.Reconnecting || session.Leaving) return;
            Authority.BindOwner(session.LocalPlayerId);
            Authority.Synchronize();
            if (advertisedGeneration != Authority.Generation) { advertisedGeneration = Authority.Generation; nextGrants = now; }
            foreach (var entry in pending.Values.ToArray())
            {
                if (now >= entry.Deadline)
                {
                    pending.Remove(entry.Request.RequestId); LastError = "work_response_unconfirmed";
                    DeliverReceipt(entry.Owner, new RemoteWorkReceipt { RequestId = entry.Request.RequestId, DryRun = entry.Request.DryRun, Reason = LastError });
                }
                else if (now >= entry.RetryAt && Connected(entry.Owner))
                {
                    Send(entry.Owner, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, Request = entry.Request });
                    entry.RetryAt = now + 3;
                }
            }
            if (now >= nextGrants)
            {
                nextGrants = now + 5;
                foreach (var player in session.Players.Where(p => p.Connected && p.Id != session.LocalPlayerId))
                {
                    SendScope(player.Id);
                }
            }
            int budget = 4;
            while (outgoing.Count > 0 && budget-- > 0)
            {
                var item = outgoing.Dequeue();
                if (session.IsHost) session.SendApplicationTo(item.Item1, item.Item2);
                else session.SendApplication(item.Item2);
            }
        }

        internal RemoteWorkGrant SetPermissions(string actorId, RemoteWorkPermission permissions)
        {
            Authority.BindOwner(session.LocalPlayerId);
            if (!session.Players.Any(p => p.Id == actorId) && Authority.GetGrant(actorId) == null)
                throw new InvalidOperationException("work_actor_unknown");
            RemoteWorkGrant grant = Authority.SetPermissions(actorId, permissions);
            Send(actorId, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, Grant = grant });
            Notify(); return grant;
        }
        // Explicit local owner operation for old session authors; never creates a new peer grant.
        internal void RemoveHistoricalPlans(string historicalActorId)
        {
            Authority.BindOwner(session.LocalPlayerId);
            if (!Authority.GetPlansByActor(historicalActorId).Any(p => p.Historical)) throw new InvalidOperationException("work_historical_author_missing");
            Authority.SetPermissions(historicalActorId, RemoteWorkPermission.None); Notify();
        }

        internal RemoteWorkGrant GetReceivedGrant(string ownerId, string planetId)
        {
            RemoteWorkGrant grant;
            long generation;
            if (ownerOfPlanet(planetId) != ownerId || !receivedGrants.TryGetValue(ownerId + ":" + planetId, out grant) ||
                (ownerGenerations.TryGetValue(ownerId, out generation) && grant.Generation != generation)) return null;
            return RemoteWorkAuthority.CopyGrant(grant);
        }
        internal string ExecutionError(string ownerId, string planetId)
        {
            if (ownerId == session.LocalPlayerId) return Authority.IsBlocked ? Authority.BlockReason : null;
            string error;
            return authorityErrors.TryGetValue(ownerId + ":" + planetId, out error) ? error : null;
        }

        // Personal drafts stay in this process. Drafting alone sends no terrain or task data.
        internal RemoteWorkRequest Draft(string planetId, RemoteWorkKind kind, WorkCell[] cells, int priority = 5, WorkBuildSpec build = null)
        {
            if (drafts.Count >= RemoteWorkAuthority.MaxPlans) throw new InvalidOperationException("work_draft_capacity");
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = planetId, Kind = kind,
                Layer = kind == RemoteWorkKind.Build ? RemoteTaskLayer.Building : RemoteTaskLayer.Terrain,
                Build = build == null ? null : build.Copy(), Cells = cells == null ? Array.Empty<WorkCell>() : (WorkCell[])cells.Clone(), Priority = priority, DryRun = true };
            drafts[request.RequestId] = CopyRequest(request);
            requestDrafts[request.RequestId] = request.RequestId;
            Submission(request.RequestId, new RemoteWorkReceipt { RequestId = request.RequestId, DryRun = true, Reason = "work_draft" });
            Notify(); return request;
        }
        internal RemoteWorkRequest DraftBuild(string planetId, WorkCell anchor, WorkBuildSpec build, int priority = 5)
        {
            if (!WorkPlanGeometry.ValidBuild(build)) throw new ArgumentException("work_unsupported_build_spec");
            return Draft(planetId, RemoteWorkKind.Build, new[] { anchor }, priority, build);
        }
        internal RemoteWorkRequest[] OwnDrafts(string planetId)
        { return drafts.Values.Where(d => d.PlanetId == planetId).Select(CopyRequest).ToArray(); }
        internal RemoteWorkReceipt GetSubmission(string draftId)
        { RemoteWorkReceipt value; return submissions.TryGetValue(draftId, out value) ? RemoteWorkAuthority.CopyReceipt(value) : null; }
        internal void RemoveDraft(string requestId) { drafts.Remove(requestId); Notify(); }

        internal bool Preview(string ownerId, RemoteWorkRequest draft)
        {
            RemoteWorkRequest request = CopyRequest(draft);
            request.DryRun = true; request.PreviewToken = "";
            if (!BindGrant(ownerId, request)) return false;
            if (!string.IsNullOrEmpty(draft.AuthorityEpoch) && draft.AuthorityEpoch != request.AuthorityEpoch) return false;
            draft.AuthorityEpoch = request.AuthorityEpoch;
            if (drafts.ContainsKey(draft.RequestId)) drafts[draft.RequestId].AuthorityEpoch = request.AuthorityEpoch;
            requestDrafts[request.RequestId] = draft.RequestId;
            Submission(draft.RequestId, new RemoteWorkReceipt { RequestId = request.RequestId, DryRun = true, Reason = "work_queued" });
            return Submit(ownerId, request);
        }

        // UI can immediately draw this personal draft. Only authority confirmation enters the merge.
        // Call Preview/Confirm separately when the user must inspect a potentially dangerous dig.
        internal bool SubmitDraft(string ownerId, RemoteWorkRequest draft, bool automaticallyConfirm)
        {
            if (automaticallyConfirm) autoConfirm[draft.RequestId] = System.Tuple.Create(ownerId, CopyRequest(draft));
            bool sent = Preview(ownerId, draft);
            if (!sent) autoConfirm.Remove(draft.RequestId);
            return sent;
        }

        internal bool Confirm(string ownerId, RemoteWorkRequest draft, RemoteWorkReceipt preview)
        {
            if (preview == null || !preview.Accepted || !preview.DryRun || preview.RequestId != draft.RequestId) return false;
            RemoteWorkRequest request = CopyRequest(draft);
            request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
            if (!BindGrant(ownerId, request) || (!string.IsNullOrEmpty(draft.AuthorityEpoch) && draft.AuthorityEpoch != request.AuthorityEpoch)) return false;
            if (requestDrafts.Count >= RemoteWorkAuthority.MaxLedger) return false;
            requestDrafts[request.RequestId] = draft.RequestId;
            Submission(draft.RequestId, new RemoteWorkReceipt { RequestId = request.RequestId, Reason = "work_confirming" });
            return Submit(ownerId, request);
        }

        internal bool Submit(string ownerId, RemoteWorkRequest request)
        {
            if (!session.Joined || session.Reconnecting || !Connected(ownerId) || ownerOfPlanet(request.PlanetId) != ownerId) return false;
            LastSubmittedRequestId = request.RequestId;
            if (ownerId == session.LocalPlayerId)
            { DeliverReceipt(ownerId, Authority.Handle(session.LocalPlayerId, CopyRequest(request), now)); Notify(); return true; }
            if (pending.Count >= 128 && !pending.ContainsKey(request.RequestId)) return false;
            if (!Send(ownerId, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, Request = CopyRequest(request) })) return false;
            pending[request.RequestId] = new PendingRequest { Owner = ownerId, Request = CopyRequest(request), RetryAt = now + 3, Deadline = now + 90 };
            return true;
        }

        // Keep the displayed plan until its authority confirms cancellation. Selecting one
        // plan cannot withdraw another author's work or a newer plan at the same location.
        internal bool CancelPlan(string ownerId, WorkTaskIntent plan)
        {
            if (plan == null || plan.ActorId != session.LocalPlayerId || !WorkPlanGeometry.ValidShape(plan.Kind, plan.Layer, plan.Build)) return false;
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = plan.PlanetId,
                Kind = plan.Kind, Layer = plan.Layer, Priority = plan.Priority, Cells = new[] { plan.Cell },
                Build = plan.Build == null ? null : plan.Build.Copy(), CancelTargetRequestId = plan.RequestId, CancelTargetRevision = plan.ConfirmedRevision };
            if (!BindGrant(ownerId, request) || requestDrafts.Count >= RemoteWorkAuthority.MaxLedger) return false;
            // Map before Submit: the local owner's authoritative receipt is synchronous.
            requestDrafts[request.RequestId] = request.RequestId;
            Submission(request.RequestId, new RemoteWorkReceipt { RequestId = request.RequestId, Reason = "work_queued" });
            if (Submit(ownerId, request)) { Notify(); return true; }
            requestDrafts.Remove(request.RequestId); submissions.Remove(request.RequestId);
            Notify(); return false;
        }

        internal bool RequestPlans(string ownerId, string planetId, bool merged, string actorId = null)
        {
            if (ownerOfPlanet(planetId) != ownerId || !Connected(ownerId)) return false;
            if (ownerId == session.LocalPlayerId) { Notify(); return true; }
            string planActor = merged ? "" : actorId ?? session.LocalPlayerId;
            if (!merged && !session.Players.Any(p => p.Id == planActor)) return false;
            return Send(ownerId, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.QueryPlans, PlanetId = planetId, Merged = merged, PlanActorId = planActor });
        }
        internal WorkTaskIntent[] GetPlans(string ownerId, string planetId, bool merged, string actorId = null)
        {
            if (ownerOfPlanet(planetId) != ownerId) return Array.Empty<WorkTaskIntent>();
            if (merged && ExecutionError(ownerId, planetId) != null) return Array.Empty<WorkTaskIntent>();
            string planActor = merged ? "" : actorId ?? session.LocalPlayerId;
            if (ownerId == session.LocalPlayerId)
                return Authority.Planet != null && Authority.Planet.PlanetId == planetId ? (merged ? Authority.GetMergedPlans() : Authority.GetPlansByActor(planActor)) : Array.Empty<WorkTaskIntent>();
            WorkTaskIntent[] result;
            return receivedPlans.TryGetValue(PlanKey(ownerId, planetId, merged, planActor), out result) ? result.Select(p => p.Copy()).ToArray() : Array.Empty<WorkTaskIntent>();
        }

        private bool BindGrant(string ownerId, RemoteWorkRequest request)
        {
            Authority.BindOwner(session.LocalPlayerId);
            if (ownerId == session.LocalPlayerId)
            {
                if (!Authority.Synchronize() || Authority.Planet.PlanetId != request.PlanetId) return false;
                request.AuthorityEpoch = Authority.Planet.AuthorityEpoch; request.GrantToken = ""; return true;
            }
            RemoteWorkGrant grant = GetReceivedGrant(ownerId, request.PlanetId);
            if (grant == null || (grant.Permissions & RemoteWorkPermission.IssueTasks) == 0) return false;
            request.AuthorityEpoch = grant.AuthorityEpoch; request.GrantToken = grant.Token; return true;
        }

        private void Receive(string sender, byte[] data)
        {
            if (disposed || !RemoteWorkWire.IsMessage(data) || !Connected(sender)) return;
            Authority.BindOwner(session.LocalPlayerId);
            RemoteWorkMessage message;
            try { message = RemoteWorkWire.Decode(data); }
            catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException || error is ArgumentException) { return; }
            if (message.TargetId != session.LocalPlayerId)
            {
                if (session.IsHost && Connected(message.TargetId)) session.ForwardApplication(sender, message.TargetId, data);
                return;
            }
            if (message.Kind == RemoteWorkMessageKind.Grant)
            {
                var grant = message.Grant;
                if (grant.ActorId != session.LocalPlayerId || ownerOfPlanet(grant.PlanetId) != sender) return;
                string key = sender + ":" + grant.PlanetId;
                long generation;
                ownerGenerations.TryGetValue(sender, out generation);
                string scope;
                if (grant.Generation < generation || (grant.Generation == generation && ownerScopes.TryGetValue(sender, out scope) &&
                    scope != key + ":" + grant.AuthorityEpoch)) return;
                RemoteWorkGrant old;
                if (receivedGrants.TryGetValue(key, out old) && (grant.Generation < old.Generation ||
                    (grant.Generation == old.Generation && (grant.AuthorityEpoch != old.AuthorityEpoch || old.Revision >= grant.Revision)))) return;
                bool changedScope = grant.Generation > generation;
                if (changedScope)
                {
                    if (generation == 0)
                    {
                        // The first notification may be a queued revision-zero scope from before
                        // its real grant. Preserve legitimate requests already bound to this epoch.
                        RetirePending(pending.Values.Where(p => p.Owner == sender &&
                            (p.Request.PlanetId != grant.PlanetId || p.Request.AuthorityEpoch != grant.AuthorityEpoch)).ToArray());
                    }
                    else
                        foreach (string planet in receivedGrants.Where(p => p.Key.StartsWith(sender + ":", StringComparison.Ordinal)).Select(p => p.Value.PlanetId)
                            .Concat(pending.Values.Where(p => p.Owner == sender).Select(p => p.Request.PlanetId)).Distinct().ToArray())
                            InvalidateScope(sender, planet);
                    ownerGenerations[sender] = grant.Generation; ownerScopes[sender] = key + ":" + grant.AuthorityEpoch;
                    invalidatedScopes.Remove(key);
                }
                else ClearPlans(key);
                receivedGrants[key] = RemoteWorkAuthority.CopyGrant(grant);
                Notify();
            }
            else if (message.Kind == RemoteWorkMessageKind.Request)
            {
                if (ownerOfPlanet(message.Request.PlanetId) != session.LocalPlayerId) return;
                var receipt = Authority.Handle(sender, message.Request, now);
                Send(sender, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Receipt, Receipt = receipt });
                if (Authority.IsBlocked) ReplyPlans(sender, new RemoteWorkMessage { PlanetId = message.Request.PlanetId, Merged = true });
                Notify();
            }
            else if (message.Kind == RemoteWorkMessageKind.Receipt)
            {
                PendingRequest request;
                if (!pending.TryGetValue(message.Receipt.RequestId, out request) || request.Owner != sender) return;
                RemoteWorkGrant scopeGrant = GetReceivedGrant(sender, request.Request.PlanetId);
                if (message.Receipt.Reason == "work_stale_world" &&
                    (scopeGrant == null || scopeGrant.AuthorityEpoch == request.Request.AuthorityEpoch)) InvalidateScope(sender, request.Request.PlanetId);
                else
                {
                    pending.Remove(message.Receipt.RequestId); DeliverReceipt(sender, message.Receipt);
                    if (request.Request.IsCancellation && (message.Receipt.Accepted || message.Receipt.Reason == "work_cancel_stale"))
                    {
                        RequestPlans(sender, request.Request.PlanetId, false);
                        RequestPlans(sender, request.Request.PlanetId, true);
                    }
                }
            }
            else if (message.Kind == RemoteWorkMessageKind.QueryPlans) ReplyPlans(sender, message);
            else if (message.Kind == RemoteWorkMessageKind.Plans) AcceptPlans(sender, message);
        }

        private void ReplyPlans(string sender, RemoteWorkMessage query)
        {
            if (!Authority.Synchronize() || Authority.Planet.PlanetId != query.PlanetId || ownerOfPlanet(query.PlanetId) != session.LocalPlayerId) return;
            SendScope(sender);
            if (query.Merged && !Authority.HasPermission(sender, RemoteWorkPermission.IssueTasks) && !Authority.HasPermission(sender, RemoteWorkPermission.ViewPlans)) return;
            string planActor = query.Merged ? "" : string.IsNullOrEmpty(query.PlanActorId) ? sender : query.PlanActorId;
            if (!query.Merged && (planActor != sender && !Authority.HasPermission(sender, RemoteWorkPermission.ViewPlans))) return;
            if (!query.Merged && !session.Players.Any(p => p.Id == planActor)) return;
            WorkTaskIntent[] plans = Authority.IsBlocked ? Array.Empty<WorkTaskIntent>() : query.Merged ? Authority.GetMergedPlans() : Authority.GetPlansByActor(planActor);
            int pageSize = plans.Any(p => p.Build != null) ? 4 : 8;
            for (int offset = 0; offset < Math.Max(1, plans.Length); offset += pageSize)
                Send(sender, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Plans, PlanetId = query.PlanetId, Merged = query.Merged,
                    PlanActorId = planActor,
                    AuthorityEpoch = Authority.Planet.AuthorityEpoch, Generation = Authority.Generation, GrantRevision = Authority.GetGrant(sender)?.Revision ?? 0, Revision = Authority.StateRevision, Total = plans.Length,
                    AuthorityBlocked = Authority.IsBlocked, AuthorityError = Authority.BlockReason,
                    Offset = offset, Plans = plans.Skip(offset).Take(pageSize).ToArray() });
        }
        private void AcceptPlans(string sender, RemoteWorkMessage message)
        {
            if (ownerOfPlanet(message.PlanetId) != sender || invalidatedScopes.Contains(sender + ":" + message.PlanetId)) return;
            RemoteWorkGrant grant = GetReceivedGrant(sender, message.PlanetId);
            if (grant == null || grant.Generation != message.Generation || grant.Revision != message.GrantRevision || grant.AuthorityEpoch != message.AuthorityEpoch || (message.Merged &&
                (grant.Permissions & (RemoteWorkPermission.IssueTasks | RemoteWorkPermission.ViewPlans)) == 0)) return;
            if (!message.Merged && message.PlanActorId != session.LocalPlayerId && (grant.Permissions & RemoteWorkPermission.ViewPlans) == 0) return;
            if (!message.Merged && !session.Players.Any(p => p.Id == message.PlanActorId)) return;
            string authorityKey = sender + ":" + message.PlanetId;
            System.Tuple<string, long> status;
            if (authorityStatuses.TryGetValue(authorityKey, out status) && status.Item1 == message.AuthorityEpoch && message.Revision < status.Item2) return;
            authorityStatuses[authorityKey] = System.Tuple.Create(message.AuthorityEpoch, message.Revision);
            if (message.AuthorityBlocked)
            {
                authorityErrors[authorityKey] = string.IsNullOrEmpty(message.AuthorityError) ? "work_execution_uncertain" : message.AuthorityError;
                foreach (string cached in receivedPlans.Keys.Where(k => k.StartsWith(authorityKey + ":", StringComparison.Ordinal)).ToArray()) receivedPlans.Remove(cached);
                Notify(); return;
            }
            authorityErrors.Remove(authorityKey);
            string key = PlanKey(sender, message.PlanetId, message.Merged, message.PlanActorId);
            PlanAssembly assembly;
            if (!assemblies.TryGetValue(key, out assembly) || assembly.Epoch != message.AuthorityEpoch || message.Revision > assembly.Revision)
            { assembly = new PlanAssembly { Epoch = message.AuthorityEpoch, Revision = message.Revision, Items = new WorkTaskIntent[message.Total], Seen = new bool[message.Total] }; assemblies[key] = assembly; }
            if (assembly.Revision != message.Revision || assembly.Items.Length != message.Total) return;
            for (int i = 0; i < message.Plans.Length; i++)
            {
                WorkTaskIntent plan = message.Plans[i];
                if (!message.Merged && plan.ActorId != message.PlanActorId) return;
                assembly.Items[message.Offset + i] = plan.Copy(); assembly.Seen[message.Offset + i] = true;
            }
            if (assembly.Seen.All(s => s)) { receivedPlans[key] = assembly.Items.Select(p => p.Copy()).ToArray(); Notify(); }
        }
        private void ClearPlans(string key)
        {
            foreach (string cacheKey in receivedPlans.Keys.Where(k => k.StartsWith(key + ":", StringComparison.Ordinal)).ToArray()) receivedPlans.Remove(cacheKey);
            foreach (string cacheKey in assemblies.Keys.Where(k => k.StartsWith(key + ":", StringComparison.Ordinal)).ToArray()) assemblies.Remove(cacheKey);
        }
        private void InvalidateScope(string ownerId, string planetId)
        {
            string key = ownerId + ":" + planetId;
            ClearPlans(key); invalidatedScopes.Add(key);
            authorityErrors.Remove(key); authorityStatuses.Remove(key);
            RemoteWorkGrant grant;
            // Keep generation high-water and epoch, but revoke locally until a newer scope arrives.
            if (receivedGrants.TryGetValue(key, out grant))
            { grant.Permissions = RemoteWorkPermission.None; grant.Token = ""; }
            RetirePending(pending.Values.Where(p => p.Owner == ownerId && p.Request.PlanetId == planetId).ToArray());
            foreach (string id in autoConfirm.Where(p => p.Value.Item1 == ownerId && p.Value.Item2.PlanetId == planetId).Select(p => p.Key).ToArray()) autoConfirm.Remove(id);
            Notify();
        }
        private void RetirePending(PendingRequest[] requests)
        {
            foreach (var entry in requests)
            {
                pending.Remove(entry.Request.RequestId); autoConfirm.Remove(entry.Request.RequestId);
                DeliverReceipt(entry.Owner, new RemoteWorkReceipt { RequestId = entry.Request.RequestId,
                    DryRun = entry.Request.DryRun, Reason = "work_stale_world" });
            }
        }
        private void SendScope(string actorId)
        {
            WorkPlanet planet = Authority.Planet;
            if (planet == null || ownerOfPlanet(planet.PlanetId) != session.LocalPlayerId) return;
            RemoteWorkGrant grant = Authority.GetGrant(actorId) ?? new RemoteWorkGrant { PlanetId = planet.PlanetId,
                AuthorityEpoch = planet.AuthorityEpoch, ActorId = actorId, Token = "", Generation = Authority.Generation,
                Revision = 0, Permissions = RemoteWorkPermission.None };
            Send(actorId, new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Grant, Grant = grant });
        }
        private bool Send(string target, RemoteWorkMessage message)
        {
            if (outgoing.Count >= 1024 || !Connected(target)) { LastError = "work_send_queue_full"; return false; }
            message.TargetId = target;
            outgoing.Enqueue(Tuple.Create(target, RemoteWorkWire.Encode(message))); return true;
        }
        private bool Connected(string id) { return session.Players.Any(p => p.Id == id && p.Connected); }
        private static string PlanKey(string owner, string planet, bool merged, string actor)
        { return owner + ":" + planet + ":" + merged + ":" + (merged ? "" : actor); }
        private void DeliverReceipt(string sender, RemoteWorkReceipt receipt)
        {
            string draftId;
            if (requestDrafts.TryGetValue(receipt.RequestId, out draftId)) Submission(draftId, receipt);
            if (ReceiptReceived != null) ReceiptReceived(sender, RemoteWorkAuthority.CopyReceipt(receipt));
            System.Tuple<string, RemoteWorkRequest> draft;
            if (autoConfirm.TryGetValue(receipt.RequestId, out draft) && draft.Item1 == sender)
            {
                autoConfirm.Remove(receipt.RequestId);
                if (receipt.Accepted && receipt.DryRun) Confirm(sender, draft.Item2, receipt);
            }
            Notify();
        }
        private void Submission(string draftId, RemoteWorkReceipt receipt)
        {
            submissions[draftId] = RemoteWorkAuthority.CopyReceipt(receipt);
            if (SubmissionChanged != null) SubmissionChanged(draftId, RemoteWorkAuthority.CopyReceipt(receipt));
        }
        private void Notify() { if (Changed != null) Changed(); }
        private static RemoteWorkRequest CopyRequest(RemoteWorkRequest r)
        { return new RemoteWorkRequest { RequestId = r.RequestId, PlanetId = r.PlanetId, AuthorityEpoch = r.AuthorityEpoch, GrantToken = r.GrantToken,
            Kind = r.Kind, Layer = r.Layer, Build = r.Build == null ? null : r.Build.Copy(), DryRun = r.DryRun, PreviewToken = r.PreviewToken, Priority = r.Priority,
            Cells = r.Cells == null ? null : (WorkCell[])r.Cells.Clone(), CancelTargetRequestId = r.CancelTargetRequestId, CancelTargetRevision = r.CancelTargetRevision }; }
        public void Dispose() { disposed = true; session.ApplicationMessageReceived -= Receive; outgoing.Clear(); pending.Clear(); autoConfirm.Clear(); receivedGrants.Clear(); receivedPlans.Clear(); assemblies.Clear(); authorityErrors.Clear(); authorityStatuses.Clear(); ownerGenerations.Clear(); ownerScopes.Clear(); invalidatedScopes.Clear(); drafts.Clear(); requestDrafts.Clear(); submissions.Clear(); }
    }
}
