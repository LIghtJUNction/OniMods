using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Multiplayer
{
    public static class WorldWire
    {
        public const int MaxCells = 262144;
        public const int MaxDupes = 128;
        public const int MaxPacketBytes = 5000;
        public const int MaxSnapshotBytes = 6 * 1024 * 1024;
        public const int ChunkDataBytes = 4400;
        private const uint Magic = 0x57494E4F; // ONIW, little endian.
        private const byte Version = 1;
        private const byte SnapshotBody = 128;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] EncodeDescriptor(WorldDescriptor descriptor)
        {
            ValidateDescriptor(descriptor);
            return WritePacket(WorldMessageKind.Descriptor, writer => WriteDescriptor(writer, descriptor));
        }

        public static byte[] EncodeRequest(string ownerId)
        {
            ValidateOwner(ownerId);
            return WritePacket(WorldMessageKind.SnapshotRequest, writer => WriteText(writer, ownerId, 32));
        }

        public static byte[] EncodeWithdraw(string worldId, string epoch, long controlRevision)
        {
            OwnerOf(worldId); ValidateEpoch(epoch);
            if (controlRevision < 1) throw new InvalidDataException("invalid_control_revision");
            return WritePacket(WorldMessageKind.Withdraw, writer =>
            {
                WriteText(writer, worldId, 48); WriteText(writer, epoch, 32); writer.Write(controlRevision);
            });
        }

        public static IReadOnlyList<byte[]> EncodeSnapshot(WorldSnapshot snapshot)
        {
            var data = EncodeSnapshotBody(snapshot);
            byte[] hash;
            using (var sha = new SHA256Managed()) hash = sha.ComputeHash(data);
            var count = (data.Length + ChunkDataBytes - 1) / ChunkDataBytes;
            var result = new List<byte[]>(count);
            var snapshotId = Guid.NewGuid().ToString("N");
            for (int index = 0; index < count; index++)
            {
                int offset = index * ChunkDataBytes;
                int length = Math.Min(ChunkDataBytes, data.Length - offset);
                int currentIndex = index;
                result.Add(WritePacket(WorldMessageKind.SnapshotChunk, writer =>
                {
                    WriteText(writer, snapshot.World.Id, 48);
                    WriteText(writer, snapshot.World.Epoch, 32);
                    WriteText(writer, snapshotId, 32);
                    writer.Write(snapshot.Sequence);
                    writer.Write(snapshot.World.ControlRevision);
                    writer.Write((ushort)currentIndex);
                    writer.Write((ushort)count);
                    writer.Write(data.Length);
                    writer.Write(hash);
                    writer.Write((ushort)length);
                    writer.Write(data, offset, length);
                }));
            }
            return result;
        }

        public static bool TryDecode(byte[] data, out WorldMessage message)
        {
            message = null;
            if (data == null || data.Length < 6 || data.Length > MaxPacketBytes) return false;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadByte() != Version) return false;
                    var decoded = new WorldMessage { Kind = (WorldMessageKind)reader.ReadByte() };
                    switch (decoded.Kind)
                    {
                        case WorldMessageKind.Descriptor:
                            decoded.Descriptor = ReadDescriptor(reader);
                            decoded.OwnerId = decoded.Descriptor.OwnerId;
                            decoded.WorldId = decoded.Descriptor.Id;
                            decoded.ControlRevision = decoded.Descriptor.ControlRevision;
                            break;
                        case WorldMessageKind.SnapshotRequest:
                            decoded.OwnerId = ReadText(reader, 32);
                            ValidateOwner(decoded.OwnerId);
                            break;
                        case WorldMessageKind.Withdraw:
                            decoded.WorldId = ReadText(reader, 48);
                            decoded.OwnerId = OwnerOf(decoded.WorldId);
                            decoded.Epoch = ReadText(reader, 32); ValidateEpoch(decoded.Epoch);
                            decoded.ControlRevision = reader.ReadInt64();
                            if (decoded.ControlRevision < 1) return false;
                            break;
                        case WorldMessageKind.SnapshotChunk:
                            var chunk = new WorldChunk
                            {
                                WorldId = ReadText(reader, 48),
                                Epoch = ReadText(reader, 32),
                                SnapshotId = ReadText(reader, 32),
                                Sequence = reader.ReadInt64(),
                                ControlRevision = reader.ReadInt64(),
                                Index = reader.ReadUInt16(),
                                Count = reader.ReadUInt16(),
                                TotalBytes = reader.ReadInt32(),
                                Hash = reader.ReadBytes(32)
                            };
                            decoded.OwnerId = OwnerOf(chunk.WorldId);
                            decoded.WorldId = chunk.WorldId;
                            ValidateEpoch(chunk.Epoch); ValidateEpoch(chunk.SnapshotId);
                            if (chunk.Sequence < 1 || chunk.ControlRevision < 1 || chunk.TotalBytes < 1 || chunk.TotalBytes > MaxSnapshotBytes ||
                                chunk.Count != (chunk.TotalBytes + ChunkDataBytes - 1) / ChunkDataBytes ||
                                chunk.Index >= chunk.Count || chunk.Hash.Length != 32) return false;
                            int length = reader.ReadUInt16();
                            int expected = Math.Min(ChunkDataBytes, chunk.TotalBytes - chunk.Index * ChunkDataBytes);
                            if (length != expected) return false;
                            chunk.Data = reader.ReadBytes(length);
                            if (chunk.Data.Length != length) return false;
                            decoded.Chunk = chunk;
                            decoded.ControlRevision = chunk.ControlRevision;
                            break;
                        default: return false;
                    }
                    if (stream.Position != stream.Length) return false;
                    message = decoded;
                    return true;
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException)
            {
                return false;
            }
        }

        public static void ValidateDescriptor(WorldDescriptor world)
        {
            if (world == null) throw new InvalidDataException("missing_world");
            ValidateOwner(world.OwnerId); ValidateEpoch(world.Epoch);
            if (world.NativeWorldId < 0 || world.ControlRevision < 1 || world.Id != world.OwnerId + ":" + world.NativeWorldId.ToString(CultureInfo.InvariantCulture) ||
                world.Width < 1 || world.Width > 1024 || world.Height < 1 || world.Height > 1024 ||
                !Finite(world.SimulationTime) || world.SimulationTime < 0)
                throw new InvalidDataException("invalid_world");
            ValidateText(world.Name, 128);
        }

        public static WorldDescriptor CopyDescriptor(WorldDescriptor world)
        {
            return new WorldDescriptor
            {
                Id = world.Id, OwnerId = world.OwnerId, Name = world.Name, Epoch = world.Epoch,
                ControlRevision = world.ControlRevision,
                NativeWorldId = world.NativeWorldId, Width = world.Width, Height = world.Height,
                Shared = world.Shared, SimulationTime = world.SimulationTime
            };
        }

        public static string OwnerOf(string worldId)
        {
            if (string.IsNullOrEmpty(worldId) || worldId.Length > 48) throw new InvalidDataException("invalid_world_id");
            int colon = worldId.IndexOf(':');
            int nativeId;
            if (colon < 1 || !int.TryParse(worldId.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out nativeId) ||
                nativeId < 0 || worldId.Substring(colon + 1) != nativeId.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("invalid_world_id");
            string owner = worldId.Substring(0, colon);
            ValidateOwner(owner);
            return owner;
        }

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        internal static WorldSnapshot DecodeSnapshotBody(byte[] data)
        {
            if (data == null || data.Length > MaxSnapshotBytes) throw new InvalidDataException("snapshot_size");
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadByte() != Version || reader.ReadByte() != SnapshotBody)
                    throw new InvalidDataException("snapshot_version");
                var snapshot = new WorldSnapshot { World = ReadDescriptor(reader), Sequence = reader.ReadInt64() };
                int count = reader.ReadInt32();
                if (snapshot.Sequence < 1 || count < 0 || count > MaxCells || count > snapshot.World.Width * snapshot.World.Height ||
                    count > (stream.Length - stream.Position) / 20 || (!snapshot.World.Shared && count != 0))
                    throw new InvalidDataException("snapshot_cells");
                snapshot.Cells = new WorldCell[count];
                var visible = new HashSet<int>();
                for (int index = 0; index < count; index++)
                {
                    var cell = new WorldCell
                    {
                        Index = reader.ReadInt32(), ElementHash = reader.ReadInt32(), Mass = reader.ReadSingle(),
                        Temperature = reader.ReadSingle(), Color = reader.ReadUInt32()
                    };
                    ValidateCell(cell, snapshot.World);
                    if (!visible.Add(cell.Index)) throw new InvalidDataException("duplicate_cell");
                    snapshot.Cells[index] = cell;
                }
                count = reader.ReadInt32();
                if (count < 0 || count > MaxDupes || (!snapshot.World.Shared && count != 0)) throw new InvalidDataException("snapshot_dupes");
                snapshot.Dupes = new WorldDupe[count];
                var ids = new HashSet<int>();
                for (int index = 0; index < count; index++)
                {
                    var dupe = new WorldDupe { Id = reader.ReadInt32(), Index = reader.ReadInt32(), Name = ReadText(reader, 64) };
                    if (dupe.Id < 0 || !visible.Contains(dupe.Index) || !ids.Add(dupe.Id)) throw new InvalidDataException("snapshot_dupe");
                    snapshot.Dupes[index] = dupe;
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("snapshot_trailing_bytes");
                return snapshot;
            }
        }

        private static byte[] EncodeSnapshotBody(WorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Sequence < 1) throw new InvalidDataException("invalid_snapshot");
            ValidateDescriptor(snapshot.World);
            // A default/private world can never accidentally publish internal data.
            var cells = snapshot.World.Shared ? snapshot.Cells ?? new WorldCell[0] : new WorldCell[0];
            var dupes = snapshot.World.Shared ? snapshot.Dupes ?? new WorldDupe[0] : new WorldDupe[0];
            if (cells.Length > MaxCells || cells.Length > snapshot.World.Width * snapshot.World.Height || dupes.Length > MaxDupes)
                throw new InvalidDataException("snapshot_count");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(SnapshotBody);
                WriteDescriptor(writer, snapshot.World); writer.Write(snapshot.Sequence);
                writer.Write(cells.Length);
                var visible = new HashSet<int>();
                foreach (var cell in cells)
                {
                    ValidateCell(cell, snapshot.World);
                    if (!visible.Add(cell.Index)) throw new InvalidDataException("duplicate_cell");
                    writer.Write(cell.Index); writer.Write(cell.ElementHash); writer.Write(cell.Mass);
                    writer.Write(cell.Temperature); writer.Write(cell.Color);
                }
                writer.Write(dupes.Length);
                var ids = new HashSet<int>();
                foreach (var dupe in dupes)
                {
                    if (dupe == null || dupe.Id < 0 || !visible.Contains(dupe.Index) || !ids.Add(dupe.Id))
                        throw new InvalidDataException("snapshot_dupe");
                    writer.Write(dupe.Id); writer.Write(dupe.Index); WriteText(writer, dupe.Name, 64);
                }
                writer.Flush();
                if (stream.Length > MaxSnapshotBytes) throw new InvalidDataException("snapshot_size");
                return stream.ToArray();
            }
        }

        private static void ValidateCell(WorldCell cell, WorldDescriptor world)
        {
            if (cell.Index < 0 || cell.Index >= world.Width * world.Height || !Finite(cell.Mass) || cell.Mass < 0 ||
                !Finite(cell.Temperature) || cell.Temperature < 0) throw new InvalidDataException("snapshot_cell");
        }

        private static byte[] WritePacket(WorldMessageKind kind, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write((byte)kind); write(writer); writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new InvalidDataException("world_packet_size");
                return stream.ToArray();
            }
        }

        private static void WriteDescriptor(BinaryWriter writer, WorldDescriptor world)
        {
            WriteText(writer, world.Id, 48); WriteText(writer, world.OwnerId, 32); WriteText(writer, world.Name, 128);
            WriteText(writer, world.Epoch, 32); writer.Write(world.ControlRevision); writer.Write(world.NativeWorldId); writer.Write(world.Width);
            writer.Write(world.Height); writer.Write(world.Shared); writer.Write(world.SimulationTime);
        }

        private static WorldDescriptor ReadDescriptor(BinaryReader reader)
        {
            var world = new WorldDescriptor
            {
                Id = ReadText(reader, 48), OwnerId = ReadText(reader, 32), Name = ReadText(reader, 128),
                Epoch = ReadText(reader, 32), ControlRevision = reader.ReadInt64(), NativeWorldId = reader.ReadInt32(), Width = reader.ReadInt32(), Height = reader.ReadInt32()
            };
            byte shared = reader.ReadByte();
            if (shared > 1) throw new InvalidDataException("world_shared");
            world.Shared = shared == 1; world.SimulationTime = reader.ReadDouble();
            ValidateDescriptor(world);
            return world;
        }

        private static void ValidateOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner) || owner.Length > 32) throw new InvalidDataException("invalid_world_owner");
            foreach (char value in owner)
                if (!(value >= 'a' && value <= 'z') && !(value >= 'A' && value <= 'Z') && !(value >= '0' && value <= '9') &&
                    value != '-' && value != '_' && value != '.') throw new InvalidDataException("invalid_world_owner");
        }

        private static void ValidateEpoch(string epoch)
        {
            if (epoch == null || epoch.Length != 32) throw new InvalidDataException("invalid_world_epoch");
            foreach (char value in epoch)
                if (!(value >= 'a' && value <= 'f') && !(value >= 'A' && value <= 'F') && !(value >= '0' && value <= '9'))
                    throw new InvalidDataException("invalid_world_epoch");
        }

        private static void ValidateText(string value, int maxChars)
        {
            if (value == null || value.Length > maxChars) throw new InvalidDataException("world_text");
            foreach (char character in value) if (char.IsControl(character)) throw new InvalidDataException("world_text");
            Utf8.GetByteCount(value);
        }

        private static void WriteText(BinaryWriter writer, string value, int maxChars)
        {
            ValidateText(value, maxChars);
            var data = Utf8.GetBytes(value); writer.Write((ushort)data.Length); writer.Write(data);
        }

        private static string ReadText(BinaryReader reader, int maxChars)
        {
            int length = reader.ReadUInt16();
            if (length > maxChars * 4) throw new InvalidDataException("world_text_size");
            byte[] data = reader.ReadBytes(length);
            if (data.Length != length) throw new EndOfStreamException();
            string value = Utf8.GetString(data); ValidateText(value, maxChars); return value;
        }
    }
}
