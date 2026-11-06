using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OniSim.CycleTrim
{
    internal static class NativePlatform
    {
        internal static bool IsWindows { get { return RuntimeInformation.IsOSPlatform(OSPlatform.Windows); } }
        internal static bool IsMac { get { return RuntimeInformation.IsOSPlatform(OSPlatform.OSX); } }
        internal static bool IsLinux { get { return RuntimeInformation.IsOSPlatform(OSPlatform.Linux); } }
        internal static string Id { get { RequireSupported(); return IsWindows ? "windows-x64" : IsMac ? "macos-x64" : "linux-x64"; } }
        internal static string LibraryName { get { return IsWindows ? "oni_sim_core.dll" : IsMac ? "liboni_sim_core.dylib" : "liboni_sim_core.so"; } }

        internal static void RequireSupported()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || !BitConverter.IsLittleEndian ||
                !(IsWindows || IsMac || IsLinux))
                throw new PlatformNotSupportedException("OniSim requires a Windows, macOS or Linux x64 game process.");
        }

        internal static string BundledLibrary(string directory)
        { return Path.Combine(directory, "OniSim", Id, LibraryName); }

        internal static string MonoPath(string assemblyPath)
        {
            RequireSupported();
            string data = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(assemblyPath)));
            string root = Path.GetDirectoryName(data);
            if (IsMac)
            {
                var ancestor = new DirectoryInfo(data);
                while (ancestor != null && ancestor.Name != "Contents") ancestor = ancestor.Parent;
                if (ancestor == null) throw new InvalidOperationException("The game assembly is outside a macOS app bundle.");
                root = ancestor.FullName;
            }
            string[] candidates = IsWindows ? new[] {
                Path.Combine(root, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll"),
                Path.Combine(data, "MonoBleedingEdge", "EmbedRuntime", "mono-2.0-bdwgc.dll")
            } : IsMac ? new[] {
                Path.Combine(root, "Frameworks", "MonoBleedingEdge", "MonoEmbedRuntime", "osx", "libmonobdwgc-2.0.dylib"),
                Path.Combine(root, "Frameworks", "MonoBleedingEdge", "EmbedRuntime", "libmonobdwgc-2.0.dylib"),
                Path.Combine(data, "MonoBleedingEdge", "EmbedRuntime", "libmonobdwgc-2.0.dylib")
            } : new[] {
                Path.Combine(data, "MonoBleedingEdge", "x86_64", "libmonobdwgc-2.0.so")
            };
            foreach (string path in candidates) if (File.Exists(path)) return path;
            throw new FileNotFoundException("The game's Unity Mono runtime was not found in its platform layout.");
        }
    }
}
