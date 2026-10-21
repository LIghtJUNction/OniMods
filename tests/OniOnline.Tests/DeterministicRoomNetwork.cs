using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;

// Deterministic protocol-boundary faults. Raw frame loss/reordering is deliberately
// harsher than TCP: TCP retransmits and orders bytes before IRoomTransport sees them.
internal sealed class DeterministicRoomNetwork : IDisposable
{
    internal sealed class Faults
    {
        internal double Delay;
        internal double Jitter;
        internal double Drop;
        internal double Duplicate;
        internal double Reorder;
        internal bool Blackhole;
        internal bool ReliableOrdered;
        internal double RetransmitDelay = 0.4;
        internal Func<bool, byte[], bool> CutBeforeDelivery;
        internal Func<bool, byte[], bool> DropBeforeDelivery;
    }

    private sealed class Delivery
    {
        internal double At;
        internal long Order;
        internal Endpoint Target;
        internal int TargetEpoch;
        internal TransportEvent Event;
    }

    internal sealed class Endpoint : IRoomTransport, IAuthenticatedRoomTransport
    {
        private readonly DeterministicRoomNetwork owner;
        internal readonly Queue<TransportEvent> Inbox = new Queue<TransportEvent>();
        internal readonly string Key;
        internal readonly Dictionary<string, Endpoint> Peers = new Dictionary<string, Endpoint>();
        internal int Epoch;
        internal bool Disposed;
        internal int PeakInbox;
        internal string AccountIdentity;
        public bool IsHost { get; private set; }
        public string HostPeer { get { return "host"; } }

        public string GetAuthenticatedIdentity(string peer)
        {
            Endpoint remote;
            return Peers.TryGetValue(peer, out remote) ? remote.AccountIdentity : null;
        }

        internal Endpoint(DeterministicRoomNetwork owner, string key, bool host)
        { this.owner = owner; Key = key; IsHost = host; }

        public bool TryReceive(out TransportEvent item)
        {
            if (Inbox.Count == 0) { item = null; return false; }
            item = Inbox.Dequeue();
            return true;
        }

        public bool Send(string peer, byte[] data)
        {
            Endpoint remote;
            if (Disposed || data == null || data.Length == 0 || data.Length > Protocol.MaxPacketBytes ||
                !Peers.TryGetValue(peer, out remote) || remote.Disposed) return false;
            return owner.Send(this, remote, data);
        }

        public void Disconnect(string peer)
        {
            Endpoint remote;
            if (Peers.TryGetValue(peer, out remote)) owner.Cut(this, remote);
        }

        public void Poll() { owner.Deliver(); }
        public void Dispose()
        {
            if (Disposed) return;
            foreach (var remote in Peers.Values.ToArray()) owner.Cut(this, remote);
            Disposed = true;
            Epoch++;
            Inbox.Clear();
        }
    }

    private readonly Random random;
    private readonly List<Delivery> deliveries = new List<Delivery>();
    private readonly List<Endpoint> endpoints = new List<Endpoint>();
    private readonly Dictionary<string, double> orderedAt = new Dictionary<string, double>();
    private long order;
    private int nextClient;
    internal readonly Faults Settings = new Faults();
    internal readonly Endpoint Host;
    internal double Now { get; private set; }
    internal int Dropped { get; private set; }
    internal int Duplicated { get; private set; }
    internal int Reordered { get; private set; }
    internal int Retransmissions { get; private set; }
    internal int PeakScheduled { get; private set; }
    internal int Refused { get; private set; }
    internal int Scheduled { get { return deliveries.Count; } }
    internal const int QueueLimit = 256;

    internal DeterministicRoomNetwork(int seed)
    {
        random = new Random(seed);
        Host = new Endpoint(this, "host", true);
        endpoints.Add(Host);
    }

    internal IRoomTransport Connect()
    {
        var client = new Endpoint(this, "client-" + (++nextClient), false);
        endpoints.Add(client);
        if (Host.Disposed)
        {
            Enqueue(client, new TransportEvent { Kind = TransportEventKind.Error, Error = "connect_failed" });
            return client;
        }
        client.Peers.Add("host", Host);
        Host.Peers.Add(client.Key, client);
        Enqueue(client, new TransportEvent { Kind = TransportEventKind.Connected, Peer = "host" });
        Enqueue(Host, new TransportEvent { Kind = TransportEventKind.Connected, Peer = client.Key });
        return client;
    }

