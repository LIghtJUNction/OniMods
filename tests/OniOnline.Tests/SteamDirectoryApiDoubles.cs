using System;
using System.Collections.Generic;
using OniOnline.Network;

// Only SteamRoomDirectory is production code in this fixture. Tests deliver SDK
// callbacks explicitly; no Steam client, Unity runtime or third-party assembly runs.
namespace Steamworks
{
    internal struct CSteamID
    {
        internal ulong m_SteamID;
        internal CSteamID(ulong value) { m_SteamID = value; }
    }
    internal struct SteamAPICall_t { internal ulong Value; }
    internal enum ELobbyType { k_ELobbyTypePublic }
    internal enum ELobbyComparison { k_ELobbyComparisonEqual }
    internal enum ELobbyDistanceFilter { k_ELobbyDistanceFilterWorldwide }
    internal enum EResult { k_EResultOK, k_EResultFail }
    internal enum EChatRoomEnterResponse { k_EChatRoomEnterResponseSuccess = 1 }
    internal struct LobbyCreated_t { internal EResult m_eResult; internal ulong m_ulSteamIDLobby; }
    internal struct LobbyEnter_t { internal uint m_EChatRoomEnterResponse; internal ulong m_ulSteamIDLobby; }
    internal struct LobbyMatchList_t { internal uint m_nLobbiesMatching; }

    internal sealed class CallResult<T> : IDisposable
    {
        private static readonly Dictionary<ulong, CallResult<T>> pending = new Dictionary<ulong, CallResult<T>>();
        private readonly Action<T, bool> callback;
        private ulong call;
        private CallResult(Action<T, bool> callback) { this.callback = callback; }
        internal static CallResult<T> Create(Action<T, bool> callback) { return new CallResult<T>(callback); }
        internal void Set(SteamAPICall_t value) { Dispose(); call = value.Value; pending.Add(call, this); }
        internal static bool Complete(SteamAPICall_t value, T result, bool failure)
        {
            CallResult<T> target;
            if (!pending.TryGetValue(value.Value, out target)) return false;
            target.Dispose();
            target.callback(result, failure);
            return true;
        }
        public void Dispose() { if (call != 0) pending.Remove(call); call = 0; }
    }

    internal static class SteamMatchmaking
    {
        private static ulong nextCall;
        private static readonly Dictionary<string, string> data = new Dictionary<string, string>();
        internal static SteamAPICall_t LastCreate, LastJoin, LastSearch;
        internal static readonly List<ulong> LeftLobbies = new List<ulong>();
        private static SteamAPICall_t Call() { return new SteamAPICall_t { Value = ++nextCall }; }
        internal static SteamAPICall_t CreateLobby(ELobbyType type, int capacity) { return LastCreate = Call(); }
        internal static SteamAPICall_t JoinLobby(CSteamID id) { return LastJoin = Call(); }
        internal static SteamAPICall_t RequestLobbyList() { return LastSearch = Call(); }
        internal static bool SetLobbyData(CSteamID id, string key, string value)
        { data[id.m_SteamID + ":" + key] = value; return true; }
        internal static string GetLobbyData(CSteamID id, string key)
        { string value; return data.TryGetValue(id.m_SteamID + ":" + key, out value) ? value : ""; }
        internal static bool SetLobbyJoinable(CSteamID id, bool joinable) { return true; }
        internal static void LeaveLobby(CSteamID id) { LeftLobbies.Add(id.m_SteamID); }
        internal static void AddRequestLobbyListStringFilter(string key, string value, ELobbyComparison comparison) { }
        internal static void AddRequestLobbyListResultCountFilter(int count) { }
        internal static void AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter filter) { }
        internal static CSteamID GetLobbyByIndex(int index) { return default(CSteamID); }
        internal static int GetNumLobbyMembers(CSteamID id) { return 1; }
    }
}

namespace OniOnline.Runtime
{
    internal static class SteamRoomCleanup { internal static bool ClientAvailable { get { return true; } } }
    internal sealed class SteamRoomTransport : IRoomTransport
    {
        internal SteamRoomTransport(Steamworks.CSteamID lobby) { }
        public bool IsHost { get { return true; } }
        public string HostPeer { get { return "host"; } }
        public bool TryReceive(out TransportEvent item) { item = null; return false; }
        public bool Send(string peer, byte[] data) { return false; }
        public void Disconnect(string peer) { }
        public void Poll() { }
        public void Dispose() { }
    }
}
