using System;
using System.Linq;
using OniOnline.Network;

internal static class ConnectionLifecycleTests
{
    internal static void Run(Action<bool, string> check)
    {
        LeavingStopsApplicationTraffic(check);
        DisconnectedSendersCannotBeRelayed(check);
        SessionSnapshotsAreIsolated(check);
        ReconnectDiagnostics(check);
        TcpFailureCategories(check);
    }

    private static SessionProfile Profile(string name)
    { return new SessionProfile { Name = name }; }

    private static void Step(DeterministicRoomNetwork network, params RoomSession[] rooms)
    {
        network.Advance(0.05);
        foreach (var room in rooms) room.Pump(network.Now);
    }

    private static void Join(DeterministicRoomNetwork network, params RoomSession[] rooms)
    {
        for (int i = 0; i < 100 && rooms.Any(room => !room.Joined); i++) Step(network, rooms);
        if (rooms.Any(room => !room.Joined)) throw new Exception("lifecycle fixture did not join");
        for (int i = 0; i < 4; i++) Step(network, rooms);
    }

    private static void LeavingStopsApplicationTraffic(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(7001))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Lifecycle"))
        using (var guest = new RoomSession(network.Connect(), Profile("Guest"), ""))
        {
            check(!host.ForwardApplication(host.LocalPlayerId, null, new byte[] { 0 }),
                "broadcast with no admitted recipients is not reported as accepted");
            Join(network, host, guest);
            int delivered = 0;
            guest.ApplicationMessageReceived += (_, bytes) => delivered++;
            check(host.SendApplicationTo(guest.LocalPlayerId, new byte[] { 1 }),
                "fixture queues a host application message before leave");
            check(guest.RequestLeave(), "guest begins graceful leave");
            check(!guest.SendApplication(new byte[] { 2 }),
                "a leaving guest refuses new application traffic");
            Step(network, host, guest);
            check(delivered == 0, "a leaving guest discards queued gameplay messages while completing leave");
            for (int i = 0; i < 8 && !guest.Closed; i++) Step(network, host, guest);
            check(guest.Closed && guest.LastError == "left_room", "control acknowledgements still complete graceful leave");
        }
    }

    private static void DisconnectedSendersCannotBeRelayed(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(7002))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Lifecycle"))
        using (var author = new RoomSession(network.Connect(), Profile("Author"), ""))
        using (var observer = new RoomSession(network.Connect(), Profile("Observer"), ""))
        {
            Join(network, host, author, observer);
            string authorId = author.LocalPlayerId;
            check(host.ForwardApplication(authorId, observer.LocalPlayerId, new byte[] { 1 }),
                "an active author can be relayed");
            author.Dispose();
            Step(network, host, observer);
            check(host.Players.Any(player => player.Id == authorId && !player.Connected),
                "disconnected author retains a reconnect seat");
            check(!host.ForwardApplication(authorId, observer.LocalPlayerId, new byte[] { 2 }),
                "a reconnect reservation cannot authorize new relayed traffic");
            check(!host.SendApplicationTo(null, new byte[] { 3 }) &&
                !host.ForwardApplication(null, observer.LocalPlayerId, new byte[] { 4 }),
                "invalid application target and sender return false");
        }
    }

    private static void SessionSnapshotsAreIsolated(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(7003))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Lifecycle"))
        using (var guest = new RoomSession(network.Connect(), Profile("Guest"), ""))
        {
            Join(network, host, guest);
            string id = guest.LocalPlayerId;
            var snapshot = host.Players;
            var exposed = snapshot.First(player => player.Id == id);
            exposed.Id = "modified";
            exposed.Name = "modified";
            exposed.Connected = false;
            check(host.Players.Any(player => player.Id == id && player.Name == "Guest" && player.Connected),
                "editing a public roster snapshot preserves live session identities");
            check(host.SendChat("original"), "fixture publishes a chat line");
            var line = host.Chat[0];
            line.Text = "modified";
            line.Sequence = -1;
            check(host.Chat[0].Text == "original" && host.Chat[0].Sequence > 0,
                "editing a public chat snapshot preserves retained history");
        }
    }
    private sealed class ProbeTransport : IRoomTransport
    {
        private readonly System.Collections.Generic.Queue<TransportEvent> events = new System.Collections.Generic.Queue<TransportEvent>();
        internal bool Disposed;
        public bool IsHost { get; set; }
        public string HostPeer { get { return "host"; } }
        internal ProbeTransport(string error = null)
        { if (error != null) events.Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = error }); }
        public bool TryReceive(out TransportEvent item)
        {
            if (events.Count == 0) { item = null; return false; }
            item = events.Dequeue(); return true;
        }
        public bool Send(string peer, byte[] data) { return !Disposed; }
        public void Disconnect(string peer) { }
        public void Poll() { }
        public void Dispose() { Disposed = true; events.Clear(); }
    }

    private static void ReconnectDiagnostics(Action<bool, string> check)
    {
        var first = new ProbeTransport("connection_refused");
        var retry = new ProbeTransport();
        var options = new SessionOptions { HandshakeTimeoutSeconds = 1, ReconnectWindowSeconds = 5, RetryJitter = () => 0.5 };
        using (var room = new RoomSession(first, Profile("Guest"), "", reconnect: () => retry, options: options))
        {
            room.Pump(0);
            check(room.Reconnecting && room.LastConnectionError == "connection_refused" && first.Disposed,
                "reconnect preserves the actual transport failure and releases its old transport");
            check(!room.ConnectionAttemptInProgress && room.RetryInSeconds == 1,
                "scheduled reconnect exposes a countdown before starting a connection");
            CheckStatusText(room, check);
            room.Pump(1.1);
            check(room.ConnectionAttemptInProgress && room.RetryAttempt == 1 && room.RetryInSeconds == 0,
                "an active reconnect is distinguished from a zero-second retry countdown");
            room.Pump(2.2);
            check(!room.ConnectionAttemptInProgress && room.LastConnectionError == "connect_timeout" && retry.Disposed,
                "a silent reconnect reports timeout and closes the attempt");
            room.Pump(5.1);
            check(room.Closed && room.LastError == "reconnect_expired" && room.LastErrorDetail == "connect_timeout",
                "terminal reconnect expiry retains the last actionable cause");
        }
        var invalid = new ProbeTransport { IsHost = true };
        using (var room = new RoomSession(new ProbeTransport("connect_failed"), Profile("Guest"), "",
            reconnect: () => invalid, options: options))
        {
            room.Pump(0); room.Pump(1.1);
            check(invalid.Disposed && !room.ConnectionAttemptInProgress,
                "a rejected reconnect transport is disposed instead of leaking a listener");
        }
    }

    private static void CheckStatusText(RoomSession room, Action<bool, string> check)
    {
        var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        const string file = "mods/OniOnline/localization/menu-draft.csv";
        while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, file))) root = root.Parent;
        if (root == null) throw new Exception("localization fixture not found");
        var catalog = new OniOnline.Core.TextCatalog(System.IO.File.ReadLines(System.IO.Path.Combine(root.FullName, file)));
        foreach (string language in new[] { "zh-CN", "zh-TW", "en" })
        {
            Func<string, string> text = key => catalog.Get(key, language);
            string message = OniOnline.Core.ConnectionStatusText.Reconnecting(room, text);
            check(message.Contains("1") && message.Contains(text("error.connection_refused")) &&
                !message.Contains("ui.reconnect"), "reconnect progress and its cause reach the " + language + " UI");
            string expired = OniOnline.Core.ConnectionStatusText.Error("reconnect_expired", "connect_timeout", text);
            check(expired.Contains(text("error.connect_timeout")) && !expired.Contains("connect_timeout"),
                "terminal failure translates its retained cause in " + language);
            check(OniOnline.Core.ConnectionStatusText.Error("player_banned", "connection_refused", text).EndsWith("connection_refused"),
                "moderation text is not interpreted as a transport code");
            foreach (string reason in new[] { "connection_refused", "address_not_found", "connect_timeout",
                "network_unreachable", "network_blocked", "peer_timeout", "handshake_timeout" })
                check(!OniOnline.Core.ConnectionStatusText.Error(reason, null, text).StartsWith("error."),
                    "connection cause has a readable " + language + " translation: " + reason);
        }
    }

    private static void TcpFailureCategories(Action<bool, string> check)
    {
        check(TcpRoomTransport.ConnectFailure(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)) == "connection_refused",
            "TCP distinguishes a refused port");
        check(TcpRoomTransport.ConnectFailure(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound)) == "address_not_found",
            "TCP distinguishes name resolution failure");
        check(TcpRoomTransport.ConnectFailure(new TimeoutException()) == "connect_timeout" &&
            TcpRoomTransport.ConnectFailure(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.TimedOut)) == "connect_timeout",
            "TCP deadline and socket timeout use the same actionable code");
        check(TcpRoomTransport.ConnectFailure(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.NetworkUnreachable)) == "network_unreachable" &&
            TcpRoomTransport.ConnectFailure(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.AccessDenied)) == "network_blocked",
            "TCP distinguishes routing and local permission failures");
        check(TcpRoomTransport.ConnectFailure(new Exception("private endpoint")) == "connect_failed",
            "unexpected TCP errors never disclose exception text");
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using (var transport = TcpRoomTransport.Join(System.Net.IPAddress.Loopback.ToString(), port))
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            TransportEvent observed = null;
            while (timer.ElapsedMilliseconds < 4000)
            {
                if (transport.TryReceive(out observed) && observed.Kind == TransportEventKind.Error) break;
                System.Threading.Thread.Sleep(5);
            }
            check(observed != null && observed.Kind == TransportEventKind.Error && observed.Error == "connection_refused",
                "a real loopback connection refusal reaches the transport error event");
            check(!transport.Send(null, new byte[] { 1 }), "a null TCP recipient is rejected without throwing");
        }
    }

}
