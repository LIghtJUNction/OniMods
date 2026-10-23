using HarmonyLib;
using OniOnline.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace OniOnline.UI
{
    [HarmonyPatch(typeof(ManagementMenu), "OnPrefabInit")]
    internal static class NativeChatEntryPatch
    {
        private static void Postfix(ManagementMenu __instance)
        {
            var source = __instance.PauseMenuButton;
            if (source == null || source.transform.parent.Find("OniOnlineCommunications") != null) return;
            var button = Util.KInstantiateUI<KToggle>(source.gameObject, source.transform.parent.gameObject, true);
            button.gameObject.name = "OniOnlineCommunications";
            button.ClearAllCallbacks(); button.ClearOnValueChanged();
            ((Toggle)button).onValueChanged.RemoveAllListeners();
            button.group = null; button.isOn = false;
            if (button.soundPlayer != null) button.soundPlayer.AcceptClickCondition = null;
            button.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
            if (button.fgImage != null)
            {
                button.fgImage.sprite = Assets.GetSprite("bubble_conversation");
                button.fgImage.preserveAspect = true;
            }
            var visibility = __instance.gameObject.AddComponent<NativeChatEntry>();
            visibility.Initialize(button);
            button.onClick += () => {
                button.isOn = false;
                var runtime = OnlineRuntime.Instance;
                if (runtime == null || !NativeGameplayUi.Ready) return;
                if (runtime.ChatOpen) runtime.HideChat(); else runtime.ShowChat();
            };
        }
    }

    internal sealed class NativeChatEntry : MonoBehaviour
    {
        private KToggle button;
        private string binding;
        internal void Initialize(KToggle value)
        {
            button = value;
            button.gameObject.SetActive(false);
        }
        private void Update()
        {
            if (button == null) return;
            bool ready = NativeGameplayUi.Ready;
            if (button.gameObject.activeSelf != ready) button.gameObject.SetActive(ready);
            string current = OnlineInputControls.ChatBinding;
            if (binding == current) return;
            binding = current;
            var tooltip = button.GetComponent<ToolTip>();
            if (tooltip != null) tooltip.SetSimpleTooltip(OnlineText.Get("chat.button") + " (" + current + ")");
        }
    }
}
