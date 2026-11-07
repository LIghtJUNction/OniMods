using System.Collections.Generic;
using OniOnline.Sdk;

namespace OniOnline.Gameplay
{
    // Reusable admission helper for a separate gameplay mod. The framework does not register it.
    public sealed class OnePlanetPerPlayer : IGameplayRules, IGameplayCapabilityContract
    {
        public const string RulesId = "onionline.one-planet-per-player";
        public string Id { get { return RulesId; } }
        public string Version { get { return "1"; } }
        public IEnumerable<string> Capabilities { get { return new[] { GameplayFramework.WorldsCapability }; } }
        public IEnumerable<string> RequiredCapabilities { get { return new[] { GameplayFramework.WorldsCapability }; } }
        public static void RegisterIfMissing()
        {
            if (System.Array.IndexOf(GameplayFramework.RegisteredIds, RulesId) >= 0) return;
            GameplayFramework.Register(new OnePlanetPerPlayer());
        }
        public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current)
        {
            foreach (var claim in current)
            {
                if (claim.PlayerId != candidate.PlayerId &&
                    (claim.Planet.PlanetId == candidate.Planet.PlanetId || claim.Planet.LineageId == candidate.Planet.LineageId))
                    return "planet_duplicate_branch";
                if (claim.PlayerId == candidate.PlayerId &&
                    (claim.Planet.PlanetId != candidate.Planet.PlanetId || claim.Planet.LineageId != candidate.Planet.LineageId ||
                    claim.Planet.OwnerId != candidate.Planet.OwnerId || claim.Planet.HomeSystemId != candidate.Planet.HomeSystemId ||
                    claim.Planet.NativeWorldId != candidate.Planet.NativeWorldId)) return "planet_selection_locked";
                if (claim.PlayerId != candidate.PlayerId &&
                    (claim.Planet.OwnerId == candidate.Planet.OwnerId || claim.SharesAuthenticatedAccount(candidate)))
                    return "planet_owner_already_admitted";
            }
            return null;
        }
    }
}
