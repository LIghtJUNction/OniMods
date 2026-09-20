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
            Action<string> setType,
            out string error)
        {
            error = null;

            if (args["name"] != null)
                setName(Truncate(args["name"].ToString(), 30));
            if (args["tooltip"] != null)
                setTooltip(Truncate(args["tooltip"].ToString(), 90));

            if (args["type"] != null)
            {
                string normalizedType;
                if (!TryNormalizeType(args["type"].ToString(), out normalizedType))
                {
                    error = "type must be bad, neutral, or duplicant_threatening";
                    return false;
                }
                setType(normalizedType);
            }

            return true;
        }

        private static bool TryNormalizeType(string value, out string normalized)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "bad":
                    normalized = "bad";
                    return true;
                case "neutral":
                    normalized = "neutral";
                    return true;
                case "duplicant_threatening":
                case "duplicantthreatening":
                case "threat":
                    normalized = "duplicant_threatening";
                    return true;
                default:
                    normalized = null;
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
