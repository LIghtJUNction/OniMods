using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;

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
                    if (amount == null || amount.amount == null) continue;
                    amounts[amount.amount.Name] = Math.Round(ToolUtil.SafeFloat(amount.value), 2);
                }
            }
            var dbAmounts = Db.Get()?.Amounts;
            AddNativeAmount(amounts, dupe, dbAmounts?.Stamina);
            AddNativeAmount(amounts, dupe, dbAmounts?.Calories);
            AddNativeAmount(amounts, dupe, dbAmounts?.Stress);
            AddNativeAmount(amounts, dupe, dbAmounts?.Bladder);
            AddNativeAmount(amounts, dupe, dbAmounts?.Breath);
            AddNativeAmount(amounts, dupe, dbAmounts?.Temperature);
            AddNativeAmount(amounts, dupe, dbAmounts?.BionicInternalBattery);
            AddNativeAmount(amounts, dupe, dbAmounts?.BionicOxygenTank);

            var availability = new Dictionary<string, bool>
            {
                ["stamina"] = HasNativeAmount(dupe, dbAmounts?.Stamina),
                ["calories"] = HasNativeAmount(dupe, dbAmounts?.Calories),
                ["stress"] = HasNativeAmount(dupe, dbAmounts?.Stress),
                ["bladder"] = HasNativeAmount(dupe, dbAmounts?.Bladder),
                ["breath"] = HasNativeAmount(dupe, dbAmounts?.Breath),
                ["bodyTemperature"] = HasNativeAmount(dupe, dbAmounts?.Temperature)
            };
            var definitions = new Dictionary<string, bool>
            {
                ["stamina"] = dbAmounts?.Stamina != null,
                ["calories"] = dbAmounts?.Calories != null,
                ["stress"] = dbAmounts?.Stress != null,
                ["bladder"] = dbAmounts?.Bladder != null,
                ["breath"] = dbAmounts?.Breath != null,
                ["bodyTemperature"] = dbAmounts?.Temperature != null
            };

            bool isBionic = DuplicantNeedReadout.IsBionic(dupe);
            var result = new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["amounts"] = amounts,
                ["availability"] = DuplicantNeedReadout.AmountStateMap(isBionic, amountInstance != null, definitions, availability)
            };
            if (isBionic)
                result["bionicPower"] = BionicPowerSummary(dupe);

            return result;
        }

        private static void AddNativeAmount(Dictionary<string, object> values, MinionIdentity dupe, Amount amount)
        {
            if (amount != null && DupeAmountUtil.TryGetAmountValue(dupe, amount, out var value))
                values[amount.Name] = Math.Round(value, 2);
        }

        private static bool HasNativeAmount(MinionIdentity dupe, Amount amount)
        {
            return DupeAmountUtil.TryGetAmountValue(dupe, amount, out _);
        }

        private static Dictionary<string, object> BionicPowerSummary(MinionIdentity dupe)
        {
            var monitor = dupe.GetSMI<BionicBatteryMonitor.Instance>();
            var result = new Dictionary<string, object>
            {
                ["state"] = monitor == null ? "unknown" : "known",
                ["chargeJ"] = null,
                ["capacityJ"] = null,
                ["online"] = null,
                ["chargedPowerbanks"] = null,
                ["depletedPowerbanks"] = null,
                ["powerbankCapacity"] = null,
                ["storedPowerbanks"] = null,
                ["sparePowerbankDamage"] = new Dictionary<string, object> { ["state"] = "unknown" },
                ["nearbyLoosePowerbanks"] = NearbyLoosePowerbankSummary(dupe),
                ["dupeWaterDamageState"] = "unknown",
                ["dupeWaterDamageActive"] = null
            };
            if (monitor == null)
                return result;

            var batteryDefinition = Db.Get()?.Amounts?.BionicInternalBattery;
            var batteryAmount = batteryDefinition == null ? null : dupe.GetComponent<Amounts>()?.Get(batteryDefinition);
            result["chargeJ"] = Math.Round(ToolUtil.SafeFloat(monitor.CurrentCharge), 1);
            result["capacityJ"] = batteryAmount == null ? null : (object)Math.Round(ToolUtil.SafeFloat(batteryAmount.GetMax()), 1);
            result["online"] = monitor.IsOnline;
            result["chargedPowerbanks"] = monitor.ChargedElectrobankCount;
            result["depletedPowerbanks"] = monitor.DepletedElectrobankCount;
            result["powerbankCapacity"] = monitor.ElectrobankCountCapacity;
            result["storedPowerbanks"] = monitor.storage == null ? null : (object)monitor.storage.items.Count;
            result["sparePowerbankDamage"] = PowerbankDamageSummary(monitor.storage?.items);
            var waterDamage = dupe.GetSMI<BionicWaterDamageMonitor.Instance>();
            result["dupeWaterDamageState"] = waterDamage == null ? "unknown" : waterDamage.IsAffectedByWaterDamage ? "risk" : "noneObserved";
            result["dupeWaterDamageActive"] = waterDamage == null ? null : (object)waterDamage.IsAffectedByWaterDamage;
            return result;
        }

        private static Dictionary<string, object> PowerbankDamageSummary(IEnumerable<UnityEngine.GameObject> items)
        {
            int count = 0;
            int healthKnown = 0;
            int damaged = 0;
            foreach (var item in items ?? Enumerable.Empty<UnityEngine.GameObject>())
            {
                var battery = item?.GetComponent<Electrobank>();
                if (battery == null)
                    continue;
                count++;
                var health = TryReadPowerbankHealth(battery);
                if (!health.HasValue)
                    continue;
                healthKnown++;
                if (health.Value < 10f)
                    damaged++;
            }
            return new Dictionary<string, object>
            {
                ["state"] = healthKnown == count ? "known" : "unknown",
                ["count"] = count,
                ["healthKnownCount"] = healthKnown,
                ["damagedCount"] = damaged
            };
        }

        private static float? TryReadPowerbankHealth(Electrobank battery)
        {
            try
            {
                var field = typeof(Electrobank).GetField("currentHealth", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null || !(field.GetValue(battery) is float health) || float.IsNaN(health) || float.IsInfinity(health))
                    return null;
                return health;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> NearbyLoosePowerbankSummary(MinionIdentity dupe)
        {
            const int radius = 6;
            int origin = Grid.PosToCell(dupe);
            if (!Grid.IsValidCell(origin))
                return new Dictionary<string, object>
                {
                    ["state"] = "unknown",
                    ["radiusCells"] = radius,
                    ["waterDamageRiskState"] = DuplicantNeedStatePolicy.WaterRisk(false, -1),
                    ["waterDamageRisk"] = null
                };

            int x = Grid.CellColumn(origin);
            int y = Grid.CellRow(origin);
            int count = 0;
            int waterExposed = 0;
            int healthKnown = 0;
            int damaged = 0;
            int worldId = dupe.GetMyWorldId();
            for (int yy = Math.Max(0, y - radius); yy <= Math.Min(Grid.HeightInCells - 1, y + radius); yy++)
            {
                for (int xx = Math.Max(0, x - radius); xx <= Math.Min(Grid.WidthInCells - 1, x + radius); xx++)
                {
                    int cell = Grid.XYToCell(xx, yy);
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    var go = Grid.Objects[cell, (int)ObjectLayer.Pickupables];
                    var battery = go?.GetComponent<Electrobank>();
                    if (battery == null || go.GetComponent<KPrefabID>()?.HasTag(GameTags.Stored) == true)
                        continue;
                    count++;
                    var health = TryReadPowerbankHealth(battery);
                    if (health.HasValue)
                    {
                        healthKnown++;
                        if (health.Value < 10f)
                            damaged++;
                    }
                    if (Grid.Element[cell] != null && Grid.Element[cell].HasTag(GameTags.AnyWater))
                        waterExposed++;
                }
            }

            string waterRiskState = DuplicantNeedStatePolicy.WaterRisk(true, waterExposed);
            return new Dictionary<string, object>
            {
                ["state"] = "known",
                ["radiusCells"] = radius,
                ["count"] = count,
                ["damageState"] = healthKnown == count ? "known" : "unknown",
                ["healthKnownCount"] = healthKnown,
                ["damagedCount"] = damaged,
                ["waterExposedCount"] = waterExposed,
                ["waterDamageRiskState"] = waterRiskState,
                ["waterDamageRisk"] = waterRiskState == "risk"
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
            public float Breath = -1f;
            public float BodyTemperature = -1f;
            public readonly Dictionary<string, bool> Available = new Dictionary<string, bool>();
            public readonly Dictionary<string, bool> Definitions = new Dictionary<string, bool>();
            public bool IsBionic;
            public bool HasAmounts;
            public Dictionary<string, object> BionicPower;
            public readonly List<string> UnknownNeeds = new List<string>();

            public Dictionary<string, object> ToDictionary()
            {
                var availability = DuplicantNeedReadout.AmountStateMap(IsBionic, HasAmounts, Definitions, Available);
                var result = new Dictionary<string, object>
                {
                    ["stamina"] = RoundOrNull(Stamina),
                    ["calories"] = RoundOrNull(Calories),
                    ["stress"] = RoundOrNull(Stress),
                    ["bladder"] = RoundOrNull(Bladder),
                    ["breath"] = RoundOrNull(Breath),
                    ["bodyTemperature"] = RoundOrNull(BodyTemperature),
                    ["availability"] = availability,
                    ["unknownNeeds"] = UnknownNeeds
                };
                if (IsBionic)
                    result["bionicPower"] = BionicPower;
                return result;
            }

            private static object RoundOrNull(float value)
            {
                return value < 0f ? null : (object)Math.Round(value, 2);
            }
        }
}
}
