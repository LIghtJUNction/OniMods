using System;
using System.Collections.Generic;
using OniOnline.Network;

// The production directory and transport run against explicit SDK callbacks and
// lobby state; no Steam client, Unity runtime or third-party assembly runs.
namespace Steamworks
{
    internal struct CSteamID
    {
        internal ulong m_SteamID;
        internal CSteamID(ulong value) { m_SteamID = value; }
        internal bool IsValid() { return m_SteamID != 0; }
        public static bool operator ==(CSteamID left, CSteamID right) { return left.m_SteamID == right.m_SteamID; }
        public static bool operator !=(CSteamID left, CSteamID right) { return !(left == right); }
        public override bool Equals(object value) { return value is CSteamID other && this == other; }
        public override int GetHashCode() { return m_SteamID.GetHashCode(); }
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

    internal sealed class Callback<T> : IDisposable
    {
        internal static Callback<T> Create(Action<T> callback) { return new Callback<T>(); }
        public void Dispose() { }
    }

    internal struct SteamNetworkingIdentity
    {
        private ulong id;
        internal ulong GetSteamID64() { return id; }
        internal void SetSteamID64(ulong value) { id = value; }
    }
    internal struct SteamNetworkingMessagesSessionRequest_t
    { internal SteamNetworkingIdentity m_identityRemote; }
    internal struct SteamNetworkingMessage_t
    {
        internal SteamNetworkingIdentity m_identityPeer;
        internal int m_cbSize;
        internal IntPtr m_pData;
        internal SteamNetworkingMessage_t(SteamNetworkingIdentity peer, IntPtr data, int size)
        { m_identityPeer = peer; m_pData = data; m_cbSize = size; }
        internal static SteamNetworkingMessage_t FromIntPtr(IntPtr pointer) { throw new NotSupportedException(); }
        internal static void Release(IntPtr pointer) { throw new NotSupportedException(); }
    }
    internal static class Constants
    {
        internal const int k_nSteamNetworkingSend_Reliable = 8;
        internal const int k_nSteamNetworkingSend_Unreliable = 0;
        internal const int k_nSteamNetworkingSend_NoNagle = 1;
    }
    internal static class SteamUser
    { internal static CSteamID GetSteamID() { return new CSteamID(1); } }
    internal static class SteamNetworkingUtils
    { internal static void InitRelayNetworkAccess() { } }
    internal static class SteamNetworkingMessages
    {
        internal static readonly List<string> ClosedChannels = new List<string>();
        internal static bool AcceptSessionWithUser(ref SteamNetworkingIdentity identity) { return true; }
        internal static int ReceiveMessagesOnChannel(int channel, IntPtr[] messages, int maximum) { return 0; }
        internal static EResult SendMessageToUser(ref SteamNetworkingIdentity identity, IntPtr data, uint size, int flags, int channel)
        { return EResult.k_EResultOK; }
        internal static bool CloseChannelWithUser(ref SteamNetworkingIdentity identity, int channel)
        { ClosedChannels.Add(identity.GetSteamID64() + ":" + channel); return true; }
    }

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
        internal static readonly Dictionary<ulong, CSteamID> Owners = new Dictionary<ulong, CSteamID>();
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
        internal static CSteamID GetLobbyOwner(CSteamID id)
        { CSteamID owner; return Owners.TryGetValue(id.m_SteamID, out owner) ? owner : SteamUser.GetSteamID(); }
        internal static int GetNumLobbyMembers(CSteamID id) { return GetLobbyOwner(id) == SteamUser.GetSteamID() ? 1 : 2; }
        internal static CSteamID GetLobbyMemberByIndex(CSteamID id, int index)
        { return index == 0 ? SteamUser.GetSteamID() : GetLobbyOwner(id); }
    }
}

namespace OniOnline.Runtime
{
    internal static class SteamRoomCleanup { internal static bool ClientAvailable { get { return true; } } }
}
