using System;
using OniOnline.Network;

namespace OniOnline.Core
{
    internal static class ConnectionStatusText
    {
        internal static string Error(string reason, string detail, Func<string, string> text)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            string key = reason.StartsWith("dependency.", StringComparison.Ordinal) ? reason : "error." + reason;
            string message = text(key);
            if (message == key) message = text("browser.connection_failed");
            if (!string.IsNullOrEmpty(detail))
            {
                // Reconnect expiry carries the last transport error code. Other
                // details, including moderation reasons, remain literal text.
                if (reason == "reconnect_expired") detail = Error(detail, null, text);
                message += " · " + detail;
            }
            return message;
        }

        internal static string Reconnecting(RoomSession session, Func<string, string> text, bool includeCause = true)
        {
            if (session == null || !session.Reconnecting) return "";
            string progress = session.ConnectionAttemptInProgress
                ? string.Format(text("ui.reconnect_attempt"), session.RetryAttempt)
                : string.Format(text("ui.reconnect_wait"), session.RetryAttempt + 1,
                    (int)Math.Ceiling(session.RetryInSeconds));
            string cause = includeCause ? Error(session.LastConnectionError, null, text) : "";
            return cause.Length == 0 ? progress : progress + " · " + cause;
        }
    }
}
