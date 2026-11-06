using System;
using System.IO;
using System.Text;

namespace OniOnline.Multiplayer
{
    // Interest delta transport. The lane byte separates motion ACK/sequence from visual/numerical ACK/sequence.
    public static class WorldInterestWire
    {
        public const int MaxPacketBytes = 6000;
        private const uint Magic = 0x44494e4f;
        private const byte Version = 1;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(WorldInterestDelta delta, bool motionLane)
        {
            ValidateHeader(delta);
            if (delta.EstimatedBytes > MaxPacketBytes) throw new InvalidDataException("interest_packet_budget");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(motionLane);
                writer.Write((byte)delta.Epoch.Length); writer.Write(Utf8.GetBytes(delta.Epoch));
                writer.Write(delta.SubscriptionRevision); writer.Write(delta.Sequence); writer.Write(delta.BaseSequence); writer.Write(delta.Tick); writer.Write(delta.Reset);
                writer.Write((ushort)delta.Cells.Length); writer.Write((ushort)delta.RemovedCells.Length);
                writer.Write((ushort)delta.Entities.Length); writer.Write((ushort)delta.RemovedEntities.Length);
                foreach (var cell in delta.Cells)
                {
                    ValidateCell(cell);
                    if (motionLane) throw new InvalidDataException("motion_contains_cell");
                    writer.Write(cell.Index); writer.Write((byte)cell.Fields);
                    if ((cell.Fields & WorldInterestChannels.Visual) != 0) { writer.Write(cell.ElementHash); writer.Write(cell.Color); }
                    if ((cell.Fields & WorldInterestChannels.Temperature) != 0) writer.Write(cell.Temperature);
                    if ((cell.Fields & WorldInterestChannels.Mass) != 0) writer.Write(cell.Mass);
                }
                foreach (int index in delta.RemovedCells) { if (index < 0 || motionLane) throw new InvalidDataException("invalid_interest_remove"); writer.Write(index); }
                foreach (var entity in delta.Entities)
                {
                    ValidateEntity(entity);
                    writer.Write(entity.Id); writer.Write(entity.CellIndex); writer.Write(entity.Kind); writer.Write(entity.X); writer.Write(entity.Y);
                }
                foreach (int id in delta.RemovedEntities) { if (id < 0) throw new InvalidDataException("invalid_interest_remove"); writer.Write(id); }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new InvalidDataException("interest_packet_size");
                return stream.ToArray();
            }
        }

