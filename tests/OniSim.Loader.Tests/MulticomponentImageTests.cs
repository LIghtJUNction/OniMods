using System;
using System.IO;
using OniSim.Compatibility;

internal static class MulticomponentImageTests
{
    public static void Run()
    {
        ValidLegacyImageParses();
        RejectsMassMismatch();
        RejectsOverlappingSpeciesRanges();
        RejectsUnknownPhase();
        Console.WriteLine("OMC1 parser validation and fail-closed regressions passed");
    }

    private static void ValidLegacyImageParses()
    {
        MulticomponentImage image = MulticomponentImage.Parse(CreateImage());
        Require(image.Version == 1, "version");
        Require(image.LifecycleGeneration == 17, "generation");
        Require(image.IsLegacySingleElementProjection, "legacy projection flag");
        Require(!image.SupportsCoexistingSpecies, "must not claim coexisting species");
        Require(image.Cells.Count == 2 && image.SpeciesRecords.Count == 2, "record counts");
        Require(image.Cells[0].SpeciesCount == 1 && image.Cells[1].SpeciesOffset == 1, "cell ranges");
        Require(image.SpeciesRecords[0].Phase == 1 && image.SpeciesRecords[1].Phase == 2, "phase codes");
        Require(Math.Abs(image.SpeciesRecords[0].Mass - 1.25f) < 0.00001f, "species mass");
    }

    private static void RejectsMassMismatch()
    {
        byte[] image = CreateImage();
        PutF32(image, 32 + 12, 0.75f);
        ExpectInvalid(image, "mass mismatch");
    }

    private static void RejectsOverlappingSpeciesRanges()
    {
        byte[] image = CreateImage();
        PutU32(image, 32 + 12 + 4, 0);
        ExpectInvalid(image, "overlapping species ranges");
    }

    private static void RejectsUnknownPhase()
    {
        byte[] image = CreateImage();
        image[32 + 2 * 12 + 2] = 0;
        ExpectInvalid(image, "unknown phase");
    }

    private static byte[] CreateImage()
    {
        const int header = 32;
        const int cellSize = 12;
        const int speciesSize = 12;
        byte[] image = new byte[header + 2 * cellSize + 2 * speciesSize];
        PutU32(image, 0, MulticomponentImage.Magic);
        PutU16(image, 4, 1);
        PutU16(image, 6, header);
        PutU64(image, 8, 17);
        PutU32(image, 16, 2);
        PutU32(image, 20, 2);
        PutU16(image, 24, cellSize);
        PutU16(image, 26, speciesSize);
        PutU32(image, 28, MulticomponentImage.LegacySingleElementFlag);

        PutF32(image, 32, 1.25f);
        PutU32(image, 36, 0);
        PutU16(image, 40, 1);
        PutU16(image, 42, 1);
        PutF32(image, 44, 0.5f);
        PutU32(image, 48, 1);
        PutU16(image, 52, 1);
        PutU16(image, 54, 1);

        int species = header + 2 * cellSize;
        PutU16(image, species, 1);
        image[species + 2] = 1;
        PutF32(image, species + 4, 1.25f);
        PutF32(image, species + 8, 300f);
        PutU16(image, species + speciesSize, 2);
        image[species + speciesSize + 2] = 2;
        PutF32(image, species + speciesSize + 4, 0.5f);
        PutF32(image, species + speciesSize + 8, 450f);
        return image;
    }

    private static void ExpectInvalid(byte[] image, string name)
    {
        try { MulticomponentImage.Parse(image); }
        catch (InvalidDataException) { return; }
        throw new Exception("OMC1 parser accepted " + name);
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("OMC1 regression failed: " + name);
    }

    private static void PutU16(byte[] b, int i, int value)
    { b[i] = (byte)value; b[i + 1] = (byte)(value >> 8); }
    private static void PutU32(byte[] b, int i, uint value)
    { for (int n = 0; n < 4; n++) b[i + n] = (byte)(value >> (8 * n)); }
    private static void PutU64(byte[] b, int i, ulong value)
    { for (int n = 0; n < 8; n++) b[i + n] = (byte)(value >> (8 * n)); }
    private static void PutF32(byte[] b, int i, float value)
    { PutU32(b, i, unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0))); }
}
