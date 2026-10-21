using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OniSim.CycleTrim
{
    internal sealed class MonoImageMapper
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AssemblyVisitor(IntPtr assembly, IntPtr userData);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AssemblyForeach(AssemblyVisitor callback, IntPtr userData);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AssemblyImage(IntPtr assembly);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ImageFilename(IntPtr image);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InsertMap(IntPtr image, IntPtr dll,
            IntPtr function, IntPtr targetDll, IntPtr targetFunction);

        private readonly AssemblyForeach foreachAssembly;
        private readonly AssemblyImage assemblyImage;
        private readonly ImageFilename imageFilename;
        private readonly InsertMap insertMap;

        internal MonoImageMapper(NativeModule mono)
        {
            foreachAssembly = mono.Bind<AssemblyForeach>("mono_assembly_foreach");
            assemblyImage = mono.Bind<AssemblyImage>("mono_assembly_get_image");
            imageFilename = mono.Bind<ImageFilename>("mono_image_get_filename");
            insertMap = mono.Bind<InsertMap>("mono_dllmap_insert");
        }

        internal IntPtr FindImage(string expectedFilename)
        {
            IntPtr found = IntPtr.Zero;
            Exception failure = null;
            AssemblyVisitor visitor = (assembly, ignored) =>
            {
                try
                {
                    IntPtr image = assemblyImage(assembly);
                    IntPtr name = image == IntPtr.Zero ? IntPtr.Zero : imageFilename(image);
                    if (name != IntPtr.Zero && string.Equals(Path.GetFullPath(ReadUtf8(name)), expectedFilename,
                        NativePlatform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        if (found != IntPtr.Zero && found != image) throw new InvalidOperationException("More than one matching MonoImage exists.");
                        found = image;
                    }
                }
                catch (Exception error) { failure = error; }
            };
            foreachAssembly(visitor, IntPtr.Zero);
            GC.KeepAlive(visitor);
            if (failure != null) throw new InvalidOperationException("Could not identify the loaded MonoImage.", failure);
            return found;
        }

        internal void Insert(IntPtr image, string targetPath)
        {
            if (image == IntPtr.Zero) throw new ArgumentException("Global DLL mappings are prohibited.", "image");
            using (var source = new PinnedUtf8("SimDLL"))
            using (var target = new PinnedUtf8(targetPath))
                insertMap(image, source.Pointer, IntPtr.Zero, target.Pointer, IntPtr.Zero);
        }

        private static string ReadUtf8(IntPtr address)
        {
            int length = 0;
            while (length < 65536 && Marshal.ReadByte(address, length) != 0) length++;
            if (length == 65536) throw new InvalidDataException("Mono image filename is not terminated.");
            var bytes = new byte[length]; Marshal.Copy(address, bytes, 0, length);
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        private sealed class PinnedUtf8 : IDisposable
        {
            private GCHandle pin;
            internal IntPtr Pointer { get { return pin.AddrOfPinnedObject(); } }
            internal PinnedUtf8(string value)
            { pin = GCHandle.Alloc(Encoding.UTF8.GetBytes(value + "\0"), GCHandleType.Pinned); }
            public void Dispose() { if (pin.IsAllocated) pin.Free(); }
        }
    }
}
