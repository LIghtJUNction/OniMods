using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OniOnline.Sdk;

namespace OniOnline.Runtime
{
    internal enum ModContentSyncStage
    {
        Queued, CheckingWorkshop, Subscribing, Downloading,
        AwaitingNativeInstall, AwaitingActivation, Verified, ManualActionRequired, Failed
    }

    // These records deliberately contain no installation paths or Steam account data.
    internal sealed class SteamModDetails
    {
        internal ulong ItemId;
        internal uint ConsumerAppId;
        internal bool Success;
        internal bool Banned;
    }

    internal sealed class SteamModOperation
    {
        internal ulong ItemId;
        internal uint AppId;
        internal bool Success;
    }

    internal sealed class SteamModDownloadState
    {
        internal bool Subscribed, Installed, NeedsUpdate, Downloading, Pending;
        internal bool HasInstallInfo;
        internal ulong DownloadedBytes, TotalBytes;
    }

    internal interface ISteamModContentApi : IDisposable
    {
        bool Inspect(ulong itemId, System.Action<SteamModDetails> completed);
        bool Subscribe(ulong itemId, System.Action<SteamModOperation> completed);
        bool Download(ulong itemId);
        bool TryGetState(ulong itemId, out SteamModDownloadState state);
        void CancelPendingRequests();
        event System.Action<SteamModOperation> DownloadCompleted;
    }

    internal sealed class ModContentSyncStatus
    {
        internal string Id { get; }
        internal ulong WorkshopId { get; }
        internal ModContentSyncStage Stage { get; }
        internal string Error { get; }
        internal ulong DownloadedBytes { get; }
        internal ulong TotalBytes { get; }
        internal ModContentSyncStatus(string id, ulong workshopId, ModContentSyncStage stage,
            string error, ulong downloadedBytes, ulong totalBytes)
        {
            Id = id; WorkshopId = workshopId; Stage = stage; Error = error;
            DownloadedBytes = downloadedBytes; TotalBytes = totalBytes;
        }
    }

    // A controller never edits mods.json, enables a mod, or treats a Workshop download as a loaded mod.
    // Drive it on the main thread; the native adapter receives Steam callbacks on that same thread.
    internal sealed class SteamModContentSync : IDisposable
    {
        internal const uint OniAppId = 457140;
        private const int MaximumConcurrentRequests = 4;
        private const double PollSeconds = 1;
        private const double RequestTimeoutSeconds = 60;
        private const double DownloadTimeoutSeconds = 1800;

        private sealed class Job
        {
            internal ModSyncChange Change;
            internal ModContentSyncStage Stage;
            internal string Error = "";
            internal double Started, NextPoll;
            internal ulong Downloaded, Total;
            internal ulong ItemId { get { return Change.Expected == null ? 0 : Change.Expected.WorkshopId; } }
        }

        private readonly ISteamModContentApi api;
        private readonly Func<ModManifest> captureInstalled;
        private readonly Func<ModManifest> currentRequirements;
        private readonly List<Job> jobs = new List<Job>();
        private ModSyncPlan plan;
        private long generation;
        private double clock;
        private bool disposed;

        internal SteamModContentSync(ISteamModContentApi api, Func<ModManifest> captureInstalled, Func<ModManifest> currentRequirements)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.captureInstalled = captureInstalled ?? throw new ArgumentNullException(nameof(captureInstalled));
            this.currentRequirements = currentRequirements ?? throw new ArgumentNullException(nameof(currentRequirements));
            api.DownloadCompleted += OnDownloadCompleted;
        }

        internal bool CanJoin { get; private set; }
        internal string Error { get; private set; } = "";
        internal IReadOnlyList<ModContentSyncStatus> Status
        {
            get
            {
                return new ReadOnlyCollection<ModContentSyncStatus>(jobs.Select(j => new ModContentSyncStatus(
                    j.Change.Id, j.ItemId, j.Stage, j.Error, j.Downloaded, j.Total)).ToList());
            }
        }

        // The fingerprint binds both manifests and the comparison policy. Any change requires confirmation again.
        internal bool Begin(ModSyncPlan confirmedPlan, string confirmedFingerprint, double now)
        {
            ThrowIfDisposed();
            ValidateTime(now);
            Cancel();
            clock = now;
            if (confirmedPlan == null || confirmedPlan.Fingerprint != confirmedFingerprint)
            { Error = "mod_sync_plan_not_confirmed"; return false; }
            if (!TryCapture(out var installed)) return false;
            if (!RequiredStillCurrent(confirmedPlan) || !confirmedPlan.IsCurrent(confirmedPlan.RequiredManifest, installed))
            { Error = "mod_sync_plan_changed"; return false; }
            plan = confirmedPlan;
            foreach (var change in plan.Changes)
            {
                var job = new Job { Change = change, Stage = ModContentSyncStage.Queued };
                if (change.Expected == null)
                { job.Stage = ModContentSyncStage.ManualActionRequired; job.Error = "extra_required_mod"; }
                else if (ContentsMatch(change.Expected, change.Installed))
                    job.Stage = ModContentSyncStage.AwaitingActivation;
                else if (!change.CanDownload)
                { job.Stage = ModContentSyncStage.ManualActionRequired; job.Error = "local_mod_not_distributable"; }
                jobs.Add(job);
            }
            // A single Workshop item cannot satisfy two different stable mod identities.
            foreach (var group in jobs.Where(j => j.ItemId != 0).GroupBy(j => j.ItemId))
                if (group.Count() > 1)
                    foreach (var job in group) Fail(job, "workshop_identity_ambiguous");
            CanJoin = confirmedPlan.CanJoin;
            Tick(now);
            return true;
        }

