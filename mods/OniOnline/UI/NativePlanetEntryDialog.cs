using System;
using System.Globalization;
using HarmonyLib;
using OniOnline.Runtime;
using UnityEngine;

namespace OniOnline.UI
{
    // Joining works on the frontend and in the base game without a cluster map.
    // The owner supplies a connection/admission predicate; this view never joins,
    // creates or loads a colony by itself.
    internal sealed class NativePlanetEntryDialog : IDisposable
    {
        private const int MaximumNameCharacters = 64, MaximumBodyCharacters = 256, MaximumFailureCharacters = 512;
        private static NativePlanetEntryDialog current;
        private readonly Func<bool> isCurrent;
        private readonly System.Action joinOrCreate, loadOrSelect, cancel;
        private readonly string systemName, planetName, failureMessage, failureTitleKey;
        private ConfirmDialogScreen dialog;
        private readonly bool waiting;
        private bool disposed, opened;
        private int generation;

        internal bool IsOpen { get { return !disposed && dialog != null && dialog.IsActive() && dialog.IsScreenActive(); } }

        private NativePlanetEntryDialog(string systemName, string planetName, System.Action joinOrCreate,
            System.Action loadOrSelect, System.Action cancel, Func<bool> isCurrent)
        {
            ValidateName(systemName, nameof(systemName));
            if (planetName != null) ValidateName(planetName, nameof(planetName));
            this.joinOrCreate = joinOrCreate ?? throw new ArgumentNullException(nameof(joinOrCreate));
            this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            if (planetName == null && loadOrSelect == null) throw new ArgumentNullException(nameof(loadOrSelect));
            this.loadOrSelect = loadOrSelect;
            this.systemName = systemName; this.planetName = planetName;
        }

        internal static NativePlanetEntryDialog Open(string systemName, string planetName,
            System.Action joinOrCreate, System.Action loadOrSelect, System.Action cancel, Func<bool> isCurrent)
        {
            return ShowView(new NativePlanetEntryDialog(systemName, planetName, joinOrCreate, loadOrSelect, cancel, isCurrent));
        }

        internal static NativePlanetEntryDialog OpenWaiting(string systemName, System.Action cancel, Func<bool> isCurrent)
        {
            return ShowView(new NativePlanetEntryDialog(systemName, cancel, isCurrent));
        }

        private NativePlanetEntryDialog(string systemName, System.Action cancel, Func<bool> isCurrent)
        {
            ValidateName(systemName, nameof(systemName));
            this.systemName = systemName; waiting = true;
            this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            joinOrCreate = cancel;
            this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        }

        internal static NativePlanetEntryDialog OpenFailure(string message, System.Action retry,
            System.Action cancel, Func<bool> isCurrent, string titleKey = "system.create_failed")
        {
            return ShowView(new NativePlanetEntryDialog(message, retry, cancel, isCurrent, titleKey));
        }

        private NativePlanetEntryDialog(string message, System.Action retry, System.Action cancel, Func<bool> isCurrent, string titleKey)
        {
            ValidatePlainText(message, MaximumFailureCharacters, true, nameof(message));
            if (string.IsNullOrEmpty(titleKey) || titleKey.Length > 64)
                throw new ArgumentException("invalid_planet_entry_title_key", nameof(titleKey));
            for (int i = 0; i < titleKey.Length; i++)
                if (!(titleKey[i] >= 'a' && titleKey[i] <= 'z' || titleKey[i] == '.' || titleKey[i] == '_'))
                    throw new ArgumentException("invalid_planet_entry_title_key", nameof(titleKey));
            failureMessage = message; failureTitleKey = titleKey;
            joinOrCreate = retry ?? throw new ArgumentNullException(nameof(retry));
            this.cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            this.isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        }

        private static NativePlanetEntryDialog ShowView(NativePlanetEntryDialog view)
        {
            try
            {
                if (!view.Refresh()) return view;
                current?.Dispose();
                current = view;
                view.Show();
                return view;
            }
            catch { view.Dispose(); throw; }
        }

        // Called by the owner while waiting for a choice. Stale connection,
        // admission, selected planet, or native screen closes without side effects.
        internal bool Refresh()
        {
            if (disposed) return false;
            bool valid;
            try { valid = isCurrent(); }
            catch (Exception) { valid = false; }
            if (!valid || opened && (dialog == null || !dialog.IsActive() || !dialog.IsScreenActive())) Dispose();
            return !disposed;
        }

