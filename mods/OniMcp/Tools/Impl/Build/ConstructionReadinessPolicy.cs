using System;

namespace OniMcp.Tools
{
    internal sealed class MaterialReadiness
    {
        public readonly string Status;
        public readonly float? ShortageKg;

        public MaterialReadiness(string status, float? shortageKg)
        {
            Status = status;
            ShortageKg = shortageKg;
        }
    }

    internal static class ConstructionReadinessPolicy
    {
        public static float LocalUnverifiedStock(float localWorldInventoryKg, float looseKg, float reachableLooseKg, float storedKg)
        {
            // WorldInventory already tracks Pickupables, including loose ones. Use the
            // larger measurement to tolerate refresh lag without double-counting the scan.
            return Math.Max(Math.Max(localWorldInventoryKg, looseKg), storedKg) - reachableLooseKg > 0f
                ? Math.Max(Math.Max(localWorldInventoryKg, looseKg), storedKg) - reachableLooseKg
                : 0f;
        }

        public static float ForeignInventoryContext(float relatedWorldKg, float localWorldKg)
        {
            return Math.Max(0f, relatedWorldKg - localWorldKg);
        }

        public static MaterialReadiness BatchMaterial(
            float requiredPerAnchorKg,
            int anchorCount,
            float reachableKg,
            float stockNotVerifiedReachableKg,
            bool hasLivingNavigator,
            bool selectedMaterialKnown)
        {
            if (anchorCount <= 0)
                return new MaterialReadiness("unknown", null);
            return Material(requiredPerAnchorKg * anchorCount, reachableKg, stockNotVerifiedReachableKg,
                hasLivingNavigator, selectedMaterialKnown);
        }

        public static MaterialReadiness Material(
            float requiredKg,
            float reachableKg,
            float stockNotVerifiedReachableKg,
            bool hasLivingNavigator,
            bool selectedMaterialKnown)
        {
            if (!selectedMaterialKnown || requiredKg <= 0f || !hasLivingNavigator)
                return new MaterialReadiness("unknown", null);

            float shortage = Math.Max(0f, requiredKg - reachableKg);
            if (shortage <= 0f)
                return new MaterialReadiness("sufficient", 0f);

            return stockNotVerifiedReachableKg > 0f
                ? new MaterialReadiness("unknown", null)
                : new MaterialReadiness("shortage", shortage);
        }

        public static string WorkPosition(bool footprintKnown, bool hasLivingNavigator, int candidateCount, int reachableCount)
        {
            if (!footprintKnown || !hasLivingNavigator || candidateCount <= 0)
                return "unknown";
            return reachableCount > 0 ? "reachable" : "unreachable";
        }
    }
}
