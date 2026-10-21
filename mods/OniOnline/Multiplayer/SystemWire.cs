using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    internal enum SystemMessageKind : byte { Register = 1, State = 2, Withdraw = 3, Rejected = 4, Reserve = 5 }
    internal sealed class SystemMessage
    {
        internal SystemMessageKind Kind;
        internal PlanetManifest Planet;
        internal StarSystemState State;
        internal string Reason;
        internal int Total = -1;
        internal int Offset;
        internal int Q;
        internal int R;
    }

    internal static class SystemWire
    {
        internal const int PageSize = 12;
        private const int Magic = 0x5353594f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] bytes)
        { return bytes != null && bytes.Length >= 6 && BitConverter.ToInt32(bytes, 0) == Magic; }

        internal static byte[] Encode(SystemMessage message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)message.Kind);
                switch (message.Kind)
                {
                    case SystemMessageKind.Register: WritePlanet(writer, message.Planet); break;
                    case SystemMessageKind.State:
                        var state = message.State;
                        ValidateState(state);
                        WriteText(writer, state.RoomId); WriteText(writer, state.SystemId); WriteText(writer, state.Name);
                        writer.Write(state.Revision); writer.Write(state.Radius);
                        writer.Write(message.Total < 0 ? state.Members.Length : message.Total); writer.Write(message.Offset);
                        writer.Write((byte)state.Members.Length);
                        foreach (var member in state.Members)
                        {
                            WriteText(writer, member.PeerId); writer.Write(member.Planet != null);
                            if (member.Planet != null) WritePlanet(writer, member.Planet);
                            writer.Write(member.Q); writer.Write(member.R); writer.Write(member.Connected);
                        }
                        break;
                    case SystemMessageKind.Withdraw: break;
                    case SystemMessageKind.Reserve: writer.Write(message.Q); writer.Write(message.R); break;
                    case SystemMessageKind.Rejected:
                        if (!Protocol.ValidText(message.Reason, 64)) throw new InvalidDataException();
                        WriteText(writer, message.Reason); break;
                    default: throw new InvalidDataException();
                }
                writer.Flush();
                if (stream.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException();
                return stream.ToArray();
            }
        }

        internal static SystemMessage Decode(byte[] bytes)
        {
            if (!IsMessage(bytes) || bytes.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException();
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadInt32(); if (reader.ReadByte() != 1) throw new InvalidDataException();
                var message = new SystemMessage { Kind = (SystemMessageKind)reader.ReadByte() };
                switch (message.Kind)
                {
                    case SystemMessageKind.Register: message.Planet = ReadPlanet(reader); break;
                    case SystemMessageKind.Withdraw: break;
                    case SystemMessageKind.Reserve: message.Q = reader.ReadInt32(); message.R = reader.ReadInt32(); break;
                    case SystemMessageKind.Rejected: message.Reason = ReadText(reader, 64); break;
                    case SystemMessageKind.State:
                        var state = new StarSystemState { RoomId = ReadText(reader, 32), SystemId = ReadText(reader, 32),
                            Name = ReadText(reader, 64), Revision = reader.ReadInt64(), Radius = reader.ReadInt32() };
                        message.Total = reader.ReadInt32(); message.Offset = reader.ReadInt32();
                        int count = reader.ReadByte();
                        if (count > PageSize || message.Total < 0 || message.Offset < 0 || message.Offset > message.Total - count) throw new InvalidDataException();
                        state.Members = new SystemMember[count];
                        for (int i = 0; i < count; i++)
                        {
                            var member = new SystemMember { PeerId = ReadText(reader, 32) };
                            byte hasPlanet = reader.ReadByte(); if (hasPlanet > 1) throw new InvalidDataException();
                            if (hasPlanet == 1) member.Planet = ReadPlanet(reader);
                            member.Q = reader.ReadInt32(); member.R = reader.ReadInt32();
                            byte connected = reader.ReadByte(); if (connected > 1) throw new InvalidDataException();
                            member.Connected = connected == 1; state.Members[i] = member;
                        }
                        ValidateState(state); message.State = state; break;
                    default: throw new InvalidDataException();
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
                return message;
            }
        }

        internal static void ValidatePlanet(PlanetManifest planet)
        {
            if (planet == null || !Id(planet.PlanetId) || !Id(planet.LineageId) || !Id(planet.OwnerId) || !Id(planet.HomeSystemId) ||
                !Protocol.ValidText(planet.Name, 64) || planet.NativeWorldId < 0 || planet.NativeWorldId > 255 ||
                planet.Width < 1 || planet.Width > 1024 || planet.Height < 1 || planet.Height > 1024 ||
                (long)planet.Width * planet.Height > 262144) throw new InvalidDataException("planet_invalid_manifest");
        }

        private static void ValidateState(StarSystemState state)
        {
            if (state == null || !Id(state.RoomId) || !Id(state.SystemId) || !Protocol.ValidText(state.Name, 64) ||
                state.Revision < 1 || state.Radius < 1 || state.Radius > 128 || state.Members == null || state.Members.Length > PageSize) throw new InvalidDataException();
            var peers = new HashSet<string>(); var planets = new HashSet<string>(); var lineages = new HashSet<string>();
            var positions = new HashSet<string>();
            foreach (var member in state.Members)
            {
                if (member == null || !Id(member.PeerId) || !peers.Add(member.PeerId) ||
                    Math.Abs((long)member.Q) > state.Radius || Math.Abs((long)member.R) > state.Radius ||
                    Math.Abs((long)member.Q + member.R) > state.Radius ||
                    !positions.Add(member.Q + ":" + member.R)) throw new InvalidDataException();
                if (member.Planet != null)
                {
                    ValidatePlanet(member.Planet);
                    if (!planets.Add(member.Planet.PlanetId) || !lineages.Add(member.Planet.LineageId)) throw new InvalidDataException();
                }
            }
        }

        internal static bool Id(string value)
        { Guid parsed; return value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out parsed) && parsed != Guid.Empty; }
        private static void WritePlanet(BinaryWriter writer, PlanetManifest planet)
        {
            ValidatePlanet(planet);
            WriteText(writer, planet.PlanetId); WriteText(writer, planet.LineageId); WriteText(writer, planet.OwnerId);
            WriteText(writer, planet.HomeSystemId); WriteText(writer, planet.Name);
            writer.Write(planet.NativeWorldId); writer.Write(planet.Width); writer.Write(planet.Height);
        }
        private static PlanetManifest ReadPlanet(BinaryReader reader)
        {
            var planet = new PlanetManifest { PlanetId = ReadText(reader, 32), LineageId = ReadText(reader, 32),
                OwnerId = ReadText(reader, 32), HomeSystemId = ReadText(reader, 32), Name = ReadText(reader, 64),
                NativeWorldId = reader.ReadInt32(), Width = reader.ReadInt32(), Height = reader.ReadInt32() };
            ValidatePlanet(planet); return planet;
        }
        private static void WriteText(BinaryWriter writer, string value)
        { byte[] data = Utf8.GetBytes(value); writer.Write((ushort)data.Length); writer.Write(data); }
        private static string ReadText(BinaryReader reader, int limit)
        {
            int size = reader.ReadUInt16(); if (size > limit * 4) throw new InvalidDataException();
            byte[] data = reader.ReadBytes(size); if (data.Length != size) throw new EndOfStreamException();
            string value = Utf8.GetString(data); if (!Protocol.ValidText(value, limit)) throw new InvalidDataException();
            return value;
        }
    }
}
