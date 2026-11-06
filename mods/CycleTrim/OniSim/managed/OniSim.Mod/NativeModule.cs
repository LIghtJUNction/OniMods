using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace OniSim.CycleTrim
{
    // Handles remain resident because Mono can cache P/Invoke function addresses.
    internal sealed class NativeModule
    {
        private readonly IntPtr handle;
        private NativeModule(IntPtr value) { handle = value; }

        internal static NativeModule OpenAlreadyLoaded(string path) { return Open(path, true); }
        internal static NativeModule OpenTarget(string path) { return Open(path, false); }

        private static NativeModule Open(string path, bool alreadyLoaded)
        {
            if (!Path.IsPathRooted(path) || !File.Exists(path))
                throw new FileNotFoundException("Native file is missing.", path);
            NativePlatform.RequireSupported();
            IntPtr module;
            if (NativePlatform.IsWindows)
            {
                module = alreadyLoaded ? GetModuleHandle(path) : LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x800);
                if (module == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open native module " + path);
            }
            else
            {
                int flags = 2 | (alreadyLoaded ? (NativePlatform.IsMac ? 0x10 : 4) : 0);
                module = NativePlatform.IsMac ? MacOpen(path, flags) : LinuxOpen(path, flags);
                if (module == IntPtr.Zero)
                    throw new InvalidOperationException("Could not open native module " + path + ": " + Error());
            }
            return new NativeModule(module);
        }

        internal IntPtr RequireSymbol(string name)
        {
            IntPtr symbol = NativePlatform.IsWindows ? GetProcAddress(handle, name) :
                (NativePlatform.IsMac ? MacSymbol(handle, name) : LinuxSymbol(handle, name));
            if (symbol == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return symbol;
        }

        internal T Bind<T>(string name) where T : class
        { return Marshal.GetDelegateForFunctionPointer(RequireSymbol(name), typeof(T)) as T; }

        private static string Error()
        {
            IntPtr error = NativePlatform.IsMac ? MacError() : LinuxError();
            return error == IntPtr.Zero ? "unknown dynamic-loader error" : Marshal.PtrToStringAnsi(error);
        }

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string path);
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("libdl.so.2", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LinuxOpen(string path, int flags);
        [DllImport("libdl.so.2", EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LinuxSymbol(IntPtr module, string name);
        [DllImport("libdl.so.2", EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr LinuxError();
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlopen", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MacOpen(string path, int flags);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MacSymbol(IntPtr module, string name);
        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlerror", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MacError();
    }
}
