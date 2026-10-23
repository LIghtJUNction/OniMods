using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OniOnline.Core;
using OniOnline.Sdk;

namespace OniOnline.Network
{
    public sealed class SessionProfile
    {
        public string Name;
        public string GameVersion;
        public string CycleTrimVersion;
        public bool SpacedOut;
        public string[] Capabilities = SessionCapabilities.Core;
        // Contracts that the remote endpoint must implement; asymmetric services
        // may require a producer capability without providing it locally.
        public string[] RequiredCapabilities = SessionCapabilities.Core;
        public string GameplayId = GameplayFramework.BuiltInId;
        public string GameplayVersion = "1";
    }

    public sealed class SessionOptions
    {
        public const int MaxChatHistoryLimit = 500;
        public double HandshakeTimeoutSeconds = 30;
        public double HeartbeatIntervalSeconds = 3;
        public double PeerTimeoutSeconds = 30;
        public double ReconnectWindowSeconds = 300;
        public double SeatReservationSeconds = 300;
        public double ChatRetrySeconds = 3;
        public int ChatHistoryLimit = 100;
        public Func<double> RetryJitter;
    }

    public sealed class ChatLine
    {
        public long Sequence;
        public string PlayerId;
        public string Name;
        public string Text;
        public long MessageId;
    }

    // All state and callbacks belong to the Pump caller's thread, including Steam.
    internal interface IRoomCleanupStatus { string CleanupStatus { get; } }

    // Only owned stage names and exception types escape cleanup. Never retain SDK messages,
    // identities, payloads, credentials, or inner exceptions in this diagnostic.
    internal sealed class RoomCleanupException : Exception
    {
        internal string[] Failures { get; private set; }
        internal RoomCleanupException(string[] failures) : base(string.Join("; ", failures)) { Failures = failures; }
    }
    internal sealed class RoomCleanup
    {
        private readonly List<string> failures = new List<string>();
        private readonly List<string> notes = new List<string>();
        internal string Status { get { return string.Join("; ", failures.Concat(notes)); } }
        internal bool HasFailures { get { return failures.Count != 0; } }
        internal void Note(string stage, string reason) { notes.Add(stage + ":" + reason); }
        internal void Run(string stage, System.Action action)
        {
            try { action(); }
            catch (RoomCleanupException error) { failures.AddRange(error.Failures.Select(f => stage + "." + f)); }
            catch (Exception error) { failures.Add(stage + ":" + error.GetType().Name); }
        }
        internal void Dispose(string stage, IDisposable resource)
        {
            if (resource == null) return;
            bool completed = false;
            Run(stage, () => { resource.Dispose(); completed = true; });
            var status = resource as IRoomCleanupStatus;
            if (completed && status != null && !string.IsNullOrEmpty(status.CleanupStatus)) Note(stage, status.CleanupStatus);
        }
        internal void ThrowIfFailed() { if (failures.Count != 0) throw new RoomCleanupException(failures.ToArray()); }
    }

    public sealed class RoomSession : IDisposable, IRoomCleanupStatus
    {
        // Work scopes cannot repeat when their coordinator is recreated in this room.
        public string CleanupStatus { get; private set; }
        private long workAuthorityGeneration;
        internal long NextWorkAuthorityGeneration() { return checked(++workAuthorityGeneration); }
        private long modAdmissionGeneration;
        internal long NextModAdmissionGeneration() { return checked(++modAdmissionGeneration); }
        private sealed class Seat
        {
            internal string Id;
            internal string Name;
            internal string Secret;
            internal string Account;
            internal int Number;
            internal string[] Capabilities = new string[0];
            internal string[] NegotiatedCapabilities = new string[0];
            internal long ConnectionGeneration;
            internal string Connection;
            internal double DisconnectedAt;
            internal long HighestAcceptedMessage;
            internal readonly Dictionary<long, long> AcceptedMessages = new Dictionary<long, long>();
            internal readonly Queue<long> AcceptedOrder = new Queue<long>();
        }

        private sealed class Peer
        {
            internal Seat Seat;
            internal string Challenge;
            internal bool Authenticated;
            internal double LastSeen;
            internal double ConnectedAt;
            internal double CloseAt;
            internal double ChatWindow;
            internal int ChatCount;
            internal double LastHistoryRequest = -10;
            internal readonly Queue<Packet> Replay = new Queue<Packet>();
            internal long RosterRevision;
            internal int RosterOffset;
            internal double NextRosterReplay;
            internal bool Leaving;
        }

        private sealed class PendingChat
        {
            internal Packet Packet;
            internal double NextSend;
            internal double Expires;
        }

        private IRoomTransport transport;
        private readonly bool host;
        private readonly Func<IRoomTransport> reconnect;
        private readonly SessionOptions options;
        private readonly SessionProfile profile;
        private string[] hostCapabilities = new string[0];
        private string[] negotiatedWithHost = new string[0];
        private long hostConnectionGeneration;
        public SessionCompatibilityResult LastCompatibilityFailure { get; private set; }
        private readonly string password;
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        private readonly Dictionary<string, Seat> seats = new Dictionary<string, Seat>();
        private readonly List<ChatLine> chat = new List<ChatLine>();
        private readonly SortedDictionary<long, Packet> receivedChat = new SortedDictionary<long, Packet>();
        private readonly Dictionary<long, PendingChat> pendingChat = new Dictionary<long, PendingChat>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string resumeSecret = Protocol.NewNonce();
        private readonly Random random = new Random();
        private readonly RoomModeration moderation = new RoomModeration();
        private System.Action<string> saveModeration;
        private bool moderationBound;
        private string moderationScope;
        private readonly PagedCollection<PlayerInfo> rosterAssembly = new PagedCollection<PlayerInfo>();
        private long rosterRevision;
        private int nextPlayerNumber = 1;
        private double nextLeaveSend;
        private double leaveDeadline;
        private PlayerInfo[] players = new PlayerInfo[0];
        private double now;
        private double lastHeartbeat;
        private double retryAt;
        private double reconnectDeadline;
        private double attemptStarted;
        private double nextHistoryRequest;
        private double nextChatSendAt;
        private long sequence;
        private long advertisedSequence;
        private long nextMessageId;
        private bool disposed;
        private string localId;
        private int localChatCount;
        private double localChatWindow;

