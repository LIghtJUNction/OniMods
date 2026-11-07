using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KMod;

namespace OniMods.JlibBootstrap
{
    internal enum StartupFailure
    {
        Missing, Disabled, Duplicate, NotInstalled, MissingLibrary, InvalidLibrary,
        ConflictingLibrary, MissingImplementation, InvalidImplementation, StartupFailed
    }

    internal static class DependencyLoader
    {
        internal const string StaticId = "LIghtJUNction.Jlib";
        private const int AbiVersion = 1;

        internal static bool TryLoad(IReadOnlyList<Mod> mods, out MethodInfo registerConsumer, out StartupFailure failure)
        {
            registerConsumer = null;
            failure = StartupFailure.InvalidLibrary;
            string phase = "provider_catalog";
            try
            {
                var providers = mods == null ? new Mod[0] : mods.Where(m => m != null && m.staticID == StaticId).ToArray();
                var enabled = providers.Where(m => m.IsEnabledForActiveDlc()).ToArray();
                if (enabled.Length == 0)
                { failure = providers.Length == 0 ? StartupFailure.Missing : StartupFailure.Disabled; return false; }
                if (enabled.Length != 1) { failure = StartupFailure.Duplicate; return false; }
                var provider = enabled[0];
                if (provider.status != Mod.Status.Installed) { failure = StartupFailure.NotInstalled; return false; }
                phase = "library_metadata";
                string expectedPath = Path.GetFullPath(Path.Combine(provider.ContentPath, "Jlib.dll"));
                if (!File.Exists(expectedPath)) { failure = StartupFailure.MissingLibrary; return false; }
                if (AssemblyName.GetAssemblyName(expectedPath).Name != "Jlib") return false;
                var existing = LoadedLibraries();
                if (existing.Length > 1 || existing.Length == 1 && !SamePath(existing[0], expectedPath))
                { failure = StartupFailure.ConflictingLibrary; return false; }
                phase = "library_load";
                var library = Assembly.LoadFrom(expectedPath);
                var loaded = LoadedLibraries();
                if (loaded.Length != 1 || !ReferenceEquals(loaded[0], library) || !SamePath(library, expectedPath))
                { failure = StartupFailure.ConflictingLibrary; return false; }
                if (library.GetName().Name != "Jlib") return false;
                phase = "abi_contract";
                var api = library.GetType("Jlib.Runtime.JlibApi", false);
                if (api == null || !ReferenceEquals(api.Assembly, library) || !Constant(api, "AbiVersion", typeof(int), AbiVersion) ||
                    !Constant(api, "StaticId", typeof(string), StaticId)) return false;
                registerConsumer = api.GetMethod("RegisterConsumer", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Assembly), typeof(Mod) }, null);
                if (registerConsumer == null || registerConsumer.ReturnType != typeof(void) || registerConsumer.ContainsGenericParameters)
                { registerConsumer = null; return false; }
                return true;
            }
            catch (Exception error) { StartupDiagnostics.Report("Jlib", phase, error); return false; }
        }

        private static Assembly[] LoadedLibraries()
        { return AppDomain.CurrentDomain.GetAssemblies().Where(a => string.Equals(a.GetName().Name, "Jlib", StringComparison.OrdinalIgnoreCase)).ToArray(); }

        private static bool Constant(Type type, string name, Type valueType, object expected)
        {
            var field = type == null ? null : type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field != null && field.IsLiteral && !field.IsInitOnly && field.FieldType == valueType &&
                Equals(field.GetRawConstantValue(), expected);
        }

        internal static bool SamePath(Assembly assembly, string expected)
        {
            return !assembly.IsDynamic && string.Equals(Path.GetFullPath(assembly.Location), Path.GetFullPath(expected),
                Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
