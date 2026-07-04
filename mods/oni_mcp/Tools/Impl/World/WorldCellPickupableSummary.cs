using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<string, object> CellPickupableSummary(int cell, int worldId)
        {
            var groups = new Dictionary<string, PickupableAggregate>(StringComparer.Ordinal);
            int count = 0;
            float totalMass = 0f;
            int stored = 0;

            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable == null || pickupable.gameObject == null)
                    continue;
                if (pickupable.cachedCell != cell || !MatchesWorld(pickupable.gameObject, worldId))
                    continue;

                count++;
                bool isStored = pickupable.storage != null
                    || (pickupable.KPrefabID != null && pickupable.KPrefabID.HasTag(GameTags.Stored));
                if (isStored)
                    stored++;

                string name = ToolUtil.CleanName(pickupable.GetProperName());
                if (string.IsNullOrWhiteSpace(name))
                    name = "Unknown";

                var primary = pickupable.PrimaryElement ?? pickupable.GetComponent<PrimaryElement>();
                float mass = primary != null ? SafeFloat(primary.Mass) : 0f;
                totalMass += mass;

                if (!groups.TryGetValue(name, out var aggregate))
                {
                    aggregate = new PickupableAggregate
                    {
                        Name = name,
                        PrefabId = PrefabId(pickupable.gameObject),
                        Element = primary != null ? primary.ElementID.ToString() : string.Empty
                    };
                    groups[name] = aggregate;
                }

                aggregate.Count++;
                aggregate.MassKg += mass;
                if (isStored)
                    aggregate.Stored++;
            }

            return new Dictionary<string, object>
            {
                ["count"] = count,
                ["stored"] = stored,
                ["loose"] = count - stored,
                ["totalMassKg"] = Math.Round(totalMass, 3),
                ["groups"] = groups.Values
                    .OrderByDescending(item => item.MassKg)
                    .ThenBy(item => item.Name, StringComparer.Ordinal)
                    .Take(16)
                    .Select(item => item.ToPayload())
                    .ToList(),
                ["truncated"] = Math.Max(0, groups.Count - 16)
            };
        }

        private static string PrefabId(GameObject go)
        {
            var prefab = go != null ? go.GetComponent<KPrefabID>() : null;
            return prefab != null && prefab.PrefabTag.IsValid ? prefab.PrefabTag.Name : string.Empty;
        }

        private sealed class PickupableAggregate
        {
            public string Name;
            public string PrefabId;
            public string Element;
            public int Count;
            public int Stored;
            public float MassKg;

            public Dictionary<string, object> ToPayload()
            {
                return new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["prefabId"] = PrefabId,
                    ["element"] = Element,
                    ["count"] = Count,
                    ["stored"] = Stored,
                    ["loose"] = Count - Stored,
                    ["massKg"] = Math.Round(MassKg, 3)
                };
            }
        }
    }
}