        public string GameplayId { get { return profile.GameplayId; } }
        public string GameplayVersion { get { return profile.GameplayVersion; } }
        public bool IsHost { get { return host; } }
        public bool Joined { get; private set; }
        public bool Started { get; private set; }
        public bool Closed { get; private set; }
        public bool Reconnecting { get; private set; }
        public bool Leaving { get; private set; }
        public int RetryAttempt { get; private set; }
        public double RetryInSeconds { get { return Reconnecting ? Math.Max(0, retryAt - now) : 0; } }
        public int PendingChatCount { get { return pendingChat.Count; } }
        public string LocalPlayerId { get { return localId; } }
        public double SmoothedRttMilliseconds { get; private set; }
        public string RoomId { get; private set; }
        public string RoomName { get; private set; }
        public string LastError { get; private set; }
        public string LastErrorDetail { get; private set; }
        public RoomBan[] Bans { get { return moderation.Bans; } }
        public PlayerInfo[] Players { get { return (PlayerInfo[])players.Clone(); } }
        public ChatLine[] Chat { get { return chat.ToArray(); } }
        public event System.Action Changed;
        public event System.Action GameStartRequested;
        public event System.Action<string, byte[]> ApplicationMessageReceived;

        public RoomSession(IRoomTransport transport, SessionProfile profile, string password, string roomName = "",
            Func<IRoomTransport> reconnect = null, SessionOptions options = null)
        {
            if (transport == null) throw new ArgumentNullException("transport");
            if (profile == null || !Protocol.ValidText(profile.Name, 32) ||
                !ValidMetadata(profile.GameVersion) || !ValidMetadata(profile.CycleTrimVersion) || !GameplayFramework.Available(profile.GameplayId, profile.GameplayVersion))
                throw new ArgumentException("dependencies_required");
            if ((password ?? "").Length > 128) throw new ArgumentException("password_too_long");
            if (transport.IsHost && !Protocol.ValidText(roomName, 64)) throw new ArgumentException("invalid_room_name");
            this.transport = transport;
            host = transport.IsHost;
            var reconnectable = transport as IReconnectableRoomTransport;
            this.reconnect = reconnect ?? (reconnectable == null ? (Func<IRoomTransport>)null : reconnectable.Reconnect);
            this.options = options ?? new SessionOptions();
            if (!ValidSeconds(this.options.HandshakeTimeoutSeconds) || !ValidSeconds(this.options.HeartbeatIntervalSeconds) ||
                !ValidSeconds(this.options.PeerTimeoutSeconds) || !ValidSeconds(this.options.ReconnectWindowSeconds) ||
                !ValidSeconds(this.options.SeatReservationSeconds) || !ValidSeconds(this.options.ChatRetrySeconds) ||
                this.options.ChatHistoryLimit < 1 || this.options.ChatHistoryLimit > SessionOptions.MaxChatHistoryLimit)
                throw new ArgumentException("invalid_session_options");
            this.profile = SessionCompatibility.Freeze(profile);
            this.password = password ?? "";
            if (IsHost)
            {
                Joined = true;
                RoomId = Guid.NewGuid().ToString("N");
                RoomName = roomName;
                localId = Guid.NewGuid().ToString("N");
                RefreshRoster();
            }
        }

        private static bool ValidMetadata(string value)
        { return string.IsNullOrEmpty(value) || Protocol.ValidText(value, 64); }

        // Capabilities negotiated with an authenticated endpoint. Other guests'
        // optional features are deliberately not inferred from the roster.
        public string[] CapabilitiesFor(string playerId)
        {
            if (!Joined || Closed || Reconnecting || Leaving || playerId == null) return new string[0];
            if (playerId == localId) return (string[])profile.Capabilities.Clone();
            if (!IsHost) return players.Length > 0 && playerId == players[0].Id ? (string[])negotiatedWithHost.Clone() : new string[0];
            Seat seat;
            return seats.TryGetValue(playerId, out seat) && seat.Connection != null
                ? (string[])seat.NegotiatedCapabilities.Clone() : new string[0];
        }

        public bool CanUseCapabilityWith(string playerId, string capability)
        { return CapabilitiesFor(playerId).Contains(capability, StringComparer.Ordinal); }

        // A player ID names a durable seat; this generation names its current
        // authenticated transport. Admission grants must bind to both values.
        public long ConnectionGenerationFor(string playerId)
        {
            if (!Joined || Closed || Reconnecting || Leaving || playerId == null) return 0;
            if (playerId == localId) return IsHost ? 1 : hostConnectionGeneration;
            if (!IsHost) return players.Length > 0 && playerId == players[0].Id ? hostConnectionGeneration : 0;
            Seat seat; Peer peer;
            return seats.TryGetValue(playerId, out seat) && seat.Connection != null &&
                peers.TryGetValue(seat.Connection, out peer) && peer.Authenticated && !peer.Leaving && peer.CloseAt == 0 &&
                ReferenceEquals(peer.Seat, seat) ? seat.ConnectionGeneration : 0;
        }

        // Producer/consumer services can inspect the authenticated endpoint's
        // advertised contracts even when the local endpoint is only a consumer.
        public string[] PeerCapabilitiesFor(string playerId)
        {
            if (!Joined || Closed || Reconnecting || Leaving || playerId == null) return new string[0];
            if (playerId == localId) return (string[])profile.Capabilities.Clone();
            if (!IsHost) return players.Length > 0 && playerId == players[0].Id ? (string[])hostCapabilities.Clone() : new string[0];
            Seat seat;
            return seats.TryGetValue(playerId, out seat) && seat.Connection != null
                ? (string[])seat.Capabilities.Clone() : new string[0];
        }

