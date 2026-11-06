using System;

namespace OniOnline.Network
{
    public enum TransportEventKind { Connected, Message, Disconnected, Error }

    public sealed class TransportEvent
    {
        public TransportEventKind Kind;
        public string Peer;
        public byte[] Data;
        public string Error;
        public bool Realtime;
    }

    public interface IReconnectableRoomTransport
    {
        IRoomTransport Reconnect();
    }

    // Identity comes from the authenticated transport, never from a Hello field.
    // A transport without account authentication (LAN TCP) does not implement this.
    public interface IAuthenticatedRoomTransport
    {
        string GetAuthenticatedIdentity(string peer);
    }

    // Ephemeral media may be dropped under pressure and is never replayed after
    // reconnect. Implementations keep its queue separate from reliable control.
    public interface IRealtimeRoomTransport
    {
        bool SendRealtime(string peer, byte[] data);
    }

    // Pumped by the owning thread. Transport workers never call game/UI code.
    public interface IRoomTransport : IDisposable
    {
        bool IsHost { get; }
        string HostPeer { get; }
        bool TryReceive(out TransportEvent item);
        bool Send(string peer, byte[] data);
        void Disconnect(string peer);
        void Poll();
    }
}
