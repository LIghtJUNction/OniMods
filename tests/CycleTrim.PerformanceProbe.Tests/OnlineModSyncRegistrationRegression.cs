using System;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using OniOnline.Sdk;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class OnlineModSyncRegistrationRegression
    {
        internal static void Run()
        {
            int assertions = 0;
            System.Action<bool, string> check = (condition, message) =>
            {
                assertions++;
                if (!condition) throw new InvalidOperationException(message);
            };
            check(!OnlineModSyncRegistration.TryRegister(Array.Empty<Assembly>(), out var absentError) &&
                absentError == null, "CycleTrim must load normally without OniOnline");
            check(!OnlineModSyncRegistration.TryRegister(new[] { typeof(OnlineModSyncRegistrationRegression).Assembly },
                    out var unrelatedError) && unrelatedError == null,
                "an unrelated loaded assembly must not be treated as OniOnline");

            // A loaded assembly with this identity but no public SDK is incompatible.
            var incompatible = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("OniOnline.Framework"), AssemblyBuilderAccess.Run);
            check(!OnlineModSyncRegistration.TryRegister(new[] { incompatible }, out var missingApi) &&
                missingApi == "mod_sync_api_unavailable", "missing SDK methods must fail explicitly");
            var framework = typeof(ModSyncDeclarations).Assembly;
            check(!OnlineModSyncRegistration.TryRegister(new Assembly[] { framework, incompatible }, out var duplicate) &&
                duplicate == "mod_sync_framework_ambiguous", "multiple framework providers must not be chosen arbitrarily");
            check(OnlineModSyncRegistration.TryRegister(new[] { framework }, out var registeredError) &&
                registeredError == null, "the real framework must accept CycleTrim's public declaration");

            var policies = ModSyncDeclarations.Snapshot();
            var extensions = ModSyncDeclarations.ContentSnapshot();
            check(policies.TryGetValue(OnlineModSyncRegistration.ModId, out var policy) &&
                policy == ModSyncPolicy.Required, "CycleTrim must remain required, never client-only");
            check(extensions.TryGetValue(OnlineModSyncRegistration.ModId, out var content) &&
                content.Count == 1 && content[0] == ".json", "public package JSON must enter the content digest");

            var required = new ModManifest(new[] { Entry("0.4.25", 'a', policy) });
            check(!ModSyncPlanner.Compare(required,
                    new ModManifest(new[] { Entry("0.4.25", 'b', policy) })).CanJoin,
                "declaring package content must not permit a different full content digest");
            check(!ModSyncPlanner.Compare(required,
                    new ModManifest(new[] { Entry("0.4.24", 'a', policy) })).CanJoin,
                "declaring package content must not permit a different version");
            check(!OnlineModSyncRegistration.TryRegister(new[] { framework }, out var frozenError) &&
                frozenError == "InvalidOperationException: mod_sync_declarations_frozen",
                "a frozen declaration must report the real SDK failure instead of claiming success");
            Console.WriteLine("PASS CycleTrim optional OniOnline content registration: " + assertions + " assertions");
        }

        private static ModManifestEntry Entry(string version, char digest, ModSyncPolicy policy)
        {
            return new ModManifestEntry(OnlineModSyncRegistration.ModId, version, policy,
                ModDistributionSource.Local, 0, new string(digest, 64), true);
        }
    }
}
