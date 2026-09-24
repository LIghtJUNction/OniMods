namespace OniMcp.Tools
{
    internal static class PriorityWriteEligibilityPolicy
    {
        public static bool TryValidate(bool isPrioritizable, out string error)
        {
            if (isPrioritizable)
            {
                error = null;
                return true;
            }

            error = "Target is not currently prioritizable";
            return false;
        }
    }
}
