using System;
using System.IO;

namespace OniOnline.Core
{
    // The pinned 744825 native Sim save format. This preserves the native records;
    // it does not reconstruct them from the public Grid arrays or load a colony.
    internal sealed class NativeSimSaveBlock
    {
        internal const int Version = 15;
        internal const int HeaderBytes = 33; // managed length prefix + native header
        internal const int MaxLogicalDimension = 8192;
        internal const int MaxStorageCells = 2 * 1024 * 1024;
        internal const int MaxSerializedBytes = HeaderBytes + 36 * MaxStorageCells;

        private readonly byte[] serialized;
        internal int StorageWidth { get; private set; }
        internal int StorageHeight { get; private set; }
        internal int GameWidth { get { return StorageWidth - 2; } }
        internal int GameHeight { get { return StorageHeight - 2; } }
        internal int TargetX { get; private set; }
        internal int TargetY { get; private set; }
        internal byte Settings { get; private set; }
        internal int SerializedLength { get { return serialized.Length; } }
        private int StorageCells { get { return StorageWidth * StorageHeight; } }

        private NativeSimSaveBlock(byte[] bytes, int width, int height, int x, int y)
        {
            serialized = bytes;
            StorageWidth = width;
            StorageHeight = height;
            TargetX = x;
            TargetY = y;
            Settings = bytes[32];
        }

        internal static NativeSimSaveBlock Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (bytes.Length < HeaderBytes || bytes.Length > MaxSerializedBytes)
                throw new InvalidDataException("native_sim_save_size_out_of_bounds");
            // Validate the owned copy so later changes to the caller's buffer
            // cannot change the checked dimensions or the records being cropped.
            var owned = (byte[])bytes.Clone();
            if (ReadInt32(owned, 0) != owned.Length - 4)
                throw new InvalidDataException("native_sim_save_length_mismatch");
            if (owned[4] != 'S' || owned[5] != 'I' || owned[6] != 'M' || owned[7] != 'S' ||
                owned[8] != 'A' || owned[9] != 'V' || owned[10] != 'E' || owned[11] != 0 ||
                ReadInt32(owned, 12) != Version)
                throw new InvalidDataException("native_sim_save_format_unsupported");
            int width = ReadInt32(owned, 16), height = ReadInt32(owned, 20);
            int cells = CheckStorageDimensions(width, height);
            if (owned.Length != HeaderBytes + 36 * cells)
                throw new InvalidDataException("native_sim_save_record_length_mismatch");
            int x = ReadInt32(owned, 24), y = ReadInt32(owned, 28);
            if (x < 0 || y < 0 || (long)x + width - 2 > MaxLogicalDimension ||
                (long)y + height - 2 > MaxLogicalDimension)
                throw new InvalidDataException("native_sim_save_offset_out_of_bounds");
            return new NativeSimSaveBlock(owned, width, height, x, y);
        }

        // x/y refer to game cells, not the stored array that includes a halo.
        // The result includes the original one-cell ring and starts at (0, 0).
        internal NativeSimSaveBlock Crop(int x, int y, int width, int height)
        {
            CheckRectangle(x, y, width, height, GameWidth, GameHeight);
            int storageWidth = checked(width + 2), storageHeight = checked(height + 2);
            int destinationCells = CheckStorageDimensions(storageWidth, storageHeight);
            var result = new byte[HeaderBytes + 36 * destinationCells];
            Buffer.BlockCopy(serialized, 0, result, 0, HeaderBytes);
            WriteInt32(result, 0, result.Length - 4);
            WriteInt32(result, 16, storageWidth);
            WriteInt32(result, 20, storageHeight);
            WriteInt32(result, 24, 0);
            WriteInt32(result, 28, 0);

            CopyRows(result, x, y, storageWidth, storageHeight, 16, HeaderBytes, HeaderBytes);
            CopyRows(result, x, y, storageWidth, storageHeight, 8,
                HeaderBytes + 16 * StorageCells, HeaderBytes + 16 * destinationCells);
            CopyRows(result, x, y, storageWidth, storageHeight, 12,
                HeaderBytes + 24 * StorageCells, HeaderBytes + 24 * destinationCells);
            return new NativeSimSaveBlock(result, storageWidth, storageHeight, 0, 0);
        }

        // Native loading can apply the source's global settings. The destination
        // must explicitly match them before a positioned block can be emitted.
        internal byte[] SerializeAt(int x, int y, int destinationGameWidth, int destinationGameHeight,
            byte destinationSettings)
        {
            CheckStorageDimensions(checked(destinationGameWidth + 2), checked(destinationGameHeight + 2));
            CheckRectangle(x, y, GameWidth, GameHeight, destinationGameWidth, destinationGameHeight);
            if (Settings != destinationSettings)
                throw new InvalidDataException("native_sim_save_settings_mismatch");
            byte[] result = CopySerializedBytes();
            WriteInt32(result, 24, x);
            WriteInt32(result, 28, y);
            return result;
        }

        internal byte[] CopySerializedBytes() { return (byte[])serialized.Clone(); }

        private void CopyRows(byte[] destination, int x, int y, int width, int height, int recordBytes,
            int sourceStart, int destinationStart)
        {
            // Storage cell (x, y) is the halo cell immediately below/left of
            // logical game cell (x, y), hence no +1 at this outer-ring boundary.
            int rowBytes = checked(width * recordBytes);
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(serialized, sourceStart + ((y + row) * StorageWidth + x) * recordBytes,
                    destination, destinationStart + row * rowBytes, rowBytes);
        }

        private static int CheckStorageDimensions(int width, int height)
        {
            long cells = (long)width * height;
            if (width < 3 || height < 3 || width > MaxLogicalDimension + 2 || height > MaxLogicalDimension + 2 ||
                cells > MaxStorageCells)
                throw new InvalidDataException("native_sim_save_dimensions_out_of_bounds");
            return (int)cells;
        }

        private static void CheckRectangle(int x, int y, int width, int height, int outerWidth, int outerHeight)
        {
            if (x < 0 || y < 0 || width < 1 || height < 1 || (long)x + width > outerWidth ||
                (long)y + height > outerHeight)
                throw new InvalidDataException("native_sim_save_rectangle_out_of_bounds");
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            unchecked
            {
                bytes[offset] = (byte)value;
                bytes[offset + 1] = (byte)(value >> 8);
                bytes[offset + 2] = (byte)(value >> 16);
                bytes[offset + 3] = (byte)(value >> 24);
            }
        }
    }
}
