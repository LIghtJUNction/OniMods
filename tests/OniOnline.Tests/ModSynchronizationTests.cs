using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Sdk;
using OniOnline.Network;

internal static class ModSynchronizationTests
{
    private static readonly string HashA = new string('a', 64), HashB = new string('b', 64);
    private static ModManifestEntry Entry(string id = "required", string version = "1", string hash = null,
        ModSyncPolicy policy = ModSyncPolicy.Required, bool enabled = true, ulong workshop = 77)
    { return new ModManifestEntry(id, version, policy, workshop == 0 ? ModDistributionSource.Local : ModDistributionSource.SteamWorkshop, workshop, hash ?? HashA, enabled); }
    private static ModManifest Manifest(params ModManifestEntry[] entries) { return new ModManifest(entries); }
    internal static void Run(Action<bool, string> check)
    {
        ManifestPlanning(check); ManifestBoundary(check); StateOrdering(check); StateRecovery(check); StateFailures(check);
        SessionDelivery(check); SessionFaults(check); SessionBounds(check); SessionConcurrency(check); SessionRequestOrdering(check); SessionSlowProgress(check); SessionGlobalBudget(check); SessionLifetime(check);
        SessionAdmissionEpoch(check); SessionRelayAdmissionEpoch(check); SessionObserverAdmissionEpoch(check); SessionRelayLifetime(check);
    }
    private static bool Throws<T>(Action action) where T : Exception
    { try { action(); return false; } catch (T) { return true; } }
    private static void ManifestPlanning(Action<bool, string> check)
    {
        var authority = Manifest(Entry("本地 模组.Local", workshop: 0), Entry());
        var same = Manifest(Entry(), Entry("本地 模组.Local", workshop: 0));
        check(authority.Fingerprint == same.Fingerprint && ModSyncPlanner.Compare(authority, same).CanJoin,
            "native Unicode IDs preserve identity and manifest order is canonical");
        foreach (var example in new[] {
            new { Actual = Manifest(), Kind = ModSyncChangeKind.Missing },
            new { Actual = Manifest(Entry(enabled: false)), Kind = ModSyncChangeKind.Disabled },
            new { Actual = Manifest(Entry(workshop: 78)), Kind = ModSyncChangeKind.SourceMismatch },
            new { Actual = Manifest(Entry(version: "2")), Kind = ModSyncChangeKind.VersionMismatch },
            new { Actual = Manifest(Entry(hash: HashB)), Kind = ModSyncChangeKind.ContentMismatch }
        })
        {
            var plan = ModSyncPlanner.Compare(Manifest(Entry()), example.Actual);
            check(!plan.CanJoin && plan.Changes.Single().Kind == example.Kind && plan.WorkshopItemsToDownload.SequenceEqual(new ulong[] { 77 }),
                "version labels, Workshop provenance, loaded enablement and exact bytes each gate entry");
        }
        check(ModSyncPlanner.Compare(Manifest(Entry(enabled: false)), Manifest()).CanJoin,
            "an authority's disabled installed mod is not a network requirement");
        check(ModSyncPlanner.Compare(Manifest(Entry()), Manifest(Entry(policy: ModSyncPolicy.ClientOnly, hash: HashB))).Changes.Single().Kind == ModSyncChangeKind.ContentMismatch,
            "guest ClientOnly declaration cannot hide host-required content");
        var extras = Manifest(Entry(), Entry("other"));
        var strict = ModSyncPlanner.Compare(Manifest(Entry()), extras);
        check(strict.Changes.Single().Kind == ModSyncChangeKind.Unexpected && strict.Changes[0].Expected == null && !strict.Changes[0].CanDownload,
            "unlisted active network mod blocks entry without inventing an install or delete action");
        check(ModSyncPlanner.Compare(Manifest(Entry()), Manifest(Entry(), Entry("other", policy: ModSyncPolicy.ClientOnly))).CanJoin &&
            ModSyncPlanner.Compare(Manifest(Entry()), extras, ModSyncComparisonMode.AuthorityRequirementsOnly).CanJoin,
            "explicit client-only mods and explicitly selected authority-only comparison have defined semantics");
        var loose = ModSyncPlanner.Compare(Manifest(Entry()), extras, ModSyncComparisonMode.AuthorityRequirementsOnly);
        check(loose.Fingerprint != strict.Fingerprint && strict.IsCurrent(Manifest(Entry()), extras) && !strict.IsCurrent(Manifest(Entry(hash: HashB)), extras),
            "approval identity binds comparison policy and both immutable catalogs");
    }
    private static void ManifestBoundary(Action<bool, string> check)
    {
        foreach (Action invalid in new Action[] {
            () => Entry("a\n"), () => Entry("\ud800"), () => Entry(new string('x', 129)),
            () => Entry(version: ""), () => Entry(hash: "bad"), () => Entry(hash: ""),
            () => new ModManifestEntry("x", "1", ModSyncPolicy.Required, ModDistributionSource.Local, 1, HashA),
            () => Manifest(Entry(), Entry()),
            () => new ModManifest(Enumerable.Range(0, 257).Select(i => Entry(i.ToString())))
        }) check(Throws<ArgumentException>(invalid), "invalid mod metadata and manifest expansion are rejected at the boundary");
        var original = Manifest(Entry("可视化", policy: ModSyncPolicy.ClientOnly, hash: ""), Entry());
        var bytes = ModManifestCodec.Encode(original);
        check(ModManifestCodec.Decode(bytes).Fingerprint == original.Fingerprint, "bounded manifest codec preserves all declared metadata");
        check(Throws<InvalidDataException>(() => ModManifestCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray())) &&
            Throws<InvalidDataException>(() => ModManifestCodec.Decode(new byte[ModManifestCodec.MaximumEncodedBytes + 1])),
            "manifest codec rejects trailing and oversized payloads");
        check(Enumerable.Range(0, bytes.Length).All(length => Throws<InvalidDataException>(() => ModManifestCodec.Decode(bytes.Take(length).ToArray()))),
            "every truncated manifest fails rather than accepting a partial catalog");
    }
    private static ModStateBinding Binding(int schema = 1, string generation = null, string authority = null)
    { return new ModStateBinding("11111111111111111111111111111111", "测试 模组", "weather", authority ?? "22222222222222222222222222222222", "33333333333333333333333333333333", generation ?? "44444444444444444444444444444444", schema); }
    private sealed class Reducer : IModStateReducer
    {
        internal bool Fail; internal int Applies;
        public bool ValidateSnapshot(byte[] bytes) { return bytes.Length > 0 && bytes[0] != 255; }
        public byte[] ApplyDelta(byte[] baseline, byte[] delta)
        { Applies++; baseline[0] += delta[0]; if (Fail) throw new InvalidOperationException("reducer failed after mutating its input"); return baseline; }
    }
    private static void StateOrdering(Action<bool, string> check)
    {
        var binding = Binding(); var publisherReducer = new Reducer(); var publisher = new ModStatePublisher(binding, publisherReducer);
        byte[] input = { 10 }; var initial = publisher.PublishSnapshot(input); input[0] = 90;
        var reducer = new Reducer(); var replica = new ModStateReplica(binding, reducer);
        check(replica.Accept("55555555555555555555555555555555", initial) == ModStateApplyResult.WrongAuthority && replica.Current == null,
            "an authenticated third player cannot assert a state authority");
        check(replica.Accept(binding.AuthorityId, initial) == ModStateApplyResult.Applied && replica.Current[0] == 10,
            "snapshot input is copied before publication");
        var second = publisher.PublishDelta(new byte[] { 2 }); var third = publisher.PublishDelta(new byte[] { 3 });
        check(replica.Accept(binding.AuthorityId, third) == ModStateApplyResult.NeedSnapshot && replica.Revision == 1 && replica.Current[0] == 10,
            "a delta gap preserves the last good revision");
        check(replica.Accept(binding.AuthorityId, second) == ModStateApplyResult.Applied && replica.NeedsSnapshot &&
            replica.Accept(binding.AuthorityId, third) == ModStateApplyResult.Applied && !replica.NeedsSnapshot && replica.Current[0] == 15,
            "contiguous replay repairs a lost delta without jumping the revision");
        check(replica.Accept(binding.AuthorityId, third) == ModStateApplyResult.Duplicate && reducer.Applies == 2 &&
            replica.Accept(binding.AuthorityId, initial) == ModStateApplyResult.Stale,
            "duplicate and delayed packets never replay reducers or roll state back");
        var conflict = new ModStateFrame(binding, third.Revision, third.BaseRevision, false, new byte[] { 20 });
        check(replica.Accept(binding.AuthorityId, conflict) == ModStateApplyResult.Conflict && replica.Current[0] == 15,
            "conflicting same-revision state preserves the committed bytes");
        check(replica.Accept(binding.AuthorityId, publisher.CaptureSnapshot()) == ModStateApplyResult.Duplicate && !replica.NeedsSnapshot,
            "a matching authoritative full snapshot resolves a same-revision conflict without an endless recovery loop");
        var copy = replica.Current; copy[0] = 8; var frameCopy = third.Payload; frameCopy[0] = 99;
        check(replica.Current[0] == 15 && third.Payload[0] == 3, "consumers cannot mutate state or queued frames");
        var oldGeneration = initial; replica.Disconnect();
        check(replica.Accept(binding.AuthorityId, third) == ModStateApplyResult.Disconnected, "offline state refuses delivery immediately");
        replica.Rebind(binding.WithGeneration("66666666666666666666666666666666"));
        check(replica.Accept(binding.AuthorityId, oldGeneration) == ModStateApplyResult.WrongScope && replica.NeedsSnapshot,
            "a reconnect lease rejects old-generation snapshots");
        var fresh = publisher.CaptureSnapshot().ForGeneration(replica.Binding.Generation);
        check(replica.Accept(binding.AuthorityId, fresh) == ModStateApplyResult.Applied && replica.Current[0] == 15,
            "reconnect resumes from a fresh authorized baseline");
    }
    private static void StateRecovery(Action<bool, string> check)
    {
        var binding = Binding(); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
        for (int i = 0; i < 40; i++) publisher.PublishDelta(new byte[] { 1 });
        check(publisher.Recover(1).UsesSnapshot && publisher.Recover(40).Frames.Single().Revision == 41 && publisher.Recover(41).UpToDate,
            "bounded replay falls back to a full snapshot once its contiguous history is evicted");
        var replica = new ModStateReplica(binding, new Reducer());
        check(replica.Accept(binding.AuthorityId, publisher.Recover(1).Frames.Single()) == ModStateApplyResult.Applied && replica.Current[0] == 41,
            "history eviction still allows a complete state recovery");
        check(Throws<ArgumentOutOfRangeException>(() => publisher.Recover(42)) && Throws<ArgumentOutOfRangeException>(() => publisher.Recover(-1)),
            "recovery cannot acknowledge an impossible revision");
        var unsupported = new ModStateFrame(Binding(schema: 2), 42, 0, true, new byte[] { 5, 7 });
        check(replica.Accept(binding.AuthorityId, unsupported) == ModStateApplyResult.UnknownSchema && replica.BlockedOnSchema && replica.Current[0] == 41 && replica.Quarantined.Payload.SequenceEqual(new byte[] { 5, 7 }),
            "unknown schema preserves its exact bytes and leaves the supported checkpoint intact");
        check(replica.Accept(binding.AuthorityId, new ModStateFrame(binding, 43, 0, true, new byte[] { 8 })) == ModStateApplyResult.UnknownSchema && replica.Revision == 41,
            "a schema-blocked generation cannot silently overwrite unknown state with a fallback");
        replica.Rebind(new ModStateBinding(binding.SessionId, binding.ModId, binding.Channel, binding.AuthorityId, binding.Epoch, "77777777777777777777777777777777", 2));
        check(replica.Accept(binding.AuthorityId, unsupported.ForGeneration(replica.Binding.Generation)) == ModStateApplyResult.Applied && replica.Quarantined == null && replica.QuarantinedFrames[0].Payload[0] == 5,
            "explicit schema upgrade requires a fresh binding and preserves the quarantine for persistence");
        for (int i = 0; i < ModStateReplica.MaximumQuarantinedFrames; i++)
        {
            var next = new ModStateBinding(binding.SessionId, binding.ModId, binding.Channel, binding.AuthorityId, binding.Epoch, Guid.NewGuid().ToString("N"), 2);
            replica.Rebind(next);
            var unknown = new ModStateBinding(next.SessionId, next.ModId, next.Channel, next.AuthorityId, next.Epoch, next.Generation, 3);
            var result = replica.Accept(binding.AuthorityId, new ModStateFrame(unknown, i + 1, 0, true, new byte[] { (byte)(20 + i) }));
            check(result == (i < 3 ? ModStateApplyResult.UnknownSchema : ModStateApplyResult.QuarantineFull) && replica.BlockedOnSchema,
                "unknown generations retain separate bounded quarantine evidence and explicitly reject overflow");
        }
        check(replica.QuarantinedFrames.Count == ModStateReplica.MaximumQuarantinedFrames && replica.QuarantinedFrames[0].Payload[0] == 5,
            "quarantine overflow cannot overwrite the oldest unsupported checkpoint");
    }
    private static void StateFailures(Action<bool, string> check)
    {
        var binding = Binding(); var reducer = new Reducer(); var publisher = new ModStatePublisher(binding, reducer); publisher.PublishSnapshot(new byte[] { 4 });
        reducer.Fail = true;
        check(Throws<InvalidOperationException>(() => publisher.PublishDelta(new byte[] { 2 })) && publisher.Revision == 1 && publisher.CaptureSnapshot().Payload[0] == 4,
            "publisher reducer failure after input mutation is transactional");
        var replicaReducer = new Reducer { Fail = true }; var replica = new ModStateReplica(binding, replicaReducer); replica.Accept(binding.AuthorityId, publisher.CaptureSnapshot());
        check(replica.Accept(binding.AuthorityId, new ModStateFrame(binding, 2, 1, false, new byte[] { 5 })) == ModStateApplyResult.ReducerFailed && replica.Revision == 1 && replica.Current[0] == 4 && replica.NeedsSnapshot,
            "replica reducer failure cannot change its accepted bytes or revision");
        var full = new ModStateFrame(binding, 2, 0, true, new byte[] { 9 });
        check(replica.Accept(binding.AuthorityId, full) == ModStateApplyResult.Applied && replica.Current[0] == 9, "authoritative snapshot repairs reducer failure");
        var encoded = ModStateCodec.Encode(full);
        var exhausted = ModStatePublisher.Restore(binding, new Reducer(), long.MaxValue, new byte[] { 3 });
        check(Throws<OverflowException>(() => exhausted.PublishDelta(new byte[] { 1 })) && Throws<OverflowException>(() => exhausted.PublishSnapshot(new byte[] { 5 })) && exhausted.Revision == long.MaxValue && exhausted.CaptureSnapshot().Payload[0] == 3,
            "restored revision overflow fails without mutating the checkpoint");
        check(ModStateCodec.Decode(encoded).Payload[0] == 9 && Enumerable.Range(0, encoded.Length).All(length => Throws<InvalidDataException>(() => ModStateCodec.Decode(encoded.Take(length).ToArray()))) &&
            Throws<InvalidDataException>(() => ModStateCodec.Decode(encoded.Concat(new byte[] { 0 }).ToArray())), "state codec rejects every truncation and trailing bytes");
        foreach (Action invalid in new Action[] {
            () => new ModStateFrame(binding, 1, 1, true, new byte[0]),
            () => new ModStateFrame(binding, 3, 1, false, new byte[0]),
            () => new ModStateFrame(binding, long.MaxValue, long.MaxValue, false, new byte[0]),
            () => new ModStateFrame(binding, 1, 0, true, new byte[ModStateFrame.MaximumSnapshotBytes + 1]),
            () => new ModStateFrame(binding, 2, 1, false, new byte[ModStateFrame.MaximumDeltaBytes + 1]),
            () => replica.Rebind(binding)
        }) check(Throws<ArgumentException>(invalid), "invalid revision arithmetic, state budgets and reused reconnect generation fail closed");
    }

    private sealed class Peer : IDisposable
    {
        internal IRoomTransport Transport; internal RoomSession Room; internal ModStateSession State;
        internal bool Admitted = true, PumpState = true;
        internal long AdmissionGeneration = 1;
        internal readonly Dictionary<string, long> AdmissionView = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly List<ModStateSessionMessage> Messages = new List<ModStateSessionMessage>();
        internal readonly List<byte[]> Raw = new List<byte[]>();
        public void Dispose() { State.Dispose(); Room.Dispose(); }
    }
    private sealed class TestRoom : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(92831);
        internal readonly Peer[] Peers;
        internal TestRoom(int count = 3, bool generationAware = false)
        {
            Peers = new Peer[count];
            for (int i = 0; i < count; i++)
            {
                var peer = Peers[i] = new Peer(); peer.Transport = i == 0 ? Network.Host : Network.Connect();
                peer.Room = new RoomSession(peer.Transport, new SessionProfile { Name = "State" + i, GameVersion = "v", CycleTrimVersion = "v" }, "", "Sync",
                    reconnect: i == 0 ? null : () => peer.Transport = Network.Connect(), options: new SessionOptions { RetryJitter = () => .5 });
                peer.State = new ModStateSession(peer.Room, _ => peer.Admitted, generationAware ? new Func<string, long>(id =>
                    peer.AdmissionView.TryGetValue(id, out var token) ? token : Peers.FirstOrDefault(p => p?.Room.LocalPlayerId == id)?.AdmissionGeneration ?? 0) : null);
                peer.Room.ApplicationMessageReceived += (_, bytes) =>
                { if (ModStateSessionWire.IsMessage(bytes)) try { peer.Messages.Add(ModStateSessionWire.Decode(bytes)); peer.Raw.Add((byte[])bytes.Clone()); } catch (InvalidDataException) { } };
            }
            Until(() => Peers.All(p => p.Room.Joined && p.Room.Players.Length == count), "authenticate");
        }
        internal void Step() { Network.Advance(.05); foreach (var peer in Peers) peer.Room.Pump(Network.Now); foreach (var peer in Peers) if (peer.PumpState) peer.State.Pump(Network.Now); }
        internal void Advance(double seconds) { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> predicate, string stage, double timeout = 30)
        { double until = Network.Now + timeout; while (Network.Now < until) { Step(); if (predicate()) return; } throw new Exception("mod state timeout: " + stage); }
        public void Dispose() { foreach (var peer in Peers.Reverse()) peer.Dispose(); Network.Dispose(); }
    }
    private static ModStateBinding NativeBinding(Peer owner, string channel = "state")
    { return new ModStateBinding(owner.Room.RoomId, "example.mod", channel, owner.Room.LocalPlayerId, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 1); }
    private static ModStateSubscription Observe(Peer observer, ModStateBinding binding)
    { return observer.State.Subscribe(binding.ModId, binding.Channel, binding.AuthorityId, binding.Epoch, binding.Schema, new Reducer()); }
    private static void SessionDelivery(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner);
            var publisher = new ModStatePublisher(binding, new Reducer()); byte[] big = Enumerable.Range(0, ModStateFrame.MaximumSnapshotBytes).Select(i => (byte)(i % 200)).ToArray();
            publisher.PublishSnapshot(big); bool allowed = true;
            using (owner.State.RegisterPublisher(publisher, id => allowed && id == observer.Room.LocalPlayerId))
            using (var subscription = Observe(observer, binding))
            {
                room.Until(() => subscription.Status == ModStateSubscriptionStatus.Active, "guest owner full snapshot via host");
                check(subscription.Replica.Current.SequenceEqual(big) && observer.Raw.All(p => p.Length <= Protocol.MaxApplicationBytes) &&
                    observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk) >= 22,
                    "64KiB production state travels in bounded fragments from guest authority through the real host relay");
                using (var denied = Observe(room.Peers[0], binding))
                { room.Until(() => denied.Status == ModStateSubscriptionStatus.Denied, "owner observer permission"); check(denied.Replica.Current == null, "routing through host does not grant the host observation permission"); }
                publisher.PublishDelta(new byte[] { 7 }); room.Until(() => subscription.Replica.Revision == 2, "delta");
                check(subscription.Replica.Current[0] == 7 && !subscription.Replica.NeedsSnapshot, "registered reducer state synchronizes over an authenticated guest-owned stream");
                allowed = false; publisher.PublishDelta(new byte[] { 4 });
                room.Until(() => subscription.Status == ModStateSubscriptionStatus.Denied, "permission revoked");
                check(subscription.Replica.Current[0] == 7 && !subscription.Replica.Connected, "owner revocation stops future state and disconnects the replica");
            }
            var host = room.Peers[0]; var hostBinding = NativeBinding(host); var hostPublisher = new ModStatePublisher(hostBinding, new Reducer()); hostPublisher.PublishSnapshot(new byte[] { 30 });
            using (host.State.RegisterPublisher(hostPublisher, _ => true))
            using (var local = Observe(host, hostBinding))
            using (var remote = Observe(owner, hostBinding))
            {
                room.Until(() => local.Status == ModStateSubscriptionStatus.Active && remote.Status == ModStateSubscriptionStatus.Active, "host loopback and remote");
                check(local.Replica.Current[0] == 30 && remote.Replica.Current[0] == 30, "host local observation uses the same authority and permission validation as remote observation");
            }
        }
    }
    private static void SessionFaults(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner);
            var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            {
                var subscription = Observe(observer, binding); room.Until(() => subscription.Status == ModStateSubscriptionStatus.Active, "fault baseline");
                bool dropped = false, gap = false;
                subscription.Changed += () => { if (subscription.Status == ModStateSubscriptionStatus.NeedsSnapshot) gap = true; };
                room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                {
                    if (fromHost || dropped) return false; var packet = Protocol.Decode(bytes);
                    if (packet.Kind != PacketKind.Application || !ModStateSessionWire.IsMessage(packet.Payload)) return false;
                    var message = ModStateSessionWire.Decode(packet.Payload);
                    if (message.Kind != ModStateSessionKind.Chunk || message.Count != 1 || ModStateCodec.Decode(message.Payload).IsSnapshot) return false;
                    dropped = true; return true;
                };
                publisher.PublishDelta(new byte[] { 2 }); publisher.PublishDelta(new byte[] { 3 });
                room.Until(() => subscription.Replica.Revision == 3 && subscription.Status == ModStateSubscriptionStatus.Active, "lost delta recovery");
                check(dropped && gap && subscription.Replica.Current[0] == 6, "lost delta triggers read-only snapshot recovery through the real subscription channel");
                room.Network.Settings.DropBeforeDelivery = null;
                byte[] old = observer.Raw.Last(bytes => ModStateSessionWire.Decode(bytes).Kind == ModStateSessionKind.Chunk);
                long accepted = subscription.Replica.Revision; int current = subscription.Replica.Current[0];
                // Third guest sends a real authenticated envelope while claiming owner binding.
                observer.Room.SendApplication(old); room.Advance(.2);
                check(subscription.Replica.Revision == accepted && subscription.Replica.Current[0] == current && room.Peers[0].State.RejectedPackets > 0,
                    "guest identity in the authenticated transport cannot impersonate a state authority");
                subscription.Dispose(); room.Advance(.2);
                using (var fresh = Observe(observer, binding))
                {
                    room.Until(() => fresh.Status == ModStateSubscriptionStatus.Active, "fresh generation");
                    owner.Room.SendApplication(old); room.Advance(.3);
                    check(fresh.Replica.Revision == 3 && fresh.Replica.Current[0] == 6, "old-generation frames cannot affect a reopened subscription");
                    owner.Transport.Disconnect("host"); room.Step();
                    check(fresh.Status == ModStateSubscriptionStatus.Disconnected && !fresh.Replica.Connected, "owner disconnect invalidates the old generation immediately");
                    room.Until(() => owner.Room.Joined && !owner.Room.Reconnecting && observer.Room.Players.Any(p => p.Id == owner.Room.LocalPlayerId && p.Connected), "owner reconnect");
                    room.Advance(2);
                    check(fresh.Status == ModStateSubscriptionStatus.Disconnected, "owner reconnect does not silently replay an old observation lease");
                    using (var reconnected = Observe(observer, binding))
                    { room.Until(() => reconnected.Status == ModStateSubscriptionStatus.Active, "explicit reconnect subscription"); check(reconnected.Replica.Current[0] == 6, "explicit new generation recovers the latest owner checkpoint after reconnect"); }
                }
            }
        }
    }
    private static void SessionBounds(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner); owner.PumpState = false;
            var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            using (var subscription = Observe(observer, binding))
            {
                room.Until(() => owner.Messages.Any(m => m.Kind == ModStateSessionKind.Subscribe), "request capture");
                var request = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe);
                var encoded = ModStateCodec.Encode(new ModStateFrame(subscription.Binding, 1, 0, true, new byte[ModStateFrame.MaximumSnapshotBytes]));
                for (int i = 0; i < 10; i++) owner.Room.SendApplication(ModStateSessionWire.Encode(new ModStateSessionMessage {
                    Kind = ModStateSessionKind.Chunk, Target = observer.Room.LocalPlayerId, Binding = request.Binding, Request = request.Request,
                    Transfer = Guid.NewGuid().ToString("N"), Digest = HashA, Total = encoded.Length,
                    Index = 0, Count = (encoded.Length + ModStateSessionWire.ChunkBytes - 1) / ModStateSessionWire.ChunkBytes,
                    Payload = encoded.Take(ModStateSessionWire.ChunkBytes).ToArray() }));
                room.Advance(.3);
                check(observer.State.IncompleteAssemblyCount == ModStateSession.MaximumAssembliesPerPeer && observer.State.IncompleteAssemblyBytes <= ModStateSession.MaximumAssemblyBytesPerPeer && observer.State.RejectedPackets >= 8,
                    "authenticated incomplete snapshots cannot exceed the per-peer reserved assembly budget");
                room.Advance(10.1);
                check(observer.State.IncompleteAssemblyCount == 0 && subscription.Replica.Current == null, "incomplete transfers expire without publishing partial state");
                owner.Room.SendApplication(owner.Raw.Last().Take(10).ToArray()); room.Advance(.2);
                check(owner.Room.Joined && observer.Room.Joined && room.Peers[0].Room.Joined, "malformed module traffic is isolated from the authenticated room protocol");
            }
            observer.Admitted = false;
            check(Throws<InvalidOperationException>(() => Observe(observer, binding)), "pre-entry mod admission gate also blocks runtime state subscriptions");
        }
    }
    private static void SessionConcurrency(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            room.Network.Settings.Delay = .15; room.Network.Settings.Jitter = .05; room.Network.Settings.Drop = .1;
            room.Network.Settings.Duplicate = .1; room.Network.Settings.Reorder = .1; room.Network.Settings.ReliableOrdered = true;
            var owner = room.Peers[1]; var observer = room.Peers[2]; var registrations = new List<IDisposable>(); var subscriptions = new List<ModStateSubscription>();
            for (int i = 0; i < ModStateSession.MaximumStreams; i++)
            {
                var binding = NativeBinding(owner, "stream" + i); var publisher = new ModStatePublisher(binding, new Reducer());
                var bytes = new byte[ModStateFrame.MaximumSnapshotBytes]; bytes[0] = (byte)(i + 1); publisher.PublishSnapshot(bytes);
                registrations.Add(owner.State.RegisterPublisher(publisher, _ => true)); subscriptions.Add(Observe(observer, binding));
            }
            room.Until(() => subscriptions.All(s => s.Status == ModStateSubscriptionStatus.Active), "sixteen large streams under reliable loss and delay", 90);
            check(subscriptions.Select((s, i) => s.Replica.Current.Length == ModStateFrame.MaximumSnapshotBytes && s.Replica.Current[0] == i + 1).All(v => v),
                "sixteen concurrent 64KiB streams converge over delayed, duplicated and retransmitted packets");
            room.Advance(12);
            check(observer.State.IncompleteAssemblyCount == 0 && room.Network.Retransmissions > 0 && room.Network.Duplicated > 0,
                "concurrent transfer scheduling eventually releases every reserved assembly under weak reliable links");
            foreach (var subscription in subscriptions) subscription.Dispose(); foreach (var registration in registrations) registration.Dispose();
        }
    }
    private static void SessionRequestOrdering(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            {
                var subscription = Observe(observer, binding); room.Until(() => subscription.Status == ModStateSubscriptionStatus.Active, "request ordering baseline");
                var request = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe);
                subscription.Dispose(); room.Advance(.3); int delivered = observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk);
                observer.Room.SendApplication(ModStateSessionWire.Encode(request)); publisher.PublishDelta(new byte[] { 3 }); room.Advance(3);
                check(observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk) == delivered,
                    "cancel tombstone prevents a delayed subscribe from reviving an old observation generation");
                for (int batch = 0; batch < 3; batch++)
                {
                    for (int i = 0; i < 90; i++)
                    {
                        var bogus = new ModStateBinding(binding.SessionId, "missing" + batch + "_" + i, binding.Channel, binding.AuthorityId, binding.Epoch, Guid.NewGuid().ToString("N"), 1);
                        observer.Room.SendApplication(ModStateSessionWire.Encode(new ModStateSessionMessage { Kind = ModStateSessionKind.Subscribe, Target = owner.Room.LocalPlayerId, Binding = bogus, Request = 1 }));
                    }
                    room.Advance(1.1);
                }
                using (var fresh = Observe(observer, binding))
                { room.Until(() => fresh.Status == ModStateSubscriptionStatus.Active, "valid request after unknown stream flood"); check(fresh.Replica.Current[0] == 4, "nonexistent stream spam cannot consume persistent subscription tombstone capacity"); }
                var wrongSchema = new ModStateBinding(binding.SessionId, binding.ModId, binding.Channel, binding.AuthorityId, binding.Epoch, Guid.NewGuid().ToString("N"), 2);
                using (var mismatched = observer.State.Subscribe(wrongSchema, new Reducer()))
                { room.Until(() => mismatched.Status == ModStateSubscriptionStatus.SchemaMismatch, "explicit wire schema rejection"); check(mismatched.Replica.Current == null, "wire schema disagreement is reported and never initialized as empty native state"); }
            }
        }
    }
    private static void SessionSlowProgress(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner); var publisher = new ModStatePublisher(binding, new Reducer());
            publisher.PublishSnapshot(new byte[ModStateFrame.MaximumSnapshotBytes]); owner.PumpState = false;
            using (owner.State.RegisterPublisher(publisher, _ => true))
            using (var subscription = Observe(observer, binding))
            {
                double began = room.Network.Now;
                for (int tick = 0; tick < 60 && subscription.Status != ModStateSubscriptionStatus.Active; tick++)
                { room.Advance(.6); owner.State.Pump(room.Network.Now); }
                check(subscription.Status == ModStateSubscriptionStatus.Active && room.Network.Now - began > 10 && subscription.Replica.Current.Length == ModStateFrame.MaximumSnapshotBytes,
                    "continuous slow transfer progress survives the idle timeout while every assembly still has a hard deadline");
            }
        }
    }
    private static void SessionGlobalBudget(Action<bool, string> check)
    {
        using (var room = new TestRoom(9))
        {
            var observer = room.Peers[0]; var registrations = new List<IDisposable>(); var subscriptions = new List<ModStateSubscription>(); bool sawFull = false;
            for (int i = 1; i < room.Peers.Length; i++) for (int stream = 0; stream < 2; stream++)
            {
                var owner = room.Peers[i]; var binding = NativeBinding(owner, "global" + stream); var publisher = new ModStatePublisher(binding, new Reducer());
                var bytes = new byte[ModStateFrame.MaximumSnapshotBytes]; bytes[0] = (byte)i; publisher.PublishSnapshot(bytes);
                registrations.Add(owner.State.RegisterPublisher(publisher, _ => true)); subscriptions.Add(Observe(observer, binding));
            }
            double limit = room.Network.Now + 90;
            while (room.Network.Now < limit && subscriptions.Any(s => s.Status != ModStateSubscriptionStatus.Active))
            {
                room.Step(); sawFull |= observer.State.IncompleteAssemblyCount >= ModStateSession.MaximumAssemblies - 1;
                if (observer.State.IncompleteAssemblyCount > ModStateSession.MaximumAssemblies || observer.State.IncompleteAssemblyBytes > ModStateSession.MaximumAssemblyBytes)
                    throw new Exception("global state assembly budget exceeded");
            }
            check(sawFull && subscriptions.All(s => s.Status == ModStateSubscriptionStatus.Active),
                "sixteen 64KiB transfers from eight authorities converge after contention for the global assembly budget");
            room.Advance(12);
            check(observer.State.IncompleteAssemblyCount == 0, "multi-owner recovery frees all incomplete transfer reservations");
            foreach (var subscription in subscriptions) subscription.Dispose(); foreach (var registration in registrations) registration.Dispose();
        }
    }
    private static void SessionLifetime(Action<bool, string> check)
    {
        using (var room = new TestRoom())
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 2 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            {
                for (int i = 0; i < 1100; i++) using (var subscription = Observe(observer, binding)) { }
                using (var live = Observe(observer, binding))
                { room.Until(() => live.Status == ModStateSubscriptionStatus.Active, "long-lived observation adapter"); check(live.Replica.Current[0] == 2, "repeated open/close does not permanently exhaust a long-lived SDK session"); }
            }
        }
    }
    private static byte[] StatePacket(ModStateSubscription watch, long request, string target, long revision, byte value)
    {
        byte[] frame = ModStateCodec.Encode(new ModStateFrame(watch.Binding, revision, 0, true, new byte[] { value }));
        return ModStateSessionWire.Encode(new ModStateSessionMessage { Kind = ModStateSessionKind.Chunk, Target = target, Binding = watch.Binding,
            Request = request, Transfer = Guid.NewGuid().ToString("N"), Digest = ModSyncValidation.Digest(frame), Total = frame.Length, Index = 0, Count = 1, Payload = frame });
    }
    private static byte[][] StateChunks(ModStateSubscription watch, long request, string target, long revision, byte[] snapshot)
    {
        byte[] frame = ModStateCodec.Encode(new ModStateFrame(watch.Binding, revision, 0, true, snapshot));
        string transfer = Guid.NewGuid().ToString("N"), digest = ModSyncValidation.Digest(frame);
        int count = (frame.Length + ModStateSessionWire.ChunkBytes - 1) / ModStateSessionWire.ChunkBytes;
        return Enumerable.Range(0, count).Select(index => ModStateSessionWire.Encode(new ModStateSessionMessage { Kind = ModStateSessionKind.Chunk,
            Target = target, Binding = watch.Binding, Request = request, Transfer = transfer, Digest = digest, Total = frame.Length, Index = index, Count = count,
            Payload = frame.Skip(index * ModStateSessionWire.ChunkBytes).Take(ModStateSessionWire.ChunkBytes).ToArray() })).ToArray();
    }
    private static void SessionAdmissionEpoch(Action<bool, string> check)
    {
        using (var room = new TestRoom(generationAware: true))
        {
            var host = room.Peers[0]; var owner = room.Peers[1]; var observer = room.Peers[2];
            var binding = NativeBinding(owner); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (var originalRegistration = owner.State.RegisterPublisher(publisher, _ => true))
            using (var watch = Observe(observer, binding))
            {
                room.Until(() => watch.Status == ModStateSubscriptionStatus.Active, "admission baseline");
                long request = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe).Request;
                foreach (var peer in room.Peers) peer.PumpState = false;
                byte[] snapshot = new byte[ModStateFrame.MaximumSnapshotBytes]; snapshot[0] = 99;
                var chunks = StateChunks(watch, request, observer.Room.LocalPlayerId, 2, snapshot);
                host.Room.ForwardApplication(owner.Room.LocalPlayerId, observer.Room.LocalPlayerId, chunks[0]); room.Advance(.15);
                check(observer.State.IncompleteAssemblyCount == 1 && watch.Replica.Revision == 1,
                    "old admitted transfer holds a real incomplete assembly before revalidation");
                // No endpoint ever observes Admitted=false, and State.Pump is not called.
                owner.AdmissionGeneration++;
                // This was already authenticated and queued by the relay before
                // the receiver learned the replacement admission token.
                foreach (var chunk in chunks.Skip(1)) host.Room.ForwardApplication(owner.Room.LocalPlayerId, observer.Room.LocalPlayerId, chunk);
                room.Advance(.3);
                check(watch.Status == ModStateSubscriptionStatus.Disconnected && watch.Replica.Revision == 1 && watch.Replica.Current[0] == 1 && observer.State.IncompleteAssemblyCount == 0,
                    "receive boundary retires a prior admitted authority even when bool stays true and State.Pump misses the transition");
                var replacementBinding = NativeBinding(owner); var replacement = new ModStatePublisher(replacementBinding, new Reducer()); replacement.PublishSnapshot(new byte[] { 7 });
                using (owner.State.RegisterPublisher(replacement, _ => true))
                {
                    originalRegistration.Dispose();
                    foreach (var peer in room.Peers) peer.PumpState = true;
                    using (var fresh = Observe(observer, replacementBinding))
                    {
                        room.Until(() => fresh.Status == ModStateSubscriptionStatus.Active, "fresh publisher after admission retirement");
                        publisher.PublishDelta(new byte[] { 4 }); room.Advance(.3);
                        check(fresh.Replica.Current[0] == 7 && fresh.Replica.Revision == 1,
                            "local revalidation requires a new publisher epoch and late disposal cannot remove its replacement");
                    }
                }
            }
        }
    }
    private static void SessionRelayAdmissionEpoch(Action<bool, string> check)
    {
        using (var room = new TestRoom(generationAware: true))
        {
            var host = room.Peers[0]; var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner);
            var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            using (var watch = Observe(observer, binding))
            {
                room.Until(() => watch.Status == ModStateSubscriptionStatus.Active, "relay admission baseline");
                var oldRequest = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe);
                int chunks = observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk);
                foreach (var peer in room.Peers) peer.PumpState = false;
                // Only the host knows the owner was readmitted; the observer has
                // not yet received that admission announcement.
                host.AdmissionView[owner.Room.LocalPlayerId] = 2;
                owner.Room.SendApplication(StatePacket(watch, oldRequest.Request, observer.Room.LocalPlayerId, 2, 99)); room.Advance(.3);
                observer.Room.SendApplication(ModStateSessionWire.Encode(oldRequest));
                owner.Room.SendApplication(StatePacket(watch, oldRequest.Request, observer.Room.LocalPlayerId, 3, 98)); room.Advance(.3);
                check(watch.Replica.Revision == 1 && watch.Replica.Current[0] == 1 && observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk) == chunks && host.State.RejectedPackets >= 2,
                    "host rejects prior-admission relay frames and renewals even before the observer learns the new token");
                owner.AdmissionGeneration = 2;
                var freshBinding = NativeBinding(owner); var freshPublisher = new ModStatePublisher(freshBinding, new Reducer()); freshPublisher.PublishSnapshot(new byte[] { 8 });
                using (owner.State.RegisterPublisher(freshPublisher, _ => true))
                using (var fresh = Observe(observer, freshBinding))
                {
                    foreach (var peer in room.Peers) peer.PumpState = true;
                    room.Until(() => fresh.Status == ModStateSubscriptionStatus.Active, "relay replacement request");
                    check(fresh.Replica.Current[0] == 8, "a fresh subscription and publisher epoch can replace a retired relay route");
                }
            }
        }
    }
    private static void SessionObserverAdmissionEpoch(Action<bool, string> check)
    {
        using (var room = new TestRoom(generationAware: true))
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; var binding = NativeBinding(owner); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
            using (owner.State.RegisterPublisher(publisher, _ => true))
            using (var watch = Observe(observer, binding))
            {
                room.Until(() => watch.Status == ModStateSubscriptionStatus.Active, "observer admission baseline");
                var request = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe); int chunks = observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk);
                foreach (var peer in room.Peers) peer.PumpState = false;
                owner.AdmissionView[observer.Room.LocalPlayerId] = 2;
                observer.Room.SendApplication(ModStateSessionWire.Encode(request)); room.Advance(.3);
                publisher.PublishDelta(new byte[] { 2 }); owner.State.Pump(room.Network.Now); room.Advance(.3);
                check(observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk) == chunks && watch.Replica.Current[0] == 1,
                    "observer revalidation retires owner-side lease and its cancelled request cannot revive it");
                watch.Dispose();
                using (var fresh = Observe(observer, binding))
                {
                    foreach (var peer in room.Peers) peer.PumpState = true;
                    room.Until(() => fresh.Status == ModStateSubscriptionStatus.Active, "fresh observer request after admission");
                    check(fresh.Replica.Current[0] == 3, "fresh observer request restores state without requiring an unrelated owner restart");
                }
            }
        }
    }
    private static void SessionRelayLifetime(Action<bool, string> check)
    {
        using (var room = new TestRoom(generationAware: true))
        {
            var owner = room.Peers[1]; var observer = room.Peers[2]; ModStateSessionMessage firstRequest = null; byte[] firstChunk = null;
            for (int i = 0; i < 270; i++)
            {
                var binding = NativeBinding(owner, "churn" + i); var publisher = new ModStatePublisher(binding, new Reducer()); publisher.PublishSnapshot(new byte[] { 1 });
                using (owner.State.RegisterPublisher(publisher, _ => true))
                using (var watch = Observe(observer, binding))
                {
                    room.Until(() => watch.Status == ModStateSubscriptionStatus.Active, "relay stream churn " + i);
                    if (i == 0) { firstRequest = owner.Messages.Last(m => m.Kind == ModStateSessionKind.Subscribe); firstChunk = observer.Raw.Last(p => ModStateSessionWire.Decode(p).Kind == ModStateSessionKind.Chunk); }
                }
                room.Advance(.1);
            }
            int delivered = observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk);
            observer.Room.SendApplication(ModStateSessionWire.Encode(firstRequest)); owner.Room.SendApplication(firstChunk); room.Advance(.3);
            check(observer.Messages.Count(m => m.Kind == ModStateSessionKind.Chunk) == delivered,
                "relay tombstone compaction permits long-lived stream churn without reviving an evicted old request");
        }
    }
}
