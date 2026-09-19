using System;

namespace CycleTrim.Core
{
    internal static class FetchPatchCompatibility
    {
        internal const string DeliveryTemperatureLimitUpdatePickupsPatch =
            "DeliveryTemperatureLimit.FetchManager_FetchablesByPrefabId_Patch";

        internal static bool IsDeliveryTemperatureLimitTranspiler(string declaringTypeFullName)
        {
            return string.Equals(
                declaringTypeFullName,
                DeliveryTemperatureLimitUpdatePickupsPatch,
                StringComparison.Ordinal);
        }
    }
}
