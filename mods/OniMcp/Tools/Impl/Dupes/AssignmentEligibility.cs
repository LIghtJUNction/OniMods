using System;

namespace OniMcp.Tools
{
    internal static class AssignmentEligibility
    {
        internal static string Resolve(Assignable target, MinionIdentity dupe, out MinionAssignablesProxy proxy)
        {
            proxy = dupe?.assignableProxy.Get();
            if (proxy == null)
                return "Duplicant assignable proxy is unavailable";
            if (target == null || !target.CanBeAssigned)
                return "Target cannot be assigned";

            // ONI evaluates assignment preconditions only for the proxy identity.
            return target.CanAssignTo(proxy) ? null : "Duplicant does not meet the target assignment requirements";
        }

        internal static string ValidateSlot(MinionIdentity dupe, AssignableSlotInstance slot)
        {
            if (slot?.slot == null)
                return "Assignable slot is unavailable";
            if (!string.Equals(slot.slot.Id, Db.Get().AssignableSlots.BionicUpgrade.Id, StringComparison.Ordinal))
                return null;

            var slots = dupe.GetSMI<BionicUpgradesMonitor.Instance>()?.upgradeComponentSlots;
            if (slots != null)
            {
                foreach (var upgrade in slots)
                    if (upgrade != null && upgrade.GetAssignableSlotInstance() == slot)
                        return upgrade.IsLocked ? "Bionic upgrade slot is locked" : null;
            }
            return "Bionic upgrade slot state is unavailable";
        }
    }
}
