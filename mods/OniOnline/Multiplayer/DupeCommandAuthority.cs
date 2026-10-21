using System;
using System.Collections.Generic;

namespace OniOnline.Multiplayer
{
    internal sealed class DupeCommandAuthority
    {
        internal const int MaxLedger = 4096;
        private readonly string localPeer;
        private readonly IWorldWorkContext context;
        private readonly IDupeCommandExecutor executor;
        private readonly Func<string, int, bool> canControl;
        private readonly Dictionary<string, Entry> ledger = new Dictionary<string, Entry>();
        private string epoch;
        private string planetId;
        private sealed class Entry { internal DupeMoveRequest Request; internal DupeCommandReceipt Receipt; }

        internal DupeCommandAuthority(string localPeer, IWorldWorkContext context, IDupeCommandExecutor executor, Func<string, int, bool> canControl)
        {
            this.localPeer = localPeer ?? throw new ArgumentNullException("localPeer");
            this.context = context ?? throw new ArgumentNullException("context");
            this.executor = executor ?? throw new ArgumentNullException("executor");
            this.canControl = canControl ?? throw new ArgumentNullException("canControl");
        }

        internal DupeCommandReceipt Handle(string authenticatedActor, DupeMoveRequest request)
        {
            if (!Valid(request)) return Reject(request, "dupe_invalid_request");
            WorkPlanet planet;
            if (!context.TryGetLocalPlanet(out planet) || planet == null) return Reject(request, "dupe_world_unavailable");
            // An empty owner/authority is not an implicit local grant. Hosted foreign worlds
            // need a separate handover protocol before this executor can serve them.
            if (planet.OwnerPeerId != localPeer || planet.AuthorityPeerId != localPeer || planet.PlanetId != request.PlanetId)
                return Reject(request, "dupe_not_local_authority");
            if (string.IsNullOrEmpty(planet.AuthorityEpoch) || request.AuthorityEpoch != planet.AuthorityEpoch)
                return Reject(request, "dupe_stale_world");
            if (epoch != planet.AuthorityEpoch || planetId != planet.PlanetId)
            { ledger.Clear(); epoch = planet.AuthorityEpoch; planetId = planet.PlanetId; }
            string key = authenticatedActor + ":" + request.RequestId;
            Entry old;
            if (ledger.TryGetValue(key, out old))
                return old.Request.SameAs(request) ? old.Receipt.Copy() : Reject(request, "dupe_request_id_conflict");
            // Do not evict old successes and make retries executable again.
            if (ledger.Count >= MaxLedger) return Reject(request, "dupe_ledger_full");
            var entry = new Entry { Request = request.Copy(), Receipt = Reject(request, "dupe_execution_unknown") };
            entry.Receipt.OutcomeUnknown = true;
            ledger.Add(key, entry);
            // A retry of a rejected intent must not become executable after a later
            // permission grant. The user must issue a new command with a new ID.
            if (!canControl(authenticatedActor, request.DupeId))
            { entry.Receipt = Reject(request, "dupe_not_authorized"); return entry.Receipt.Copy(); }
            try
            {
                WorkExecution preview = executor.Preflight(planet, request);
                if (preview == null || !preview.Accepted)
                    entry.Receipt = Reject(request, preview == null ? "dupe_preflight_failed" : preview.Reason);
                else if (!canControl(authenticatedActor, request.DupeId))
                    entry.Receipt = Reject(request, "dupe_not_authorized");
                else
                {
                    WorkPlanet current;
                    if (!context.TryGetLocalPlanet(out current) || current == null || current.PlanetId != request.PlanetId ||
                        current.AuthorityEpoch != request.AuthorityEpoch || current.OwnerPeerId != localPeer || current.AuthorityPeerId != localPeer)
                        entry.Receipt = Reject(request, "dupe_stale_world");
                    else
                    {
                        WorkExecution executed = executor.Execute(current, request);
                        if (executed != null)
                            entry.Receipt = new DupeCommandReceipt { RequestId = request.RequestId, Accepted = executed.Accepted, Reason = executed.Reason };
                    }
                }
            }
            catch (Exception)
            {
                // Native code may throw after accepting a chore. Retain the ID without
                // retrying execution, and never report an unobserved success.
            }
            return entry.Receipt.Copy();
        }

        internal static bool Valid(DupeMoveRequest request)
        {
            Guid parsed;
            return request != null && request.RequestId != null && request.RequestId.Length == 32 && Guid.TryParseExact(request.RequestId, "N", out parsed) &&
                request.PlanetId != null && request.PlanetId.Length == 32 && Guid.TryParseExact(request.PlanetId, "N", out parsed) &&
                request.AuthorityEpoch != null && request.AuthorityEpoch.Length == 32 && Guid.TryParseExact(request.AuthorityEpoch, "N", out parsed) &&
                request.DupeId >= 0 && request.Target.X >= 0 && request.Target.Y >= 0 && request.Target.X <= 32767 && request.Target.Y <= 32767;
        }
        internal static DupeCommandReceipt Reject(DupeMoveRequest request, string reason)
        { return new DupeCommandReceipt { RequestId = request == null ? "" : request.RequestId, Reason = reason }; }
    }
}
