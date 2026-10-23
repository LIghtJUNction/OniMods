using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using OniOnline.Network;
using Steamworks;

namespace OniOnline.Runtime
{
    internal sealed class SteamRoomTransport : IRoomTransport, IReconnectableRoomTransport, IAuthenticatedRoomTransport, IRealtimeRoomTransport, IRoomCleanupStatus
    {
        private const int Channel = 27140;
        private const int RealtimeChannel = 27142;
        private readonly CSteamID lobby;
        private readonly CSteamID owner;
        private readonly ulong self;
        private readonly Queue<TransportEvent> inbox = new Queue<TransportEvent>();
        private readonly HashSet<ulong> peers = new HashSet<ulong>();
        private readonly Dictionary<ulong, double> blocked = new Dictionary<ulong, double>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly Dictionary<ulong, long> LatestAttempt = new Dictionary<ulong, long>();
        private static long nextAttempt;
        private readonly long attempt;
        private readonly CallResult<LobbyEnter_t> reentered;
        private bool joinPending;
        private bool ready;
        private readonly Callback<SteamNetworkingMessagesSessionRequest_t> request;
        private readonly IntPtr[] incoming = new IntPtr[32];
        private bool disposed;
        public string CleanupStatus { get; private set; }
        public bool IsHost { get; private set; }
        public string HostPeer { get { return owner.m_SteamID.ToString(); } }

        public string GetAuthenticatedIdentity(string peer)
        {
            ulong id;
            // Peer keys originate only from Steam lobby/network identities. Keep that
            // identity when processing a queued Hello after Steam reports disconnect.
            return ulong.TryParse(peer, out id) && new CSteamID(id).IsValid() ? "steam:" + id.ToString() : null;
        }

        internal SteamRoomTransport(CSteamID lobby) : this(lobby, SteamMatchmaking.GetLobbyOwner(lobby), false) { }

        private SteamRoomTransport(CSteamID lobby, CSteamID expectedOwner, bool rejoin)
        {
            this.lobby = lobby;
            owner = expectedOwner;
            attempt = ++nextAttempt;
            LatestAttempt[lobby.m_SteamID] = attempt;
            self = SteamUser.GetSteamID().m_SteamID;
            IsHost = owner.m_SteamID == self;
            request = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
            SteamNetworkingUtils.InitRelayNetworkAccess();
            reentered = CallResult<LobbyEnter_t>.Create(OnReentered);
            ready = !rejoin || ContainsSelf();
            if (!ready)
            {
                joinPending = true;
                reentered.Set(SteamMatchmaking.JoinLobby(lobby));
            }
        }

        private bool ContainsSelf()
        {
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == self) return true;
            return false;
        }

