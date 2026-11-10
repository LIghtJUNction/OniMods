using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OniSim.Compatibility
{
    public sealed class NativeApi
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int MessageCallback(int id, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void InitializeDelegate(MessageCallback callback);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void VoidDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr MessageDelegate(int id, int length, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr MessagesDelegate(int id, int length, int count, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr BeginSaveDelegate(IntPtr size, int x, int y);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr PointerDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint VersionDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StatusDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate ulong GenerationDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CopyImageDelegate(IntPtr destination, int capacity);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GpuPathfinderInitGridDelegate(uint width, uint height, IntPtr navMask, uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GpuPathfinderEvaluateChoresDelegate(uint originCell, IntPtr chores, uint count);

        private static readonly object Gate = new object();
        private static NativeApi instance;
        private readonly IntPtr library;
        public readonly InitializeDelegate Initialize, TryInitialize;
        public readonly VoidDelegate Shutdown, EndSave, DebugCrash, ReleaseSystemInfo;
        public readonly MessageDelegate HandleMessage;
        public readonly MessagesDelegate HandleMessages;
        public readonly BeginSaveDelegate BeginSave;
        public readonly PointerDelegate AcquireSystemInfo, GetLastError, GetCapabilities, GetGrid, AcquireMulticomponentUpdate;
        public readonly VoidDelegate ReleaseMulticomponentUpdate;
        public readonly VersionDelegate GetAbiVersion;
        public readonly StatusDelegate GetLastStatus;
        public readonly GenerationDelegate GetGeneration;
        public readonly GenerationDelegate GetMulticomponentRevision;
        public readonly CopyImageDelegate CopyGridImage, CopyMulticomponentImage;
        public readonly GpuPathfinderInitGridDelegate GpuPathfinderInitGrid;
        public readonly GpuPathfinderEvaluateChoresDelegate GpuPathfinderEvaluateChores;
        public readonly string Path;

        private NativeApi(string path)
        {
            Path = path;
            library = dlopen(path, 2); // RTLD_NOW | RTLD_LOCAL; deliberately never unloaded.
            if (library == IntPtr.Zero) throw new InvalidOperationException("dlopen failed: " + LoaderError());
            Initialize = Load<InitializeDelegate>("SIM_Initialize");
            TryInitialize = Load<InitializeDelegate>("ONISIM_TryInitialize");
            Shutdown = Load<VoidDelegate>("SIM_Shutdown");
            HandleMessage = Load<MessageDelegate>("SIM_HandleMessage");
            HandleMessages = Load<MessagesDelegate>("SIM_HandleMessages");
            BeginSave = Load<BeginSaveDelegate>("SIM_BeginSave");
            EndSave = Load<VoidDelegate>("SIM_EndSave");
            DebugCrash = Load<VoidDelegate>("SIM_DebugCrash");
            AcquireSystemInfo = Load<PointerDelegate>("SYSINFO_Acquire");
            ReleaseSystemInfo = Load<VoidDelegate>("SYSINFO_Release");
            GetAbiVersion = Load<VersionDelegate>("ONISIM_GetAbiVersion");
            GetLastStatus = Load<StatusDelegate>("ONISIM_GetLastStatus");
            GetLastError = Load<PointerDelegate>("ONISIM_GetLastError");
            GetGeneration = Load<GenerationDelegate>("ONISIM_GetGeneration");
            GetMulticomponentRevision = TryLoad<GenerationDelegate>("ONISIM_GetMulticomponentRevision");
            GetCapabilities = Load<PointerDelegate>("ONISIM_GetCapabilities");
            GetGrid = Load<PointerDelegate>("ONISIM_GetGrid");
            CopyGridImage = Load<CopyImageDelegate>("ONISIM_CopyGridImage");
            CopyMulticomponentImage = TryLoad<CopyImageDelegate>("ONISIM_CopyMulticomponentImage");
            AcquireMulticomponentUpdate = TryLoad<PointerDelegate>("ONISIM_AcquireMulticomponentUpdate");
            ReleaseMulticomponentUpdate = TryLoad<VoidDelegate>("ONISIM_ReleaseMulticomponentUpdate");
            GpuPathfinderInitGrid = TryLoad<GpuPathfinderInitGridDelegate>("ONISIM_GpuPathfinder_InitGrid");
            GpuPathfinderEvaluateChores = TryLoad<GpuPathfinderEvaluateChoresDelegate>("ONISIM_GpuPathfinder_EvaluateChores");
            uint version = GetAbiVersion();
            if (version != 1) throw new NotSupportedException("Native ABI version " + version + " is not supported.");
        }

        public static NativeApi LoadExplicit(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
                throw new ArgumentException("An explicit absolute native library path is required.", "path");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || IntPtr.Size != 8 || !BitConverter.IsLittleEndian)
                throw new PlatformNotSupportedException("This compatibility facade supports Linux x64 little-endian only.");
            string full = System.IO.Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("Native library was not found.", full);
            lock (Gate)
            {
                if (instance != null)
                {
                    if (!string.Equals(instance.Path, full, StringComparison.Ordinal))
                        throw new InvalidOperationException("A different native library is already resident in this process.");
                    return instance;
                }
                instance = new NativeApi(full);
                return instance;
            }
        }

        internal static string ReadUtf8(IntPtr address)
        {
            if (address == IntPtr.Zero) return null;
            const int limit = 1024 * 1024;
            int length = 0;
            while (length < limit && Marshal.ReadByte(address, length) != 0) length++;
            if (length == limit) throw new InvalidDataException("Native UTF-8 text is not terminated within the supported limit.");
            var bytes = new byte[length];
            Marshal.Copy(address, bytes, 0, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        private T Load<T>(string symbol) where T : class
        {
            IntPtr address = dlsym(library, symbol);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException("Missing native export " + symbol + ": " + LoaderError());
            return Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        private T TryLoad<T>(string symbol) where T : class
        {
            IntPtr address = dlsym(library, symbol);
            return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }
        private static string LoaderError()
        { IntPtr p = dlerror(); return p == IntPtr.Zero ? "unknown loader error" : Marshal.PtrToStringAnsi(p); }

        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlopen(string file, int flags);
        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlsym(IntPtr handle, string name);
        [DllImport("libdl.so.2", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr dlerror();
    }
}
