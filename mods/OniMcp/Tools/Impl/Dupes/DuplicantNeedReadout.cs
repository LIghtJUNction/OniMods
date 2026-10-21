using System;
using System.Collections.Generic;
using Klei.AI;

namespace OniMcp.Tools
{
    internal static class DuplicantNeedReadout
    {
        internal static Dictionary<string, object> AmountStateMap(bool isBionic, bool hasAmounts, IDictionary<string, bool> definitions, IDictionary<string, bool> available)
        {
            var result = new Dictionary<string, object>();
            foreach (var pair in available)
                result[pair.Key] = DuplicantNeedStatePolicy.Availability(isBionic, hasAmounts,
                    definitions.TryGetValue(pair.Key, out var defined) && defined, pair.Value);
            return result;
        }

        internal static bool IsBionic(MinionIdentity dupe)
        {
            try
            {
                return dupe?.GetComponent<KPrefabID>()?.HasTag(GameTags.Minions.Models.Bionic) == true;
            }
            catch
            {
                return false;
            }
        }

    }
}
