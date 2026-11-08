using System;
using OniOnline.Network;
using OniOnline.Runtime;
using Steamworks;

internal static class SteamRoomTransportTests
{
    internal static void Run(Action<bool, string> check)
    {
        var lobby = new CSteamID(5201);
        SteamMatchmaking.Owners[lobby.m_SteamID] = new CSteamID(2);
        SteamNetworkingMessages.ClosedChannels.Clear();
        using (var transport = new SteamRoomTransport(lobby))
        {
            transport.Poll();
            SteamMatchmaking.Owners[lobby.m_SteamID] = new CSteamID(3);
            transport.Poll();
            TransportEvent item;
            check(transport.TryReceive(out item) && item.Kind == TransportEventKind.Error && item.Error == "host_changed",
                "host change survives transport cleanup and replaces stale connection events");
            check(SteamNetworkingMessages.ClosedChannels.Contains("2:27140") &&
                SteamNetworkingMessages.ClosedChannels.Contains("2:27142"),
                "host change closes both reliable and realtime channels");
            transport.Poll();
            transport.Dispose();
            check(!transport.TryReceive(out item) && !transport.Send("2", new byte[] { 1 }),
                "closed transport reports host change once and cannot send more data");
        }

        lobby = new CSteamID(5202);
        SteamMatchmaking.Owners[lobby.m_SteamID] = new CSteamID(2);
        using (var session = new RoomSession(new SteamRoomTransport(lobby),
            new SessionProfile { Name = "Guest", GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }, ""))
        {
            SteamMatchmaking.Owners[lobby.m_SteamID] = new CSteamID(3);
            session.Pump(0);
            check(session.Closed && session.LastError == "host_changed" && !session.Reconnecting,
                "room session handles host change immediately without waiting for heartbeat timeout");
        }
    }
}
