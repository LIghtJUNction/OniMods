using System;
using OniOnline.Network;
using OniOnline.Runtime;
using Steamworks;

internal static class SteamRoomDirectoryTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly SteamRoomDirectory Directory = new SteamRoomDirectory();
        internal int ReadyCount, RoomFailures, SearchFailures, FoundCount;
        internal string Code, RoomError, SearchError;
        internal Fixture()
        {
            Directory.Ready += (transport, code) => { ReadyCount++; Code = code; transport.Dispose(); };
            Directory.Failed += reason => { RoomFailures++; RoomError = reason; };
            Directory.SearchFailed += reason => { SearchFailures++; SearchError = reason; };
            Directory.Found += rooms => FoundCount++;
        }
        public void Dispose() { Directory.Dispose(); }
    }

    internal static void Run(Action<bool, string> check)
    {
        SearchFailureDuringHost(check);
        SearchFailureDuringJoin(check);
        RoomFailuresStaySeparate(check);
        SearchRetryAndCancellation(check);
        Console.WriteLine("Steam directory: production callback routing with explicit Steam API/transport/cleanup doubles; no SDK or game runtime claim.");
    }

    private static void SearchFailureDuringHost(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            fixture.Directory.Search();
            var search = SteamMatchmaking.LastSearch;
            fixture.Directory.Host("Loaded colony", false);
            var create = SteamMatchmaking.LastCreate;
            check(fixture.Directory.Searching && fixture.Directory.Busy, "a lobby search and host creation can be pending together");
            check(CallResult<LobbyMatchList_t>.Complete(search, default(LobbyMatchList_t), true), "the pending search failure callback actually executes");
            check(fixture.SearchFailures == 1 && fixture.SearchError == "steam_lobby_failed" && fixture.RoomFailures == 0,
                "search failure does not enter the room-creation failure handler");
            check(!fixture.Directory.Searching && fixture.Directory.Busy && fixture.ReadyCount == 0,
                "failed discovery leaves host creation pending");
            check(CallResult<LobbyCreated_t>.Complete(create,
                new LobbyCreated_t { m_eResult = EResult.k_EResultOK, m_ulSteamIDLobby = 5101 }, false), "the later successful creation callback actually executes");
            check(fixture.ReadyCount == 1 && fixture.Code == "oni://steam/5101" && !fixture.Directory.Busy && fixture.RoomFailures == 0,
                "host creation remains usable after its concurrent search fails");
        }
    }

    private static void SearchFailureDuringJoin(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            var lobby = new CSteamID(5102);
            SteamMatchmaking.SetLobbyData(lobby, "oni_online", "1");
            SteamMatchmaking.SetLobbyData(lobby, "oni_protocol", Protocol.Version.ToString());
            fixture.Directory.Search();
            var search = SteamMatchmaking.LastSearch;
            fixture.Directory.Join(lobby.m_SteamID);
            var join = SteamMatchmaking.LastJoin;
            check(CallResult<LobbyMatchList_t>.Complete(search, default(LobbyMatchList_t), true), "the search preceding a join completes with failure");
            check(fixture.SearchFailures == 1 && fixture.RoomFailures == 0 && fixture.Directory.Busy,
                "search failure cannot cancel a pending join");
            check(CallResult<LobbyEnter_t>.Complete(join, new LobbyEnter_t { m_ulSteamIDLobby = lobby.m_SteamID,
                m_EChatRoomEnterResponse = (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess }, false), "the later successful join callback actually executes");
            check(fixture.ReadyCount == 1 && fixture.Code == "oni://steam/5102" && !fixture.Directory.Busy,
                "joining a valid lobby still becomes ready after discovery fails");
        }
    }

    private static void RoomFailuresStaySeparate(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            fixture.Directory.Host("Rejected host", false);
            check(CallResult<LobbyCreated_t>.Complete(SteamMatchmaking.LastCreate,
                new LobbyCreated_t { m_eResult = EResult.k_EResultFail }, false), "the host rejection callback executes");
            check(fixture.RoomFailures == 1 && fixture.RoomError == "steam_lobby_failed" && fixture.SearchFailures == 0 &&
                fixture.ReadyCount == 0 && !fixture.Directory.Busy, "actual host failure still reaches room cleanup only");
            fixture.Directory.Join(5103);
            check(CallResult<LobbyEnter_t>.Complete(SteamMatchmaking.LastJoin, default(LobbyEnter_t), true), "the failed join callback executes");
            check(fixture.RoomFailures == 2 && fixture.SearchFailures == 0 && fixture.ReadyCount == 0 && !fixture.Directory.Busy,
                "actual join failure still reaches room cleanup only");
        }
    }

    private static void SearchRetryAndCancellation(Action<bool, string> check)
    {
        using (var fixture = new Fixture())
        {
            fixture.Directory.Search();
            check(CallResult<LobbyMatchList_t>.Complete(SteamMatchmaking.LastSearch, default(LobbyMatchList_t), true), "standalone failed search callback executes");
            fixture.Directory.Search();
            check(CallResult<LobbyMatchList_t>.Complete(SteamMatchmaking.LastSearch,
                new LobbyMatchList_t { m_nLobbiesMatching = 0 }, false), "retry search callback executes");
            check(fixture.SearchFailures == 1 && fixture.RoomFailures == 0 && fixture.FoundCount == 1 && !fixture.Directory.Searching,
                "failed search clears its busy flag and allows a successful retry");
            fixture.Directory.Search();
            var canceledSearch = SteamMatchmaking.LastSearch;
            fixture.Dispose();
            check(!CallResult<LobbyMatchList_t>.Complete(canceledSearch, default(LobbyMatchList_t), true) && fixture.SearchFailures == 1,
                "disposed directory unregisters its pending search callback");
        }
        using (var fixture = new Fixture())
        {
            fixture.Directory.Host("Canceled host", false);
            var canceledCreate = SteamMatchmaking.LastCreate;
            fixture.Dispose();
            check(CallResult<LobbyCreated_t>.Complete(canceledCreate,
                new LobbyCreated_t { m_eResult = EResult.k_EResultOK, m_ulSteamIDLobby = 5104 }, false), "late canceled creation callback executes for cleanup");
            check(fixture.ReadyCount == 0 && fixture.RoomFailures == 0 && fixture.SearchFailures == 0 && SteamMatchmaking.LeftLobbies.Contains(5104),
                "late creation after disposal leaves its lobby without publishing ready or failure events");
        }
    }
}
