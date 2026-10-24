using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OniOnline.Sdk;

namespace OniOnline.Runtime
{
    // ContentRoot stays inside this adapter and must never be serialized into a manifest or error.
    internal sealed class NativeModRecord
    {
        internal string Id, Version, ContentRoot;
        internal IReadOnlyCollection<string> ContentExtensions = null;
        internal ModDistributionSource Source;
        internal ulong WorkshopId;
        internal ModSyncPolicy Policy;
        internal bool ConfiguredEnabled, Loaded, Installed;
    }

    internal sealed class NativeModCatalogIssue
    {
        internal string Id { get; }
        internal string Code { get; }
        internal bool Blocking { get; }
        internal NativeModCatalogIssue(string id, string code, bool blocking = true)
        { Id = id ?? ""; Code = code; Blocking = blocking; }
    }

    internal sealed class NativeModCatalogSnapshot
    {
        internal ModManifest InstalledManifest { get; }
        internal ModManifest ActiveManifest { get; }
        internal IReadOnlyList<NativeModCatalogIssue> Issues { get; }
        internal bool IsComplete { get { return Issues.All(issue => !issue.Blocking); } }
        internal NativeModCatalogSnapshot(ModManifest installed, IList<NativeModCatalogIssue> issues)
        {
            InstalledManifest = installed;
            ActiveManifest = new ModManifest(installed.Entries.Where(e => e.Enabled));
            Issues = new ReadOnlyCollection<NativeModCatalogIssue>(new List<NativeModCatalogIssue>(issues));
        }
    }

    // CaptureLoadedSnapshot belongs at OnAllModsLoaded, before any package update can replace disk content.
    // A fresh file digest can invalidate loaded evidence; it cannot establish that Unity reloaded assemblies.
    internal sealed class NativeModCatalog
    {
        private readonly Func<IEnumerable<NativeModRecord>> source;
        private readonly Func<long> loaderEpoch;
        private readonly Func<bool> loaderIdle;
        private readonly Dictionary<string, ModManifestEntry> loaded = new Dictionary<string, ModManifestEntry>(StringComparer.Ordinal);
        private bool captured;

        internal NativeModCatalog(Func<IEnumerable<NativeModRecord>> source, Func<long> loaderEpoch, Func<bool> loaderIdle)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.loaderEpoch = loaderEpoch ?? throw new ArgumentNullException(nameof(loaderEpoch));
            this.loaderIdle = loaderIdle ?? throw new ArgumentNullException(nameof(loaderIdle));
        }

        internal NativeModCatalogSnapshot CaptureLoadedSnapshot()
        {
            if (captured) throw new InvalidOperationException("loaded_mod_snapshot_already_captured");
            var snapshot = Capture(true, null);
            if (!snapshot.IsComplete) return snapshot;
            foreach (var entry in snapshot.InstalledManifest.Entries)
                if (entry.Enabled) loaded.Add(entry.Id, entry);
            captured = true;
            return snapshot;
        }

        internal NativeModCatalogSnapshot CaptureInstalled(ModManifest desired = null)
        {
            if (!captured)
                return new NativeModCatalogSnapshot(new ModManifest(new ModManifestEntry[0]),
                    new[] { new NativeModCatalogIssue("", "loaded_mod_snapshot_missing") });
            return Capture(false, desired);
        }

