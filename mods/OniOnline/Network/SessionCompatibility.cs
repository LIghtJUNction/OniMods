using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Network
{
    // Capability IDs carry their wire/schema version. Game build and DLC labels
    // describe an adapter; they are not a substitute for this contract.
    public static class SessionCapabilities
    {
        public const string SessionV1 = "onionline.session.v1";
        public const string GameplayV1 = "onionline.gameplay.v1";
        public const string PlanetPresenceV1 = "onionline.planet-presence.v1";
        public const string ModSynchronizationV1 = "onionline.mod-sync.v1";
        public const string NativePlanetV1 = "onionline.native-planet.v1";
        public const string NativeObservationV1 = "onionline.native-observation.v1";
        public const string NativeStarMapV1 = "onionline.native-star-map.v1";
        public const string NativePlanet744825V1 = NativePlanetV1;
        public const string NativeObservation744825V1 = NativeObservationV1;
        public const string NativeStarMap744825V1 = NativeStarMapV1;

        public static string[] Core
        { get { return new[] { SessionV1, GameplayV1, PlanetPresenceV1 }; } }

        // Only the pinned adapter may enter native code. WorldContainer/Grid
        // contracts are shared by base game and DLC; the cluster map is DLC-only.
        // Adding a build here also requires adapting and checking the native implementation.
        public static string[] ForNativeAdapter(string gameVersion, bool spacedOut)
        {
            var supported = new List<string>(Core);
            if (gameVersion == "744825")
            {
                supported.Add(NativePlanetV1);
                supported.Add(NativeObservationV1);
                if (spacedOut) supported.Add(NativeStarMapV1);
            }
            return SessionCompatibility.Normalize(supported);
        }

        public static bool SupportsNativeAdapter(string gameVersion, bool spacedOut, string capability)
        {
            return (capability == NativePlanetV1 || capability == NativeObservationV1 || capability == NativeStarMapV1) &&
                ForNativeAdapter(gameVersion, spacedOut).Contains(capability, StringComparer.Ordinal);
        }
    }

    public sealed class SessionCompatibilityResult
    {
        private readonly string[] missingAtHost;
        private readonly string[] missingAtPeer;
        private readonly string[] negotiated;
        public bool Compatible { get { return missingAtHost.Length == 0 && missingAtPeer.Length == 0; } }
        public string[] MissingAtHost { get { return (string[])missingAtHost.Clone(); } }
        public string[] MissingAtPeer { get { return (string[])missingAtPeer.Clone(); } }
        public string[] Negotiated { get { return (string[])negotiated.Clone(); } }
        internal SessionCompatibilityResult(string[] missingAtHost, string[] missingAtPeer, string[] negotiated)
        { this.missingAtHost = missingAtHost; this.missingAtPeer = missingAtPeer; this.negotiated = negotiated; }
    }

    public sealed class NativeAdapterUnavailableException : InvalidOperationException
    {
        public string Capability { get; private set; }
        public NativeAdapterUnavailableException(string capability) : base("native_adapter_unavailable")
        { Capability = capability; }
    }

    // This guard owns the boundary before an adapter touches native metadata.
    // DLC probing is deferred so an unknown build cannot invoke native callbacks.
    public sealed class NativeAdapterGuard
    {
        private readonly string gameVersion;
        private readonly Func<bool> spacedOut;
        public NativeAdapterGuard(string gameVersion, Func<bool> spacedOut)
        { this.gameVersion = gameVersion; this.spacedOut = spacedOut; }

        public bool Supports(string capability)
        {
            if (SessionCapabilities.SupportsNativeAdapter(gameVersion, false, capability)) return true;
            return SessionCapabilities.SupportsNativeAdapter(gameVersion, true, capability) &&
                spacedOut != null && spacedOut();
        }

        public void Require(string capability)
        { if (!Supports(capability)) throw new NativeAdapterUnavailableException(capability); }

        public T Read<T>(string capability, Func<T> readNative)
        {
            Require(capability);
            if (readNative == null) throw new ArgumentNullException(nameof(readNative));
            return readNative();
        }
    }

    public static class SessionCompatibility
    {
        public const int MaximumCapabilities = 32;
        public const int MaximumCapabilityLength = 64;

        public static string[] Normalize(IEnumerable<string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (value == null || value.Length < 1 || value.Length > MaximumCapabilityLength ||
                    value.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_')))
                    throw new ArgumentException("invalid_capability_id");
                if (!unique.Add(value)) throw new ArgumentException("duplicate_capability_id");
                if (unique.Count > MaximumCapabilities) throw new ArgumentException("capability_budget");
            }
            return unique.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public static SessionCompatibilityResult Evaluate(IEnumerable<string> hostSupported, IEnumerable<string> hostRequired,
            IEnumerable<string> peerSupported, IEnumerable<string> peerRequired)
        {
            var host = Normalize(hostSupported);
            var peer = Normalize(peerSupported);
            var hostNeeds = Normalize(hostRequired);
            var peerNeeds = Normalize(peerRequired);
            return new SessionCompatibilityResult(peerNeeds.Except(host, StringComparer.Ordinal).ToArray(),
                hostNeeds.Except(peer, StringComparer.Ordinal).ToArray(), host.Intersect(peer, StringComparer.Ordinal).ToArray());
        }

        internal static SessionProfile Freeze(SessionProfile profile)
        {
            var supported = Normalize(profile.Capabilities ?? SessionCapabilities.Core);
            var required = Normalize(profile.RequiredCapabilities ?? SessionCapabilities.Core);
            return new SessionProfile { Name = profile.Name, GameVersion = profile.GameVersion ?? "",
                CycleTrimVersion = profile.CycleTrimVersion ?? "", SpacedOut = profile.SpacedOut,
                GameplayId = profile.GameplayId, GameplayVersion = profile.GameplayVersion,
                Capabilities = supported, RequiredCapabilities = required };
        }
    }
}
