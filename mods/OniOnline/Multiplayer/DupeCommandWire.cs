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
            if (message == null || (message.Request == null ? 0 : 1) + (message.Query == null ? 0 : 1) + (message.Receipt == null ? 0 : 1) != 1)
                throw new InvalidDataException("dupe_message");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                byte kind = message.Request != null ? (byte)1 : message.Receipt != null ? (byte)2 : (byte)3;
                writer.Write(Magic); writer.Write((byte)2); writer.Write(kind); Text(writer, message.TargetId);
                if (message.Request != null || message.Query != null)
                {
                    var request = message.Request ?? message.Query;
                    if (!DupeCommandAuthority.Valid(request)) throw new InvalidDataException("dupe_request");
                    Text(writer, request.RequestId); Text(writer, request.PlanetId); Text(writer, request.AuthorityEpoch);
                    writer.Write(request.DupeId); writer.Write(request.Target.X); writer.Write(request.Target.Y);
                    writer.Write(request.IsCancellation);
                    if (request.IsCancellation) Text(writer, request.CancelTargetRequestId);
                }
                else
                {
                    var receipt = message.Receipt;
                    if (!Valid(receipt)) throw new InvalidDataException("dupe_receipt");
                    Text(writer, receipt.RequestId); writer.Write(receipt.Accepted); writer.Write(receipt.OutcomeUnknown); Text(writer, receipt.Reason);
                    writer.Write((byte)receipt.State); writer.Write((byte)receipt.Revision);
                }
                if (stream.Length > MaxBytes) throw new InvalidDataException("dupe_packet");
                return stream.ToArray();
            }
        }
        internal static DupeCommandMessage Decode(byte[] data)
        {
            if (!IsMessage(data) || data.Length > MaxBytes) throw new InvalidDataException("dupe_packet");
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadUInt32(); if (reader.ReadByte() != 2) throw new InvalidDataException("dupe_version");
                byte kind = reader.ReadByte();
                if (kind < 1 || kind > 3) throw new InvalidDataException("dupe_kind");
                var message = new DupeCommandMessage { TargetId = Text(reader) };
                if (kind != 2)
                {
                    var request = new DupeMoveRequest { RequestId = Text(reader), PlanetId = Text(reader), AuthorityEpoch = Text(reader),
                        DupeId = reader.ReadInt32(), Target = new WorkCell(reader.ReadInt32(), reader.ReadInt32()) };
                    if (Boolean(reader)) request.CancelTargetRequestId = Text(reader);
                    if (!DupeCommandAuthority.Valid(request)) throw new InvalidDataException("dupe_request");
                    if (kind == 1) message.Request = request; else message.Query = request;
                }
                else
                {
                    message.Receipt = new DupeCommandReceipt { RequestId = Text(reader), Accepted = Boolean(reader), OutcomeUnknown = Boolean(reader), Reason = Text(reader),
                        State = (DupeCommandState)reader.ReadByte(), Revision = reader.ReadByte() };
                    if (!Valid(message.Receipt)) throw new InvalidDataException("dupe_receipt");
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("dupe_trailing_data");
                return message;
            }
        }
        private static bool Boolean(BinaryReader reader)
        { byte value = reader.ReadByte(); if (value > 1) throw new InvalidDataException("dupe_boolean"); return value != 0; }
        private static bool Valid(DupeCommandReceipt receipt)
        {
            return receipt != null && DupeCommandAuthority.Id(receipt.RequestId) && !receipt.Pending &&
                !(receipt.Accepted && receipt.OutcomeUnknown) && receipt.Revision >= 1 && receipt.Revision <= 3 &&
                receipt.State >= DupeCommandState.None && receipt.State <= DupeCommandState.WorldChanged &&
                (receipt.State == DupeCommandState.None ? receipt.Revision == 1 :
                    receipt.State == DupeCommandState.Active ? receipt.Accepted && receipt.Revision == 2 : !receipt.OutcomeUnknown && receipt.Revision == 3);
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
