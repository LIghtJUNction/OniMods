using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KMod;

namespace OniMods.JlibBootstrap
{
    // This assembly deliberately has no Jlib or implementation-type references.
    // KMod scans its types before OnLoad can check the external dependency.
    public sealed class BootstrapMod : UserMod2
    {
        private UserMod2 implementation;
        private Assembly implementationAssembly;
        private bool attempted, postLoaded;

        public override void OnLoad(Harmony harmony)
        {
            if (attempted) return;
            attempted = true;
            StartupFailure failure;
            MethodInfo registerConsumer;
            if (!DependencyLoader.TryLoad(Global.Instance?.modManager?.mods, out registerConsumer, out failure))
            { DependencyNotice.Enqueue(ConsumerName(), failure); return; }
            string phase = "implementation_load";
            try
            {
                string consumer = ConsumerName();
                string payload = Path.GetFullPath(Path.Combine(path, "implementation", consumer + ".dll"));
                if (!File.Exists(payload))
                { DependencyNotice.Enqueue(consumer, StartupFailure.MissingImplementation); return; }
                var loaded = Assembly.LoadFrom(payload);
                if (loaded.GetName().Name != consumer || !DependencyLoader.SamePath(loaded, payload))
                { DependencyNotice.Enqueue(consumer, StartupFailure.InvalidImplementation); return; }
                phase = "consumer_registration";
                registerConsumer.Invoke(null, new object[] { loaded, mod });
                phase = "implementation_discovery";
                var entries = loaded.GetTypes().Where(t => !t.IsAbstract && typeof(UserMod2).IsAssignableFrom(t)).ToArray();
                if (entries.Length != 1)
                { DependencyNotice.Enqueue(consumer, StartupFailure.InvalidImplementation); return; }
                phase = "implementation_creation";
                var candidate = (UserMod2)Activator.CreateInstance(entries[0]);
                candidate.assembly = loaded;
                candidate.path = path;
                candidate.mod = mod;
                phase = "implementation_on_load";
                candidate.OnLoad(harmony);
                implementationAssembly = loaded;
                implementation = candidate;
            }
            catch (Exception error)
            {
                StartupDiagnostics.Report(ConsumerName(), phase, error);
                DependencyNotice.Enqueue(ConsumerName(), StartupFailure.StartupFailed);
            }
        }

        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<Mod> mods)
        {
            DependencyNotice.Schedule();
            if (implementation == null || postLoaded) return;
            postLoaded = true;
            try
            {
                // KMod is currently enumerating userMod2Instances. Keep that map
                // untouched and record only the implementation assembly ownership.
                mod.loaded_mod_data.dlls.Add(implementationAssembly);
                implementation.OnAllModsLoaded(harmony, mods);
            }
            catch (Exception error)
            {
                StartupDiagnostics.Report(ConsumerName(), "implementation_post_load", error);
                DependencyNotice.Enqueue(ConsumerName(), StartupFailure.StartupFailed);
            }
        }

        private static string ConsumerName()
        {
            const string suffix = ".Bootstrap";
            string name = typeof(BootstrapMod).Assembly.GetName().Name;
            if (!name.EndsWith(suffix, StringComparison.Ordinal)) return "Mod";
            string consumer = name.Substring(0, name.Length - suffix.Length);
            if (consumer.Length == 0 || consumer == "." || consumer == ".." ||
                consumer.Any(c => !(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-')))
                return "Mod";
            return consumer;
        }
    }
}
