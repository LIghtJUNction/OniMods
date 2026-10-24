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

            var candidates = new Dictionary<string, List<KMod.Mod>>(StringComparer.Ordinal);
            foreach (var mod in manager.mods.ToArray())
            {
                if (mod == null) { error = "invalid_native_mod_record"; return false; }
                string id = string.IsNullOrEmpty(mod.staticID) ? mod.label.defaultStaticID : mod.staticID;
                if (!candidates.TryGetValue(id, out var list)) candidates.Add(id, list = new List<KMod.Mod>());
                list.Add(mod);
            }

            var changes = new List<System.Tuple<KMod.Mod, bool>>();
            foreach (var change in request.Changes)
            {
                if (change == null || !candidates.TryGetValue(change.Id, out var list))
                { error = "native_mod_not_found"; return false; }
                var active = list.Where(mod => mod.IsEnabledForActiveDlc() || mod.loaded_content != (KMod.Content)0).ToList();
                KMod.Mod target;
                if (active.Count == 1) target = active[0];
                else if (active.Count == 0 && list.Count == 1) target = list[0];
                else { error = "native_mod_identity_ambiguous"; return false; }
                if (target.status != KMod.Mod.Status.Installed)
                { error = "native_mod_not_installed"; return false; }
                bool current = target.IsEnabledForActiveDlc();
                if (current != change.Enable) changes.Add(System.Tuple.Create(target, current));
            }
            if (changes.Count == 0) { error = "native_mod_activation_noop"; return false; }

            try
            {
                foreach (var change in request.Changes)
                {
                    var list = candidates[change.Id];
                    var active = list.Where(mod => mod.IsEnabledForActiveDlc() || mod.loaded_content != (KMod.Content)0).ToList();
                    var target = active.Count == 1 ? active[0] : list[0];
                    target.SetEnabledForActiveDlc(change.Enable);
                }
                if (manager.Save()) return true;
                error = "native_mod_settings_write_failed";
            }
            catch (Exception)
            { error = "native_mod_settings_write_failed"; }

            foreach (var change in changes)
                try { change.Item1.SetEnabledForActiveDlc(change.Item2); } catch (Exception) { }
            try { manager.Save(); } catch (Exception) { }
            return false;
        }
    }
}
