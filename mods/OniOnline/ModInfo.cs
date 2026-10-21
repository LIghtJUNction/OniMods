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
            Strings.Add("STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.NAME", OnlineText.Get("options.prefer_steam"));
            Strings.Add("STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.DESC", OnlineText.Get("options.prefer_steam_hint"));
            // UserMod2.OnLoad patches this assembly once.
            base.OnLoad(harmony);
            new POptions().RegisterOptions(this, typeof(OnlineOptions));
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
