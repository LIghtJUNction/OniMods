namespace OniMcp.Tools
{
    internal static class DuplicantNeedStatePolicy
    {
        internal static string Availability(bool isBionic, bool amountsAvailable, bool definitionAvailable, bool valueAvailable)
        {
            if (valueAvailable)
                return "known";
            if (!amountsAvailable || !definitionAvailable)
                return "unknown";
            return isBionic ? "notApplicable" : "unknown";
        }

        internal static string WaterRisk(bool scanKnown, int exposedCount)
        {
            if (!scanKnown || exposedCount < 0)
                return "unknown";
            return exposedCount > 0 ? "risk" : "noneObserved";
        }
    }
}
