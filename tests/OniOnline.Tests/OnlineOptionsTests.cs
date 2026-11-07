using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline;
using OniOnline.Network;
using OniOnline.Sdk;

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
        check(defaults.ActiveGameplayId == OniOnline.Sdk.GameplayFramework.BuiltInId &&
            OniOnline.Sdk.GameplayFramework.IsEnabled(OniOnline.Sdk.GameplayFramework.BuiltInId),
            "gameplay options default to built-in chat");
        var disabledGameplay = new OnlineOptions { DisabledGameplays = OniOnline.Sdk.GameplayFramework.BuiltInId }.Normalize();
        check(!OniOnline.Sdk.GameplayFramework.IsEnabled(OniOnline.Sdk.GameplayFramework.BuiltInId),
            "disabled gameplay list can turn off built-in chat");
        var reenabledGameplay = new OnlineOptions().Normalize();
        check(reenabledGameplay.ActiveGameplayId == OniOnline.Sdk.GameplayFramework.BuiltInId &&
            OniOnline.Sdk.GameplayFramework.IsEnabled(OniOnline.Sdk.GameplayFramework.BuiltInId),
            "clearing the disable list turns built-in chat back on");
        GameplayToggles(check);
        defaults.OpenDevGuide?.Invoke(null);
        check(UnityEngine.Application.LastOpenedUrl != null && UnityEngine.Application.LastOpenedUrl.Contains("SDK.md"),
            "OpenDevGuide opens the SDK developer documentation URL");
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

    private sealed class ToggleRules : IGameplayRules
    {
        public string Id { get { return "test.options-toggle"; } }
        public string Version { get { return "1"; } }
        public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current) { return null; }
    }

    private static void GameplayToggles(Action<bool, string> check)
    {
        if (!GameplayFramework.RegisteredIds.Contains("test.options-toggle"))
            GameplayFramework.Register(new ToggleRules());
        var options = new OnlineOptions().Normalize();
        var entries = options.CreateOptions().OfType<GameplayToggleEntry>().ToArray();
        var chat = entries.First(entry => entry.GameplayId == GameplayFramework.BuiltInId);
        var extra = entries.First(entry => entry.GameplayId == "test.options-toggle");
        check(!chat.RestartRequired && !extra.RestartRequired,
            "gameplay checkboxes do not ask for a game restart");
        chat.ReadFrom(options);
        extra.ReadFrom(options);
        check((bool)chat.Value && (bool)extra.Value && entries.Length == GameplayFramework.RegisteredIds.Length,
            "each registered gameplay appears checked when it is enabled");
        extra.Value = false;
        check(extra.WriteTo(options) && options.DisabledGameplays.Split(';').Contains("test.options-toggle"),
            "clearing a checkbox records that gameplay as disabled");
        options.OnOptionsChanged();
        check(!GameplayFramework.IsEnabled("test.options-toggle") && GameplayFramework.IsEnabled(GameplayFramework.BuiltInId) &&
            GameplayFramework.SelectedId == GameplayFramework.BuiltInId,
            "confirming the options disables that gameplay immediately");
        extra.Value = true;
        check(extra.WriteTo(options) && !OnlineOptions.ParseDisabled(options.DisabledGameplays).Contains("test.options-toggle"),
            "checking the box removes that gameplay from the disabled list");
        options.OnOptionsChanged();
        check(GameplayFramework.IsEnabled("test.options-toggle") && GameplayFramework.SelectedId == GameplayFramework.BuiltInId,
            "checking the box enables that gameplay without restarting");
        new OnlineOptions { DisabledGameplays = "test.options-toggle,onionline.chat" }.Normalize();
        check(!GameplayFramework.IsEnabled("test.options-toggle") && !GameplayFramework.IsEnabled(GameplayFramework.BuiltInId),
            "a saved comma-separated disable list still turns those gameplays off");
        new OnlineOptions().Normalize();
        check(GameplayFramework.IsEnabled("test.options-toggle") && GameplayFramework.IsEnabled(GameplayFramework.BuiltInId) &&
            GameplayFramework.SelectedId == GameplayFramework.BuiltInId,
            "an empty disable list turns every registered gameplay back on");
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
