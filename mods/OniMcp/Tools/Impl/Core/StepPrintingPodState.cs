namespace OniMcp.Tools
{
    internal static class StepPrintingPodState
    {
        internal static bool IsAvailable
        {
            get
            {
                var immigration = Immigration.Instance;
                return immigration != null && immigration.ImmigrantsAvailable;
            }
        }
    }
}
