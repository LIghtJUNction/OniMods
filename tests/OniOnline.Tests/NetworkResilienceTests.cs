using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using OniOnline.Network;

internal static class NetworkResilienceTests
{
    private const int Seed = 1729;
    private static Action<bool, string> check;

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        DelayedReliableNetwork();
        FourPlayerLongBlackhole();
        LostWelcomeAndAcknowledgement();
        FullOfflineQueueAndLostLastChat();
        InvalidResumeSecretsCannotClaimSeats();
        DuplicateAndLostFrames();
        ReconnectWindowAndQueueBounds();
        HostRestartDoesNotChangeRoom();
        RealTcpFaults();
        Console.WriteLine("Weak-network fixtures completed: seed=" + Seed + "; virtual protocol faults and real TCP delay/throttle/blackhole/restart.");
    }

    private static SessionProfile Profile(string name)
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    private static SessionOptions Options(double timeout = 12, double window = 40)
    {
        return new SessionOptions { HandshakeTimeoutSeconds = timeout, HeartbeatIntervalSeconds = 1,
            PeerTimeoutSeconds = timeout, ReconnectWindowSeconds = window, SeatReservationSeconds = window + 5,
            ChatRetrySeconds = 1, RetryJitter = () => 0.5 };
    }

    private static void Step(DeterministicRoomNetwork network, RoomSession host, RoomSession client, double seconds = 0.05)
    {
        network.Advance(seconds);
        host.Pump(network.Now);
        client.Pump(network.Now);
    }

    private static void Until(DeterministicRoomNetwork network, RoomSession host, RoomSession client,
        Func<bool> condition, double limit, string description)
    {
        double deadline = network.Now + limit;
        while (network.Now <= deadline)
        {
            Step(network, host, client);
            if (condition()) { check(true, description); return; }
        }
        throw new Exception("Virtual network timeout: " + description + "; client=" + client.LastError + "; seed=" + Seed);
    }

    private static void DelayedReliableNetwork()
    {
        using (var network = new DeterministicRoomNetwork(Seed))
        using (var host = new RoomSession(network.Host, Profile("房主"), "secret", "弱网测试", options: Options(20)))
        using (var client = new RoomSession(network.Connect(), Profile("高延迟玩家"), "secret",
            reconnect: network.Connect, options: Options(20)))
        {
            network.Settings.Delay = 0.65;
            network.Settings.Jitter = 0.5;
            network.Settings.Drop = 0.2;
            network.Settings.Duplicate = 0.3;
            network.Settings.Reorder = 0.3;
            network.Settings.ReliableOrdered = true;
            Until(network, host, client, () => client.Joined, 20, "1.3s RTT with jitter and retransmission completes the real handshake");
            string identity = client.LocalPlayerId;
            for (int i = 0; i < 4; i++)
            {
                check(client.SendChat("延迟消息 " + i), "weak-network chat accepted");
                for (int frame = 0; frame < 22; frame++) Step(network, host, client);
            }
            Until(network, host, client, () => client.Chat.Length == 4 && client.PendingChatCount == 0, 20,
                "four delayed chats acknowledged and visible exactly once");
            check(host.Chat.Length == 4, "retry does not duplicate host chat");
            check(host.StartGame(), "host starts under high latency");
            Until(network, host, client, () => client.Started, 10, "start request survives retransmission delay");
            network.CutAll();
            Step(network, host, client);
            check(client.Reconnecting && !client.Closed, "short interruption enters automatic reconnect");
            host.SendChat("断线期间的消息");
            Until(network, host, client, () => client.Joined && !client.Reconnecting && client.Chat.Length == 5, 30,
                "short interruption restores chat history without manual rejoin");
            check(client.LocalPlayerId == identity, "reconnect preserves player identity");
            check(host.Players.Length == 2, "reconnect replaces its old connection instead of creating a duplicate player");
            check(client.Started, "reconnect preserves started state");
            check(network.Retransmissions > 0 && network.Reordered > 0 && network.Duplicated > 0,
                "seed exercised loss, reorder and duplication below reliable delivery");
            check(network.PeakScheduled <= DeterministicRoomNetwork.QueueLimit, "fault scheduler remains bounded");
        }
    }

    private static void DuplicateAndLostFrames()
    {
        using (var network = new DeterministicRoomNetwork(Seed + 1))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Protocol fault injection", options: Options(20)))
        using (var client = new RoomSession(network.Connect(), Profile("Client"), "", reconnect: network.Connect, options: Options(20)))
        {
            Until(network, host, client, () => client.Joined, 2, "protocol fault fixture authenticates before raw frame injection");
            // Unlike packet faults inside TCP, these directly stress application
            // acknowledgements/replay, and must not be reported as TCP packet loss.
            network.Settings.Delay = 0.15;
            network.Settings.Jitter = 0.13;
            network.Settings.Drop = 0.2;
            network.Settings.Duplicate = 0.8;
            network.Settings.Reorder = 0.6;
            for (int i = 0; i < 3; i++)
            {
                check(client.SendChat("idempotent-" + i), "raw-frame fault chat accepted");
                for (int frame = 0; frame < 22; frame++) Step(network, host, client);
            }
            Until(network, host, client, () => host.Chat.Length == 3 && client.PendingChatCount == 0, 25,
                "lost and duplicated submissions are retried exactly once");
            // Turn the connection into an ordered reliable channel before the
            // resume handshake. Replay must fill every sequence gap afterwards.
            network.Settings.Drop = 0;
            network.Settings.Duplicate = 0;
            network.Settings.Reorder = 0;
            network.Settings.ReliableOrdered = true;
            network.CutAll();
            Until(network, host, client, () => client.Joined && client.Chat.Length == 3, 15,
                "resume fills missing out-of-order chat and deduplicates replay");
            check(client.Chat.Select(c => c.Text).Distinct().Count() == 3, "each application message occurs once after replay");
            check(client.Chat.Zip(client.Chat.Skip(1), (a, b) => a.Sequence < b.Sequence).All(value => value),
                "replayed messages are displayed in authoritative sequence order");
            check(network.Dropped > 0 && network.Duplicated > 0 && network.Reordered > 0,
                "raw-frame test actually injected all configured faults");
        }
    }

    private static void FourPlayerLongBlackhole()
    {
        using (var network = new DeterministicRoomNetwork(Seed + 5))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Four players", options: Options(30, 90)))
        {
            network.Settings.Delay = 1.5;
            network.Settings.Jitter = 0.5;
            network.Settings.Drop = 0.2;
            network.Settings.Duplicate = 0.2;
            network.Settings.Reorder = 0.3;
            network.Settings.ReliableOrdered = true;
            var clients = Enumerable.Range(1, 3).Select(i => new RoomSession(network.Connect(), Profile("Player " + i), "",
                reconnect: network.Connect, options: Options(30, 90))).ToArray();
            try
            {
                Action step = () =>
                {
                    network.Advance(0.05);
                    host.Pump(network.Now);
                    foreach (var client in clients) client.Pump(network.Now);
                };
                Action<Func<bool>, double, string> until = (condition, limit, description) =>
                {
                    double deadline = network.Now + limit;
                    while (network.Now <= deadline) { step(); if (condition()) { check(true, description); return; } }
                    throw new Exception("Four-player timeout: " + description + "; " + string.Join(",", clients.Select(c => c.LastError)));
                };
                until(() => clients.All(c => c.Joined), 45, "four players join with 1500ms one-way delay, jitter and 20% loss/duplication");
                var identities = clients.Select(c => c.LocalPlayerId).ToArray();
                check(host.StartGame(), "four-player host starts once");
                until(() => clients.All(c => c.Started), 20, "all three high-latency guests receive the same start state");
                network.CutAll();
                step();
                network.Settings.Blackhole = true;
                for (int i = 0; i < clients.Length; i++)
                    check(clients[i].SendChat("offline player " + i), "four-player interruption queues chat");
                check(host.SendChat("host while clients offline"), "host continues chat while clients reconnect");
                double resumeAt = network.Now + 30;
                while (network.Now < resumeAt) step();
                check(clients.All(c => c.Reconnecting && !c.Joined && !c.Closed),
                    "all three clients survive a full 30-second blackhole without terminal failure");
                check(host.Players.Length == 4, "offline seats remain reserved for all four players");
                network.Settings.Blackhole = false;
                until(() => clients.All(c => c.Joined && !c.Reconnecting && c.PendingChatCount == 0 && c.Chat.Length == 4),
                    55, "all four players recover automatically after the 30-second blackhole");
                check(clients.Select(c => c.LocalPlayerId).SequenceEqual(identities),
                    "simultaneous reconnection preserves every player's identity");
                check(host.Players.Length == 4 && host.Players.Select(p => p.Id).Distinct().Count() == 4,
                    "simultaneous reconnect does not allocate duplicate seats");
                check(host.Chat.Length == 4 && clients.All(c => c.Chat.Select(m => m.Text).Distinct().Count() == 4),
                    "all four peers converge on four unique chat messages");
                check(clients.All(c => c.Started), "all four players retain the started state after network recovery");
            }
            finally { foreach (var client in clients) client.Dispose(); }
        }
    }

    private static void LostWelcomeAndAcknowledgement()
    {
        using (var network = new DeterministicRoomNetwork(Seed + 6))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Interrupted handshake", options: Options()))
        {
            bool welcomeCut = false;
            network.Settings.CutBeforeDelivery = (isHost, data) =>
            {
                if (!welcomeCut && isHost && Protocol.Decode(data).Kind == PacketKind.Welcome)
                { welcomeCut = true; return true; }
                return false;
            };
            using (var client = new RoomSession(network.Connect(), Profile("Client"), "", reconnect: network.Connect, options: Options()))
            {
                Until(network, host, client, () => client.Joined, 15,
                    "connection cut before first Welcome recovers without having received a player ID");
                check(welcomeCut && host.Players.Length == 2,
                    "lost first Welcome reuses its unconfirmed seat instead of allocating another");
                string identity = client.LocalPlayerId;
                bool publicationCut = false;
                network.Settings.CutBeforeDelivery = (isHost, data) =>
                {
                    if (!publicationCut && isHost && Protocol.Decode(data).Kind == PacketKind.Chat)
                    { publicationCut = true; return true; }
                    return false;
                };
                check(client.SendChat("accepted but acknowledgement lost"), "pending message accepted before lost acknowledgement");
                Until(network, host, client, () => publicationCut && client.Joined && client.PendingChatCount == 0 && client.Chat.Length == 1,
                    15, "disconnect after host publication recovers lost acknowledgement and message");
                check(host.Chat.Length == 1 && client.LocalPlayerId == identity,
                    "replayed pending submission is published exactly once under the original identity");
            }
        }
    }

    private static void FullOfflineQueueAndLostLastChat()
    {
        using (var network = new DeterministicRoomNetwork(Seed + 7))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Offline queue", options: Options(10, 90)))
        using (var client = new RoomSession(network.Connect(), Profile("Client"), "", reconnect: network.Connect, options: Options(10, 90)))
        {
            Until(network, host, client, () => client.Joined, 2, "full offline queue fixture joins");
            string identity = client.LocalPlayerId;
            network.Settings.Blackhole = true;
            network.CutAll();
            Step(network, host, client);
            int accepted = 0;
            for (int i = 0; i < 40; i++)
            {
                if (client.SendChat("offline queue message " + i)) accepted++;
                Step(network, host, client, 0.5);
            }
            check(accepted == 20 && client.PendingChatCount == 20,
                "twenty offline messages fill the queue and excess messages receive backpressure");
            network.Settings.Blackhole = false;
            Until(network, host, client, () => client.Joined && client.PendingChatCount == 0 && client.Chat.Length == 20, 60,
                "full offline queue drains after reconnect without rate-limit disconnection");
            check(host.Chat.Length == 20 && host.Chat.Select(c => c.Text).Distinct().Count() == 20,
                "all twenty offline messages publish exactly once");
            check(client.LocalPlayerId == identity && !client.Closed && !client.Reconnecting,
                "draining the full offline queue preserves the same connected player");
            bool dropped = false;
            network.Settings.DropBeforeDelivery = (isHost, data) =>
            {
                if (!dropped && isHost && Protocol.Decode(data).Kind == PacketKind.Chat)
                { dropped = true; return true; }
                return false;
            };
            check(host.SendChat("last message deliberately lost"), "host publishes a final chat without a subsequent chat");
            Until(network, host, client, () => dropped && client.Chat.Length == 21, 10,
                "heartbeat detects a missing last chat and requests replay without another chat arriving");
            check(client.Chat.Last().Text == "last message deliberately lost" && host.Chat.Length == 21,
                "missing final chat is restored exactly once");
        }
    }

    private static void InvalidResumeSecretsCannotClaimSeats()
    {
        foreach (string secret in new[] { new string('!', 44), Convert.ToBase64String(new byte[31]), Convert.ToBase64String(new byte[33]) })
        {
            using (var network = new DeterministicRoomNetwork(Seed + 8))
            using (var host = new RoomSession(network.Host, Profile("Host"), "", "Resume validation", options: Options()))
            using (var raw = network.Connect())
            {
                bool sent = false;
                bool rejected = false;
                for (int frame = 0; frame < 40; frame++)
                {
                    network.Advance(0.05);
                    host.Pump(network.Now);
                    TransportEvent item;
                    while (raw.TryReceive(out item))
                    {
                        if (item.Kind != TransportEventKind.Message) continue;
                        Packet packet = Protocol.Decode(item.Data);
                        if (packet.Kind == PacketKind.Challenge)
                        {
                            var hello = new Packet { Kind = PacketKind.Hello, Name = "Malformed resume secret", GameVersion = "744825",
                                CycleTrimVersion = "0.3.2", SpacedOut = true, Nonce = Protocol.NewNonce(), PlayerId = "",
                                ResumeToken = secret, ResumeProof = "", Sequence = 0 };
                            hello.Proof = Protocol.PasswordProof("", packet.Nonce, hello);
                            sent = raw.Send("host", Protocol.Encode(hello));
                        }
                        else if (packet.Kind == PacketKind.Reject && packet.Text == "resume_invalid") rejected = true;
                    }
                }
                check(sent && rejected && host.Players.Length == 1,
                    "malformed base64 or non-32-byte resume secret cannot authenticate with an empty proof");
            }
        }
    }

    private static void ReconnectWindowAndQueueBounds()
    {
        using (var network = new DeterministicRoomNetwork(Seed + 2))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Bounds", options: Options(3, 30)))
        using (var client = new RoomSession(network.Connect(), Profile("Client"), "", reconnect: network.Connect, options: Options(3, 30)))
        {
            Until(network, host, client, () => client.Joined, 2, "bounded-retry fixture joins");
            network.Settings.Blackhole = true;
            network.CutAll();
            Step(network, host, client);
            int accepted = 0;
            int maximumPending = 0;
            for (int i = 0; i < 100; i++)
            {
                if (client.SendChat("offline-" + i)) accepted++;
                maximumPending = Math.Max(maximumPending, client.PendingChatCount);
                Step(network, host, client, 0.25);
            }
            check(accepted < 100, "offline sender receives backpressure instead of unlimited queuing");
            check(maximumPending == 20 && accepted == 20, "offline chat accepts exactly its bounded capacity of 20");
            Until(network, host, client, () => client.Closed, 15, "persistent blackhole reaches an explicit reconnect deadline");
            check(!client.Reconnecting && !client.Joined && !string.IsNullOrEmpty(client.LastError),
                "expired reconnect reports terminal failure instead of spinning forever");
            check(client.RetryAttempt <= 10, "retry backoff avoids a reconnect storm");
            check(network.PeakScheduled <= DeterministicRoomNetwork.QueueLimit,
                "persistent blackhole does not grow the simulated transport queue");
        }
    }

    private static void HostRestartDoesNotChangeRoom()
    {
        using (var original = new DeterministicRoomNetwork(Seed + 3))
        using (var replacement = new DeterministicRoomNetwork(Seed + 4))
        using (var host = new RoomSession(original.Host, Profile("Host"), "", "Original", options: Options()))
        {
            var current = original;
            using (var client = new RoomSession(original.Connect(), Profile("Client"), "", reconnect: () => current.Connect(), options: Options()))
            using (var newHost = new RoomSession(replacement.Host, Profile("Host"), "", "New room", options: Options()))
            {
                Until(original, host, client, () => client.Joined, 2, "host restart fixture joins original room");
                string originalRoom = client.RoomId;
                string originalPlayer = client.LocalPlayerId;
                host.Dispose();
                current = replacement;
                replacement.Advance(original.Now);
                Until(replacement, newHost, client, () => client.Closed, 15,
                    "host process restart with a different room fails resume explicitly");
                check(!client.Joined && client.RoomId == originalRoom && client.LocalPlayerId == originalPlayer,
                    "replacement server cannot roll a client into a different room or player identity");
                check(newHost.Players.Length == 1, "failed resume does not claim a fresh seat on an unrelated room");
            }
        }
    }

    private static void RealTcpFaults()
    {
        using (var transport = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var proxy = new TcpFaultProxy(transport.Port, Seed))
        using (var host = new RoomSession(transport, Profile("TCP host"), "pass", "TCP proxy", options: Options(12, 25)))
        using (var client = new RoomSession(TcpRoomTransport.Join("127.0.0.1", proxy.Port), Profile("TCP client"), "pass",
            options: Options(12, 25)))
        {
            proxy.DelayMilliseconds = 160;
            proxy.JitterMilliseconds = 70;
            proxy.BytesPerSecond = 2048;
            var timer = Stopwatch.StartNew();
            double offset = 0;
            Action pump = () => { double now = timer.Elapsed.TotalSeconds + offset; host.Pump(now); client.Pump(now); };
            WallUntil(pump, () => client.Joined, 12, "real TCP delayed/throttled handshake");
            string identity = client.LocalPlayerId;
            check(client.SendChat(new string('慢', 250)), "real TCP large UTF-8 chat accepted");
            WallUntil(pump, () => host.Chat.Length == 1 && client.Chat.Length == 1 && client.PendingChatCount == 0,
                12, "real TCP throttled chat framing and acknowledgement");
            proxy.Blackhole = true;
            check(host.SendChat("blackhole-held"), "real TCP sends a frame into the blackhole");
            WallUntil(pump, () => proxy.StalledChunks >= 2, 5, "real TCP proxy stalls both forwarding directions before timeout");
            // A production timeout is based on monotonic session time. Advance
            // that clock explicitly instead of spending 12 seconds sleeping.
            offset += 13;
            pump();
            WallUntil(pump, () => client.Reconnecting, 3, "real TCP blackhole triggers heartbeat timeout");
            proxy.StopListening();
            proxy.DisconnectAll();
            WallUntil(pump, () => proxy.ActiveConnections == 0, 3, "real TCP proxy closes every obsolete socket");
            proxy.Blackhole = false;
            proxy.StartListening();
            WallUntil(pump, () => client.Joined && !client.Reconnecting && client.Chat.Length == 2, 15,
                "real TCP listener restart reconnects and recovers unseen chat automatically");
            check(client.LocalPlayerId == identity && host.Players.Length == 2,
                "real TCP reconnect keeps its original seat and player identity");
            check(client.SendChat("断开后恢复"), "real TCP chat accepted after reconnect");
            WallUntil(pump, () => host.Chat.Length == 3 && client.Chat.Length == 3 && client.PendingChatCount == 0,
                10, "real TCP chat resumes without duplicates");
            check(proxy.AcceptedConnections >= 2 && proxy.ForwardedBytes > 0,
                "test exercised multiple real proxy connections and bytes");
            client.Dispose();
            WallUntil(() => host.Pump(timer.Elapsed.TotalSeconds + offset), () => host.Players.Length == 1, 5,
                "explicit leave releases its seat instead of reconnecting");
        }
    }

    private static void WallUntil(Action pump, Func<bool> condition, double seconds, string description)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds <= seconds)
        {
            pump();
            if (condition()) { check(true, description); return; }
            Thread.Sleep(5);
        }
        throw new Exception("Real TCP timeout: " + description);
    }
}
