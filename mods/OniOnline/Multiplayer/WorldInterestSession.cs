using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    internal sealed class WorldInterestSession : IDisposable
    {
        internal const int ObserverMemoryBudget = 16 * 1024 * 1024, OutboxByteBudget = 512 * 1024;
        internal const double MotionIntervalSeconds = 1, VisualIntervalSeconds = 2;
        private readonly RoomSession session;
        private readonly Func<string, string> authorityForPlanet;
        private readonly Func<string, PlanetManifest> manifestForPlanet;
        private readonly Func<string, string, WorldInterestSubscription, WorldInterestFrame> capture;
        private readonly Func<string, string, bool> canObserve;
        private readonly Dictionary<string, Observer> observers = new Dictionary<string, Observer>();
        private readonly Dictionary<string, RequestStamp> requestHighwater = new Dictionary<string, RequestStamp>();
        private readonly Dictionary<string, Authority> authorities = new Dictionary<string, Authority>();
        private readonly Dictionary<string, Authority> hostAuthorities = new Dictionary<string, Authority>();
        private readonly Dictionary<string, Authority> receivedAuthorities = new Dictionary<string, Authority>();
        private readonly Dictionary<string, Outgoing> outbox = new Dictionary<string, Outgoing>();
        private string owner, planet, nonce, boundEpoch;
        private WorldInterestSubscription subscription;
        private long requestNumber, boundGeneration, boundBinding, nextBinding;
        private double now, renewAt;
        private int cursor, outboxBytes;
        private bool disposed, disconnected;

        private sealed class Observer
        {
            internal string Peer, Planet, Nonce, Epoch;
            internal long Request, SourceGeneration, Binding;
            internal WorldInterestSubscription Subscription;
            internal WorldInterestPublisher Visual, Motion;
            internal double Expires, BindAt, VisualAt, MotionAt;
            internal double VisualRetryAt, MotionRetryAt;
            internal long VisualSentSequence, MotionSentSequence;
            internal bool Bound;
            internal int Memory;
        }
        private sealed class Authority { internal string Owner, Epoch; internal long SourceGeneration, Generation; }
        private sealed class RequestStamp { internal long Number; internal string Nonce, Planet; internal bool Cancelled; }
        private sealed class Outgoing { internal string Sender, Target; internal byte[] Data; internal int Priority; internal long Order; internal double QueuedAt; }
        private long order;
        internal WorldInterestReplica VisualReplica { get; private set; }
        internal WorldInterestReplica MotionReplica { get; private set; }
        internal bool Stale { get; private set; } = true;
        internal string LastError { get; private set; }
        internal string PlanetId { get { return planet; } }
        internal string BoundEpoch { get { return boundEpoch; } }
        internal string OwnerId { get { return owner; } }
        internal int ActiveObservers { get { return observers.Count; } }
        internal int QueuedBytes { get { return outboxBytes; } }
        internal int EstimatedObserverBytes { get { return observers.Values.Sum(o => o.Memory); } }
        internal event System.Action Changed;
        internal event System.Action<string> ObserverRemoved;

        // These callbacks resolve authenticated room manifests and the local native authority.
        internal WorldInterestSession(RoomSession session, Func<string, string> authorityForPlanet,
            Func<string, PlanetManifest> manifestForPlanet,
            Func<string, string, WorldInterestSubscription, WorldInterestFrame> capture,
            Func<string, string, bool> canObserve)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.authorityForPlanet = authorityForPlanet ?? throw new ArgumentNullException("authorityForPlanet");
            this.manifestForPlanet = manifestForPlanet ?? throw new ArgumentNullException("manifestForPlanet");
            this.capture = capture ?? throw new ArgumentNullException("capture");
            this.canObserve = canObserve ?? throw new ArgumentNullException("canObserve");
            session.ApplicationMessageReceived += Receive;
        }

        internal bool Subscribe(string authorityPeer, string planetId, WorldInterestSubscription value)
        {
            PlanetManifest manifest = manifestForPlanet(planetId);
            if (disposed || manifest == null || authorityForPlanet(planetId) != authorityPeer || authorityPeer == session.LocalPlayerId ||
                !session.Players.Any(player => player.Id == authorityPeer)) return false;
            // Retain a known authority's latest viewport during reconnect; Pump sends only once it is connected.
            var valid = value.ValidatedCopy(manifest.Width, manifest.Height);
            CancelCurrent();
            owner = authorityPeer; planet = planetId; subscription = valid;
            NewRequest(); return true;
        }

        internal void Stop()
        {
            CancelCurrent(); owner = planet = nonce = boundEpoch = null; subscription = null;
            VisualReplica = MotionReplica = null; Stale = true; LastError = null; Notify();
        }

        internal void RevalidateObservers()
        {
            if (disposed) return;
            foreach (var observer in observers.Values.ToArray())
                if (!canObserve(observer.Peer, observer.Planet))
                {
                    RemoveObserver(observer.Peer);
                    Deny(observer.Peer, observer.Planet, observer.Nonce, observer.Request, "world_interest_denied");
                }
        }

        private void NewRequest()
        {
            nonce = Guid.NewGuid().ToString("N"); requestNumber++; renewAt = 0;
            boundEpoch = null; boundGeneration = boundBinding = 0; Stale = true; LastError = null;
            Notify();
        }

        internal void Pump(double seconds)
        {
            now = seconds;
            if (disposed) return;
            if (!session.Joined || session.Reconnecting || session.Leaving || session.Closed)
            {
                if (!disconnected)
                {
                    disconnected = true; Stale = true;
                    foreach (string peer in observers.Keys.ToArray()) RemoveObserver(peer);
                    outbox.Clear(); outboxBytes = 0; Notify();
                }
                return;
            }
            if (disconnected)
            {
                disconnected = false;
                foreach (var authority in authorities.Values) authority.SourceGeneration++;
                if (subscription != null) NewRequest();
            }
            foreach (var entry in observers.ToArray())
                if (entry.Value.Expires <= now || !Connected(entry.Key) || authorityForPlanet(entry.Value.Planet) != session.LocalPlayerId)
                    RemoveObserver(entry.Key);
            foreach (string peer in requestHighwater.Keys.Where(id => !session.Players.Any(p => p.Id == id)).ToArray()) requestHighwater.Remove(peer);
            if (subscription != null && now >= renewAt && Connected(owner))
            {
                Queue(owner, Current(WorldInterestSessionKind.Subscribe), 0); renewAt = now + 10;
            }
            if (subscription != null && (!Connected(owner) || authorityForPlanet(planet) != owner) && !Stale)
            { Stale = true; LastError = "world_interest_owner_offline"; Notify(); }
            var work = observers.Values.ToArray();
            if (work.Length > 0)
            {
                // At most two source callbacks per Pump, round robin across observers.
                int count = Math.Min(2, work.Length);
                for (int i = 0; i < count; i++) Produce(work[(cursor + i) % work.Length]);
                cursor = (cursor + count) % work.Length;
            }
            int sendBudget = 2;
            while (sendBudget-- > 0 && outbox.Count > 0)
            {
                var pair = sendBudget == 0 && outbox.Any(p => p.Value.Priority == 2 && now - p.Value.QueuedAt >= .5)
                    ? outbox.Where(p => p.Value.Priority == 2).OrderBy(p => p.Value.Order).First()
                    : outbox.OrderBy(p => p.Value.Priority).ThenBy(p => p.Value.Order).First();
                Outgoing item = pair.Value;
                bool sent = session.IsHost ? (item.Sender == session.LocalPlayerId ? session.SendApplicationTo(item.Target, item.Data) :
                    session.ForwardApplication(item.Sender, item.Target, item.Data)) : session.SendApplication(item.Data);
                if (!sent) break;
                outbox.Remove(pair.Key); outboxBytes -= item.Data.Length;
            }
        }

        private void Produce(Observer observer)
        {
            bool motionDue = (observer.Subscription.Channels & WorldInterestChannels.Entities) != 0 && now >= observer.MotionAt;
            bool visualDue = now >= observer.VisualAt;
            if (observer.Bound)
            {
                if (!motionDue && !visualDue) return;
            }
            else if (now < observer.BindAt) return;
            if (!canObserve(observer.Peer, observer.Planet))
            { RemoveObserver(observer.Peer); Deny(observer.Peer, observer.Planet, observer.Nonce, observer.Request, "world_interest_denied"); return; }
            var samplingSubscription = observer.Bound && !visualDue ? MotionSubscription(observer.Subscription) : observer.Subscription;
            WorldInterestFrame frame;
            try { frame = capture(observer.Peer, observer.Planet, samplingSubscription); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
            { LastError = "world_interest_source_unavailable"; DeferUnavailable(observer); return; }
            if (frame == null) { DeferUnavailable(observer); return; }
            PlanetManifest manifest = manifestForPlanet(observer.Planet);
            if (manifest == null || frame.Width != manifest.Width || frame.Height != manifest.Height || !WorldInterestPublisher.ValidEpoch(frame.Epoch))
            { DeferUnavailable(observer); return; }
            Authority authority;
            if (!authorities.TryGetValue(observer.Planet, out authority))
                authorities[observer.Planet] = authority = new Authority { Owner = session.LocalPlayerId };
            if (authority.Epoch != frame.Epoch) { authority.Epoch = frame.Epoch; authority.SourceGeneration++; }
            if (observer.Epoch != frame.Epoch || observer.Visual == null)
            {
                observer.Epoch = frame.Epoch; observer.SourceGeneration = authority.SourceGeneration;
                observer.Bound = false; observer.BindAt = 0;
                observer.Visual = new WorldInterestPublisher(frame.Width, frame.Height, VisualSubscription(observer.Subscription));
                observer.Motion = new WorldInterestPublisher(frame.Width, frame.Height, MotionSubscription(observer.Subscription), latestMotion: true);
                observer.VisualSentSequence = observer.MotionSentSequence = 0;
                observer.VisualRetryAt = observer.MotionRetryAt = 0;
                Purge(observer.Peer, observer.Nonce);
            }
            if (!observer.Bound)
            {
                if (now >= observer.BindAt)
                { Queue(observer.Peer, Message(observer, WorldInterestSessionKind.Bind), 0); observer.BindAt = now + 2; }
                return;
            }
            if (motionDue)
            {
                // The native source has already filtered explored cells and cell-level permissions.
                // Planet-wide rights were checked once above on this same owning thread.
                // Keep sampling at the motion cadence, but do not supersede a sent
                // sequence before its ACK can cross a slow connection. Otherwise
                // no ACK can ever match and unchanged entities flood reliable TCP.
                if (observer.MotionSentSequence == 0 || now >= observer.MotionRetryAt)
                {
                    var delta = observer.Motion.Build(frame, 5000, index => true);
                    if (delta != null)
                    {
                        QueueDelta(observer, delta, true);
                        observer.MotionSentSequence = delta.Sequence;
                        observer.MotionRetryAt = now + RetryDelay();
                    }
                }
                observer.MotionAt = now + MotionIntervalSeconds;
            }
            if (visualDue)
            {
                var delta = observer.Visual.Build(frame, 5000, index => true);
                if (delta != null && (delta.Sequence != observer.VisualSentSequence || now >= observer.VisualRetryAt))
                {
                    QueueDelta(observer, delta, false);
                    observer.VisualSentSequence = delta.Sequence;
                    observer.VisualRetryAt = now + RetryDelay();
                }
                observer.VisualAt = now + VisualIntervalSeconds;
            }
        }

        // ACK loss still recovers without creating a new request or starving the
        // other lane. Fast ACKs release the next sample immediately; retries use
        // two observed round trips, bounded to a 2-10 second recovery window.
        private double RetryDelay()
        { return Math.Max(2, Math.Min(10, session.SmoothedRttMilliseconds / 500)); }

        private void DeferUnavailable(Observer observer)
        {
            if (!observer.Bound) observer.BindAt = now + .5;
            else { observer.MotionAt = now + MotionIntervalSeconds; observer.VisualAt = now + VisualIntervalSeconds; }
        }

        private void QueueDelta(Observer observer, WorldInterestDelta delta, bool motion)
        {
            var message = Message(observer, WorldInterestSessionKind.Delta);
            message.Motion = motion; message.Sequence = delta.Sequence; message.Data = WorldInterestWire.Encode(delta, motion);
            Queue(observer.Peer, message, motion ? 1 : 2);
        }

        private void Receive(string sender, byte[] data)
        {
            if (disposed || !WorldInterestSessionWire.IsMessage(data) || !Connected(sender)) return;
            WorldInterestSessionMessage message;
            if (!WorldInterestSessionWire.TryDecode(data, out message) || message.RoomId != session.RoomId || !Connected(message.TargetId)) return;
            string authority = authorityForPlanet(message.PlanetId);
            if (authority == null) return;
            bool fromAuthority = message.Kind == WorldInterestSessionKind.Bind || message.Kind == WorldInterestSessionKind.Delta || message.Kind == WorldInterestSessionKind.Denied;
            if (fromAuthority ? sender != authority : message.TargetId != authority) return;
            if (session.IsHost && (message.Kind == WorldInterestSessionKind.Bind || message.Kind == WorldInterestSessionKind.Delta))
                if (!Stamp(sender, message)) return;
            if (message.TargetId != session.LocalPlayerId)
            {
                if (session.IsHost) QueueRaw(sender, message.TargetId, message, Priority(message));
                return;
            }
            switch (message.Kind)
            {
                case WorldInterestSessionKind.Subscribe: AcceptSubscription(sender, message); break;
                case WorldInterestSessionKind.Cancel:
                    AcceptCancel(sender, message);
                    break;
                case WorldInterestSessionKind.Ack: AcceptAck(sender, message); break;
                case WorldInterestSessionKind.Bind: AcceptBind(sender, message); break;
                case WorldInterestSessionKind.Delta: AcceptDelta(sender, message); break;
                case WorldInterestSessionKind.Denied:
                    if (Matches(sender, message))
                    {
                        VisualReplica = MotionReplica = null; boundEpoch = null; boundGeneration = boundBinding = 0;
                        Stale = true; LastError = message.Reason; Notify();
                    }
                    break;
            }
        }

        private bool Stamp(string sender, WorldInterestSessionMessage message)
        {
            if (!WorldInterestPublisher.ValidEpoch(message.Epoch) || message.SourceGeneration < 1) return false;
            Authority authority;
            if (!hostAuthorities.TryGetValue(message.PlanetId, out authority))
                hostAuthorities[message.PlanetId] = authority = new Authority();
            if (authority.Owner != sender)
            { authority.Owner = sender; authority.Epoch = null; authority.SourceGeneration = 0; }
            if (message.SourceGeneration < authority.SourceGeneration ||
                (message.SourceGeneration == authority.SourceGeneration && authority.Epoch != message.Epoch)) return false;
            if (message.SourceGeneration > authority.SourceGeneration)
            {
                if (message.Kind != WorldInterestSessionKind.Bind) return false;
                authority.SourceGeneration = message.SourceGeneration; authority.Epoch = message.Epoch; authority.Generation++;
            }
            message.Generation = authority.Generation; return message.Generation > 0;
        }

        private void AcceptSubscription(string sender, WorldInterestSessionMessage message)
        {
            PlanetManifest manifest = manifestForPlanet(message.PlanetId);
            if (manifest == null || !canObserve(sender, message.PlanetId))
            { Deny(sender, message.PlanetId, message.Nonce, message.RequestNumber, "world_interest_denied"); return; }
            WorldInterestSubscription valid;
            try { valid = message.Subscription.ValidatedCopy(manifest.Width, manifest.Height); }
            catch (ArgumentException) { return; }
            RequestStamp highwater; requestHighwater.TryGetValue(sender, out highwater);
            Observer current;
            observers.TryGetValue(sender, out current);
            if (highwater != null && (message.RequestNumber < highwater.Number || (message.RequestNumber == highwater.Number &&
                (highwater.Nonce != message.Nonce || highwater.Planet != message.PlanetId)))) return;
            if (highwater != null && highwater.Number == message.RequestNumber && highwater.Cancelled) return;
            if (current != null && current.Nonce == message.Nonce && current.Request == message.RequestNumber)
            { current.Expires = now + 25; return; }
            int memory = 131072 + valid.Region.Width * valid.Region.Height * 128;
            if (EstimatedObserverBytes - (current == null ? 0 : current.Memory) + memory > ObserverMemoryBudget)
            { Deny(sender, message.PlanetId, message.Nonce, message.RequestNumber, "world_interest_memory_budget"); return; }
            RemoveObserver(sender); requestHighwater[sender] = new RequestStamp { Number = message.RequestNumber, Nonce = message.Nonce, Planet = message.PlanetId };
            observers[sender] = new Observer { Peer = sender, Planet = message.PlanetId, Nonce = message.Nonce,
                Request = message.RequestNumber, Binding = ++nextBinding, Subscription = valid, Expires = now + 25, Memory = memory };
        }

        private void AcceptCancel(string sender, WorldInterestSessionMessage message)
        {
            RequestStamp stamp;
            requestHighwater.TryGetValue(sender, out stamp);
            if (stamp == null || message.RequestNumber > stamp.Number)
                requestHighwater[sender] = stamp = new RequestStamp { Number = message.RequestNumber,
                    Nonce = message.Nonce, Planet = message.PlanetId };
            if (message.RequestNumber != stamp.Number || message.Nonce != stamp.Nonce || message.PlanetId != stamp.Planet) return;
            stamp.Cancelled = true;
            // A future request may be cancelled before its Subscribe arrives. It supersedes an older observer too.
            Observer current;
            if (observers.TryGetValue(sender, out current) && current.Request <= message.RequestNumber) RemoveObserver(sender);
        }

        private void AcceptAck(string sender, WorldInterestSessionMessage message)
        {
            Observer observer;
            if (!observers.TryGetValue(sender, out observer) || observer.Nonce != message.Nonce || observer.Request != message.RequestNumber ||
                observer.Planet != message.PlanetId || observer.Epoch != message.Epoch || observer.SourceGeneration != message.SourceGeneration ||
                observer.Binding != message.BindingNumber) return;
            if (message.Sequence == 0) { observer.Bound = true; observer.BindAt = double.MaxValue; return; }
            bool accepted = (message.Motion ? observer.Motion : observer.Visual)?.Acknowledge(
                message.Epoch, observer.Subscription.Revision, message.Sequence) == true;
            if (accepted && message.Motion) observer.MotionSentSequence = 0;
        }

        private void AcceptBind(string sender, WorldInterestSessionMessage message)
        {
            if (!Matches(sender, message) || message.Generation < 1 || message.BindingNumber < 1 || !WorldInterestPublisher.ValidEpoch(message.Epoch)) return;
            string key = sender + ":" + planet;
            Authority previous;
            if (receivedAuthorities.TryGetValue(key, out previous) && (message.Generation < previous.Generation ||
                (message.Generation == previous.Generation && message.Epoch != previous.Epoch))) return;
            if (boundEpoch == message.Epoch && boundGeneration == message.Generation)
            {
                if (message.BindingNumber < boundBinding) return;
                if (message.BindingNumber == boundBinding) { Ack(message, 0, false); return; }
            }
            PlanetManifest manifest = manifestForPlanet(planet); if (manifest == null) return;
            receivedAuthorities[key] = new Authority { Epoch = message.Epoch, Generation = message.Generation };
            VisualReplica = new WorldInterestReplica(manifest.Width, manifest.Height);
            MotionReplica = new WorldInterestReplica(manifest.Width, manifest.Height, latestMotion: true);
            VisualReplica.Bind(message.Epoch, VisualSubscription(subscription)); MotionReplica.Bind(message.Epoch, MotionSubscription(subscription));
            boundEpoch = message.Epoch; boundGeneration = message.Generation; boundBinding = message.BindingNumber; Stale = true; LastError = null;
            Ack(message, 0, false); Notify();
        }

        private void AcceptDelta(string sender, WorldInterestSessionMessage message)
        {
            if (!Matches(sender, message) || boundEpoch != message.Epoch || boundGeneration != message.Generation ||
                boundBinding != message.BindingNumber || VisualReplica == null) return;
            WorldInterestDelta delta; bool motion;
            if (!WorldInterestWire.TryDecode(message.Data, out delta, out motion) || motion != message.Motion ||
                delta.Sequence != message.Sequence || delta.Epoch != boundEpoch || delta.SubscriptionRevision != subscription.Revision) return;
            var replica = motion ? MotionReplica : VisualReplica;
            bool applied = replica.TryApply(delta);
            // ACK a duplicate only when this exact sequence was already accepted; never acknowledge a gap.
            if (applied || replica.Sequence == delta.Sequence) Ack(message, delta.Sequence, motion);
            if (applied) { Stale = VisualReplica.Sequence == 0; LastError = null; Notify(); }
        }

        private void Ack(WorldInterestSessionMessage source, long sequence, bool motion)
        {
            var message = Current(WorldInterestSessionKind.Ack); message.Epoch = source.Epoch;
            message.Generation = source.Generation; message.SourceGeneration = source.SourceGeneration; message.Sequence = sequence; message.Motion = motion;
            message.BindingNumber = source.BindingNumber;
            Queue(owner, message, 0);
        }

        private bool Matches(string sender, WorldInterestSessionMessage message)
        { return subscription != null && sender == owner && message.PlanetId == planet && message.Nonce == nonce && message.RequestNumber == requestNumber; }
        private void CancelCurrent()
        {
            if (nonce != null) Purge(null, nonce);
            if (subscription != null && Connected(owner)) Queue(owner, Current(WorldInterestSessionKind.Cancel), 0);
        }
        private WorldInterestSessionMessage Current(WorldInterestSessionKind kind)
        { return new WorldInterestSessionMessage { Kind = kind, RoomId = session.RoomId, TargetId = owner, PlanetId = planet, Nonce = nonce,
            RequestNumber = requestNumber, Subscription = subscription }; }
        private WorldInterestSessionMessage Message(Observer value, WorldInterestSessionKind kind)
        { return new WorldInterestSessionMessage { Kind = kind, RoomId = session.RoomId, TargetId = value.Peer, PlanetId = value.Planet,
            Nonce = value.Nonce, RequestNumber = value.Request, BindingNumber = value.Binding, Epoch = value.Epoch, SourceGeneration = value.SourceGeneration }; }
        private void Deny(string target, string targetPlanet, string requestNonce, long request, string reason)
        { Queue(target, new WorldInterestSessionMessage { Kind = WorldInterestSessionKind.Denied, RoomId = session.RoomId,
            TargetId = target, PlanetId = targetPlanet, Nonce = requestNonce, RequestNumber = request, Reason = reason }, 0); }

        private void Queue(string target, WorldInterestSessionMessage message, int priority)
        {
            if (!Connected(target)) return;
            if (session.IsHost && (message.Kind == WorldInterestSessionKind.Bind || message.Kind == WorldInterestSessionKind.Delta) && !Stamp(session.LocalPlayerId, message)) return;
            QueueRaw(session.LocalPlayerId, target, message, priority);
        }
        private void QueueRaw(string sender, string target, WorldInterestSessionMessage message, int priority)
        {
            byte[] bytes = WorldInterestSessionWire.Encode(message);
            string key = sender + ":" + target + ":" + message.Nonce + ":" + message.Kind + ":" + message.Motion;
            Outgoing old; outbox.TryGetValue(key, out old);
            if (outboxBytes - (old == null ? 0 : old.Data.Length) + bytes.Length > OutboxByteBudget)
            { LastError = "world_interest_backpressure"; return; }
            outboxBytes += bytes.Length - (old == null ? 0 : old.Data.Length);
            outbox[key] = new Outgoing { Sender = sender, Target = target, Data = bytes, Priority = priority,
                Order = old == null ? ++order : old.Order, QueuedAt = old == null ? now : old.QueuedAt };
        }
        private static int Priority(WorldInterestSessionMessage message)
        { return message.Kind != WorldInterestSessionKind.Delta ? 0 : message.Motion ? 1 : 2; }
        private void RemoveObserver(string peer)
        {
            Observer value;
            if (!observers.TryGetValue(peer, out value)) return;
            observers.Remove(peer); Purge(peer, value.Nonce);
            if (ObserverRemoved != null) ObserverRemoved(peer);
        }
        private void Purge(string peer, string requestNonce)
        {
            foreach (var pair in outbox.Where(p => p.Key.Contains(":" + requestNonce + ":") && (peer == null || p.Value.Target == peer)).ToArray())
            { outboxBytes -= pair.Value.Data.Length; outbox.Remove(pair.Key); }
        }
        private bool Connected(string peer) { return peer != null && session.Players.Any(p => p.Id == peer && p.Connected); }
        private static WorldInterestSubscription VisualSubscription(WorldInterestSubscription source)
        { return new WorldInterestSubscription { Revision = source.Revision, Region = source.Region, Channels = source.Channels & ~WorldInterestChannels.Entities,
            HoverIndex = source.HoverIndex, NumericIntervalTicks = source.NumericIntervalTicks }; }
        private static WorldInterestSubscription MotionSubscription(WorldInterestSubscription source)
        { return new WorldInterestSubscription { Revision = source.Revision, Region = source.Region, Channels = WorldInterestChannels.Entities,
            HoverIndex = -1, NumericIntervalTicks = source.NumericIntervalTicks }; }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        {
            if (disposed) return;
            Stop(); disposed = true; session.ApplicationMessageReceived -= Receive;
            foreach (string peer in observers.Keys.ToArray()) RemoveObserver(peer);
            outbox.Clear(); outboxBytes = 0;
        }
    }
}
