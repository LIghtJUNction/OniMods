using System;
using Klei.AI;

namespace OniMcp.Tools
{
    internal static class DupeAmountUtil
    {
        public static bool TryGetStressValue(MinionIdentity dupe, out float value)
        {
            value = 0f;
            try
            {
                var amounts = dupe?.GetComponent<Amounts>();
                var stressAmount = Db.Get()?.Amounts?.Stress;
                var stress = stressAmount == null ? null : amounts?.Get(stressAmount);
                if (stress == null)
                    return false;
                value = ToolUtil.SafeFloat(stress.value);
                return true;
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
            var amounts = dupe?.GetComponent<Amounts>();
            if (amounts == null)
                return fallback;

            foreach (var amount in amounts.ModifierList)
            {
                if (amount == null || amount.amount == null)
                    continue;
                string id = amount.amount.Id ?? "";
                string name = amount.amount.Name ?? "";
                if (Contains(id, query) || Contains(name, query))
                    return ToolUtil.SafeFloat(amount.value);
            }

            return fallback;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
