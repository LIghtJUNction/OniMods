using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Multiplayer
{
    internal sealed class DupeCommandAuthority
    {
        internal const int MaxLedger = 4096;
        private readonly string localPeer;
        private readonly IWorldWorkContext context;
        private readonly IDupeCommandExecutor executor;
        private readonly IDupeCommandLifecycle lifecycle;
        private readonly Func<string, int, bool> canControl;
        private readonly Dictionary<string, Entry> ledger = new Dictionary<string, Entry>();
        private readonly HashSet<Entry> active = new HashSet<Entry>();
        private string epoch;
        private string planetId;
        internal event Action<string, DupeCommandReceipt> Changed;
        private sealed class Entry
        {
            internal string Actor;
            internal DupeMoveRequest Request;
            internal WorkPlanet Planet;
            internal DupeCommandReceipt Receipt;
            internal DupeCommandState StopState;
            internal readonly List<Entry> Cancellations = new List<Entry>();
        }

        internal DupeCommandAuthority(string localPeer, IWorldWorkContext context, IDupeCommandExecutor executor, Func<string, int, bool> canControl)
        {
            this.localPeer = localPeer ?? throw new ArgumentNullException("localPeer");
            this.context = context ?? throw new ArgumentNullException("context");
            this.executor = executor ?? throw new ArgumentNullException("executor");
            lifecycle = executor as IDupeCommandLifecycle;
            this.canControl = canControl ?? throw new ArgumentNullException("canControl");
        }

        internal DupeCommandReceipt Handle(string authenticatedActor, DupeMoveRequest request)
        {
            if (!Valid(request)) return Reject(request, "dupe_invalid_request");
            if (string.IsNullOrEmpty(authenticatedActor)) return Reject(request, "dupe_not_authorized");
            Reconcile();
            WorkPlanet planet;
            if (!context.TryGetLocalPlanet(out planet) || planet == null) return Reject(request, "dupe_world_unavailable");
            // Foreign-world hosting still requires native handover before this bridge
            // can serve it. Room hosting alone is never duplicant control authority.
            if (planet.OwnerPeerId != localPeer || planet.AuthorityPeerId != localPeer || planet.PlanetId != request.PlanetId)
                return Reject(request, "dupe_not_local_authority");
            if (string.IsNullOrEmpty(planet.AuthorityEpoch) || request.AuthorityEpoch != planet.AuthorityEpoch)
                return Reject(request, "dupe_stale_world");
            if (epoch != planet.AuthorityEpoch || planetId != planet.PlanetId)
            {
                // Do not forget an uncertain native cleanup when changing scopes.
                if (active.Count != 0) return Reject(request, "dupe_cleanup_pending");
                ledger.Clear(); epoch = planet.AuthorityEpoch; planetId = planet.PlanetId;
            }
            planet = Copy(planet);
            string key = authenticatedActor + ":" + request.RequestId;
            Entry old;
            if (ledger.TryGetValue(key, out old))
                return old.Request.SameAs(request) ? old.Receipt.Copy() : Reject(request, "dupe_request_id_conflict");
            // Retain every result, including denial and uncertainty, for this epoch.
            if (ledger.Count >= MaxLedger) return Reject(request, "dupe_ledger_full");
            var entry = new Entry { Actor = authenticatedActor, Request = request.Copy(), Receipt = Unknown(request) };
            entry.Request.NativeLeaseId = Guid.NewGuid().ToString("N");
            ledger.Add(key, entry);
            if (request.IsCancellation) return Cancel(entry);
            if (!Allowed(authenticatedActor, request.DupeId))
            { entry.Receipt = Reject(request, "dupe_not_authorized"); return entry.Receipt.Copy(); }
            try
            {
                WorkExecution preview = executor.Preflight(planet, entry.Request);
                if (preview == null || !preview.Accepted)
                    entry.Receipt = Reject(request, preview == null ? "dupe_preflight_failed" : preview.Reason);
                else if (!Allowed(authenticatedActor, request.DupeId))
                    entry.Receipt = Reject(request, "dupe_not_authorized");
                else
                {
                    WorkPlanet current;
                    if (!context.TryGetLocalPlanet(out current) || !SameAuthority(planet, current))
                        entry.Receipt = Reject(request, "dupe_stale_world");
                    else
                    {
                        entry.Planet = Copy(current);
                        // An exception can occur after MoveToLocation creates its chore.
                        // Reserve cleanup ownership before entering native execution.
                        if (lifecycle != null) active.Add(entry);
                        WorkExecution executed = executor.Execute(current, entry.Request);
                        if (executed != null && executed.Accepted)
                        {
                            entry.Receipt = new DupeCommandReceipt { RequestId = request.RequestId, Accepted = true, Reason = executed.Reason };
                            if (lifecycle != null) Refresh(entry);
                        }
                        else if (executed != null && (lifecycle == null || executed.NoMutationGuaranteed))
                        { active.Remove(entry); entry.Receipt = Reject(request, executed.Reason); }
                    }
                }
            }
            catch (Exception)
            {
                // Keep both the replay reservation and any captured native handle.
                // The next lifecycle pump can observe/clean it without re-execution.
            }
            return entry.Receipt.Copy();
        }

        internal DupeCommandReceipt Query(string authenticatedActor, DupeMoveRequest request)
        {
            if (!Valid(request) || string.IsNullOrEmpty(authenticatedActor)) return Reject(request, "dupe_invalid_request");
            Reconcile();
            Entry entry;
            if (ledger.TryGetValue(authenticatedActor + ":" + request.RequestId, out entry))
                return entry.Request.SameAs(request) ? entry.Receipt.Copy() : Reject(request, "dupe_request_id_conflict");
            WorkPlanet current;
            if (!context.TryGetLocalPlanet(out current) || current == null || current.PlanetId != request.PlanetId || current.AuthorityEpoch != request.AuthorityEpoch)
                return new DupeCommandReceipt { RequestId = request.RequestId, State = DupeCommandState.WorldChanged, Revision = 3, Reason = "dupe_stale_world" };
            // A query cannot create an intent, even if its original packet never arrived.
            return new DupeCommandReceipt { RequestId = request.RequestId, OutcomeUnknown = true, Reason = "dupe_response_unconfirmed" };
        }

        private DupeCommandReceipt Cancel(Entry cancellation)
        {
            DupeMoveRequest request = cancellation.Request;
            Entry target;
            if (!ledger.TryGetValue(cancellation.Actor + ":" + request.CancelTargetRequestId, out target) || target.Request.IsCancellation)
                cancellation.Receipt = Reject(request, "dupe_request_unknown");
            else if (target.Request.PlanetId != request.PlanetId || target.Request.AuthorityEpoch != request.AuthorityEpoch ||
                target.Request.DupeId != request.DupeId || target.Request.Target.X != request.Target.X || target.Request.Target.Y != request.Target.Y)
                cancellation.Receipt = Reject(request, "dupe_cancel_target_mismatch");
            else if (target.Receipt.State >= DupeCommandState.Completed)
                cancellation.Receipt = CancelledReceipt(cancellation, target.Receipt.State, "dupe_already_finished");
            else if (!Allowed(cancellation.Actor, request.DupeId))
                cancellation.Receipt = Reject(request, "dupe_not_authorized");
            else if (lifecycle == null || !active.Contains(target))
                cancellation.Receipt = Reject(request, "dupe_cancel_not_supported");
            else
            {
                target.Cancellations.Add(cancellation);
                if (target.StopState == DupeCommandState.None) target.StopState = DupeCommandState.Cancelled;
                Stop(target);
            }
            return cancellation.Receipt.Copy();
        }

        // Called even while the session is leaving/reconnecting. Permission checks at
        // issue time alone do not withdraw a chore that was already accepted by ONI.
        internal void Reconcile(bool sessionReady = true, Func<string, bool> connected = null)
        {
            foreach (Entry entry in active.ToArray())
            {
                if (Refresh(entry)) continue;
                WorkPlanet current;
                if (!context.TryGetLocalPlanet(out current) || !SameAuthority(entry.Planet, current))
                    entry.StopState = DupeCommandState.WorldChanged;
                else if (!sessionReady || connected != null && !connected(entry.Actor))
                    entry.StopState = DupeCommandState.Disconnected;
                else if (!Allowed(entry.Actor, entry.Request.DupeId))
                    entry.StopState = DupeCommandState.Revoked;
                if (entry.StopState != DupeCommandState.None) Stop(entry);
            }
        }

        internal void StopAll()
        {
            foreach (Entry entry in active.ToArray())
            {
                if (Refresh(entry)) continue;
                entry.StopState = DupeCommandState.Disconnected;
                Stop(entry);
            }
        }

        private bool Refresh(Entry entry)
        {
            DupeCommandState state;
            try { state = lifecycle.GetState(entry.Planet, entry.Request); }
            catch (Exception) { return false; }
            if (state >= DupeCommandState.Completed)
            {
                Finish(entry, state == DupeCommandState.Cancelled && entry.StopState != DupeCommandState.None ? entry.StopState : state,
                    state != DupeCommandState.WorldChanged);
                return true;
            }
            if (state == DupeCommandState.Active && entry.Receipt.State != DupeCommandState.Active)
            {
                entry.Receipt = new DupeCommandReceipt { RequestId = entry.Request.RequestId, Accepted = true,
                    State = DupeCommandState.Active, Revision = 2, Reason = "ok" };
                Notify(entry);
            }
            return false;
        }

        private void Stop(Entry entry)
        {
            if (Refresh(entry)) return;
            try
            {
                WorkExecution result = lifecycle.Cancel(entry.Planet, entry.Request);
                if (result != null && result.Accepted) Finish(entry, entry.StopState, result.Count > 0);
            }
            catch (Exception) { /* Keep the exact handle for the next cleanup pump. */ }
        }

        private void Finish(Entry entry, DupeCommandState state, bool observedTask)
        {
            active.Remove(entry);
            // Scope destruction ends cleanup responsibility but does not prove that
            // a previously unobserved MoveTo call ever accepted a native chore.
            entry.Receipt = new DupeCommandReceipt { RequestId = entry.Request.RequestId, Accepted = entry.Receipt.Accepted || observedTask,
                State = state, Revision = 3, Reason = Reason(state) };
            Notify(entry);
            foreach (Entry cancellation in entry.Cancellations)
            {
                cancellation.Receipt = CancelledReceipt(cancellation, state,
                    state == DupeCommandState.Cancelled ? "dupe_cancelled" : "dupe_already_finished");
                Notify(cancellation);
            }
            entry.Cancellations.Clear();
        }

        private bool Allowed(string actor, int dupe)
        { try { return canControl(actor, dupe); } catch (Exception) { return false; } }
        private bool SameAuthority(WorkPlanet expected, WorkPlanet current)
        {
            return expected != null && current != null && expected.PlanetId == current.PlanetId &&
                expected.AuthorityEpoch == current.AuthorityEpoch && expected.NativeWorldId == current.NativeWorldId &&
                current.OwnerPeerId == localPeer && current.AuthorityPeerId == localPeer;
        }
        private void Notify(Entry entry)
        { if (Changed != null) Changed(entry.Actor, entry.Receipt.Copy()); }
        private static WorkPlanet Copy(WorkPlanet planet)
        {
            return new WorkPlanet { PlanetId = planet.PlanetId, AuthorityEpoch = planet.AuthorityEpoch, NativeWorldId = planet.NativeWorldId,
                OwnerPeerId = planet.OwnerPeerId, AuthorityPeerId = planet.AuthorityPeerId, PersistentOwnerId = planet.PersistentOwnerId };
        }
        private static DupeCommandReceipt CancelledReceipt(Entry entry, DupeCommandState state, string reason)
        { return new DupeCommandReceipt { RequestId = entry.Request.RequestId, Accepted = true, State = state, Revision = 3, Reason = reason }; }
        private static DupeCommandReceipt Unknown(DupeMoveRequest request)
        { return new DupeCommandReceipt { RequestId = request.RequestId, OutcomeUnknown = true, Reason = "dupe_execution_unknown" }; }
        internal static string Reason(DupeCommandState state)
        {
            switch (state)
            {
                case DupeCommandState.Completed: return "dupe_completed";
                case DupeCommandState.Cancelled: return "dupe_cancelled";
                case DupeCommandState.Replaced: return "dupe_replaced";
                case DupeCommandState.Revoked: return "dupe_permission_revoked";
                case DupeCommandState.Disconnected: return "dupe_connection_lost";
                case DupeCommandState.WorldChanged: return "dupe_stale_world";
                default: return "dupe_execution_unknown";
            }
        }
        internal static bool Valid(DupeMoveRequest request)
        {
            return request != null && Id(request.RequestId) && Id(request.PlanetId) && Id(request.AuthorityEpoch) &&
                request.DupeId >= 0 && request.Target.X >= 0 && request.Target.Y >= 0 && request.Target.X <= 32767 && request.Target.Y <= 32767 &&
                (!request.IsCancellation || Id(request.CancelTargetRequestId) && request.CancelTargetRequestId != request.RequestId);
        }
        internal static bool Id(string value)
        { Guid parsed; return value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out parsed); }
        internal static DupeCommandReceipt Reject(DupeMoveRequest request, string reason)
        { return new DupeCommandReceipt { RequestId = request == null ? "" : request.RequestId, Reason = reason }; }
    }
}
