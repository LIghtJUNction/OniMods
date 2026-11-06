using System;
using System.IO;

namespace OniOnline.Multiplayer
{
    internal static class DupeCommandWire
    {
        private const uint Magic = 0x44494e4f;
        internal const int MaxBytes = 1024;
        private static readonly CommandWireText Text = new CommandWireText("dupe_text");
        internal static bool IsMessage(byte[] data) { return data != null && data.Length >= 6 && BitConverter.ToUInt32(data, 0) == Magic; }
        internal static byte[] Encode(DupeCommandMessage message)
        {
            if (message == null || (message.Request == null ? 0 : 1) + (message.Query == null ? 0 : 1) + (message.Receipt == null ? 0 : 1) != 1)
                throw new InvalidDataException("dupe_message");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, CommandWireText.Encoding))
            {
                byte kind = message.Request != null ? (byte)1 : message.Receipt != null ? (byte)2 : (byte)3;
                writer.Write(Magic); writer.Write((byte)2); writer.Write(kind); Text.Write(writer, message.TargetId);
                if (message.Request != null || message.Query != null)
                {
                    var request = message.Request ?? message.Query;
                    if (!DupeCommandAuthority.Valid(request)) throw new InvalidDataException("dupe_request");
                    Text.Write(writer, request.RequestId); Text.Write(writer, request.PlanetId); Text.Write(writer, request.AuthorityEpoch);
                    writer.Write(request.DupeId); writer.Write(request.Target.X); writer.Write(request.Target.Y);
                    writer.Write(request.IsCancellation);
                    if (request.IsCancellation) Text.Write(writer, request.CancelTargetRequestId);
                }
                else
                {
                    var receipt = message.Receipt;
                    if (!Valid(receipt)) throw new InvalidDataException("dupe_receipt");
                    Text.Write(writer, receipt.RequestId); writer.Write(receipt.Accepted); writer.Write(receipt.OutcomeUnknown); Text.Write(writer, receipt.Reason);
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
            using (var reader = new BinaryReader(stream, CommandWireText.Encoding))
            {
                reader.ReadUInt32(); if (reader.ReadByte() != 2) throw new InvalidDataException("dupe_version");
                byte kind = reader.ReadByte();
                if (kind < 1 || kind > 3) throw new InvalidDataException("dupe_kind");
                var message = new DupeCommandMessage { TargetId = Text.Read(reader) };
                if (kind != 2)
                {
                    var request = new DupeMoveRequest { RequestId = Text.Read(reader), PlanetId = Text.Read(reader), AuthorityEpoch = Text.Read(reader),
                        DupeId = reader.ReadInt32(), Target = new WorkCell(reader.ReadInt32(), reader.ReadInt32()) };
                    if (Boolean(reader)) request.CancelTargetRequestId = Text.Read(reader);
                    if (!DupeCommandAuthority.Valid(request)) throw new InvalidDataException("dupe_request");
                    if (kind == 1) message.Request = request; else message.Query = request;
                }
                else
                {
                    message.Receipt = new DupeCommandReceipt { RequestId = Text.Read(reader), Accepted = Boolean(reader), OutcomeUnknown = Boolean(reader), Reason = Text.Read(reader),
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
    }
}
