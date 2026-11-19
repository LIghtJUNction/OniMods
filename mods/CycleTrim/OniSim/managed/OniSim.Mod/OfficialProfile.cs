using NativeModule = Jlib.Runtime.NativeModule;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OniSim.CycleTrim
{
    internal static class OfficialProfile
    {
        private static readonly string[] Imports =
        {
            "SIM_Initialize", "SIM_Shutdown", "SIM_HandleMessage", "SIM_HandleMessages",
            "SIM_BeginSave", "SIM_EndSave", "SIM_DebugCrash", "SYSINFO_Acquire", "SYSINFO_Release",
            "ConduitTemperatureManager_Initialize", "ConduitTemperatureManager_Shutdown",
            "ConduitTemperatureManager_Add", "ConduitTemperatureManager_Set",
            "ConduitTemperatureManager_Remove", "ConduitTemperatureManager_Update", "ConduitTemperatureManager_Clear"
        };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();

        internal static InspectedGameProfile ValidateAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.GetName().Name != "Assembly-CSharp")
                throw new InvalidOperationException("The loader requires the loaded official Assembly-CSharp assembly.");
            if (!NativePlatform.IsLinux)
            {
                // Other platforms retain their existing, separately inspected U59 scope.
                PortableProfile.Validate(assembly);
                return null;
            }

            var profile = InspectedGameProfile.ForLinuxBuild(PortableProfile.ReadGameBuild(assembly));
            RequireDigest(assembly.Location, profile.AssemblySha256, "Assembly-CSharp");
            // Linux now verifies the complete import and message-layout inventory too.
            // A known file alone is not the ABI contract for a new game release.
            PortableProfile.Validate(assembly, profile.GameBuild);
            return profile;
        }

        internal static void ValidateRuntime(Assembly assembly, InspectedGameProfile profile, string monoPath)
        {
            if (!NativePlatform.IsLinux) return;
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string monoDigest = FileDigest(monoPath);
            string dataDirectory = Path.GetDirectoryName(Path.GetDirectoryName(assembly.Location));
            var candidates = new[]
            {
                Path.Combine(dataDirectory, "Plugins", "x86_64", "libSimDLL.so"),
                Path.Combine(dataDirectory, "Plugins", "libSimDLL.so")
            };
            bool found = false;
            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                profile.ValidateRuntimeDigests(monoDigest, FileDigest(path));
                found = true;
            }
            if (!found)
                throw new FileNotFoundException("The official simulator was not found in the game's Linux plugin layout.");
        }

        internal static void ValidateTarget(NativeModule module)
        {
            foreach (string entry in Imports) module.RequireSymbol(entry);
            module.RequireSymbol("ONISIM_TryInitialize");
            AbiVersion version = module.Bind<AbiVersion>("ONISIM_GetAbiVersion");
            if (version() != 1) throw new InvalidOperationException("The target does not implement native ABI version 1.");
        }

        private static void RequireDigest(string path, string expected, string description)
        {
            if (!string.Equals(FileDigest(path), expected, StringComparison.Ordinal))
                throw new InvalidOperationException(description + " does not match the statically inspected profile.");
        }

        private static string FileDigest(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
