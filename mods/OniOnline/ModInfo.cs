using System.Collections.Generic;
using HarmonyLib;
using OniOnline.Runtime;
using UnityEngine;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace OniOnline
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            PUtil.InitLibrary();
            OnlineText.Load(path);
            OnlineInputControls.RegisterActions();
            RegisterOptionText("PREFERSTEAM", "prefer_steam");
            RegisterOptionText("LANPORT", "lan_port");
            RegisterOptionText("HANDSHAKE", "handshake_timeout");
            RegisterOptionText("PEERTIMEOUT", "peer_timeout");
            RegisterOptionText("RECONNECT", "reconnect_window");
            RegisterOptionText("CHATHISTORY", "chat_history");
            RegisterOptionText("CHATFONT", "chat_font");
            RegisterOptionText("VOICEENABLED", "voice_enabled");
            RegisterOptionText("VOICEVOLUME", "voice_volume");
            // UserMod2.OnLoad patches this assembly once.
            base.OnLoad(harmony);
            new POptions().RegisterOptions(this, typeof(OnlineOptions));
        }

        private static void RegisterOptionText(string name, string key)
        {
            Strings.Add("STRINGS.ONIONLINE.OPTIONS." + name + ".NAME", OnlineText.Get("options." + key));
            Strings.Add("STRINGS.ONIONLINE.OPTIONS." + name + ".DESC", OnlineText.Get("options." + key + "_hint"));
        }

        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<KMod.Mod> mods)
        {
            base.OnAllModsLoaded(harmony, mods);
            if (OnlineRuntime.Instance != null) return;
            var root = new GameObject("OniOnline");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<OnlineRuntime>().Initialize(path, mods);
#if ONIONLINE_RUNTIME_PROBE
            RuntimeProbe.Install(root);
#endif
        }
    }
}
