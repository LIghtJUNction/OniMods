using System;
using System.Linq;
using OniOnline;
using OniOnline.Network;

internal static class OnlineOptionsTests
{
    internal static void Run(Action<bool, string> check)
    {
        var defaults = new OnlineOptions().Normalize();
        check(defaults.VoiceEnabled && defaults.VoiceVolume == 100,
            "voice defaults enable explicit push-to-talk with normal playback volume");
        check(new OnlineOptions { VoiceVolume = -1 }.Normalize().VoiceVolume == 0 &&
            new OnlineOptions { VoiceVolume = 101 }.Normalize().VoiceVolume == 100,
            "voice playback volume is bounded even when edited manually");
        SavedOptions(check);
        OnlineInputGateTests.Run(check);
        check(defaults.PreferSteamLobbies && defaults.DefaultLanPort == 27140 && defaults.ChatFontSize == 18,
            "network defaults and readable chat size retain existing behavior");
        var session = defaults.CreateSessionOptions();
        check(session.HandshakeTimeoutSeconds == 30 && session.PeerTimeoutSeconds == 30 &&
            session.ReconnectWindowSeconds == 300 && session.SeatReservationSeconds == 300 && session.ChatHistoryLimit == 100,
            "default session timing and chat history remain unchanged");

        var low = new OnlineOptions { DefaultLanPort = int.MinValue, HandshakeTimeoutSeconds = int.MinValue,
            PeerTimeoutSeconds = int.MinValue, ReconnectWindowSeconds = int.MinValue,
            ChatHistoryLimit = int.MinValue, ChatFontSize = int.MinValue }.Normalize();
        check(low.DefaultLanPort == 1 && low.HandshakeTimeoutSeconds == 15 && low.PeerTimeoutSeconds == 15 &&
            low.ReconnectWindowSeconds == 30 && low.ChatHistoryLimit == 20 && low.ChatFontSize == 14,
            "manually edited out-of-range settings cannot create zero waits or unbounded history");
        var high = new OnlineOptions { DefaultLanPort = int.MaxValue, HandshakeTimeoutSeconds = int.MaxValue,
            PeerTimeoutSeconds = int.MaxValue, ReconnectWindowSeconds = int.MaxValue,
            ChatHistoryLimit = int.MaxValue, ChatFontSize = int.MaxValue }.Normalize();
        check(high.DefaultLanPort == 65535 && high.HandshakeTimeoutSeconds == 120 && high.PeerTimeoutSeconds == 180 &&
            high.ReconnectWindowSeconds == 1800 && high.ChatHistoryLimit == 500 && high.ChatFontSize == 28,
            "manually edited large settings keep bounded waits and text layout");
        var custom = new OnlineOptions { HandshakeTimeoutSeconds = 75, PeerTimeoutSeconds = 90,
            ReconnectWindowSeconds = 30, ChatHistoryLimit = 240 }.CreateSessionOptions();
        check(custom.HandshakeTimeoutSeconds == 75 && custom.PeerTimeoutSeconds == 90 &&
            custom.ReconnectWindowSeconds == 90 && custom.SeatReservationSeconds == 90 && custom.ChatHistoryLimit == 240,
            "session receives chosen values and grace cannot expire before a silent peer is detected");

        HistoryRetention(check);
        LargeHistoryReplay(check);
        HandshakeWaitIsIndependent(check);
        InvalidHistoryLimit(check, 0);
        InvalidHistoryLimit(check, SessionOptions.MaxChatHistoryLimit + 1);
    }

    private static SessionProfile Profile(string name)
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    private static void SavedOptions(Action<bool, string> check)
    {
        OnlineOptions received = null;
        Action<OnlineOptions> changed = value => received = value;
        OnlineOptions.Saved += changed;
        try
        {
            var options = new OnlineOptions { VoiceEnabled = false, VoiceVolume = int.MinValue };
            options.OnOptionsChanged();
            check(ReferenceEquals(received, options) && !received.VoiceEnabled && received.VoiceVolume == 0,
                "saving mod options emits normalized voice settings for immediate mute and volume changes");
        }
        finally { OnlineOptions.Saved -= changed; }
    }

    private static void HistoryRetention(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(1701))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "History", options: new SessionOptions { ChatHistoryLimit = 3 }))
        {
            for (int i = 0; i < 8; i++)
            {
                network.Advance(3);
                host.Pump(network.Now);
                check(host.SendChat("line-" + i), "bounded-history host accepts normal paced chat");
            }
            check(host.Chat.Select(line => line.Text).SequenceEqual(new[] { "line-5", "line-6", "line-7" }),
                "history drops only oldest messages at the configured capacity");
            check(host.Chat.Select(line => line.Sequence).SequenceEqual(new long[] { 6, 7, 8 }),
                "history pruning preserves protocol sequence numbers");
        }
    }

    private static void LargeHistoryReplay(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(1702))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "History",
            options: new SessionOptions { ChatHistoryLimit = SessionOptions.MaxChatHistoryLimit }))
        {
            for (int i = 0; i < SessionOptions.MaxChatHistoryLimit; i++)
            {
                network.Advance(3); host.Pump(network.Now);
                if (!host.SendChat("history-" + i)) throw new Exception("history fixture paced chat rejected");
            }
            using (var client = new RoomSession(network.Connect(), Profile("Client"), "",
                options: new SessionOptions { ChatHistoryLimit = 20 }))
            {
                double deadline = network.Now + 20;
                while (network.Now < deadline && (client.Chat.Length != 20 || client.Chat.Last().Sequence != SessionOptions.MaxChatHistoryLimit))
                { network.Advance(0.025); host.Pump(network.Now); client.Pump(network.Now); }
                check(client.Joined && !client.Closed && client.Chat.Length == 20,
                    "a joining client with smaller local history receives a large host replay without disconnecting");
                check(client.Chat.First().Sequence == 481 && client.Chat.Last().Sequence == 500,
                    "client retains its own latest-message budget after full history catch-up");
                check(host.Chat.Length == 500, "host's selected history budget remains intact after replay");
            }
        }
    }

    private static void HandshakeWaitIsIndependent(Action<bool, string> check)
    {
        using (var network = new DeterministicRoomNetwork(1703))
        using (var host = new RoomSession(network.Host, Profile("Host"), "", "Slow handshake",
            options: new SessionOptions { HandshakeTimeoutSeconds = 60, PeerTimeoutSeconds = 15 }))
        using (var client = new RoomSession(network.Connect(), Profile("Client"), "",
            options: new SessionOptions { HandshakeTimeoutSeconds = 60, PeerTimeoutSeconds = 15 }))
        {
            network.Settings.Delay = 10;
            double deadline = network.Now + 45;
            while (network.Now < deadline && !client.Joined)
            { network.Advance(0.05); host.Pump(network.Now); client.Pump(network.Now); }
            check(client.Joined && !client.Closed,
                "a deliberately longer authentication wait is not cut short by the established-peer timeout");
        }
    }

    private static void InvalidHistoryLimit(Action<bool, string> check, int capacity)
    {
        using (var network = new DeterministicRoomNetwork(1704))
        {
            bool rejected = false;
            try
            {
                using (var host = new RoomSession(network.Host, Profile("Host"), "", "Invalid history",
                    options: new SessionOptions { ChatHistoryLimit = capacity })) { }
            }
            catch (ArgumentException error) { rejected = error.Message == "invalid_session_options"; }
            check(rejected, "session rejects invalid history capacity " + capacity);
        }
    }
}
