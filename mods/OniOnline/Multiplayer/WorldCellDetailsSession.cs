using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    // One current hover per observer. Native cell bounds, exploration and cell-level authorization
    // are checked by capture; this channel additionally checks room identity and planet authority.
    internal sealed class WorldCellDetailsSession : IDisposable
    {
        internal const int ObserverByteBudget = 256 * 1024, OutboxByteBudget = 256 * 1024;
        private const int ObserverBytes = 1024;
        private readonly RoomSession session;
        private readonly Func<string, string> authorityForPlanet;
        private readonly Func<string, string, int, WorldCellDetailResult> capture;
        private readonly Func<string, string, bool> canObserve;
        private readonly Dictionary<string, Observer> observers = new Dictionary<string, Observer>();
        private readonly Dictionary<string, RequestStamp> highwater = new Dictionary<string, RequestStamp>();
        private readonly Dictionary<string, Outgoing> outbox = new Dictionary<string, Outgoing>();
        private string owner, planet, nonce, epoch;
        private int index = -1, cursor, queuedBytes;
        private long request, receivedSequence, order;
        private double now, renewAt;
        private bool disposed;
        private WorldCellDetailResult current;

        private sealed class RequestStamp
        {
            internal long Request, Sequence;
            internal string Nonce, Planet, Epoch;
            internal int Index;
            internal bool Cancelled;
            internal WorldCellDetailResult Value;
        }
        private sealed class Observer
        {
            internal string Peer, Planet, Nonce, Epoch;
            internal int Index;
            internal long Request, Sequence, Ack;
            internal double Expires, SampleAt, SendAt;
            internal WorldCellDetailResult Value;
        }
        private sealed class Outgoing { internal string Sender, Target, Nonce; internal byte[] Bytes; internal long Order; }
        internal event System.Action Changed;
        internal event System.Action<string> ObserverRemoved;
        internal WorldCellDetailResult Current { get { return current == null ? null : current.Copy(); } }
        internal bool Stale { get; private set; } = true;
        internal string LastError { get; private set; }
        internal int ActiveObservers { get { return observers.Count; } }
        internal int QueuedBytes { get { return queuedBytes; } }
        internal int EstimatedObserverBytes { get { return (observers.Count + highwater.Count) * ObserverBytes; } }

        internal WorldCellDetailsSession(RoomSession session, Func<string, string> authorityForPlanet,
            Func<string, string, int, WorldCellDetailResult> capture, Func<string, string, bool> canObserve)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.authorityForPlanet = authorityForPlanet ?? throw new ArgumentNullException("authorityForPlanet");
            this.capture = capture ?? throw new ArgumentNullException("capture");
            this.canObserve = canObserve ?? throw new ArgumentNullException("canObserve");
            session.ApplicationMessageReceived += Receive;
        }

        // trustedEpoch must come from an accepted primary-interest Bind, never an untrusted detail response.
        internal bool Watch(string authority, string planetId, string trustedEpoch, int cellIndex)
        {
            if (disposed || !Ready || authority == session.LocalPlayerId || !Connected(authority) ||
                authorityForPlanet(planetId) != authority || !WorldInterestPublisher.ValidEpoch(trustedEpoch) ||
                cellIndex < 0 || cellIndex >= 262144) return false;
            if (owner == authority && planet == planetId && epoch == trustedEpoch && index == cellIndex) return true;
            Cancel(); owner = authority; planet = planetId; epoch = trustedEpoch; index = cellIndex;
            nonce = Guid.NewGuid().ToString("N"); request++; receivedSequence = 0; renewAt = 0;
            current = null; Stale = true; LastError = null; Notify(); return true;
        }
        internal void Stop()
        {
            Cancel(); owner = planet = nonce = epoch = null; index = -1; current = null;
            receivedSequence = 0; Stale = true; LastError = null; Notify();
        }
        private void Cancel()
        {
            if (nonce == null) return;
            Purge(session.LocalPlayerId, owner, nonce);
            if (Ready && Connected(owner)) Queue(owner, CurrentMessage(WorldCellDetailsKind.Cancel));
        }

        internal void Pump(double seconds)
        {
            now = seconds;
            if (disposed) return;
            if (!Ready)
            {
                if (owner != null || observers.Count > 0 || outbox.Count > 0)
                {
                    // Reconnection needs a fresh trusted primary Bind before another Watch.
                    owner = planet = nonce = epoch = null; index = -1; receivedSequence = 0;
                    foreach (string peer in observers.Keys.ToArray()) Remove(peer);
                    outbox.Clear(); queuedBytes = 0;
                    Stale = true; Notify();
                }
                return;
            }
            foreach (var pair in observers.ToArray())
                if (pair.Value.Expires <= now || !Connected(pair.Key) || authorityForPlanet(pair.Value.Planet) != session.LocalPlayerId)
                    Remove(pair.Key);
            foreach (string peer in highwater.Keys.Where(p => !session.Players.Any(v => v.Id == p)).ToArray()) highwater.Remove(peer);
            if (owner != null)
            {
                if (!Connected(owner) || authorityForPlanet(planet) != owner)
                { if (!Stale) { Stale = true; LastError = "world_cell_details_owner_offline"; Notify(); } }
                else if (now >= renewAt)
                { Queue(owner, CurrentMessage(WorldCellDetailsKind.Watch)); renewAt = now + 1; }
            }
            var work = observers.Values.ToArray();
            for (int n = 0; n < Math.Min(4, work.Length); n++) Produce(work[(cursor + n) % work.Length]);
            if (work.Length > 0) cursor = (cursor + Math.Min(4, work.Length)) % work.Length;
            int sends = 4;
            while (sends-- > 0 && outbox.Count > 0)
            {
                var pair = outbox.OrderBy(p => p.Value.Order).First(); var item = pair.Value;
                bool sent = session.IsHost ? (item.Sender == session.LocalPlayerId ? session.SendApplicationTo(item.Target, item.Bytes) :
                    session.ForwardApplication(item.Sender, item.Target, item.Bytes)) : session.SendApplication(item.Bytes);
                if (!sent) break;
                outbox.Remove(pair.Key); queuedBytes -= item.Bytes.Length;
            }
        }

        private void Produce(Observer observer)
        {
            if (!canObserve(observer.Peer, observer.Planet)) { Deny(observer); Remove(observer.Peer, false); return; }
            if (now >= observer.SampleAt)
            {
                observer.SampleAt = now + .5;
                WorldCellDetailResult value;
                try { value = capture(observer.Peer, observer.Planet, observer.Index); }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
                { value = null; }
                if (!WorldCellDetailsWire.ValidResult(value, observer.Epoch, observer.Index))
                { Deny(observer); Remove(observer.Peer, false); return; }
                if (!Same(observer.Value, value))
                {
                    observer.Value = value.Copy(); observer.Sequence++; observer.SendAt = 0;
                    observer.Value.Sequence = observer.Sequence;
                    RequestStamp stamp;
                    if (highwater.TryGetValue(observer.Peer, out stamp))
                    { stamp.Sequence = observer.Sequence; stamp.Value = observer.Value.Copy(); }
                }
            }
            // ACK loss resends the same sequence, never a fresh numerical version.
            if (observer.Value != null && observer.Ack < observer.Sequence && now >= observer.SendAt)
            {
                var message = Message(observer, WorldCellDetailsKind.Value);
                message.Sequence = observer.Sequence; message.Result = observer.Value;
                Queue(observer.Peer, message); observer.SendAt = now + 1;
            }
        }

        private void Receive(string sender, byte[] data)
        {
            if (disposed || !Ready || !Connected(sender) || !WorldCellDetailsWire.IsMessage(data)) return;
            WorldCellDetailsMessage message;
            if (!WorldCellDetailsWire.TryDecode(data, out message) || message.RoomId != session.RoomId || !Connected(message.TargetId)) return;
            string authority = authorityForPlanet(message.PlanetId);
            bool response = message.Kind == WorldCellDetailsKind.Value || message.Kind == WorldCellDetailsKind.Denied;
            if (authority == null || (response ? sender != authority : message.TargetId != authority)) return;
            if (message.TargetId != session.LocalPlayerId)
            {
                if (session.IsHost) QueueRaw(sender, message.TargetId, message);
                return;
            }
            if (message.Kind == WorldCellDetailsKind.Watch) AcceptWatch(sender, message);
            else if (message.Kind == WorldCellDetailsKind.Cancel || message.Kind == WorldCellDetailsKind.Ack)
            {
                Observer observer;
                if (message.Kind == WorldCellDetailsKind.Cancel)
                {
                    RequestStamp stamp;
                    highwater.TryGetValue(sender, out stamp);
                    if (stamp == null || message.Request > stamp.Request)
                    {
                        if (stamp == null && EstimatedObserverBytes + ObserverBytes > ObserverByteBudget) return;
                        highwater[sender] = stamp = new RequestStamp { Request = message.Request, Nonce = message.Nonce,
                            Planet = message.PlanetId, Epoch = message.Epoch, Index = message.Index };
                    }
                    if (stamp.Request != message.Request || stamp.Nonce != message.Nonce ||
                        stamp.Planet != message.PlanetId || stamp.Epoch != message.Epoch || stamp.Index != message.Index) return;
                    stamp.Cancelled = true;
                    Remove(sender);
                }
                else if (observers.TryGetValue(sender, out observer) && Matches(observer, message) && message.Sequence == observer.Sequence)
                    observer.Ack = message.Sequence;
            }
            else if (MatchesCurrent(sender, message))
            {
                if (message.Kind == WorldCellDetailsKind.Denied)
                {
                    current = null; Stale = true; LastError = "world_cell_details_denied";
                    Purge(session.LocalPlayerId, owner, nonce);
                    owner = planet = nonce = epoch = null; index = -1;
                    Notify(); return;
                }
                if (message.Sequence < receivedSequence) return;
                var ack = CurrentMessage(WorldCellDetailsKind.Ack); ack.Sequence = message.Sequence; Queue(owner, ack);
                if (message.Sequence == receivedSequence) return;
                receivedSequence = message.Sequence;
                current = message.Result.Copy(); Stale = false; LastError = null; Notify();
            }
        }

        private void AcceptWatch(string sender, WorldCellDetailsMessage message)
        {
            RequestStamp stamp; highwater.TryGetValue(sender, out stamp);
            if (stamp != null && (message.Request < stamp.Request || message.Request == stamp.Request &&
                (message.Nonce != stamp.Nonce || message.PlanetId != stamp.Planet || message.Epoch != stamp.Epoch || message.Index != stamp.Index))) return;
            if (stamp != null && stamp.Cancelled && stamp.Request == message.Request) return;
            Observer observer; observers.TryGetValue(sender, out observer);
            if (observer != null && Matches(observer, message)) { observer.Expires = now + 2.5; return; }
            if (!canObserve(sender, message.PlanetId) ||
                EstimatedObserverBytes + (stamp == null ? ObserverBytes : 0) + (observer == null ? ObserverBytes : 0) > ObserverByteBudget)
            {
                var denied = CopyHeader(message, sender, WorldCellDetailsKind.Denied); Queue(sender, denied); return;
            }
            Remove(sender);
            bool resume = stamp != null && stamp.Request == message.Request;
            if (!resume) highwater[sender] = stamp = new RequestStamp { Request = message.Request, Nonce = message.Nonce,
                Planet = message.PlanetId, Epoch = message.Epoch, Index = message.Index };
            observers[sender] = new Observer { Peer = sender, Planet = message.PlanetId, Nonce = message.Nonce,
                Epoch = message.Epoch, Index = message.Index, Request = message.Request, Expires = now + 2.5,
                Sequence = resume ? stamp.Sequence : 0, Value = resume ? stamp.Value?.Copy() : null };
        }
        private void Deny(Observer observer)
        {
            Purge(session.LocalPlayerId, observer.Peer, observer.Nonce);
            Queue(observer.Peer, Message(observer, WorldCellDetailsKind.Denied));
        }
        private void Remove(string peer, bool purge = true)
        {
            Observer observer;
            bool removed = observers.TryGetValue(peer, out observer) && observers.Remove(peer);
            if (purge && removed) Purge(session.LocalPlayerId, peer, observer.Nonce);
            if (removed && ObserverRemoved != null) ObserverRemoved(peer);
        }
        private bool MatchesCurrent(string sender, WorldCellDetailsMessage message)
        { return owner != null && sender == owner && message.PlanetId == planet && message.Nonce == nonce &&
            message.Epoch == epoch && message.Index == index && message.Request == request; }
        private static bool Matches(Observer observer, WorldCellDetailsMessage message)
        { return observer.Nonce == message.Nonce && observer.Planet == message.PlanetId && observer.Epoch == message.Epoch &&
            observer.Index == message.Index && observer.Request == message.Request; }
        private static bool Same(WorldCellDetailResult a, WorldCellDetailResult b)
        { return a != null && a.Epoch == b.Epoch && a.Width == b.Width && a.Height == b.Height && WorldInterestPublisher.Same(a.Cell, b.Cell); }
        private WorldCellDetailsMessage CurrentMessage(WorldCellDetailsKind kind)
        { return new WorldCellDetailsMessage { Kind = kind, RoomId = session.RoomId, TargetId = owner, PlanetId = planet,
            Nonce = nonce, Epoch = epoch, Index = index, Request = request }; }
        private WorldCellDetailsMessage Message(Observer observer, WorldCellDetailsKind kind)
        { return new WorldCellDetailsMessage { Kind = kind, RoomId = session.RoomId, TargetId = observer.Peer, PlanetId = observer.Planet,
            Nonce = observer.Nonce, Epoch = observer.Epoch, Index = observer.Index, Request = observer.Request }; }
        private static WorldCellDetailsMessage CopyHeader(WorldCellDetailsMessage source, string target, WorldCellDetailsKind kind)
        { return new WorldCellDetailsMessage { Kind = kind, RoomId = source.RoomId, TargetId = target, PlanetId = source.PlanetId,
            Nonce = source.Nonce, Epoch = source.Epoch, Index = source.Index, Request = source.Request }; }

        private void Queue(string target, WorldCellDetailsMessage message)
        { if (Ready && Connected(target)) QueueRaw(session.LocalPlayerId, target, message); }
        private void QueueRaw(string sender, string target, WorldCellDetailsMessage message)
        {
            byte[] bytes = WorldCellDetailsWire.Encode(message);
            // A peer can have only one current cell in each direction. Queued old hovers cannot accumulate.
            string key = sender + ":" + target + ":" + message.Kind;
            Outgoing previous; outbox.TryGetValue(key, out previous);
            if (queuedBytes - (previous == null ? 0 : previous.Bytes.Length) + bytes.Length > OutboxByteBudget)
            { LastError = "world_cell_details_backpressure"; return; }
            queuedBytes += bytes.Length - (previous == null ? 0 : previous.Bytes.Length);
            outbox[key] = new Outgoing { Sender = sender, Target = target, Nonce = message.Nonce, Bytes = bytes, Order = previous == null ? ++order : previous.Order };
        }
        private void Purge(string sender, string target, string requestNonce)
        {
            foreach (var pair in outbox.Where(p => p.Value.Sender == sender && p.Value.Target == target && p.Value.Nonce == requestNonce).ToArray())
            { outbox.Remove(pair.Key); queuedBytes -= pair.Value.Bytes.Length; }
        }
        private bool Ready { get { return session.Joined && !session.Reconnecting && !session.Leaving && !session.Closed; } }
        private bool Connected(string peer) { return peer != null && session.Players.Any(p => p.Id == peer && p.Connected); }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true; session.ApplicationMessageReceived -= Receive;
            foreach (string peer in observers.Keys.ToArray()) Remove(peer);
            highwater.Clear(); outbox.Clear(); queuedBytes = 0;
        }
    }
}
