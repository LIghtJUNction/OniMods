using System;
using OniOnline.Core;

internal static class OnlineInputGateTests
{
    internal static void Run(Action<bool, string> check)
    {
        BindingConflicts(check);
        var gate = new OnlineInputGate();
        check(!gate.UpdateVoice(true, true), "a held key without an accepted key-down never opens the microphone");
        gate.PressVoice(false);
        check(!gate.UpdateVoice(true, true), "a voice key pressed while editing does not start recording after editing ends");
        gate.PressVoice(true);
        check(gate.UpdateVoice(true, true), "an allowed deliberate press enables push-to-talk");
        check(!gate.UpdateVoice(false, true), "losing focus or opening a modal stops microphone transmission");
        check(!gate.UpdateVoice(true, true), "regaining focus with the key still held does not resume recording");
        gate.PressVoice(true);
        check(!gate.UpdateVoice(true, false), "releasing the main key or a required modifier stops recording");
        check(!gate.UpdateVoice(true, true), "reapplying a modifier without a new main-key press cannot restart recording");
        gate.PressVoice(true);
        gate.ReleaseVoice();
        check(!gate.UpdateVoice(true, true), "cancel and key-up clear recording authorization immediately");
    }
    private static void BindingConflicts(Action<bool, string> check)
    {
        const int T = 84, V = 86, Ctrl = 1;
        var chat = new OnlineInputBinding { Action = 1000, KeyCode = T, Modifiers = Ctrl };
        var focus = new OnlineInputBinding { Action = 1, KeyCode = T, Modifiers = Ctrl, DebugFocus = true };
        check(!OnlineInputBindingPolicy.Conflicts(chat, focus, false), "disabled native DebugFocus must not reserve default Ctrl+T");
        check(OnlineInputBindingPolicy.Conflicts(chat, focus, true), "enabled native DebugFocus still conflicts with Ctrl+T");
        var attack = new OnlineInputBinding { Action = 2, KeyCode = T };
        check(!OnlineInputBindingPolicy.Conflicts(chat, attack, false), "bare native T is separate from Ctrl+T");
        var rebound = chat; rebound.Modifiers = 0;
        check(OnlineInputBindingPolicy.Conflicts(rebound, attack, false), "rebinding chat to native bare T still fails closed");
        var otherMod = new OnlineInputBinding { Action = 2000, KeyCode = T, Modifiers = Ctrl };
        check(OnlineInputBindingPolicy.Conflicts(chat, otherMod, false), "another active action on Ctrl+T still conflicts");
        check(!OnlineInputBindingPolicy.Conflicts(chat, chat, false), "a shortcut does not conflict with its own action");
        otherMod.IgnoreRootConflicts = true;
        check(!OnlineInputBindingPolicy.Conflicts(chat, otherMod, false), "native ignore-root-conflicts semantics are preserved");
        var voice = new OnlineInputBinding { Action = 1001, KeyCode = V, Modifiers = Ctrl };
        var vitals = new OnlineInputBinding { Action = 3, KeyCode = V };
        check(!OnlineInputBindingPolicy.Conflicts(voice, vitals, false), "default Ctrl+V does not replace native bare V");
        vitals.Modifiers = Ctrl;
        check(OnlineInputBindingPolicy.Conflicts(voice, vitals, false), "push-to-talk keeps rejecting a true rebound conflict");
        check(OnlineInputBindingPolicy.Conflicts(chat, focus, true) && !OnlineInputBindingPolicy.Conflicts(chat, focus, false),
            "conflict follows the current debug flag without a stale cached decision");
        focus.DebugFocus = false;
        check(OnlineInputBindingPolicy.Conflicts(chat, focus, false), "only the verified DebugFocus action gets the inactive exception");
    }

}
