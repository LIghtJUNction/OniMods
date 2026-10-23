using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;

namespace OniOnline.Sdk
{
    public enum ModStateSubscriptionStatus { Waiting, Active, NeedsSnapshot, Denied, Unavailable, SchemaMismatch, Disconnected, Disposed }

    public sealed class ModStateSubscription : IDisposable
    {
        internal ModStateSession Owner;
        internal long Request;
        internal long LocalAdmission, HostAdmission, AuthorityAdmission;
        internal double NextRequest, LastSeen;
        internal bool Active = true, ForceSnapshot = true;
        public ModStateReplica Replica { get; }
        public ModStateBinding Binding { get { return Replica.Binding; } }
        public ModStateSubscriptionStatus Status { get; internal set; } = ModStateSubscriptionStatus.Waiting;
        public ModStateApplyResult? LastApplyResult { get; internal set; }
        public event Action Changed;
        internal ModStateSubscription(ModStateSession owner, ModStateBinding binding, IModStateReducer reducer, long request, double now)
        { Owner = owner; Replica = new ModStateReplica(binding, reducer); Request = request; LastSeen = now; }
        internal void Notify()
        { var callbacks = Changed; if (callbacks != null) foreach (Action callback in callbacks.GetInvocationList()) try { callback(); } catch (Exception) { } }
        public void Dispose() { Owner?.Cancel(this, ModStateSubscriptionStatus.Disposed); Owner = null; Changed = null; }
    }

