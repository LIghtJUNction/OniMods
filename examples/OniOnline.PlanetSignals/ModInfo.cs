using System;
using System.Collections.Generic;
using HarmonyLib;
using OniOnline.Sdk;
using OniOnline.Gameplay;

namespace PlanetSignals
{
    // Compile this separate assembly against the installed SDK. It is not auto-installed.
    public sealed class ModInfo : KMod.UserMod2
    {
        public static StateExample State { get; private set; }
        // Optional gameplay UI/action integration; this example adds no game UI.
        public static bool SendSignal() { return State?.SendSignal() == true; }
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            GameplayFramework.Register(new SignalRules());
        }
        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<KMod.Mod> mods)
        {
            base.OnAllModsLoaded(harmony, mods);
            GameplayFramework.Select("example.planet-signals");
            GameplayFramework.SessionChanged += Attach;
            Attach();
        }
        private static void Attach()
        {
            State?.Dispose(); State = null;
            var session = GameplayFramework.Current;
            if (session == null || session.GameplayId != "example.planet-signals") return;
            State = new StateExample(session);
        }
        private sealed class SignalRules : IGameplayRules, IGameplayCapabilityContract
        {
            private readonly OnePlanetPerPlayer defaults = new OnePlanetPerPlayer();
            public string Id { get { return "example.planet-signals"; } }
            public string Version { get { return "1"; } }
            public IEnumerable<string> Capabilities { get { return new[] { "example.planet-signals.state.v1" }; } }
            public IEnumerable<string> RequiredCapabilities { get { return new[] { "example.planet-signals.state.v1" }; } }
            public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current)
            { return defaults.ValidatePlanetAdmission(candidate, current); }
        }
    }
}
