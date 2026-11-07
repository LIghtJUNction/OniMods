using System;
using System.Collections.Generic;
using System.IO;

namespace OniSim.Compatibility
{
    /// <summary>
    /// Validated OMC1 copy snapshot. ABI v1 is a legacy single-element projection;
    /// it is useful for validating the native/managed transport, not for enabling
    /// multicomponent gameplay behavior.
    /// </summary>
    public sealed class MulticomponentImage
    {
        public const int HeaderSize = 32;
        public const int CellRecordSize = 12;
        public const int SpeciesRecordSize = 12;
        public const uint Magic = 0x31434d4f;
        public const ushort CurrentVersion = 1;
        public const uint LegacySingleElementFlag = 1;

        public sealed class Cell
        {
            public float TotalMass { get; private set; }
            public uint SpeciesOffset { get; private set; }
            public ushort SpeciesCount { get; private set; }
            public ushort Flags { get; private set; }

            internal Cell(float mass, uint offset, ushort count, ushort flags)
            { TotalMass = mass; SpeciesOffset = offset; SpeciesCount = count; Flags = flags; }
        }

        public sealed class Species
        {
            public ushort ElementIndex { get; private set; }
            // Official Element.State values: 1 gas, 2 liquid, 3 solid.
            public byte Phase { get; private set; }
            public float Mass { get; private set; }
            public float Temperature { get; private set; }

            internal Species(ushort element, byte phase, float mass, float temperature)
            { ElementIndex = element; Phase = phase; Mass = mass; Temperature = temperature; }
        }

        private readonly Cell[] cells;
        private readonly Species[] species;

        public ushort Version { get; private set; }
        public ulong LifecycleGeneration { get; private set; }
        public uint Flags { get; private set; }
        public bool IsLegacySingleElementProjection
        { get { return (Flags & LegacySingleElementFlag) != 0; } }
        public bool SupportsCoexistingSpecies { get { return !IsLegacySingleElementProjection; } }
        public IList<Cell> Cells { get { return Array.AsReadOnly(cells); } }
        public IList<Species> SpeciesRecords { get { return Array.AsReadOnly(species); } }

        private MulticomponentImage(ushort version, ulong generation, uint flags, Cell[] cellRecords, Species[] speciesRecords)
        {
            Version = version;
            LifecycleGeneration = generation;
            Flags = flags;
            cells = cellRecords;
            species = speciesRecords;
        }

        public static MulticomponentImage Parse(byte[] image)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (image.Length < HeaderSize) throw new InvalidDataException("OMC1 image is shorter than its header.");
            if (ReadU32(image, 0) != Magic) throw new InvalidDataException("OMC1 magic does not match.");
            ushort version = ReadU16(image, 4);
            ushort headerSize = ReadU16(image, 6);
            if (version != CurrentVersion || headerSize != HeaderSize)
                throw new NotSupportedException("Unsupported OMC1 version or header size.");
            ulong generation = ReadU64(image, 8);
            uint cellCount = ReadU32(image, 16);
            uint speciesCount = ReadU32(image, 20);
            ushort cellSize = ReadU16(image, 24);
            ushort speciesSize = ReadU16(image, 26);
            uint flags = ReadU32(image, 28);
            if (cellSize != CellRecordSize || speciesSize != SpeciesRecordSize)
                throw new NotSupportedException("Unsupported OMC1 record layout.");
            if ((flags & ~LegacySingleElementFlag) != 0)
                throw new NotSupportedException("OMC1 contains unknown capability flags.");

            long expected = HeaderSize + (long)cellCount * CellRecordSize + (long)speciesCount * SpeciesRecordSize;
            if (expected != image.Length || cellCount > int.MaxValue || speciesCount > int.MaxValue)
                throw new InvalidDataException("OMC1 counts do not match the image length.");

            var cellRecords = new Cell[(int)cellCount];
            var speciesRecords = new Species[(int)speciesCount];
            var seen = new bool[(int)speciesCount];
            int speciesBase = checked(HeaderSize + (int)cellCount * CellRecordSize);
            for (int i = 0; i < cellRecords.Length; i++)
            {
                int at = HeaderSize + i * CellRecordSize;
                float totalMass = ReadF32(image, at);
                uint offset = ReadU32(image, at + 4);
                ushort count = ReadU16(image, at + 8);
                ushort cellFlags = ReadU16(image, at + 10);
                if (!FiniteNonNegative(totalMass) || (cellFlags & ~1) != 0)
                    throw new InvalidDataException("OMC1 cell header contains invalid values.");
                if ((count == 0) != (cellFlags == 0))
                    throw new InvalidDataException("OMC1 cell species flag disagrees with its count.");
                if ((ulong)offset + count > speciesCount)
                    throw new InvalidDataException("OMC1 cell species range is outside the species table.");

                float sum = 0f;
                for (int j = 0; j < count; j++)
                {
                    int index = checked((int)offset + j);
                    if (seen[index]) throw new InvalidDataException("OMC1 species ranges overlap.");
                    seen[index] = true;
                    int speciesAt = checked(speciesBase + index * SpeciesRecordSize);
                    ushort element = ReadU16(image, speciesAt);
                    byte phase = image[speciesAt + 2];
                    byte speciesFlags = image[speciesAt + 3];
                    float mass = ReadF32(image, speciesAt + 4);
                    float temperature = ReadF32(image, speciesAt + 8);
                    if (element == ushort.MaxValue || phase < 1 || phase > 3 || speciesFlags != 0
                        || !Finite(mass) || mass <= 0f || !Finite(temperature))
                        throw new InvalidDataException("OMC1 species record contains invalid values.");
                    sum += mass;
                    if (!Finite(sum)) throw new InvalidDataException("OMC1 species mass sum overflowed.");
                    speciesRecords[index] = new Species(element, phase, mass, temperature);
                }
                float tolerance = Math.Max(0.001f, totalMass * 0.0001f);
                if (Math.Abs(sum - totalMass) > tolerance)
                    throw new InvalidDataException("OMC1 cell mass does not match its species records.");
                cellRecords[i] = new Cell(totalMass, offset, count, cellFlags);
            }

            for (int i = 0; i < seen.Length; i++)
                if (!seen[i]) throw new InvalidDataException("OMC1 contains unreferenced species records.");

            if ((flags & LegacySingleElementFlag) != 0)
                for (int i = 0; i < cellRecords.Length; i++)
                    if (cellRecords[i].SpeciesCount > 1)
                        throw new InvalidDataException("Legacy OMC1 projection cannot contain multiple species in one cell.");

            return new MulticomponentImage(version, generation, flags, cellRecords, speciesRecords);
        }

        private static ushort ReadU16(byte[] b, int i) { return (ushort)(b[i] | b[i + 1] << 8); }
        private static uint ReadU32(byte[] b, int i) { return (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24); }
        private static ulong ReadU64(byte[] b, int i) { return ReadU32(b, i) | ((ulong)ReadU32(b, i + 4) << 32); }
        private static float ReadF32(byte[] b, int i) { return BitConverter.ToSingle(b, i); }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool FiniteNonNegative(float v) { return Finite(v) && v >= 0f; }
    }
}
