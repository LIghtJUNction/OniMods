namespace OniOnline.Core
{
    internal struct OnlineInputBinding
    {
        internal int Action, KeyCode, Modifiers;
        internal bool IgnoreRootConflicts, DebugFocus;
    }

    internal static class OnlineInputBindingPolicy
    {
        internal static bool Conflicts(OnlineInputBinding shortcut, OnlineInputBinding other, bool debugEnabled)
        {
            return other.Action != shortcut.Action && !other.IgnoreRootConflicts &&
                other.KeyCode == shortcut.KeyCode && other.Modifiers == shortcut.Modifiers &&
                // Global registers Ctrl+T even while DebugHandler.OnKeyDown returns
                // immediately. Other actions keep their normal strict conflict checks.
                (!other.DebugFocus || debugEnabled);
        }
    }

    // A gameplay HUD control must yield to any active native modal or pause-level
    // screen. Keep the decision pure so the Unity adapter and host tests share the
    // same fail-closed rule.
    internal static class OnlineUiPolicy
    {
        internal static bool BlocksGameplayCommunication(bool active, bool modal, float sortKey,
            float pauseMenuSortKey, bool exempt)
        {
            return active && !exempt && (modal || sortKey >= pauseMenuSortKey);
        }
    }
}