        internal void Tick(double now)
        {
            ThrowIfDisposed();
            ValidateTime(now);
            if (now < clock) throw new ArgumentOutOfRangeException(nameof(now));
            clock = now;
            if (plan != null && !RequiredStillCurrent(plan))
            {
                generation++;
                CanJoin = false;
                foreach (var job in jobs) Fail(job, "mod_sync_plan_changed");
                try { api.CancelPendingRequests(); } catch { }
                return;
            }
            foreach (var job in jobs)
            {
                if (!Active(job.Stage)) continue;
                double timeout = job.Stage == ModContentSyncStage.Downloading ? DownloadTimeoutSeconds : RequestTimeoutSeconds;
                if (now - job.Started >= timeout) { Fail(job, "steam_request_timeout"); continue; }
                if (job.Stage != ModContentSyncStage.Downloading || now < job.NextPoll) continue;
                job.NextPoll = now + PollSeconds;
                PollDownload(job);
            }
            int active = jobs.Count(j => Active(j.Stage));
            foreach (var job in jobs)
            {
                if (active >= MaximumConcurrentRequests) break;
                if (job.Stage != ModContentSyncStage.Queued) continue;
                active++;
                BeginInspect(job);
            }
        }

        // Call only after the native loader has completed a catalog refresh. Disk changes alone never suffice.
        internal bool RefreshNativeCatalog()
        {
            ThrowIfDisposed();
            if (plan == null || !RequiredStillCurrent(plan) || !TryCapture(out var installed)) { CanJoin = false; return false; }
            var comparison = ModSyncPlanner.Compare(plan.RequiredManifest, installed, plan.ComparisonMode);
            CanJoin = comparison.CanJoin;
            var actual = installed.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
            foreach (var job in jobs)
            {
                actual.TryGetValue(job.Change.Id, out var entry);
                if (job.Change.Expected == null)
                {
                    if (entry == null || !entry.Enabled) { job.Stage = ModContentSyncStage.Verified; job.Error = ""; }
                    else { job.Stage = ModContentSyncStage.ManualActionRequired; job.Error = "extra_required_mod"; }
                    continue;
                }
                if (!ContentsMatch(job.Change.Expected, entry))
                {
                    if (job.Stage == ModContentSyncStage.Verified || job.Stage == ModContentSyncStage.AwaitingActivation)
                    { job.Stage = ModContentSyncStage.ManualActionRequired; job.Error = "native_mod_content_changed"; }
                    continue;
                }
                job.Stage = entry.Enabled ? ModContentSyncStage.Verified : ModContentSyncStage.AwaitingActivation;
                job.Error = "";
            }
            return CanJoin;
        }

        internal void Cancel()
        {
            generation++;
            jobs.Clear(); plan = null; CanJoin = false; Error = "";
            try { api.CancelPendingRequests(); } catch { }
        }

        private void BeginInspect(Job job)
        {
            SetStage(job, ModContentSyncStage.CheckingWorkshop);
            long requestGeneration = generation;
            try
            {
                bool accepted = api.Inspect(job.ItemId, details =>
                {
                    if (!Current(job, requestGeneration, ModContentSyncStage.CheckingWorkshop)) return;
                    if (details == null || !details.Success) { Fail(job, "steam_details_failed"); return; }
                    if (details.ItemId != job.ItemId || details.ConsumerAppId != OniAppId || details.Banned)
                    { Fail(job, "steam_item_identity_mismatch"); return; }
                    BeginSubscribe(job, requestGeneration);
                });
                if (!accepted && Current(job, requestGeneration, ModContentSyncStage.CheckingWorkshop)) Fail(job, "steam_details_rejected");
            }
            catch { Fail(job, "steam_sdk_failed"); }
        }

