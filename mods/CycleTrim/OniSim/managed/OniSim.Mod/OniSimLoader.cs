using NativeModule = Jlib.Runtime.NativeModule;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace OniSim.CycleTrim
{
    public enum LoaderState { Disabled, MappedUnverified, Refused }

    public sealed class LoaderResult
    {
        public LoaderState State { get; private set; }
        public string Message { get; private set; }
        internal LoaderResult(LoaderState state, string message) { State = state; Message = message; }
    }

    public static class OniSimLoader
    {
        public const string EnableVariable = "CYCLETRIM_ONISIM_ENABLE";
        public const string LibraryVariable = "CYCLETRIM_ONISIM_NATIVE_PATH";
        private static readonly object Gate = new object();
        private static string mappedPath;
        private static NativeModule targetModule;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ReadyDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FrameStateDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ErrorDelegate();
        private static ReadyDelegate frameReady;
        private static FrameStateDelegate frameState;
        private static ErrorDelegate nativeError;

        // Standalone / CycleTrim entry: OniSim is on by default.
        // CYCLETRIM_ONISIM_ENABLE=0|false|off remains an emergency kill switch.
        public static LoaderResult Configure(Assembly gameAssembly)
        {
            return Configure(gameAssembly, true);
        }

        public static LoaderResult Configure(Assembly gameAssembly, bool enabled)
        {
            lock (Gate)
            {
                if (mappedPath != null) return Mapped();
                if (!enabled)
                    return new LoaderResult(LoaderState.Disabled,
                        "Disabled by caller; no native module was opened and no mapping was inserted.");
                string setting = Environment.GetEnvironmentVariable(EnableVariable);
                if (!string.IsNullOrWhiteSpace(setting))
                {
                    setting = setting.Trim();
                    if (string.Equals(setting, "0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(setting, "off", StringComparison.OrdinalIgnoreCase))
                        return new LoaderResult(LoaderState.Disabled,
                            "Disabled by " + EnableVariable + "; no native module was opened and no mapping was inserted.");
                    if (!string.Equals(setting, "1", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(setting, "true", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(setting, "on", StringComparison.OrdinalIgnoreCase))
                        return new LoaderResult(LoaderState.Refused,
                            "Invalid " + EnableVariable + " value; use 0/false/off to disable or leave unset / 1/true/on for OniSim.");
                }
                try
                {
                    string path = Environment.GetEnvironmentVariable(LibraryVariable);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        string modDirectory = Path.GetDirectoryName(typeof(OniSimLoader).Assembly.Location);
                        if (string.Equals(Path.GetFileName(modDirectory), "implementation", StringComparison.OrdinalIgnoreCase))
                            modDirectory = Path.GetDirectoryName(modDirectory);
                        path = NativePlatform.BundledLibrary(modDirectory);
                    }
                    if (!Path.IsPathRooted(path))
                        throw new InvalidOperationException(LibraryVariable + " must be an absolute path when set.");
                    path = Path.GetFullPath(path);
                    NativePlatform.RequireSupported();
                    if (Type.GetType("Mono.Runtime") == null)
                        throw new PlatformNotSupportedException("OniSim takeover requires Unity Mono.");
                    OfficialProfile.ValidateAssembly(gameAssembly);
                    string monoPath = Jlib.Runtime.NativePlatform.MonoPath(gameAssembly.Location);
                    OfficialProfile.ValidateMono(monoPath);
                    // Validate the native Sim target prior to invoking Jlib's native component replacement.
                    targetModule = NativeModule.OpenTarget(path);
                    OfficialProfile.ValidateTarget(targetModule);
                    frameReady = targetModule.Bind<ReadyDelegate>("ONISIM_IsReady");
                    frameState = targetModule.Bind<FrameStateDelegate>("ONISIM_GetFrameState");
                    nativeError = targetModule.Bind<ErrorDelegate>("ONISIM_GetLastError");

                    // Game.PrepareSimulationData rebinds every array on publication.
                    // The standalone session API continues to request stable arrays.
                    if (targetModule.Bind<ReadyDelegate>("ONISIM_UseGamePublicationViews")() != 0)
                        throw new InvalidOperationException(Marshal.PtrToStringAnsi(nativeError()));

                    var replacement = Jlib.Runtime.JlibApi.ReplaceSimDll(gameAssembly, path);
                    if (!replacement.Succeeded)
                    {
                        return new LoaderResult(LoaderState.Refused,
                            "OniSim mapping refused: " + replacement.Message);
                    }
                    mappedPath = path;
                    return Mapped();
                }
                catch (Exception error)
                {
                    return new LoaderResult(LoaderState.Refused,
                        "OniSim mapping refused: " + error.GetType().Name + ": " + error.Message);
                }
            }
        }

        public static string GetMappedLibraryPath()
        {
            lock (Gate)
                return mappedPath;
        }

        public static bool IsFrameReady()
        {
            if (mappedPath == null) throw new InvalidOperationException("OniSim is not mapped.");
            // Coalesced completion events: no native mutex and no wait on the
            // render thread. The regular query is used only to report a fault.
            int state = frameState();
            if (state == 0 || state == 2) return true;
            if (state == 1) return false;
            frameReady();
            throw new InvalidOperationException("OniSim frame failed: " + Marshal.PtrToStringAnsi(nativeError()));
        }

        private static LoaderResult Mapped()
        {
            return new LoaderResult(LoaderState.MappedUnverified,
                "Assembly-scoped SimDLL mapping remains active and unverified; gameReady=false. Environment changes cannot remove or replace it. Restart is required.");
        }
    }
}
