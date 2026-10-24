using System;
using OniOnline.Core;

internal static class OnlineChatAvailabilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        check(OnlineChatAvailability.IsVisible(true, true, false, false, false),
            "connected session exposes the colony chat entry");
        check(OnlineChatAvailability.IsVisible(true, false, true, false, false),
            "reconnecting session keeps the colony chat entry visible");
        check(!OnlineChatAvailability.IsVisible(true, false, false, false, false),
            "a session that never joined does not expose the chat entry");
        check(!OnlineChatAvailability.IsVisible(true, false, true, true, false),
            "an expired reconnect hides the chat entry");
        check(!OnlineChatAvailability.IsVisible(true, true, false, false, true),
            "a deliberate leave hides the chat entry");
        check(OnlineChatAvailability.CanSend(true, true, false, false, false),
            "a live session enables the composer");
        check(!OnlineChatAvailability.CanSend(true, false, true, false, false),
            "reconnecting keeps the composer disabled");
    }
}
