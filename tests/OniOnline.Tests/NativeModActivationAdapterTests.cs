using System;
using System.Collections.Generic;
using OniOnline.Runtime;
using OniOnline.Sdk;

// Host-only native adapter doubles. No game or Unity assembly is loaded.
namespace KMod
{
    internal sealed class Mod
    {
        internal NativeModRecord Record;
        internal bool Malformed;
        internal Action<bool> OnSetEnabled;

        internal void SetEnabledForActiveDlc(bool enabled)
        {
            OnSetEnabled?.Invoke(enabled);
            Record.ConfiguredEnabled = enabled;
        }
    }

    internal sealed class Manager
    {
        internal readonly List<Mod> mods = new List<Mod>();
        internal bool Busy;
        internal long EpochValue = 1;
        internal Func<bool> SaveBehavior = () => true;

        internal bool Save() { return SaveBehavior(); }
    }
}

namespace OniOnline.Runtime
{
    internal static class NativeModCatalogLifecycle
    {
        internal static bool Idle(KMod.Manager manager) { return !manager.Busy; }
        internal static long Epoch(KMod.Manager manager) { return manager.EpochValue; }
    }

    internal static class NativeKModCatalogSource
    {
        internal static NativeModRecord ReadMod(KMod.Mod mod)
        {
            if (mod.Malformed) throw new InvalidOperationException("invalid_native_workshop_id");
            return mod.Record;
        }
    }
}

internal static class NativeModActivationAdapterTests
{
    internal static void Run(Action<bool, string> check)
    {
        var entry = new ModManifestEntry("test", "1", ModSyncPolicy.Required,
            ModDistributionSource.SteamWorkshop, 7, new string('a', 64), true);
        var disabled = new ModManifestEntry("test", "1", ModSyncPolicy.Required,
            ModDistributionSource.SteamWorkshop, 7, new string('a', 64), false);
        var required = new ModManifest(new[] { entry });
        var installed = new ModManifest(new[] { disabled });
        var plan = ModSyncPlanner.Compare(required, installed);
        check(NativeModActivationRequest.TryCreate(plan, plan.Fingerprint, required, installed,
            out var request, out var error), "a reviewed disabled package yields an activation request");

        int writes = 0, saves = 0;
        var good = new KMod.Mod
        {
            Record = new NativeModRecord
            {
                Id = "test", Version = "1", Source = ModDistributionSource.SteamWorkshop,
                WorkshopId = 7, Policy = ModSyncPolicy.Required, Installed = true
            },
            OnSetEnabled = _ => writes++
        };
        var manager = new KMod.Manager();
        manager.mods.Add(good);
        manager.SaveBehavior = () => { saves++; return true; };
        manager.mods.Add(new KMod.Mod { Malformed = true });

        check(!NativeModActivationAdapter.TryApply(manager, request, out error) &&
            error == "native_mod_catalog_failed" && writes == 0 && saves == 0,
            "malformed second package fails closed before writing the first package");
        manager.mods.RemoveAt(1);
        manager.Busy = true;
        check(!NativeModActivationAdapter.TryApply(manager, request, out error) &&
            error == "native_mod_loader_busy" && writes == 0 && saves == 0,
            "a busy native loader rejects activation before mutation");
        manager.Busy = false;
        check(NativeModActivationAdapter.TryApply(manager, request, out error) &&
            error == "" && good.Record.ConfiguredEnabled && writes == 1 && saves == 1,
            "valid reviewed activation still enables and saves the exact package");
    }
}
