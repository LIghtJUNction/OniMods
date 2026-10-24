using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OniOnline.Sdk
{
    // Descriptive information about one authenticated endpoint. Game build,
    // DLC state and CycleTrim are deliberately observations, not admission
    // rules. Feature access still goes through the negotiated capabilities.
    public sealed class SessionEnvironment
    {
        private readonly ReadOnlyCollection<string> capabilities;
        private readonly ReadOnlyCollection<string> requiredCapabilities;

        public string GameVersion { get; }
        public string CycleTrimVersion { get; }
        public bool SpacedOut { get; }
        public IReadOnlyList<string> Capabilities { get { return capabilities; } }
        public IReadOnlyList<string> RequiredCapabilities { get { return requiredCapabilities; } }

        internal SessionEnvironment(string gameVersion, string cycleTrimVersion, bool spacedOut,
            IEnumerable<string> capabilities, IEnumerable<string> requiredCapabilities)
        {
            GameVersion = gameVersion ?? "";
            CycleTrimVersion = cycleTrimVersion ?? "";
            SpacedOut = spacedOut;
            this.capabilities = new ReadOnlyCollection<string>((capabilities ?? Enumerable.Empty<string>()).ToArray());
            this.requiredCapabilities = new ReadOnlyCollection<string>((requiredCapabilities ?? Enumerable.Empty<string>()).ToArray());
        }

        public bool Provides(string capability)
        { return capability != null && capabilities.Contains(capability, StringComparer.Ordinal); }

        public bool Requires(string capability)
        { return capability != null && requiredCapabilities.Contains(capability, StringComparer.Ordinal); }
    }
}
