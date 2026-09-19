namespace OniMcp.Tools
{
    internal static class SweepEligibilityPolicy
    {
        internal static string RejectionReason(bool hasClearable, bool isClearable)
        {
            if (!hasClearable)
                return "no_clearable";
            return null;
        }
    }
}
