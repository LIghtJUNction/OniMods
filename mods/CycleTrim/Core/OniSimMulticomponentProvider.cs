using System;
using System.Runtime.InteropServices;
using System.Threading;
using Jlib.Multicomponent;

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
    public sealed class OniSimMulticomponentProvider : IMulticomponentCellProvider,
        IMulticomponentCellBatchProvider
    {
        public const uint CurrentVersion = 1;
        public const uint FlagLegacySingleElement = 1;
        public const uint FlagMulticomponentCoexistence = 2;
        internal const int MaximumBatchSpeciesRecords = 4096;
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

        public bool TryGetCellBatch(int firstCell, int count, out IMulticomponentCellBatch batch)
        {
            batch = null;
            if (firstCell < 0 || count <= 0
                || count > Jlib.Multicomponent.MulticomponentCellAccess.MaximumBatchCells)
                return false;

            var reader = ThreadLocalObjectPool<CellBatch>.Rent();
            reader.Begin(this, firstCell, count);
            bool retained = false;
            try
            {
                IntPtr pointer = acquireUpdate();
                if (pointer == IntPtr.Zero)
                    return false;
                try
                {
                    NativeMulticomponentUpdate update;
                    if (!TryReadUpdateHeader(pointer, out update) || !reader.Copy(update))
                        return false;
                }
                finally
                {
                    releaseUpdate();
                }
                // No native pointers or lease escape. Element resolution and
                // game callbacks run only when the managed copy is consumed.
                batch = reader;
                retained = true;
                return true;
            }
            finally
            {
                if (!retained)
                    reader.Dispose();
            }
        }

        private sealed class CellBatch : IMulticomponentCellBatch
        {
            private readonly byte[] cells = new byte[
                Jlib.Multicomponent.MulticomponentCellAccess.MaximumBatchCells * 24];
            // Fixed-size scratch bounds per-thread retained memory. An
            // unusually large native span uses the existing per-cell reader.
            private readonly byte[] species = new byte[MaximumBatchSpeciesRecords * 20];
            private readonly bool[] readable = new bool[
                Jlib.Multicomponent.MulticomponentCellAccess.MaximumBatchCells];
            private MulticomponentSpecies[] components = Array.Empty<MulticomponentSpecies>();
            private OniSimMulticomponentProvider owner;
            private int firstCell, cellCount, thread;
            private uint firstSpecies;
            private bool active;
            private bool reading;

            internal void Begin(OniSimMulticomponentProvider provider, int first, int count)
            {
                owner = provider;
                firstCell = first;
                cellCount = count;
                thread = Thread.CurrentThread.ManagedThreadId;
                active = true;
            }

            internal bool Copy(NativeMulticomponentUpdate update)
            {
                if ((ulong)firstCell + (uint)cellCount > update.CellCount
                    || !Address(update.Cells, (ulong)firstCell * 24,
                        (ulong)cellCount * 24, out var start))
                    return false;
                Marshal.Copy(start, cells, 0, cellCount * 24);

                uint minimum = uint.MaxValue;
                ulong maximum = 0;
                for (int i = 0; i < cellCount; i++)
                {
                    int at = i * 24;
                    uint offset = BitConverter.ToUInt32(cells, at);
                    ushort count = BitConverter.ToUInt16(cells, at + 4);
                    readable[i] = BitConverter.ToUInt16(cells, at + 6) == (count == 0 ? 0 : 1)
                        && (ulong)offset + count <= update.TotalSpeciesRecords;
                    if (!readable[i] || count == 0)
                        continue;
                    minimum = Math.Min(minimum, offset);
                    maximum = Math.Max(maximum, (ulong)offset + count);
                }
                firstSpecies = minimum == uint.MaxValue ? 0 : minimum;
                ulong span = maximum - firstSpecies;
                if (span > MaximumBatchSpeciesRecords)
                    return false;
                if (span == 0)
                    return true;
                if (!Address(update.Species, (ulong)firstSpecies * 20, span * 20, out start))
                    return false;
                Marshal.Copy(start, species, 0, (int)span * 20);
                return true;
            }

            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot)
            {
                snapshot = default(MulticomponentCellSnapshot);
                if (!active || reading || thread != Thread.CurrentThread.ManagedThreadId
                    || cell < firstCell || (long)cell >= (long)firstCell + cellCount)
                    return false;
                int index = cell - firstCell;
                if (!readable[index])
                    return false;
                int at = index * 24;
                int count = BitConverter.ToUInt16(cells, at + 4);
                if (components.Length < count)
                    components = new MulticomponentSpecies[Math.Min(MaximumBatchSpeciesRecords,
                        Math.Max(count, Math.Max(4, components.Length * 2)))];
                uint offset = BitConverter.ToUInt32(cells, at);
                reading = true;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        int record = checked((int)(offset - firstSpecies) + i) * 20;
                        float mass = ReadSingle(species, record + 4);
                        if (species[record + 3] != 0 || mass <= 0f
                            || !TryMapPhase(species[record + 2], out var phase))
                            return false;
                        Element element = owner.resolveElement(BitConverter.ToUInt16(species, record));
                        if (element == null)
                            return false;
                        components[i] = new MulticomponentSpecies(element, phase, mass,
                            ReadSingle(species, record + 8), ReadSingle(species, record + 12),
                            ReadSingle(species, record + 16));
                    }
                    snapshot = new MulticomponentCellSnapshot(components, count,
                        ReadSingle(cells, at + 8), ReadSingle(cells, at + 12),
                        ReadSingle(cells, at + 16), ReadSingle(cells, at + 20));
                    return true;
                }
                finally { reading = false; }
            }

            public void Dispose()
            {
                if (!active)
                    return;
                if (thread != Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("A cell batch must be disposed on its acquiring thread.");
                if (reading)
                    throw new InvalidOperationException("A cell batch cannot be disposed during element resolution.");
                active = false;
                owner = null;
                firstCell = cellCount = thread = 0;
                firstSpecies = 0;
                Array.Clear(components, 0, components.Length);
                Array.Clear(readable, 0, readable.Length);
                ThreadLocalObjectPool<CellBatch>.Return(this);
            }

            private static float ReadSingle(byte[] source, int offset)
            {
                var value = new Int32SingleUnion { Bits = BitConverter.ToInt32(source, offset) };
                return value.Value;
            }

            private static bool Address(IntPtr pointer, ulong offset, ulong length, out IntPtr result)
            {
                result = IntPtr.Zero;
                if (pointer == IntPtr.Zero || pointer.ToInt64() < 0)
                    return false;
                ulong address = (ulong)pointer.ToInt64();
                if (offset > long.MaxValue - address || length > long.MaxValue - address - offset)
                    return false;
                result = new IntPtr((long)(address + offset));
                return true;
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
