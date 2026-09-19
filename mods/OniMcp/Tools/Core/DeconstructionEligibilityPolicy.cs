namespace OniMcp.Tools
{
    internal readonly struct DeconstructionEligibility
    {
        internal DeconstructionEligibility(bool canQueue, string reasonCode, string error)
        {
            CanQueue = canQueue;
            ReasonCode = reasonCode;
            Error = error;
        }

        internal bool CanQueue { get; }
        internal string ReasonCode { get; }
        internal string Error { get; }
    }

    internal static class DeconstructionEligibilityPolicy
    {
        internal static DeconstructionEligibility Evaluate(
            bool hasDeconstructable,
            bool allowDeconstruction,
            bool instantBuildMode,
            bool isUtilityTarget)
        {
            // Mirrors the current preview behavior: once target resolution succeeds,
            // preview reports the operation as queueable without checking execution eligibility.
            return new DeconstructionEligibility(true, null, null);
        }
    }
}
