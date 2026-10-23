using System.Collections.Generic;
using OniOnline.Sdk;

namespace OniOnline.Gameplay
{
    // The bundled mode uses exactly the extension point offered to other mods.
    public sealed class OnePlanetPerPlayer : IGameplayRules
    {
        public string Id { get { return GameplayFramework.BuiltInId; } }
        public string Version { get { return "1"; } }
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
            }
            return null;
        }
    }
}
