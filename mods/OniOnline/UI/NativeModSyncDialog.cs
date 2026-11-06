using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using OniOnline.Runtime;
using OniOnline.Sdk;
using UnityEngine;

namespace OniOnline.UI
{
    // Use the same frame and button layout as the native mod-difference dialog.
    // The adapter owns downloads and activation; this view only returns a reviewed plan.
    internal sealed class NativeModSyncDialog : IDisposable
    {
        private const int MaximumPageCharacters = 640;
        private const int MaximumPageEntries = 6;
        private readonly string fingerprint;
        private readonly Func<string, bool> isCurrent;
        private readonly System.Action<string> onConfirmDownload;
        private readonly System.Action onOpenMods;
        private readonly System.Action onPrimary;
        private readonly string primaryText;
        private readonly System.Action onCancel;
        private readonly List<string> pages;
        private readonly string overview;
        private readonly ModSyncReviewAction reviewAction;
        private ConfirmDialogScreen dialog;
        private int page;
        private int screenGeneration;
        private bool disposed, opened;

        internal string PlanFingerprint { get { return fingerprint; } }
        internal bool IsOpen { get { return !disposed && dialog != null && dialog.IsActive() && dialog.IsScreenActive(); } }

        private NativeModSyncDialog(string fingerprint, Func<string, bool> isCurrent,
            System.Action<string> onConfirmDownload, System.Action onOpenMods, System.Action onPrimary,
            string primaryText, System.Action onCancel, List<string> pages, ModSyncReviewAction reviewAction, string overview = null)
        {
            if (string.IsNullOrEmpty(fingerprint)) throw new ArgumentException("invalid_mod_sync_fingerprint");
            this.fingerprint = fingerprint;
            this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            this.onConfirmDownload = onConfirmDownload;
            this.onOpenMods = onOpenMods;
            this.onPrimary = onPrimary;
            this.primaryText = primaryText;
            this.onCancel = onCancel;
            this.pages = pages;
            this.reviewAction = reviewAction;
            this.overview = overview;
        }

