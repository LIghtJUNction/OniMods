using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OniSim.CycleTrim
{
    // Handles remain resident because Mono can cache P/Invoke function addresses.
    internal sealed class LinuxModule
    {
        private readonly IntPtr handle;
        private LinuxModule(IntPtr value) { handle = value; }

        internal static LinuxModule OpenAlreadyLoaded(string path) { return Open(path, 2 | 4); }
        internal static LinuxModule OpenTarget(string path) { return Open(path, 2); }

        private static LinuxModule Open(string path, int flags)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new FileNotFoundException("Native file is missing.", path);
            IntPtr handle = dlopen(path, flags); // RTLD_NOW; RTLD_NOLOAD is used for the existing Mono runtime.
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Could not obtain the requested native module: " + Error());
            return new LinuxModule(handle);
        }

        internal IntPtr RequireSymbol(string name)
        {
            IntPtr symbol = dlsym(handle, name);
            if (symbol == IntPtr.Zero) throw new EntryPointNotFoundException(name + ": " + Error());
            return symbol;
        }

        internal T Bind<T>(string name) where T : class
        { return Marshal.GetDelegateForFunctionPointer(RequireSymbol(name), typeof(T)) as T; }

        private static string Error()
        { IntPtr error = dlerror(); return error == IntPtr.Zero ? "unknown dynamic-loader error" : Marshal.PtrToStringAnsi(error); }

        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlopen(string path, int flags);
        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlsym(IntPtr module, string symbol);
        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlerror();
    }
}
