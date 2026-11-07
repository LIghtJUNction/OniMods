using System;
using System.Runtime.InteropServices;

namespace CycleTrim.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeMulticomponentSpeciesRecord
    {
        public ushort ElementId;
        public byte Phase;
        public byte Reserved;
        public float Mass;
        public float Temperature;
        public float VolumeFraction;
        public float PartialPressureKPa;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeMulticomponentCellHeader
    {
        public uint SpeciesOffset;
        public ushort SpeciesCount;
        public ushort Reserved;
        public float TotalMass;
        public float GasPressureKPa;
        public float LiquidPressureKPa;
        public float LiquidVolumeFraction;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct NativeMulticomponentUpdate
    {
        public uint Version;
        public uint Flags;
        public ulong Generation;
        public uint CellCount;
        public uint TotalSpeciesRecords;
        public IntPtr Cells;
        public IntPtr Species;
    }

    /// <summary>
    /// Reads the versioned <see cref="NativeMulticomponentUpdate"/> published by
    /// OniSim's double-buffered GameData handoff. Fail-closed on any version,
    /// flag, pointer, bounds, or thermodynamic invariant mismatch.
    /// </summary>
    public sealed class OniSimMulticomponentProvider : IMulticomponentCellProvider
    {
        public const uint CurrentVersion = 1;
        public const uint FlagLegacySingleElement = 1;
        public const uint FlagMulticomponentCoexistence = 2;
        public const int MaxPooledSpeciesPerCell = 16;

        [ThreadStatic]
        private static MulticomponentSpecies[][] speciesBufferPool;

        private readonly Func<IntPtr> getUpdatePointer;
        private readonly Func<ushort, Element> resolveElement;
        private readonly bool allowLegacySingleElement;

        public OniSimMulticomponentProvider(
            Func<IntPtr> getUpdatePointer,
            Func<ushort, Element> resolveElement = null,
            bool allowLegacySingleElement = false)
        {
            if (getUpdatePointer == null)
                throw new ArgumentNullException("getUpdatePointer");
            this.getUpdatePointer = getUpdatePointer;
            this.resolveElement = resolveElement ?? ResolveCanonicalElement;
            this.allowLegacySingleElement = allowLegacySingleElement;
        }

        public bool IsReady
        {
            get
            {
                NativeMulticomponentUpdate update;
                return TryReadUpdateHeader(out update);
            }
        }

        public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
        {
            snapshot = default(MulticomponentCellSnapshot);
            if (cell < 0)
                return false;

            NativeMulticomponentUpdate update;
            if (!TryReadUpdateHeader(out update))
                return false;

            if ((uint)cell >= update.CellCount)
                return false;

            IntPtr cellAddress = new IntPtr(update.Cells.ToInt64() + (long)cell * 24L);
            uint speciesOffset = (uint)Marshal.ReadInt32(cellAddress, 0);
            ushort count = (ushort)Marshal.ReadInt16(cellAddress, 4);
            float totalMass = ReadSingle(cellAddress, 8);
            float gasPressureKPa = ReadSingle(cellAddress, 12);
            float liquidPressureKPa = ReadSingle(cellAddress, 16);
            float liquidVolumeFraction = ReadSingle(cellAddress, 20);

            if ((ulong)speciesOffset + count > update.TotalSpeciesRecords)
                return false;

            if (count > 0 && update.Species == IntPtr.Zero)
                return false;

            MulticomponentSpecies[] components = BorrowBuffer(count);
            long speciesBase = update.Species.ToInt64() + (long)speciesOffset * 20L;
            for (int i = 0; i < count; i++)
            {
                IntPtr recordPtr = new IntPtr(speciesBase + (long)i * 20L);
                ushort elementId = (ushort)Marshal.ReadInt16(recordPtr, 0);
                byte rawPhase = Marshal.ReadByte(recordPtr, 2);
                float mass = ReadSingle(recordPtr, 4);
                float temperature = ReadSingle(recordPtr, 8);
                float volumeFraction = ReadSingle(recordPtr, 12);
                float partialPressureKPa = ReadSingle(recordPtr, 16);

                MulticomponentPhase phase;
                if (!TryMapPhase(rawPhase, out phase))
                    return false;

                Element element = resolveElement(elementId);
                if (element == null)
                    return false;

                components[i] = new MulticomponentSpecies(
                    element,
                    phase,
                    mass,
                    temperature,
                    volumeFraction,
                    partialPressureKPa);
            }

            snapshot = new MulticomponentCellSnapshot(
                components,
                totalMass,
                gasPressureKPa,
                liquidPressureKPa,
                liquidVolumeFraction);
            return true;
        }

        private bool TryReadUpdateHeader(out NativeMulticomponentUpdate update)
        {
            update = default(NativeMulticomponentUpdate);
            IntPtr ptr = getUpdatePointer();
            if (ptr == IntPtr.Zero)
                return false;

            update.Version = (uint)Marshal.ReadInt32(ptr, 0);
            update.Flags = (uint)Marshal.ReadInt32(ptr, 4);
            update.Generation = (ulong)Marshal.ReadInt64(ptr, 8);
            update.CellCount = (uint)Marshal.ReadInt32(ptr, 16);
            update.TotalSpeciesRecords = (uint)Marshal.ReadInt32(ptr, 20);
            update.Cells = Marshal.ReadIntPtr(ptr, 24);
            update.Species = Marshal.ReadIntPtr(ptr, 32);

            if (update.Version != CurrentVersion || update.Generation == 0 || update.Cells == IntPtr.Zero)
                return false;

            uint knownFlags = FlagLegacySingleElement | FlagMulticomponentCoexistence;
            if ((update.Flags & ~knownFlags) != 0)
                return false;

            if (!allowLegacySingleElement && (update.Flags & FlagMulticomponentCoexistence) == 0)
                return false;

            return true;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct Int32SingleUnion
        {
            [FieldOffset(0)]
            public int Bits;
            [FieldOffset(0)]
            public float Value;
        }

        private static float ReadSingle(IntPtr address, int offset)
        {
            var u = new Int32SingleUnion { Bits = Marshal.ReadInt32(address, offset) };
            return u.Value;
        }

        private static bool TryMapPhase(byte nativePhase, out MulticomponentPhase phase)
        {
            switch (nativePhase)
            {
                case 1:
                    phase = MulticomponentPhase.Gas;
                    return true;
                case 2:
                    phase = MulticomponentPhase.Liquid;
                    return true;
                case 3:
                    phase = MulticomponentPhase.Solid;
                    return true;
                default:
                    phase = MulticomponentPhase.Gas;
                    return false;
            }
        }

        private static MulticomponentSpecies[] BorrowBuffer(ushort count)
        {
            if (count > MaxPooledSpeciesPerCell)
                return new MulticomponentSpecies[count];

            MulticomponentSpecies[][] pool = speciesBufferPool;
            if (pool == null)
            {
                pool = new MulticomponentSpecies[MaxPooledSpeciesPerCell + 1][];
                for (int i = 0; i <= MaxPooledSpeciesPerCell; i++)
                    pool[i] = new MulticomponentSpecies[i];
                speciesBufferPool = pool;
            }
            return pool[count];
        }

        private static Element ResolveCanonicalElement(ushort elementIndex)
        {
            var list = ElementLoader.elements;
            if (list == null || elementIndex >= list.Count)
                return null;
            return list[elementIndex];
        }
    }
}
