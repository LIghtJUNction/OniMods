namespace OniMcp.Tools
{
    public static class HarvestMarkPolicy
    {
        public static bool ShouldMarkNow(bool canBeHarvested, bool readyOnly)
        {
            return canBeHarvested;
        }
    }
}
