using System;
using System.Collections.Generic;
using OniOnline.Network;
using PeterHan.PLib.Options;

namespace OniOnline
{
    [ConfigFile("OniOnlineOptions.json", true)]
    public sealed class OnlineOptions : IOptions
    {
        internal static event System.Action<OnlineOptions> Saved;
        [Option("STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.NAME", "STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.DESC")]
        [RestartRequired]
        public bool PreferSteamLobbies { get; set; } = true;

        [Option("STRINGS.ONIONLINE.OPTIONS.LANPORT.NAME", "STRINGS.ONIONLINE.OPTIONS.LANPORT.DESC")]
        [Limit(1, 65535)]
        [RestartRequired]
        public int DefaultLanPort { get; set; } = 27140;

        [Option("STRINGS.ONIONLINE.OPTIONS.HANDSHAKE.NAME", "STRINGS.ONIONLINE.OPTIONS.HANDSHAKE.DESC")]
        [Limit(15, 120)]
        public int HandshakeTimeoutSeconds { get; set; } = 30;

        [Option("STRINGS.ONIONLINE.OPTIONS.PEERTIMEOUT.NAME", "STRINGS.ONIONLINE.OPTIONS.PEERTIMEOUT.DESC")]
        [Limit(15, 180)]
        public int PeerTimeoutSeconds { get; set; } = 30;

        [Option("STRINGS.ONIONLINE.OPTIONS.RECONNECT.NAME", "STRINGS.ONIONLINE.OPTIONS.RECONNECT.DESC")]
        [Limit(30, 1800)]
        public int ReconnectWindowSeconds { get; set; } = 300;

        [Option("STRINGS.ONIONLINE.OPTIONS.CHATHISTORY.NAME", "STRINGS.ONIONLINE.OPTIONS.CHATHISTORY.DESC")]
        [Limit(20, SessionOptions.MaxChatHistoryLimit)]
        public int ChatHistoryLimit { get; set; } = 100;

        [Option("STRINGS.ONIONLINE.OPTIONS.CHATFONT.NAME", "STRINGS.ONIONLINE.OPTIONS.CHATFONT.DESC")]
        [Limit(14, 28)]
        [RestartRequired]
        public int ChatFontSize { get; set; } = 18;

        [Option("STRINGS.ONIONLINE.OPTIONS.VOICEENABLED.NAME", "STRINGS.ONIONLINE.OPTIONS.VOICEENABLED.DESC")]
        public bool VoiceEnabled { get; set; } = true;

        [Option("STRINGS.ONIONLINE.OPTIONS.VOICEVOLUME.NAME", "STRINGS.ONIONLINE.OPTIONS.VOICEVOLUME.DESC")]
        [Limit(0, 100)]
        public int VoiceVolume { get; set; } = 100;

        [Option("STRINGS.ONIONLINE.OPTIONS.DEVGUIDE.NAME", "STRINGS.ONIONLINE.OPTIONS.DEVGUIDE.DESC", "STRINGS.ONIONLINE.OPTIONS.CATEGORY_GAMEPLAY")]
        public System.Action<object> OpenDevGuide => _ =>
        {
            try
            {
                UnityEngine.Application.OpenURL("https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniOnline/docs/SDK.md");
            }
            catch (Exception) { }
        };

        [Option("STRINGS.ONIONLINE.OPTIONS.ACTIVEGAMEPLAY.NAME", "STRINGS.ONIONLINE.OPTIONS.ACTIVEGAMEPLAY.DESC", "STRINGS.ONIONLINE.OPTIONS.CATEGORY_GAMEPLAY")]
        public string ActiveGameplayId { get; set; } = Sdk.GameplayFramework.BuiltInId;

        [Option("STRINGS.ONIONLINE.OPTIONS.DISABLEDGAMEPLAYS.NAME", "STRINGS.ONIONLINE.OPTIONS.DISABLEDGAMEPLAYS.DESC", "STRINGS.ONIONLINE.OPTIONS.CATEGORY_GAMEPLAY")]
        public string DisabledGameplays { get; set; } = "";

        public IEnumerable<IOptionsEntry> CreateOptions() { yield break; }

        public void OnOptionsChanged() { Saved?.Invoke(Normalize()); }

        internal OnlineOptions Normalize()
        {
            DefaultLanPort = Clamp(DefaultLanPort, 1, 65535);
            HandshakeTimeoutSeconds = Clamp(HandshakeTimeoutSeconds, 15, 120);
            PeerTimeoutSeconds = Clamp(PeerTimeoutSeconds, 15, 180);
            ReconnectWindowSeconds = Math.Max(PeerTimeoutSeconds, Clamp(ReconnectWindowSeconds, 30, 1800));
            ChatHistoryLimit = Clamp(ChatHistoryLimit, 20, SessionOptions.MaxChatHistoryLimit);
            ChatFontSize = Clamp(ChatFontSize, 14, 28);
            VoiceVolume = Clamp(VoiceVolume, 0, 100);
            if (string.IsNullOrEmpty(ActiveGameplayId)) ActiveGameplayId = Sdk.GameplayFramework.BuiltInId;
            if (DisabledGameplays == null) DisabledGameplays = "";

            bool chatDisabled = false;
            if (!string.IsNullOrEmpty(DisabledGameplays))
            {
                foreach (var id in DisabledGameplays.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = id.Trim();
                    if (trimmed == Sdk.GameplayFramework.BuiltInId) chatDisabled = true;
                    Sdk.GameplayFramework.SetEnabled(trimmed, false);
                }
            }
            Sdk.GameplayFramework.SetEnabled(Sdk.GameplayFramework.BuiltInId, !chatDisabled);
            if (!string.IsNullOrEmpty(ActiveGameplayId) && Sdk.GameplayFramework.IsEnabled(ActiveGameplayId))
            {
                try { Sdk.GameplayFramework.Select(ActiveGameplayId); } catch { }
            }
            return this;
        }

        internal SessionOptions CreateSessionOptions()
        {
            Normalize();
            return new SessionOptions {
                HandshakeTimeoutSeconds = HandshakeTimeoutSeconds,
                PeerTimeoutSeconds = PeerTimeoutSeconds,
                ReconnectWindowSeconds = ReconnectWindowSeconds,
                SeatReservationSeconds = ReconnectWindowSeconds,
                ChatHistoryLimit = ChatHistoryLimit
            };
        }

        private static int Clamp(int value, int minimum, int maximum)
        { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}