        private NativeModCatalogSnapshot Capture(bool initial, ModManifest desired)
        {
            var issues = new List<NativeModCatalogIssue>();
            var entries = new List<ModManifestEntry>();
            long epoch;
            List<NativeModRecord> records;
            try
            {
                if (!loaderIdle()) return Failure("native_mod_loader_busy");
                epoch = loaderEpoch();
                records = source().Take(ModManifest.MaximumEntries + 1).ToList();
            }
            catch { return Failure("native_mod_catalog_failed"); }
            if (records.Count > ModManifest.MaximumEntries) return Failure("native_mod_catalog_budget");
            foreach (var record in SelectRecords(records, desired, issues))
            {
                if (record == null) { issues.Add(new NativeModCatalogIssue("", "invalid_native_mod_record")); continue; }
                if (!record.Installed)
                {
                    if (record.ConfiguredEnabled) issues.Add(new NativeModCatalogIssue(record.Id, "native_mod_not_installed"));
                    continue;
                }
                if (!NativeModContentDigest.TryCompute(record.ContentRoot, record.ContentExtensions,
                    () => loaderIdle() && loaderEpoch() == epoch, out var digest, out var error))
                { issues.Add(new NativeModCatalogIssue(record.Id, error)); continue; }
                try
                {
                    var content = new ModManifestEntry(record.Id, record.Version, record.Policy, record.Source,
                        record.WorkshopId, digest, false);
                    bool enabled = record.Loaded;
                    if (!initial)
                        enabled = enabled && loaded.TryGetValue(record.Id, out var original) && SteamModContentSync.ContentsMatch(original, content);
                    if (record.ConfiguredEnabled != enabled)
                        issues.Add(new NativeModCatalogIssue(record.Id, record.Loaded ? "native_mod_restart_required" : "native_mod_not_loaded"));
                    entries.Add(new ModManifestEntry(content.Id, content.Version, content.Policy, content.Source,
                        content.WorkshopId, content.ContentSha256, enabled));
                }
                catch (ArgumentException) { issues.Add(new NativeModCatalogIssue(record.Id, "invalid_native_mod_metadata")); }
            }
            try
            {
                if (!loaderIdle() || loaderEpoch() != epoch) return Failure("native_mod_catalog_changed");
            }
            catch { return Failure("native_mod_catalog_failed"); }
            return new NativeModCatalogSnapshot(new ModManifest(entries), issues);
        }

        private static NativeModCatalogSnapshot Failure(string code)
        { return new NativeModCatalogSnapshot(new ModManifest(new ModManifestEntry[0]), new[] { new NativeModCatalogIssue("", code) }); }

