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
        [ThreadStatic]
        private static MulticomponentSpecies[] speciesBuffer;

        private readonly Func<IntPtr> acquireUpdate;
        private readonly System.Action releaseUpdate;
        private readonly Func<ushort, Element> resolveElement;

        public OniSimMulticomponentProvider(
            Func<IntPtr> acquireUpdate,
            System.Action releaseUpdate,
            Func<ushort, Element> resolveElement = null)
        {
            if (acquireUpdate == null)
                throw new ArgumentNullException("acquireUpdate");
            if (releaseUpdate == null)
                throw new ArgumentNullException("releaseUpdate");
            this.acquireUpdate = acquireUpdate;
            this.releaseUpdate = releaseUpdate;
            this.resolveElement = resolveElement ?? ResolveCanonicalElement;
        }

        public bool IsReady
        {
            get
            {
                IntPtr pointer = acquireUpdate();
                if (pointer == IntPtr.Zero)
                    return false;
                try
                {
                    NativeMulticomponentUpdate update;
                    return TryReadUpdateHeader(pointer, out update);
                }
                finally
                {
                    releaseUpdate();
                }
            }
        }

        public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
        {
            snapshot = default(MulticomponentCellSnapshot);
            if (cell < 0)
                return false;

            IntPtr pointer = acquireUpdate();
            if (pointer == IntPtr.Zero)
                return false;

            try
            {
                NativeMulticomponentUpdate update;
                if (!TryReadUpdateHeader(pointer, out update) || (uint)cell >= update.CellCount)
                    return false;

                long cellAddressValue;
                try { cellAddressValue = checked(update.Cells.ToInt64() + (long)cell * 24L); }
                catch (OverflowException) { return false; }
                IntPtr cellAddress = new IntPtr(cellAddressValue);
                uint speciesOffset = (uint)Marshal.ReadInt32(cellAddress, 0);
                ushort count = (ushort)Marshal.ReadInt16(cellAddress, 4);
                ushort cellFlags = (ushort)Marshal.ReadInt16(cellAddress, 6);
                if (cellFlags != (count == 0 ? 0 : 1))
                    return false;
                float totalMass = ReadSingle(cellAddress, 8);
                float gasPressureKPa = ReadSingle(cellAddress, 12);
                float liquidPressureKPa = ReadSingle(cellAddress, 16);
                float liquidVolumeFraction = ReadSingle(cellAddress, 20);

                if ((ulong)speciesOffset + count > update.TotalSpeciesRecords)
                    return false;
                if (count > 0 && update.Species == IntPtr.Zero)
                    return false;

                MulticomponentSpecies[] components = BorrowBuffer(count);
                long speciesBase;
                try { speciesBase = checked(update.Species.ToInt64() + (long)speciesOffset * 20L); }
                catch (OverflowException) { return false; }
                for (int i = 0; i < count; i++)
                {
                    long recordAddress;
                    try { recordAddress = checked(speciesBase + (long)i * 20L); }
                    catch (OverflowException) { return false; }
                    IntPtr recordPtr = new IntPtr(recordAddress);
                    ushort elementId = (ushort)Marshal.ReadInt16(recordPtr, 0);
                    byte rawPhase = Marshal.ReadByte(recordPtr, 2);
                    byte reserved = Marshal.ReadByte(recordPtr, 3);
                    float mass = ReadSingle(recordPtr, 4);
                    if (reserved != 0 || mass <= 0f)
                        return false;
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

                // Species records are copied while the native GameData read
                // lease is held. The returned snapshot uses a reusable
                // thread-local buffer and must be consumed before the next
                // TryGetCell call on this thread.
                snapshot = new MulticomponentCellSnapshot(
                    components,
                    count,
                    totalMass,
                    gasPressureKPa,
                    liquidPressureKPa,
                    liquidVolumeFraction);
                return true;
            }
            finally
            {
                releaseUpdate();
            }
        }

        private static bool TryReadUpdateHeader(IntPtr ptr, out NativeMulticomponentUpdate update)
        {
            update = default(NativeMulticomponentUpdate);
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
            if (update.TotalSpeciesRecords > 0 && update.Species == IntPtr.Zero)
                return false;

            // A single-element projection cannot safely drive multicomponent
            // gameplay shims. Require the producer's explicit coexistence
            // capability; generation and pointer presence are not sufficient.
            return update.Flags == FlagMulticomponentCoexistence;
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
            if (speciesBuffer == null || speciesBuffer.Length < count)
            {
                int capacity = Math.Max(4, speciesBuffer == null ? 0 : speciesBuffer.Length * 2);
                if (capacity < count)
                    capacity = count;
                speciesBuffer = new MulticomponentSpecies[capacity];
            }
            return speciesBuffer;
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
