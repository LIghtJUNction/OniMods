using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OniSim.CycleTrim
{
    internal static class OfficialProfile
    {
        internal const string AssemblySha256 = "6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1";
        internal const string MonoSha256 = "60bf98b369d3a48cd93fab678dc8c6d37f61adea00f942a59e418d1d9db6bf44";
        private static readonly string[] Imports =
        {
            "SIM_Initialize", "SIM_Shutdown", "SIM_HandleMessage", "SIM_HandleMessages",
            "SIM_BeginSave", "SIM_EndSave", "SIM_DebugCrash", "SYSINFO_Acquire", "SYSINFO_Release",
            "ConduitTemperatureManager_Initialize", "ConduitTemperatureManager_Shutdown",
            "ConduitTemperatureManager_Add", "ConduitTemperatureManager_Set",
            "ConduitTemperatureManager_Remove", "ConduitTemperatureManager_Update", "ConduitTemperatureManager_Clear"
        };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();

        internal static void ValidateAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.GetName().Name != "Assembly-CSharp")
                throw new InvalidOperationException("The loader requires the loaded official Assembly-CSharp assembly.");
            RequireDigest(assembly.Location, AssemblySha256, "Assembly-CSharp");
        }

        internal static string MonoPath(string assemblyPath)
        {
            string managed = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            string data = Path.GetDirectoryName(managed);
            return Path.Combine(data, "MonoBleedingEdge", "x86_64", "libmonobdwgc-2.0.so");
        }

        internal static void ValidateMono(string path) { RequireDigest(path, MonoSha256, "Unity Mono runtime"); }

        internal static void ValidateTarget(LinuxModule module)
        {
            foreach (string entry in Imports) module.RequireSymbol(entry);
            module.RequireSymbol("ONISIM_TryInitialize");
            AbiVersion version = module.Bind<AbiVersion>("ONISIM_GetAbiVersion");
            if (version() != 1) throw new InvalidOperationException("The target does not implement native ABI version 1.");
        }

        private static void RequireDigest(string path, string expected, string description)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new InvalidOperationException(description + " does not match the statically inspected profile.");
            }
        }
    }
}