    internal void Advance(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        Now += seconds;
        Deliver();
    }

    internal void CutAll()
    {
        foreach (var remote in Host.Peers.Values.ToArray()) Cut(Host, remote);
    }

    // Capture/inject only through the actual transport boundary, without executing
    // a second copy of the session protocol in the fixture.
    internal void InjectHostFrame(Endpoint target, byte[] bytes, double delay)
    {
        Schedule(target, new TransportEvent { Kind = TransportEventKind.Message, Peer = "host", Data = (byte[])bytes.Clone() }, Now + delay);
    }

    private void Cut(Endpoint left, Endpoint right)
    {
        string leftRemote = left.IsHost ? right.Key : "host";
        string rightRemote = right.IsHost ? left.Key : "host";
        if (!left.Peers.Remove(leftRemote)) return;
        right.Peers.Remove(rightRemote);
        Enqueue(left, new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = leftRemote });
        Enqueue(right, new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = rightRemote });
    }

    private bool Send(Endpoint source, Endpoint target, byte[] bytes)
    {
        if (Settings.CutBeforeDelivery != null && Settings.CutBeforeDelivery(source.IsHost, bytes))
        { Cut(source, target); return false; }
        if (Settings.DropBeforeDelivery != null && Settings.DropBeforeDelivery(source.IsHost, bytes))
        { Dropped++; return true; }
        if (Settings.Blackhole) { Dropped++; return true; }
        double arrival = Now + Settings.Delay + (random.NextDouble() * 2 - 1) * Settings.Jitter;
        arrival = Math.Max(Now, arrival);
        bool lost = random.NextDouble() < Settings.Drop;
        bool duplicate = random.NextDouble() < Settings.Duplicate;
        bool reordered = random.NextDouble() < Settings.Reorder;
        if (lost)
        {
            Dropped++;
            if (!Settings.ReliableOrdered) return true;
            arrival += Settings.RetransmitDelay;
            Retransmissions++;
        }
        if (reordered) { Reordered++; arrival += Math.Max(0.2, Settings.Delay * 2); }
        if (Settings.ReliableOrdered)
        {
            string key = source.Key + "->" + target.Key;
            double previous;
            orderedAt.TryGetValue(key, out previous);
            arrival = Math.Max(arrival, previous + 0.000001);
            orderedAt[key] = arrival;
        }
        var message = new TransportEvent { Kind = TransportEventKind.Message, Peer = target.IsHost ? source.Key : "host", Data = (byte[])bytes.Clone() };
        if (!Schedule(target, message, arrival)) return false;
        if (duplicate)
        {
            Duplicated++;
            if (!Settings.ReliableOrdered)
                Schedule(target, new TransportEvent { Kind = message.Kind, Peer = message.Peer, Data = (byte[])bytes.Clone() }, arrival + 0.03);
        }
        return true;
    }

    private bool Schedule(Endpoint target, TransportEvent message, double at)
    {
        if (deliveries.Count >= QueueLimit) { Refused++; return false; }
        deliveries.Add(new Delivery { At = at, Order = ++order, Target = target, TargetEpoch = target.Epoch, Event = message });
        PeakScheduled = Math.Max(PeakScheduled, deliveries.Count);
        return true;
    }

    private static void Enqueue(Endpoint target, TransportEvent item)
    {
        if (target.Disposed || target.Inbox.Count >= QueueLimit) return;
        target.Inbox.Enqueue(item);
        target.PeakInbox = Math.Max(target.PeakInbox, target.Inbox.Count);
    }

    private void Deliver()
    {
        foreach (var delivery in deliveries.Where(d => d.At <= Now).OrderBy(d => d.At).ThenBy(d => d.Order).ToArray())
        {
            deliveries.Remove(delivery);
            if (delivery.Target.Epoch == delivery.TargetEpoch)
                Enqueue(delivery.Target, delivery.Event);
        }
    }

    public void Dispose()
    {
        foreach (var endpoint in endpoints) endpoint.Dispose();
        deliveries.Clear();
    }
}
