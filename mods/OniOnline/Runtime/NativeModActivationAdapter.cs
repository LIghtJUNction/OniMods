using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Runtime
{
    // Applies only a previously reviewed, content-matching request. The caller
    // must obtain explicit UI confirmation before invoking this adapter.
    internal static class NativeModActivationAdapter
    {
        internal static bool TryApply(KMod.Manager manager, NativeModActivationRequest request, out string error)
        {
            error = "";
            if (manager == null || request == null || request.Changes == null || request.Changes.Count == 0)
            { error = "native_mod_activation_invalid"; return false; }
            if (!NativeModCatalogLifecycle.Idle(manager))
            { error = "native_mod_loader_busy"; return false; }

            var targets = new List<NativeModActivationTarget>();
            foreach (var mod in manager.mods.ToArray())
            {
                if (mod == null) { error = "invalid_native_mod_record"; return false; }
                string id = string.IsNullOrEmpty(mod.staticID) ? mod.label.defaultStaticID : mod.staticID;
                targets.Add(new NativeModActivationTarget(id, mod.status == KMod.Mod.Status.Installed,
                    mod.IsEnabledForActiveDlc(), mod.loaded_content != (KMod.Content)0,
                    enabled => mod.SetEnabledForActiveDlc(enabled)));
            }
            return NativeModActivationOperations.TryApply(targets, request.Changes, manager.Save, out error);
        }
    }
}
