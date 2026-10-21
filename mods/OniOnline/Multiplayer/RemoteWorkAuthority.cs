using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace OniOnline.Multiplayer
{
    // Owned by the planet's actual simulation process, never implicitly by the room host.
    // In-memory replay protection survives transport reconnects, not process/save rollback.
    internal sealed class RemoteWorkAuthority
    {
        private string ownerId;
        private readonly IWorldWorkContext context;
        private readonly IWorkExecutor executor;
        private readonly IRemoteWorkPersistence persistence;
        private readonly Dictionary<string, RemoteWorkGrant> grants = new Dictionary<string, RemoteWorkGrant>();
        private readonly Dictionary<string, LedgerEntry> ledger = new Dictionary<string, LedgerEntry>();
        private readonly Dictionary<string, Preview> previews = new Dictionary<string, Preview>();
        private readonly Dictionary<string, WorkTaskIntent> plans = new Dictionary<string, WorkTaskIntent>();
        private WorkPlanet current;
        private long grantRevision;
        private readonly Func<long> nextGeneration;
        public long Generation { get; private set; }
        public long ConfirmedRevision { get; private set; }
        public long StateRevision { get; private set; }
        internal WorkPlanet Planet
        {
            get { return current == null ? null : new WorkPlanet { PlanetId = current.PlanetId, AuthorityEpoch = current.AuthorityEpoch,
                NativeWorldId = current.NativeWorldId, OwnerPeerId = current.OwnerPeerId, AuthorityPeerId = current.AuthorityPeerId, PersistentOwnerId = current.PersistentOwnerId }; }
        }
        public string LastError { get; private set; }
        public bool IsBlocked { get; private set; }
        public string BlockReason { get; private set; }
        public const int MaxLedger = 4096;
        public const int MaxPlans = 1024;
        private sealed class LedgerEntry { internal string Hash; internal RemoteWorkReceipt Receipt; }
        private sealed class Preview { internal string Hash; internal double Expires; }

        internal RemoteWorkAuthority(string ownerId, IWorldWorkContext context, IWorkExecutor executor, Func<long> nextGeneration = null)
        { this.nextGeneration = nextGeneration; this.ownerId = ownerId; this.context = context; this.executor = executor; persistence = executor as IRemoteWorkPersistence; if (persistence != null) persistence.Attach(this); }
        internal void BindOwner(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!string.IsNullOrEmpty(ownerId) && ownerId != value) throw new InvalidOperationException("work_identity_changed");
            ownerId = value;
        }

        internal bool Synchronize()
        {
            if (string.IsNullOrEmpty(ownerId)) return false;
            WorkPlanet planet;
            if (!context.TryGetLocalPlanet(out planet) || planet == null || string.IsNullOrEmpty(planet.PlanetId) ||
                string.IsNullOrEmpty(planet.AuthorityEpoch) ||
                (!string.IsNullOrEmpty(planet.OwnerPeerId) && planet.OwnerPeerId != ownerId) ||
                (!string.IsNullOrEmpty(planet.AuthorityPeerId) && planet.AuthorityPeerId != ownerId)) return false;
            if (current == null || current.PlanetId != planet.PlanetId || current.AuthorityEpoch != planet.AuthorityEpoch ||
                current.NativeWorldId != planet.NativeWorldId)
            {
                Generation = nextGeneration == null ? checked(Generation + 1) : nextGeneration();
                grants.Clear(); ledger.Clear(); previews.Clear(); plans.Clear();
                ConfirmedRevision = 0; grantRevision = 0; StateRevision = 1;
                IsBlocked = false; BlockReason = LastError = null;
                current = new WorkPlanet { PlanetId = planet.PlanetId, AuthorityEpoch = planet.AuthorityEpoch, NativeWorldId = planet.NativeWorldId,
                    OwnerPeerId = ownerId, AuthorityPeerId = ownerId, PersistentOwnerId = planet.PersistentOwnerId };
                if (persistence != null)
                {
                    RemoteWorkSavedPlanet saved = persistence.Restore(current, ownerId);
                    if (saved != null)
                    {
                        foreach (var plan in RemoteWorkPersistence.RestorePlans(saved, ownerId)) plans[plan.ActorId + ":" + plan.Key] = plan;
                        ConfirmedRevision = plans.Count == 0 ? 0 : plans.Values.Max(p => p.ConfirmedRevision);
                    }
                }
            }
            return true;
        }

        internal RemoteWorkGrant SetPermissions(string actorId, RemoteWorkPermission permissions)
        {
            if (!Synchronize()) throw new InvalidOperationException("work_world_unavailable");
            if (string.IsNullOrEmpty(actorId) || actorId == ownerId || ((byte)permissions & ~15) != 0)
                throw new ArgumentException("work_actor");
            RemoteWorkGrant existing;
            if (grants.TryGetValue(actorId, out existing) && existing.Permissions == permissions) return CopyGrant(existing);
            WorkTaskIntent[] before = Merge(plans, grants);
            var grant = new RemoteWorkGrant { ActorId = actorId, PlanetId = current.PlanetId, AuthorityEpoch = current.AuthorityEpoch,
                Generation = Generation, Token = Guid.NewGuid().ToString("N"), Revision = ++grantRevision, Permissions = permissions };
            var candidateGrants = new Dictionary<string, RemoteWorkGrant>(grants) { [actorId] = grant };
            var candidatePlans = new Dictionary<string, WorkTaskIntent>(plans);
            // Old queued confirmations must never be resurrected by a later re-grant.
            if ((permissions & RemoteWorkPermission.IssueTasks) == 0)
                foreach (string key in candidatePlans.Where(p => p.Value.ActorId == actorId).Select(p => p.Key).ToArray()) candidatePlans.Remove(key);
            WorkTaskIntent[] desired = Merge(candidatePlans, candidateGrants);
            // Access revocation takes effect even when physical reconciliation fails. In that case
            // the whole execution view becomes explicitly uncertain, never a false canonical merge.
            Commit(grants, candidateGrants); Commit(plans, candidatePlans); StateRevision++;
            if (!IsBlocked && !SameMerge(before, desired))
            {
                try
                {
                    WorkExecution result = executor.Reconcile(current, before, desired);
                    if (!result.Accepted) Block("work_execution_uncertain");
                    else LastError = null;
                }
                catch (Exception) { Block("work_execution_uncertain"); }
            }
            Publish();
            return CopyGrant(grant);
        }

        internal RemoteWorkGrant GetGrant(string actorId)
        { RemoteWorkGrant value; return grants.TryGetValue(actorId, out value) ? CopyGrant(value) : null; }
        internal bool HasPermission(string actorId, RemoteWorkPermission value)
        { RemoteWorkGrant g; return actorId == ownerId || (grants.TryGetValue(actorId, out g) && (g.Permissions & value) == value); }
        internal WorkTaskIntent[] GetOwnPlans(string actorId)
        { return plans.Values.Where(p => p.ActorId == actorId).Select(p => p.Copy()).ToArray(); }
        internal WorkTaskIntent[] GetPlansByActor(string actorId) { return GetOwnPlans(actorId); }
        internal WorkTaskIntent[] GetMergedPlans()
        {
            return IsBlocked ? Array.Empty<WorkTaskIntent>() : Merge(plans, grants);
        }
        private WorkTaskIntent[] Merge(Dictionary<string, WorkTaskIntent> candidatePlans, Dictionary<string, RemoteWorkGrant> candidateGrants)
        {
            return WorkPlanGeometry.Merge(candidatePlans.Values.Where(p => p.Historical ? p.HistoricalActive : p.ActorId == ownerId || (candidateGrants.ContainsKey(p.ActorId) &&
                    (candidateGrants[p.ActorId].Permissions & RemoteWorkPermission.IssueTasks) != 0))
                .ToArray());
        }

        internal RemoteWorkReceipt Handle(string actorId, RemoteWorkRequest request, double now)
        {
            if (request == null) return Reject("", false, "work_request");
            if (!Synchronize()) return Reject(request.RequestId, request.DryRun, "work_world_unavailable");
            Guid id;
            if (!Guid.TryParseExact(request.RequestId, "N", out id)) return Reject(request.RequestId, request.DryRun, "work_request_id");
            if (request.PlanetId != current.PlanetId || request.AuthorityEpoch != current.AuthorityEpoch)
                return Reject(request.RequestId, request.DryRun, "work_stale_world");
            RemoteWorkGrant grant = GetGrant(actorId);
            if (actorId != ownerId && (grant == null || !HasPermission(actorId, RemoteWorkPermission.IssueTasks) || grant.Token != request.GrantToken))
                return Reject(request.RequestId, request.DryRun, "work_not_authorized");
            if (!WorkPlanGeometry.ValidShape(request.Kind, request.Layer, request.Build))
                return Reject(request.RequestId, request.DryRun, "work_unsupported_kind");
            if (request.Cells == null || request.Cells.Length < 1 || request.Cells.Length > RemoteWorkWire.MaxCells || request.Priority < 1 || request.Priority > 9 ||
                (request.Kind == RemoteWorkKind.Build && request.Cells.Length != 1) || request.Cells.Any(c => c.X < 0 || c.Y < 0 || c.X > 8192 || c.Y > 8192) || request.Cells.Select(c => c.X + ":" + c.Y).Distinct().Count() != request.Cells.Length)
                return Reject(request.RequestId, request.DryRun, "work_invalid_cells");
            if (request.Kind == RemoteWorkKind.Build && WorkPlanGeometry.Footprint(new WorkTaskIntent { Kind = request.Kind, Cell = request.Cells[0], Build = request.Build }).Any(c => c.X > 8192 || c.Y > 8192))
                return Reject(request.RequestId, request.DryRun, "work_invalid_cells");
            string hash;
            try { hash = Fingerprint(actorId, request, false); }
            catch (InvalidDataException) { return Reject(request.RequestId, request.DryRun, "work_request"); }
            string ledgerKey = actorId + ":" + request.RequestId;
            LedgerEntry previous;
            if (ledger.TryGetValue(ledgerKey, out previous))
                return previous.Hash == hash ? CopyReceipt(previous.Receipt) : Reject(request.RequestId, request.DryRun, "work_request_id_conflict");
            var capabilities = executor as IWorkCapabilities;
            if (request.Kind == RemoteWorkKind.Build && capabilities != null && !capabilities.SupportsBuildingExecution)
                return Reject(request.RequestId, request.DryRun, "work_native_build_validation_required");
            if (IsBlocked) return Reject(request.RequestId, request.DryRun, BlockReason);
            if (ledger.Count >= MaxLedger) return Reject(request.RequestId, request.DryRun, "work_replay_capacity");
            foreach (string key in previews.Where(p => p.Value.Expires < now).Select(p => p.Key).ToArray()) previews.Remove(key);
            WorkExecution check;
            try { check = executor.Preflight(current, request); }
            catch (Exception) { return Remember(ledgerKey, hash, Reject(request.RequestId, request.DryRun, "work_preflight_failed")); }
            if (check == null) return Remember(ledgerKey, hash, Reject(request.RequestId, request.DryRun, "work_preflight_failed"));
            if (!check.Accepted) return Remember(ledgerKey, hash, Reject(request.RequestId, request.DryRun, check.Reason));
            string content = Fingerprint(actorId, request, true);
            if (request.DryRun)
            {
                if (previews.Count >= 128) return Remember(ledgerKey, hash, Reject(request.RequestId, true, "work_preview_capacity"));
                string token = Guid.NewGuid().ToString("N");
                previews[token] = new Preview { Hash = content, Expires = now + 30 };
                return Remember(ledgerKey, hash, new RemoteWorkReceipt { RequestId = request.RequestId, Accepted = true,
                    DryRun = true, Count = check.Count, Reason = "work_preview", PreviewToken = token });
            }
            Preview preview;
            if (request.PreviewToken == null || !previews.TryGetValue(request.PreviewToken, out preview) || preview.Expires < now || preview.Hash != content)
                return Remember(ledgerKey, hash, Reject(request.RequestId, false, "work_preview_required"));
            int added = request.Cells.Count(cell => !plans.ContainsKey(actorId + ":" + ((byte)request.Layer) + ":" + cell.X + ":" + cell.Y));
            if (plans.Count + added > MaxPlans) return Remember(ledgerKey, hash, Reject(request.RequestId, false, "work_plan_capacity"));
            previews.Remove(request.PreviewToken);
            WorkTaskIntent[] before = GetMergedPlans();
            long revision = ConfirmedRevision + 1;
            var candidatePlans = new Dictionary<string, WorkTaskIntent>(plans);
            foreach (var cell in request.Cells)
            {
                var plan = new WorkTaskIntent { ActorId = actorId, PlanetId = request.PlanetId, RequestId = request.RequestId,
                    Cell = cell, Kind = request.Kind, Layer = request.Layer, Build = request.Build == null ? null : request.Build.Copy(), Priority = request.Priority, ConfirmedRevision = revision };
                candidatePlans[actorId + ":" + plan.Key] = plan;
            }
            // Record before native calls. An uncertain native result cannot replay physical mutations.
            var pending = new RemoteWorkReceipt { RequestId = request.RequestId, Reason = "work_execution_uncertain" };
            Remember(ledgerKey, hash, pending);
            try
            {
                var desired = Merge(candidatePlans, grants);
                var execution = SameMerge(before, desired) ? WorkExecution.Accept(0) : executor.Reconcile(current, before, desired);
                if (!execution.Accepted)
                {
                    if (!execution.NoMutationGuaranteed) Block("work_execution_uncertain");
                    LastError = IsBlocked ? BlockReason : execution.Reason;
                    return Remember(ledgerKey, hash, Reject(request.RequestId, false, LastError));
                }
                Commit(plans, candidatePlans); ConfirmedRevision = revision; StateRevision++;
                Publish();
                var receipt = new RemoteWorkReceipt { RequestId = request.RequestId, Accepted = true, Count = execution.Count, Reason = execution.Reason };
                LastError = null;
                return Remember(ledgerKey, hash, receipt);
            }
            catch (Exception)
            { Block("work_execution_uncertain"); return CopyReceipt(pending); }
        }

        private void Block(string reason)
        { IsBlocked = true; BlockReason = LastError = reason; previews.Clear(); StateRevision++; Publish(); }
        private void Publish() { if (persistence != null && current != null) persistence.Publish(current, ownerId); }
        internal WorkTaskIntent[] ExportConfirmedPlans() { return plans.Values.Select(p => p.Copy()).ToArray(); }
        internal void ForgetNativeCell(string key)
        {
            WorkTaskIntent removed = plans.Values.FirstOrDefault(p => p.Key == key);
            if (removed != null) { ForgetNativePlan(removed); return; }
            StateRevision++; Publish();
        }
        internal void ForgetNativePlan(WorkTaskIntent removed)
        {
            foreach (string id in plans.Where(p => WorkPlanGeometry.Overlaps(p.Value, removed)).Select(p => p.Key).ToArray()) plans.Remove(id);
            StateRevision++; Publish();
        }
        private static void Commit<T>(Dictionary<string, T> destination, Dictionary<string, T> candidate)
        { destination.Clear(); foreach (var item in candidate) destination.Add(item.Key, item.Value); }
        private static bool SameMerge(WorkTaskIntent[] before, WorkTaskIntent[] after)
        { return before.Length == after.Length && before.All(p => after.Any(q => q.Key == p.Key && q.RequestId == p.RequestId && q.ActorId == p.ActorId && q.Priority == p.Priority && WorkPlanGeometry.SameNative(p, q))); }

        private static string Fingerprint(string actorId, RemoteWorkRequest request, bool contentOnly)
        {
            var copy = new RemoteWorkRequest { RequestId = contentOnly ? "" : request.RequestId, PlanetId = request.PlanetId,
                AuthorityEpoch = request.AuthorityEpoch, GrantToken = request.GrantToken ?? "", Kind = request.Kind, Layer = request.Layer,
                DryRun = contentOnly ? false : request.DryRun, PreviewToken = contentOnly ? "" : request.PreviewToken,
                Priority = request.Priority, Cells = request.Cells, Build = request.Build == null ? null : request.Build.Copy() };
            byte[] bytes = RemoteWorkWire.Encode(new RemoteWorkMessage { Kind = RemoteWorkMessageKind.Request, TargetId = actorId, Request = copy });
            using (var sha = new SHA256Managed()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }
        private RemoteWorkReceipt Remember(string key, string hash, RemoteWorkReceipt result)
        { ledger[key] = new LedgerEntry { Hash = hash, Receipt = CopyReceipt(result) }; return CopyReceipt(result); }
        private static RemoteWorkReceipt Reject(string id, bool dryRun, string reason)
        { return new RemoteWorkReceipt { RequestId = id, DryRun = dryRun, Reason = reason }; }
        internal static RemoteWorkReceipt CopyReceipt(RemoteWorkReceipt r)
        { return new RemoteWorkReceipt { RequestId = r.RequestId, Accepted = r.Accepted, DryRun = r.DryRun, Reason = r.Reason, Count = r.Count, PreviewToken = r.PreviewToken }; }
        internal static RemoteWorkGrant CopyGrant(RemoteWorkGrant g)
        { return new RemoteWorkGrant { ActorId = g.ActorId, PlanetId = g.PlanetId, AuthorityEpoch = g.AuthorityEpoch, Token = g.Token, Generation = g.Generation, Revision = g.Revision, Permissions = g.Permissions }; }
    }
}
