using System;
using System.Linq;
using OniOnline.Runtime;
using OniOnline.Sdk;
using OniOnline.UI;

internal static class NativeModSyncDialogTests
{
    private static ModManifest Manifest(bool enabled)
    { return new ModManifest(new[] { new ModManifestEntry("test", "1", ModSyncPolicy.Required,
        ModDistributionSource.SteamWorkshop, 7, new string('a', 64), enabled) }); }

    internal static void Run(System.Action<bool, string> check)
    {
        int apply = 0, manage = 0, download = 0, cancel = 0;
        var required = Manifest(true);
        var disabled = ModSyncPlanner.Compare(required, Manifest(false));
        KScreenManager.Instance.Created.Clear();
        using (var view = NativeModSyncDialog.Open(disabled, null, () => manage++, () => apply++,
            () => cancel++, fp => fp == disabled.Fingerprint))
        {
            var screen = KScreenManager.Instance.Created.Single();
            check(view.IsOpen && screen.ConfirmText == "Apply and restart" && !screen.Body.richText,
                "production review shows the apply action for verified disabled content and keeps mod text literal");
            var previous = screen.Confirm.OnClick;
            screen.Configure.OnClick();
            previous();
            check(apply == 0 && view.IsOpen && KScreenManager.Instance.Created.Count == 1,
                "details reuse the modal and invalidate the previous overview callback");
            screen.Confirm.OnClick();
            check(screen.ConfirmText == "Apply and restart", "details return to the reviewed apply action");
            var confirm = screen.Confirm.OnClick;
            confirm(); confirm();
            check(apply == 1 && manage == 0 && download == 0 && cancel == 0 && !view.IsOpen &&
                !screen.gameObject.Active && screen.gameObject.Destroyed,
                "review confirmation applies once and closes its native control before invoking the callback");
        }

        var missing = ModSyncPlanner.Compare(required, new ModManifest(new ModManifestEntry[0]));
        string current = missing.Fingerprint;
        using (var view = NativeModSyncDialog.Open(missing, fp => download++, () => manage++, () => apply++,
            () => cancel++, fp => fp == current))
        {
            var screen = KScreenManager.Instance.Created.Last();
            check(screen.ConfirmText == "Download Mods", "missing content exposes download rather than apply");
            var stale = screen.Confirm.OnClick;
            current = disabled.Fingerprint;
            stale();
            check(download == 0 && apply == 1 && !view.IsOpen,
                "a replaced installed fingerprint invalidates the old download callback");
        }
        using (var view = NativeModSyncDialog.Open(disabled, fp => download++, () => manage++, () => apply++,
            () => cancel++, fp => fp == current))
        {
            var screen = KScreenManager.Instance.Created.Last();
            screen.Confirm.OnClick();
            check(apply == 2 && download == 0, "a refreshed plan requires and accepts its own apply confirmation");
        }
        using (var view = NativeModSyncDialog.Open(missing, fp => { if (fp == missing.Fingerprint) download++; },
            () => manage++, () => apply++, () => cancel++, fp => fp == missing.Fingerprint))
        {
            KScreenManager.Instance.Created.Last().Confirm.OnClick();
            check(download == 1 && apply == 2, "download receives the reviewed fingerprint and cannot invoke apply");
        }
        using (var view = NativeModSyncDialog.OpenStatus(missing.Fingerprint, "Downloading", () => cancel++,
            fp => fp == missing.Fingerprint, () => manage++))
        {
            var screen = KScreenManager.Instance.Created.Last();
            check(screen.ConfirmText == "Manage Mods" && view.SetStatus("Awaiting native installation") && screen.RenderCount == 1,
                "download status updates in place and does not reuse download consent as an apply action");
            screen.Cancel.OnClick();
            check(cancel == 1 && manage == 0 && apply == 2, "cancelling the status invokes only its cancellation callback");
        }
        var extra = ModSyncPlanner.Compare(new ModManifest(new ModManifestEntry[0]), required);
        using (var view = NativeModSyncDialog.Open(extra, null, () => manage++, () => apply++,
            () => cancel++, fp => fp == extra.Fingerprint))
        {
            var screen = KScreenManager.Instance.Created.Last();
            check(screen.ConfirmText == "Apply and restart" && screen.Body.Text.Contains("Disable mod: test"),
                "the same apply confirmation accurately describes reviewed mod removal");
        }
        check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name == "Assembly-CSharp" || assembly.GetName().Name == "0Harmony" ||
            assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)),
            "dialog host execution does not load game, Unity or Harmony implementation assemblies");
        Console.WriteLine("Mod sync dialog: production review, callback invalidation and status paths with host controls; no Unity/native input or rendering acceptance.");
    }
}
