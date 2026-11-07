namespace OniMcp
{
    /// <summary>
    /// Compatibility shim forwarding to Jlib's PeterHan.PLib.Actions.KButtonEventSafety.
    /// </summary>
    internal static class KButtonEventSafety
    {
        public static bool TryGetActionArray(KButtonEvent buttonEvent, out bool[] isAction) =>
            PeterHan.PLib.Actions.KButtonEventSafety.TryGetActionArray(buttonEvent, out isAction);

        public static void EnsureActionArrayCapacity(KButtonEvent buttonEvent) =>
            PeterHan.PLib.Actions.KButtonEventSafety.EnsureActionArrayCapacity(buttonEvent);

        public static bool SafeIsAction(KButtonEvent buttonEvent, Action action) =>
            PeterHan.PLib.Actions.KButtonEventSafety.SafeIsAction(buttonEvent, action);

        public static bool SafeTryConsume(KButtonEvent buttonEvent, Action action) =>
            PeterHan.PLib.Actions.KButtonEventSafety.SafeTryConsume(buttonEvent, action);

        public static int GetActionArrayLength(KButtonEvent buttonEvent) =>
            PeterHan.PLib.Actions.KButtonEventSafety.GetActionArrayLength(buttonEvent);
    }
}
