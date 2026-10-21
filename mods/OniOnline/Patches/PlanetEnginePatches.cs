using HarmonyLib;
using OniOnline.Runtime;
using OniOnline.UI;
using UnityEngine;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(ClusterMapScreen), "OnShow")]
    internal static class StarSystemMapPatch
    {
        private static void Postfix(ClusterMapScreen __instance, bool show)
        {
            if (!show || OnlineRuntime.Instance == null) return;
            StarSystemPanel.Attach(__instance);
        }
    }

    [HarmonyPatch(typeof(ClusterMapScreen), nameof(ClusterMapScreen.OnKeyDown))]
    internal static class StarSystemNameInputPatch
    {
        private static bool Prefix(ClusterMapScreen __instance, KButtonEvent e)
        {
            var panel = __instance.GetComponent<StarSystemPanel>();
            if (panel == null || !panel.IsEditing || e.IsAction(global::Action.Escape)) return true;
            e.Consumed = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Telepad), "OnSpawn")]
    internal static class PlanetEnginePrintingPodPatch
    {
        private static void Postfix(Telepad __instance)
        {
            // This is a repeatable printing-pod entry, including in existing saves.
            // It consumes neither the printing reward nor a world-generation trait slot.
            __instance.Subscribe((int)GameHashes.RefreshUserMenu, _ =>
            {
                var runtime = OnlineRuntime.Instance;
                if (runtime == null || Game.Instance == null || Game.Instance.userMenu == null) return;
                Game.Instance.userMenu.AddButton(__instance.gameObject,
                    new KIconButtonMenu.ButtonInfo("action_research", OnlineText.Get("engine.title"),
                        () => runtime.OpenStarMap(true), tooltipText: OnlineText.Get("engine.tooltip")), 30);
            });
        }
    }
}
