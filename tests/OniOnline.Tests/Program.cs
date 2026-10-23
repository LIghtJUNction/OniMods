using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using OniOnline.Network;
using OniOnline.Core;

internal static class Program
{
    private static int assertions;
    private static readonly List<RoomSession> sessions = new List<RoomSession>();
    private static string address;

    private static SessionProfile Profile(string name, string version = "744825")
    {
        return new SessionProfile { Name = name, GameVersion = version, CycleTrimVersion = "0.3.2", SpacedOut = true };
    }

    private static void Check(bool value, string description)
    {
        assertions++;
        if (!value) throw new Exception(description);
    }

    private static void Until(Func<bool> condition, string description, int milliseconds = 6000)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            foreach (var session in sessions.ToArray()) session.Pump();
            if (condition()) { Check(true, description); return; }
            Thread.Sleep(5);
        }
        throw new Exception("Timeout: " + description);
    }

    private static RoomSession Host(out int port, string password = "房间密钥")
    {
        var transport = TcpRoomTransport.Host(IPAddress.Any, 0);
        port = transport.Port;
        var host = new RoomSession(transport, Profile("房主"), password, "OniOnline 测试");
        sessions.Add(host);
        return host;
    }

    private static RoomSession Join(int port, string name, string password = "房间密钥", string version = "744825")
    {
        var client = new RoomSession(TcpRoomTransport.Join(address, port), Profile(name, version), password);
        sessions.Add(client);
        return client;
    }

    private static void Reset()
    {
        foreach (var session in sessions) session.Dispose();
        sessions.Clear();
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--tcp-worker-budget") return TcpWorkerBudgetTests.Run();
            if (args.Length == 1 && args[0] == "--loaded-colony-start")
            { LoadedColonyRoomStartTests.Run(Check); Console.WriteLine("PASS " + assertions + " loaded-colony start assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--local-star-map")
            { LocalStarMapCatalogTests.Run(Check); Console.WriteLine("PASS " + assertions + " local star-map assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--online-options")
            { OnlineOptionsTests.Run(Check); Console.WriteLine("PASS " + assertions + " online options assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--star-map-directory")
            { StarMapDirectoryTests.Run(Check); Console.WriteLine("PASS " + assertions + " star-map directory assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--voice")
            { RoomVoiceTests.Run(Check); Console.WriteLine("PASS " + assertions + " room voice assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--viewport-only-world")
            { ViewportOnlyWorldReplicationTests.Run(Check); Console.WriteLine("PASS " + assertions + " viewport-only world assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--steam-directory")
            { SteamRoomDirectoryTests.Run(Check); Console.WriteLine("PASS " + assertions + " Steam directory assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--readonly-latency")
            { ReadOnlyLatencyTests.Run(Check); Console.WriteLine("PASS " + assertions + " read-only high-latency assertions"); return 0; }
            if (args.Length > 0 && args[0] == "--peer") return PeerProcess(args);
            if (args.Length > 0 && args[0] == "--native-interest-peer") return NativeInterestPeer.Run(args);
            if (args.Length > 0 && args[0] == "--native-work-peer") return NativeRemoteWorkPeer.Run(args);
            if (args.Length > 0 && args[0] == "--visual-host") return VisualHost(args);
            if (args.Length == 1 && args[0] == "--resilience")
            { NetworkResilienceTests.Run(Check); Console.WriteLine("PASS " + assertions + " network resilience assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--moderation")
            { ModerationTests.Run(Check); Console.WriteLine("PASS " + assertions + " moderation assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--moderation-save")
            { ModerationPersistenceTests.Run(Check); Console.WriteLine("PASS " + assertions + " moderation save assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--star-systems")
            { StarSystemTests.Run(Check); Console.WriteLine("PASS " + assertions + " star-system assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--network-matrix")
            { NetworkScaleTests.Run(Check); Console.WriteLine("PASS " + assertions + " network matrix assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--teams")
            { TeamColonyTests.Run(Check); TeamCoordinatorTests.Run(Check); Console.WriteLine("PASS " + assertions + " team assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--world-interest")
            { WorldInterestTests.Run(Check); WorldInterestSessionTests.Run(Check); WorldCellDetailsTests.Run(Check); Console.WriteLine("PASS " + assertions + " world interest assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-origin")
            { DupeOriginTests.Run(Check); Console.WriteLine("PASS " + assertions + " duplicant origin assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-commands")
            { DupeCommandTests.Run(Check); Console.WriteLine("PASS " + assertions + " dupe command assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-lifecycle")
            { DupeCommandLifecycleTests.Run(Check); Console.WriteLine("PASS " + assertions + " dupe lifecycle assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--remote-work")
            { RemoteWorkTests.Run(Check); Console.WriteLine("PASS " + assertions + " remote work assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-archive-audit")
            { NativeDuplicantImportAuditTests.Run(Check); Console.WriteLine("PASS " + assertions + " native archive audit assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-remap")
            { NativeDuplicantRemapPlanTests.Run(Check); Console.WriteLine("PASS " + assertions + " native archive remap assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--dupe-transfer-preflight")
            { DuplicantTransferPreflightTests.Run(Check); Console.WriteLine("PASS " + assertions + " duplicant transfer preflight assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--native-sim-block")
            { NativeSimSaveBlockTests.Run(Check); Console.WriteLine("PASS " + assertions + " native Sim block assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--world-sync")
            { WorldSyncTests.Run(Check); SnapshotTransferTests.Run(Check); Console.WriteLine("PASS " + assertions + " world sync assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--multiplayer-tcp")
            { MultiplayerIntegrationTests.RunTcp(Check); Console.WriteLine("PASS " + assertions + " TCP multiplayer assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--multiplayer")
            { MultiplayerIntegrationTests.Run(Check); Console.WriteLine("PASS " + assertions + " multiplayer integration assertions"); return 0; }
            if (args.Length == 1 && args[0] == "--shared-time")
            { SharedTimeTests.Run(Check); Console.WriteLine("PASS " + assertions + " shared time assertions"); return 0; }
            if (args.Length == 2 && args[0] == "--check-bundled-valuetuple") return CheckBundledValueTuple(args[1]);
            if (args.Length == 2 && args[0] == "--check-no-probe") return CheckNoProbe(args[1]);
            if (args.Length == 2 && args[0] == "--check-plib") return CheckPLib(args[1]);
            address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString()).FirstOrDefault() ?? "127.0.0.1";
            ProtocolTests();
            ChatAndStart();
            LoadedColonyRoomStartTests.Run(Check);
            LocalStarMapCatalogTests.Run(Check);
            StarMapDirectoryTests.Run(Check);
            OnlineOptionsTests.Run(Check);
            RoomVoiceTests.Run(Check);
            ViewportOnlyWorldReplicationTests.Run(Check);
            SteamRoomDirectoryTests.Run(Check);
            WrongCredentials();
            CapacityAndAuthority();
            DisconnectAndRejoin();
            Framing();
            Discovery();
            Translations();
            TcpWorkerBudgetProcess();
            MultiProcess();
            NetworkResilienceTests.Run(Check);
            ModerationTests.Run(Check);
            ModerationPersistenceTests.Run(Check);
            StarSystemTests.Run(Check);
            NetworkScaleTests.Run(Check);
            DupeOriginTests.Run(Check);
            TeamColonyTests.Run(Check);
            TeamCoordinatorTests.Run(Check);
            WorldInterestTests.Run(Check);
            WorldInterestSessionTests.Run(Check);
            WorldCellDetailsTests.Run(Check);
            ReadOnlyLatencyTests.Run(Check);
            DupeCommandTests.Run(Check);
            DupeCommandLifecycleTests.Run(Check);
            NativeDuplicantImportAuditTests.Run(Check);
            NativeDuplicantRemapPlanTests.Run(Check);
            DuplicantTransferPreflightTests.Run(Check);
            NativeSimSaveBlockTests.Run(Check);
            RemoteWorkTests.Run(Check);
            WorldSyncTests.Run(Check);
            SnapshotTransferTests.Run(Check);
            SharedTimeTests.Run(Check);
            MultiplayerIntegrationTests.Run(Check);
            MultiplayerIntegrationTests.RunTcp(Check);
            Console.WriteLine("PASS " + assertions + " assertions; real TCP endpoint " + address + "; includes three separate client processes.");
            Console.WriteLine("This is same-host IP transport verification, not two-device ONI runtime verification.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Reset(); }
    }

    private static int VisualHost(string[] args)
    {
        int port, holdSeconds;
        if (args.Length != 3 || !int.TryParse(args[1], out port) || port < 0 || port > 65535 ||
            !int.TryParse(args[2], out holdSeconds) || holdSeconds < 1)
        {
            Console.Error.WriteLine("Usage: --visual-host <port (0 for automatic)> <holdSeconds>");
            return 2;
        }
        using (var transport = TcpRoomTransport.Host(IPAddress.Loopback, port))
        using (var host = new RoomSession(transport, Profile("好友房主"), "", "朋友的房间"))
        {
            Console.WriteLine("READY visual-host port=" + transport.Port);
            Console.Out.Flush();
            bool greeted = false;
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < holdSeconds)
            {
                host.Pump();
                if (!greeted && host.Players.Length > 1)
                    greeted = host.SendChat("欢迎来到房间，准备好就一起出发。");
                Thread.Sleep(10);
            }
        }
        return 0;
    }

    private static void ProtocolTests()
    {
        Check(Protocol.ValidText("中文🚀", 32), "valid Chinese and surrogate pair");
        Check(!Protocol.ValidText("line\nbreak", 32), "reject injected newline");
        Check(!Protocol.ValidText("\ud800", 32), "reject unpaired surrogate");
        Check(!Protocol.ValidText(new string('x', 513), 512), "chat bound");
        var hello = new Packet { Kind = PacketKind.Hello, Nonce = Protocol.NewNonce(), Name = "A", GameVersion = "744825", CycleTrimVersion = "0.3.2" };
        var proof = Protocol.PasswordProof("secret", "challenge-A", hello);
        hello.Proof = proof;
        Check(!Protocol.EqualProof(proof, Protocol.PasswordProof("secret", "challenge-B", hello)), "proof tied to challenge");
        Check(!Protocol.EqualProof(proof, Protocol.PasswordProof("bad", "challenge-A", hello)), "wrong password fails");
        Check(Protocol.Decode(Protocol.Encode(hello)).Name == "A", "protocol roundtrip");
        Check(RoomAddress.Parse("oni://steam/123456").Steam && RoomAddress.Parse("oni://steam/123456").LobbyId == 123456, "unified Steam room code");
        Check(RoomAddress.Parse("oni-steam://123456").Steam && RoomAddress.Parse("oni-steam://123456").LobbyId == 123456, "legacy Steam room code");
        Check(RoomAddress.Parse("oni://192.168.1.2:27140").Host == "192.168.1.2" && !RoomAddress.Parse("oni://192.168.1.2:27140").Steam, "unified LAN room code");
        Check(RoomAddress.Parse("oni-lan://192.168.1.2:27140").Port == 27140, "legacy LAN room code");
        Check(RoomAddress.Parse("192.168.1.2:27140").Port == 27140, "bare LAN address");
        Check(RoomAddress.Parse("oni://[::1]:27140").Host == "::1", "unified IPv6 room code");
        Check(RoomAddress.Parse("oni-lan://[::1]:27140").Host == "::1", "legacy IPv6 room code");
        Check(RoomAddress.Parse("[::1]:27140").Host == "::1", "bare IPv6 address");
        Check(RoomAddress.Parse("  ONI://STEAM/123456  ").LobbyId == 123456, "room codes ignore surrounding whitespace and scheme case");
        foreach (var bad in new[] { "http://example.com:80", "oni://a:70000", "oni-lan://a:70000",
            "oni://a:1?token=x", "oni-lan://a:1?token=x", "oni://a:1#fragment", "oni://a:1/path",
            "oni://steam/123456?token=x", "oni-steam://123456?token=x", "oni://steam/123456#fragment",
            "oni://steam/123456/extra", "oni://steam/+123456", "oni://steam/", "oni://steam/0", "oni-steam://0",
            "oni://user:pass@host:1", "oni-lan://user:pass@host:1" })
        {
            try { RoomAddress.Parse(bad); throw new Exception("invalid address accepted"); }
            catch (ArgumentException) { Check(true, "reject invalid room code"); }
        }
        try { Protocol.Decode(new byte[8193]); throw new Exception("oversize accepted"); }
        catch (InvalidDataException) { Check(true, "oversize rejected"); }
        byte[] unsupported = Protocol.Encode(new Packet { Kind = PacketKind.Ping });
        unsupported[4] = 99;
        try { Protocol.Decode(unsupported); throw new Exception("version accepted"); }
        catch (InvalidDataException) { Check(true, "wire version rejected"); }
        byte[] ping = Protocol.Encode(new Packet { Kind = PacketKind.Ping });
        try { Protocol.Decode(ping.Concat(new byte[] { 1 }).ToArray()); throw new Exception("trailing data accepted"); }
        catch (InvalidDataException) { Check(true, "trailing bytes rejected"); }
        for (int i = 0; i < ping.Length; i++)
        {
            try { Protocol.Decode(ping.Take(i).ToArray()); throw new Exception("truncated packet accepted"); }
            catch (InvalidDataException) { Check(true, "truncated packet rejected"); }
        }
    }

    private static int CheckBundledValueTuple(string path)
    {
        // Inspect the actual package without loading/executing game assemblies.
        using (var file = File.OpenRead(path))
        using (var pe = new PEReader(file))
        {
            var metadata = pe.GetMetadataReader();
            bool external = metadata.AssemblyReferences.Any(h => metadata.GetString(metadata.GetAssemblyReference(h).Name) == "System.ValueTuple");
            bool bundled = metadata.TypeDefinitions.Any(h =>
            {
                var type = metadata.GetTypeDefinition(h);
                return metadata.GetString(type.Namespace) == "System" && metadata.GetString(type.Name) == "ValueTuple`2";
            });
            Console.WriteLine("ValueTuple package metadata: bundled=" + bundled + "; external reference=" + external);
            return bundled && !external ? 0 : 1;
        }
    }

    private static int CheckNoProbe(string path)
    {
        using (var file = File.OpenRead(path))
        using (var pe = new PEReader(file))
        {
            var metadata = pe.GetMetadataReader();
            var forbidden = new HashSet<string>(StringComparer.Ordinal) {
                "RuntimeProbe", "NativeLifecycleMarker", "NativeLifecycleProbe", "NativeNavigationQuiescenceProbe", "NativeDuplicantRoundTripPreparation",
                "NativeRoundTripPreparationPermit", "NativeRoundTripPreparationReport", "NativeNavigationQuiescencePermit",
                "NativeDuplicantReadOnlyDiagnostics", "NativeNavigationExperimentEligibility",
                "NativeBuildingBridgePermit", "NativeBuildingBridgeProbe"
            };
            string[] found = metadata.TypeDefinitions.Select(h => metadata.GetString(metadata.GetTypeDefinition(h).Name))
                .Where(forbidden.Contains).Distinct(StringComparer.Ordinal).ToArray();
            Console.WriteLine("Runtime probe types present: " + (found.Length == 0 ? "none" : string.Join(", ", found)));
            return found.Length == 0 ? 0 : 1;
        }
    }

    private static void Translations()
    {
        DirectoryInfo root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "mods/OniOnline/localization/menu-draft.csv"))) root = root.Parent;
        Check(root != null, "locate project text catalog");
        var catalog = new TextCatalog(File.ReadLines(Path.Combine(root.FullName, "mods/OniOnline/localization/menu-draft.csv")));
        Check(catalog.Get("ui.send", "zh_klei") == "发送", "Simplified Chinese UI");
        Check(catalog.Get("ui.send", "zh-Hant") == "傳送", "Traditional Chinese UI");
        Check(catalog.Get("ui.send", "tchinese") == "傳送", "Steam Traditional locale");
        Check(catalog.Get("ui.send", "en-US") == "Send", "English UI");
        Check(catalog.Get("ui.send", "ko_klei") == "Send", "unsupported language falls back to English");
        Check(catalog.Get("ui.send", null) == "Send", "missing locale falls back to English");
        Check(catalog.Get("mode.online", "zh_klei") == "联机" && catalog.Get("mode.online", "zh-Hant") == "連線" &&
            catalog.Get("mode.online", "en-US") == "Online", "Online game category is localized in all three languages");
        Check(catalog.Get("menu.browse_lobbies", "zh_klei") == "浏览游戏大厅" &&
            catalog.Get("menu.browse_lobbies", "en-US") == "Browse game lobbies", "lobby entry describes browsing games rather than creating a system");
        Check(catalog.Get("room.join_running", "en-US") == "Join game", "late-join lobby action is localized");
        Check(catalog.Get("engine.preview", "zh_klei").StartsWith("你的星球与本地存档绑定。"), "Chinese engine status describes locally owned planets");
        Check(catalog.Get("engine.preview", "en-US").Contains("They cannot control your planet."), "English engine status preserves read-only authority");
        var quoted = new TextCatalog(new[] { "quoted,\"中文,状态\",\"繁體,狀態\",\"rooms, comms\"" });
        Check(quoted.Get("quoted", "zh_klei") == "中文,状态" && quoted.Get("quoted", "en-US") == "rooms, comms",
            "quoted CSV commas are retained as one field independently of product copy");
        Check(catalog.Get("engine.story", "en-US").StartsWith("A folded") &&
            catalog.Get("engine.story", "en-US").Contains("\"This device"), "CSV quoting unescapes story punctuation without displaying delimiters");
    }

    private static int CheckPLib(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var pe = new PEReader(stream))
        {
            var metadata = pe.GetMetadataReader();
            bool core = metadata.TypeDefinitions.Any(h => {
                var type = metadata.GetTypeDefinition(h);
                return metadata.GetString(type.Name) == "PUtil" && metadata.GetString(type.Namespace) == "PeterHan.PLib.Core";
            });
            bool options = metadata.TypeDefinitions.Any(h => metadata.GetString(metadata.GetTypeDefinition(h).Name) == "OnlineOptions");
            bool external = metadata.AssemblyReferences.Any(h => metadata.GetString(metadata.GetAssemblyReference(h).Name) == "PLib");
            Console.WriteLine("PLib embedded: " + core + "; OniOnline options: " + options + "; external PLib reference: " + external);
            return core && options && !external ? 0 : 1;
        }
    }

    private static void Discovery()
    {
        using (var host = new LanDiscovery(true, 0))
        using (var raw = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            host.Advertise("深空 LAN", 27140, 3, true);
            byte[] query = System.Text.Encoding.UTF8.GetBytes("ONIONLINE_DISCOVER_1");
            raw.Send(query, query.Length, new IPEndPoint(IPAddress.Loopback, host.Port));
            var reply = raw.ReceiveAsync();
            Check(reply.Wait(3000), "real UDP discovery reply");
            string text = System.Text.Encoding.UTF8.GetString(reply.Result.Buffer);
            Check(text.Contains("深空 LAN") && text.Contains("27140\n3\n1"), "discovery advertises name and capacity/password flag");
            host.Advertise("深空 LAN", 27140, 3, true, true);
            byte[] modernQuery = System.Text.Encoding.UTF8.GetBytes("ONIONLINE_DISCOVER_2");
            raw.Send(modernQuery, modernQuery.Length, new IPEndPoint(IPAddress.Loopback, host.Port));
            reply = raw.ReceiveAsync();
            Check(reply.Wait(3000) && System.Text.Encoding.UTF8.GetString(reply.Result.Buffer).EndsWith("深空 LAN\n1"),
                "modern LAN discovery includes in-progress rooms for late joining");
        }
        using (var scanner = new LanDiscovery(false))
        using (var raw = new UdpClient())
        {
            byte[] advert = System.Text.Encoding.UTF8.GetBytes("ONIONLINE_ROOM_1\n27140\n2\n0\nTest LAN");
            raw.Send(advert, advert.Length, new IPEndPoint(IPAddress.Loopback, scanner.Port));
            RoomListing room = null;
            var timer = Stopwatch.StartNew();
            while (room == null && timer.ElapsedMilliseconds < 3000) { scanner.TryReceive(out room); Thread.Sleep(2); }
            Check(room != null, "browser receives UDP listing");
            Check(room.Code == "oni://127.0.0.1:27140" && room.Players == 2, "unified room code derives IP from datagram sender");
            Check(!room.Started && room.CanJoin, "legacy LAN listing remains joinable with a safe waiting default");
            advert = System.Text.Encoding.UTF8.GetBytes("ONIONLINE_ROOM_1\n27140\n4\n0\nRunning LAN\n1");
            raw.Send(advert, advert.Length, new IPEndPoint(IPAddress.Loopback, scanner.Port));
            room = null;
            timer.Restart();
            while (room == null && timer.ElapsedMilliseconds < 3000) { scanner.TryReceive(out room); Thread.Sleep(2); }
            Check(room != null && room.Started && room.CanJoin, "running LAN listing remains joinable beyond the old four-player cap");
        }
    }

    private static void ChatAndStart()
    {
        Reset();
        int port;
        var host = Host(out port);
        var a = Join(port, "A");
        var b = Join(port, "B");
        var c = Join(port, "C");
        Until(() => sessions.All(s => s.Joined && s.Players.Length == 4), "four-player handshake and roster");
        Check(a.SendChat("大家好 <b>原样显示</b> 🚀"), "client chat queued");
        Check(host.SendChat("host message"), "host chat queued");
        Until(() => sessions.All(s => s.Chat.Length == 2), "chat broadcast all participants");
        foreach (var session in sessions)
        {
            Check(session.Chat[0].Name == "房主" || session.Chat[0].Name == "A", "canonical name");
            Check(session.Chat[0].Sequence < session.Chat[1].Sequence, "ordered chat");
            Check(session.Chat.Any(l => l.Name == "A" && l.Text.Contains("<b>")), "wire preserves text, UI must disable rich text");
        }
        Check(!a.StartGame(), "guest cannot start");
        int events = 0;
        foreach (var session in sessions) session.GameStartRequested += () => events++;
        Check(host.StartGame(), "host starts");
        Check(!host.StartGame(), "host cannot start twice");
        Until(() => sessions.All(s => s.Started), "start broadcast");
        Check(events == 4, "one start event per participant");
    }

    private static void WrongCredentials()
    {
        Reset();
        int port;
        var host = Host(out port);
        var wrong = Join(port, "Wrong", "wrong");
        Until(() => wrong.Closed, "wrong password rejected");
        Check(wrong.LastError == "wrong_password", "password failure code");
        Check(host.Players.Length == 1, "unauthenticated player absent");
        var version = Join(port, "Old", "房间密钥", "old");
        Until(() => version.Closed, "game version mismatch rejected");
        Check(version.LastError == "version_mismatch", "version failure code");
        var valid = Join(port, "Valid");
        Until(() => valid.Joined && host.Players.Length == 2, "room usable after bad peers");
    }

    private static void CapacityAndAuthority()
    {
        Reset();
        int port;
        var host = Host(out port);
        for (int i = 0; i < 11; i++) Join(port, "Player" + i);
        Until(() => sessions.All(s => s.Joined && s.Players.Length == 12), "twelve real TCP peers converge without a four-player cap");
        Check(host.Players.Select(p => p.Id).Distinct().Count() == 12, "larger room has distinct authenticated seats");
        Reset();
        host = Host(out port);
        var transport = TcpRoomTransport.Join(address, port);
        var attacker = new RoomSession(transport, Profile("Attacker"), "房间密钥");
        sessions.Add(attacker);
        Until(() => attacker.Joined && host.Players.Length == 2, "attacker joins normally");
        transport.Send(transport.HostPeer, Protocol.Encode(new Packet { Kind = PacketKind.StartGame }));
        Until(() => attacker.Closed, "forged start rejected");
        Check(!host.Started, "forged packet cannot change host phase");
        Check(attacker.LastError == "host_only", "authority failure code");
    }

    private static void DisconnectAndRejoin()
    {
        Reset();
        int port;
        var host = Host(out port);
        var client = Join(port, "Guest");
        Until(() => client.Joined && host.Players.Length == 2, "initial join");
        Check(client.RequestLeave(), "guest requests a graceful acknowledged departure");
        Until(() => host.Players.Length == 1, "disconnect removes roster entry");
        client = Join(port, "Guest");
        Until(() => client.Joined && host.Players.Length == 2, "join again after disconnect");
        host.StartGame();
        Until(() => client.Started, "start before late join");
        var late = Join(port, "Late");
        Until(() => late.Joined, "late join allowed");
        Check(late.Started, "late join sees room phase");
        host.Dispose();
        Until(() => client.Closed && late.Closed, "host loss is surfaced");
    }

    private static void Framing()
    {
        Reset();
        using (var host = TcpRoomTransport.Host(IPAddress.Any, 0))
        using (var raw = new TcpClient(address, host.Port))
        {
            string peer = null;
            var timer = Stopwatch.StartNew();
            while (peer == null && timer.ElapsedMilliseconds < 3000)
            {
                TransportEvent item;
                if (host.TryReceive(out item) && item.Kind == TransportEventKind.Connected) peer = item.Peer;
                Thread.Sleep(2);
            }
            Check(peer != null, "raw peer connected");
            byte[] bytes = Protocol.Encode(new Packet { Kind = PacketKind.Ping });
            byte[] header = { 0, 0, (byte)(bytes.Length >> 8), (byte)bytes.Length };
            foreach (byte part in header) raw.GetStream().WriteByte(part);
            foreach (byte part in bytes) raw.GetStream().WriteByte(part);
            TransportEvent message = null;
            timer.Restart();
            while (message == null && timer.ElapsedMilliseconds < 3000)
            {
                TransportEvent item;
                if (host.TryReceive(out item) && item.Kind == TransportEventKind.Message) message = item;
                Thread.Sleep(2);
            }
            Check(message != null && Protocol.Decode(message.Data).Kind == PacketKind.Ping, "fragmented header and payload reassembled");
            raw.GetStream().Write(new byte[] { 127, 255, 255, 255 }, 0, 4);
            bool disconnected = false;
            timer.Restart();
            while (!disconnected && timer.ElapsedMilliseconds < 3000)
            {
                TransportEvent item;
                if (host.TryReceive(out item) && item.Kind == TransportEventKind.Disconnected) disconnected = true;
                Thread.Sleep(2);
            }
            Check(disconnected, "oversized frame disconnects without payload allocation");
        }
    }

    private static ProcessStartInfo SelfStart()
    {
        // Reuse this host, including an SDK supplied by absolute path. An apphost
        // already knows its managed entry point; dotnet needs the assembly path.
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing test host path.");
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        return start;
    }

    private static void TcpWorkerBudgetProcess()
    {
        Reset();
        var start = SelfStart();
        start.ArgumentList.Add("--tcp-worker-budget");
        using (var child = Process.Start(start))
        {
            if (!child.WaitForExit(20000)) { child.Kill(true); throw new Exception("TCP worker-budget process timed out."); }
            Check(child.ExitCode == 0, "bounded TCP workers: " + child.StandardError.ReadToEnd());
            Console.Write(child.StandardOutput.ReadToEnd());
        }
    }

    private static void MultiProcess()
    {
        Reset();
        int port;
        var host = Host(out port);
        var children = new List<Process>();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var start = SelfStart();
                start.ArgumentList.Add("--peer"); start.ArgumentList.Add(address);
                start.ArgumentList.Add(port.ToString()); start.ArgumentList.Add("Process" + i);
                children.Add(Process.Start(start));
            }
            Until(() => host.Players.Length == 4 && host.Chat.Length == 3, "separate processes join and chat", 10000);
            Check(host.Chat.Select(c => c.Name).Distinct().Count() == 3, "three authenticated process senders");
            host.StartGame();
            Until(() => children.All(c => c.HasExited), "child processes receive start and exit", 10000);
            foreach (var child in children)
                Check(child.ExitCode == 0, "child passed: " + child.StandardError.ReadToEnd());
        }
        finally
        {
            foreach (var child in children)
            {
                if (!child.HasExited) child.Kill(true);
                child.Dispose();
            }
        }
    }

    private static int PeerProcess(string[] args)
    {
        var profile = Profile(args[3], args.Length > 4 ? args[4] : "744825");
        if (args.Length > 5) profile.CycleTrimVersion = args[5];
        int holdSeconds = args.Length > 7 ? int.Parse(args[7]) : 0;
        using (var client = new RoomSession(TcpRoomTransport.Join(args[1], int.Parse(args[2])), profile, args.Length > 6 ? args[6] : "房间密钥"))
        using (var worlds = args.Length > 8 ? new OniOnline.Multiplayer.WorldReplication(client, new ObserverSource(), allowRemoteSnapshots: true) : null)
        {
            bool sent = false;
            long observed = 0;
            double nextRequest = 0;
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < Math.Max(12000, holdSeconds * 1000))
            {
                client.Pump();
                if (worlds != null)
                {
                    double now = timer.Elapsed.TotalSeconds;
                    worlds.Pump(now);
                    if (client.Joined && now >= nextRequest)
                    { worlds.RequestWorld(client.Players[0].Id); nextRequest = now + 5; }
                    var frame = worlds.Worlds.FirstOrDefault(w => !w.IsLocal && w.Snapshot != null)?.Snapshot;
                    if (frame != null && frame.Sequence > observed)
                    {
                        observed = frame.Sequence;
                        string json = System.Text.Json.JsonSerializer.Serialize(new {
                            boundary = "real game publisher, .NET read-only observer", sequence = frame.Sequence,
                            owner = frame.World.OwnerId, epoch = frame.World.Epoch, cells = frame.Cells.Length,
                            dupes = frame.Dupes.Length, width = frame.World.Width, height = frame.World.Height });
                        File.WriteAllText(args[8] + ".tmp", json);
                        File.Move(args[8] + ".tmp", args[8], true);
                    }
                }
                if (client.Closed) throw new Exception(client.LastError);
                if (client.Joined && !sent) sent = client.SendChat("hello from " + args[3]);
                if (client.Started && client.Chat.Length >= 1 && holdSeconds == 0) return 0;
                Thread.Sleep(5);
            }
            if (holdSeconds > 0 && sent) return 0;
            throw new Exception("peer timed out");
        }
    }

    private sealed class ObserverSource : OniOnline.Multiplayer.IWorldSource
    {
        public bool IsAvailable { get { return false; } }
        public OniOnline.Multiplayer.WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true)
        { throw new InvalidOperationException("observer_has_no_native_colony"); }
    }
}
