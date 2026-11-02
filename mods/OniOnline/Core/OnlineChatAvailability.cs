namespace OniOnline.Core
{
    // Keep the colony HUD entry visible while a session is reconnecting so the
    // player can read retained history and see the disabled composer state.
    internal static class OnlineChatAvailability
    {
        internal static bool IsVisible(bool enabled, bool joined, bool reconnecting, bool closed, bool leaving)
        { return enabled && !closed && !leaving && (joined || reconnecting); }

        internal static bool CanSend(bool enabled, bool joined, bool reconnecting, bool closed, bool leaving)
        { return enabled && joined && !reconnecting && !closed && !leaving; }
    }
}
