using System;
using OniMcp.Tools;

internal static class AttackDesignationPolicyRegression
{
    internal static void Run()
    {
        Check(AttackDesignationPolicy.CanAttemptMark(true, true),
            "active player-targetable attack targets must remain eligible");
        Check(!AttackDesignationPolicy.CanAttemptMark(false, true),
            "game-declared non-targetable entities must be rejected before marking");
        Check(!AttackDesignationPolicy.CanAttemptMark(true, false),
            "inactive faction alignments must be rejected before marking");
        Check(AttackDesignationPolicy.MarkAccepted(true),
            "a game-accepted attack mark must be reported as accepted");
        Check(!AttackDesignationPolicy.MarkAccepted(false),
            "a game-rejected attack mark must not be reported as accepted");
        Check(AttackDesignationPolicy.NotTargetableStatus == "skipped_not_targetable",
            "preflight targetability status must remain machine-readable");
        Check(AttackDesignationPolicy.GameRejectedStatus == "skipped_game_rejected_target",
            "post-call rejection status must remain machine-readable");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