        private static IEnumerable<NativeModRecord> SelectRecords(List<NativeModRecord> records, ModManifest desired,
            IList<NativeModCatalogIssue> issues)
        {
            foreach (var group in records.GroupBy(record => record?.Id, StringComparer.Ordinal))
            {
                var candidates = group.ToList();
                if (candidates.Count == 1) { yield return candidates[0]; continue; }
                var active = candidates.Where(record => record != null && (record.Loaded || record.ConfiguredEnabled)).ToList();
                if (active.Count > 1)
                { issues.Add(new NativeModCatalogIssue(group.Key, "native_mod_identity_ambiguous")); continue; }
                if (active.Count == 1)
                {
                    issues.Add(new NativeModCatalogIssue(group.Key, "inactive_native_mod_copies_ignored", false));
                    yield return active[0]; continue;
                }
                var expected = desired?.Entries.FirstOrDefault(entry => entry.Id == group.Key);
                if (expected != null)
                {
                    candidates = candidates.Where(record => record != null && record.Source == expected.Source &&
                        record.WorkshopId == expected.WorkshopId && record.Version == expected.Version).ToList();
                    if (candidates.Count == 1)
                    {
                        issues.Add(new NativeModCatalogIssue(group.Key, "inactive_native_mod_copy_selected", false));
                        yield return candidates[0]; continue;
                    }
                }
                // No loaded identity is hidden. An ambiguous disabled copy remains Missing in a sync plan.
                issues.Add(new NativeModCatalogIssue(group.Key, "inactive_native_mod_copies_ambiguous", false));
            }
        }
    }

    internal sealed class NativeModActivationChange
    {
        internal string Id { get; }
        internal bool Enable { get; }
        internal NativeModActivationChange(string id, bool enable) { Id = id; Enable = enable; }
    }

    internal sealed class NativeModActivationTarget
    {
        internal string Id { get; }
        internal bool Installed, Enabled, Loaded;
        internal Action<bool> SetEnabled { get; }
        internal NativeModActivationTarget(string id, bool installed, bool enabled, bool loaded, Action<bool> setEnabled)
        { Id = id; Installed = installed; Enabled = enabled; Loaded = loaded; SetEnabled = setEnabled; }
    }

    internal static class NativeModActivationOperations
    {
        internal static bool TryApply(IReadOnlyList<NativeModActivationTarget> targets,
            IReadOnlyList<NativeModActivationChange> changes, Func<bool> save, out string error)
        {
            error = "";
            if (targets == null || changes == null || changes.Count == 0 || save == null)
            { error = "native_mod_activation_invalid"; return false; }
            var groups = targets.Where(target => target != null && target.Id != null)
                .GroupBy(target => target.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var assignments = new List<System.Tuple<NativeModActivationTarget, bool>>();
            foreach (var change in changes)
            {
                if (change == null || !groups.TryGetValue(change.Id, out var group))
                { error = "native_mod_not_found"; return false; }
                var active = group.Where(candidate => candidate.Enabled || candidate.Loaded).ToList();
                NativeModActivationTarget selected;
                if (active.Count == 1) selected = active[0];
                else if (active.Count == 0 && group.Count == 1) selected = group[0];
                else { error = "native_mod_identity_ambiguous"; return false; }
                if (!selected.Installed || selected.SetEnabled == null)
                { error = "native_mod_not_installed"; return false; }
                if (selected.Enabled != change.Enable) assignments.Add(System.Tuple.Create(selected, selected.Enabled));
            }
            if (assignments.Count == 0) { error = "native_mod_activation_noop"; return false; }
            try
            {
                foreach (var assignment in assignments)
                {
                    var requested = changes.First(change => change.Id == assignment.Item1.Id).Enable;
                    assignment.Item1.SetEnabled(requested);
                    assignment.Item1.Enabled = requested;
                }
                if (save()) return true;
                error = "native_mod_settings_write_failed";
            }
            catch (Exception) { error = "native_mod_settings_write_failed"; }
            foreach (var assignment in assignments)
                try { assignment.Item1.SetEnabled(assignment.Item2); assignment.Item1.Enabled = assignment.Item2; } catch (Exception) { }
            try { save(); } catch (Exception) { }
            return false;
        }
    }

    // Preparation is reversible and side-effect free. Actual changes belong to an idle native-menu adapter.
    internal sealed class NativeModActivationRequest
    {
        internal string PlanFingerprint { get; }
        internal IReadOnlyList<NativeModActivationChange> Changes { get; }
        private NativeModActivationRequest(ModSyncPlan plan, IList<NativeModActivationChange> changes)
        { PlanFingerprint = plan.Fingerprint; Changes = new ReadOnlyCollection<NativeModActivationChange>(changes); }

        internal static bool TryCreate(ModSyncPlan plan, string confirmedFingerprint, ModManifest currentRequired,
            ModManifest currentInstalled, out NativeModActivationRequest request, out string error)
        {
            request = null; error = "";
            if (plan == null || plan.Fingerprint != confirmedFingerprint || !plan.IsCurrent(currentRequired, currentInstalled))
            { error = "mod_sync_plan_changed"; return false; }
            var changes = new List<NativeModActivationChange>();
            foreach (var change in plan.Changes)
            {
                if (change.Kind == ModSyncChangeKind.Unexpected)
                    changes.Add(new NativeModActivationChange(change.Installed.Id, false));
                else if (change.Installed != null && !change.Installed.Enabled && SteamModContentSync.ContentsMatch(change.Expected, change.Installed))
                    changes.Add(new NativeModActivationChange(change.Expected.Id, true));
                else { error = "native_mod_content_not_ready"; return false; }
            }
            request = new NativeModActivationRequest(plan, changes);
            return true;
        }
    }
}