        private void Show()
        {
            var prefabs = ScreenPrefabs.Instance;
            var manager = KScreenManager.Instance;
            var root = Global.Instance == null ? null : Global.Instance.globalCanvas;
            if (prefabs == null || prefabs.ConfirmDialogScreen == null || manager == null || root == null)
                throw new InvalidOperationException("native_planet_entry_dialog_unavailable");
            string text = waiting ? string.Format(CultureInfo.CurrentCulture, T("planet_entry.waiting"), systemName)
                : failureMessage ?? (planetName == null
                ? string.Format(CultureInfo.CurrentCulture, T("planet_entry.choose"), systemName)
                : string.Format(CultureInfo.CurrentCulture, T("planet_entry.loaded"), planetName, systemName));
            if (text.Length > (failureMessage == null ? MaximumBodyCharacters : MaximumFailureCharacters))
                throw new InvalidOperationException("native_planet_entry_text_budget");
            string title = T(failureMessage == null ? "planet_entry.title" : failureTitleKey);
            ValidatePlainText(title, 96, false, "title");
            dialog = manager.StartScreen(prefabs.ConfirmDialogScreen.gameObject, root) as ConfirmDialogScreen;
            if (dialog == null) throw new InvalidOperationException("native_planet_entry_dialog_unavailable");
            dialog.gameObject.name = "OniOnlinePlanetEntryDialog";
            dialog.deactivateOnConfirmAction = false;
            dialog.deactivateOnCancelAction = false;
            dialog.deactivateOnConfigurableAction = false;
            ClearNativeClick("confirmButton");
            ClearNativeClick("cancelButton");
            ClearNativeClick("configurableButton");
            int stamp = ++generation;
            dialog.PopupConfirmDialog(text, () => Select(stamp, joinOrCreate), () => Select(stamp, cancel),
                loadOrSelect == null ? null : T(planetName == null ? "planet_entry.load" : "planet_entry.select"),
                loadOrSelect == null ? null : (System.Action)(() => Select(stamp, loadOrSelect)),
                title,
                T(waiting ? "planet_entry.back" : failureMessage != null ? "system.retry" : planetName == null ? "planet_entry.create" : "planet_entry.join"), T("planet_entry.back"));
            if (waiting)
            {
                var cancelButton = AccessTools.Field(typeof(ConfirmDialogScreen), "cancelButton")?.GetValue(dialog) as GameObject;
                if (cancelButton != null) cancelButton.SetActive(false);
            }
            var body = AccessTools.Field(typeof(ConfirmDialogScreen), "popupMessage")?.GetValue(dialog) as LocText;
            if (body == null) throw new InvalidOperationException("native_planet_entry_body_unavailable");
            body.richText = false;
            var heading = AccessTools.Field(typeof(ConfirmDialogScreen), "titleText")?.GetValue(dialog) as LocText;
            if (heading == null) throw new InvalidOperationException("native_planet_entry_title_unavailable");
            heading.richText = false;
            opened = true;
        }

        private void ClearNativeClick(string name)
        {
            var button = AccessTools.Field(typeof(ConfirmDialogScreen), name)?.GetValue(dialog) as GameObject;
            var control = button == null ? null : button.GetComponent<KButton>();
            if (control == null) throw new InvalidOperationException("native_planet_entry_button_unavailable");
            control.ClearOnClick();
        }

        private void Select(int stamp, System.Action action)
        {
            if (stamp != generation || !Refresh()) return;
            Dispose();
            action();
        }

        private static void ValidateName(string value, string argument)
        { ValidatePlainText(value, MaximumNameCharacters, false, argument); }

        private static void ValidatePlainText(string value, int maximum, bool multiline, string argument)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
                throw new ArgumentException("invalid_planet_entry_text", argument);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c) && !(multiline && (c == '\n' || c == '\r')) || char.IsSurrogate(c) &&
                    !(char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[++i])))
                    throw new ArgumentException("invalid_planet_entry_text", argument);
            }
        }

        private static string T(string key) { return OnlineText.Get(key); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; generation++;
            if (ReferenceEquals(current, this)) current = null;
            var previous = dialog; dialog = null;
            if (previous == null) return;
            // Release the native camera lock synchronously before the next native
            // create/load/selection screen is opened in this action's frame.
            previous.gameObject.SetActive(false);
            if (previous.IsActive()) previous.Deactivate();
            else UnityEngine.Object.Destroy(previous.gameObject);
        }
    }
}
