using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Network;
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

    // Optional contract for a gameplay package. These IDs are merged into the
    // adapter's native and mod-sync capabilities when the gameplay is selected.
    // RequiredCapabilities are directional: a consumer may require a capability
    // from the remote endpoint without providing it locally.
    public interface IGameplayCapabilityContract
    {
        IEnumerable<string> Capabilities { get; }
        IEnumerable<string> RequiredCapabilities { get; }
    }

    public sealed class PlanetClaim
    {
        private readonly PlanetManifest planet;
        private readonly string authenticatedAccount;
        public string PlayerId { get; }
        // Admission rules receive a snapshot, not a mutable reference into the
        // coordinator's roster. Returning a copy also prevents one rule from
        // mutating the input observed by its own later checks.
        public PlanetManifest Planet { get { return planet.Copy(); } }
        public PlanetClaim(string playerId, PlanetManifest planet)
            : this(playerId, planet, null) { }

        // Only the host coordinator supplies transport-authenticated context.
        // It is neither part of a client manifest nor a public account identifier.
        internal PlanetClaim(string playerId, PlanetManifest planet, string authenticatedAccount)
        {
            PlayerId = playerId; this.planet = planet?.Copy() ?? throw new ArgumentNullException(nameof(planet));
            this.authenticatedAccount = string.IsNullOrWhiteSpace(authenticatedAccount) ? null : authenticatedAccount;
        }
        internal bool SharesAuthenticatedAccount(PlanetClaim other)
        { return other != null && authenticatedAccount != null && authenticatedAccount == other.authenticatedAccount; }
    }

    public static class GameplayFramework
    {
        public const int ApiVersion = 1;
        public const string BuiltInId = "onionline.chat";
        // Gameplay mods that need planet admission and world services declare this.
        // The adapter does not start those services for chat.
        public const string WorldsCapability = "onionline.worlds.v1";
        private static readonly Dictionary<string, IGameplayRules> rules = new Dictionary<string, IGameplayRules>(StringComparer.Ordinal);
        private static readonly Dictionary<string, GameplayCapabilityContract> contracts = new Dictionary<string, GameplayCapabilityContract>(StringComparer.Ordinal);
        private static readonly HashSet<string> disabled = new HashSet<string>(StringComparer.Ordinal);
        private static string selected = BuiltInId;
        private sealed class GameplayCapabilityContract
        {
            internal readonly string[] Capabilities, RequiredCapabilities;
            internal GameplayCapabilityContract(string[] capabilities, string[] requiredCapabilities)
            { Capabilities = capabilities; RequiredCapabilities = requiredCapabilities; }
        }
        static GameplayFramework() { Register(new ChatOnly()); }
        public static bool UsesWorlds(IEnumerable<string> capabilities)
        {
            if (capabilities == null) return false;
            foreach (string capability in capabilities)
                if (capability == WorldsCapability) return true;
            return false;
        }
        public static GameplaySession Current { get; private set; }
        public static event System.Action SessionChanged;
        public static string SelectedId { get { return selected; } }
        public static string SelectedVersion { get { return rules[selected].Version; } }
        public static string[] RegisteredIds { get { return rules.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(); } }
        public static string[] EnabledIds { get { return rules.Keys.Where(id => !disabled.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray(); } }

        public static bool IsEnabled(string id)
        { return id != null && rules.ContainsKey(id) && !disabled.Contains(id); }

        public static void SetEnabled(string id, bool enabled)
        {
            if (id == null) return;
            if (enabled)
            {
                disabled.Remove(id);
            }
            else
            {
                disabled.Add(id);
                // An open room keeps the rules it already started with. The next
                // host or join picks another enabled gameplay after that room ends.
                if (selected == id && Current == null)
                    selected = EnabledIds.FirstOrDefault() ?? BuiltInId;
            }
        }

        public static void Register(IGameplayRules gameplay)
        {
            if (gameplay == null || !ValidName(gameplay.Id) || !ValidName(gameplay.Version))
                throw new ArgumentException("invalid_gameplay_registration");
            if (rules.ContainsKey(gameplay.Id)) throw new InvalidOperationException("duplicate_gameplay_registration");
            var contract = gameplay as IGameplayCapabilityContract;
            string[] capabilities = NormalizeContract(contract?.Capabilities);
            string[] required = NormalizeContract(contract?.RequiredCapabilities);
            rules.Add(gameplay.Id, gameplay);
            contracts.Add(gameplay.Id, new GameplayCapabilityContract(capabilities, required));
        }

        // Select before opening/joining a system. Every participant must install the same rules/version.
        public static void Select(string gameplayId)
        {
            if (Current != null) throw new InvalidOperationException("gameplay_session_active");
            if (gameplayId == null || !rules.ContainsKey(gameplayId)) throw new ArgumentException("unknown_gameplay");
            if (!IsEnabled(gameplayId)) throw new InvalidOperationException("gameplay_disabled");
            selected = gameplayId;
        }

        internal static bool Available(string id, string version)
        { return id != null && IsEnabled(id) && rules.TryGetValue(id, out var value) && value.Version == version; }

        // The adapter merges these with its own native and mod-sync contracts.
        // Copies prevent a gameplay package from changing the selected profile
        // after a connection has captured it.
        internal static string[] SelectedCapabilities
        { get { return Contract(selected).Capabilities.ToArray(); } }

        internal static string[] SelectedRequiredCapabilities
        { get { return Contract(selected).RequiredCapabilities.ToArray(); } }

        private static GameplayCapabilityContract Contract(string id)
        { return contracts.TryGetValue(id, out var value) ? value : new GameplayCapabilityContract(new string[0], new string[0]); }

        private static string[] NormalizeContract(IEnumerable<string> values)
        {
            if (values == null) return new string[0];
            return SessionCompatibility.Normalize(values.Where(value => value != null).Distinct(StringComparer.Ordinal));
        }

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
            var previous = Current;
            Current = session;
            // Binding is the adapter's ownership boundary. Replacing the
            // current session must release its RoomSession subscriptions before
            // notifying plugins, otherwise an old gameplay can still consume
            // messages after a leave/rejoin or gameplay switch.
            if (previous != null) previous.Dispose();
            if (Current == null && !IsEnabled(selected))
                selected = EnabledIds.FirstOrDefault() ?? BuiltInId;
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
