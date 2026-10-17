using System;

namespace OniMcp.Tools
{
    internal static class RadboltDirectionEligibilityPolicy
    {
        internal static bool TryParseDirection(string value, out EightDirection direction)
        {
            direction = default(EightDirection);
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(',') < 0
                && Enum.TryParse(value, true, out direction)
                && Enum.IsDefined(typeof(EightDirection), direction);
        }

        internal static bool CanControl(
            bool hasDirectionControl,
            bool hasRedirector,
            bool redirectorDirectionControllable)
        {
            return hasDirectionControl && (!hasRedirector || redirectorDirectionControllable);
        }
    }
}