        private void OnReentered(LobbyEnter_t result, bool failure)
        {
            joinPending = false;
            bool success = !failure && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            if (disposed)
            {
                var cleanup = new RoomCleanup();
                bool available = false;
                cleanup.Run("steam_state", () => available = SteamRoomCleanup.ClientAvailable);
                long latest;
                if (success && LatestAttempt.TryGetValue(lobby.m_SteamID, out latest) && latest == attempt)
                {
                    if (available) cleanup.Run("late_lobby_leave", () => SteamMatchmaking.LeaveLobby(lobby));
                    else cleanup.Note("late_lobby_leave", "sdk_inactive");
                }
                cleanup.Dispose("join_callback", reentered);
                CleanupStatus = cleanup.Status;
                cleanup.ThrowIfFailed();
                return;
            }
            if (!success) { inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "steam_lobby_failed" }); return; }
            if (SteamMatchmaking.GetLobbyOwner(lobby) != owner)
            { inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "host_changed" }); return; }
            ready = true;
        }

        public IRoomTransport Reconnect()
        {
            if (IsHost) throw new InvalidOperationException("host_only");
            return new SteamRoomTransport(lobby, owner, true);
        }

        private bool IsBlocked(ulong id)
        {
            double until;
            return blocked.TryGetValue(id, out until) && until > clock.Elapsed.TotalSeconds;
        }

        private HashSet<ulong> Members()
        {
            var members = new HashSet<ulong>();
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count; i++)
            {
                ulong id = SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID;
                if (id != self && (IsHost || id == owner.m_SteamID)) members.Add(id);
            }
            return members;
        }

        private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t item)
        {
            if (disposed) return;
            ulong id = item.m_identityRemote.GetSteamID64();
            if (!IsBlocked(id) && Members().Contains(id))
                SteamNetworkingMessages.AcceptSessionWithUser(ref item.m_identityRemote);
        }

        public void Poll()
        {
            if (disposed || !ready) return;
            if (SteamMatchmaking.GetLobbyOwner(lobby) != owner)
            {
                inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "host_changed" });
                Dispose();
                return;
            }
            var members = Members();
            foreach (ulong id in blocked.Keys.Where(id => !members.Contains(id) || !IsBlocked(id)).ToArray()) blocked.Remove(id);
            foreach (ulong id in members)
                if (!IsBlocked(id) && peers.Add(id))
                    inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Peer = id.ToString() });
            foreach (ulong id in peers.Where(id => !members.Contains(id)).ToArray()) Disconnect(id.ToString());

            ReceiveChannel(Channel, false, incoming.Length);
            ReceiveChannel(RealtimeChannel, true, 16);
        }

        private void ReceiveChannel(int channel, bool realtime, int maximum)
        {
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, incoming, maximum);
            for (int i = 0; i < count; i++)
            {
                try
                {
                    var message = SteamNetworkingMessage_t.FromIntPtr(incoming[i]);
                    ulong id = message.m_identityPeer.GetSteamID64();
                    if (!peers.Contains(id) || IsBlocked(id)) continue;
                    if (message.m_cbSize <= 0 || message.m_cbSize > Protocol.MaxPacketBytes || inbox.Count >= (realtime ? 192 : 256))
                    { if (!realtime) Disconnect(id.ToString()); continue; }
                    var bytes = new byte[message.m_cbSize];
                    Marshal.Copy(message.m_pData, bytes, 0, bytes.Length);
                    inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Message, Peer = id.ToString(), Data = bytes, Realtime = realtime });
                }
                finally { SteamNetworkingMessage_t.Release(incoming[i]); incoming[i] = IntPtr.Zero; }
            }
        }

        public bool Send(string peer, byte[] data)
        { return SendPacket(peer, data, Channel, Steamworks.Constants.k_nSteamNetworkingSend_Reliable, true); }

        public bool SendRealtime(string peer, byte[] data)
        {
            try
            {
                return SendPacket(peer, data, RealtimeChannel,
                    Steamworks.Constants.k_nSteamNetworkingSend_Unreliable | Steamworks.Constants.k_nSteamNetworkingSend_NoNagle, false);
            }
            catch (Exception) { return false; }
        }

        private bool SendPacket(string peer, byte[] data, int channel, int flags, bool reportFailure)
        {
            ulong id;
            if (disposed || data == null || data.Length < 1 || data.Length > Protocol.MaxPacketBytes ||
                !ulong.TryParse(peer, out id) || !peers.Contains(id) || IsBlocked(id)) return false;
            if (!SteamRoomCleanup.ClientAvailable) return false;
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(id);
            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var result = SteamNetworkingMessages.SendMessageToUser(ref identity, pin.AddrOfPinnedObject(),
                    (uint)data.Length, flags, channel);
                if (result == EResult.k_EResultOK) return true;
                if (reportFailure) inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "steam_send_failed" });
                return false;
            }
            finally { pin.Free(); }
        }

        public void Disconnect(string peer)
        {
            ulong id;
            if (!ulong.TryParse(peer, out id) || !peers.Remove(id)) return;
            blocked[id] = clock.Elapsed.TotalSeconds + 2;
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(id);
            try
            {
                if (SteamRoomCleanup.ClientAvailable)
                {
                    try { SteamNetworkingMessages.CloseChannelWithUser(ref identity, Channel); }
                    finally { SteamNetworkingMessages.CloseChannelWithUser(ref identity, RealtimeChannel); }
                }
            }
            finally { inbox.Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = peer }); }
        }

        public bool TryReceive(out TransportEvent item)
        {
            item = inbox.Count > 0 ? inbox.Dequeue() : null;
            return item != null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ready = false;
            var cleanup = new RoomCleanup();
            bool available = false;
            cleanup.Run("steam_state", () => available = SteamRoomCleanup.ClientAvailable);
            ulong[] closing = peers.ToArray(); peers.Clear(); blocked.Clear(); inbox.Clear();
            foreach (ulong id in closing)
            {
                if (!available) { cleanup.Note("channel_close", "sdk_inactive"); continue; }
                cleanup.Run("channel_close", () => {
                    var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(id);
                    if (!SteamNetworkingMessages.CloseChannelWithUser(ref identity, Channel)) cleanup.Note("channel_close", "not_closed");
                });
                cleanup.Run("realtime_channel_close", () => {
                    var identity = new SteamNetworkingIdentity(); identity.SetSteamID64(id);
                    if (!SteamNetworkingMessages.CloseChannelWithUser(ref identity, RealtimeChannel)) cleanup.Note("realtime_channel_close", "not_closed");
                });
            }
            cleanup.Dispose("session_callback", request);
            if (!joinPending || !available) { cleanup.Dispose("join_callback", reentered); joinPending = false; }
            else cleanup.Note("join_result", "cleanup_pending");
            CleanupStatus = cleanup.Status;
            cleanup.ThrowIfFailed();
        }
    }
}
