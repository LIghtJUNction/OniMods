using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class DuplicantCandidateInfo
    {
        internal static Dictionary<string, object> Describe(MinionStartingStats stats)
        {
            return new Dictionary<string, object>
            {
                ["name"] = stats.Name,
                ["personalityId"] = stats.personality?.Id,
                ["startingLevels"] = stats.StartingLevels,
                ["traits"] = stats.Traits.Where(trait => trait != null).Select(trait => trait.Id).ToArray(),
                ["aptitudes"] = stats.skillAptitudes.ToDictionary(aptitude => aptitude.Key.Id, aptitude => aptitude.Value),
                ["stressTrait"] = stats.stressTrait?.Id,
                ["joyTrait"] = stats.joyTrait?.Id
            };
        }
    }
}
