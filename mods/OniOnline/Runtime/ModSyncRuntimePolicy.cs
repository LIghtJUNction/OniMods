using System;
using System.Collections.Generic;

namespace OniOnline.Runtime
{
    // Pure lifecycle and presentation decisions shared by the Unity adapter and
    // host tests. It never mutates native mod settings or exposes raw errors.
    internal static class ModSyncRuntimePolicy
    {
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
                    statuses[i].Stage == ModContentSyncStage.AwaitingActivation)) return true;
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
