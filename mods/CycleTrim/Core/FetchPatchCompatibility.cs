using System;

namespace CycleTrim.Core
{
    internal static class FetchPatchCompatibility
    {
        internal const string DeliveryTemperatureLimitUpdatePickupsPatch =
            "DeliveryTemperatureLimit.FetchManager_FetchablesByPrefabId_Patch";
        internal const string DeliveryTemperatureLimitSupercooledPickupGroupingType =
            "DeliveryTemperatureLimit.KleiPickupTemperatureGroupingPatches";

        internal static bool IsDeliveryTemperatureLimitTranspiler(string declaringTypeFullName)
        {
            return string.Equals(
                declaringTypeFullName,
                DeliveryTemperatureLimitUpdatePickupsPatch,
                StringComparison.Ordinal);
        }

        internal static bool IsDeliveryTemperatureLimitSupercooledType(string typeFullName)
        {
            return string.Equals(
                typeFullName,
                DeliveryTemperatureLimitSupercooledPickupGroupingType,
                StringComparison.Ordinal);
        }
    }
}
