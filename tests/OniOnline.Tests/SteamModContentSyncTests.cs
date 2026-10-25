using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Runtime;
using OniOnline.Sdk;

internal static class SteamModContentSyncTests
{
    internal static void Run(System.Action<bool, string> check)
    {
        var expected = Entry("test", "1", 'a', true);
        var required = Manifest(expected);
        var installed = Manifest();
        var plan = ModSyncPlanner.Compare(required, installed);
        var fake = new FakeSteam();
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            check(!sync.Begin(plan, "unconfirmed", 0) && fake.Inspections == 0, "unconfirmed plan makes no Steam calls");
            installed = Manifest(Entry("other", "1", 'b', true));
            check(!sync.Begin(plan, plan.Fingerprint, 0) && fake.Inspections == 0, "changed installed manifest invalidates confirmation");
            installed = Manifest();
            check(sync.Begin(plan, plan.Fingerprint, 0), "exact confirmed plan starts");
            check(fake.Inspections == 1 && fake.Subscriptions == 1 && fake.Downloads == 1, "metadata precedes subscription and download");
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading && !sync.CanJoin, "download in progress does not allow joining");
            fake.State.Installed = true; fake.State.HasInstallInfo = true; fake.State.Pending = true;
            fake.Emit(7, SteamModContentSync.OniAppId, true);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading, "installed flag with pending download is incomplete");
            fake.State.Pending = false; fake.State.NeedsUpdate = true;
            sync.Tick(1);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading, "needs-update package remains incomplete");
            fake.State.NeedsUpdate = false;
            fake.Emit(99, SteamModContentSync.OniAppId, true);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading, "unrelated item callback cannot complete a job");
            fake.Emit(7, 123, true);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading, "foreign app callback cannot complete a job");
            fake.Emit(7, SteamModContentSync.OniAppId, true);
            check(sync.Status.Single().Stage == ModContentSyncStage.AwaitingNativeInstall && !sync.CanJoin, "Steam installation waits for native installed and loaded evidence");
            installed = Manifest(Entry("test", "1", 'a', false));
            check(!sync.RefreshNativeCatalog() && sync.Status.Single().Stage == ModContentSyncStage.AwaitingActivation, "matching downloaded but disabled mod cannot join");
            installed = required;
            check(sync.RefreshNativeCatalog() && sync.Status.Single().Stage == ModContentSyncStage.Verified, "exact enabled native manifest allows joining");
            installed = Manifest(Entry("test", "1", 'b', true));
            check(!sync.RefreshNativeCatalog() && sync.Status.Single().Stage != ModContentSyncStage.Verified, "changed digest revokes verified status");
        }
        check(fake.Disposed, "controller disposes callback adapter");

        foreach (var bad in new[] { "wrong_item", "wrong_app", "banned", "details_failed", "sdk_throw", "subscribe_failed", "download_rejected", "state_failed" })
        {
            installed = Manifest(); fake = new FakeSteam { Fault = bad };
            using (var sync = new SteamModContentSync(fake, () => installed, () => required))
            {
                sync.Begin(plan, plan.Fingerprint, 0);
                check(sync.Status.Single().Stage == ModContentSyncStage.Failed && !sync.CanJoin, bad + " fails closed");
                if (bad.StartsWith("wrong_", StringComparison.Ordinal) || bad == "banned" || bad == "details_failed")
                    check(fake.Subscriptions == 0 && fake.Downloads == 0, bad + " is rejected before subscription");
            }
        }

        installed = Manifest(); fake = new FakeSteam { HoldInspection = true };
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            sync.Begin(plan, plan.Fingerprint, 0);
            var cancelled = fake.PendingInspection;
            sync.Cancel(); cancelled(new SteamModDetails { ItemId = 7, ConsumerAppId = SteamModContentSync.OniAppId, Success = true });
            check(fake.Subscriptions == 0 && fake.Downloads == 0, "late cancelled query callback has no side effects");
            sync.Begin(plan, plan.Fingerprint, 1);
            installed = Manifest(Entry("extra", "1", 'b', true));
            fake.PendingInspection(new SteamModDetails { ItemId = 7, ConsumerAppId = SteamModContentSync.OniAppId, Success = true });
            check(fake.Subscriptions == 0 && fake.Downloads == 0 && sync.Error == "mod_sync_plan_changed", "catalog drift during metadata query invalidates subsequent mutation");
        }

        installed = Manifest(); fake = new FakeSteam();
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            sync.Begin(plan, plan.Fingerprint, 0);
            sync.Cancel();
            sync.Begin(plan, plan.Fingerprint, 1);
            fake.State.Downloading = true;
            fake.Emit(7, SteamModContentSync.OniAppId, false);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading, "stale failure callback cannot fail a current active Steam download");
            fake.State.Downloading = false;
            fake.Emit(7, SteamModContentSync.OniAppId, false);
            check(sync.Status.Single().Stage == ModContentSyncStage.Downloading && sync.Status.Single().Error == "steam_download_callback_failed", "download callback failure is reported without trusting its request generation");
        }

        installed = Manifest(); fake = new FakeSteam { HoldInspection = true };
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            sync.Begin(plan, plan.Fingerprint, 0); sync.Tick(61);
            check(sync.Status.Single().Stage == ModContentSyncStage.Failed, "metadata callback timeout is bounded");
        }
        installed = Manifest(); fake = new FakeSteam { HoldInspection = true };
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            sync.Begin(plan, plan.Fingerprint, 0);
            var callback = fake.PendingInspection;
            required = Manifest(Entry("test", "2", 'b', true)); sync.Tick(1);
            callback(new SteamModDetails { ItemId = 7, ConsumerAppId = SteamModContentSync.OniAppId, Success = true });
            check(!sync.CanJoin && fake.Subscriptions == 0 && sync.Error == "mod_sync_plan_changed", "authority requirement change invalidates outstanding requests");
            required = plan.RequiredManifest;
        }
        var many = Manifest(Enumerable.Range(1, 9).Select(i => Entry("mod" + i, "1", 'a', true, (ulong)i)).ToArray());
        fake = new FakeSteam { HoldInspection = true };
        using (var sync = new SteamModContentSync(fake, () => Manifest(), () => many))
        {
            var manyPlan = ModSyncPlanner.Compare(many, Manifest()); sync.Begin(manyPlan, manyPlan.Fingerprint, 0);
            check(fake.Inspections == 4, "Workshop requests have a concurrency limit");
        }

        var local = new ModManifestEntry("local", "1", ModSyncPolicy.Required, ModDistributionSource.Local, 0, new string('a', 64));
        installed = Manifest(Entry("extra", "1", 'b', true)); fake = new FakeSteam();
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            required = Manifest(local); var manual = ModSyncPlanner.Compare(required, installed); sync.Begin(manual, manual.Fingerprint, 0);
            check(fake.Inspections == 0 && fake.Subscriptions == 0 && fake.Downloads == 0, "local and unexpected mods never download or upload automatically");
            check(sync.Status.All(s => s.Stage == ModContentSyncStage.ManualActionRequired), "manual actions remain explicit");
        }
        required = Manifest(); installed = Manifest(Entry("extra", "1", 'b', true)); fake = new FakeSteam();
        using (var sync = new SteamModContentSync(fake, () => installed, () => required))
        {
            var extras = ModSyncPlanner.Compare(required, installed); sync.Begin(extras, extras.Fingerprint, 0);
            installed = Manifest(); check(sync.RefreshNativeCatalog(), "removing extra loaded mod clears admission gate");
            installed = extras.InstalledManifest;
            check(!sync.RefreshNativeCatalog() && sync.Status.Single().Stage == ModContentSyncStage.ManualActionRequired,
                "reintroduced extra required mod revokes verified status");
        }
        Activation(check);
        Catalog(check);
        Diagnostics(check);
        RuntimePolicy(check);
    }

    private static void Activation(System.Action<bool, string> check)
    {
        var required = Manifest(Entry("test", "1", 'a', true));
        var installed = Manifest(Entry("test", "1", 'a', false));
        var plan = ModSyncPlanner.Compare(required, installed);
        NativeModActivationRequest request; string error;
        check(NativeModActivationRequest.TryCreate(plan, plan.Fingerprint, required, installed, out request, out error) &&
            request.Changes.Count == 1 && request.Changes[0].Enable, "activation preparation binds exact matching disabled content");
        check(!NativeModActivationRequest.TryCreate(plan, "stale", required, installed, out request, out error), "activation rejects unconfirmed plan");
        var wrong = Manifest(Entry("test", "2", 'b', false)); plan = ModSyncPlanner.Compare(required, wrong);
        check(plan.Changes.Single().Kind == ModSyncChangeKind.Disabled &&
            !NativeModActivationRequest.TryCreate(plan, plan.Fingerprint, required, wrong, out request, out error), "disabled comparison cannot hide wrong version or hash during activation");
        installed = Manifest(Entry("extra", "1", 'b', true)); plan = ModSyncPlanner.Compare(Manifest(), installed);
        check(NativeModActivationRequest.TryCreate(plan, plan.Fingerprint, Manifest(), installed, out request, out error) &&
            !request.Changes.Single().Enable, "extra required mod produces explicit disable request without performing it");
        ActivationOperations(check);
        DisabledCopyActivation(check);
    }

    private static void DisabledCopyActivation(System.Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-activation-copies-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "code.dll"), "reviewed-package");
            check(NativeModContentDigest.TryCompute(root, () => true, out var digest, out var digestError),
                "disabled copy fixture has a controlled content digest");
            var workshop = new NativeModRecord { Id = "test", Version = "1", ContentRoot = root,
                Source = ModDistributionSource.SteamWorkshop, WorkshopId = 7, Policy = ModSyncPolicy.Required, Installed = true };
            var dev = new NativeModRecord { Id = "test", Version = "dev", ContentRoot = root,
                Source = ModDistributionSource.Local, Policy = ModSyncPolicy.Required, Installed = true };
            var required = Manifest(new ModManifestEntry("test", "1", ModSyncPolicy.Required,
                ModDistributionSource.SteamWorkshop, 7, digest));
            var catalog = new NativeModCatalog(() => new[] { dev, workshop }, () => 1, () => true);
            catalog.CaptureLoadedSnapshot();
            var installed = catalog.CaptureInstalled(required);
            check(installed.IsComplete && installed.InstalledManifest.Entries.Single().Source == ModDistributionSource.SteamWorkshop,
                "native catalog selects the required disabled Workshop copy beside a Dev copy");
            var plan = ModSyncPlanner.Compare(required, installed.InstalledManifest);
            check(NativeModActivationRequest.TryCreate(plan, plan.Fingerprint, required, installed.InstalledManifest,
                out var request, out var error), "disabled Workshop copy prepares an exact reviewed activation");
            int saves = 0;
            var targets = new[] {
                new NativeModActivationTarget(dev, value => dev.ConfiguredEnabled = value),
                new NativeModActivationTarget(workshop, value => workshop.ConfiguredEnabled = value) };
            check(NativeModActivationOperations.TryApply(targets, request.Changes, () => { saves++; return true; }, out error) &&
                workshop.ConfiguredEnabled && !dev.ConfiguredEnabled && saves == 1,
                "activation enables only the reviewed disabled Workshop copy and saves once");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void ActivationOperations(System.Action<bool, string> check)
    {
        bool enabled = false;
        var content = Entry("test", "1", 'a', false);
        var target = Target(content, value => enabled = value);
        var change = new[] { new NativeModActivationChange(content, true) };
        check(NativeModActivationOperations.TryApply(new[] { target }, change, () => true, out var error) &&
            enabled && target.Enabled && error == "", "activation operations apply one reviewed native setting");

        enabled = false; target.Enabled = false;
        check(!NativeModActivationOperations.TryApply(new[] { target }, change, () => false, out error) &&
            !enabled && !target.Enabled && error == "native_mod_settings_write_failed",
            "activation operations roll back when the native settings write fails");

        var duplicateContent = Entry("duplicate", "1", 'a', false);
        var duplicate = new[] { Target(duplicateContent, _ => { }), Target(duplicateContent, _ => { }) };
        check(!NativeModActivationOperations.TryApply(duplicate,
            new[] { new NativeModActivationChange(duplicateContent, true) }, () => true, out error) &&
            error == "native_mod_identity_ambiguous", "activation operations refuse ambiguous disabled sources");

        enabled = false; target.Enabled = false;
        check(!NativeModActivationOperations.TryApply(new[] { target }, new[] {
                new NativeModActivationChange(content, true), new NativeModActivationChange(content, false) },
                () => true, out error) && error == "native_mod_activation_conflict" && !enabled && !target.Enabled,
            "activation operations reject conflicting duplicate requests before mutating native settings");

        check(NativeModActivationOperations.TryApply(new[] { target }, new[] {
                new NativeModActivationChange(content, true), new NativeModActivationChange(content, true) },
                () => true, out error) && enabled && target.Enabled,
            "activation operations coalesce identical duplicate requests");

        enabled = false; target.Enabled = false;
        check(!NativeModActivationOperations.TryApply(new[] { target }, new[] {
                new NativeModActivationChange(content, true),
                new NativeModActivationChange(Entry("test", "1", 'b', false), true) },
                () => true, out error) && error == "native_mod_activation_conflict" && !enabled,
            "activation refuses duplicate requests for different reviewed content before any write");

        foreach (var different in new[] {
            Entry("test", "2", 'a', false), Entry("test", "1", 'a', false, 99),
            new ModManifestEntry("test", "1", ModSyncPolicy.Required, ModDistributionSource.Local, 0, new string('a', 64)) })
        {
            int writes = 0, saves = 0;
            var earlier = Target(Entry("earlier", "1", 'a', false), _ => writes++);
            var drifted = Target(different, _ => writes++);
            check(!NativeModActivationOperations.TryApply(new[] { earlier, drifted }, new[] {
                    new NativeModActivationChange(Entry("earlier", "1", 'a', false), true), change[0] },
                    () => { saves++; return true; }, out error) && error == "native_mod_content_not_ready" && writes == 0 && saves == 0,
                "native package source, Workshop item and version stay bound to the reviewed batch");
        }

        var otherContent = Entry("test", "2", 'b', true);
        var loadedOther = Target(otherContent, _ => { }, loaded: true);
        check(!NativeModActivationOperations.TryApply(new[] { loadedOther, target }, change,
                () => true, out error) && error == "native_mod_content_not_ready" && !enabled,
            "a different active package cannot be replaced by silently enabling an inactive copy");
        var loadedExpected = Target(Entry("test", "1", 'a', true), _ => { }, loaded: true);
        check(!NativeModActivationOperations.TryApply(new[] { loadedOther, loadedExpected }, change,
                () => true, out error) && error == "native_mod_identity_ambiguous",
            "multiple active packages stay ambiguous even when only one matches the review");
    }

    private static NativeModActivationTarget Target(ModManifestEntry content, System.Action<bool> setEnabled, bool loaded = false)
    {
        return new NativeModActivationTarget(new NativeModRecord { Id = content.Id, Version = content.Version,
            Source = content.Source, WorkshopId = content.WorkshopId, Policy = content.Policy,
            Installed = true, ConfiguredEnabled = content.Enabled, Loaded = loaded }, setEnabled);
    }

    private static void Catalog(System.Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-native-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "code.dll"), "first");
            File.WriteAllText(Path.Combine(root, "mesh.custom"), "declared-resource");
            var record = new NativeModRecord
            {
                Id = "测试 模组", Version = "1", ContentRoot = root, Source = ModDistributionSource.Local,
                WorkshopId = 0, Policy = ModSyncPolicy.Required, ConfiguredEnabled = true, Loaded = true, Installed = true,
                ContentExtensions = new[] { ".custom" }
            };
            long epoch = 1; bool idle = true;
            var catalog = new NativeModCatalog(() => new[] { record }, () => epoch, () => idle);
            check(!catalog.CaptureInstalled().IsComplete, "catalog cannot invent an initial loaded snapshot");
            var initial = catalog.CaptureLoadedSnapshot();
            check(initial.IsComplete && initial.ActiveManifest.Entries.Single().Id == record.Id, "native Unicode stable identity is preserved");
            var withoutDeclaration = new NativeModRecord
            {
                Id = record.Id, Version = record.Version, ContentRoot = root, Source = record.Source,
                WorkshopId = record.WorkshopId, Policy = record.Policy, ConfiguredEnabled = true, Loaded = true, Installed = true
            };
            var defaultCatalog = new NativeModCatalog(() => new[] { withoutDeclaration }, () => epoch, () => idle);
            check(defaultCatalog.CaptureLoadedSnapshot().InstalledManifest.Fingerprint != initial.InstalledManifest.Fingerprint,
                "native catalog passes trusted custom content extensions into the digest");
            check(catalog.CaptureInstalled().InstalledManifest.Fingerprint == initial.InstalledManifest.Fingerprint, "unchanged package retains loaded evidence");
            File.WriteAllText(Path.Combine(root, "code.dll"), "other");
            var changed = catalog.CaptureInstalled();
            check(!changed.IsComplete && !changed.InstalledManifest.Entries.Single().Enabled && changed.ActiveManifest.Entries.Count == 0,
                "disk update cannot claim an assembly was hot loaded");
            check(changed.Issues.All(i => !i.Code.Contains(root)), "catalog error codes contain no private path");
            idle = false;
            check(catalog.CaptureInstalled().Issues.Single().Code == "native_mod_loader_busy", "catalog refuses to inspect a busy loader");
            idle = true;
            var ambiguous = new NativeModCatalog(() => new[] { record, record }, () => epoch, () => idle).CaptureLoadedSnapshot();
            check(!ambiguous.IsComplete && ambiguous.InstalledManifest.Entries.Count == 0, "duplicate stable IDs fail closed");
            var devCopy = new NativeModRecord
            {
                Id = record.Id, Version = "dev", ContentRoot = root, Source = ModDistributionSource.Local,
                Policy = ModSyncPolicy.Required, Installed = true
            };
            var normalCopies = new NativeModCatalog(() => new[] { record, devCopy }, () => epoch, () => idle).CaptureLoadedSnapshot();
            check(normalCopies.IsComplete && normalCopies.ActiveManifest.Entries.Single().Version == "1" &&
                normalCopies.Issues.Single().Code == "inactive_native_mod_copies_ignored", "unique active Local wins over disabled Dev copy");
            record.ConfiguredEnabled = false;
            check(!new NativeModCatalog(() => new[] { record }, () => epoch, () => idle).CaptureLoadedSnapshot().IsComplete,
                "disabled configuration cannot hide code already loaded in memory");
            record.ConfiguredEnabled = false; record.Loaded = false;
            var disabled = new NativeModCatalog(() => new[] { record }, () => epoch, () => idle).CaptureLoadedSnapshot();
            check(disabled.IsComplete && !disabled.InstalledManifest.Entries.Single().Enabled, "installed disabled package remains available for plan comparison");
        }
        finally { Directory.Delete(root, true); }
    }

    private static ModManifestEntry Entry(string id, string version, char digest, bool enabled, ulong workshop = 7)
    { return new ModManifestEntry(id, version, ModSyncPolicy.Required, ModDistributionSource.SteamWorkshop, workshop, new string(digest, 64), enabled); }
    private static ModManifest Manifest(params ModManifestEntry[] entries) { return new ModManifest(entries); }

    private static void Diagnostics(System.Action<bool, string> check)
    {
        var busy = new NativeModCatalogSnapshot(Manifest(), new[] { new NativeModCatalogIssue("", "native_mod_loader_busy") });
        check(NativeModCatalogDiagnostics.ShouldRetryInitial(busy, 0) && !NativeModCatalogDiagnostics.ShouldRetryInitial(busy, 30),
            "initial catalog loading has a bounded retry window");
        var rejected = new NativeModCatalogSnapshot(Manifest(), new[] { new NativeModCatalogIssue("private/path", "content_data_unclassified") });
        string summary = NativeModCatalogDiagnostics.Summary(rejected);
        check(!NativeModCatalogDiagnostics.ShouldRetryInitial(rejected, 0) && !summary.Contains("private/path") && summary.Contains("尚未声明"),
            "unclassified content stops retrying and exposes a safe actionable cause");
        var copies = new NativeModCatalogSnapshot(Manifest(), new[] { new NativeModCatalogIssue("public-mod-id", "native_mod_identity_ambiguous") });
        check(NativeModCatalogDiagnostics.Summary(copies).Contains("重复启用") && NativeModCatalogDiagnostics.Summary(copies).Contains("public-mod-id"),
            "active-source collision identifies the safe mod identity");
    }

    private static void RuntimePolicy(System.Action<bool, string> check)
    {
        var fake = new FakeSteam();
        SteamModContentSync sync = new SteamModContentSync(fake, () => Manifest(), () => Manifest());
        ModSyncRuntimePolicy.DisposeContentSync(ref sync);
        check(sync == null && fake.Disposed, "runtime cleanup releases an obsolete Steam content controller");
        check(ModSyncRuntimePolicy.ErrorKey("steam_item_identity_mismatch") == "mod_sync.error_item" &&
            ModSyncRuntimePolicy.ErrorKey("steam_request_timeout") == "mod_sync.error_timeout" &&
            ModSyncRuntimePolicy.ErrorKey("native_mod_settings_write_failed") == "mod_sync.activation_failed" &&
            ModSyncRuntimePolicy.ErrorKey("native_mod_loader_busy") == "mod_sync.activation_wait" &&
            ModSyncRuntimePolicy.ErrorKey("unrecognized_private_error") == "mod_sync.error_generic",
            "mod sync UI maps known failures to safe localized causes and hides unknown details");
        check(ModSyncRuntimePolicy.NeedsNativeRefresh(new[] {
                new ModContentSyncStatus("a", 1, ModContentSyncStage.Downloading, "", 0, 1),
                new ModContentSyncStatus("b", 2, ModContentSyncStage.AwaitingActivation, "", 1, 1) }),
            "native refresh is requested while downloaded content awaits loaded evidence");
        check(!ModSyncRuntimePolicy.NeedsNativeRefresh(new[] {
                new ModContentSyncStatus("a", 1, ModContentSyncStage.Verified, "", 1, 1) }),
            "native refresh is not repeated after every mod is verified");
    }

    private sealed class FakeSteam : ISteamModContentApi
    {
        internal string Fault = "";
        internal bool HoldInspection, Disposed;
        internal int Inspections, Subscriptions, Downloads;
        internal System.Action<SteamModDetails> PendingInspection;
        internal SteamModDownloadState State = new SteamModDownloadState { Subscribed = false, DownloadedBytes = 0, TotalBytes = 0 };
        public event System.Action<SteamModOperation> DownloadCompleted;
        public bool Inspect(ulong itemId, System.Action<SteamModDetails> completed)
        {
            Inspections++;
            if (Fault == "sdk_throw") throw new InvalidOperationException("private/path");
            if (HoldInspection) { PendingInspection = completed; return true; }
            completed(new SteamModDetails
            {
                ItemId = Fault == "wrong_item" ? 99 : itemId,
                ConsumerAppId = Fault == "wrong_app" ? 123 : SteamModContentSync.OniAppId,
                Success = Fault != "details_failed", Banned = Fault == "banned"
            });
            return true;
        }
        public bool Subscribe(ulong itemId, System.Action<SteamModOperation> completed)
        { Subscriptions++; completed(new SteamModOperation { ItemId = itemId, Success = Fault != "subscribe_failed" }); return true; }
        public bool Download(ulong itemId) { Downloads++; return Fault != "download_rejected"; }
        public bool TryGetState(ulong itemId, out SteamModDownloadState state) { state = State; return Fault != "state_failed"; }
        internal void Emit(ulong itemId, uint app, bool success)
        { DownloadCompleted?.Invoke(new SteamModOperation { ItemId = itemId, AppId = app, Success = success }); }
        public void CancelPendingRequests() { }
        public void Dispose() { Disposed = true; }
    }
}
