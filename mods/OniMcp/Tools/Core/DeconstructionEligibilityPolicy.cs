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
            if (hasDeconstructable)
            {
                if (!allowDeconstruction && !instantBuildMode)
                {
                    return new DeconstructionEligibility(
                        false,
                        "deconstruction_disabled",
                        "Target does not allow deconstruction");
                }

                return new DeconstructionEligibility(true, null, null);
            }

            if (isUtilityTarget)
                return new DeconstructionEligibility(true, null, null);

            return new DeconstructionEligibility(
                false,
                "not_deconstructable",
                "Target is not deconstructable");
        }
    }
}
