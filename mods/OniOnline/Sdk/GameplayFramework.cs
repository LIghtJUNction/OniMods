using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Multiplayer;
using OniOnline.Gameplay;

namespace OniOnline.Sdk
{
    public interface IGameplayRules
    {
        string Id { get; }
        string Version { get; }
        // Null accepts. A short localization error key rejects. No native writes belong here.
        string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current);
    }

    public sealed class PlanetClaim
    {
        public string PlayerId { get; }
        public PlanetManifest Planet { get; }
        public PlanetClaim(string playerId, PlanetManifest planet)
        { PlayerId = playerId; Planet = planet?.Copy() ?? throw new ArgumentNullException(nameof(planet)); }
    }

    public static class GameplayFramework
    {
        public const int ApiVersion = 1;
        public const string BuiltInId = "onionline.one-planet-per-player";
        private static readonly Dictionary<string, IGameplayRules> rules = new Dictionary<string, IGameplayRules>(StringComparer.Ordinal);
        private static string selected = BuiltInId;
        static GameplayFramework() { Register(new OnePlanetPerPlayer()); }
        public static GameplaySession Current { get; private set; }
        public static event System.Action SessionChanged;
        public static string SelectedId { get { return selected; } }
        public static string SelectedVersion { get { return rules[selected].Version; } }
        public static string[] RegisteredIds { get { return rules.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(); } }

        public static void Register(IGameplayRules gameplay)
        {
            if (gameplay == null || !ValidName(gameplay.Id) || !ValidName(gameplay.Version))
                throw new ArgumentException("invalid_gameplay_registration");
            if (rules.ContainsKey(gameplay.Id)) throw new InvalidOperationException("duplicate_gameplay_registration");
            rules.Add(gameplay.Id, gameplay);
        }

        // Select before opening/joining a system. Every participant must install the same rules/version.
        public static void Select(string gameplayId)
        {
            if (Current != null) throw new InvalidOperationException("gameplay_session_active");
            if (gameplayId == null || !rules.ContainsKey(gameplayId)) throw new ArgumentException("unknown_gameplay");
            selected = gameplayId;
        }

        internal static bool Available(string id, string version)
        { return id != null && rules.TryGetValue(id, out var value) && value.Version == version; }

        internal static string Validate(string id, string version, PlanetClaim candidate, PlanetClaim[] current)
        {
            if (!Available(id, version)) return "gameplay_mismatch";
            try
            {
                string error = rules[id].ValidatePlanetAdmission(candidate, Array.AsReadOnly(current));
                return error == null || ValidName(error) ? error : "gameplay_rule_failed";
            }
            catch (Exception) { return "gameplay_rule_failed"; }
        }

        internal static void Bind(GameplaySession session)
        {
            if (ReferenceEquals(Current, session)) return;
            Current = session;
            var handlers = SessionChanged;
            if (handlers == null) return;
            foreach (System.Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception) { /* A plugin cannot abort the room lifecycle. */ }
        }

        internal static bool ValidName(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')) return false;
            return true;
        }
    }
}
