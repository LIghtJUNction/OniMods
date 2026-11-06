using System;
using System.Collections.Generic;
using OniOnline.Network;
using Steamworks;

namespace OniOnline.Runtime
{
    internal sealed class SteamRoomDirectory : IDisposable, IRoomCleanupStatus
    {
        internal const int SteamLobbyCapacity = 250; // Steamworks platform maximum, not an OniOnline gameplay limit.
        private readonly CallResult<LobbyCreated_t> created;
        private readonly CallResult<LobbyEnter_t> entered;
        private readonly CallResult<LobbyMatchList_t> listed;
        private CSteamID lobby;
        private string roomName;
        private bool hasPassword;
        private bool disposed;
        public string CleanupStatus { get; private set; }
        private string publishedStatus;
        internal bool Busy { get; private set; }
        internal bool Searching { get; private set; }
        internal event Action<IRoomTransport, string> Ready;
        internal event Action<string> Failed;
        internal event Action<string> SearchFailed;
        internal event Action<RoomListing[]> Found;

        internal SteamRoomDirectory()
        {
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            listed = CallResult<LobbyMatchList_t>.Create(OnListed);
        }

        internal void Host(string name, bool password)
        {
            if (Busy || lobby.m_SteamID != 0) throw new InvalidOperationException("already_connected");
            roomName = name;
            hasPassword = password;
            Busy = true;
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, SteamLobbyCapacity));
        }

        internal void Join(ulong id)
        {
            if (Busy || lobby.m_SteamID != 0) throw new InvalidOperationException("already_connected");
            Busy = true;
            entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
        }

        internal void Search()
        {
            if (disposed || Searching) return;
            Searching = true;
            SteamMatchmaking.AddRequestLobbyListStringFilter("oni_online", "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("oni_protocol", Protocol.Version.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(32);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        private void OnCreated(LobbyCreated_t result, bool failure)
        {
            Busy = false;
            if (disposed)
            {
                FinishCanceledLobby(!failure && result.m_eResult == EResult.k_EResultOK, result.m_ulSteamIDLobby);
                return;
            }
            if (failure || result.m_eResult != EResult.k_EResultOK) { Fail("steam_lobby_failed"); return; }
            lobby = new CSteamID(result.m_ulSteamIDLobby);
            bool ok = SteamMatchmaking.SetLobbyData(lobby, "oni_online", "1") &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_protocol", Protocol.Version.ToString()) &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_name", roomName) &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_password", hasPassword ? "1" : "0") &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_started", "0") &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_players", "1") &&
                SteamMatchmaking.SetLobbyJoinable(lobby, true);
            if (!ok) { SteamMatchmaking.LeaveLobby(lobby); lobby = default(CSteamID); Fail("steam_lobby_failed"); return; }
            PublishReady();
        }

        private void OnEntered(LobbyEnter_t result, bool failure)
        {
            Busy = false;
            if (disposed)
            {
                FinishCanceledLobby(!failure && result.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess, result.m_ulSteamIDLobby);
                return;
            }
            if (failure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            { Fail("steam_lobby_failed"); return; }
            lobby = new CSteamID(result.m_ulSteamIDLobby);
            if (SteamMatchmaking.GetLobbyData(lobby, "oni_online") != "1" ||
                SteamMatchmaking.GetLobbyData(lobby, "oni_protocol") != Protocol.Version.ToString())
            { SteamMatchmaking.LeaveLobby(lobby); lobby = default(CSteamID); Fail("protocol_mismatch"); return; }
            PublishReady();
        }

        private void PublishReady()
        {
            try
            {
                var transport = new SteamRoomTransport(lobby);
                if (Ready != null) Ready(transport, "oni://steam/" + lobby.m_SteamID);
                else transport.Dispose();
            }
            catch (Exception)
            {
                SteamMatchmaking.LeaveLobby(lobby); lobby = default(CSteamID);
                Fail("steam_unavailable");
            }
        }

        private void OnListed(LobbyMatchList_t result, bool failure)
        {
            Searching = false;
            if (disposed) return;
            if (failure)
            {
                if (SearchFailed != null) SearchFailed("steam_lobby_failed");
                return;
            }
            var rooms = new List<RoomListing>();
            for (int i = 0; i < Math.Min(result.m_nLobbiesMatching, 32); i++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(i);
                string name = SteamMatchmaking.GetLobbyData(id, "oni_name");
                if (!Protocol.ValidText(name, 64)) continue;
                int players;
                if (!int.TryParse(SteamMatchmaking.GetLobbyData(id, "oni_players"), out players) || players < 1)
                    players = SteamMatchmaking.GetNumLobbyMembers(id);
                rooms.Add(new RoomListing { Name = name, Players = players,
                    Started = SteamMatchmaking.GetLobbyData(id, "oni_started") == "1",
                    HasPassword = SteamMatchmaking.GetLobbyData(id, "oni_password") == "1", Code = "oni://steam/" + id.m_SteamID,
                    CanJoin = SteamMatchmaking.GetNumLobbyMembers(id) < SteamLobbyCapacity });
            }
            if (Found != null) Found(rooms.ToArray());
        }

        // Starting the game updates its listing; it must not close the Steam lobby.
        internal void Advertise(RoomSession session)
        {
            if (disposed || lobby.m_SteamID == 0 || session == null || !session.IsHost) return;
            string status = session.Players.Length + ":" + session.Started;
            if (status == publishedStatus) return;
            if (SteamMatchmaking.SetLobbyData(lobby, "oni_started", session.Started ? "1" : "0") &&
                SteamMatchmaking.SetLobbyData(lobby, "oni_players", session.Players.Length.ToString())) publishedStatus = status;
        }

        private void Fail(string reason) { if (Failed != null) Failed(reason); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; Searching = false;
            var cleanup = new RoomCleanup();
            bool available = false;
            cleanup.Run("steam_state", () => available = SteamRoomCleanup.ClientAvailable);
            var oldLobby = lobby; lobby = default(CSteamID);
            if (oldLobby.m_SteamID != 0)
            {
                if (available) cleanup.Run("lobby_leave", () => SteamMatchmaking.LeaveLobby(oldLobby));
                else cleanup.Note("lobby_leave", "sdk_inactive");
            }
            // While Steam is alive, retain only a pending create/join callback so a late
            // success can leave that lobby. Shutdown unregisters it; never reinitialize.
            if (!Busy || !available) { ReleasePendingCallbacks(cleanup); Busy = false; }
            else cleanup.Note("lobby_result", "cleanup_pending");
            cleanup.Dispose("list_callback", listed);
            CleanupStatus = cleanup.Status;
            cleanup.ThrowIfFailed();
        }

        private void FinishCanceledLobby(bool success, ulong id)
        {
            var cleanup = new RoomCleanup();
            bool available = false;
            cleanup.Run("steam_state", () => available = SteamRoomCleanup.ClientAvailable);
            if (success)
            {
                if (available) cleanup.Run("late_lobby_leave", () => SteamMatchmaking.LeaveLobby(new CSteamID(id)));
                else cleanup.Note("late_lobby_leave", "sdk_inactive");
            }
            ReleasePendingCallbacks(cleanup);
            cleanup.Dispose("list_callback", listed);
            CleanupStatus = cleanup.Status;
            cleanup.ThrowIfFailed();
        }
        private void ReleasePendingCallbacks(RoomCleanup cleanup)
        { cleanup.Dispose("create_callback", created); cleanup.Dispose("enter_callback", entered); }
    }
}
