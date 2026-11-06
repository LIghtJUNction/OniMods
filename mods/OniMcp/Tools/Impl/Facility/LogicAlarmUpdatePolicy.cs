using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class LogicAlarmUpdatePolicy
    {
        public static bool TryApplyTextAndType(
            JObject args,
            Action<string> setName,
            Action<string> setTooltip,
            Action<NotificationType> setType,
            out string error)
        {
            error = null;

            bool hasName = args["name"] != null;
            bool hasTooltip = args["tooltip"] != null;
            bool hasType = args["type"] != null;
            string name = hasName ? Truncate(args["name"].ToString(), 30) : null;
            string tooltip = hasTooltip ? Truncate(args["tooltip"].ToString(), 90) : null;
            NotificationType notificationType = NotificationType.Neutral;

            if (hasType && !TryParseType(args["type"].ToString(), out notificationType))
            {
                error = "type must be bad, neutral, or duplicant_threatening";
                return false;
            }

            if (hasName)
                setName(name);
            if (hasTooltip)
                setTooltip(tooltip);
            if (hasType)
                setType(notificationType);

            return true;
        }

        private static bool TryParseType(string value, out NotificationType type)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bad":
                    type = NotificationType.Bad;
                    return true;
                case "neutral":
                    type = NotificationType.Neutral;
                    return true;
                case "duplicant_threatening":
                case "duplicantthreatening":
                case "threat":
                    type = NotificationType.DuplicantThreatening;
                    return true;
                default:
                    type = NotificationType.Neutral;
                    return false;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;
            return value.Substring(0, maxLength);
        }
    }
}
