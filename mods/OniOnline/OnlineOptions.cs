using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;
using PeterHan.PLib.Options;
using PeterHan.PLib.UI;
#if ONIONLINE_MOD
using UnityEngine;
#endif

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

        // Kept for existing config files. The options screen shows one checkbox per
        // registered gameplay instead of asking the player to type these ids.
        public string ActiveGameplayId { get; set; } = Sdk.GameplayFramework.BuiltInId;

        public string DisabledGameplays { get; set; } = "";

        public IEnumerable<IOptionsEntry> CreateOptions()
        {
            foreach (string id in Sdk.GameplayFramework.RegisteredIds)
            {
                string title = id == Sdk.GameplayFramework.BuiltInId
                    ? "STRINGS.ONIONLINE.OPTIONS.GAMEPLAYCHAT.NAME" : id;
                yield return new GameplayToggleEntry(id, title, "STRINGS.ONIONLINE.OPTIONS.GAMEPLAYTOGGLE.DESC");
            }
        }

        public void OnOptionsChanged()
        {
            var normalized = Normalize();
            Saved?.Invoke(normalized);
        }

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

            var disabled = ParseDisabled(DisabledGameplays);
            foreach (string id in Sdk.GameplayFramework.RegisteredIds)
                Sdk.GameplayFramework.SetEnabled(id, !disabled.Contains(id));
            if (!Sdk.GameplayFramework.IsEnabled(ActiveGameplayId))
            {
                string fallback = Sdk.GameplayFramework.EnabledIds.FirstOrDefault();
                if (!string.IsNullOrEmpty(fallback)) ActiveGameplayId = fallback;
            }
            // An open room keeps its current rules. The next host or join uses the checkbox state.
            if (Sdk.GameplayFramework.Current == null && Sdk.GameplayFramework.IsEnabled(ActiveGameplayId))
            {
                try { Sdk.GameplayFramework.Select(ActiveGameplayId); }
                catch (InvalidOperationException) { }
            }
            return this;
        }

        internal static HashSet<string> ParseDisabled(string value)
        {
            var disabled = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(value)) return disabled;
            foreach (string id in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = id.Trim();
                if (trimmed.Length > 0) disabled.Add(trimmed);
            }
            return disabled;
        }

        internal static string WithGameplay(string value, string id, bool enabled)
        {
            var disabled = ParseDisabled(value);
            if (enabled) disabled.Remove(id);
            else disabled.Add(id);
            return string.Join(";", disabled.OrderBy(item => item, StringComparer.Ordinal));
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

    // One row in the gameplay list. Checked means the gameplay can be used; cleared means it cannot.
    public sealed class GameplayToggleEntry : IOptionsEntry
    {
        private readonly string gameplayId;
        private bool enabled;
#if ONIONLINE_MOD
        private object checkbox;
#endif

        public string GameplayId { get { return gameplayId; } }
        public string Category { get { return "STRINGS.ONIONLINE.OPTIONS.CATEGORY_GAMEPLAY"; } }
        public string Format { get { return null; } }
        public string Title { get; }
        public string Tooltip { get; }
        public bool RestartRequired { get { return false; } set { } }

        public object Value
        {
            get { return enabled; }
            set
            {
                if (value is bool flag) enabled = flag;
                Refresh();
            }
        }

        public GameplayToggleEntry(string gameplayId, string title, string tooltip)
        {
            this.gameplayId = gameplayId;
            Title = title;
            Tooltip = tooltip;
        }

        public void CreateUIEntry(PGridPanel parent, ref int row)
        {
#if ONIONLINE_MOD
            var box = new PCheckBox
            {
                OnChecked = (source, state) =>
                {
                    enabled = state == PCheckBox.STATE_CHECKED;
                    Refresh();
                },
                ToolTip = gameplayId + "\n" + OptionsEntry.LookInStrings(Tooltip)
            }.SetKleiBlueStyle();
            box.AddOnRealize(realized => { checkbox = realized; Refresh(); });
            OptionsEntry.CreateDefaultUIEntry(this, parent, row, box);
#endif
        }

        public void ReadFrom(object settings)
        {
            var options = settings as OnlineOptions;
            if (options == null) return;
            Value = !OnlineOptions.ParseDisabled(options.DisabledGameplays).Contains(gameplayId);
        }

        public bool WriteTo(object settings)
        {
            var options = settings as OnlineOptions;
            if (options == null) return false;
            bool nowEnabled = Value is bool flag && flag;
            bool wasEnabled = !OnlineOptions.ParseDisabled(options.DisabledGameplays).Contains(gameplayId);
            if (nowEnabled == wasEnabled) return false;
            options.DisabledGameplays = OnlineOptions.WithGameplay(options.DisabledGameplays, gameplayId, nowEnabled);
            return true;
        }

        private void Refresh()
        {
#if ONIONLINE_MOD
            var widget = checkbox as GameObject;
            if (widget != null)
                PCheckBox.SetCheckState(widget, enabled ? PCheckBox.STATE_CHECKED : PCheckBox.STATE_UNCHECKED);
#endif
        }
    }
}
