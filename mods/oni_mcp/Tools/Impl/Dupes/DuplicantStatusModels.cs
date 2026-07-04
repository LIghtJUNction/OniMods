using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        private static Dictionary<string, object> GetAttributeSummary(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            var attributes = new List<Dictionary<string, object>>();
            var attrs = dupe.GetAttributes();
            if (attrs != null)
            {
                foreach (AttributeInstance attr in attrs)
                {
                    if (attr == null || attr.hide) continue;
                    attributes.Add(new Dictionary<string, object>
                    {
                        ["id"] = attr.Id,
                        ["name"] = attr.Name,
                        ["value"] = Math.Round(attr.GetTotalValue(), 2),
                        ["baseValue"] = Math.Round(attr.GetBaseValue(), 2)
                    });
                }
            }

            var mastered = resume != null
                ? resume.MasteryBySkillID.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x).ToList()
                : new List<string>();

            var aptitudes = resume != null
                ? resume.AptitudeBySkillGroup.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 2))
                : new Dictionary<string, double>();

            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["profession"] = attrs?.GetProfession()?.Name,
                ["suggestedRole"] = GuessRole(dupe),
                ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                ["skillsMastered"] = mastered,
                ["aptitudes"] = aptitudes,
                ["attributes"] = attributes.OrderByDescending(a => Convert.ToDouble(a["value"])).ToList()
            };
        }

        private static Dictionary<string, object> GetNeedsSummary(MinionIdentity dupe)
        {
            var amounts = new Dictionary<string, object>();
            var amountInstance = dupe.GetComponent<Amounts>();
            if (amountInstance != null)
            {
                foreach (var amount in amountInstance.ModifierList)
                {
                    if (amount == null) continue;
                    amounts[amount.amount.Name] = Math.Round(ToolUtil.SafeFloat(amount.value), 2);
                }
            }
            if (DupeAmountUtil.TryGetStressValue(dupe, out var stress))
                amounts["Stress"] = Math.Round(stress, 2);

            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["amounts"] = amounts
            };
        }

        private sealed class ReachabilitySummary
        {
            public int ReachableCells;
            public int VisibleCells;
            public int SolidCells;
            public readonly List<Dictionary<string, object>> Samples = new List<Dictionary<string, object>>();
        }

        private sealed class KeyNeeds
        {
            public float Stamina = -1f;
            public float Calories = -1f;
            public float Stress = -1f;
            public float Bladder = -1f;
            public float Breath = 100f;
            public float BodyTemperature = -1f;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["stamina"] = RoundOrNull(Stamina),
                    ["calories"] = RoundOrNull(Calories),
                    ["stress"] = RoundOrNull(Stress),
                    ["bladder"] = RoundOrNull(Bladder),
                    ["breath"] = RoundOrNull(Breath),
                    ["bodyTemperature"] = RoundOrNull(BodyTemperature)
                };
            }

            private static object RoundOrNull(float value)
            {
                return value < 0f ? null : (object)Math.Round(value, 2);
            }
        }
}
}
