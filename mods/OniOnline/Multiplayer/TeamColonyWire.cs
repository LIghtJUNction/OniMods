using System;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Core;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    internal enum TeamColonyMessageKind : byte
    {
        Query = 1, Invite = 2, Accept = 3, Leave = 4, Permissions = 5, Administrator = 6,
        TransferAdministration = 7, Delegate = 8, DelegateTeam = 9, CancelInvitation = 10,
        Offer = 11, Snapshot = 12, Receipt = 13
    }
    internal enum TeamColonyEntryKind : byte { Member = 1, Duplicant = 2, Delegate = 3 }
    internal sealed class TeamColonyEntry
    {
        internal TeamColonyEntryKind Kind;
        internal string ActorId = "";
        internal string EntityId = "";
        internal TeamPermissionFlags Permissions;
        internal bool Administrator;
        internal bool Online;
        internal bool Enabled;
        internal TeamColonyEntry Copy() { return (TeamColonyEntry)MemberwiseClone(); }
    }
    internal sealed class TeamColonySnapshot
    {
        internal string RoomId = "";
        internal string TeamId = "";
        internal string PlanetId = "";
        internal string AuthorityPeerId = "";
        internal string AuthorityEpoch = "";
        internal long Revision;
        internal bool HostAvailable;
        internal bool CanContinue;
        internal bool Invited;
        internal bool IsMember;
        internal TeamColonyEntry[] Entries = Array.Empty<TeamColonyEntry>();
        internal TeamColonyEntry[] Members { get { return Entries.Where(e => e.Kind == TeamColonyEntryKind.Member).Select(e => e.Copy()).ToArray(); } }
        internal TeamColonySnapshot Copy(bool includeEntries = true)
        {
            var copy = (TeamColonySnapshot)MemberwiseClone();
            copy.Entries = includeEntries ? Entries.Select(e => e.Copy()).ToArray() : Array.Empty<TeamColonyEntry>();
            return copy;
        }
    }
    internal sealed class TeamColonyMessage
    {
        internal TeamColonyMessageKind Kind;
        internal string RoomId = "";
        internal string TeamId = "";
        internal string PlanetId = "";
        internal string AuthorityEpoch = "";
        internal string RequestId = "";
        internal string TargetId = "";
        internal string EntityId = "";
        internal TeamPermissionFlags Permissions;
        internal bool Enabled;
        internal decimal Amount;
        internal string Reason = "";
        internal bool Accepted;
        internal TeamColonySnapshot Snapshot;
        internal int Total;
        internal int Offset;
    }

    internal static class TeamColonyWire
    {
        internal const int PageSize = 4;
        private const int Magic = 0x4d544e4f; // ONTM
        private const byte Version = 12;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] bytes)
        { return bytes != null && bytes.Length >= 6 && BitConverter.ToInt32(bytes, 0) == Magic && bytes[4] == Version; }
        internal static byte[] Encode(TeamColonyMessage message)
        {
            if (message == null || message.Kind < TeamColonyMessageKind.Query || message.Kind > TeamColonyMessageKind.Receipt ||
                (message.Permissions & ~(TeamPermissionFlags)7) != 0) throw new InvalidDataException("team_message");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write((byte)message.Kind);
                Text(writer, message.RoomId); Text(writer, message.TeamId); Text(writer, message.PlanetId);
                Text(writer, message.AuthorityEpoch); Text(writer, message.RequestId);
                if (message.Kind == TeamColonyMessageKind.Snapshot)
                {
                    TeamColonySnapshot s = message.Snapshot;
                    if (s == null || s.Entries.Length > PageSize || s.Revision < 1 || message.Total < 0 ||
                        message.Offset < 0 || message.Offset > message.Total - s.Entries.Length ||
                        (!s.IsMember && (s.Entries.Length != 0 || message.Total != 0)) ||
                        (!s.HostAvailable && s.CanContinue)) throw new InvalidDataException("team_page");
                    Text(writer, s.AuthorityPeerId); writer.Write(s.Revision); writer.Write(s.HostAvailable);
                    writer.Write(s.CanContinue); writer.Write(s.Invited); writer.Write(s.IsMember);
                    writer.Write(message.Total); writer.Write(message.Offset); writer.Write((byte)s.Entries.Length);
                    foreach (var entry in s.Entries)
                    {
                        writer.Write((byte)entry.Kind); Text(writer, entry.ActorId); Text(writer, entry.EntityId);
                        writer.Write((byte)entry.Permissions); writer.Write(entry.Administrator); writer.Write(entry.Online); writer.Write(entry.Enabled);
                    }
                }
                else if (message.Kind == TeamColonyMessageKind.Receipt)
                { writer.Write(message.Accepted); Text(writer, message.Reason); }
                else if (message.Kind != TeamColonyMessageKind.Query)
                {
                    Text(writer, message.TargetId); Text(writer, message.EntityId);
                    writer.Write((byte)message.Permissions); writer.Write(message.Enabled); writer.Write(message.Amount);
                }
                writer.Flush();
                if (stream.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException("team_packet_budget");
                return stream.ToArray();
            }
        }
        internal static TeamColonyMessage Decode(byte[] bytes)
        {
            if (!IsMessage(bytes) || bytes.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException("team_packet");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadInt32(); reader.ReadByte();
                var m = new TeamColonyMessage { Kind = (TeamColonyMessageKind)reader.ReadByte(), RoomId = Text(reader),
                    TeamId = Text(reader), PlanetId = Text(reader), AuthorityEpoch = Text(reader), RequestId = Text(reader) };
                if (m.Kind < TeamColonyMessageKind.Query || m.Kind > TeamColonyMessageKind.Receipt) throw new InvalidDataException("team_kind");
                if (m.Kind == TeamColonyMessageKind.Snapshot)
                {
                    var s = new TeamColonySnapshot { RoomId = m.RoomId, TeamId = m.TeamId, PlanetId = m.PlanetId,
                        AuthorityEpoch = m.AuthorityEpoch, AuthorityPeerId = Text(reader), Revision = reader.ReadInt64(),
                        HostAvailable = Bool(reader), CanContinue = Bool(reader), Invited = Bool(reader), IsMember = Bool(reader) };
                    m.Total = reader.ReadInt32(); m.Offset = reader.ReadInt32(); int count = reader.ReadByte();
                    if (s.Revision < 1 || count > PageSize || m.Total < 0 || m.Offset < 0 || m.Offset > m.Total - count ||
                        (!s.HostAvailable && s.CanContinue) || (!s.IsMember && (count != 0 || m.Total != 0)))
                        throw new InvalidDataException("team_page");
                    s.Entries = new TeamColonyEntry[count];
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new TeamColonyEntry { Kind = (TeamColonyEntryKind)reader.ReadByte(), ActorId = Text(reader),
                            EntityId = Text(reader), Permissions = (TeamPermissionFlags)reader.ReadByte(), Administrator = Bool(reader),
                            Online = Bool(reader), Enabled = Bool(reader) };
                        if (entry.Kind < TeamColonyEntryKind.Member || entry.Kind > TeamColonyEntryKind.Delegate ||
                            (entry.Permissions & ~(TeamPermissionFlags)7) != 0 || string.IsNullOrEmpty(entry.ActorId) ||
                            (entry.Kind != TeamColonyEntryKind.Member && string.IsNullOrEmpty(entry.EntityId)))
                            throw new InvalidDataException("team_entry");
                        s.Entries[i] = entry;
                    }
                    m.Snapshot = s;
                }
                else if (m.Kind == TeamColonyMessageKind.Receipt)
                { m.Accepted = Bool(reader); m.Reason = Text(reader); }
                else if (m.Kind != TeamColonyMessageKind.Query)
                {
                    m.TargetId = Text(reader); m.EntityId = Text(reader); m.Permissions = (TeamPermissionFlags)reader.ReadByte();
                    m.Enabled = Bool(reader); m.Amount = reader.ReadDecimal();
                    if ((m.Permissions & ~(TeamPermissionFlags)7) != 0 || string.IsNullOrEmpty(m.RequestId)) throw new InvalidDataException("team_request");
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("team_trailing");
                return m;
            }
        }
        private static void Text(BinaryWriter writer, string value)
        {
            value = value ?? "";
            byte[] bytes = Utf8.GetBytes(value);
            if (value.Length > 128 || bytes.Length > 512 || value.Any(char.IsControl)) throw new InvalidDataException("team_text");
            writer.Write((ushort)bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int count = reader.ReadUInt16(); if (count > 512) throw new InvalidDataException("team_text");
            byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException();
            string value = Utf8.GetString(bytes);
            if (value.Length > 128 || value.Any(char.IsControl)) throw new InvalidDataException("team_text");
            return value;
        }
        private static bool Bool(BinaryReader reader)
        { byte value = reader.ReadByte(); if (value > 1) throw new InvalidDataException("team_boolean"); return value == 1; }
    }
}
