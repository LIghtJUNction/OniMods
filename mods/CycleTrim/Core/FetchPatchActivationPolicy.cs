namespace CycleTrim.Core
{
    internal static class FetchPatchActivationPolicy
    {
        internal static bool ShouldInstall(bool fastTrackPresent, bool efficientSupplyPresent)
        {
            return !fastTrackPresent && !efficientSupplyPresent;
        }
    }
}
