using System;
using System.IO;
using System.Text;

namespace OniOnline.Sdk
{
    public enum CellBatchType : byte
    {
        Box = 0,
        DiscreteCells = 1
    }

    /// <summary>
    /// Compact, high-throughput binary serialization for multi-cell drag operations.
    /// Eliminates network flooding by compressing whole-area tool interactions (digging, building,
    /// mopping, prioritizing) into a single bounded payload under 4096 bytes.
    /// </summary>
    public static class CellBatchCodec
    {
        public const int MaximumCellsPerBatch = 4096;
        public const int MaximumPayloadSize = 4096;
        public const int MaximumToolIdLength = 32;
        public const int MaximumMaterialTagLength = 64;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] EncodeBox(
            int worldId,
            int startX, int startY,
            int endX, int endY,
            string toolId,
            int priority = 5,
            string materialTag = "")
        {
            if (!ValidToolId(toolId))
                throw new ArgumentException("invalid_tool_id", nameof(toolId));
            if (priority < 1 || priority > 9)
                throw new ArgumentOutOfRangeException(nameof(priority), "priority must be 1..9");
            if (startX < short.MinValue || startX > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(startX));
            if (startY < short.MinValue || startY > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(startY));
            if (endX < short.MinValue || endX > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(endX));
            if (endY < short.MinValue || endY > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(endY));

            string normalizedMaterial = materialTag ?? string.Empty;
            if (!ValidMaterialTag(normalizedMaterial))
                throw new ArgumentException("invalid_material_tag", nameof(materialTag));

            using (var ms = new MemoryStream(64))
            using (var writer = new BinaryWriter(ms, StrictUtf8))
            {
                writer.Write((byte)CellBatchType.Box);
                writer.Write(worldId);
                writer.Write((short)startX);
                writer.Write((short)startY);
                writer.Write((short)endX);
                writer.Write((short)endY);
                writer.Write((byte)priority);
                WriteBoundedString(ms, toolId);
                WriteBoundedString(ms, normalizedMaterial);
                return ms.ToArray();
            }
        }

