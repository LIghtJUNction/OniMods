namespace CycleTrim.Core
{
    internal static class FetchPatchActivationPolicy
    {
        internal static bool AllowsCycleTrimReplacement(bool efficientSupplyPresent)
        {
            return !efficientSupplyPresent;
        }
    }
}
