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
}
