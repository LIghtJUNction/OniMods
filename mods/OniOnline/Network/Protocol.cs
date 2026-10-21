using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OniOnline.Network
{
    public enum PacketKind { Challenge, Hello, Welcome, Roster, ChatSubmit, Chat, StartGame, Ping, Pong, Reject, ChatAck, Leave, HistoryRequest, Application, HistoryReset, Removed, RosterPart, LeaveAck }

    public sealed class PlayerInfo
    {
        public string Id;
        public string Name;
        public bool Connected = true;
        public int Number;
    }

    public sealed class Packet
    {
        public int Protocol;
        public PacketKind Kind;
        public string Room;
        public string Name;
        public string Text;
        public string Nonce;
        public string Proof;
        public string ResumeToken;
        public string ResumeProof;
        public string GameVersion;
        public string CycleTrimVersion;
        public bool SpacedOut;
        public string PlayerId;
        public long Sequence;
        public long MessageId;
        public long HistoryStart;
        public byte[] Payload;
        public bool Started;
        public PlayerInfo[] Players;
        public long RosterRevision;
        public int RosterTotal;
        public int RosterOffset;
    }

    public static class Protocol
    {
        public const int Version = 6;
        public const int MaxPacketBytes = 8192;
        public const int RosterPageSize = 32;
        public const int MaxChatLength = 512;
        public const int MaxApplicationBytes = 6144;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(Packet packet)
        {
            packet.Protocol = Version;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(0x4F494E4F); // ASCII ONIO, little endian.
                writer.Write((ushort)Version);
                writer.Write((byte)packet.Kind);
                switch (packet.Kind)
                {
                    case PacketKind.Challenge:
                        WriteText(writer, packet.Room, 32); WriteText(writer, packet.Nonce, 44); break;
                    case PacketKind.Hello:
                        WriteText(writer, packet.Name, 32); WriteText(writer, packet.GameVersion, 64);
                        WriteText(writer, packet.CycleTrimVersion, 64); writer.Write(packet.SpacedOut);
                        WriteText(writer, packet.Nonce, 44); WriteText(writer, packet.Proof, 44);
                        WriteText(writer, packet.PlayerId ?? "", 32); WriteText(writer, packet.ResumeToken ?? "", 44);
                        WriteText(writer, packet.ResumeProof ?? "", 44); writer.Write(packet.Sequence); break;
                    case PacketKind.Welcome:
                        WriteText(writer, packet.Room, 32); WriteText(writer, packet.Name, 64);
                        WriteText(writer, packet.PlayerId, 32); writer.Write(packet.Started); writer.Write(packet.Sequence);
                        writer.Write(packet.HistoryStart); WritePlayers(writer, packet.Players); break;
                    case PacketKind.Roster: WritePlayers(writer, packet.Players); break;
                    case PacketKind.RosterPart:
                        writer.Write(packet.RosterRevision); writer.Write(packet.RosterTotal); writer.Write(packet.RosterOffset);
                        WritePlayers(writer, packet.Players); break;
                    case PacketKind.ChatSubmit: writer.Write(packet.MessageId); WriteText(writer, packet.Text, MaxChatLength); break;
                    case PacketKind.Chat:
                        writer.Write(packet.Sequence); WriteText(writer, packet.PlayerId, 32);
                        WriteText(writer, packet.Name, 32); WriteText(writer, packet.Text, MaxChatLength); writer.Write(packet.MessageId); break;
                    case PacketKind.Reject: WriteText(writer, packet.Text, 64); break;
                    case PacketKind.Removed: WriteText(writer, packet.Name, 64); WriteText(writer, packet.Text, 128); break;
                    case PacketKind.ChatAck: writer.Write(packet.MessageId); break;
                    case PacketKind.HistoryRequest: case PacketKind.HistoryReset: writer.Write(packet.Sequence); break;
                    case PacketKind.Application:
                        WriteText(writer, packet.PlayerId ?? "", 32);
                        if (packet.Payload == null || packet.Payload.Length > MaxApplicationBytes) throw new InvalidDataException("invalid_payload");
                        writer.Write((ushort)packet.Payload.Length); writer.Write(packet.Payload); break;
                    case PacketKind.Ping: case PacketKind.Pong: writer.Write(packet.Sequence); writer.Write(packet.MessageId); break;
                    case PacketKind.StartGame: case PacketKind.Leave: case PacketKind.LeaveAck: break;
                    default: throw new InvalidDataException("invalid_packet_kind");
                }
                writer.Flush();
                if (stream.Length > MaxPacketBytes) throw new InvalidDataException("packet_too_large");
                return stream.ToArray();
            }
        }

        public static Packet Decode(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxPacketBytes)
                throw new InvalidDataException("invalid_packet");
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream, Utf8))
                {
                    if (reader.ReadInt32() != 0x4F494E4F || reader.ReadUInt16() != Version)
                        throw new InvalidDataException("protocol_mismatch");
                    var packet = new Packet { Protocol = Version, Kind = (PacketKind)reader.ReadByte() };
                    switch (packet.Kind)
                    {
                        case PacketKind.Challenge:
                            packet.Room = ReadText(reader, 32); packet.Nonce = ReadText(reader, 44); break;
                        case PacketKind.Hello:
                            packet.Name = ReadText(reader, 32); packet.GameVersion = ReadText(reader, 64);
                            packet.CycleTrimVersion = ReadText(reader, 64); packet.SpacedOut = ReadBool(reader);
                            packet.Nonce = ReadText(reader, 44); packet.Proof = ReadText(reader, 44);
                            packet.PlayerId = ReadText(reader, 32); packet.ResumeToken = ReadText(reader, 44);
                            packet.ResumeProof = ReadText(reader, 44); packet.Sequence = reader.ReadInt64(); break;
                        case PacketKind.Welcome:
                            packet.Room = ReadText(reader, 32); packet.Name = ReadText(reader, 64);
                            packet.PlayerId = ReadText(reader, 32); packet.Started = ReadBool(reader);
                            packet.Sequence = reader.ReadInt64(); packet.HistoryStart = reader.ReadInt64(); packet.Players = ReadPlayers(reader); break;
                        case PacketKind.Roster: packet.Players = ReadPlayers(reader); break;
                        case PacketKind.RosterPart:
                            packet.RosterRevision = reader.ReadInt64(); packet.RosterTotal = reader.ReadInt32();
                            packet.RosterOffset = reader.ReadInt32(); packet.Players = ReadPlayers(reader); break;
                        case PacketKind.ChatSubmit: packet.MessageId = reader.ReadInt64(); packet.Text = ReadText(reader, MaxChatLength); break;
                        case PacketKind.Chat:
                            packet.Sequence = reader.ReadInt64(); packet.PlayerId = ReadText(reader, 32);
                            packet.Name = ReadText(reader, 32); packet.Text = ReadText(reader, MaxChatLength); packet.MessageId = reader.ReadInt64(); break;
                        case PacketKind.Reject: packet.Text = ReadText(reader, 64); break;
                        case PacketKind.Removed: packet.Name = ReadText(reader, 64); packet.Text = ReadText(reader, 128); break;
                        case PacketKind.ChatAck: packet.MessageId = reader.ReadInt64(); break;
                        case PacketKind.HistoryRequest: case PacketKind.HistoryReset: packet.Sequence = reader.ReadInt64(); break;
                        case PacketKind.Application:
                            packet.PlayerId = ReadText(reader, 32); int length = reader.ReadUInt16();
                            if (length > MaxApplicationBytes) throw new InvalidDataException("invalid_payload");
                            packet.Payload = reader.ReadBytes(length);
                            if (packet.Payload.Length != length) throw new EndOfStreamException(); break;
                        case PacketKind.Ping: case PacketKind.Pong: packet.Sequence = reader.ReadInt64(); packet.MessageId = reader.ReadInt64(); break;
                        case PacketKind.StartGame: case PacketKind.Leave: case PacketKind.LeaveAck: break;
                        default: throw new InvalidDataException("invalid_packet_kind");
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("trailing_packet_data");
                    return packet;
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("truncated_packet", error); }
        }

        private static void WriteText(BinaryWriter writer, string text, int limit)
        {
            if (text == null || text.Length > limit) throw new InvalidDataException("invalid_text_length");
            byte[] bytes = Utf8.GetBytes(text);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int limit)
        {
            int length = reader.ReadUInt16();
            if (length > limit * 4) throw new InvalidDataException("invalid_text_length");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            string text = Utf8.GetString(bytes);
            if (text.Length > limit) throw new InvalidDataException("invalid_text_length");
            return text;
        }

        private static bool ReadBool(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("invalid_boolean");
            return value == 1;
        }

        private static void WritePlayers(BinaryWriter writer, PlayerInfo[] players)
        {
            if (players == null || players.Length < 1 || players.Length > RosterPageSize) throw new InvalidDataException("invalid_roster");
            writer.Write((byte)players.Length);
            foreach (var player in players) { WriteText(writer, player.Id, 32); WriteText(writer, player.Name, 32); writer.Write(player.Number); writer.Write(player.Connected); }
        }

        private static PlayerInfo[] ReadPlayers(BinaryReader reader)
        {
            int count = reader.ReadByte();
            if (count < 1 || count > RosterPageSize) throw new InvalidDataException("invalid_roster");
            var players = new PlayerInfo[count];
            for (int i = 0; i < count; i++) players[i] = new PlayerInfo { Id = ReadText(reader, 32), Name = ReadText(reader, 32), Number = reader.ReadInt32(), Connected = ReadBool(reader) };
            return players;
        }

        public static bool ValidText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength) return false;
            for (int i = 0; i < text.Length; i++)
                if (char.IsControl(text[i]) || (char.IsSurrogate(text[i]) &&
                    !(char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[++i]))))
                    return false;
            return true;
        }

        public static string NewNonce()
        {
            var bytes = new byte[32];
            using (var random = new RNGCryptoServiceProvider()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        public static string PasswordProof(string password, string challenge, Packet hello)
        {
            var material = challenge + "\n" + hello.Nonce + "\n" + hello.GameVersion + "\n" +
                hello.CycleTrimVersion + "\n" + hello.Name + "\n" + hello.PlayerId + "\n" +
                hello.ResumeToken + "\n" + hello.ResumeProof + "\n" + hello.Sequence;
            using (var sha = new SHA256Managed())
            using (var hmac = new HMACSHA256(sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? ""))))
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(material)));
        }

        public static string ResumeProof(string secret, string challenge, Packet hello)
        {
            if (string.IsNullOrEmpty(secret)) return null;
            byte[] key;
            try { key = Convert.FromBase64String(secret); }
            catch (FormatException) { return null; }
            if (key.Length != 32) return null;
            using (var hmac = new HMACSHA256(key))
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(challenge + "\n" +
                    hello.Nonce + "\n" + hello.PlayerId + "\n" + hello.Sequence + "\n" + hello.Name)));
        }

        public static bool EqualProof(string expected, string actual)
        {
            if (expected == null || actual == null || actual.Length != expected.Length) return false;
            int difference = 0;
            for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
    }
}
