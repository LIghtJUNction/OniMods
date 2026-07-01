using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static IEnumerable<PlanMaterialCandidate> FallbackMaterialCandidates(List<string> terms, Dictionary<string, string> aliases)
        {
            if (terms == null || aliases == null)
                yield break;

            foreach (string term in terms)
            {
                string materialTag;
                if (!aliases.TryGetValue(NormalizePlanText(term), out materialTag))
                    continue;

                yield return new PlanMaterialCandidate
                {
                    Tag = materialTag,
                    Name = term,
                    Score = 980,
                    Matched = term,
                    AvailableKg = 0f,
                    ValidForBuilding = true
                };
            }
        }
    }
}
