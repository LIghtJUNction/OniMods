using System;
using System.IO;
using System.Text;

namespace OniOnline.Multiplayer
{
    internal static class DupeCommandWire
    {
        private const uint Magic = 0x44494e4f;
        internal const int MaxBytes = 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] data) { return data != null && data.Length >= 6 && BitConverter.ToUInt32(data, 0) == Magic; }
        internal static byte[] Encode(DupeCommandMessage message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write(message.Request != null); Text(writer, message.TargetId);
                if (message.Request != null)
                {
                    var request = message.Request;
                    if (!DupeCommandAuthority.Valid(request)) throw new InvalidDataException("dupe_request");
                    Text(writer, request.RequestId); Text(writer, request.PlanetId); Text(writer, request.AuthorityEpoch);
                    writer.Write(request.DupeId); writer.Write(request.Target.X); writer.Write(request.Target.Y);
                }
                else
                {
                    var receipt = message.Receipt;
                    if (receipt == null || receipt.Pending) throw new InvalidDataException("dupe_receipt");
                    Text(writer, receipt.RequestId); writer.Write(receipt.Accepted); writer.Write(receipt.OutcomeUnknown); Text(writer, receipt.Reason);
                }
                return stream.ToArray();
            }
        }
        internal static DupeCommandMessage Decode(byte[] data)
        {
            if (!IsMessage(data) || data.Length > MaxBytes) throw new InvalidDataException("dupe_packet");
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadUInt32(); if (reader.ReadByte() != 1) throw new InvalidDataException("dupe_version");
                bool isRequest = reader.ReadBoolean();
                var message = new DupeCommandMessage { TargetId = Text(reader) };
                if (isRequest)
                {
                    message.Request = new DupeMoveRequest { RequestId = Text(reader), PlanetId = Text(reader), AuthorityEpoch = Text(reader),
                        DupeId = reader.ReadInt32(), Target = new WorkCell(reader.ReadInt32(), reader.ReadInt32()) };
                    if (!DupeCommandAuthority.Valid(message.Request)) throw new InvalidDataException("dupe_request");
                }
                else
                {
                    message.Receipt = new DupeCommandReceipt { RequestId = Text(reader), Accepted = reader.ReadBoolean(), OutcomeUnknown = reader.ReadBoolean(), Reason = Text(reader) };
                    if (message.Receipt.Accepted && message.Receipt.OutcomeUnknown) throw new InvalidDataException("dupe_receipt");
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("dupe_trailing_data");
                return message;
            }
        }
        private static void Text(BinaryWriter writer, string value)
        {
            if (value == null || value.Length > 128) throw new InvalidDataException("dupe_text");
            byte[] bytes = Utf8.GetBytes(value);
            if (bytes.Length > 384) throw new InvalidDataException("dupe_text");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length > 384 || reader.BaseStream.Length - reader.BaseStream.Position < length) throw new InvalidDataException("dupe_text");
            string value = Utf8.GetString(reader.ReadBytes(length));
            if (value.Length > 128) throw new InvalidDataException("dupe_text");
            return value;
        }
    }
}
