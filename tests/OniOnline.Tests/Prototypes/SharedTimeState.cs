using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Core
{
    public static class NativePausePolicy
    {
        public static int ExternalCount(int nativeCount, bool ownsPublicTicket)
        {
            return Math.Max(0, nativeCount - (ownsPublicTicket ? 1 : 0));
        }

        public static bool AllowExplicitLocalToggle(int nativeCount, bool ownsPublicTicket)
        {
            return ownsPublicTicket && ExternalCount(nativeCount, ownsPublicTicket) > 0;
        }
    }

    public enum SharedTimeMessage : byte { Report = 1, State = 2, Pause = 3, Resume = 4, Speed = 5 }

    public sealed class SharedTimeMember
    {
        public string Id;
        public bool Available;
        public bool CanRun;
    }

    public sealed class SharedTimePacket
    {
        public SharedTimeMessage Kind;
        public string Epoch;
        public string Generation;
        public long Revision;
        public long Sequence;
        public bool Available;
        public bool CanRun;
        public bool Paused;
        public bool Waiting;
        public int Speed = 1;
        public SharedTimeMessage VoteKind;
        public int VoteSpeed = 1;
        public string[] Voters = new string[0];
        public string[] Votes = new string[0];
        public string[] Reporters = new string[0];
        public string[] Connections = new string[0];
    }

    /// <summary>Host decisions only; no Unity time, network transport, or simulation is run here.</summary>
    public sealed class SharedTimeState
    {
        private string memberKey;
        private readonly HashSet<string> votes = new HashSet<string>(StringComparer.Ordinal);
        private string[] voters = new string[0];
        public string Epoch { get; private set; }
        public long Revision { get; private set; } = 1;
        public bool Paused { get; private set; } = true;
        public bool Waiting { get; private set; } = true;
        public int Speed { get; private set; } = 1;
        public SharedTimeMessage VoteKind { get; private set; }
        public int VoteSpeed { get; private set; } = 1;
        public int Votes { get { return votes.Count; } }
        public int Voters { get { return voters.Length; } }

        public SharedTimeState(string epoch)
        {
            if (!SharedTimeWire.ValidId(epoch)) throw new ArgumentException("invalid_time_epoch");
            Epoch = epoch;
        }

        public bool Configure(SharedTimeMember[] members)
        {
            if (members == null || members.Length > 4 || members.Any(m => m == null || !SharedTimeWire.ValidId(m.Id)) ||
                members.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != members.Length)
                throw new ArgumentException("invalid_time_members");
            var ordered = members.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
            string key = string.Join(";", ordered.Select(m => m.Id + (m.Available ? ":1" : ":0") + (m.CanRun ? ":1" : ":0")));
            if (key == memberKey) return false;
            memberKey = key;
            voters = ordered.Where(m => m.Available).Select(m => m.Id).ToArray();
            Waiting = ordered.Length == 0 || ordered.Any(m => !m.Available || !m.CanRun);
            Pause();
            return true;
        }

        public void Pause()
        {
            Paused = true;
            ClearVotes();
            Revision++;
        }

        public bool Request(string sender, SharedTimeMessage kind, int speed, string epoch, long revision)
        {
            if (epoch != Epoch || revision != Revision || !voters.Contains(sender, StringComparer.Ordinal)) return false;
            if (kind == SharedTimeMessage.Pause) { Pause(); return true; }
            if (Waiting || (kind != SharedTimeMessage.Resume && kind != SharedTimeMessage.Speed)) return false;
            if (kind == SharedTimeMessage.Resume && !Paused) return false;
            if (kind == SharedTimeMessage.Speed && (speed < 1 || speed > 3 || speed == Speed)) return false;
            if (VoteKind != 0 && (VoteKind != kind || (kind == SharedTimeMessage.Speed && VoteSpeed != speed))) return false;
            VoteKind = kind;
            VoteSpeed = speed;
            if (!votes.Add(sender)) return false;
            if (votes.Count == voters.Length)
            {
                if (kind == SharedTimeMessage.Resume) Paused = false;
                else Speed = speed;
                ClearVotes();
                Revision++;
            }
            return true;
        }

        public SharedTimePacket Packet(long sequence)
        {
            return new SharedTimePacket
            {
                Kind = SharedTimeMessage.State, Epoch = Epoch, Revision = Revision, Sequence = sequence,
                Paused = Paused, Waiting = Waiting, Speed = Speed, VoteKind = VoteKind, VoteSpeed = VoteSpeed,
                Voters = (string[])voters.Clone(), Votes = votes.OrderBy(v => v, StringComparer.Ordinal).ToArray()
            };
        }

        private void ClearVotes() { votes.Clear(); VoteKind = 0; VoteSpeed = Speed; }
    }

    public static class SharedTimeWire
    {
        private const int Magic = 0x54494E4F; // ONIT
        private static readonly Encoding Ascii = Encoding.ASCII;
        public static bool IsMessage(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 4 && bytes[0] == 'O' && bytes[1] == 'N' && bytes[2] == 'I' && bytes[3] == 'T';
        }

        public static byte[] Encode(SharedTimePacket packet)
        {
            Validate(packet);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Ascii, true))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)packet.Kind);
                WriteId(writer, packet.Epoch); writer.Write(packet.Revision); writer.Write(packet.Sequence);
                switch (packet.Kind)
                {
                    case SharedTimeMessage.Report:
                        WriteId(writer, packet.Generation); writer.Write(packet.Available); writer.Write(packet.CanRun); break;
                    case SharedTimeMessage.State:
                        writer.Write(packet.Paused); writer.Write(packet.Waiting); writer.Write((byte)packet.Speed);
                        writer.Write((byte)packet.VoteKind); writer.Write((byte)packet.VoteSpeed);
                        WriteIds(writer, packet.Voters); WriteIds(writer, packet.Votes);
                        WriteIds(writer, packet.Reporters); WriteIds(writer, packet.Connections); break;
                    case SharedTimeMessage.Speed: writer.Write((byte)packet.Speed); break;
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static SharedTimePacket Decode(byte[] bytes)
        {
            if (!IsMessage(bytes) || bytes.Length > 1024) throw new InvalidDataException("invalid_time_packet");
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Ascii))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadByte() != 1) throw new InvalidDataException("time_protocol");
                    var packet = new SharedTimePacket
                    {
                        Kind = (SharedTimeMessage)reader.ReadByte(), Epoch = ReadId(reader),
                        Revision = reader.ReadInt64(), Sequence = reader.ReadInt64()
                    };
                    switch (packet.Kind)
                    {
                        case SharedTimeMessage.Report:
                            packet.Generation = ReadId(reader); packet.Available = ReadBool(reader); packet.CanRun = ReadBool(reader); break;
                        case SharedTimeMessage.State:
                            packet.Paused = ReadBool(reader); packet.Waiting = ReadBool(reader); packet.Speed = reader.ReadByte();
                            packet.VoteKind = (SharedTimeMessage)reader.ReadByte(); packet.VoteSpeed = reader.ReadByte();
                            packet.Voters = ReadIds(reader); packet.Votes = ReadIds(reader);
                            packet.Reporters = ReadIds(reader); packet.Connections = ReadIds(reader); break;
                        case SharedTimeMessage.Speed: packet.Speed = reader.ReadByte(); break;
                        case SharedTimeMessage.Pause: case SharedTimeMessage.Resume: break;
                        default: throw new InvalidDataException("time_kind");
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("trailing_time_packet");
                    Validate(packet);
                    return packet;
                }
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("truncated_time_packet", error); }
        }

        public static bool ValidId(string value)
        {
            return value != null && value.Length == 32 && value.All(c =>
                (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || (c >= '0' && c <= '9'));
        }

        private static void Validate(SharedTimePacket packet)
        {
            if (packet == null || !ValidId(packet.Epoch) || packet.Revision < 1 || packet.Sequence < 1 ||
                packet.Kind < SharedTimeMessage.Report || packet.Kind > SharedTimeMessage.Speed)
                throw new InvalidDataException("invalid_time_packet");
            if (packet.Kind == SharedTimeMessage.Report && (!ValidId(packet.Generation) || (!packet.Available && packet.CanRun)))
                throw new InvalidDataException("invalid_time_report");
            if (packet.Kind == SharedTimeMessage.Speed && (packet.Speed < 1 || packet.Speed > 3))
                throw new InvalidDataException("invalid_time_speed");
            if (packet.Kind != SharedTimeMessage.State) return;
            if (packet.Speed < 1 || packet.Speed > 3 || packet.VoteSpeed < 1 || packet.VoteSpeed > 3 ||
                (packet.VoteKind != 0 && packet.VoteKind != SharedTimeMessage.Resume && packet.VoteKind != SharedTimeMessage.Speed) ||
                !ValidIds(packet.Voters) || !ValidIds(packet.Votes) || packet.Votes.Any(id => !packet.Voters.Contains(id)) ||
                !ValidIds(packet.Reporters) || packet.Connections == null || packet.Connections.Length > 4 ||
                packet.Connections.Any(id => !ValidId(id)) || packet.Reporters.Length != packet.Connections.Length ||
                (packet.VoteKind == 0 && packet.Votes.Length != 0) || (packet.Waiting && !packet.Paused) ||
                (packet.Voters.Length == 0 && !packet.Paused))
                throw new InvalidDataException("invalid_time_state");
        }

        private static bool ValidIds(string[] values)
        {
            return values != null && values.Length <= 4 && values.All(ValidId) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
        }

        private static void WriteId(BinaryWriter writer, string value) { writer.Write(Ascii.GetBytes(value)); }
        private static string ReadId(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(32);
            if (bytes.Length != 32) throw new EndOfStreamException();
            string value = Ascii.GetString(bytes);
            if (!ValidId(value)) throw new InvalidDataException("invalid_time_identity");
            return value;
        }
        private static void WriteIds(BinaryWriter writer, string[] ids)
        {
            writer.Write((byte)ids.Length);
            foreach (string id in ids) WriteId(writer, id);
        }
        private static string[] ReadIds(BinaryReader reader)
        {
            int count = reader.ReadByte();
            if (count > 4) throw new InvalidDataException("invalid_time_voters");
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = ReadId(reader);
            return result;
        }
        private static bool ReadBool(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > 1) throw new InvalidDataException("invalid_time_boolean");
            return value == 1;
        }
    }
}
