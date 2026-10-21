using System;
using Klei.AI;

namespace OniMcp.Tools
{
    internal static class DupeAmountUtil
    {
        public static bool TryGetAmountValue(MinionIdentity dupe, Amount amount, out float value)
        {
            value = 0f;
            if (amount == null)
                return false;
            try
            {
                var instance = dupe?.GetComponent<Amounts>()?.Get(amount);
                if (instance == null)
                    return false;
                value = ToolUtil.SafeFloat(instance.value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetStressValue(MinionIdentity dupe, out float value)
        {
            value = 0f;
            try
            {
                var stressAmount = Db.Get()?.Amounts?.Stress;
                return TryGetAmountValue(dupe, stressAmount, out value);
            }
            catch
            {
                return false;
            }
        }

        public static float StressValue(MinionIdentity dupe, float fallback = 0f)
        {
            return TryGetStressValue(dupe, out var value)
                ? value
                : AmountValueByName(dupe, "Stress", fallback);
        }

        public static float AmountValueByName(MinionIdentity dupe, string query, float fallback = 0f)
        {
            var amounts = Db.Get()?.Amounts;
            if (amounts == null || string.IsNullOrWhiteSpace(query))
                return fallback;
            var candidates = new[]
            {
                amounts.Stamina, amounts.Calories, amounts.Stress, amounts.Bladder,
                amounts.Breath, amounts.Temperature, amounts.HitPoints,
                amounts.BionicInternalBattery, amounts.BionicOxygenTank
            };
            foreach (var amount in candidates)
                if (amount != null && (Contains(amount.Id, query) || Contains(amount.Name, query))
                    && TryGetAmountValue(dupe, amount, out var value))
                    return value;
            return fallback;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
