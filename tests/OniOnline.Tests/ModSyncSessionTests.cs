using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;
using OniOnline.Sdk;

internal static class ModSyncSessionTests
{
    private const int Magic = 0x59534d4f;
    private sealed class Fixture : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(81253);
        internal readonly RoomSession[] Rooms;
        internal readonly ModSyncSession[] Sync;
        internal readonly DeterministicRoomNetwork.Endpoint[] Transports;
        internal readonly List<byte[]> Frames = new List<byte[]>();
        internal Fixture(params ModManifest[] manifests) : this(manifests, true) { }
        internal Fixture(ModManifest[] manifests, bool advertiseSync, Func<int, SessionProfile> profiles = null)
        {
            Rooms = new RoomSession[manifests.Length]; Sync = new ModSyncSession[manifests.Length];
            Transports = new DeterministicRoomNetwork.Endpoint[manifests.Length];
            Network.Settings.DropBeforeDelivery = (fromHost, frame) => { Frames.Add((byte[])frame.Clone()); return false; };
            for (int i = 0; i < manifests.Length; i++)
            {
                int index = i;
                Transports[i] = i == 0 ? Network.Host : (DeterministicRoomNetwork.Endpoint)Network.Connect();
                Rooms[i] = new RoomSession(Transports[i], profiles == null ? Profile("Sync" + i, advertiseSync) : profiles(i), "", "Mod-sync fixture",
                    reconnect: i == 0 ? null : () => Transports[index] = (DeterministicRoomNetwork.Endpoint)Network.Connect(),
                    options: new SessionOptions { RetryJitter = () => .5 });
                Sync[i] = new ModSyncSession(Rooms[i], manifests[i]);
            }
            Until(() => Rooms.All(r => r.Joined && r.Players.Length == manifests.Length));
        }
        internal void Step(double seconds = .01)
        {
            Network.Advance(seconds);
            foreach (var sync in Sync) sync.Pump(Network.Now);
            foreach (var room in Rooms) room.Pump(Network.Now);
        }
        internal void Until(Func<bool> condition, double timeout = 30)
        {
            double deadline = Network.Now + timeout;
            while (Network.Now < deadline) { Step(); if (condition()) return; }
            throw new Exception("Mod-sync fixture timed out");
        }
        internal bool Ready { get { return Sync.All(s => s.LocalReady) && Rooms.Skip(1).All(r => Sync[0].IsReady(r.LocalPlayerId)); } }
        public void Dispose()
        {
            foreach (var sync in Sync) sync.Dispose();
            foreach (var room in Rooms.Reverse()) room.Dispose();
            Network.Dispose();
        }
    }
    private static SessionProfile Profile(string name, bool advertiseSync = true)
    {
        var capabilities = advertiseSync ? SessionCapabilities.Core.Concat(new[] { SessionCapabilities.ModSynchronizationV1 }).ToArray() : SessionCapabilities.Core;
        return new SessionProfile { Name = name, Capabilities = capabilities, RequiredCapabilities = capabilities };
    }
    private static ModManifest Manifest(int count = 1, string version = "1", ModSyncPolicy extra = ModSyncPolicy.Required)
    {
        return new ModManifest(Enumerable.Range(0, count).Select(i => new ModManifestEntry("test.mod." + i.ToString("D3"),
            version, i == 0 ? ModSyncPolicy.Required : extra, ModDistributionSource.SteamWorkshop,
            (ulong)i + 100, new string('a', 64))));
    }
    private static bool Application(byte[] bytes, out Packet packet)
    {
        try { packet = Protocol.Decode(bytes); return packet.Kind == PacketKind.Application && packet.Payload.Length >= 6 && BitConverter.ToInt32(packet.Payload, 0) == Magic; }
        catch (Exception) { packet = null; return false; }
    }
    private static bool Kind(byte[] bytes, int kind)
    { return Application(bytes, out var packet) && packet.Payload[4] == 2 && packet.Payload[5] == kind; }
    private static byte[] Payload(Fixture room, bool fromHost, int kind) { return PayloadFrom(room, fromHost ? 0 : 1, kind); }
    private static byte[] PayloadFrom(Fixture room, int index, int kind)
    {
        return room.Frames.Select(bytes => { Application(bytes, out var packet); return packet; })
            .Where(p => p != null && p.Kind == PacketKind.Application && p.PlayerId == room.Rooms[index].LocalPlayerId &&
                p.Payload.Length >= 6 && BitConverter.ToInt32(p.Payload, 0) == Magic && p.Payload[5] == kind)
            .Select(p => p.Payload).First();
    }
    internal static void Run(Action<bool, string> check)
    {
        Alignment(check);
        PagingAndAcknowledgements(check);
        ReplayAndMalformedFrames(check);
        Reconnect(check);
        OnlineConnectionReplacement(check);
        CapabilityGate(check);
        CrossGuestAdmission(check);
        SameConnectionReadmission(check);
        NativeCapabilityAnnouncements(check);
        AssemblyBudgets(check);
        Fairness(check);
        PagedAdmission(check);
        Console.WriteLine("MOD-SYNC SESSION: authenticated production RoomSession transport, manifest barrier, paging, recovery and reconnect generations. No Steam/native loader claim.");
    }
    private static void Alignment(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(), Manifest()))
        {
            check(room.Sync[0].LocalReady, "host may run its own loaded manifest while waiting for peers");
            room.Until(() => room.Ready);
            check(room.Sync[1].PendingPlan.CanJoin && room.Sync[0].GetPlan(room.Rooms[1].LocalPlayerId).CanJoin,
                "both authenticated peers compare manifests before readiness");
            check(!room.Sync[0].IsReady("unknown") && room.Sync[0].IsReady(room.Rooms[0].LocalPlayerId),
                "readiness is scoped to the current authenticated endpoint");
            int changed = 0; room.Sync[1].Changed += () => { changed++; throw new Exception("plugin"); };
            room.Network.CutAll(); room.Step();
            check(!room.Sync[1].LocalReady && !room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId) && changed > 0,
                "disconnect synchronously removes readiness despite callback failures");
        }
        foreach (var variant in new[] { Manifest(version: "2"), Manifest(2) })
        using (var room = new Fixture(Manifest(), variant))
        {
            room.Until(() => room.Sync[1].PendingPlan != null && room.Sync[0].GetPlan(room.Rooms[1].LocalPlayerId) != null);
            check(!room.Sync[1].LocalReady && !room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId),
                "different versions and extra active Required mods cannot cross admission barrier");
            check(room.Sync[1].PendingPlan.Changes.Count == 1 && !room.Sync[1].PendingPlan.CanJoin,
                "native download review receives the concrete incompatible manifest plan");
        }
        using (var room = new Fixture(Manifest(), Manifest(2, extra: ModSyncPolicy.ClientOnly)))
        {
            room.Until(() => room.Ready);
            check(room.Sync[1].PendingPlan.CanJoin, "declared client-only additions do not block the authority requirements");
        }
        using (var room = new Fixture(Manifest(), Manifest(), Manifest()))
        {
            room.Until(() => room.Ready);
            check(!room.Sync[1].IsReady(room.Rooms[2].LocalPlayerId), "guests do not infer another guest's admission from roster membership");
            room.Sync[1].Dispose();
            check(!room.Sync[1].LocalReady && !room.Sync[1].IsReady(room.Rooms[1].LocalPlayerId), "disposed barrier cannot authorize a later session");
        }
    }
    private static void PagingAndAcknowledgements(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(256), Manifest(256)))
        {
            int dropped = 0;
            room.Network.Settings.DropBeforeDelivery = (fromHost, frame) => {
                if (Kind(frame, 2) && Application(frame, out var packet) && BitConverter.ToInt32(packet.Payload, 58) == 3072 && dropped++ == 0) return true;
                return false;
            };
            room.Until(() => room.Ready);
            check(dropped > 0 && room.Sync[1].PendingPlan.RequiredManifest.Entries.Count == 256,
                "a missing middle page recovers through bounded manifest retransmission");
        }
        foreach (bool dropFromHost in new[] { false, true })
        using (var room = new Fixture(Manifest(32), Manifest(32)))
        {
            int dropped = 0;
            room.Network.Settings.DropBeforeDelivery = (fromHost, frame) => fromHost == dropFromHost && Kind(frame, 3) && dropped++ < 2;
            room.Until(() => room.Ready);
            check(dropped >= 2, "one-sided lost Accepted acknowledgements recover even after the other side became ready");
        }
        using (var room = new Fixture(Manifest(256), Manifest(256)))
        {
            room.Network.Settings.Delay = .03; room.Network.Settings.Jitter = .02;
            room.Network.Settings.Duplicate = .2; room.Network.Settings.Reorder = .25;
            room.Until(() => room.Ready, 90);
            check(room.Network.Duplicated > 0 && room.Network.Reordered > 0,
                "out-of-order and duplicate actual application frames cannot corrupt paged manifests");
        }
    }
    private static void ReplayAndMalformedFrames(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(), Manifest()))
        {
            room.Until(() => room.Ready);
            byte[] offer = Payload(room, false, 1), page = Payload(room, false, 2), accepted = Payload(room, false, 3);
            byte[] replacement = (byte[])offer.Clone(); Guid.NewGuid().ToByteArray().CopyTo(replacement, 22);
            check(room.Rooms[1].SendApplication(replacement), "new generation offer uses the authenticated application channel");
            room.Step();
            check(!room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId) && room.Sync[0].GetPlan(room.Rooms[1].LocalPlayerId) == null,
                "remote nonce replacement invalidates readiness and the old review plan immediately");
            room.Rooms[1].SendApplication(offer); room.Rooms[1].SendApplication(page); room.Rooms[1].SendApplication(accepted); room.Step();
            check(!room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId), "retired offer, pages and accepted frames cannot restore a previous generation");
        }
        using (var room = new Fixture(Manifest(), Manifest()))
        {
            room.Until(() => room.Ready);
            int changes = 0; room.Sync[0].Changed += () => changes++;
            byte[] page = Payload(room, false, 2);
            var malformed = new List<byte[]>();
            var unknown = (byte[])page.Clone(); unknown[5] = 255; malformed.Add(unknown);
            malformed.Add(page.Concat(new byte[] { 0 }).ToArray());
            var oversized = (byte[])page.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(oversized, 54); malformed.Add(oversized);
            var wrongRecipient = (byte[])page.Clone(); Guid.NewGuid().ToByteArray().CopyTo(wrongRecipient, 38); malformed.Add(wrongRecipient);
            malformed.Add(page.Take(40).ToArray());
            foreach (byte[] frame in malformed) room.Rooms[1].SendApplication(frame);
            room.Step();
            check(room.Ready && changes == 0 && room.Rooms.All(r => !r.Closed),
                "malformed/trailing/unknown/oversized/stale-recipient frames leave valid admission and room lifetime intact");
        }
    }
    private static void Reconnect(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(32), Manifest(32)))
        {
            room.Until(() => room.Ready);
            string identity = room.Rooms[1].LocalPlayerId;
            byte[] oldHostOffer = Payload(room, true, 1), oldHostPage = Payload(room, true, 2), oldHostAccepted = Payload(room, true, 3);
            room.Network.CutAll(); room.Step();
            check(!room.Sync[1].LocalReady && room.Sync[1].PendingPlan == null, "reconnecting client discards the former loaded-plan binding");
            room.Until(() => room.Rooms[1].Joined);
            room.Network.InjectHostFrame(room.Transports[1], Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = room.Rooms[0].LocalPlayerId, Payload = oldHostOffer }), 0);
            room.Network.InjectHostFrame(room.Transports[1], Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = room.Rooms[0].LocalPlayerId, Payload = oldHostPage }), 0);
            room.Network.InjectHostFrame(room.Transports[1], Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = room.Rooms[0].LocalPlayerId, Payload = oldHostAccepted }), 0);
            room.Step();
            check(!room.Sync[1].LocalReady, "replayed prior-connection manifest and ACK cannot admit a resumed seat");
            room.Until(() => room.Ready);
            check(room.Rooms[1].LocalPlayerId == identity, "same player identity is re-admitted only after a fresh nonce exchange");
        }
    }
    private static void OnlineConnectionReplacement(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(), Manifest()))
        {
            room.Until(() => room.Ready);
            string player = room.Rooms[1].LocalPlayerId;
            long generation = room.Rooms[0].ConnectionGenerationFor(player);
            byte[] oldPage = Payload(room, false, 2), oldAccepted = Payload(room, false, 3);
            var firstHello = room.Frames.Select(Protocol.Decode).First(p => p.Kind == PacketKind.Hello);
            var replacement = (DeterministicRoomNetwork.Endpoint)room.Network.Connect();
            room.Network.Advance(.01); room.Rooms[0].Pump(room.Network.Now); room.Network.Advance(.01);
            Packet challenge = null; TransportEvent item;
            while (replacement.TryReceive(out item))
                if (item.Kind == TransportEventKind.Message)
                { var packet = Protocol.Decode(item.Data); if (packet.Kind == PacketKind.Challenge) challenge = packet; }
            if (challenge == null) throw new Exception("replacement challenge was not delivered");
            var profile = Profile("Sync1");
            var hello = new Packet { Kind = PacketKind.Hello, Name = profile.Name, GameVersion = "", CycleTrimVersion = "",
                GameplayId = profile.GameplayId, GameplayVersion = profile.GameplayVersion, Capabilities = profile.Capabilities,
                RequiredCapabilities = profile.RequiredCapabilities, Nonce = Protocol.NewNonce(), PlayerId = player, ResumeToken = "" };
            hello.ResumeProof = Protocol.ResumeProof(firstHello.ResumeToken, challenge.Nonce, hello);
            hello.Proof = Protocol.PasswordProof("", challenge.Nonce, hello);
            replacement.Send("host", Protocol.Encode(hello));
            room.Network.Advance(.01); room.Rooms[0].Pump(room.Network.Now);
            check(room.Rooms[0].Players.Single(p => p.Id == player).Connected && room.Rooms[0].ConnectionGenerationFor(player) > generation,
                "same-seat online connection replacement changes authentication generation without a disconnected roster");
            check(!room.Sync[0].IsReady(player) && room.Sync[0].GetPlan(player) == null,
                "online connection replacement cannot inherit an earlier connection's mod admission");
            foreach (byte[] old in new[] { oldPage, oldAccepted }) replacement.Send("host", Protocol.Encode(new Packet {
                Kind = PacketKind.Application, PlayerId = player, Payload = old }));
            room.Network.Advance(.01); room.Rooms[0].Pump(room.Network.Now);
            check(!room.Sync[0].IsReady(player), "old manifest pages and acknowledgements on a newly authenticated connection remain unauthorized");
        }
    }
    private static void CapabilityGate(Action<bool, string> check)
    {
        using (var room = new Fixture(new[] { Manifest(), Manifest() }, false))
        {
            for (int i = 0; i < 150; i++) room.Step();
            check(!room.Sync[1].LocalReady && !room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId) &&
                !room.Frames.Any(frame => Application(frame, out _)),
                "an unnegotiated mod-sync capability cannot silently become ready or emit private manifest data");
        }
        using (var room = new Fixture(Manifest(), Manifest()))
        {
            room.Until(() => room.Ready);
            byte[] offer = Payload(room, false, 1);
            for (int i = 0; i < 66; i++)
            {
                byte[] changed = (byte[])offer.Clone(); Guid.NewGuid().ToByteArray().CopyTo(changed, 22);
                room.Rooms[1].SendApplication(changed);
            }
            room.Step();
            check(!room.Sync[0].IsReady(room.Rooms[1].LocalPlayerId) && room.Sync[0].GetPlan(room.Rooms[1].LocalPlayerId) == null,
                "nonce churn exhausts a bounded retirement budget and fails closed for this connection");
        }
    }
    private static void CrossGuestAdmission(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(), Manifest(), Manifest()))
        {
            string a = room.Rooms[1].LocalPlayerId, b = room.Rooms[2].LocalPlayerId;
            room.Until(() => room.Ready && room.Sync[1].IsReady(b) && room.Sync[2].IsReady(a));
            long generation = room.Sync[1].AdmissionGenerationFor(b);
            check(generation == room.Sync[0].AdmissionGenerationFor(b) && generation > 0,
                "authenticated host admission announces one guest's current admission epoch to another guest");
            byte[] aNonce = PayloadFrom(room, 1, 1).Skip(22).Take(16).ToArray();
            byte[] oldAnnouncement = room.Frames.Select(bytes => { Application(bytes, out var packet); return packet; })
                .Where(p => p != null && p.Kind == PacketKind.Application && p.Payload.Length >= 54 && p.Payload[5] == 4 &&
                    p.Payload.Skip(38).Take(16).SequenceEqual(aNonce))
                .Select(p => p.Payload).Last();
            byte[] offer = (byte[])PayloadFrom(room, 2, 1).Clone(); Guid.NewGuid().ToByteArray().CopyTo(offer, 22);
            room.Rooms[2].SendApplication(offer); room.Step();
            check(!room.Sync[0].IsReady(b), "host revokes a changed guest manifest binding synchronously");
            room.Until(() => !room.Sync[1].IsReady(b));
            check(room.Sync[1].AdmissionGenerationFor(b) == 0, "host revision revokes cross-guest authority on the observer");
            room.Network.InjectHostFrame(room.Transports[1], Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = room.Rooms[0].LocalPlayerId, Payload = oldAnnouncement }), 0);
            room.Rooms[2].SendApplication(oldAnnouncement); room.Step();
            check(!room.Sync[1].IsReady(b), "old host announcements and guest-forged announcements cannot restore revoked admission");
            room.Transports[2].Disconnect("host"); room.Step();
            room.Until(() => room.Ready && room.Sync[1].IsReady(b));
            check(room.Sync[1].AdmissionGenerationFor(b) > generation,
                "guest reconnect is announced only after revalidation with a fresh host-authenticated generation");
            room.Network.Settings.DropBeforeDelivery = (fromHost, bytes) => fromHost && Kind(bytes, 4);
            room.Transports[1].Disconnect("host"); room.Step();
            check(!room.Sync[1].LocalReady && room.Sync[1].AdmissionGenerationFor(b) == 0,
                "observer disconnect clears every cross-guest admission immediately");
            room.Until(() => room.Sync[1].LocalReady);
            room.Network.InjectHostFrame(room.Transports[1], Protocol.Encode(new Packet { Kind = PacketKind.Application,
                PlayerId = room.Rooms[0].LocalPlayerId, Payload = oldAnnouncement }), 0); room.Step();
            check(!room.Sync[1].IsReady(b), "prior-observer nonce announcement cannot authorize a resumed connection");
            room.Network.Settings.DropBeforeDelivery = null;
            room.Until(() => room.Sync[1].IsReady(b));
            check(room.Sync[1].AdmissionGenerationFor(b) == room.Sync[0].AdmissionGenerationFor(b),
                "periodic host announcement retransmission recovers lost admission pages");
        }
    }
    private static void SameConnectionReadmission(Action<bool, string> check)
    {
        using (var room = new Fixture(Manifest(), Manifest(), Manifest()))
        {
            string b = room.Rooms[2].LocalPlayerId;
            room.Until(() => room.Ready && room.Sync[1].IsReady(b));
            long oldEpoch = room.Sync[1].AdmissionGenerationFor(b), connection = room.Rooms[0].ConnectionGenerationFor(b);
            byte[] oldPage = PayloadFrom(room, 2, 2), oldAck = PayloadFrom(room, 2, 3);
            string nonce = Guid.NewGuid().ToString("N");
            byte[] offer = PayloadFrom(room, 2, 1); new Guid(nonce).ToByteArray().CopyTo(offer, 22);
            byte[] ack = (byte[])oldAck.Clone(); new Guid(nonce).ToByteArray().CopyTo(ack, 22);
            byte[] manifest = ModManifestCodec.Encode(Manifest());
            room.Rooms[2].SendApplication(offer);
            room.Rooms[2].SendApplication(Page(oldPage, nonce, manifest.Length, 0, manifest));
            room.Rooms[2].SendApplication(ack); room.Step();
            check(room.Sync[0].IsReady(b) && room.Rooms[0].ConnectionGenerationFor(b) == connection &&
                room.Sync[0].AdmissionGenerationFor(b) > oldEpoch,
                "same-connection revoke and readmit within one room pump allocates a fresh admission epoch");
            room.Until(() => room.Sync[1].AdmissionGenerationFor(b) == room.Sync[0].AdmissionGenerationFor(b));
            check(room.Sync[1].AdmissionGenerationFor(b) > oldEpoch,
                "observer retires the prior authorization epoch even when it never received the intermediate revocation");
        }
    }
    private static void NativeCapabilityAnnouncements(Action<bool, string> check)
    {
        Func<int, SessionProfile> profiles = index => {
            var profile = Profile("Caps" + index);
            string[] native = index == 0 ? new[] { SessionCapabilities.NativePlanetV1, SessionCapabilities.NativeObservationV1, SessionCapabilities.NativeStarMapV1 }
                : index == 1 ? new[] { SessionCapabilities.NativePlanetV1, SessionCapabilities.NativeObservationV1 }
                : new[] { SessionCapabilities.NativePlanetV1 };
            profile.Capabilities = profile.Capabilities.Concat(native).ToArray();
            return profile;
        };
        using (var room = new Fixture(new[] { Manifest(), Manifest(), Manifest() }, true, profiles))
        {
            string host = room.Rooms[0].LocalPlayerId, a = room.Rooms[1].LocalPlayerId, b = room.Rooms[2].LocalPlayerId;
            room.Until(() => room.Ready && room.Sync[1].IsReady(b) && room.Sync[2].IsReady(a));
            check(room.Sync[1].SupportsNativeCapability(b, SessionCapabilities.NativePlanetV1) &&
                !room.Sync[1].SupportsNativeCapability(b, SessionCapabilities.NativeObservationV1),
                "guest native feature support comes from the host's authenticated remote profile announcement");
            check(!room.Sync[2].SupportsNativeCapability(a, SessionCapabilities.NativeObservationV1) &&
                !room.Sync[1].SupportsNativeCapability(host, SessionCapabilities.NativeStarMapV1),
                "announced remote native support is intersected with this endpoint's own supported contracts");
            check(room.Sync[1].SupportsNativeCapability(host, SessionCapabilities.NativeObservationV1) &&
                !room.Sync[1].SupportsNativeCapability(a, SessionCapabilities.NativeStarMapV1) &&
                !room.Sync[1].SupportsNativeCapability(b, SessionCapabilities.ModSynchronizationV1),
                "native capability API verifies self and host contracts and refuses arbitrary capability names");
            byte[] offer = PayloadFrom(room, 2, 1); Guid.NewGuid().ToByteArray().CopyTo(offer, 22);
            room.Rooms[2].SendApplication(offer); room.Step(); room.Until(() => !room.Sync[1].IsReady(b));
            check(!room.Sync[1].SupportsNativeCapability(b, SessionCapabilities.NativePlanetV1),
                "revoked guest admission cannot retain an advertised native capability");
        }
    }
    private static byte[] Page(byte[] template, string nonce, int total, int offset, byte[] chunk)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
        {
            byte[] header = template.Take(54).ToArray(); new Guid(nonce).ToByteArray().CopyTo(header, 22);
            writer.Write(header); writer.Write(total); writer.Write(offset); writer.Write((ushort)chunk.Length); writer.Write(chunk);
            return stream.ToArray();
        }
    }
    private static void AssemblyBudgets(Action<bool, string> check)
    {
        using (var room = new Fixture(Enumerable.Repeat(Manifest(), 34).ToArray()))
        {
            room.Until(() => room.Ready);
            var templates = Enumerable.Range(1, 33).Select(i => PayloadFrom(room, i, 2)).ToArray();
            var nonces = Enumerable.Range(1, 33).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
            for (int i = 1; i < room.Sync.Length; i++) room.Sync[i].Dispose();
            for (int i = 1; i < room.Rooms.Length; i++)
            {
                byte[] offer = templates[i - 1].Take(38).ToArray(); offer[5] = 1;
                new Guid(nonces[i - 1]).ToByteArray().CopyTo(offer, 22);
                room.Rooms[i].SendApplication(offer);
                room.Rooms[i].SendApplication(Page(templates[i - 1], nonces[i - 1], ModManifestCodec.MaximumEncodedBytes, 0, new byte[3072]));
            }
            room.Step();
            byte[] valid = ModManifestCodec.Encode(Manifest()); string last = room.Rooms[33].LocalPlayerId;
            room.Rooms[33].SendApplication(Page(templates[32], nonces[32], valid.Length, 0, valid)); room.Step();
            check(room.Sync[0].GetPlan(last) == null, "thirty-three maximum-sized partial manifests cannot exceed the global eight-MiB assembly budget");
            int maximum = ModManifestCodec.MaximumEncodedBytes;
            for (int offset = 3072; offset < maximum; offset += 3072)
                room.Rooms[1].SendApplication(Page(templates[0], nonces[0], maximum, offset, new byte[Math.Min(3072, maximum - offset)]));
            room.Step();
            room.Rooms[33].SendApplication(Page(templates[32], nonces[32], valid.Length, 0, valid)); room.Step();
            check(room.Sync[0].GetPlan(last)?.CanJoin == true && room.Rooms.All(r => !r.Closed),
                "failed manifest decoding releases assembly memory so a waiting authenticated peer can progress");
        }
    }
    private static void PagedAdmission(Action<bool, string> check)
    {
        using (var room = new Fixture(Enumerable.Repeat(Manifest(), 130).ToArray()))
        {
            string last = room.Rooms[129].LocalPlayerId;
            room.Until(() => room.Ready && room.Sync[1].IsReady(last) && room.Sync[129].IsReady(room.Rooms[1].LocalPlayerId), 120);
            check(room.Sync[1].AdmissionGenerationFor(last) == room.Sync[0].AdmissionGenerationFor(last),
                "a host ready roster larger than one page publishes atomically to actual authenticated guests");
            check(room.Frames.Any(frame => Application(frame, out var packet) && packet.Payload[5] == 4 &&
                BitConverter.ToInt32(packet.Payload, 66) == 128), "production host sends the second bounded admission page");
        }
    }
    private static void Fairness(Action<bool, string> check)
    {
        using (var room = new Fixture(Enumerable.Repeat(Manifest(256), 41).ToArray()))
        {
            for (int step = 0; step < 20000 && !room.Ready; step++)
            {
                room.Step();
            }
            check(room.Ready, "forty peers with paged manifests eventually receive admission without starvation");
            check(room.Network.PeakScheduled <= DeterministicRoomNetwork.QueueLimit && room.Network.Refused == 0,
                "forty-peer admission stays inside the deterministic transport queue budget");
        }
    }
}
