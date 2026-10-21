using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Network
{
    internal sealed class SavedAccountBan
    {
        internal string Id;
        internal string AccountHash;
        internal string Name;
        internal string Reason;
    }

    // Only transport-verified account bans belong in a save. Room credentials and
    // ordinary kicks expire with the room and must never become persistent identity.
    internal static class RoomModerationPersistence
    {
        private const int Magic = 0x424d4e4f;
        internal const int MaxBytes = 192 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string AccountHash(string scope, string account)
        {
            if (account == null) return null;
            using (var sha = new SHA256Managed())
                return Convert.ToBase64String(sha.ComputeHash(Utf8.GetBytes("OniOnline/account-ban/v1\n" + scope + "\n" + account)));
        }

        internal static string Encode(string scope, SavedAccountBan[] records)
        {
            Validate(scope, records);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1);
                writer.Write(Guid.ParseExact(scope, "N").ToByteArray());
                writer.Write((ushort)records.Length);
                foreach (var record in records.OrderBy(r => r.Id, StringComparer.Ordinal))
                {
                    writer.Write(Guid.ParseExact(record.Id, "N").ToByteArray());
                    writer.Write(Convert.FromBase64String(record.AccountHash));
                    WriteText(writer, record.Name); WriteText(writer, record.Reason);
                }
                writer.Flush();
                byte[] body = stream.ToArray();
                if (body.Length > MaxBytes - 32) throw Invalid();
                byte[] result = new byte[body.Length + 32];
                Buffer.BlockCopy(body, 0, result, 0, body.Length);
                using (var sha = new SHA256Managed())
                    Buffer.BlockCopy(sha.ComputeHash(body), 0, result, body.Length, 32);
                return Convert.ToBase64String(result);
            }
        }

        internal static SavedAccountBan[] Decode(string scope, string payload)
        {
            if (!ValidId(scope)) throw Invalid();
            if (string.IsNullOrEmpty(payload)) return new SavedAccountBan[0]; // Saves from earlier versions.
            if (payload.Length > ((MaxBytes + 2) / 3) * 4) throw Invalid();
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                if (bytes.Length < 55 || bytes.Length > MaxBytes || Convert.ToBase64String(bytes) != payload) throw Invalid();
                int bodyLength = bytes.Length - 32;
                using (var sha = new SHA256Managed())
                    if (!sha.ComputeHash(bytes, 0, bodyLength).SequenceEqual(bytes.Skip(bodyLength))) throw Invalid();
                using (var stream = new MemoryStream(bytes, 0, bodyLength, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadByte() != 1 ||
                        ReadId(reader) != scope) throw Invalid();
                    int count = reader.ReadUInt16();
                    if (count > RoomModeration.Limit || count > (stream.Length - stream.Position) / 52) throw Invalid();
                    var records = new SavedAccountBan[count];
                    for (int i = 0; i < count; i++)
                        records[i] = new SavedAccountBan { Id = ReadId(reader),
                            AccountHash = Convert.ToBase64String(ReadExact(reader, 32)),
                            Name = ReadText(reader, 128), Reason = ReadText(reader, 512) };
                    if (stream.Position != stream.Length) throw Invalid();
                    Validate(scope, records);
                    return records;
                }
            }
            catch (Exception error) when (error is FormatException || error is ArgumentException || error is IOException)
            { throw Invalid(); }
        }

        private static void Validate(string scope, SavedAccountBan[] records)
        {
            if (!ValidId(scope) || records == null || records.Length > RoomModeration.Limit) throw Invalid();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var accounts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || !ValidId(record.Id) || !ids.Add(record.Id) ||
                    !Protocol.ValidText(record.Name, 32) || record.Reason == null ||
                    (record.Reason.Length != 0 && !Protocol.ValidText(record.Reason, 128)) ||
                    record.AccountHash == null || record.AccountHash.Length != 44 || !accounts.Add(record.AccountHash)) throw Invalid();
                byte[] hash;
                try { hash = Convert.FromBase64String(record.AccountHash); }
                catch (FormatException) { throw Invalid(); }
                if (hash.Length != 32 || Convert.ToBase64String(hash) != record.AccountHash) throw Invalid();
            }
        }

        private static bool ValidId(string value)
        {
            Guid id;
            return value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out id) &&
                id != Guid.Empty && id.ToString("N") == value;
        }
        private static byte[] ReadExact(BinaryReader reader, int size)
        { byte[] bytes = reader.ReadBytes(size); if (bytes.Length != size) throw Invalid(); return bytes; }
        private static string ReadId(BinaryReader reader) { return new Guid(ReadExact(reader, 16)).ToString("N"); }
        private static void WriteText(BinaryWriter writer, string value)
        { byte[] bytes = Utf8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
        private static string ReadText(BinaryReader reader, int maxBytes)
        { int size = reader.ReadUInt16(); if (size > maxBytes) throw Invalid(); return Utf8.GetString(ReadExact(reader, size)); }
        private static InvalidDataException Invalid() { return new InvalidDataException("moderation_state_invalid"); }
    }
}