    // Pump and every API call belong to RoomSession's caller thread. This synchronizes
    // registered byte state, never native objects or business commands.
    public sealed class ModStateSession : IDisposable
    {
        public const int MaximumStreams = 16, MaximumRemoteLeases = 64;
        public const int MaximumAssemblies = 8, MaximumAssembliesPerPeer = 2;
        public const int MaximumAssemblyBytes = 512 * 1024, MaximumAssemblyBytesPerPeer = 128 * 1024 + 2048;
        private const double LeaseSeconds = 12, RequestSeconds = 2, TransferSeconds = 10;
        private readonly RoomSession room;
        private readonly Func<string, bool> canUsePeer;
        private readonly Func<string, long> admissionGeneration;
        private readonly Dictionary<string, Registration> publishers = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModStateSubscription> subscriptions = new Dictionary<string, ModStateSubscription>(StringComparer.Ordinal);
        private readonly Dictionary<string, RequestStamp> requests = new Dictionary<string, RequestStamp>(StringComparer.Ordinal);
        private readonly Dictionary<string, Lease> leases = new Dictionary<string, Lease>(StringComparer.Ordinal);
        private readonly Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        private readonly HashSet<string> usedGenerations = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> usedPublisherEpochs = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> recentGenerations = new Queue<string>(), recentPublisherEpochs = new Queue<string>();
        private readonly Dictionary<string, Rate> rates = new Dictionary<string, Rate>(StringComparer.Ordinal);
        private readonly Dictionary<string, RelayRoute> relayRoutes = new Dictionary<string, RelayRoute>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> retiredRelayRequests = new Dictionary<string, long>(StringComparer.Ordinal);
        private sealed class Rate { internal double At; internal int Packets, Bytes; }
        private sealed class RequestStamp { internal long Number, Admission; internal string Generation; internal bool Cancelled; }
        private sealed class RelayRoute
        {
            internal string Observer, Authority; internal ModStateBinding Binding; internal long Request, LocalAdmission, ObserverAdmission, AuthorityAdmission;
            internal bool Cancelled;
        }
        private sealed class Registration : IDisposable
        {
            internal ModStateSession Owner; internal string Key; internal ModStatePublisher Publisher; internal Func<string, bool> CanObserve;
            internal long LocalAdmission, HostAdmission;
            public void Dispose() { Owner?.Unregister(this); Owner = null; }
        }
        private sealed class Lease
        {
            internal string Observer, Key; internal Registration Registration; internal ModStateBinding Binding; internal long Request, SentRevision;
            internal double Expires; internal byte[] Sending; internal string Transfer, Digest; internal int NextChunk; internal long SendingRevision;
            internal bool Recover = true; internal long KnownRevision;
            internal long LocalAdmission, HostAdmission, ObserverAdmission;
        }
        private sealed class Assembly
        {
            internal string Sender, Transfer, Digest; internal ModStateSubscription Subscription; internal int Total, Count, Received;
            internal byte[][] Chunks; internal double Expires, HardDeadline;
        }
        private bool disposed, refreshing;
        private double now, sendWindow;
        private int sentInWindow, roundRobin;
        public bool Ready { get { return !disposed && room.Joined && !room.Closed && !room.Reconnecting && !room.Leaving &&
            Generation(room.LocalPlayerId) > 0 && (room.IsHost || Generation(HostId) > 0); } }
        public int IncompleteAssemblyCount { get { return assemblies.Count; } }
        public int IncompleteAssemblyBytes { get { return assemblies.Values.Sum(a => a.Total); } }
        public long RejectedPackets { get; private set; }
        public ModStateSession(RoomSession room, Func<string, bool> canUsePeer, Func<string, long> admissionGeneration = null)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            this.canUsePeer = canUsePeer ?? throw new ArgumentNullException(nameof(canUsePeer));
            this.admissionGeneration = admissionGeneration;
            room.ApplicationMessageReceived += Receive; room.Changed += Lifecycle;
        }
        public IDisposable RegisterPublisher(ModStatePublisher publisher, Func<string, bool> canObserve)
        {
            Lifecycle();
            if (!Ready) throw new InvalidOperationException("mod_state_not_admitted");
            if (publisher == null || canObserve == null || publisher.Binding.SessionId != room.RoomId || publisher.Binding.AuthorityId != room.LocalPlayerId)
                throw new ArgumentException("invalid_mod_state_publisher");
            string key = Stream(publisher.Binding);
            if (publishers.Count >= MaximumStreams || publishers.ContainsKey(key)) throw new InvalidOperationException("mod_state_publisher_budget");
            string incarnation = key + "|" + publisher.Binding.Epoch;
            if (!Remember(usedPublisherEpochs, recentPublisherEpochs, incarnation)) throw new InvalidOperationException("mod_state_publisher_fresh_epoch_required");
            var registration = new Registration { Owner = this, Key = key, Publisher = publisher, CanObserve = canObserve,
                LocalAdmission = Generation(room.LocalPlayerId), HostAdmission = Generation(HostId) };
            publishers.Add(key, registration); return registration;
        }
        public ModStateSubscription Subscribe(ModStateBinding binding, IModStateReducer reducer)
        {
            Lifecycle();
            if (!Ready) throw new InvalidOperationException("mod_state_not_admitted");
            if (binding == null || reducer == null || binding.SessionId != room.RoomId || !Connected(binding.AuthorityId)) throw new ArgumentException("invalid_mod_state_subscription");
            string key = SubscriptionKey(binding);
            if (subscriptions.Count >= MaximumStreams || subscriptions.ContainsKey(key) || !Remember(usedGenerations, recentGenerations, binding.Generation))
                throw new InvalidOperationException("mod_state_subscription_budget_or_reused_generation");
            var subscription = new ModStateSubscription(this, binding, reducer, room.NextWorkAuthorityGeneration(), now);
            subscription.LocalAdmission = Generation(room.LocalPlayerId); subscription.HostAdmission = Generation(HostId);
            subscription.AuthorityAdmission = Generation(binding.AuthorityId);
            subscriptions.Add(key, subscription); return subscription;
        }
        public ModStateSubscription Subscribe(string modId, string channel, string authorityId, string epoch, int schema, IModStateReducer reducer)
        { return Subscribe(new ModStateBinding(room.RoomId, modId, channel, authorityId, epoch, Guid.NewGuid().ToString("N"), schema), reducer); }
        public void Pump(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now) throw new ArgumentOutOfRangeException(nameof(seconds));
            now = seconds; if (disposed) return; Lifecycle();
            foreach (var pair in assemblies.Where(p => p.Value.Expires <= now || p.Value.HardDeadline <= now).ToArray())
            { pair.Value.Subscription.ForceSnapshot = true; pair.Value.Subscription.NextRequest = now; assemblies.Remove(pair.Key); }
            if (!Ready) return;
            foreach (var subscription in subscriptions.Values.ToArray())
            {
                if (!subscription.Active) continue;
                if (now - subscription.LastSeen >= LeaseSeconds) { Cancel(subscription, ModStateSubscriptionStatus.Disconnected); continue; }
                if (subscription.NextRequest > now) continue;
                var message = new ModStateSessionMessage { Kind = ModStateSessionKind.Subscribe, Target = subscription.Binding.AuthorityId,
                    Binding = subscription.Binding, Request = subscription.Request, Revision = subscription.ForceSnapshot || subscription.Replica.NeedsSnapshot ? 0 : subscription.Replica.Revision };
                Send(message.Target, ModStateSessionWire.Encode(message)); subscription.NextRequest = now + RequestSeconds;
            }
            foreach (var lease in leases.Values.Where(l => l.Expires <= now || !Connected(l.Observer) || !Observe(l.Registration, l.Observer)).ToArray())
            { Deny(lease, ModStateSessionStatus.Denied); leases.Remove(lease.Key); }
            var active = leases.Values.ToArray(); if (active.Length == 0) return;
            if (now - sendWindow >= 1) { sendWindow = now; sentInWindow = 0; }
            int budget = Math.Min(8, 96 - sentInWindow);
            for (int i = 0; i < active.Length && budget > 0; i++)
            {
                var lease = active[(roundRobin + i) % active.Length];
                if (SendChunk(lease)) { budget--; sentInWindow++; }
            }
            roundRobin = (roundRobin + 1) % active.Length;
        }
        private bool SendChunk(Lease lease)
        {
            Lifecycle();
            if (!leases.TryGetValue(lease.Key, out var current) || current != lease) return false;
            if (!Observe(lease.Registration, lease.Observer)) return false;
            var publisher = lease.Registration.Publisher; if (!publisher.HasSnapshot) return false;
            if (lease.Sending == null)
            {
                if (leases.Values.Count(other => other.Observer == lease.Observer && other.Sending != null) >= MaximumAssembliesPerPeer) return false;
                ModStateFrame frame;
                if (lease.Recover)
                {
                    if (lease.KnownRevision > publisher.Revision) return false;
                    var recovery = publisher.Recover(lease.KnownRevision); lease.Recover = false;
                    if (recovery.UpToDate) { lease.SentRevision = lease.KnownRevision; return false; }
                    frame = recovery.Frames[0];
                }
                else
                {
                    if (lease.SentRevision >= publisher.Revision) return false;
                    frame = publisher.Recover(lease.SentRevision).Frames[0];
                }
                frame = frame.ForGeneration(lease.Binding.Generation); lease.Sending = ModStateCodec.Encode(frame);
                lease.Digest = ModSyncValidation.Digest(lease.Sending); lease.Transfer = Guid.NewGuid().ToString("N"); lease.NextChunk = 0; lease.SendingRevision = frame.Revision;
            }
            int offset = lease.NextChunk * ModStateSessionWire.ChunkBytes;
            byte[] chunk = lease.Sending.Skip(offset).Take(ModStateSessionWire.ChunkBytes).ToArray();
            var message = new ModStateSessionMessage { Kind = ModStateSessionKind.Chunk, Target = lease.Observer, Binding = lease.Binding, Request = lease.Request,
                Transfer = lease.Transfer, Digest = lease.Digest, Total = lease.Sending.Length, Index = lease.NextChunk,
                Count = (lease.Sending.Length + ModStateSessionWire.ChunkBytes - 1) / ModStateSessionWire.ChunkBytes, Payload = chunk };
            if (!Send(lease.Observer, ModStateSessionWire.Encode(message))) return false;
            lease.NextChunk++;
            if (lease.NextChunk == message.Count) { lease.SentRevision = lease.SendingRevision; lease.Sending = null; }
            return true;
        }
        private bool Send(string target, byte[] bytes)
        {
            Lifecycle();
            if (!Ready || !Connected(target)) return false;
            if (target == room.LocalPlayerId) { Receive(room.LocalPlayerId, bytes); return true; }
            return room.IsHost ? room.SendApplicationTo(target, bytes) : room.SendApplication(bytes);
        }
        private void Receive(string sender, byte[] bytes)
        {
            if (disposed || !ModStateSessionWire.IsMessage(bytes)) return;
            try
            {
                Lifecycle();
                if (!Ready || !Connected(sender) || !AcceptRate(sender, bytes.Length)) { RejectedPackets++; return; }
                var message = ModStateSessionWire.Decode(bytes);
                if (message.Binding.SessionId != room.RoomId || !Connected(message.Target)) { RejectedPackets++; return; }
                bool fromAuthority = message.Kind == ModStateSessionKind.Chunk || message.Kind == ModStateSessionKind.Status;
                if (fromAuthority ? message.Binding.AuthorityId != sender : message.Binding.AuthorityId != message.Target) { RejectedPackets++; return; }
                if (message.Target != room.LocalPlayerId)
                {
                    if (!room.IsHost || sender == message.Target) { RejectedPackets++; return; }
                    if (!AllowRelay(sender, message)) { RejectedPackets++; return; }
                    room.ForwardApplication(sender, message.Target, bytes); return;
                }
                if (message.Kind == ModStateSessionKind.Subscribe || message.Kind == ModStateSessionKind.Cancel) HandleRequest(sender, message);
                else HandleResponse(sender, message);
            }
            catch (Exception) { RejectedPackets++; }
        }
        private void HandleRequest(string sender, ModStateSessionMessage message)
        {
            string stream = Stream(message.Binding), key = sender + "|" + stream;
            if (!publishers.TryGetValue(stream, out var registration) || !registration.Publisher.Binding.SameScope(message.Binding, schema: false, generation: false))
            { Reply(sender, message, ModStateSessionStatus.Unavailable); return; }
            if (registration.Publisher.Binding.Schema != message.Binding.Schema) { Reply(sender, message, ModStateSessionStatus.SchemaMismatch); return; }
            if (!requests.TryGetValue(key, out var stamp))
            {
                if (requests.Count >= MaximumRemoteLeases * 4 || requests.Keys.Count(k => k.StartsWith(sender + "|", StringComparison.Ordinal)) >= MaximumStreams)
                { RejectedPackets++; return; }
                stamp = new RequestStamp { Admission = Generation(sender) }; requests.Add(key, stamp);
            }
            if (message.Request < stamp.Number || message.Request == stamp.Number && (stamp.Cancelled || stamp.Generation != message.Binding.Generation)) return;
            if (message.Request > stamp.Number)
            {
                stamp.Number = message.Request; stamp.Generation = message.Binding.Generation; stamp.Cancelled = false;
                leases.Remove(key);
            }
            if (message.Kind == ModStateSessionKind.Cancel)
            { stamp.Cancelled = true; leases.Remove(key); return; }
            if (!Observe(registration, sender)) { stamp.Cancelled = true; leases.Remove(key); Reply(sender, message, ModStateSessionStatus.Denied); return; }
            if (!registration.Publisher.HasSnapshot || message.Revision > registration.Publisher.Revision)
            { Reply(sender, message, ModStateSessionStatus.Unavailable); return; }
            if (!leases.TryGetValue(key, out var lease))
            {
                if (leases.Count >= MaximumRemoteLeases) { Reply(sender, message, ModStateSessionStatus.Unavailable); return; }
                lease = new Lease { Key = key, Observer = sender, Registration = registration, Binding = message.Binding, Request = message.Request,
                    LocalAdmission = Generation(room.LocalPlayerId), HostAdmission = Generation(HostId), ObserverAdmission = Generation(sender) };
                leases.Add(key, lease);
            }
            lease.Expires = now + LeaseSeconds;
            lease.KnownRevision = message.Revision;
            if (message.Revision < registration.Publisher.Revision) lease.Recover = true;
            Reply(sender, message, ModStateSessionStatus.Available);
        }
        private void Reply(string target, ModStateSessionMessage request, ModStateSessionStatus status)
        { Send(target, ModStateSessionWire.Encode(new ModStateSessionMessage { Kind = ModStateSessionKind.Status, Target = target, Binding = request.Binding, Request = request.Request, Status = status })); }
        private void Deny(Lease lease, ModStateSessionStatus status)
        {
            if (requests.TryGetValue(lease.Key, out var stamp)) stamp.Cancelled = true;
            Reply(lease.Observer, new ModStateSessionMessage { Binding = lease.Binding, Request = lease.Request }, status);
        }
        private void HandleResponse(string sender, ModStateSessionMessage message)
        {
            if (!subscriptions.TryGetValue(SubscriptionKey(message.Binding), out var subscription) || !subscription.Active ||
                subscription.Request != message.Request || !subscription.Binding.SameScope(message.Binding)) { RejectedPackets++; return; }
            subscription.LastSeen = now;
            if (message.Kind == ModStateSessionKind.Status)
            {
                if (message.Status == ModStateSessionStatus.Denied) { Cancel(subscription, ModStateSubscriptionStatus.Denied); return; }
                if (message.Status == ModStateSessionStatus.SchemaMismatch) { Cancel(subscription, ModStateSubscriptionStatus.SchemaMismatch); return; }
                if (message.Status == ModStateSessionStatus.Unavailable) { subscription.Status = ModStateSubscriptionStatus.Unavailable; subscription.Notify(); }
                return;
            }
            string key = sender + "|" + message.Transfer;
            if (!assemblies.TryGetValue(key, out var assembly))
            {
                // A refused/expired transfer must not reserve a slot from its tail.
                // Reliable RoomSession links deliver chunk zero before later chunks;
                // after protocol-boundary loss a fresh full recovery starts at zero.
                if (message.Index != 0) { subscription.ForceSnapshot = true; RejectedPackets++; return; }
                if (assemblies.Count >= MaximumAssemblies || assemblies.Values.Count(a => a.Sender == sender) >= MaximumAssembliesPerPeer ||
                    IncompleteAssemblyBytes + message.Total > MaximumAssemblyBytes || assemblies.Values.Where(a => a.Sender == sender).Sum(a => a.Total) + message.Total > MaximumAssemblyBytesPerPeer)
                { subscription.ForceSnapshot = true; RejectedPackets++; return; }
                assembly = new Assembly { Sender = sender, Transfer = message.Transfer, Digest = message.Digest, Subscription = subscription,
                    Total = message.Total, Count = message.Count, Chunks = new byte[message.Count][], Expires = now + TransferSeconds, HardDeadline = now + 60 };
                assemblies.Add(key, assembly);
            }
            if (assembly.Subscription != subscription || assembly.Total != message.Total || assembly.Count != message.Count || assembly.Digest != message.Digest)
            { assemblies.Remove(key); subscription.ForceSnapshot = true; RejectedPackets++; return; }
            byte[] previous = assembly.Chunks[message.Index];
            if (previous != null)
            {
                if (!previous.SequenceEqual(message.Payload)) { assemblies.Remove(key); subscription.ForceSnapshot = true; RejectedPackets++; }
                return;
            }
            assembly.Chunks[message.Index] = message.Payload; assembly.Received++; assembly.Expires = now + TransferSeconds;
            if (assembly.Received != assembly.Count) return;
            assemblies.Remove(key); byte[] encoded = assembly.Chunks.SelectMany(c => c).ToArray();
            if (encoded.Length != assembly.Total || ModSyncValidation.Digest(encoded) != assembly.Digest) { subscription.ForceSnapshot = true; RejectedPackets++; return; }
            var frame = ModStateCodec.Decode(encoded);
            if (!message.Binding.SameScope(frame.Binding)) { subscription.ForceSnapshot = true; RejectedPackets++; return; }
            var result = subscription.Replica.Accept(sender, frame); subscription.LastApplyResult = result;
            if (result == ModStateApplyResult.UnknownSchema || result == ModStateApplyResult.QuarantineFull) { Cancel(subscription, ModStateSubscriptionStatus.SchemaMismatch); return; }
            subscription.ForceSnapshot = subscription.Replica.NeedsSnapshot;
            subscription.Status = subscription.ForceSnapshot ? ModStateSubscriptionStatus.NeedsSnapshot : ModStateSubscriptionStatus.Active;
            if (subscription.ForceSnapshot) subscription.NextRequest = now;
            subscription.Notify();
        }
        private bool AcceptRate(string sender, int bytes)
        {
            if (!rates.TryGetValue(sender, out var rate)) { if (rates.Count >= 64) return false; rates.Add(sender, rate = new Rate { At = now }); }
            if (now - rate.At >= 1) { rate.At = now; rate.Packets = rate.Bytes = 0; }
            // 96 data fragments/s plus lease renewal/status traffic can exceed 128.
            return ++rate.Packets <= 256 && (rate.Bytes += bytes) <= 1024 * 1024;
        }
        private bool Allowed(string player)
        { if (player == null) return false; try { return canUsePeer(player); } catch (Exception) { return false; } }
        private bool Connected(string player)
        { return Generation(player) > 0; }
        private string HostId { get { return room.Players.FirstOrDefault()?.Id; } }
        private long Generation(string player)
        {
            if (!Allowed(player) || !room.Players.Any(p => p.Id == player && p.Connected)) return 0;
            try { long value = admissionGeneration == null ? 1 : admissionGeneration(player); return value > 0 ? value : 0; }
            catch (Exception) { return 0; }
        }
        private static bool Observe(Registration registration, string player)
        { try { return registration.Owner != null && registration.CanObserve(player); } catch (Exception) { return false; } }
        private static string Stream(ModStateBinding binding) { return binding.ModId.Length + ":" + binding.ModId + binding.Channel; }
        private static string SubscriptionKey(ModStateBinding binding) { return binding.AuthorityId + "|" + Stream(binding); }
        private static bool Remember(HashSet<string> set, Queue<string> recent, string value)
        {
            if (!set.Add(value)) return false;
            recent.Enqueue(value); if (recent.Count > 256) set.Remove(recent.Dequeue()); return true;
        }
        private void Lifecycle()
        {
            if (disposed || refreshing) return;
            refreshing = true;
            try
            {
                long local = Generation(room.LocalPlayerId), host = Generation(HostId);
                bool ready = Ready;
                var retired = subscriptions.Values.Where(s => s.Active && (!ready || s.LocalAdmission != local || s.HostAdmission != host ||
                    s.AuthorityAdmission != Generation(s.Binding.AuthorityId))).ToArray();
                // Retire all wire work before invoking callbacks, and do not send an old
                // Cancel/Status into the replacement admission context.
                foreach (var subscription in retired)
                {
                    subscription.Active = false; subscription.Status = ModStateSubscriptionStatus.Disconnected;
                    subscription.Replica.Disconnect(); subscriptions.Remove(SubscriptionKey(subscription.Binding));
                }
                foreach (var registration in publishers.Values.Where(r => admissionGeneration != null && (!ready || r.LocalAdmission != local || r.HostAdmission != host)).ToArray())
                { registration.Owner = null; publishers.Remove(registration.Key); }
                foreach (var lease in leases.Values.ToArray())
                    if (!ready || lease.Registration.Owner == null || lease.LocalAdmission != local || lease.HostAdmission != host || lease.ObserverAdmission != Generation(lease.Observer))
                    { if (requests.TryGetValue(lease.Key, out var stamp)) stamp.Cancelled = true; leases.Remove(lease.Key); }
                foreach (var request in requests)
                {
                    long current = Generation(request.Key.Substring(0, 32));
                    if (!ready || request.Value.Admission != current) { request.Value.Admission = current; request.Value.Cancelled = true; }
                }
                foreach (var route in relayRoutes.Values)
                    if (!ready || route.LocalAdmission != local || route.ObserverAdmission != Generation(route.Observer) || route.AuthorityAdmission != Generation(route.Authority)) route.Cancelled = true;
                foreach (var key in assemblies.Where(p => !p.Value.Subscription.Active).Select(p => p.Key).ToArray()) assemblies.Remove(key);
                foreach (var peer in rates.Keys.Where(p => !Connected(p)).ToArray()) rates.Remove(peer);
                var present = new HashSet<string>(room.Players.Select(p => p.Id), StringComparer.Ordinal);
                foreach (var key in requests.Keys.Where(k => !present.Contains(k.Substring(0, 32))).ToArray()) requests.Remove(key);
                foreach (var key in relayRoutes.Where(p => !present.Contains(p.Value.Observer) || !present.Contains(p.Value.Authority)).Select(p => p.Key).ToArray()) relayRoutes.Remove(key);
                foreach (var peer in retiredRelayRequests.Keys.Where(p => !present.Contains(p)).ToArray()) retiredRelayRequests.Remove(peer);
                foreach (var subscription in retired) subscription.Notify();
            }
            finally { refreshing = false; }
        }
        private bool AllowRelay(string sender, ModStateSessionMessage message)
        {
            bool request = message.Kind == ModStateSessionKind.Subscribe || message.Kind == ModStateSessionKind.Cancel;
            string observer = request ? sender : message.Target, authority = request ? message.Target : sender;
            string key = observer + "|" + authority + "|" + Stream(message.Binding);
            if (!relayRoutes.TryGetValue(key, out var route))
            {
                if (!request || retiredRelayRequests.TryGetValue(observer, out var retired) && message.Request <= retired ||
                    relayRoutes.Values.Count(r => r.Observer == observer && !r.Cancelled) >= MaximumStreams) return false;
                // Compact cancelled scopes into a per-observer request floor. Known
                // active requests remain valid, while an evicted old request cannot
                // recreate a route under a replacement admission stamp.
                if (relayRoutes.Count >= MaximumRemoteLeases * 4)
                {
                    var oldest = relayRoutes.Where(p => p.Value.Cancelled).OrderBy(p => p.Value.Request).FirstOrDefault();
                    if (oldest.Value == null) return false;
                    var old = oldest.Value;
                    retiredRelayRequests.TryGetValue(old.Observer, out var floor);
                    retiredRelayRequests[old.Observer] = Math.Max(floor, old.Request); relayRoutes.Remove(oldest.Key);
                }
                route = new RelayRoute { Observer = observer, Authority = authority }; relayRoutes.Add(key, route);
            }
            if (request)
            {
                if (message.Request < route.Request || message.Request == route.Request && (route.Cancelled || !message.Binding.SameScope(route.Binding))) return false;
                if (message.Request > route.Request)
                {
                    route.Request = message.Request; route.Binding = message.Binding; route.Cancelled = false;
                    route.LocalAdmission = Generation(room.LocalPlayerId); route.ObserverAdmission = Generation(observer); route.AuthorityAdmission = Generation(authority);
                }
                if (message.Kind == ModStateSessionKind.Cancel) route.Cancelled = true;
                return true;
            }
            if (route.Cancelled || route.Request != message.Request || !message.Binding.SameScope(route.Binding)) return false;
            if (message.Kind == ModStateSessionKind.Status && message.Status != ModStateSessionStatus.Available)
                route.Cancelled = true;
            return true;
        }
        internal void Cancel(ModStateSubscription subscription, ModStateSubscriptionStatus status)
        {
            if (!subscription.Active) { if (status == ModStateSubscriptionStatus.Disposed) subscription.Status = status; return; }
            subscription.Active = false; subscription.Status = status;
            if (Ready) Send(subscription.Binding.AuthorityId, ModStateSessionWire.Encode(new ModStateSessionMessage { Kind = ModStateSessionKind.Cancel,
                Target = subscription.Binding.AuthorityId, Binding = subscription.Binding, Request = subscription.Request }));
            subscription.Replica.Disconnect(); subscriptions.Remove(SubscriptionKey(subscription.Binding));
            foreach (var key in assemblies.Where(p => p.Value.Subscription == subscription).Select(p => p.Key).ToArray()) assemblies.Remove(key);
            subscription.Notify();
        }
        private void Unregister(Registration registration)
        {
            if (!publishers.TryGetValue(registration.Key, out var current) || current != registration) return;
            foreach (var lease in leases.Values.Where(l => l.Registration == registration).ToArray()) { Deny(lease, ModStateSessionStatus.Unavailable); leases.Remove(lease.Key); }
            publishers.Remove(registration.Key);
            foreach (var key in requests.Keys.Where(k => k.Substring(33) == registration.Key).ToArray()) requests.Remove(key);
        }
        public void Dispose()
        {
            if (disposed) return;
            foreach (var subscription in subscriptions.Values.ToArray()) Cancel(subscription, ModStateSubscriptionStatus.Disconnected);
            foreach (var registration in publishers.Values.ToArray()) registration.Dispose();
            disposed = true; room.ApplicationMessageReceived -= Receive; room.Changed -= Lifecycle;
            subscriptions.Clear(); publishers.Clear(); leases.Clear(); requests.Clear(); assemblies.Clear(); rates.Clear(); relayRoutes.Clear(); retiredRelayRequests.Clear(); usedGenerations.Clear(); usedPublisherEpochs.Clear();
            recentGenerations.Clear(); recentPublisherEpochs.Clear();
        }
    }

    internal enum ModStateSessionKind : byte { Subscribe = 1, Cancel = 2, Chunk = 3, Status = 4 }
    internal enum ModStateSessionStatus : byte { Available = 1, Denied = 2, Unavailable = 3, SchemaMismatch = 4 }
    internal sealed class ModStateSessionMessage
    {
        internal ModStateSessionKind Kind; internal string Target; internal ModStateBinding Binding; internal long Request, Revision;
        internal string Transfer, Digest; internal int Total, Index, Count; internal byte[] Payload; internal ModStateSessionStatus Status;
    }
    internal static class ModStateSessionWire
    {
        internal const int ChunkBytes = 3072;
        private const int Magic = 0x54534d4f;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal static bool IsMessage(byte[] bytes) { return bytes != null && bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == Magic; }
        internal static byte[] Encode(ModStateSessionMessage message)
        {
            Validate(message);
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)message.Kind); ModStateCodec.WriteText(writer, message.Target);
                ModStateCodec.WriteBinding(writer, message.Binding); writer.Write(message.Request);
                if (message.Kind == ModStateSessionKind.Subscribe) writer.Write(message.Revision);
                if (message.Kind == ModStateSessionKind.Status) writer.Write((byte)message.Status);
                if (message.Kind == ModStateSessionKind.Chunk)
                {
                    ModStateCodec.WriteText(writer, message.Transfer); ModStateCodec.WriteText(writer, message.Digest);
                    writer.Write(message.Total); writer.Write((ushort)message.Index); writer.Write((ushort)message.Count); writer.Write((ushort)message.Payload.Length); writer.Write(message.Payload);
                }
                if (stream.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException("mod_state_packet_budget");
                return stream.ToArray();
            }
        }
        internal static ModStateSessionMessage Decode(byte[] bytes)
        {
            if (!IsMessage(bytes) || bytes.Length > Protocol.MaxApplicationBytes) throw new InvalidDataException("mod_state_packet_budget");
            try
            {
                using (var stream = new MemoryStream(bytes, false)) using (var reader = new BinaryReader(stream, Utf8))
                {
                    reader.ReadInt32(); if (reader.ReadByte() != 1) throw new InvalidDataException("mod_state_packet_version");
                    var message = new ModStateSessionMessage { Kind = (ModStateSessionKind)reader.ReadByte(), Target = ModStateCodec.ReadText(reader, 32), Binding = ModStateCodec.ReadBinding(reader), Request = reader.ReadInt64() };
                    if (message.Kind == ModStateSessionKind.Subscribe) message.Revision = reader.ReadInt64();
                    if (message.Kind == ModStateSessionKind.Status) message.Status = (ModStateSessionStatus)reader.ReadByte();
                    if (message.Kind == ModStateSessionKind.Chunk)
                    {
                        message.Transfer = ModStateCodec.ReadText(reader, 32); message.Digest = ModStateCodec.ReadText(reader, 64);
                        message.Total = reader.ReadInt32(); message.Index = reader.ReadUInt16(); message.Count = reader.ReadUInt16(); int length = reader.ReadUInt16();
                        if (length > ChunkBytes) throw new InvalidDataException("mod_state_chunk_budget"); message.Payload = reader.ReadBytes(length);
                        if (message.Payload.Length != length) throw new EndOfStreamException();
                    }
                    if (stream.Position != stream.Length) throw new InvalidDataException("mod_state_packet_trailing"); Validate(message); return message;
                }
            }
            catch (Exception error) when (error is ArgumentException || error is EndOfStreamException || error is FormatException)
            { throw new InvalidDataException("invalid_mod_state_packet", error); }
        }
        private static void Validate(ModStateSessionMessage message)
        {
            if (message == null || message.Binding == null || message.Request < 1 || message.Kind < ModStateSessionKind.Subscribe || message.Kind > ModStateSessionKind.Status)
                throw new InvalidDataException("invalid_mod_state_packet");
            ModStateBinding.Nonce(message.Target);
            if (message.Kind == ModStateSessionKind.Subscribe && message.Revision < 0 || message.Kind == ModStateSessionKind.Status &&
                (message.Status < ModStateSessionStatus.Available || message.Status > ModStateSessionStatus.SchemaMismatch)) throw new InvalidDataException("invalid_mod_state_packet");
            if (message.Kind != ModStateSessionKind.Chunk) return;
            ModStateBinding.Nonce(message.Transfer);
            if (!ModSyncValidation.Sha256(message.Digest) || message.Total < 1 || message.Total > ModStateCodec.MaximumEncodedBytes ||
                message.Count != (message.Total + ChunkBytes - 1) / ChunkBytes || message.Index < 0 || message.Index >= message.Count || message.Payload == null ||
                message.Payload.Length != Math.Min(ChunkBytes, message.Total - message.Index * ChunkBytes)) throw new InvalidDataException("invalid_mod_state_chunk");
        }
    }
}
