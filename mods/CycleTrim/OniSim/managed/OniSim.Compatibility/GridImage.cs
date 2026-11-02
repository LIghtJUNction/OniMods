using System;
using System.IO;

namespace OniSim.Compatibility
{
    /// <summary>An owned copy of the official InitializeFromCells serialized payload.</summary>
    public sealed class GridImage
    {
        private readonly byte[] bytes;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public uint Seed { get; private set; }
        public byte Radiation { get; private set; }
        public byte Headless { get; private set; }
        public int CellCount { get { return checked(Width * Height); } }

        private GridImage(byte[] ownedBytes, int width, int height)
        { bytes = ownedBytes; Width = width; Height = height; Seed = BinaryLayout.ReadU32(bytes, 8); Radiation = bytes[12]; Headless = bytes[13]; }

        public static GridImage Parse(byte[] image)
        {
            if (image == null) throw new ArgumentNullException("image");
            if (image.Length < 14 || image.Length > 14 + 40 * BinaryLayout.MaxCells)
                throw new InvalidDataException("Grid image length is outside the supported limit.");
            var copy = (byte[])image.Clone();
            Validate(copy);
            return new GridImage(copy, BinaryLayout.ReadI32(copy, 0), BinaryLayout.ReadI32(copy, 4));
        }

        internal static void Validate(byte[] image)
        {
            if (image == null || image.Length < 14) throw new InvalidDataException("Grid image is truncated.");
            int width = BinaryLayout.ReadI32(image, 0), height = BinaryLayout.ReadI32(image, 4);
            if (width <= 0 || height <= 0 || (long)width * height > BinaryLayout.MaxCells)
                throw new InvalidDataException("Grid dimensions are outside the supported limit.");
            int count = checked(width * height);
            if (image.Length != checked(14 + 40 * count)) throw new InvalidDataException("Grid image length does not match dimensions.");
            if (image[12] > 1 || image[13] > 1) throw new InvalidDataException("Grid boolean/radiation header byte is invalid.");
            ValidateFiniteFields(image, count);
        }

        public static GridImage Create(int width, int height, uint seed, byte radiation, bool headless)
        {
            if (width <= 0 || height <= 0 || (long)width * height > BinaryLayout.MaxCells) throw new ArgumentOutOfRangeException("width");
            if (radiation > 1) throw new ArgumentOutOfRangeException("radiation", "Radiation flag must be 0 or 1.");
            int length = checked(14 + 40 * checked(width * height));
            var b = new byte[length]; BinaryLayout.WriteI32(b, 0, width); BinaryLayout.WriteI32(b, 4, height);
            BinaryLayout.WriteU32(b, 8, seed); b[12] = radiation; b[13] = headless ? (byte)1 : (byte)0;
            return new GridImage(b, width, height);
        }

        public byte[] ToArray()
        { var copy = new byte[bytes.Length]; Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length); return copy; }

        internal byte[] OwnedBytes { get { return bytes; } }

        private static void ValidateFiniteFields(byte[] b, int n)
        {
            int cells = 14, diseases = 14 + 16 * n, walls = 14 + 28 * n;
            for (int i = 0; i < n; i++)
            {
                RequireFinite(b, cells + 8 + 16 * i); RequireFinite(b, cells + 12 + 16 * i);
                RequireFinite(b, diseases + 8 + 12 * i); RequireFinite(b, walls + 4 + 12 * i); RequireFinite(b, walls + 8 + 12 * i);
            }
        }
        private static void RequireFinite(byte[] b, int offset)
        { if (!IsFinite(BinaryLayout.ReadF32(b, offset))) throw new InvalidDataException("Grid image contains a non-finite float."); }
        internal static bool IsFinite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
    }

}
