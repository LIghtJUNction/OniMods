using System;
using HarmonyLib;
using OniOnline.Runtime;
using UnityEngine;

namespace OniOnline.UI
{
    // The in-game options screen is a second, deliberate path to the same
    // communications window. It stays hidden in the frontend and during
    // colony creation so the online HUD never leaks into setup screens.
    [HarmonyPatch(typeof(OptionsMenuScreen), "OnPrefabInit")]
    internal static class NativeSettingsChatEntryPatch
    {
        private static void Postfix(OptionsMenuScreen __instance)
        {
            try
            {
                if (__instance == null || __instance.transform.Find("OniOnlineSettingsChat") != null)
                    return;

                var field = AccessTools.Field(typeof(OptionsMenuScreen), "backButton");
                var template = field?.GetValue(__instance) as KButton;
                if (template == null || template.transform.parent == null)
                    return;

                var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
                clone.name = "OniOnlineSettingsChat";
                clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex());

                var button = clone.GetComponent<KButton>();
                if (button == null)
                {
                    UnityEngine.Object.Destroy(clone);
                    return;
                }

                button.ClearOnClick();
                button.ClearOnPointerEvents();
                foreach (var label in clone.GetComponentsInChildren<LocText>(true))
                    label.SetText(OnlineText.Get("chat.button"));

                var gate = __instance.gameObject.AddComponent<NativeSettingsChatEntry>();
                gate.Initialize(button);
                button.onClick += () =>
                {
                    var runtime = OnlineRuntime.Instance;
                    if (runtime != null && NativeGameplayUi.Ready)
                        runtime.ShowChat();
                };
            }
            catch (Exception error)
            {
                Debug.LogWarning("[OniOnline] Settings chat entry unavailable: " + error.GetType().Name);
            }
        }
    }

    internal sealed class NativeSettingsChatEntry : MonoBehaviour
    {
        private KButton button;

        internal void Initialize(KButton value)
        {
            button = value;
            button.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (button == null) return;
            var ready = NativeGameplayUi.Ready;
            if (button.gameObject.activeSelf != ready)
                button.gameObject.SetActive(ready);
        }
    }
}
