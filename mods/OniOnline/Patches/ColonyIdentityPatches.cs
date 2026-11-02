using HarmonyLib;
using OniOnline.Multiplayer;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
    internal static class ColonyIdentityPatch
    {
        private static void Postfix(SaveGame __instance)
        {
            __instance.gameObject.AddOrGet<OnlineColonyState>();
        }
    }
}
