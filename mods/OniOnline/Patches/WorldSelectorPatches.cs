using HarmonyLib;
using OniOnline.UI;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(WorldSelector), "RefreshToggles")]
    internal static class OnlineWorldSelectorPatch
    {
        private static void Postfix(WorldSelector __instance) { OnlineWorldSelector.Attach(__instance); }
    }
}
