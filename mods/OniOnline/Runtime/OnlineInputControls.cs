using System.Collections.Generic;
using HarmonyLib;
using OniOnline.Core;
using OniOnline.UI;
using PeterHan.PLib.Actions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OniOnline.Runtime
{
    internal sealed class OnlineInputControls : KScreen
    {
        private static readonly System.Reflection.FieldInfo ScreenStack =
            AccessTools.Field(typeof(KScreenManager), "screenStack");
        private static PAction chatAction;
        private static PAction voiceAction;
        private readonly OnlineInputGate voiceGate = new OnlineInputGate();
        private OnlineRuntime runtime;

        internal static void RegisterActions()
        {
            var actions = new PActionManager();
            chatAction = actions.CreateAction("OniOnline.OpenChat", OnlineText.Get("input.chat"),
                new PKeyBinding(KKeyCode.T, Modifier.Ctrl));
            voiceAction = actions.CreateAction("OniOnline.PushToTalk", OnlineText.Get("input.voice"),
                new PKeyBinding(KKeyCode.V, Modifier.Ctrl));
        }

        internal static void Attach(OnlineRuntime runtime)
        {
            var root = new GameObject("OniOnlineInputControls");
            root.transform.SetParent(runtime.transform, false);
            var controls = root.AddComponent<OnlineInputControls>();
            controls.runtime = runtime;
            OnlineOptions.Saved += controls.OnOptionsSaved;
        }

        public override float GetSortKey() { return 1; }
        public override bool IsModal() { return false; }

        internal static bool EditingText
        {
            get
            {
                var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
                if (selected == null) return false;
                var tmp = selected.GetComponentInParent<TMP_InputField>();
                if (tmp != null && tmp.isActiveAndEnabled && tmp.isFocused) return true;
                var legacy = selected.GetComponentInParent<InputField>();
                return legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
            }
        }

        internal static bool ContextAllowsShortcuts
        {
            get
            {
                if (!Application.isFocused || !KInputManager.isFocused || EditingText ||
                    !NativeGameplayUi.Ready) return false;
                if (KScreenManager.Instance == null || ScreenStack == null) return false;
                var stack = ScreenStack.GetValue(KScreenManager.Instance) as List<KScreen>;
                if (stack == null) return false;
                foreach (var screen in stack)
                    if (screen != null && !(screen is OnlineInputControls) && screen.IsScreenActive() &&
                        (screen.IsModal() || screen.GetSortKey() >= PAUSE_MENU_SORT_KEY)) return false;
                return true;
            }
        }

        public override void OnKeyDown(KButtonEvent e)
        {
            if (!ContextAllowsShortcuts) return;
            if (chatAction != null && e.IsAction(chatAction.GetKAction()))
            {
                if (BindingConflicts(chatAction)) { ReportConflict(); return; }
                e.Consumed = true;
                runtime.ShowChat();
                ClearResolvedConflict();
            }
            else if (voiceAction != null && e.IsAction(voiceAction.GetKAction()) &&
                runtime.VoiceEnabled && runtime.Session?.Joined == true && !runtime.Session.Reconnecting)
            {
                if (BindingConflicts(voiceAction)) { ReportConflict(); return; }
                voiceGate.PressVoice(true);
                e.Consumed = true;
                ClearResolvedConflict();
            }
        }

        public override void OnKeyUp(KButtonEvent e)
        {
            if (voiceAction != null && e.IsAction(voiceAction.GetKAction()))
                voiceGate.ReleaseVoice();
        }

        private void Update()
        {
            if (KScreenManager.Instance == null)
            {
                voiceGate.ReleaseVoice();
                runtime.Voice?.SetPushToTalk(false);
                return;
            }
            if (!IsActive()) Activate();
            bool allowed = runtime.VoiceEnabled && ContextAllowsShortcuts && runtime.Session?.Joined == true &&
                !runtime.Session.Closed && !runtime.Session.Reconnecting;
            runtime.Voice?.SetPushToTalk(voiceGate.UpdateVoice(allowed, VoiceBindingHeld));
        }

        internal static bool VoiceBindingHeld
        {
            get
            {
                var binding = FindBinding(voiceAction);
                return binding.HasValue && ModifiersMatch(binding.Value.mModifier) &&
                    !BindingConflicts(voiceAction) && Global.GetInputManager().GetDefaultController().IsActive(voiceAction.GetKAction());
            }
        }

        private void ClearResolvedConflict()
        {
            if (runtime.Error != "input_binding_conflict") return;
            runtime.Error = null;
            runtime.Revision++;
        }

        private void ReportConflict()
        {
            runtime.Error = "input_binding_conflict";
            runtime.Revision++;
        }

        private static bool ModifiersMatch(Modifier required)
        {
            Modifier current = Modifier.None;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) current |= Modifier.Ctrl;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) current |= Modifier.Shift;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) current |= Modifier.Alt;
            if (Input.GetKey(KeyCode.CapsLock)) current |= Modifier.CapsLock;
            if (Input.GetKey(KeyCode.BackQuote)) current |= Modifier.Backtick;
            return current == required;
        }

        private void OnOptionsSaved(OnlineOptions options)
        {
            runtime.VoiceEnabled = options.VoiceEnabled;
            runtime.VoiceVolume = options.VoiceVolume;
            if (!options.VoiceEnabled)
            {
                voiceGate.ReleaseVoice();
                runtime.Voice?.SetPushToTalk(false);
            }
        }

        protected override void OnCleanUp()
        {
            OnlineOptions.Saved -= OnOptionsSaved;
            voiceGate.ReleaseVoice();
            runtime?.Voice?.SetPushToTalk(false);
            base.OnCleanUp();
        }

        private static bool BindingConflicts(PAction action)
        {
            var bindings = GameInputMapping.GetBindingEntries();
            var key = FindBinding(action);
            if (!key.HasValue || key.Value.mKeyCode == KKeyCode.None) return true;
            var shortcut = BindingSnapshot(key.Value);
            bool debugEnabled = DebugHandler.enabled;
            foreach (var other in bindings)
                if (OnlineInputBindingPolicy.Conflicts(shortcut, BindingSnapshot(other), debugEnabled)) return true;
            return false;
        }

        private static OnlineInputBinding BindingSnapshot(BindingEntry binding)
        {
            return new OnlineInputBinding { Action = (int)binding.mAction, KeyCode = (int)binding.mKeyCode,
                Modifiers = (int)binding.mModifier, IgnoreRootConflicts = binding.mIgnoreRootConflics,
                DebugFocus = binding.mAction == global::Action.DebugFocus };
        }

        private static BindingEntry? FindBinding(PAction action)
        {
            var bindings = GameInputMapping.GetBindingEntries();
            if (action != null && bindings != null)
                foreach (var entry in bindings)
                    if (entry.mAction == action.GetKAction()) return entry;
            return null;
        }

        private static string BindingLabel(PAction action)
        {
            var binding = FindBinding(action);
            if (!binding.HasValue || binding.Value.mKeyCode == KKeyCode.None) return "—";
            Modifier modifiers = binding.Value.mModifier;
            var parts = new List<string>();
            if ((modifiers & Modifier.Ctrl) != 0) parts.Add("Ctrl");
            if ((modifiers & Modifier.Shift) != 0) parts.Add("Shift");
            if ((modifiers & Modifier.Alt) != 0) parts.Add("Alt");
            if ((modifiers & Modifier.CapsLock) != 0) parts.Add("CapsLock");
            if ((modifiers & Modifier.Backtick) != 0) parts.Add("`");
            Modifier known = Modifier.Ctrl | Modifier.Shift | Modifier.Alt | Modifier.CapsLock | Modifier.Backtick;
            if ((modifiers & ~known) != 0) parts.Add("?");
            string key = GameUtil.GetKeycodeLocalized(binding.Value.mKeyCode);
            parts.Add(string.IsNullOrEmpty(key) ? "?" : key);
            return string.Join("+", parts.ToArray());
        }

        internal static bool ChatBindingConflicts { get { return BindingConflicts(chatAction); } }
        internal static bool VoiceBindingConflicts { get { return BindingConflicts(voiceAction); } }
        internal static string ChatBinding { get { return BindingLabel(chatAction); } }
        internal static string VoiceBinding { get { return BindingLabel(voiceAction); } }
    }
}
