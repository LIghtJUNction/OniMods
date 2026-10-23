using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Core;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    // These are chart labels, not native worlds or simulation objects.
    internal sealed class StarMapEntry
    {
        internal int Q, R;
        internal string Name;
        internal bool Discovered;
        internal StarMapEntry Copy() { return (StarMapEntry)MemberwiseClone(); }
    }

    internal enum StarMapDirectoryKind : byte { Subscribe = 1, Stop = 2, State = 3, Unavailable = 4 }

    internal sealed class StarMapDirectoryMessage
    {
        internal StarMapDirectoryKind Kind;
        internal string Room, Nonce;
        internal long Request, Sequence;
        internal int Total, Offset;
        internal StarMapEntry[] Entries = new StarMapEntry[0];
    }

    internal static class StarMapDirectoryWire
    {
        internal const int MaximumEntries = 64, PageSize = 16;
        private const int Magic = 0x4d534e4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static bool IsMessage(byte[] data)
        { return data != null && data.Length >= 6 && BitConverter.ToInt32(data, 0) == Magic; }

        internal static StarMapEntry[] Snapshot(StarMapEntry[] entries)
        {
            if (entries == null || entries.Length > MaximumEntries) throw new InvalidDataException("star_map_invalid");
            var positions = new HashSet<string>();
            var copy = new StarMapEntry[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || Math.Abs((long)entry.Q) > 128 || Math.Abs((long)entry.R) > 128 ||
                    Math.Abs((long)entry.Q + entry.R) > 128 || !positions.Add(entry.Q + ":" + entry.R) ||
                    entry.Discovered && !Protocol.ValidText(entry.Name, 64))
                    throw new InvalidDataException("star_map_invalid");
                copy[i] = new StarMapEntry { Q = entry.Q, R = entry.R, Discovered = entry.Discovered,
                    Name = entry.Discovered ? entry.Name : "" };
            }
            return copy.OrderBy(e => e.Q).ThenBy(e => e.R).ToArray();
        }

        internal static byte[] Encode(StarMapDirectoryMessage message)
        {
            if (message == null || !SystemWire.Id(message.Room) || !SystemWire.Id(message.Nonce) ||
                message.Request < 1 || message.Sequence < 0) throw new InvalidDataException();
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)message.Kind);
                writer.Write(new Guid(message.Room).ToByteArray()); writer.Write(new Guid(message.Nonce).ToByteArray());
                writer.Write(message.Request); writer.Write(message.Sequence);
                switch (message.Kind)
                {
                    case StarMapDirectoryKind.Subscribe:
                    case StarMapDirectoryKind.Stop: break;
                    case StarMapDirectoryKind.Unavailable:
                        if (message.Sequence < 1) throw new InvalidDataException();
                        break;
                    case StarMapDirectoryKind.State:
                        if (message.Sequence < 1 || message.Entries == null || message.Entries.Length > PageSize ||
                            message.Total < 0 || message.Total > MaximumEntries || message.Offset < 0 ||
                            message.Offset > message.Total - message.Entries.Length) throw new InvalidDataException();
                        var entries = Snapshot(message.Entries);
                        writer.Write((byte)message.Total); writer.Write((byte)message.Offset); writer.Write((byte)entries.Length);
                        foreach (var entry in entries)
                        {
                            writer.Write((short)entry.Q); writer.Write((short)entry.R); writer.Write(entry.Discovered);
                            byte[] text = Utf8.GetBytes(entry.Name); writer.Write((ushort)text.Length); writer.Write(text);
                        }
                        break;
                    default: throw new InvalidDataException();
                }
                writer.Flush();
                if (stream.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException();
                return stream.ToArray();
            }
        }

        internal static StarMapDirectoryMessage Decode(byte[] data)
        {
            if (!IsMessage(data) || data.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException();
            using (var stream = new MemoryStream(data, false))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                reader.ReadInt32(); if (reader.ReadByte() != 1) throw new InvalidDataException();
                var message = new StarMapDirectoryMessage { Kind = (StarMapDirectoryKind)reader.ReadByte(),
                    Room = new Guid(reader.ReadBytes(16)).ToString("N"), Nonce = new Guid(reader.ReadBytes(16)).ToString("N"),
                    Request = reader.ReadInt64(), Sequence = reader.ReadInt64() };
                if (message.Kind == StarMapDirectoryKind.State)
                {
                    message.Total = reader.ReadByte(); message.Offset = reader.ReadByte(); int count = reader.ReadByte();
                    if (count > PageSize) throw new InvalidDataException();
                    message.Entries = new StarMapEntry[count];
                    for (int i = 0; i < count; i++)
                    {
                        int q = reader.ReadInt16(), r = reader.ReadInt16(); byte discovered = reader.ReadByte();
                        int length = reader.ReadUInt16();
                        if (discovered > 1 || length > 256) throw new InvalidDataException();
                        byte[] text = reader.ReadBytes(length); if (text.Length != length) throw new EndOfStreamException();
                        string name = Utf8.GetString(text);
                        if (discovered == 0 && name.Length != 0) throw new InvalidDataException();
                        message.Entries[i] = new StarMapEntry { Q = q, R = r, Discovered = discovered == 1, Name = name };
                    }
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
                // Run the same limits on both the incoming and outgoing boundary.
                Encode(message);
                return message;
            }
        }
    }

    // The host publishes only its visible chart metadata while somebody has the
    // native map open. Reliable room traffic carries neither textures nor worlds.
    internal sealed class StarMapDirectorySession : IDisposable
    {
        private sealed class Observer
        {
            internal string Nonce;
            internal long Request, KnownSequence;
            internal double Expires;
            internal int Offset;
        }
        private sealed class Stamp { internal string Nonce; internal long Request; internal bool Stopped; }
        private readonly RoomSession session;
        private readonly Func<StarMapEntry[]> capture;
        private readonly Dictionary<string, Observer> observers = new Dictionary<string, Observer>();
        private readonly Dictionary<string, Stamp> highwater = new Dictionary<string, Stamp>();
        private readonly PagedCollection<StarMapEntry> assembly = new PagedCollection<StarMapEntry>();
        private StarMapEntry[] current = new StarMapEntry[0], hostSnapshot;
        private byte[] pendingStop;
        private string nonce;
        private string lastServedObserver;
        private long request, receivedSequence, sequence;
        private double now, nextRequest, nextCapture;
        private bool requested, ready, disposed, sourceKnown;
        internal bool Stale { get; private set; } = true;
        internal string LastError { get; private set; }
        internal StarMapEntry[] Entries { get { return Array.ConvertAll(current, e => e.Copy()); } }
        internal event System.Action Changed;

        internal StarMapDirectorySession(RoomSession session, Func<StarMapEntry[]> capture)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            session.ApplicationMessageReceived += Receive;
        }

        internal void Start()
        {
            if (disposed || session.IsHost || requested) return;
            requested = true; BeginRequest();
        }

        internal void Stop()
        {
            if (disposed || !requested) return;
            requested = false;
            if (Ready() && nonce != null)
            {
                pendingStop = StarMapDirectoryWire.Encode(Message(StarMapDirectoryKind.Stop));
                if (session.SendApplication(pendingStop)) pendingStop = null;
            }
            ClearClient();
        }

        private bool Ready()
        { return session.Joined && !session.Reconnecting && !session.Leaving && !session.Closed; }

        private void BeginRequest()
        {
            nonce = Guid.NewGuid().ToString("N"); request++; nextRequest = 0;
            ClearClient();
        }

        private void ClearClient()
        {
            current = new StarMapEntry[0]; receivedSequence = 0; assembly.Reset();
            Stale = true; LastError = null; Changed?.Invoke();
        }

        internal void Pump(double seconds)
        {
            if (disposed) return;
            now = seconds;
            bool connected = Ready();
            if (!connected)
            {
                if (ready)
                {
                    ready = false; observers.Clear(); highwater.Clear(); pendingStop = null;
                    nonce = null; ClearClient();
                }
                return;
            }
            if (!ready)
            {
                ready = true;
                if (requested) BeginRequest();
            }
            if (!session.IsHost)
            {
                if (pendingStop != null && session.SendApplication(pendingStop)) pendingStop = null;
                if (requested && now >= nextRequest)
                {
                    bool sent = session.SendApplication(StarMapDirectoryWire.Encode(Message(StarMapDirectoryKind.Subscribe)));
                    nextRequest = now + (sent && !Stale ? 20 : 2);
                }
                return;
            }
            foreach (var peer in observers.Keys.ToArray())
                if (observers[peer].Expires <= now || !Connected(peer)) observers.Remove(peer);
            foreach (var peer in highwater.Keys.ToArray())
                if (!session.Players.Any(p => p.Id == peer)) highwater.Remove(peer);
            if (observers.Count == 0) return;
            if (now >= nextCapture)
            {
                nextCapture = now + 2;
                StarMapEntry[] snapshot = null;
                try { var entries = capture(); if (entries != null) snapshot = StarMapDirectoryWire.Snapshot(entries); }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is InvalidDataException)
                { }
                if (!sourceKnown || !Same(snapshot, hostSnapshot))
                {
                    sourceKnown = true; hostSnapshot = snapshot; sequence++;
                    foreach (var observer in observers.Values) observer.Offset = 0;
                }
            }
            int budget = 2;
            // Continue after the last successful recipient so a changing source
            // cannot spend every frame's bounded budget on the same early peers.
            var recipients = observers.ToArray();
            int previous = Array.FindIndex(recipients, pair => pair.Key == lastServedObserver);
            for (int i = 0; i < recipients.Length && budget > 0; i++)
            {
                var pair = recipients[(previous + 1 + i) % recipients.Length];
                var observer = pair.Value;
                if (observer.KnownSequence == sequence || sequence == 0) continue;
                var message = new StarMapDirectoryMessage { Room = session.RoomId, Nonce = observer.Nonce,
                    Request = observer.Request, Sequence = sequence, Kind = hostSnapshot == null
                        ? StarMapDirectoryKind.Unavailable : StarMapDirectoryKind.State };
                if (hostSnapshot != null)
                {
                    message.Total = hostSnapshot.Length; message.Offset = observer.Offset;
                    message.Entries = hostSnapshot.Skip(observer.Offset).Take(StarMapDirectoryWire.PageSize).ToArray();
                }
                if (!session.SendApplicationTo(pair.Key, StarMapDirectoryWire.Encode(message))) continue;
                budget--;
                lastServedObserver = pair.Key;
                observer.Offset += message.Entries.Length;
                if (hostSnapshot == null || observer.Offset >= hostSnapshot.Length)
                { observer.KnownSequence = sequence; observer.Offset = 0; }
            }
        }

        private static bool Same(StarMapEntry[] left, StarMapEntry[] right)
        {
            if (left == null || right == null) return left == right;
            return left.Length == right.Length && left.Zip(right, (a, b) => a.Q == b.Q && a.R == b.R &&
                a.Name == b.Name && a.Discovered == b.Discovered).All(equal => equal);
        }

        private StarMapDirectoryMessage Message(StarMapDirectoryKind kind)
        { return new StarMapDirectoryMessage { Kind = kind, Room = session.RoomId, Nonce = nonce,
            Request = request, Sequence = receivedSequence }; }

        private bool Connected(string peer)
        { return session.Players.Any(p => p.Id == peer && p.Connected); }

        private void Receive(string sender, byte[] data)
        {
            if (disposed || !Ready() || !StarMapDirectoryWire.IsMessage(data)) return;
            StarMapDirectoryMessage message;
            try { message = StarMapDirectoryWire.Decode(data); }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException ||
                error is EndOfStreamException || error is DecoderFallbackException) { return; }
            if (message.Room != session.RoomId) return;
            if (session.IsHost)
            {
                if (!Connected(sender) || sender == session.LocalPlayerId ||
                    message.Kind != StarMapDirectoryKind.Subscribe && message.Kind != StarMapDirectoryKind.Stop) return;
                Stamp stamp;
                if (highwater.TryGetValue(sender, out stamp) && (message.Request < stamp.Request ||
                    message.Request == stamp.Request && (message.Nonce != stamp.Nonce || stamp.Stopped))) return;
                bool fresh = stamp == null || message.Request > stamp.Request;
                highwater[sender] = new Stamp { Request = message.Request, Nonce = message.Nonce,
                    Stopped = message.Kind == StarMapDirectoryKind.Stop };
                if (message.Kind == StarMapDirectoryKind.Stop) { observers.Remove(sender); return; }
                Observer observer;
                if (!observers.TryGetValue(sender, out observer) || fresh)
                {
                    observers[sender] = observer = new Observer { Request = message.Request, Nonce = message.Nonce };
                    nextCapture = 0;
                }
                observer.Expires = now + 30;
                // An incomplete or lost response is recovered by the next request.
                if (observer.KnownSequence != message.Sequence) observer.Offset = 0;
                observer.KnownSequence = message.Sequence;
            }
            else
            {
                var host = session.Players.FirstOrDefault();
                if (!requested || host == null || sender != host.Id || !host.Connected || message.Nonce != nonce ||
                    message.Request != request || message.Sequence < receivedSequence) return;
                if (message.Kind == StarMapDirectoryKind.Unavailable)
                {
                    current = new StarMapEntry[0]; Stale = true; LastError = "star_map_unavailable";
                    receivedSequence = message.Sequence; assembly.Reset(); nextRequest = now + 2; Changed?.Invoke();
                }
                else if (message.Kind == StarMapDirectoryKind.State && (Stale || message.Sequence > receivedSequence))
                {
                    try
                    {
                        StarMapEntry[] complete;
                        if (!assembly.Accept(message.Sequence, message.Total, message.Offset, message.Entries, data.Length, out complete)) return;
                        current = StarMapDirectoryWire.Snapshot(complete); receivedSequence = message.Sequence;
                        Stale = false; LastError = null; nextRequest = now + 20; Changed?.Invoke();
                    }
                    catch (InvalidDataException) { }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true; session.ApplicationMessageReceived -= Receive;
            observers.Clear(); highwater.Clear(); current = hostSnapshot = new StarMapEntry[0];
            assembly.Reset(); Stale = true;
        }
    }
}