        public static bool TryDecode(byte[] data, out WorldInterestDelta delta, out bool motionLane)
        {
            delta = null; motionLane = false;
            if (data == null || data.Length < 48 || data.Length > MaxPacketBytes) return false;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadByte() != Version) return false;
                    byte lane = reader.ReadByte(); if (lane > 1) return false;
                    int epochLength = reader.ReadByte(); if (epochLength < 1 || epochLength > 64) return false;
                    var decoded = new WorldInterestDelta { Epoch = Utf8.GetString(reader.ReadBytes(epochLength)),
                        SubscriptionRevision = reader.ReadInt64(), Sequence = reader.ReadInt64(), BaseSequence = reader.ReadInt64(), Tick = reader.ReadInt64() };
                    byte reset = reader.ReadByte(); if (reset > 1) return false; decoded.Reset = reset == 1;
                    int cellCount = reader.ReadUInt16(), removeCellCount = reader.ReadUInt16(), entityCount = reader.ReadUInt16(), removeEntityCount = reader.ReadUInt16();
                    if (cellCount > WorldInterestPublisher.MaxVisibleCells || removeCellCount > WorldInterestPublisher.MaxVisibleCells ||
                        entityCount > WorldInterestPublisher.MaxVisibleEntities || removeEntityCount > WorldInterestPublisher.MaxVisibleEntities ||
                        (long)cellCount * 5 + (long)entityCount * 20 + ((long)removeCellCount + removeEntityCount) * 4 > stream.Length - stream.Position ||
                        (lane == 1 && (cellCount != 0 || removeCellCount != 0))) return false;
                    decoded.Cells = new WorldInterestCell[cellCount]; decoded.RemovedCells = new int[removeCellCount];
                    decoded.Entities = new WorldInterestEntity[entityCount]; decoded.RemovedEntities = new int[removeEntityCount];
                    for (int i = 0; i < cellCount; i++)
                    {
                        var cell = new WorldInterestCell { Index = reader.ReadInt32(), Fields = (WorldInterestChannels)reader.ReadByte(), Explored = true };
                        if ((cell.Fields & WorldInterestChannels.Visual) != 0) { cell.ElementHash = reader.ReadInt32(); cell.Color = reader.ReadUInt32(); }
                        if ((cell.Fields & WorldInterestChannels.Temperature) != 0) cell.Temperature = reader.ReadSingle();
                        if ((cell.Fields & WorldInterestChannels.Mass) != 0) cell.Mass = reader.ReadSingle();
                        ValidateCell(cell); decoded.Cells[i] = cell;
                    }
                    for (int i = 0; i < removeCellCount; i++) { int value = reader.ReadInt32(); if (value < 0) return false; decoded.RemovedCells[i] = value; }
                    for (int i = 0; i < entityCount; i++)
                    {
                        var value = new WorldInterestEntity { Id = reader.ReadInt32(), CellIndex = reader.ReadInt32(), Kind = reader.ReadInt32(), X = reader.ReadSingle(), Y = reader.ReadSingle() };
                        ValidateEntity(value); decoded.Entities[i] = value;
                    }
                    for (int i = 0; i < removeEntityCount; i++) { int value = reader.ReadInt32(); if (value < 0) return false; decoded.RemovedEntities[i] = value; }
                    ValidateHeader(decoded);
                    if (stream.Position != stream.Length || decoded.EstimatedBytes > MaxPacketBytes) return false;
                    delta = decoded; motionLane = lane == 1; return true;
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException)
            { return false; }
        }

        private static void ValidateHeader(WorldInterestDelta value)
        {
            if (value == null || !WorldInterestPublisher.ValidEpoch(value.Epoch) || value.SubscriptionRevision < 1 || value.Sequence < 1 ||
                value.BaseSequence < 0 || value.BaseSequence >= value.Sequence || value.Tick < 0 || value.Cells == null || value.RemovedCells == null ||
                value.Entities == null || value.RemovedEntities == null || value.Cells.Length > WorldInterestPublisher.MaxVisibleCells ||
                value.RemovedCells.Length > WorldInterestPublisher.MaxVisibleCells || value.Entities.Length > WorldInterestPublisher.MaxVisibleEntities ||
                value.RemovedEntities.Length > WorldInterestPublisher.MaxVisibleEntities) throw new InvalidDataException("invalid_interest_header");
        }
        private static void ValidateCell(WorldInterestCell value)
        {
            if (value.Index < 0 || !value.Explored || (value.Fields & ~(WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass)) != 0 ||
                !WorldInterestPublisher.Finite(value.Temperature) || value.Temperature < 0 || !WorldInterestPublisher.Finite(value.Mass) || value.Mass < 0 ||
                ((value.Fields & WorldInterestChannels.Temperature) == 0 && value.Temperature != 0) ||
                ((value.Fields & WorldInterestChannels.Mass) == 0 && value.Mass != 0) ||
                ((value.Fields & WorldInterestChannels.Visual) == 0 && (value.ElementHash != 0 || value.Color != 0))) throw new InvalidDataException("invalid_interest_cell");
        }
        private static void ValidateEntity(WorldInterestEntity value)
        {
            if (value.Id < 0 || value.CellIndex < 0 || !WorldInterestPublisher.Finite(value.X) || !WorldInterestPublisher.Finite(value.Y) || value.X < 0 || value.Y < 0)
                throw new InvalidDataException("invalid_interest_entity");
        }
    }
}