        public static bool TryDecodeBox(
            byte[] payload,
            out int worldId,
            out int startX, out int startY,
            out int endX, out int endY,
            out string toolId,
            out int priority,
            out string materialTag)
        {
            worldId = 0; startX = 0; startY = 0; endX = 0; endY = 0;
            toolId = string.Empty; priority = 5; materialTag = string.Empty;

            if (payload == null || payload.Length < 14 || payload.Length > MaximumPayloadSize) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(ms, StrictUtf8))
                {
                    if (reader.ReadByte() != (byte)CellBatchType.Box) return false;
                    worldId = reader.ReadInt32();
                    startX = reader.ReadInt16();
                    startY = reader.ReadInt16();
                    endX = reader.ReadInt16();
                    endY = reader.ReadInt16();
                    priority = reader.ReadByte();
                    if (priority < 1 || priority > 9) return false;
                    if (!TryReadBoundedString(ms, MaximumToolIdLength, allowEmpty: false, out toolId)) return false;
                    if (!TryReadBoundedString(ms, MaximumMaterialTagLength, allowEmpty: true, out materialTag)) return false;
                    return ms.Position == ms.Length && ValidToolId(toolId) && ValidMaterialTag(materialTag);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static byte[] EncodeCells(
            int worldId,
            int[] cells,
            string toolId,
            int priority = 5,
            string materialTag = "")
        {
            if (cells == null || cells.Length == 0 || cells.Length > MaximumCellsPerBatch)
                throw new ArgumentException("invalid_cells_array", nameof(cells));
            if (!ValidToolId(toolId))
                throw new ArgumentException("invalid_tool_id", nameof(toolId));
            if (priority < 1 || priority > 9)
                throw new ArgumentOutOfRangeException(nameof(priority), "priority must be 1..9");

            string normalizedMaterial = materialTag ?? string.Empty;
            if (!ValidMaterialTag(normalizedMaterial))
                throw new ArgumentException("invalid_material_tag", nameof(materialTag));
            for (int k = 0; k < cells.Length; k++)
            {
                if (cells[k] < 0)
                    throw new ArgumentOutOfRangeException(nameof(cells), "cell index cannot be negative");
            }

            using (var ms = new MemoryStream(Math.Min(MaximumPayloadSize, 32 + cells.Length * 2)))
            using (var writer = new BinaryWriter(ms, StrictUtf8))
            {
                writer.Write((byte)CellBatchType.DiscreteCells);
                writer.Write(worldId);
                writer.Write((byte)priority);
                WriteBoundedString(ms, toolId);
                WriteBoundedString(ms, normalizedMaterial);
                writer.Write((ushort)cells.Length);

                // Hybrid RLE + ZigZag VarInt delta compression:
                // - Even token ((run - 1) << 1): run of consecutive +1 cell increments
                // - Odd token ((zigzag << 1) | 1): single arbitrary signed delta
                writer.Write(cells[0]);
                int i = 1;
                while (i < cells.Length)
                {
                    int delta = cells[i] - cells[i - 1];
                    if (delta == 1)
                    {
                        int run = 1;
                        while (i + run < cells.Length && cells[i + run] - cells[i + run - 1] == 1)
                        {
                            run++;
                        }
                        ulong runToken = (ulong)(run - 1) << 1;
                        WriteVarUInt64(writer, runToken);
                        i += run;
                    }
                    else
                    {
                        uint zigzag = (uint)((delta << 1) ^ (delta >> 31));
                        ulong deltaToken = ((ulong)zigzag << 1) | 1UL;
                        WriteVarUInt64(writer, deltaToken);
                        i++;
                    }
                }
                if (ms.Length > MaximumPayloadSize)
                    throw new ArgumentException("encoded_batch_exceeds_maximum_payload_size", nameof(cells));
                return ms.ToArray();
            }
        }

        public static bool TryDecodeCells(
            byte[] payload,
            out int worldId,
            out int[] cells,
            out string toolId,
            out int priority,
            out string materialTag)
        {
            worldId = 0; cells = null; toolId = string.Empty; priority = 5; materialTag = string.Empty;

            if (payload == null || payload.Length < 10 || payload.Length > MaximumPayloadSize) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(ms, StrictUtf8))
                {
                    if (reader.ReadByte() != (byte)CellBatchType.DiscreteCells) return false;
                    worldId = reader.ReadInt32();
                    priority = reader.ReadByte();
                    if (priority < 1 || priority > 9) return false;
                    if (!TryReadBoundedString(ms, MaximumToolIdLength, allowEmpty: false, out toolId)) return false;
                    if (!TryReadBoundedString(ms, MaximumMaterialTagLength, allowEmpty: true, out materialTag)) return false;
                    if (!ValidToolId(toolId) || !ValidMaterialTag(materialTag)) return false;

                    int count = reader.ReadUInt16();
                    if (count <= 0 || count > MaximumCellsPerBatch) return false;

                    int anchor = reader.ReadInt32();
                    if (anchor < 0) return false;

                    var decoded = new int[count];
                    decoded[0] = anchor;
                    int i = 1;
                    while (i < count)
                    {
                        ulong token;
                        if (!TryReadVarUInt64(reader, out token)) return false;
                        if ((token & 1UL) == 0UL)
                        {
                            ulong run = (token >> 1) + 1UL;
                            if (run > (ulong)(count - i)) return false;
                            for (ulong r = 0; r < run; r++)
                            {
                                long next = (long)decoded[i - 1] + 1L;
                                if (next > int.MaxValue) return false;
                                decoded[i++] = (int)next;
                            }
                        }
                        else
                        {
                            ulong zz64 = token >> 1;
                            if (zz64 > uint.MaxValue) return false;
                            uint zigzag = (uint)zz64;
                            int delta = (int)((zigzag >> 1) ^ -(int)(zigzag & 1));
                            long next = (long)decoded[i - 1] + delta;
                            if (next < 0 || next > int.MaxValue) return false;
                            decoded[i++] = (int)next;
                        }
                    }
                    if (ms.Position != ms.Length) return false;
                    cells = decoded;
                    return true;
                }
            }
            catch (Exception)
            {
                cells = null;
                return false;
            }
        }

        internal static void WriteBoundedString(Stream stream, string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));
            stream.WriteByte((byte)(bytes.Length & 0xFF));
            stream.WriteByte((byte)((bytes.Length >> 8) & 0xFF));
            if (bytes.Length > 0)
                stream.Write(bytes, 0, bytes.Length);
        }

        internal static bool TryReadBoundedString(Stream stream, int maxLength, bool allowEmpty, out string value)
        {
            value = string.Empty;
            int b0 = stream.ReadByte();
            int b1 = stream.ReadByte();
            if (b0 < 0 || b1 < 0) return false;
            int byteLength = b0 | (b1 << 8);
            if (byteLength == 0) return allowEmpty;
            if (byteLength > maxLength * 4 || stream.Position + byteLength > stream.Length) return false;
            byte[] buffer = new byte[byteLength];
            int read = stream.Read(buffer, 0, byteLength);
            if (read != byteLength) return false;
            string decoded = StrictUtf8.GetString(buffer, 0, byteLength);
            if (decoded.Length > maxLength || (!allowEmpty && decoded.Length == 0)) return false;
            value = decoded;
            return true;
        }

        private static void WriteVarUInt64(BinaryWriter writer, ulong value)
        {
            while (value >= 0x80UL)
            {
                writer.Write((byte)(value | 0x80UL));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        private static bool TryReadVarUInt64(BinaryReader reader, out ulong value)
        {
            value = 0;
            int shift = 0;
            while (shift < 63)
            {
                byte b = reader.ReadByte();
                if (shift == 56 && (b & 0xFE) != 0)
                    return false;
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return true;
                shift += 7;
            }
            return false;
        }

        private static bool ValidToolId(string toolId)
        {
            if (string.IsNullOrEmpty(toolId) || toolId.Length > MaximumToolIdLength) return false;
            for (int i = 0; i < toolId.Length; i++)
            {
                char c = toolId[i];
                if (c <= 32 || c > 126) return false;
            }
            return true;
        }

        private static bool ValidMaterialTag(string materialTag)
        {
            if (materialTag == null || materialTag.Length > MaximumMaterialTagLength) return false;
            for (int i = 0; i < materialTag.Length; i++)
            {
                char c = materialTag[i];
                if (c < 32 || c == 127) return false;
            }
            return true;
        }
    }
}
