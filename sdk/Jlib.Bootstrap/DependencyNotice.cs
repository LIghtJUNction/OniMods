using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OniMods.JlibBootstrap
{
    internal static class DependencyNotice
    {
        private const string QueueKey = "OniMods.JlibBootstrap.Notices.v1";
        private const string RunnerKey = "OniMods.JlibBootstrap.NoticeRunner.v1";

        internal static void Enqueue(string consumer, StartupFailure failure)
        {
            Console.WriteLine("[Jlib bootstrap] " + consumer + " startup blocked: " + failure);
            var domain = AppDomain.CurrentDomain;
            lock (domain)
            {
                var notices = domain.GetData(QueueKey) as List<string>;
                if (notices == null) { notices = new List<string>(); domain.SetData(QueueKey, notices); }
                string text = consumer + ": " + Reason(failure);
                if (!notices.Contains(text)) notices.Add(text);
            }
            Schedule();
        }

        internal static void Schedule()
        {
            var domain = AppDomain.CurrentDomain;
            lock (domain)
            {
                var notices = domain.GetData(QueueKey) as List<string>;
                if (notices == null || notices.Count == 0 || Equals(domain.GetData(RunnerKey), true) || Global.Instance == null) return;
                domain.SetData(RunnerKey, true);
                try { Global.Instance.StartCoroutine(ShowWhenReady()); }
                catch (Exception) { domain.SetData(RunnerKey, false); }
            }
        }

        private static IEnumerator ShowWhenReady()
        {
            var domain = AppDomain.CurrentDomain;
            try
            {
                // Coalesce consumers loaded during the same native startup pass.
                yield return null;
                while (true)
                {
                    while (MainMenu.Instance == null || !MainMenu.Instance.IsActive() || Global.Instance == null ||
                        Global.Instance.globalCanvas == null || ScreenPrefabs.Instance?.ConfirmDialogScreen == null)
                        yield return null;
                    string[] messages;
                    lock (domain)
                    {
                        var notices = domain.GetData(QueueKey) as List<string>;
                        messages = notices == null ? new string[0] : notices.ToArray();
                    }
                    if (messages.Length == 0) yield break;
                    bool shown = false;
                    ConfirmDialogScreen dialog = null;
                    try
                    {
                        dialog = KScreenManager.AddChild(Global.Instance.globalCanvas,
                            ScreenPrefabs.Instance.ConfirmDialogScreen.gameObject).GetComponent<ConfirmDialogScreen>();
                        dialog.PopupConfirmDialog(string.Join("\n", messages) +
                            "\n\nInstall and enable a compatible Jlib, check the affected mod installations, then restart the game to complete startup.",
                            () => { }, null, title_text: "Mod startup");
                        dialog.Activate();
                        shown = true;
                    }
                    catch (Exception)
                    {
                        if (dialog != null) UnityEngine.Object.Destroy(dialog.gameObject);
                    }
                    if (shown)
                        lock (domain)
                        {
                            var notices = domain.GetData(QueueKey) as List<string>;
                            if (notices != null) foreach (string message in messages) notices.Remove(message);
                        }
                    // Retain the queue and the single runner through a transient UI failure.
                    yield return null;
                }
            }
            finally { lock (domain) domain.SetData(RunnerKey, false); }
        }

        private static string Reason(StartupFailure failure)
        {
            switch (failure)
            {
                case StartupFailure.Missing: return "Jlib is not installed.";
                case StartupFailure.Disabled: return "Jlib is not enabled.";
                case StartupFailure.Duplicate: return "More than one Jlib copy is enabled.";
                case StartupFailure.NotInstalled: return "Jlib installation is incomplete.";
                case StartupFailure.MissingLibrary: return "Jlib installation is missing its library.";
                case StartupFailure.InvalidLibrary: return "Jlib is incompatible or could not be loaded.";
                case StartupFailure.ConflictingLibrary: return "A conflicting Jlib copy is already loaded. Restart after fixing the mod list.";
                case StartupFailure.MissingImplementation: return "The mod installation is incomplete.";
                case StartupFailure.InvalidImplementation: return "The mod implementation could not be verified.";
                default: return "The mod could not start.";
            }
        }
    }
}
