using System;
using System.Collections.Generic;
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
    /// mopping, prioritizing) into a single bounded payload under 256 bytes.
    /// </summary>
    public static class CellBatchCodec
    {
        public const int MaximumCellsPerBatch = 4096;
        public const int MaximumPayloadSize = 4096;

        public static byte[] EncodeBox(
            int worldId,
            int startX, int startY,
            int endX, int endY,
            string toolId,
            int priority = 5,
            string materialTag = "")
        {
            if (string.IsNullOrEmpty(toolId) || toolId.Length > 32)
                throw new ArgumentException("invalid_tool_id", nameof(toolId));
            if (priority < 1 || priority > 9)
                throw new ArgumentOutOfRangeException(nameof(priority), "priority must be 1..9");

            using (var ms = new MemoryStream(64))
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write((byte)CellBatchType.Box);
                writer.Write(worldId);
                writer.Write((short)startX);
                writer.Write((short)startY);
                writer.Write((short)endX);
                writer.Write((short)endY);
                writer.Write((byte)priority);
                writer.Write(toolId);
                writer.Write(materialTag ?? string.Empty);
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
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (reader.ReadByte() != (byte)CellBatchType.Box) return false;
                    worldId = reader.ReadInt32();
                    startX = reader.ReadInt16();
                    startY = reader.ReadInt16();
                    endX = reader.ReadInt16();
                    endY = reader.ReadInt16();
                    priority = reader.ReadByte();
                    if (priority < 1 || priority > 9) return false;
                    toolId = reader.ReadString();
                    materialTag = reader.ReadString();
                    return toolId.Length > 0 && toolId.Length <= 32;
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
            if (string.IsNullOrEmpty(toolId) || toolId.Length > 32)
                throw new ArgumentException("invalid_tool_id", nameof(toolId));
            if (priority < 1 || priority > 9)
                throw new ArgumentOutOfRangeException(nameof(priority), "priority must be 1..9");

            using (var ms = new MemoryStream(Math.Min(MaximumPayloadSize, 32 + cells.Length * 4)))
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write((byte)CellBatchType.DiscreteCells);
                writer.Write(worldId);
                writer.Write((byte)priority);
                writer.Write(toolId);
                writer.Write(materialTag ?? string.Empty);
                writer.Write((ushort)cells.Length);

                // Delta compression: store anchor cell and subsequent relative offsets
                int anchor = cells[0];
                writer.Write(anchor);
                for (int i = 1; i < cells.Length; i++)
                {
                    int delta = cells[i] - cells[i - 1];
                    writer.Write(delta);
                }
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
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (reader.ReadByte() != (byte)CellBatchType.DiscreteCells) return false;
                    worldId = reader.ReadInt32();
                    priority = reader.ReadByte();
                    if (priority < 1 || priority > 9) return false;
                    toolId = reader.ReadString();
                    materialTag = reader.ReadString();
                    int count = reader.ReadUInt16();
                    if (count <= 0 || count > MaximumCellsPerBatch) return false;

                    cells = new int[count];
                    int anchor = reader.ReadInt32();
                    cells[0] = anchor;
                    for (int i = 1; i < count; i++)
                    {
                        int delta = reader.ReadInt32();
                        cells[i] = cells[i - 1] + delta;
                    }
                    return toolId.Length > 0 && toolId.Length <= 32;
                }
            }
            catch (Exception)
            {
                cells = null;
                return false;
            }
        }
    }
}
