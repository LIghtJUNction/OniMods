using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;
using OniOnline.Multiplayer;

namespace OniOnline.Sdk
{
    public sealed class GameplayMessage
    {
        public string SenderId { get; }
        public string Channel { get; }
        public byte[] Payload { get; }
        internal GameplayMessage(string sender, string channel, byte[] payload)
        { SenderId = sender; Channel = channel; Payload = (byte[])payload.Clone(); }
    }

    // All callbacks run on RoomSession.Pump's thread (Unity main thread in the adapter).
    // Guests address the host only. The host validates game commands and explicitly replies.
    public sealed class GameplaySession : IDisposable
    {
        public const int MaxPayloadBytes = 4096;
        private const int Magic = 0x4b44534f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly RoomSession room;
        private readonly Func<StarSystemState> capture;
        private readonly Func<GameplayStateStore> storage;
        private readonly Func<string, bool> canUsePeer;
        private readonly Func<string, long> admissionGeneration;
        private readonly Dictionary<string, System.Action<GameplayMessage>> handlers = new Dictionary<string, System.Action<GameplayMessage>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Budget> budgets = new Dictionary<string, Budget>(StringComparer.Ordinal);
        private sealed class Budget { internal double At; internal int Packets, Bytes; }
        private bool disposed;
        private bool wasReady;
        private long lastAdmissionGeneration;
        private double now;
        public string GameplayId { get { return room.GameplayId; } }
        public string GameplayVersion { get { return room.GameplayVersion; } }
        public string SessionId { get { return room.RoomId; } }
        public string LocalPlayerId { get { return room.LocalPlayerId; } }
        public bool IsHost { get { return room.IsHost; } }
        public long AdmissionGeneration
        {
            get
            {
                if (!Ready) return 0;
                try { return admissionGeneration == null ? 1 : Math.Max(0, admissionGeneration(LocalPlayerId)); }
                catch (Exception) { return 0; }
            }
        }
        public ModStateSession ModStates { get; }
        public bool Ready { get { return !disposed && room.Joined && !room.Closed && !room.Leaving && !room.Reconnecting &&
            CanUsePeer(LocalPlayerId) && (IsHost || CanUsePeer(room.Players.FirstOrDefault()?.Id)); } }
        public StarSystemState System { get { return capture()?.Copy(); } }
        public PlayerInfo[] Players { get { return room.Players.Select(p => new PlayerInfo { Id = p.Id, Name = p.Name, Number = p.Number, Connected = p.Connected }).ToArray(); } }
        public event System.Action Changed;
        public event System.Action<string> HandlerFailed;

