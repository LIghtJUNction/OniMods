using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using OniOnline.Sdk;

namespace OniOnline.Runtime
{
    internal static class NativeKModCatalogSource
    {
        internal static NativeModCatalog Create(KMod.Manager manager, IReadOnlyDictionary<string, ModSyncPolicy> localPolicies = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>> contentExtensions = null)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            var policies = new Dictionary<string, ModSyncPolicy>(StringComparer.Ordinal);
            if (localPolicies != null)
                foreach (var pair in localPolicies)
                {
                    // The manifest boundary applies the same exact native ID and enum validation.
                    new ModManifestEntry(pair.Key, "0", pair.Value, ModDistributionSource.Local, 0, new string('0', 64));
                    if (pair.Key == ModSyncDeclarations.FrameworkModId && pair.Value != ModSyncPolicy.Required)
                        throw new ArgumentException("framework_mod_must_be_required");
                    policies.Add(pair.Key, pair.Value);
                }
            return new NativeModCatalog(() => Read(manager, id => policies.TryGetValue(id, out var policy) ? policy : ModSyncPolicy.Required,
                    id => contentExtensions != null && contentExtensions.TryGetValue(id, out var extensions) ? extensions : null),
                () => NativeModCatalogLifecycle.Epoch(manager), () => NativeModCatalogLifecycle.Idle(manager));
        }

        // Policy is local trusted configuration. An unrecognized mod is network-required by default.
        internal static IEnumerable<NativeModRecord> Read(KMod.Manager manager, Func<string, ModSyncPolicy> policy = null,
            Func<string, IReadOnlyCollection<string>> contentExtensions = null)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            foreach (var mod in manager.mods.ToArray())
                yield return ReadMod(mod, policy, contentExtensions);
        }

        internal static NativeModRecord ReadMod(KMod.Mod mod, Func<string, ModSyncPolicy> policy = null,
            Func<string, IReadOnlyCollection<string>> contentExtensions = null)
        {
            if (mod == null) throw new InvalidOperationException("invalid_native_mod_record");
            string id = string.IsNullOrEmpty(mod.staticID) ? mod.label.defaultStaticID : mod.staticID;
            bool workshop = mod.label.distribution_platform == KMod.Label.DistributionPlatform.Steam;
            ulong workshopId = 0;
            if (workshop && (!ulong.TryParse(mod.label.id, NumberStyles.None, CultureInfo.InvariantCulture, out workshopId) || workshopId == 0))
                throw new InvalidOperationException("invalid_native_workshop_id");
            return new NativeModRecord
            {
                Id = id,
                Version = string.IsNullOrEmpty(mod.packagedModInfo?.version)
                    ? mod.label.version.ToString(CultureInfo.InvariantCulture) : mod.packagedModInfo.version,
                ContentRoot = mod.ContentPath,
                ContentExtensions = contentExtensions?.Invoke(id),
                Source = workshop ? ModDistributionSource.SteamWorkshop : ModDistributionSource.Local,
                WorkshopId = workshopId,
                Policy = policy == null ? ModSyncPolicy.Required : policy(id),
                ConfiguredEnabled = mod.IsEnabledForActiveDlc(),
                Loaded = mod.loaded_content != (KMod.Content)0,
                Installed = mod.status == KMod.Mod.Status.Installed
            };
        }
    }

    internal static class NativeModCatalogLifecycle
    {
        private sealed class State
        {
            public State() { }
            internal int Busy;
            internal long Generation;
            internal string Metadata = "";
        }
        private static readonly ConditionalWeakTable<KMod.Manager, State> States = new ConditionalWeakTable<KMod.Manager, State>();
        private static readonly FieldInfo Loading = typeof(KMod.Manager).GetField("mod_load_in_progress", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static bool Idle(KMod.Manager manager)
        {
            if (Loading == null || Loading.FieldType != typeof(bool)) return false;
            return States.GetOrCreateValue(manager).Busy == 0 && !(bool)Loading.GetValue(manager);
        }

        internal static long Epoch(KMod.Manager manager)
        {
            var state = States.GetOrCreateValue(manager);
            var metadata = new StringBuilder();
            foreach (var mod in manager.mods.ToArray())
            {
                if (mod == null) throw new InvalidOperationException("invalid_native_mod_record");
                // This private comparison token is never placed in a manifest or diagnostic.
                Append(metadata, mod.staticID); Append(metadata, mod.label.id);
                Append(metadata, mod.ContentPath); Append(metadata, mod.packagedModInfo?.version);
                metadata.Append((int)mod.label.distribution_platform).Append(':').Append(mod.label.version).Append(':')
                    .Append((int)mod.status).Append(':').Append((int)mod.loaded_content).Append(':')
                    .Append(mod.IsEnabledForActiveDlc() ? '1' : '0').Append(';');
            }
            string current = metadata.ToString();
            if (state.Metadata != current) { state.Metadata = current; state.Generation++; }
            return state.Generation;
        }

        private static void Append(StringBuilder target, string value)
        { value = value ?? ""; target.Append(value.Length).Append(':').Append(value).Append(';'); }

        [HarmonyPatch(typeof(KMod.Manager), nameof(KMod.Manager.Load))]
        private static class LoadingPatch
        {
            private static void Prefix(KMod.Manager __instance)
            { var state = States.GetOrCreateValue(__instance); state.Busy++; state.Generation++; }
            private static Exception Finalizer(KMod.Manager __instance, Exception __exception)
            { var state = States.GetOrCreateValue(__instance); state.Busy--; state.Generation++; return __exception; }
        }

        [HarmonyPatch(typeof(KMod.Manager), nameof(KMod.Manager.Unload))]
        private static class UnloadingPatch
        {
            private static void Prefix(KMod.Manager __instance)
            { var state = States.GetOrCreateValue(__instance); state.Busy++; state.Generation++; }
            private static Exception Finalizer(KMod.Manager __instance, Exception __exception)
            { var state = States.GetOrCreateValue(__instance); state.Busy--; state.Generation++; return __exception; }
        }
    }
}
