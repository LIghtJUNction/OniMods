using System;
using System.Runtime.InteropServices;

namespace OniSim.Compatibility
{
    /// <summary>
    /// Identity of the five images in the current native publication. The
    /// pointers are identifiers, not new leases; normal game-thread publication
    /// ordering still owns their lifetime. Revision zero disables upload reuse.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct PresentationUpdate
    {
        public const uint ByteSize = 64;
        public uint Version;
        public uint CellCount;
        public ulong Generation;
        public ulong Revision;
        public IntPtr Flow;
        public IntPtr Liquid;
        public IntPtr LiquidData;
        public IntPtr MaterialData;
        public IntPtr ExposedToSunlight;

        public bool IsValid
        {
            get
            {
                return Version == 1 && CellCount != 0 && Generation != 0 && Revision != 0
                    && Flow != IntPtr.Zero && Liquid != IntPtr.Zero && LiquidData != IntPtr.Zero
                    && MaterialData != IntPtr.Zero && ExposedToSunlight != IntPtr.Zero;
            }
        }

        public int BytesPerCell(IntPtr pointer)
        {
            if (pointer == Flow) return 8;
            if (pointer == Liquid || pointer == LiquidData || pointer == MaterialData) return 4;
            return pointer == ExposedToSunlight ? 1 : 0;
        }

        public int ImageIndex(IntPtr pointer)
        {
            if (pointer == Flow) return 0;
            if (pointer == Liquid) return 1;
            if (pointer == LiquidData) return 2;
            if (pointer == MaterialData) return 3;
            return pointer == ExposedToSunlight ? 4 : -1;
        }
    }
}
