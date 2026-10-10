using System;

namespace OniMcp.Tools
{
    internal readonly struct BackwallSupportDecision
    {
        public bool Valid { get; }
        public string ReasonCode { get; }
        public string Error { get; }

        public BackwallSupportDecision(bool valid, string reasonCode, string error)
        {
            Valid = valid;
            ReasonCode = reasonCode;
            Error = error;
        }
    }

    internal static class BuildPlanningBackwallSupportPolicy
    {
        internal static BackwallSupportDecision Evaluate(string buildLocationRule, bool nativeFoundationValid)
        {
            if (!string.Equals(buildLocationRule, "OnBackWall", StringComparison.OrdinalIgnoreCase)
                || nativeFoundationValid)
            {
                return new BackwallSupportDecision(true, null, null);
            }

            return new BackwallSupportDecision(
                false,
                "backwall_required",
                "OnBackWall building requires a complete backwall foundation behind its oriented footprint.");
        }
    }
}
