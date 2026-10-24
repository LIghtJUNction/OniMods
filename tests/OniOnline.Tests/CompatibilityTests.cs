using System;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Network;

internal static class CompatibilityTests
{
    private const string Producer = "test.observation-producer.v1";
    private sealed class Fixture : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(28421);
        internal readonly RoomSession Host;
        internal readonly RoomSession Peer;
        internal Fixture(SessionProfile host, SessionProfile peer, Action afterConstruction = null)
        {
            Host = new RoomSession(Network.Host, host, "secret", "Compatibility fixture");
            Peer = new RoomSession(Network.Connect(), peer, "secret");
            if (afterConstruction != null) afterConstruction();
            for (int i = 0; i < 500 && !Peer.Closed && !(Peer.Joined && Host.Players.Length == 2); i++)
            { Network.Advance(.01); Host.Pump(Network.Now); Peer.Pump(Network.Now); }
            if (!Peer.Joined && !Peer.Closed) throw new Exception("compatibility handshake timed out");
        }
        public void Dispose() { Peer.Dispose(); Host.Dispose(); Network.Dispose(); }
    }

    private static SessionProfile Profile(string name = "Player")
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    internal static void Run(Action<bool, string> check)
    {
        var host = Profile("Host"); var peer = Profile("Peer");
        peer.GameVersion = "different-build"; peer.CycleTrimVersion = null; peer.SpacedOut = false;
        using (var fixture = new Fixture(host, peer))
            check(fixture.Peer.Joined && fixture.Host.Players.Length == 2,
                "game build, absent CycleTrim and base-game metadata do not block a compatible protocol");

        host = Profile("Host"); peer = Profile("Peer");
        host.GameVersion = "host-build"; host.CycleTrimVersion = "host-cycletrim"; host.SpacedOut = true;
        peer.GameVersion = "peer-build"; peer.CycleTrimVersion = ""; peer.SpacedOut = false;
        using (var fixture = new Fixture(host, peer))
        {
            string hostId = fixture.Host.LocalPlayerId, peerId = fixture.Peer.LocalPlayerId;
            var hostView = fixture.Peer.HostEnvironment;
            var peerView = fixture.Host.EnvironmentFor(peerId);
            bool immutable = false;
            try { ((System.Collections.Generic.IList<string>)hostView.Capabilities)[0] = "mutated"; }
            catch (NotSupportedException) { immutable = true; }
            check(hostView != null && hostView.GameVersion == "host-build" && hostView.CycleTrimVersion == "host-cycletrim" && hostView.SpacedOut,
                "Welcome returns the host game and DLC description without making it an admission gate");
            check(peerView != null && peerView.GameVersion == "peer-build" && peerView.CycleTrimVersion == "" && !peerView.SpacedOut,
                "the host can inspect an authenticated peer environment separately from negotiated capabilities");
            check(immutable && fixture.Peer.HostEnvironment.Capabilities[0] != "mutated" &&
                fixture.Peer.EnvironmentFor("other-peer") == null && fixture.Peer.LocalEnvironment.GameVersion == "peer-build",
                "environment snapshots are immutable and guests cannot infer another guest's environment");
            fixture.Peer.Dispose();
            check(fixture.Peer.HostEnvironment == null && fixture.Peer.EnvironmentFor(hostId) == null,
                "closing a session withdraws environment observations");
        }

        host = Profile("Host"); peer = Profile("Peer");
        host.GameVersion = null; host.CycleTrimVersion = ""; host.SpacedOut = false;
        host.Capabilities = host.Capabilities.Concat(new[] { Producer }).ToArray();
        peer.RequiredCapabilities = peer.RequiredCapabilities.Concat(new[] { Producer }).ToArray();
        using (var fixture = new Fixture(host, peer))
        {
            string hostId = fixture.Host.LocalPlayerId, peerId = fixture.Peer.LocalPlayerId;
            check(fixture.Peer.Joined, "a consumer may require a host producer capability it does not provide locally");
            check(fixture.Peer.PeerCapabilitiesFor(hostId).Contains(Producer) && !fixture.Peer.CanUseCapabilityWith(hostId, Producer),
                "remote advertised contracts are distinct from shared bidirectional contracts");
            check(!fixture.Host.PeerCapabilitiesFor(peerId).Contains(Producer), "host sees actual guest producer capabilities");
            var view = fixture.Peer.PeerCapabilitiesFor(hostId); view[0] = "test.mutated";
            check(!fixture.Peer.PeerCapabilitiesFor(hostId).Contains("test.mutated"), "remote capability snapshots are defensive");
            check(fixture.Peer.CapabilitiesFor("unrelated-player").Length == 0,
                "the roster cannot imply capabilities for an unauthenticated endpoint");
            fixture.Peer.Dispose();
            check(fixture.Peer.PeerCapabilitiesFor(hostId).Length == 0 && !fixture.Peer.CanUseCapabilityWith(hostId, SessionCapabilities.SessionV1),
                "closed sessions expose no usable capability binding");
        }

        host = Profile("Host"); peer = Profile("Peer");
        host.RequiredCapabilities = host.RequiredCapabilities.Concat(new[] { Producer }).ToArray();
        using (var fixture = new Fixture(host, peer))
        {
            check(fixture.Peer.Closed && fixture.Peer.LastError == "capability_mismatch" && fixture.Host.Players.Length == 1,
                "host-required contracts reject a guest before allocating a seat");
            check(fixture.Peer.LastCompatibilityFailure.MissingAtPeer.SequenceEqual(new[] { Producer }) &&
                fixture.Peer.LastCompatibilityFailure.MissingAtHost.Length == 0,
                "rejection reports exactly which host requirement is absent at the guest");
        }

        host = Profile("Host"); peer = Profile("Peer");
        peer.RequiredCapabilities = peer.RequiredCapabilities.Concat(new[] { Producer }).ToArray();
        using (var fixture = new Fixture(host, peer))
            check(fixture.Peer.Closed && fixture.Host.Players.Length == 1 &&
                fixture.Peer.LastCompatibilityFailure.MissingAtHost.SequenceEqual(new[] { Producer }),
                "guest-required contracts reject a host without the producer and retain diagnostics");

        host = Profile("Host"); peer = Profile("Peer");
        var originalCapabilities = host.Capabilities; var originalRequirements = peer.RequiredCapabilities;
        using (var fixture = new Fixture(host, peer, () =>
        { originalCapabilities[0] = "test.mutated"; originalRequirements[0] = "test.mutated"; }))
            check(fixture.Peer.Joined && fixture.Peer.CanUseCapabilityWith(fixture.Host.LocalPlayerId, SessionCapabilities.SessionV1),
                "session construction freezes caller-owned capability lists before the handshake");

        var baseCapabilities = SessionCapabilities.ForNativeAdapter("744825", false);
        var dlcCapabilities = SessionCapabilities.ForNativeAdapter("744825", true);
        check(baseCapabilities.Contains(SessionCapabilities.NativePlanetV1) && baseCapabilities.Contains(SessionCapabilities.NativeObservationV1),
            "the pinned WorldContainer and Grid adapters do not require Spaced Out");
        check(!baseCapabilities.Contains(SessionCapabilities.NativeStarMapV1) && dlcCapabilities.Contains(SessionCapabilities.NativeStarMapV1),
            "only the cluster star-map adapter requires Spaced Out");
        check(SessionCapabilities.ForNativeAdapter("unknown", true).SequenceEqual(SessionCompatibility.Normalize(SessionCapabilities.Core)) &&
            !SessionCapabilities.SupportsNativeAdapter("unknown", true, SessionCapabilities.NativePlanetV1),
            "unadapted game builds keep core networking but fail closed for native interfaces");
        check(!SessionCapabilities.Core.Contains(SessionCapabilities.ModSynchronizationV1),
            "a raw RoomSession does not advertise a coordinator it has not installed");
        VerifyWelcome(check, false);
        VerifyWelcome(check, true);
        Codec(check);
        NativeGuard(check);
        ConnectionGenerations(check);
        Console.WriteLine("Compatibility: real RoomSession handshakes, directional capability contracts, codec bounds and pinned native capability table. No cross-version native runtime claim.");
    }

    private static void VerifyWelcome(Action<bool, string> check, bool corruptGameplay)
    {
        using (var network = new DeterministicRoomNetwork(49381))
        {
            var hostProfile = Profile("Host"); var peerProfile = Profile("Peer");
            hostProfile.Capabilities = hostProfile.Capabilities.Concat(new[] { Producer }).ToArray();
            peerProfile.RequiredCapabilities = peerProfile.RequiredCapabilities.Concat(new[] { Producer }).ToArray();
            using (var host = new RoomSession(network.Host, hostProfile, "secret", "Welcome fixture"))
            {
                var endpoint = (DeterministicRoomNetwork.Endpoint)network.Connect();
                using (var peer = new RoomSession(endpoint, peerProfile, "secret"))
                {
                    network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
                    {
                        if (!fromHost) return false;
                        var packet = Protocol.Decode(bytes);
                        if (packet.Kind != PacketKind.Welcome) return false;
                        if (corruptGameplay) packet.GameplayVersion = "incompatible";
                        else packet.Capabilities = SessionCapabilities.Core;
                        network.InjectHostFrame(endpoint, Protocol.Encode(packet), 0);
                        return true;
                    };
                    for (int i = 0; i < 500 && !peer.Closed; i++)
                    { network.Advance(.01); host.Pump(network.Now); peer.Pump(network.Now); }
                    check(peer.Closed && !peer.Joined && peer.LastError == (corruptGameplay ? "gameplay_mismatch" : "capability_mismatch"),
                        corruptGameplay ? "client rejects a Welcome selecting a different gameplay contract" :
                        "client independently rejects a Welcome missing a required host capability");
                }
            }
        }
    }

    private static void Codec(Action<bool, string> check)
    {
        foreach (var kind in new[] { PacketKind.Hello, PacketKind.Welcome, PacketKind.Reject })
        {
            var packet = Hello(); packet.Kind = kind; packet.Room = new string('r', 32); packet.Text = "capability_mismatch";
            packet.PlayerId = new string('p', 32); packet.HistoryStart = 1;
            packet.Players = new[] { new PlayerInfo { Id = packet.PlayerId, Name = "Peer", Number = 1 } };
            packet.Capabilities = new[] { Producer, SessionCapabilities.SessionV1 };
            packet.RequiredCapabilities = new[] { SessionCapabilities.SessionV1 };
            var decoded = Protocol.Decode(Protocol.Encode(packet));
            check(decoded.Capabilities.SequenceEqual(SessionCompatibility.Normalize(packet.Capabilities)) &&
                decoded.RequiredCapabilities.SequenceEqual(packet.RequiredCapabilities), "capability contracts survive " + kind + " serialization");
            if (kind == PacketKind.Welcome)
                check(decoded.GameplayId == packet.GameplayId && decoded.GameplayVersion == packet.GameplayVersion &&
                    decoded.GameVersion == packet.GameVersion && decoded.CycleTrimVersion == packet.CycleTrimVersion &&
                    decoded.SpacedOut == packet.SpacedOut,
                    "Welcome identifies the host gameplay and environment contracts for the joining client");
        }

        foreach (var invalid in new[] { new[] { "UPPER" }, new[] { "test.invalid/id" }, new[] { "test.duplicate", "test.duplicate" },
            new[] { "" }, new[] { new string('a', 65) }, Enumerable.Range(0, 33).Select(i => "test.capability." + i).ToArray() })
        {
            var packet = Hello(); packet.Capabilities = invalid;
            check(Rejects(() => Protocol.Encode(packet)), "outgoing malformed capability list is rejected");
        }
        foreach (var malformed in new[] { RawReject(33, new string[0]), RawReject(2, new[] { "test.duplicate", "test.duplicate" }),
            RawReject(1, new[] { "UPPER" }), RawReject(1, new[] { new string('a', 65) }) })
            check(Rejects(() => Protocol.Decode(malformed)), "untrusted malformed capability list is rejected by the wire decoder");

        var hello = Hello(); hello.Capabilities = new[] { Producer, SessionCapabilities.SessionV1 };
        string proof = Protocol.PasswordProof("secret", "challenge", hello);
        hello.Capabilities = hello.Capabilities.Reverse().ToArray();
        check(proof == Protocol.PasswordProof("secret", "challenge", hello), "capability order does not change the authenticated contract");
        hello.Capabilities = new[] { SessionCapabilities.SessionV1 };
        check(proof != Protocol.PasswordProof("secret", "challenge", hello), "password proof binds advertised capability contracts");
        hello = Hello(); proof = Protocol.PasswordProof("secret", "challenge", hello);
        hello.RequiredCapabilities = new[] { Producer };
        check(proof != Protocol.PasswordProof("secret", "challenge", hello), "password proof binds required capability contracts");
        hello = Hello(); proof = Protocol.PasswordProof("secret", "challenge", hello); hello.SpacedOut = !hello.SpacedOut;
        check(proof != Protocol.PasswordProof("secret", "challenge", hello), "DLC description cannot be silently changed in a password handshake");
        byte[] old = Protocol.Encode(new Packet { Kind = PacketKind.Ping }); old[4] = 9;
        check(Rejects(() => Protocol.Decode(old)), "different wire protocol still fails closed despite descriptive game versions");
    }

    private static Packet Hello()
    { return new Packet { Kind = PacketKind.Hello, Name = "Peer", GameVersion = "744825", CycleTrimVersion = "0.3.2",
        SpacedOut = true, Nonce = Protocol.NewNonce(), Proof = Protocol.NewNonce() }; }

    private static bool Rejects(Action action)
    { try { action(); return false; } catch (InvalidDataException) { return true; } }

    private static void NativeGuard(Action<bool, string> check)
    {
        foreach (string version in new[] { "unknown", null })
        {
            int dlcReads = 0, nativeReads = 0; string metadata = "unread";
            var guard = new NativeAdapterGuard(version, () => { dlcReads++; throw new Exception("native DLC query touched"); });
            bool rejected = false;
            try { guard.Read(SessionCapabilities.NativePlanetV1, () => { nativeReads++; metadata = "read"; return metadata; }); }
            catch (NativeAdapterUnavailableException error)
            { rejected = error.Message == "native_adapter_unavailable" && error.Capability == SessionCapabilities.NativePlanetV1; }
            check(rejected && dlcReads == 0 && nativeReads == 0 && metadata == "unread",
                "an unknown or uninitialized adapter fails before DLC/native delegates or world metadata are touched");
        }
        int baseDlcReads = 0, baseNativeReads = 0;
        var baseGuard = new NativeAdapterGuard("744825", () => { baseDlcReads++; throw new Exception("base adapter queried DLC"); });
        check(baseGuard.Read(SessionCapabilities.NativePlanetV1, () => ++baseNativeReads) == 1 &&
            baseGuard.Read(SessionCapabilities.NativeObservationV1, () => ++baseNativeReads) == 2 && baseDlcReads == 0,
            "verified common native adapters run without consulting DLC state");
        int starDlcReads = 0, starNativeReads = 0;
        var noDlc = new NativeAdapterGuard("744825", () => { starDlcReads++; return false; });
        bool starRejected = false;
        try { noDlc.Read(SessionCapabilities.NativeStarMapV1, () => ++starNativeReads); }
        catch (NativeAdapterUnavailableException) { starRejected = true; }
        check(starRejected && starDlcReads == 1 && starNativeReads == 0,
            "the base game cannot enter the DLC-only native star-map reader");
        var withDlc = new NativeAdapterGuard("744825", () => true);
        check(withDlc.Read(SessionCapabilities.NativeStarMapV1, () => ++starNativeReads) == 1,
            "the pinned DLC star-map adapter invokes its native reader only after authorization");
        check(!withDlc.Supports(SessionCapabilities.SessionV1) && !withDlc.Supports(Producer),
            "network and plugin contracts cannot authorize entry into a native adapter");
    }

    private static void ConnectionGenerations(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(51973))
        {
            var hostProfile = Profile("Host"); var peerProfile = Profile("Peer");
            hostProfile.Capabilities = hostProfile.Capabilities.Concat(new[] { Producer }).ToArray();
            peerProfile.Capabilities = peerProfile.Capabilities.Concat(new[] { Producer }).ToArray();
            Packet initialHello = null;
            network.Settings.DropBeforeDelivery = (fromHost, bytes) =>
            {
                var packet = Protocol.Decode(bytes);
                if (!fromHost && packet.Kind == PacketKind.Hello && initialHello == null) initialHello = packet;
                return false;
            };
            using (var host = new RoomSession(network.Host, hostProfile, "secret", "Generation fixture"))
            {
                var endpoint = network.Connect();
                using (var peer = new RoomSession(endpoint, peerProfile, "secret"))
                {
                    check(host.ConnectionGenerationFor(host.LocalPlayerId) == 1 &&
                        peer.ConnectionGenerationFor(host.LocalPlayerId) == 0 && host.ConnectionGenerationFor("unknown") == 0,
                        "only the hosting process has a local connection generation before a guest authenticates");
                    Until(network, host, peer, () => peer.Joined);
                    string seatId = peer.LocalPlayerId;
                    long previous = host.ConnectionGenerationFor(seatId);
                    check(previous == 1 && peer.ConnectionGenerationFor(host.LocalPlayerId) == 1 && peer.ConnectionGenerationFor(seatId) == 1,
                        "initial authentication publishes generation one at both endpoints");
                    long observed = 0; bool connected = false;
                    host.Changed += () =>
                    {
                        if (host.ConnectionGenerationFor(seatId) > previous)
                        { observed = host.ConnectionGenerationFor(seatId); connected = host.Players.Any(p => p.Id == seatId && p.Connected); }
                    };
                    var replacement = (DeterministicRoomNetwork.Endpoint)network.Connect();
                    Packet challenge = null;
                    for (int i = 0; i < 100 && challenge == null; i++)
                    {
                        network.Advance(.01); host.Pump(network.Now);
                        TransportEvent item;
                        while (replacement.TryReceive(out item))
                            if (item.Kind == TransportEventKind.Message)
                            { var packet = Protocol.Decode(item.Data); if (packet.Kind == PacketKind.Challenge) challenge = packet; }
                    }
                    if (challenge == null || initialHello == null) throw new Exception("replacement handshake challenge missing");
                    var hello = Hello(); hello.PlayerId = seatId; hello.ResumeToken = "";
                    hello.Capabilities = peerProfile.Capabilities; hello.RequiredCapabilities = peerProfile.RequiredCapabilities;
                    hello.ResumeProof = Protocol.ResumeProof(initialHello.ResumeToken, challenge.Nonce, hello);
                    hello.Proof = Protocol.PasswordProof("secret", challenge.Nonce, hello);
                    replacement.Send("host", Protocol.Encode(hello));
                    for (int i = 0; i < 100 && host.ConnectionGenerationFor(seatId) == previous; i++)
                    { network.Advance(.01); host.Pump(network.Now); }
                    check(host.ConnectionGenerationFor(seatId) == previous + 1 && observed == previous + 1 && connected && host.Players.Length == 2,
                        "online replacement of a durable seat changes the generation before Changed even when Connected remains true");
                    check(host.CanUseCapabilityWith(seatId, Producer), "replacement authentication preserves negotiated capability contracts");
                    replacement.Dispose(); network.Advance(.01); host.Pump(network.Now);
                    check(host.ConnectionGenerationFor(seatId) == 0 && host.ConnectionGenerationFor(host.LocalPlayerId) == 1,
                        "a disconnected reserved seat exposes no live generation while the host retains its local generation");
                }
                host.Dispose();
                check(host.ConnectionGenerationFor(host.LocalPlayerId) == 0, "disposed host cannot expose a usable local generation");
            }
        }
        using (var network = new DeterministicRoomNetwork(62113))
        using (var host = new RoomSession(network.Host, Profile("Host"), "secret", "Reconnect fixture"))
        using (var peer = new RoomSession(network.Connect(), Profile("Peer"), "secret", reconnect: () => network.Connect(),
            options: new SessionOptions { RetryJitter = () => .5 }))
        {
            Until(network, host, peer, () => peer.Joined);
            string seatId = peer.LocalPlayerId; long priorGuest = peer.ConnectionGenerationFor(host.LocalPlayerId);
            network.CutAll(); network.Advance(.01); host.Pump(network.Now); peer.Pump(network.Now);
            check(peer.Reconnecting && peer.ConnectionGenerationFor(host.LocalPlayerId) == 0 &&
                peer.ConnectionGenerationFor(seatId) == 0 && host.ConnectionGenerationFor(seatId) == 0,
                "reconnect backoff invalidates both guest endpoint generations and the host's reserved connection");
            Until(network, host, peer, () => peer.Joined);
            check(peer.LocalPlayerId == seatId && peer.ConnectionGenerationFor(host.LocalPlayerId) == priorGuest + 1 &&
                peer.ConnectionGenerationFor(seatId) == priorGuest + 1 && host.ConnectionGenerationFor(seatId) == 2,
                "a new authenticated Welcome advances the guest generation while retaining the same durable seat");
            check(peer.CanUseCapabilityWith(host.LocalPlayerId, SessionCapabilities.SessionV1),
                "new Welcome generation does not alter successful capability negotiation");
            using (var other = new RoomSession(network.Connect(), Profile("Other"), "secret"))
            {
                for (int i = 0; i < 500 && (!other.Joined || peer.Players.Length < 3); i++)
                { network.Advance(.01); host.Pump(network.Now); peer.Pump(network.Now); other.Pump(network.Now); }
                check(peer.Players.Any(p => p.Id == other.LocalPlayerId) && peer.ConnectionGenerationFor(other.LocalPlayerId) == 0 &&
                    host.ConnectionGenerationFor(other.LocalPlayerId) == 1,
                    "guests cannot infer another guest's authenticated transport generation from the roster");
            }
            peer.Dispose();
            check(peer.ConnectionGenerationFor(host.LocalPlayerId) == 0 && peer.ConnectionGenerationFor(seatId) == 0,
                "disposed guest exposes neither a host nor a local authentication generation");
        }
    }

    private static void Until(DeterministicRoomNetwork network, RoomSession host, RoomSession peer, Func<bool> condition)
    {
        for (int i = 0; i < 1000; i++)
        {
            network.Advance(.01); host.Pump(network.Now); peer.Pump(network.Now);
            if (condition()) return;
        }
        throw new Exception("connection generation fixture timed out");
    }

    private static byte[] RawReject(byte count, string[] capabilities)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(0x4F494E4F); writer.Write((ushort)Protocol.Version); writer.Write((byte)PacketKind.Reject);
            WriteText(writer, "capability_mismatch"); writer.Write(count);
            foreach (string capability in capabilities) WriteText(writer, capability);
            writer.Write((byte)0); writer.Flush(); return stream.ToArray();
        }
    }
    private static void WriteText(BinaryWriter writer, string text)
    { var bytes = Encoding.UTF8.GetBytes(text); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
}
