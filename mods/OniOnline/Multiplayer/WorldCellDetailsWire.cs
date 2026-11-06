using System;
using System.IO;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal sealed class WorldCellDetailResult
    {
        internal string Epoch;
        internal int Width, Height;
        internal long Sequence;
        internal WorldInterestCell Cell;
        internal WorldCellDetailResult Copy() { return (WorldCellDetailResult)MemberwiseClone(); }
    }

    internal enum WorldCellDetailsKind : byte { Watch = 1, Cancel = 2, Value = 3, Denied = 4, Ack = 5 }
    internal sealed class WorldCellDetailsMessage
    {
        internal WorldCellDetailsKind Kind;
        internal string RoomId, TargetId, PlanetId, Nonce, Epoch;
        internal int Index;
        internal long Request, Sequence;
        internal WorldCellDetailResult Result;
    }

    // A separate one-cell envelope: moving the pointer does not change the viewport subscription.
    internal static class WorldCellDetailsWire
    {
        internal const int MaxPacketBytes = 512;
        private const uint Magic = 0x43444e4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] data)
        { return data != null && data.Length >= 4 && BitConverter.ToUInt32(data, 0) == Magic; }

        internal static byte[] Encode(WorldCellDetailsMessage message)
        {
            Validate(message);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)message.Kind);
                Write(writer, message.RoomId); Write(writer, message.TargetId); Write(writer, message.PlanetId);
                Write(writer, message.Nonce); Write(writer, message.Epoch);
                writer.Write(message.Index); writer.Write(message.Request); writer.Write(message.Sequence);
                if (message.Kind == WorldCellDetailsKind.Value)
                {
                    var value = message.Result;
                    writer.Write(value.Width); writer.Write(value.Height);
                    writer.Write(value.Cell.ElementHash); writer.Write(value.Cell.Color);
                    writer.Write(value.Cell.Temperature); writer.Write(value.Cell.Mass);
                }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new InvalidDataException("cell_details_packet_budget");
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] bytes, out WorldCellDetailsMessage result)
        {
            result = null;
            if (bytes == null || bytes.Length < 32 || bytes.Length > MaxPacketBytes) return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadByte() != 1) return false;
                    var message = new WorldCellDetailsMessage { Kind = (WorldCellDetailsKind)reader.ReadByte(),
                        RoomId = Read(reader), TargetId = Read(reader), PlanetId = Read(reader), Nonce = Read(reader), Epoch = Read(reader),
                        Index = reader.ReadInt32(), Request = reader.ReadInt64(), Sequence = reader.ReadInt64() };
                    if (message.Kind == WorldCellDetailsKind.Value)
                    {
                        message.Result = new WorldCellDetailResult { Epoch = message.Epoch, Sequence = message.Sequence,
                            Width = reader.ReadInt32(), Height = reader.ReadInt32(),
                            Cell = new WorldInterestCell { Index = message.Index, Explored = true,
                                Fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass,
                                ElementHash = reader.ReadInt32(), Color = reader.ReadUInt32(),
                                Temperature = reader.ReadSingle(), Mass = reader.ReadSingle() } };
                    }
                    Validate(message);
                    if (stream.Position != stream.Length) return false;
                    result = message; return true;
                }
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is OverflowException)
            { return false; }
        }

        internal static bool ValidResult(WorldCellDetailResult value, string epoch, int index)
        {
            const WorldInterestChannels fields = WorldInterestChannels.Visual | WorldInterestChannels.Temperature | WorldInterestChannels.Mass;
            return value != null && value.Epoch == epoch && value.Width >= 1 && value.Height >= 1 &&
                value.Width <= 4096 && value.Height <= 4096 && (long)value.Width * value.Height <= 262144 &&
                index >= 0 && index < (long)value.Width * value.Height && value.Cell.Index == index &&
                value.Cell.Explored && value.Cell.Fields == fields && Finite(value.Cell.Temperature) && Finite(value.Cell.Mass);
        }
        private static bool Finite(float value) { return value >= 0 && !float.IsInfinity(value); }
        private static void Validate(WorldCellDetailsMessage message)
        {
            if (message == null || message.Kind < WorldCellDetailsKind.Watch || message.Kind > WorldCellDetailsKind.Ack ||
                !Token(message.RoomId) || !Token(message.TargetId) || !Token(message.PlanetId) || !Token(message.Nonce) ||
                !Token(message.Epoch) || message.Index < 0 || message.Index >= 262144 || message.Request < 1 || message.Sequence < 0 ||
                ((message.Kind == WorldCellDetailsKind.Value || message.Kind == WorldCellDetailsKind.Ack) ? message.Sequence < 1 : message.Sequence != 0) ||
                (message.Kind == WorldCellDetailsKind.Value && !ValidResult(message.Result, message.Epoch, message.Index)) ||
                (message.Kind != WorldCellDetailsKind.Value && message.Result != null)) throw new InvalidDataException("invalid_cell_details");
        }
        private static bool Token(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    c != '-' && c != '_' && c != '.' && c != ':') return false;
            return true;
        }
        private static void Write(BinaryWriter writer, string value)
        { byte[] bytes = Utf8.GetBytes(value); writer.Write((byte)bytes.Length); writer.Write(bytes); }
        private static string Read(BinaryReader reader)
        {
            int count = reader.ReadByte(); if (count < 1 || count > 64) throw new InvalidDataException("cell_details_token");
            byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException();
            return Utf8.GetString(bytes);
        }
    }
}
