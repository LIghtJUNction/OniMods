namespace OniMcp.Tools
{
    internal static class RadboltDirectionEligibilityPolicy
    {
        internal static bool CanControl(
            bool hasDirectionControl,
            bool hasRedirector,
            bool redirectorDirectionControllable)
        {
            return hasDirectionControl && (!hasRedirector || redirectorDirectionControllable);
        }
    }
}
