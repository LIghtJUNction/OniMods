using System;
using HarmonyLib;
using OniOnline.Runtime;
using OniOnline.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(MainMenu), "OnPrefabInit")]
    internal static class MainMenuOnlinePatch
    {
        private static void Postfix(MainMenu __instance)
        {
            try
            {
                var original = AccessTools.Field(typeof(MainMenu), "Button_NewGame").GetValue(__instance) as KButton;
                if (original == null || original.transform.parent.Find("OniOnlineBrowseLobbies") != null) return;
                var clone = UnityEngine.Object.Instantiate(original.gameObject, original.transform.parent, false);
                clone.name = "OniOnlineBrowseLobbies";
                clone.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
                var button = clone.GetComponent<KButton>();
                button.ClearOnClick(); button.ClearOnPointerEvents();
                button.onClick += () => OnlineRuntime.Instance?.BrowseSystems();
                foreach (var label in clone.GetComponentsInChildren<LocText>(true))
                    label.SetText(OnlineText.Get("menu.browse_lobbies"));
            }
            catch (Exception error) { Debug.LogWarning("[OniOnline] Main menu integration failed: " + error.GetType().Name); }
        }
    }

    [HarmonyPatch(typeof(ClusterCategorySelectionScreen), "OnClickOption")]
    internal static class NativeCreationIntentPatch
    {
        private static void Prefix() { OnlineRuntime.Instance?.ClearCreationIntent(); }
    }

    [HarmonyPatch(typeof(NewGameFlow), "BeginFlow")]
    internal static class NewCreationFlowPatch
    {
        private static void Prefix() { OnlineRuntime.Instance?.BeginNativeFlowFromMenu(); }
    }

    [HarmonyPatch(typeof(MainMenu), "LoadGame")]
    internal static class IndependentLoadMenuPatch
    {
        private static void Prefix() { OnlineRuntime.Instance?.BeginNativeLoadFromMenu(); }
    }

    [HarmonyPatch(typeof(KModalScreen), nameof(KModalScreen.OnKeyDown))]
    internal static class NativeEntryEscapePatch
    {
        private static void Prefix(KModalScreen __instance, KButtonEvent e)
        {
            if (!e.Consumed && e.IsAction(global::Action.Escape)) OnlineRuntime.Instance?.CancelNativeModal(__instance);
        }
    }

    [HarmonyPatch(typeof(LoadScreen), "OnPrefabInit")]
    internal static class NativeLoadCancelPatch
    {
        private static void Postfix(LoadScreen __instance)
        {
            var close = AccessTools.Field(typeof(LoadScreen), "closeButton")?.GetValue(__instance) as KButton;
            if (close != null) close.onClick += () => OnlineRuntime.Instance?.CancelNativeModal(__instance);
        }
    }

    [HarmonyPatch(typeof(ClusterCategorySelectionScreen), "OnSpawn")]
    internal static class OnlineCategoryPatch
    {
        private static void Postfix(ClusterCategorySelectionScreen __instance)
        {
            try
            {
                var runtime = OnlineRuntime.Instance;
                runtime?.ClearCreationIntent();
                NativeOnlineCategory.Attach(__instance, runtime);
            }
            catch (Exception error) { Debug.LogWarning("[OniOnline] Online category integration failed: " + error.GetType().Name); }
        }
    }
}