        internal GameplaySession(RoomSession room, Func<StarSystemState> capture, Func<GameplayStateStore> storage,
            Func<string, bool> canUsePeer = null, Func<string, long> admissionGeneration = null)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.canUsePeer = canUsePeer;
            this.admissionGeneration = admissionGeneration;
            ModStates = new ModStateSession(room, peer => Ready && CanUsePeer(peer), admissionGeneration);
            room.ApplicationMessageReceived += Receive;
            room.Changed += Notify;
        }
        public IDisposable Subscribe(string channel, System.Action<GameplayMessage> handler)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameplaySession));
            if (!GameplayFramework.ValidName(channel) || handler == null) throw new ArgumentException("invalid_gameplay_channel");
            if (handlers.ContainsKey(channel)) throw new InvalidOperationException("duplicate_gameplay_channel");
            handlers.Add(channel, handler); return new Subscription(this, channel, handler);
        }
        private sealed class Subscription : IDisposable
        {
            private GameplaySession owner; private readonly string channel; private readonly System.Action<GameplayMessage> handler;
            internal Subscription(GameplaySession owner, string channel, System.Action<GameplayMessage> handler)
            { this.owner = owner; this.channel = channel; this.handler = handler; }
            public void Dispose()
            {
                if (owner == null) return;
                if (owner.handlers.TryGetValue(channel, out var current) && current == handler) owner.handlers.Remove(channel);
                owner = null;
            }
        }
        public bool SendToHost(string channel, byte[] payload)
        {
            if (!Ready) return false;
            byte[] bytes = Encode(channel, payload);
            if (IsHost) { Receive(LocalPlayerId, bytes); return true; }
            return room.SendApplication(bytes);
        }
        public bool SendToPlayer(string playerId, string channel, byte[] payload)
        {
            if (!Ready || !IsHost || !CanUsePeer(playerId)) return false;
            byte[] bytes = Encode(channel, payload);
            if (playerId == LocalPlayerId) { Receive(LocalPlayerId, bytes); return true; }
            return room.SendApplicationTo(playerId, bytes);
        }
        public GameplayState ReadLocalState()
        {
            var store = storage();
            if (store?.Blocked == true) throw new InvalidOperationException("gameplay_save_blocked");
            return store?.Read(GameplayId);
        }
        public void WriteLocalState(int schema, byte[] payload)
        {
            if (!Ready) throw new InvalidOperationException("gameplay_not_connected");
            var store = storage() ?? throw new InvalidOperationException("native_colony_not_loaded");
            store.Write(GameplayId, schema, payload);
        }
        internal void Pump(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now) throw new ArgumentOutOfRangeException(nameof(seconds));
            now = seconds;
            ModStates.Pump(seconds);
            var connected = new HashSet<string>(room.Players.Where(p => p.Connected).Select(p => p.Id), StringComparer.Ordinal);
            foreach (string peer in budgets.Keys.Where(p => !connected.Contains(p)).ToArray()) budgets.Remove(peer);
            bool ready = Ready;
            long generation = AdmissionGeneration;
            if (wasReady != ready || lastAdmissionGeneration != generation)
            { wasReady = ready; lastAdmissionGeneration = generation; Notify(); }
        }
        private bool CanUsePeer(string playerId)
        {
            if (playerId == null || !room.Players.Any(p => p.Id == playerId && p.Connected)) return false;
            try { return (canUsePeer == null || canUsePeer(playerId)) &&
                    (admissionGeneration == null || admissionGeneration(playerId) > 0); }
            catch (Exception) { return false; }
        }
        private byte[] Encode(string channel, byte[] payload)
        {
            if (!GameplayFramework.ValidName(channel) || payload == null || payload.Length > MaxPayloadBytes) throw new ArgumentException("invalid_gameplay_message");
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write(GameplayId); writer.Write(channel);
                writer.Write(payload.Length); writer.Write(payload); return stream.ToArray();
            }
        }
        private void Receive(string sender, byte[] bytes)
        {
            if (!Ready || bytes == null || bytes.Length < 5 || bytes.Length > MaxPayloadBytes + 160 || BitConverter.ToInt32(bytes, 0) != Magic ||
                !CanUsePeer(sender) || !IsHost && sender != room.Players[0].Id) return;
            if (!budgets.TryGetValue(sender, out var budget)) budgets.Add(sender, budget = new Budget { At = now });
            if (now - budget.At >= 1) { budget.At = now; budget.Packets = budget.Bytes = 0; }
            if (++budget.Packets > 32 || (budget.Bytes += bytes.Length) > 64 * 1024) return;
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, Utf8))
                {
                    reader.ReadInt32(); if (reader.ReadByte() != 1 || reader.ReadString() != GameplayId) return;
                    string channel = reader.ReadString(); int length = reader.ReadInt32();
                    if (!GameplayFramework.ValidName(channel) || length < 0 || length > MaxPayloadBytes || length != stream.Length - stream.Position) return;
                    if (!handlers.TryGetValue(channel, out var handler)) return;
                    var message = new GameplayMessage(sender, channel, reader.ReadBytes(length));
                    try { handler(message); } catch (Exception) { ReportFailure(channel); }
                }
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is FormatException) { }
        }
        private void ReportFailure(string channel)
        {
            var callbacks = HandlerFailed; if (callbacks == null) return;
            foreach (System.Action<string> callback in callbacks.GetInvocationList()) try { callback(channel); } catch (Exception) { }
        }
        internal void Notify()
        {
            var callbacks = Changed; if (callbacks == null) return;
            foreach (System.Action callback in callbacks.GetInvocationList()) try { callback(); } catch (Exception) { ReportFailure("lifecycle"); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; room.ApplicationMessageReceived -= Receive; room.Changed -= Notify;
            ModStates.Dispose();
            handlers.Clear(); budgets.Clear(); Changed = null; HandlerFailed = null;
        }
    }
}
