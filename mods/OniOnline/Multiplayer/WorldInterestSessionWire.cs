using System;
using System.IO;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal enum WorldInterestSessionKind : byte { Subscribe = 1, Bind = 2, Delta = 3, Ack = 4, Cancel = 5, Denied = 6 }

    internal sealed class WorldInterestSessionMessage
    {
        internal WorldInterestSessionKind Kind;
        internal string RoomId, TargetId, PlanetId, Nonce, Epoch = "", Reason = "";
        internal long RequestNumber, SourceGeneration, Generation, Sequence, BindingNumber;
        internal bool Motion;
        internal WorldInterestSubscription Subscription;
        internal byte[] Data;
    }

    internal static class WorldInterestSessionWire
    {
        internal const int MaxBytes = 6144;
        private const uint Magic = 0x53494e4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] data)
        { return data != null && data.Length >= 6 && BitConverter.ToUInt32(data, 0) == Magic; }

        internal static byte[] Encode(WorldInterestSessionMessage message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)message.Kind);
                Text(writer, message.RoomId); Text(writer, message.TargetId); Text(writer, message.PlanetId); Text(writer, message.Nonce);
                writer.Write(message.RequestNumber); writer.Write(message.SourceGeneration); writer.Write(message.Generation); writer.Write(message.BindingNumber);
                Text(writer, message.Epoch); writer.Write(message.Motion); writer.Write(message.Sequence);
                if (message.Kind == WorldInterestSessionKind.Subscribe)
                {
                    var sub = message.Subscription; if (sub == null) throw new InvalidDataException("interest_subscription_missing");
                    writer.Write(sub.Revision); writer.Write(sub.Region.X); writer.Write(sub.Region.Y); writer.Write(sub.Region.Width); writer.Write(sub.Region.Height);
                    writer.Write((byte)sub.Channels); writer.Write(sub.HoverIndex); writer.Write(sub.NumericIntervalTicks);
                }
                else if (message.Kind == WorldInterestSessionKind.Delta)
                {
                    if (message.Data == null || message.Data.Length > 5000) throw new InvalidDataException("interest_delta_size");
                    writer.Write((ushort)message.Data.Length); writer.Write(message.Data);
                }
                else if (message.Kind == WorldInterestSessionKind.Denied) Text(writer, message.Reason);
                else if (message.Kind != WorldInterestSessionKind.Bind && message.Kind != WorldInterestSessionKind.Ack && message.Kind != WorldInterestSessionKind.Cancel)
                    throw new InvalidDataException("interest_message_kind");
                writer.Flush();
                if (stream.Length > MaxBytes) throw new InvalidDataException("interest_message_size");
                return stream.ToArray();
            }
        }

        internal static bool TryDecode(byte[] data, out WorldInterestSessionMessage message)
        {
            message = null;
            if (!IsMessage(data) || data.Length > MaxBytes) return false;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    reader.ReadUInt32(); if (reader.ReadByte() != 1) return false;
                    var value = new WorldInterestSessionMessage { Kind = (WorldInterestSessionKind)reader.ReadByte(), RoomId = Text(reader),
                        TargetId = Text(reader), PlanetId = Text(reader), Nonce = Text(reader), RequestNumber = reader.ReadInt64(),
                        SourceGeneration = reader.ReadInt64(), Generation = reader.ReadInt64(), BindingNumber = reader.ReadInt64(), Epoch = Text(reader) };
                    byte motion = reader.ReadByte(); if (motion > 1) return false; value.Motion = motion == 1; value.Sequence = reader.ReadInt64();
                    if (value.RoomId.Length != 32 || value.TargetId.Length != 32 || value.PlanetId.Length != 32 || value.Nonce.Length != 32 ||
                        value.RequestNumber < 1 || value.SourceGeneration < 0 || value.Generation < 0 || value.Sequence < 0 || value.BindingNumber < 0) return false;
                    if (value.Kind == WorldInterestSessionKind.Subscribe)
                        value.Subscription = new WorldInterestSubscription { Revision = reader.ReadInt64(), Region = new WorldInterestRegion
                        { X = reader.ReadInt32(), Y = reader.ReadInt32(), Width = reader.ReadInt32(), Height = reader.ReadInt32() },
                            Channels = (WorldInterestChannels)reader.ReadByte(), HoverIndex = reader.ReadInt32(), NumericIntervalTicks = reader.ReadInt32() };
                    else if (value.Kind == WorldInterestSessionKind.Delta)
                    {
                        int length = reader.ReadUInt16(); if (length > 5000 || length < 1 || length > stream.Length - stream.Position) return false;
                        value.Data = reader.ReadBytes(length);
                    }
                    else if (value.Kind == WorldInterestSessionKind.Denied) value.Reason = Text(reader);
                    else if (value.Kind != WorldInterestSessionKind.Bind && value.Kind != WorldInterestSessionKind.Ack && value.Kind != WorldInterestSessionKind.Cancel) return false;
                    if (stream.Position != stream.Length) return false;
                    message = value; return true;
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException || error is OverflowException) { return false; }
        }

        private static void Text(BinaryWriter writer, string value)
        {
            if (value == null || value.Length > 64) throw new InvalidDataException("interest_message_text");
            byte[] bytes = Utf8.GetBytes(value); if (bytes.Length > 192) throw new InvalidDataException("interest_message_text");
            writer.Write((byte)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int length = reader.ReadByte(); if (length > 192 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("interest_message_text");
            string value = Utf8.GetString(reader.ReadBytes(length)); if (value.Length > 64) throw new InvalidDataException("interest_message_text");
            foreach (char c in value) if (char.IsControl(c)) throw new InvalidDataException("interest_message_text");
            return value;
        }
    }
}