        internal static NativeModSyncDialog Open(ModSyncPlan plan, System.Action<string> onConfirmDownload,
            System.Action onOpenMods, System.Action onApply, System.Action onCancel, Func<string, bool> isCurrent)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.CanJoin) throw new ArgumentException("mod_sync_plan_has_no_changes");
            if (onOpenMods == null) throw new ArgumentNullException(nameof(onOpenMods));
            if (onCancel == null) throw new ArgumentNullException(nameof(onCancel));
            var action = ModSyncRuntimePolicy.PrimaryAction(plan, onConfirmDownload != null, onApply != null);
            var view = new NativeModSyncDialog(plan.Fingerprint, isCurrent, onConfirmDownload,
                onOpenMods, onApply, T("mod_sync.apply_restart"), onCancel, BuildPages(plan), action, BuildOverview(plan));
            try { view.ShowOverview(); return view; }
            catch { view.Dispose(); throw; }
        }

        internal static NativeModSyncDialog OpenStatus(string planFingerprint, string text,
            System.Action onCancel, Func<string, bool> isCurrent, System.Action onOpenMods = null,
            System.Action onPrimary = null, string primaryText = null)
        {
            if (onCancel == null) throw new ArgumentNullException(nameof(onCancel));
            ValidateStatus(text);
            var view = new NativeModSyncDialog(planFingerprint, isCurrent, null, onOpenMods, onPrimary,
                primaryText, onCancel, null, ModSyncReviewAction.Manage);
            try
            {
                if (!view.Refresh()) return view;
                int generation = ++view.screenGeneration;
                var action = onPrimary ?? onOpenMods ?? onCancel;
                string actionText = primaryText ?? T(onOpenMods == null ? "mod_sync.back" : "mod_sync.manage");
                view.Show(text, () => view.Select(generation, action), null,
                    actionText, null, generation, action == onCancel && onOpenMods == null && onPrimary == null);
                return view;
            }
            catch { view.Dispose(); throw; }
        }

        // Call from the owning adapter's update loop. Expired connection/plan views close silently.
        internal bool Refresh()
        {
            if (disposed) return false;
            bool current;
            try { current = isCurrent(fingerprint); }
            catch (Exception) { current = false; }
            if (!current || opened && (dialog == null || !dialog.IsActive() || !dialog.IsScreenActive())) Dispose();
            return !disposed;
        }

        internal bool SetStatus(string text)
        {
            ValidateStatus(text);
            if (pages != null) throw new InvalidOperationException("mod_sync_review_in_progress");
            if (!Refresh() || dialog == null) return false;
            var body = Body(dialog);
            if (body == null) { Dispose(); return false; }
            body.SetText(text);
            return true;
        }

        private static void ValidateStatus(string text)
        {
            if (text == null || text.Length > 2048) throw new ArgumentException("invalid_mod_sync_status");
        }

        private void ShowOverview()
        {
            if (!Refresh()) return;
            int generation = ++screenGeneration;
            System.Action primary = reviewAction == ModSyncReviewAction.Download
                ? (System.Action)(() => onConfirmDownload(fingerprint))
                : reviewAction == ModSyncReviewAction.ApplyAndRestart ? onPrimary : onOpenMods;
            string label = reviewAction == ModSyncReviewAction.ApplyAndRestart ? primaryText
                : T(reviewAction == ModSyncReviewAction.Download ? "mod_sync.download" : "mod_sync.manage");
            Show(overview, () => Select(generation, primary), () => ShowDetails(generation),
                label, T("mod_sync.details"), generation);
        }

        private void ShowDetails(int generation)
        {
            if (generation != screenGeneration || !Refresh()) return;
            page = 0;
            ShowPage();
        }

        private void BackToOverview(int generation)
        {
            if (generation != screenGeneration || !Refresh()) return;
            ShowOverview();
        }

        private void ShowPage()
        {
            if (!Refresh()) return;
            int generation = ++screenGeneration;
            bool last = page == pages.Count - 1;
            string text = T("mod_sync.intro") + "\n\n" + pages[page] + "\n\n" + T("mod_sync.restart");
            if (pages.Count > 1)
                text += "\n" + string.Format(CultureInfo.CurrentCulture, T("mod_sync.page"), page + 1, pages.Count);
            if (!last)
            {
                Show(text, () => Next(generation), () => BackToOverview(generation),
                    T("mod_sync.next"), T("mod_sync.overview"), generation);
            }
            else
                Show(text, () => BackToOverview(generation), null,
                    T("mod_sync.overview"), null, generation);
        }

        private void Show(string text, System.Action confirm, System.Action configure,
            string confirmText, string configureText, int generation, bool singleAction = false)
        {
            if (dialog == null)
            {
                var prefabs = ScreenPrefabs.Instance;
                var manager = KScreenManager.Instance;
                var root = Global.Instance == null ? null : Global.Instance.globalCanvas;
                if (prefabs == null || prefabs.ConfirmDialogScreen == null || manager == null || root == null)
                    throw new InvalidOperationException("native_mod_sync_dialog_unavailable");
                dialog = manager.StartScreen(prefabs.ConfirmDialogScreen.gameObject, root) as ConfirmDialogScreen;
                if (dialog == null) throw new InvalidOperationException("native_mod_sync_dialog_unavailable");
                dialog.gameObject.name = "OniOnlineModSyncDialog";
                // Keep one native modal alive across review pages. Replacing modal
                // objects in the same frame can release the new screen's camera lock.
                dialog.deactivateOnConfirmAction = false;
                dialog.deactivateOnCancelAction = false;
                dialog.deactivateOnConfigurableAction = false;
            }
            ClearNativeClick("confirmButton");
            ClearNativeClick("cancelButton");
            ClearNativeClick("configurableButton");
            dialog.PopupConfirmDialog(text, confirm, () => Select(generation, onCancel),
                configureText, configure, T("mod_sync.title"), confirmText, T("mod_sync.back"));
            var cancel = AccessTools.Field(typeof(ConfirmDialogScreen), "cancelButton")?.GetValue(dialog) as GameObject;
            if (cancel != null) cancel.SetActive(!singleAction);
            var body = Body(dialog);
            if (body != null) body.richText = false;
            opened = true;
        }

        private void ClearNativeClick(string name)
        {
            var button = AccessTools.Field(typeof(ConfirmDialogScreen), name)?.GetValue(dialog) as GameObject;
            if (button == null) throw new InvalidOperationException("native_mod_sync_button_unavailable");
            var control = button.GetComponent<KButton>();
            if (control == null) throw new InvalidOperationException("native_mod_sync_button_unavailable");
            control.ClearOnClick();
        }

        private static LocText Body(ConfirmDialogScreen screen)
        { return AccessTools.Field(typeof(ConfirmDialogScreen), "popupMessage")?.GetValue(screen) as LocText; }

        private void Next(int generation)
        {
            if (generation != screenGeneration || !Refresh()) return;
            page++;
            ShowPage();
        }

        private void Select(int generation, System.Action action)
        {
            if (generation != screenGeneration || !Refresh()) return;
            Dispose();
            action?.Invoke();
        }

        private static List<string> BuildPages(ModSyncPlan plan)
        {
            var result = new List<string>();
            var text = new StringBuilder();
            int entries = 0;
            foreach (var change in plan.Changes)
            {
                string line = Describe(change);
                if (entries != 0 && (entries >= MaximumPageEntries || text.Length + line.Length + 2 > MaximumPageCharacters))
                { result.Add(text.ToString()); text.Clear(); entries = 0; }
                if (entries != 0) text.Append('\n');
                text.Append(line);
                entries++;
            }
            if (entries != 0) result.Add(text.ToString());
            return result;
        }

        private static string BuildOverview(ModSyncPlan plan)
        {
            int download = 0, enable = 0, disable = 0, manual = 0;
            foreach (var change in plan.Changes)
            {
                if (change.Kind == ModSyncChangeKind.Unexpected) disable++;
                else if (SteamModContentSync.ContentsMatch(change.Expected, change.Installed)) enable++;
                else if (change.CanDownload) download++;
                else manual++;
            }
            var text = new StringBuilder(Format("mod_sync.summary", plan.Changes.Count));
            if (download != 0) text.Append('\n').Append(Format("mod_sync.summary_download", download));
            if (enable != 0) text.Append('\n').Append(Format("mod_sync.summary_enable", enable));
            if (disable != 0) text.Append('\n').Append(Format("mod_sync.summary_disable", disable));
            if (manual != 0) text.Append('\n').Append(Format("mod_sync.summary_manual", manual));
            text.Append("\n\n");
            int shown = 0, characters = 0;
            foreach (var change in plan.Changes)
            {
                string line = Describe(change);
                if (shown != 0 && (shown >= 3 || characters + line.Length > 420)) break;
                if (shown != 0) text.Append('\n');
                text.Append(line);
                characters += line.Length;
                shown++;
            }
            if (shown < plan.Changes.Count)
                text.Append('\n').Append(Format("mod_sync.more", plan.Changes.Count - shown));
            return text.Append("\n\n").Append(T("mod_sync.restart")).ToString();
        }

        private static string Describe(ModSyncChange change)
        {
            string key;
            switch (change.Kind)
            {
                case ModSyncChangeKind.Missing: key = "mod_sync.missing"; break;
                case ModSyncChangeKind.Disabled: key = "mod_sync.disabled"; break;
                case ModSyncChangeKind.SourceMismatch: key = "mod_sync.source_mismatch"; break;
                case ModSyncChangeKind.VersionMismatch: key = "mod_sync.version_mismatch"; break;
                case ModSyncChangeKind.ContentMismatch: key = "mod_sync.content_mismatch"; break;
                case ModSyncChangeKind.Unexpected: key = "mod_sync.unexpected"; break;
                default: throw new ArgumentOutOfRangeException(nameof(change));
            }
            string expected = change.Expected == null ? "" : change.Expected.Version;
            string installed = change.Installed == null ? "" : change.Installed.Version;
            return string.Format(CultureInfo.CurrentCulture, T(key), change.Id, expected, installed);
        }

        private static string T(string key) { return OnlineText.Get(key); }
        private static string Format(string key, params object[] arguments)
        { return string.Format(CultureInfo.CurrentCulture, T(key), arguments); }

        private void CloseScreen()
        {
            var previous = dialog;
            dialog = null;
            if (previous == null) return;
            // Deactivate owns native pause/focus cleanup and destroys the screen.
            // Disable synchronously so its native OnCmpDisable camera unlock runs
            // before an action opens the next native screen in this same frame.
            previous.gameObject.SetActive(false);
            if (previous.IsActive()) previous.Deactivate();
            else UnityEngine.Object.Destroy(previous.gameObject);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            screenGeneration++;
            CloseScreen();
        }
    }
}
