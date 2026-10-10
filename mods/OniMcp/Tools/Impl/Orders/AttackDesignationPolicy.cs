namespace OniMcp.Tools
{
    internal static class AttackDesignationPolicy
    {
        internal const string NotTargetableStatus = "skipped_not_targetable";
        internal const string GameRejectedStatus = "skipped_game_rejected_target";

        internal static bool CanAttemptMark(bool canBePlayerTargeted, bool alignmentActive)
        {
            return canBePlayerTargeted && alignmentActive;
        }

        internal static bool MarkAccepted(bool isPlayerTargeted)
        {
            return isPlayerTargeted;
        }
    }
}