        private void BeginSubscribe(Job job, long requestGeneration)
        {
            if (!PlanStillCurrent(job)) return;
            if (!SafeState(job, out var state)) return;
            if (state.Subscribed) { BeginDownload(job); return; }
            SetStage(job, ModContentSyncStage.Subscribing);
            try
            {
                bool accepted = api.Subscribe(job.ItemId, result =>
                {
                    if (!Current(job, requestGeneration, ModContentSyncStage.Subscribing)) return;
                    if (result == null || !result.Success) { Fail(job, "steam_subscribe_failed"); return; }
                    if (result.ItemId != job.ItemId) { Fail(job, "steam_item_identity_mismatch"); return; }
                    BeginDownload(job);
                });
                if (!accepted && Current(job, requestGeneration, ModContentSyncStage.Subscribing)) Fail(job, "steam_subscribe_rejected");
            }
            catch { Fail(job, "steam_sdk_failed"); }
        }

        private void BeginDownload(Job job)
        {
            if (!PlanStillCurrent(job)) return;
            if (!SafeState(job, out var state)) return;
            if (DownloadInstalled(state)) { SetStage(job, ModContentSyncStage.AwaitingNativeInstall); return; }
            SetStage(job, ModContentSyncStage.Downloading);
            try { if (!api.Download(job.ItemId)) Fail(job, "steam_download_rejected"); }
            catch { Fail(job, "steam_sdk_failed"); }
        }

        private void PollDownload(Job job)
        {
            if (!SafeState(job, out var state)) return;
            job.Downloaded = state.DownloadedBytes; job.Total = state.TotalBytes;
            if (DownloadInstalled(state)) SetStage(job, ModContentSyncStage.AwaitingNativeInstall);
            else if (state.Downloading || state.Pending) job.Error = "";
        }

        private void OnDownloadCompleted(SteamModOperation result)
        {
            if (disposed || result == null) return;
            foreach (var job in jobs)
            {
                if (job.ItemId != result.ItemId || job.Stage != ModContentSyncStage.Downloading) continue;
                if (result.AppId != OniAppId) continue;
                // Steam download callbacks have no request generation. A cancelled older request can arrive here.
                // Consult current state before trusting either its success or failure.
                if (!SafeState(job, out var state)) continue;
                job.Downloaded = state.DownloadedBytes; job.Total = state.TotalBytes;
                if (DownloadInstalled(state)) SetStage(job, ModContentSyncStage.AwaitingNativeInstall);
                else if (!result.Success && !state.Downloading && !state.Pending) job.Error = "steam_download_callback_failed";
            }
        }

        private bool PlanStillCurrent(Job job)
        {
            if (plan != null && RequiredStillCurrent(plan) && TryCapture(out var installed) && plan.IsCurrent(plan.RequiredManifest, installed)) return true;
            CanJoin = false;
            Error = "mod_sync_plan_changed";
            Fail(job, Error);
            return false;
        }

        private bool RequiredStillCurrent(ModSyncPlan candidate)
        {
            try
            {
                var current = currentRequirements();
                if (current != null && current.Fingerprint == candidate.RequiredFingerprint) return true;
            }
            catch { }
            Error = "mod_sync_plan_changed";
            return false;
        }

        private bool SafeState(Job job, out SteamModDownloadState state)
        {
            state = null;
            try { if (api.TryGetState(job.ItemId, out state) && state != null) return true; }
            catch { }
            Fail(job, "steam_state_failed"); return false;
        }

        private bool TryCapture(out ModManifest installed)
        {
            installed = null;
            try { installed = captureInstalled(); }
            catch { Error = "native_catalog_unavailable"; return false; }
            if (installed != null) return true;
            Error = "native_catalog_unavailable"; return false;
        }

        private bool Current(Job job, long requestGeneration, ModContentSyncStage stage)
        { return !disposed && generation == requestGeneration && job.Stage == stage; }
        private void SetStage(Job job, ModContentSyncStage stage)
        { job.Stage = stage; job.Started = clock; job.NextPoll = clock; job.Error = ""; }
        private static void Fail(Job job, string error)
        { job.Stage = ModContentSyncStage.Failed; job.Error = error; }
        private static bool Active(ModContentSyncStage stage)
        { return stage == ModContentSyncStage.CheckingWorkshop || stage == ModContentSyncStage.Subscribing || stage == ModContentSyncStage.Downloading; }
        private static bool DownloadInstalled(SteamModDownloadState state)
        { return state.Installed && state.HasInstallInfo && !state.NeedsUpdate && !state.Downloading && !state.Pending; }
        internal static bool ContentsMatch(ModManifestEntry expected, ModManifestEntry actual)
        {
            return expected != null && actual != null && expected.Id == actual.Id && expected.Source == actual.Source &&
                expected.WorkshopId == actual.WorkshopId && expected.Version == actual.Version && expected.ContentSha256 == actual.ContentSha256;
        }
        private static void ValidateTime(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException(nameof(SteamModContentSync)); }
        public void Dispose()
        {
            if (disposed) return;
            Cancel(); disposed = true; api.DownloadCompleted -= OnDownloadCompleted;
            api.Dispose();
        }
    }
}
