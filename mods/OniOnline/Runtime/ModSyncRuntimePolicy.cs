using System;
using System.Collections.Generic;
using OniOnline.Sdk;

namespace OniOnline.Runtime
{
    internal enum ModSyncReviewAction { Manage, Download, ApplyAndRestart }

    // Pure lifecycle and presentation decisions shared by the Unity adapter and
    // host tests. It never mutates native mod settings or exposes raw errors.
    internal static class ModSyncRuntimePolicy
    {
        internal static ModSyncReviewAction PrimaryAction(ModSyncPlan plan, bool canDownload, bool canApply)
        {
            if (plan == null || plan.CanJoin) return ModSyncReviewAction.Manage;
            if (canDownload)
                foreach (var change in plan.Changes)
                    if (change.CanDownload && !SteamModContentSync.ContentsMatch(change.Expected, change.Installed))
                        return ModSyncReviewAction.Download;
            if (canApply && NativeModActivationRequest.TryCreate(plan, plan.Fingerprint,
                plan.RequiredManifest, plan.InstalledManifest, out _, out _))
                return ModSyncReviewAction.ApplyAndRestart;
            return ModSyncReviewAction.Manage;
        }

        internal static void DisposeContentSync(ref SteamModContentSync sync)
        {
            var old = sync;
            sync = null;
            old?.Dispose();
        }

        internal static bool NeedsNativeRefresh(IReadOnlyList<ModContentSyncStatus> statuses)
        {
            if (statuses == null) return false;
            for (int i = 0; i < statuses.Count; i++)
                if (statuses[i] != null && (statuses[i].Stage == ModContentSyncStage.AwaitingNativeInstall ||
                    statuses[i].Stage == ModContentSyncStage.AwaitingActivation ||
                    statuses[i].Error == "mod_sync_plan_changed" || statuses[i].Error == "native_mod_content_changed" ||
                    statuses[i].Error == "native_mod_install_timeout")) return true;
            return false;
        }

        internal static string ErrorKey(string error)
        {
            switch (error)
            {
                case "steam_item_identity_mismatch": return "mod_sync.error_item";
                case "steam_details_failed":
                case "steam_subscribe_failed":
                case "steam_download_rejected":
                case "steam_state_failed":
                case "steam_sdk_failed":
                case "steam_download_callback_failed": return "mod_sync.error_network";
                case "steam_request_timeout": return "mod_sync.error_timeout";
                case "native_mod_install_timeout": return "mod_sync.error_install";
                case "local_mod_not_distributable":
                case "extra_required_mod": return "mod_sync.error_local";
                case "mod_sync_plan_changed":
                case "native_mod_catalog_changed":
                case "native_mod_content_not_ready":
                case "native_mod_content_changed": return "mod_sync.error_changed";
                case "native_mod_loader_busy": return "mod_sync.activation_wait";
                case "native_mod_settings_write_failed":
                case "native_mod_activation_invalid":
                case "native_mod_activation_noop":
                case "native_mod_activation_conflict":
                case "native_mod_not_found":
                case "native_mod_identity_ambiguous":
                case "native_mod_not_installed": return "mod_sync.activation_failed";
                default: return "mod_sync.error_generic";
            }
        }
    }
}