        private static bool ValidSeconds(double value) { return value > 0 && !double.IsInfinity(value) && !double.IsNaN(value); }
        public void Pump() { Pump(clock.Elapsed.TotalSeconds); }

        public void Pump(double seconds)
        {
            if (disposed || Closed) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now) throw new ArgumentOutOfRangeException("seconds");
            now = seconds;
            if (Leaving && now >= leaveDeadline) { Stop("leave_unconfirmed"); return; }
            if (Reconnecting)
            {
                if (now >= reconnectDeadline) { Stop("reconnect_expired"); return; }
                if (transport == null && now >= retryAt) ReconnectNow();
            }
            if (transport != null)
            {
                transport.Poll();
                TransportEvent item;
                int budget = 128;
                while (transport != null && budget-- > 0 && transport.TryReceive(out item))
                {
                    if (item.Kind == TransportEventKind.Connected) OnConnected(item.Peer);
                    else if (item.Kind == TransportEventKind.Disconnected) RemovePeer(item.Peer, false);
                    else if (item.Kind == TransportEventKind.Error)
                    {
                        if (!IsHost)
                        {
                            if (item.Error == "host_changed" || item.Error == "protocol_mismatch") Stop(item.Error);
                            else ConnectionLost(item.Error ?? "connect_failed");
                        }
                        else { LastError = item.Error; Notify(); }
                    }
                    else
                    {
                        Peer peer;
                        if (!peers.TryGetValue(item.Peer, out peer)) continue;
                        if (peer.CloseAt > 0)
                        {
                            if (IsHost && peer.Leaving && !item.Realtime)
                            {
                                try { if (Protocol.Decode(item.Data).Kind == PacketKind.Leave) Send(item.Peer, new Packet { Kind = PacketKind.LeaveAck }); }
                                catch (Exception error) when (error is InvalidDataException || error is ArgumentException) { }
                            }
                            continue;
                        }
                        try
                        {
                            var packet = Protocol.Decode(item.Data);
                            if (item.Realtime && (!RealtimeReady || !peer.Authenticated || packet.Kind != PacketKind.Application ||
                                !RoomVoiceWire.IsVoicePayload(packet.Payload))) continue;
                            Receive(item.Peer, peer, packet);
                        }
                        catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is FormatException)
                        {
                            if (!item.Realtime) Reject(item.Peer, peer,
                                error is InvalidDataException && error.Message == "protocol_mismatch" ? "protocol_mismatch" : "invalid_packet");
                        }
                    }
                }
            }
            if (Leaving && Joined && transport != null && now >= nextLeaveSend)
            { Send(transport.HostPeer, new Packet { Kind = PacketKind.Leave }); nextLeaveSend = now + 1; }
            foreach (var entry in peers.ToArray())
            {
                Peer peer = entry.Value;
                if (peer.CloseAt > 0 ? now >= peer.CloseAt : peer.Authenticated
                    ? now - peer.LastSeen > options.PeerTimeoutSeconds
                    : now - peer.ConnectedAt > options.HandshakeTimeoutSeconds)
                {
                    if (transport != null) transport.Disconnect(entry.Key);
                    RemovePeer(entry.Key, false);
                }
                else if (peer.Authenticated && peer.CloseAt == 0)
                {
                    int budget = 4;
                    while (budget-- > 0 && peer.Replay.Count > 0)
                    {
                        if (!Send(entry.Key, peer.Replay.Peek())) break;
                        peer.Replay.Dequeue();
                    }
                    if (IsHost) SendRosterPage(entry.Key, peer);
                }
            }
            if (!IsHost && transport != null && !Joined && peers.Count == 0 && now - attemptStarted > options.HandshakeTimeoutSeconds)
                ConnectionLost("connect_failed");
            if (IsHost)
            {
                bool changed = false;
                foreach (var seat in seats.Values.Where(s => s.Connection == null && now - s.DisconnectedAt >= options.SeatReservationSeconds).ToArray())
                { seats.Remove(seat.Id); changed = true; }
                if (changed) { RefreshRoster(); Notify(); }
            }
            if (now - lastHeartbeat >= options.HeartbeatIntervalSeconds)
            {
                lastHeartbeat = now;
                foreach (var entry in peers.Where(p => p.Value.Authenticated).ToArray())
                {
                    Send(entry.Key, new Packet { Kind = PacketKind.Ping, Sequence = (long)(now * 1000), MessageId = sequence });
                    if (IsHost && Started) Send(entry.Key, new Packet { Kind = PacketKind.StartGame });
                }
            }
            if (Joined && !IsHost)
            {
                foreach (var pending in pendingChat.Values.OrderBy(p => p.Packet.MessageId).ToArray())
                {
                    if (now >= pending.Expires) { pendingChat.Remove(pending.Packet.MessageId); LastError = "chat_delivery_failed"; Notify(); }
                    else if (now >= pending.NextSend && now >= nextChatSendAt)
                    {
                        Send(transport.HostPeer, pending.Packet);
                        pending.NextSend = now + Math.Max(options.ChatRetrySeconds, SmoothedRttMilliseconds / 500);
                        nextChatSendAt = now + 0.45;
                    }
                }
                if ((receivedChat.Count > 0 || advertisedSequence > sequence) && now >= nextHistoryRequest)
                { Send(transport.HostPeer, new Packet { Kind = PacketKind.HistoryRequest, Sequence = sequence }); nextHistoryRequest = now + 3; }
            }
        }

        private void ReconnectNow()
        {
            RetryAttempt++;
            try
            {
                transport = reconnect();
                if (transport == null || transport.IsHost) throw new InvalidOperationException("invalid_reconnect_transport");
                attemptStarted = now;
                Notify();
            }
            catch (Exception) { transport = null; ScheduleRetry(); }
        }

        private void ConnectionLost(string reason)
        {
            if (Closed || disposed) return;
            Joined = false;
            peers.Clear();
            rosterAssembly.Reset();
            var old = transport; transport = null;
            if (old != null) old.Dispose();
            if (reconnect == null) { Stop(reason ?? "host_disconnected"); return; }
            if (!Reconnecting) { Reconnecting = true; reconnectDeadline = now + options.ReconnectWindowSeconds; RetryAttempt = 0; }
            LastError = "reconnecting";
            ScheduleRetry();
        }

        private void ScheduleRetry()
        {
            double jitter = options.RetryJitter == null ? random.NextDouble() : options.RetryJitter();
            if (double.IsNaN(jitter) || double.IsInfinity(jitter)) jitter = 0.5;
            retryAt = now + Math.Min(15, Math.Pow(2, Math.Min(RetryAttempt, 4))) * (0.8 + Math.Max(0, Math.Min(1, jitter)) * 0.4);
            Notify();
        }

        private void Stop(string reason, string detail = null)
        {
            LastError = reason; LastErrorDetail = detail; Joined = false; Closed = true; Reconnecting = false; Leaving = false;
            var old = transport; transport = null;
            peers.Clear();
            if (old != null) old.Dispose();
            Notify();
        }

        private void OnConnected(string key)
        {
            if (peers.ContainsKey(key)) return;
            if ((!IsHost && key != transport.HostPeer) || peers.Values.Count(p => !p.Authenticated) >= 32) { transport.Disconnect(key); return; }
            var peer = new Peer { ConnectedAt = now, LastSeen = now };
            peers.Add(key, peer);
            if (IsHost)
            {
                peer.Challenge = Protocol.NewNonce();
                Send(key, new Packet { Kind = PacketKind.Challenge, Nonce = peer.Challenge, Room = RoomId });
            }
        }

        private void Receive(string key, Peer peer, Packet packet)
        {
            peer.LastSeen = now;
            if (packet.Kind == PacketKind.Reject && !IsHost)
            {
                if (packet.Text == "capability_mismatch")
                    LastCompatibilityFailure = SessionCompatibility.Evaluate(packet.Capabilities, packet.RequiredCapabilities,
                        profile.Capabilities, profile.RequiredCapabilities);
                Stop(Protocol.ValidText(packet.Text, 64) ? packet.Text : "connection_rejected"); return;
            }
            if (!peer.Authenticated)
            {
                if (IsHost && packet.Kind == PacketKind.Hello) AcceptHello(key, peer, packet);
                else if (!IsHost && packet.Kind == PacketKind.Challenge)
                {
                    if (peer.Challenge != null || packet.Nonce == null || packet.Nonce.Length != 44 ||
                        packet.Room == null || packet.Room.Length != 32) throw new InvalidDataException();
                    if (RoomId != null && RoomId != packet.Room) { Stop("resume_invalid"); return; }
                    peer.Challenge = packet.Nonce;
                    RoomId = packet.Room;
                    var hello = new Packet { Kind = PacketKind.Hello, Name = profile.Name,
                        GameVersion = profile.GameVersion, CycleTrimVersion = profile.CycleTrimVersion, SpacedOut = profile.SpacedOut,
                        Capabilities = profile.Capabilities, RequiredCapabilities = profile.RequiredCapabilities,
                        GameplayId = profile.GameplayId, GameplayVersion = profile.GameplayVersion,
                        Nonce = Protocol.NewNonce(), PlayerId = localId ?? "", ResumeToken = localId == null ? resumeSecret : "", Sequence = sequence };
                    hello.ResumeProof = Protocol.ResumeProof(resumeSecret, peer.Challenge, hello);
                    hello.Proof = Protocol.PasswordProof(password, peer.Challenge, hello);
                    Send(key, hello);
                }
                else if (!IsHost && packet.Kind == PacketKind.Welcome && peer.Challenge != null)
                {
                    ValidateRoster(packet.Players);
                    if (!Protocol.ValidText(packet.Name, 64) || packet.PlayerId == null || packet.PlayerId.Length != 32 ||
                        !packet.Players.Any(p => p.Id == packet.PlayerId) || packet.Room != RoomId ||
                        packet.Sequence < sequence || packet.HistoryStart < 1 || packet.HistoryStart > packet.Sequence + 1 ||
                        (localId != null && packet.PlayerId != localId)) throw new InvalidDataException();
                    if (packet.GameplayId != profile.GameplayId || packet.GameplayVersion != profile.GameplayVersion)
                    { Stop("gameplay_mismatch"); return; }
                    var compatibility = SessionCompatibility.Evaluate(packet.Capabilities, packet.RequiredCapabilities,
                        profile.Capabilities, profile.RequiredCapabilities);
                    if (!compatibility.Compatible)
                    { LastCompatibilityFailure = compatibility; Stop("capability_mismatch"); return; }
                    hostCapabilities = (string[])packet.Capabilities.Clone();
                    negotiatedWithHost = compatibility.Negotiated; LastCompatibilityFailure = null;
                    hostConnectionGeneration = checked(hostConnectionGeneration + 1);
                    peer.Authenticated = true; Joined = true; Reconnecting = false; RetryAttempt = 0; LastError = null;
                    localId = packet.PlayerId; players = packet.Players; RoomName = packet.Name;
                    advertisedSequence = packet.Sequence;
                    bool startChanged = !Started && packet.Started;
                    Started = packet.Started;
                    if (sequence < packet.HistoryStart - 1) sequence = packet.HistoryStart - 1;
                    receivedChat.Clear();
                    foreach (var pending in pendingChat.Values) pending.NextSend = now;
                    Notify();
                    if (startChanged && GameStartRequested != null) GameStartRequested();
                }
                else Reject(key, peer, "handshake_required");
                return;
            }
            if (packet.Kind == PacketKind.Ping)
            {
                if (!IsHost) advertisedSequence = Math.Max(advertisedSequence, packet.MessageId);
                Send(key, new Packet { Kind = PacketKind.Pong, Sequence = packet.Sequence, MessageId = packet.MessageId });
                return;
            }
            if (packet.Kind == PacketKind.Pong)
            {
                double sample = now * 1000 - packet.Sequence;
                if (sample >= 0 && sample < options.PeerTimeoutSeconds * 1000)
                {
                    double previous = SmoothedRttMilliseconds;
                    SmoothedRttMilliseconds = previous == 0 ? sample : previous * 0.8 + sample * 0.2;
                    if (Math.Abs(previous - SmoothedRttMilliseconds) >= 25) Notify();
                }
                return;
            }
            if (packet.Kind == PacketKind.Leave)
            {
                if (!IsHost) { Stop("host_left"); return; }
                peer.Authenticated = false; peer.Leaving = true; peer.CloseAt = now + options.HandshakeTimeoutSeconds;
                seats.Remove(peer.Seat.Id); peer.Replay.Clear(); RefreshRoster();
                Send(key, new Packet { Kind = PacketKind.LeaveAck }); Notify(); return;
            }
            if (!IsHost && Leaving && packet.Kind == PacketKind.LeaveAck) { Stop("left_room"); return; }
            if (!IsHost && packet.Kind == PacketKind.Removed)
            {
                if ((packet.Name != "player_kicked" && packet.Name != "player_banned") ||
                    (packet.Text.Length > 0 && !Protocol.ValidText(packet.Text, 128))) throw new InvalidDataException();
                Stop(packet.Name, packet.Text); return;
            }
            if (packet.Kind == PacketKind.Application)
            {
                if (packet.Payload == null || packet.Payload.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException();
                string sender = IsHost ? peer.Seat.Id : packet.PlayerId;
                if (!IsHost && (sender == null || sender.Length != 32)) throw new InvalidDataException();
                if (!IsHost && !players.Any(p => p.Id == sender)) return; // A roster page may still be in flight.
                if (ApplicationMessageReceived != null) ApplicationMessageReceived(sender, (byte[])packet.Payload.Clone());
                return;
            }
            if (IsHost)
            {
                if (packet.Kind == PacketKind.HistoryRequest)
                {
                    if (packet.Sequence < 0 || packet.Sequence > sequence) throw new InvalidDataException();
                    if (now - peer.LastHistoryRequest >= 2) { peer.LastHistoryRequest = now; QueueHistory(peer, packet.Sequence); }
                    return;
                }
                if (packet.Kind != PacketKind.ChatSubmit) { Reject(key, peer, "host_only"); return; }
                if (!Protocol.ValidText(packet.Text, Protocol.MaxChatLength) || packet.MessageId <= 0) { Reject(key, peer, "invalid_chat"); return; }
                if (!peer.Seat.AcceptedMessages.ContainsKey(packet.MessageId) && packet.MessageId > peer.Seat.HighestAcceptedMessage - 1024)
                {
                    if (now - peer.ChatWindow >= 2) { peer.ChatWindow = now; peer.ChatCount = 0; }
                    // A stalled reliable stream can deliver a legitimate backlog in one
                    // frame. Leave excess submissions unacknowledged for a paced retry.
                    if (++peer.ChatCount > 5) return;
                    PublishChat(peer.Seat.Id, peer.Seat.Name, packet.Text, packet.MessageId);
                    peer.Seat.HighestAcceptedMessage = Math.Max(peer.Seat.HighestAcceptedMessage, packet.MessageId);
                    peer.Seat.AcceptedMessages.Add(packet.MessageId, sequence);
                    peer.Seat.AcceptedOrder.Enqueue(packet.MessageId);
                    while (peer.Seat.AcceptedOrder.Count > 1024) peer.Seat.AcceptedMessages.Remove(peer.Seat.AcceptedOrder.Dequeue());
                }
                Send(key, new Packet { Kind = PacketKind.ChatAck, MessageId = packet.MessageId });
            }
            else
            {
                if (packet.Kind == PacketKind.Roster)
                {
                    ValidateRoster(packet.Players);
                    if (!packet.Players.Any(p => p.Id == localId)) throw new InvalidDataException();
                    players = packet.Players; Notify();
                }
                else if (packet.Kind == PacketKind.RosterPart)
                {
                    ValidateRoster(packet.Players);
                    PlayerInfo[] complete;
                    if (rosterAssembly.Accept(packet.RosterRevision, packet.RosterTotal, packet.RosterOffset,
                        packet.Players, Protocol.Encode(packet).Length, out complete))
                    {
                        ValidateRoster(complete);
                        if (!complete.Any(p => p.Id == localId)) throw new InvalidDataException();
                        players = complete; Notify();
                    }
                }
                else if (packet.Kind == PacketKind.Chat)
                {
                    if (!Protocol.ValidText(packet.Name, 32) || !Protocol.ValidText(packet.Text, Protocol.MaxChatLength) ||
                        packet.PlayerId == null || packet.PlayerId.Length != 32 || packet.Sequence <= 0) throw new InvalidDataException();
                    if (packet.PlayerId == localId && packet.MessageId > 0) pendingChat.Remove(packet.MessageId);
                    if (packet.Sequence > sequence && !receivedChat.ContainsKey(packet.Sequence))
                    {
                        if (receivedChat.Count >= SessionOptions.MaxChatHistoryLimit) throw new InvalidDataException();
                        receivedChat.Add(packet.Sequence, packet);
                        Packet next;
                        while (receivedChat.TryGetValue(sequence + 1, out next))
                        { receivedChat.Remove(++sequence); AddChat(next); }
                    }
                }
                else if (packet.Kind == PacketKind.HistoryReset)
                {
                    if (packet.Sequence < 0) throw new InvalidDataException();
                    if (packet.Sequence > sequence)
                    {
                        sequence = packet.Sequence;
                        foreach (long number in receivedChat.Keys.Where(n => n <= sequence).ToArray()) receivedChat.Remove(number);
                        LastError = "chat_history_expired"; Notify();
                    }
                }
                else if (packet.Kind == PacketKind.ChatAck)
                { if (packet.MessageId <= 0) throw new InvalidDataException(); if (pendingChat.Remove(packet.MessageId)) Notify(); }
                else if (packet.Kind == PacketKind.StartGame)
                { if (!Started) { Started = true; Notify(); if (GameStartRequested != null) GameStartRequested(); } }
                else Reject(key, peer, "invalid_packet");
            }
        }

        private void AcceptHello(string key, Peer peer, Packet hello)
        {
            string error = null;
            Seat seat = null;
            var authenticated = transport as IAuthenticatedRoomTransport;
            string account = authenticated == null ? null : authenticated.GetAuthenticatedIdentity(key);
            if (!Protocol.ValidText(hello.Name, 32) || hello.Nonce == null || hello.Nonce.Length != 44 || hello.Sequence < 0 || hello.Sequence > sequence)
                error = "invalid_name";
            else if (!ValidMetadata(hello.GameVersion) || !ValidMetadata(hello.CycleTrimVersion)) error = "invalid_profile";
            else if (hello.GameplayId != profile.GameplayId || hello.GameplayVersion != profile.GameplayVersion) error = "gameplay_mismatch";
            else if (!Protocol.EqualProof(Protocol.PasswordProof(password, peer.Challenge, hello), hello.Proof)) error = "wrong_password";
            else if (moderation.Rejection(hello.PlayerId, hello.ResumeToken, account) is string rejection) error = rejection;
            else if (!string.IsNullOrEmpty(hello.PlayerId))
            {
                if (!seats.TryGetValue(hello.PlayerId, out seat) ||
                    (seat.Connection == null && now - seat.DisconnectedAt >= options.SeatReservationSeconds) ||
                    seat.Account != account || !Protocol.EqualProof(Protocol.ResumeProof(seat.Secret, peer.Challenge, hello), hello.ResumeProof)) error = "resume_invalid";
            }
            else if (hello.ResumeToken == null || hello.ResumeToken.Length != 44 ||
                !Protocol.EqualProof(Protocol.ResumeProof(hello.ResumeToken, peer.Challenge, hello), hello.ResumeProof)) error = "resume_invalid";
            var compatibility = SessionCompatibility.Evaluate(profile.Capabilities, profile.RequiredCapabilities,
                hello.Capabilities, hello.RequiredCapabilities);
            if (error == null && !compatibility.Compatible)
            { LastCompatibilityFailure = compatibility; error = "capability_mismatch"; }
            if (error == null && string.IsNullOrEmpty(hello.PlayerId))
            {
                // The first Welcome may have been lost. Reuse the pending identity bound
                // to this secret instead of consuming another player slot.
                seat = seats.Values.FirstOrDefault(s => Protocol.EqualProof(s.Secret, hello.ResumeToken));
                if (seat != null && seat.Account != account) error = "resume_invalid";
            }
            if (error != null) { Reject(key, peer, error); return; }
            if (seat == null)
            {
                seat = new Seat { Id = Guid.NewGuid().ToString("N"), Name = hello.Name, Secret = hello.ResumeToken, Account = account,
                    Number = checked(++nextPlayerNumber) };
                seats.Add(seat.Id, seat);
            }
            if (seat.Connection != null && seat.Connection != key)
            {
                string old = seat.Connection;
                peers.Remove(old); transport.Disconnect(old);
            }
            seat.ConnectionGeneration = checked(seat.ConnectionGeneration + 1);
            seat.Connection = key; seat.Capabilities = (string[])hello.Capabilities.Clone(); seat.NegotiatedCapabilities = compatibility.Negotiated;
            peer.Seat = seat; peer.Authenticated = true;
            RefreshRoster();
            Send(key, new Packet { Kind = PacketKind.Welcome, Room = RoomId, Name = RoomName, PlayerId = seat.Id,
                Players = new[] { players[0], players.First(p => p.Id == seat.Id) }, Started = Started, Sequence = sequence,
                HistoryStart = chat.Count == 0 ? sequence + 1 : chat[0].Sequence,
                GameplayId = profile.GameplayId, GameplayVersion = profile.GameplayVersion,
                Capabilities = profile.Capabilities, RequiredCapabilities = profile.RequiredCapabilities });
            QueueHistory(peer, hello.Sequence);
            Notify();
        }

        private void QueueHistory(Peer peer, long after)
        {
            peer.Replay.Clear();
            if (chat.Count > 0 && after < chat[0].Sequence - 1)
                peer.Replay.Enqueue(new Packet { Kind = PacketKind.HistoryReset, Sequence = chat[0].Sequence - 1 });
            foreach (var line in chat.Where(c => c.Sequence > after))
                peer.Replay.Enqueue(new Packet { Kind = PacketKind.Chat, PlayerId = line.PlayerId, Name = line.Name,
                    Text = line.Text, MessageId = line.MessageId, Sequence = line.Sequence });
        }

        private static void ValidateRoster(PlayerInfo[] roster)
        {
            if (roster == null || roster.Length < 1 ||
                roster.Any(p => p == null || p.Id == null || p.Id.Length != 32 || !Protocol.ValidText(p.Name, 32)) ||
                roster.Select(p => p.Id).Distinct().Count() != roster.Length) throw new InvalidDataException("invalid_roster");
        }

        private void Reject(string key, Peer peer, string reason)
        {
            if (IsHost)
            {
                Send(key, new Packet { Kind = PacketKind.Reject, Text = reason,
                    Capabilities = profile.Capabilities, RequiredCapabilities = profile.RequiredCapabilities });
                peer.CloseAt = now + 1;
            }
            else Stop(reason);
        }

        private void RemovePeer(string key, bool explicitLeave)
        {
            Peer peer;
            if (!peers.TryGetValue(key, out peer)) return;
            peers.Remove(key);
            if (IsHost)
            {
                if (peer.Seat != null && peer.Seat.Connection == key)
                {
                    peer.Seat.Connection = null; peer.Seat.DisconnectedAt = now;
                    if (explicitLeave) seats.Remove(peer.Seat.Id);
                }
                RefreshRoster(); Notify();
            }
            else ConnectionLost("host_disconnected");
        }

        private void RefreshRoster()
        {
            rosterRevision++;
            players = new[] { new PlayerInfo { Id = localId, Name = profile.Name, Number = 1, Connected = true } }
                .Concat(seats.Values.Select(s => new PlayerInfo { Id = s.Id, Name = s.Name, Number = s.Number, Connected = s.Connection != null })).ToArray();
        }

        private void SendRosterPage(string key, Peer peer)
        {
            if (peer.RosterRevision != rosterRevision)
            { peer.RosterRevision = rosterRevision; peer.RosterOffset = 0; }
            if (peer.RosterOffset >= players.Length)
            {
                if (now < peer.NextRosterReplay) return;
                peer.RosterOffset = 0;
            }
            var page = players.Skip(peer.RosterOffset).Take(Protocol.RosterPageSize).ToArray();
            if (Send(key, new Packet { Kind = PacketKind.RosterPart, RosterRevision = rosterRevision,
                RosterTotal = players.Length, RosterOffset = peer.RosterOffset, Players = page }))
            { peer.RosterOffset += page.Length; peer.NextRosterReplay = now + 10; }
        }

        private bool Send(string key, Packet packet) { return transport != null && transport.Send(key, Protocol.Encode(packet)); }
        private void Broadcast(Packet packet)
        {
            if (transport == null) return;
            byte[] bytes = Protocol.Encode(packet);
            foreach (var entry in peers.Where(p => p.Value.Authenticated && p.Value.CloseAt == 0).ToArray()) transport.Send(entry.Key, bytes);
        }

        public bool SendChat(string text)
        {
            if ((!Joined && !Reconnecting) || Closed || disposed || Leaving || !Protocol.ValidText(text, Protocol.MaxChatLength)) return false;
            if (now - localChatWindow >= 2) { localChatWindow = now; localChatCount = 0; }
            if (++localChatCount > 5) return false;
            if (IsHost) { PublishChat(localId, profile.Name, text, ++nextMessageId); return true; }
            if (pendingChat.Count >= 20) { LastError = "chat_queue_full"; Notify(); return false; }
            var pending = new PendingChat { Packet = new Packet { Kind = PacketKind.ChatSubmit, Text = text, MessageId = ++nextMessageId },
                NextSend = now, Expires = now + options.ReconnectWindowSeconds };
            pendingChat.Add(pending.Packet.MessageId, pending);
            Notify(); return true;
        }

        private void PublishChat(string id, string name, string text, long messageId)
        {
            var packet = new Packet { Kind = PacketKind.Chat, PlayerId = id, Name = name, Text = text, Sequence = ++sequence, MessageId = messageId };
            AddChat(packet); Broadcast(packet);
        }

        private void AddChat(Packet packet)
        {
            if (chat.Count >= options.ChatHistoryLimit) chat.RemoveAt(0);
            chat.Add(new ChatLine { Sequence = packet.Sequence, PlayerId = packet.PlayerId, Name = packet.Name, Text = packet.Text, MessageId = packet.MessageId });
            Notify();
        }

        public bool SendApplication(byte[] payload)
        {
            if (IsHost || !Joined || Closed || disposed || payload == null || payload.Length > Protocol.MaxApplicationBytes) return false;
            return Send(transport.HostPeer, new Packet { Kind = PacketKind.Application, PlayerId = localId, Payload = payload });
        }

        public bool SendApplicationTo(string playerId, byte[] payload)
        {
            if (!IsHost || !Joined || Closed || disposed || payload == null || payload.Length > Protocol.MaxApplicationBytes) return false;
            Seat seat;
            return seats.TryGetValue(playerId, out seat) && seat.Connection != null &&
                Send(seat.Connection, new Packet { Kind = PacketKind.Application, PlayerId = localId, Payload = payload });
        }

        internal bool SendRealtimeApplication(byte[] payload)
        {
            if (IsHost || !RealtimeReady || !RoomVoiceWire.IsVoicePayload(payload) || payload.Length > Protocol.MaxApplicationBytes) return false;
            var realtime = transport as IRealtimeRoomTransport;
            return realtime != null && realtime.SendRealtime(transport.HostPeer,
                Protocol.Encode(new Packet { Kind = PacketKind.Application, PlayerId = localId, Payload = payload }));
        }

        internal bool ForwardRealtimeApplication(string senderPlayerId, string targetPlayerId, byte[] payload)
        {
            if (!IsHost || !RealtimeReady || senderPlayerId == null || targetPlayerId == null || !RoomVoiceWire.IsVoicePayload(payload) ||
                payload.Length > Protocol.MaxApplicationBytes) return false;
            Seat sender;
            Peer senderPeer;
            if (senderPlayerId != localId && (!seats.TryGetValue(senderPlayerId, out sender) || sender.Connection == null ||
                !peers.TryGetValue(sender.Connection, out senderPeer) || !senderPeer.Authenticated || senderPeer.CloseAt != 0)) return false;
            Seat target;
            Peer targetPeer;
            if (!seats.TryGetValue(targetPlayerId, out target) || target.Connection == null ||
                !peers.TryGetValue(target.Connection, out targetPeer) || !targetPeer.Authenticated || targetPeer.CloseAt != 0) return false;
            var realtime = transport as IRealtimeRoomTransport;
            return realtime != null && realtime.SendRealtime(target.Connection,
                Protocol.Encode(new Packet { Kind = PacketKind.Application, PlayerId = senderPlayerId, Payload = payload }));
        }

        private bool RealtimeReady
        { get { return !Closed && !disposed && Joined && !Reconnecting && !Leaving && transport != null; } }

        public bool ForwardApplication(string senderPlayerId, string targetPlayerId, byte[] payload)
        {
            if (!IsHost || !Joined || Closed || disposed || payload == null || payload.Length > Protocol.MaxApplicationBytes ||
                (senderPlayerId != localId && !seats.ContainsKey(senderPlayerId))) return false;
            var packet = new Packet { Kind = PacketKind.Application, PlayerId = senderPlayerId, Payload = payload };
            if (targetPlayerId == null) { Broadcast(packet); return true; }
            Seat target;
            return seats.TryGetValue(targetPlayerId, out target) && target.Connection != null && Send(target.Connection, packet);
        }

        public void BroadcastApplication(byte[] payload)
        {
            if (!IsHost || !Joined || Closed || disposed || payload == null || payload.Length > Protocol.MaxApplicationBytes) return;
            Broadcast(new Packet { Kind = PacketKind.Application, PlayerId = localId, Payload = payload });
        }

        public bool StartGame()
        {
            if (!IsHost || !Joined || Started || Closed || disposed) return false;
            Started = true; Broadcast(new Packet { Kind = PacketKind.StartGame }); Notify();
            if (GameStartRequested != null) GameStartRequested();
            return true;
        }

        public bool RequestLeave()
        {
            if (IsHost || Closed || disposed || (!Joined && !Reconnecting)) return false;
            if (Leaving) return true;
            Leaving = true; nextLeaveSend = 0; leaveDeadline = now + Math.Max(30, options.HandshakeTimeoutSeconds);
            pendingChat.Clear(); Notify(); return true;
        }

        public bool RemovePlayer(string playerId, bool ban, string reason = "")
        {
            Seat seat;
            reason = reason ?? "";
            if (!IsHost || !Joined || Closed || disposed || playerId == null ||
                (reason.Length > 0 && !Protocol.ValidText(reason, 128)) || !seats.TryGetValue(playerId, out seat) ||
                !moderation.Revoke(seat.Id, seat.Name, seat.Secret, seat.Account, ban, reason)) return false;
            // Revoke before notifying: queued application/chat packets can no longer execute.
            // A verified account may already own more than one admitted connection.
            // Its ban revokes every current seat, not just the row selected in the UI.
            var revoked = ban && seat.Account != null
                ? seats.Values.Where(s => s.Account == seat.Account).ToArray() : new[] { seat };
            foreach (var removed in revoked)
                RevokeSeat(removed, ban ? "player_banned" : "player_kicked", reason);
            if (ban) PersistModeration();
            RefreshRoster(); Notify();
            return true;
        }

        public bool Unban(string banId)
        {
            if (!IsHost || !Joined || Closed || disposed || !moderation.Unban(banId)) return false;
            PersistModeration();
            Notify(); return true;
        }

        // Bind before the first transport poll. A bad or foreign save must not
        // admit peers with an accidentally empty ban list.
        internal void BindModeration(string homeSystemId, string payload, System.Action<string> save)
        {
            if (!IsHost || !Joined || Closed || disposed || moderationBound || peers.Count != 0 || seats.Count != 0 || save == null)
                throw new InvalidOperationException("moderation_already_active");
            LoadModeration(homeSystemId, payload, save);
        }

        // Called by the main thread before Pump when native load replaces the
        // colony object. The loaded save supplies the account decisions; live
        // LAN revocations remain scoped to this connection session.
        internal void LoadModeration(string homeSystemId, string payload, System.Action<string> save)
        {
            if (!IsHost || !Joined || Closed || disposed || save == null ||
                (moderationBound && moderationScope != homeSystemId))
                throw new InvalidOperationException("moderation_already_active");
            moderation.Restore(homeSystemId, payload, !moderationBound);
            saveModeration = save; moderationScope = homeSystemId; moderationBound = true;
            foreach (var seat in seats.Values.ToArray())
            {
                var ban = moderation.BanForAccount(seat.Account);
                if (ban != null) RevokeSeat(seat, "player_banned", ban.Reason);
            }
            PersistModeration();
            RefreshRoster(); Notify();
        }

        private void RevokeSeat(Seat seat, string reason, string detail)
        {
            seats.Remove(seat.Id);
            if (seat.Connection == null) return;
            Peer peer;
            if (!peers.TryGetValue(seat.Connection, out peer)) return;
            peer.Authenticated = false; peer.Replay.Clear();
            peer.CloseAt = now + options.HandshakeTimeoutSeconds;
            Send(seat.Connection, new Packet { Kind = PacketKind.Removed, Name = reason, Text = detail });
        }

        private void PersistModeration()
        {
            if (saveModeration == null) return;
            try { saveModeration(moderation.Capture()); }
            catch (Exception error) when (error is InvalidOperationException || error is IOException || error is ArgumentException)
            { LastError = "moderation_save_failed"; LastErrorDetail = null; }
        }

        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        {
            if (disposed) return;
            // Retire before calling third-party transports: failed/reentrant Leave sends must
            // neither keep the session live nor skip releasing its transport.
            bool wasJoined = Joined;
            var old = transport; transport = null;
            disposed = true; Joined = false; Closed = true; Reconnecting = false; Leaving = false;
            var cleanup = new RoomCleanup();
            if (old != null)
            {
                byte[] leave = Protocol.Encode(new Packet { Kind = PacketKind.Leave });
                if (IsHost)
                    foreach (var peer in peers.Where(p => p.Value.Authenticated && p.Value.CloseAt == 0).ToArray())
                    {
                        string key = peer.Key;
                        cleanup.Run("leave_send", () => { if (!old.Send(key, leave)) cleanup.Note("leave_send", "not_sent"); });
                    }
                else if (wasJoined)
                    cleanup.Run("leave_send", () => { if (!old.Send(old.HostPeer, leave)) cleanup.Note("leave_send", "not_sent"); });
                cleanup.Dispose("transport", old);
            }
            peers.Clear(); pendingChat.Clear();
            CleanupStatus = cleanup.Status;
            cleanup.ThrowIfFailed();
        }
    }
}
