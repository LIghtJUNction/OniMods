using System;
using OniMcp.Tools;

internal static class DeferredBuildPolicyRegression
{
    internal static void Run()
    {
        Check(BuildPlanningDeferredBuildPolicy.HasPendingOrders(1, 0, 0, 0), "new dig mark remains pending");
        Check(BuildPlanningDeferredBuildPolicy.HasPendingOrders(0, 1, 0, 0), "existing dig mark remains pending");
        Check(BuildPlanningDeferredBuildPolicy.HasPendingOrders(0, 0, 1, 0), "new uproot mark remains pending");
        Check(BuildPlanningDeferredBuildPolicy.HasPendingOrders(0, 0, 0, 1), "existing uproot mark remains pending");
        Check(!BuildPlanningDeferredBuildPolicy.HasPendingOrders(0, 0, 0, 0), "no marks do not defer placement");

        Check(BuildPlanningDeferredBuildPolicy.AllTargetsQueued(2, 1, 1, 0, 0, 0, 0, false),
            "all native marks allow automatic continuation");
        Check(!BuildPlanningDeferredBuildPolicy.AllTargetsQueued(2, 1, 0, 0, 0, 1, 0, false),
            "failed target remains a manual continuation");
        Check(!BuildPlanningDeferredBuildPolicy.AllTargetsQueued(2, 1, 0, 0, 0, 0, 1, false),
            "skipped target remains a manual continuation");
        Check(!BuildPlanningDeferredBuildPolicy.AllTargetsQueued(2, 2, 0, 0, 0, 0, 0, true),
            "limit exhaustion prevents automatic continuation");
        Check(!BuildPlanningDeferredBuildPolicy.AllTargetsQueued(0, 0, 0, 0, 0, 0, 0, false),
            "empty obstruction set does not create pending work");

        Equal("blueprint_placed", BuildPlanningDeferredBuildPolicy.CompletionStatus(true, false, false));
        Equal("already_present", BuildPlanningDeferredBuildPolicy.CompletionStatus(false, true, false));
        Equal("pending", BuildPlanningDeferredBuildPolicy.CompletionStatus(false, false, true));
        Equal("failed", BuildPlanningDeferredBuildPolicy.CompletionStatus(false, false, false));

        Check(!BuildPlanningDeferredBuildPolicy.CanReuseHandle(false, true, true, true, true, true, true, true),
            "a caller-supplied fake handle cannot create a deferred request");
        Check(!BuildPlanningDeferredBuildPolicy.CanReuseHandle(true, true, true, true, true, false, true, true),
            "a deferred handle cannot be reused for a different anchor");
        Check(BuildPlanningDeferredBuildPolicy.CanReuseHandle(true, true, true, true, true, true, true, true),
            "an exact internal retry can reuse its pending handle");
        Check(!BuildPlanningDeferredBuildPolicy.CanCancel(true, true, true),
            "preview cancellation does not mutate pending state");
        Check(BuildPlanningDeferredBuildPolicy.IsCancelPreview(true),
            "preview cancellation follows the non-mutating production path");
        Check(!BuildPlanningDeferredBuildPolicy.IsCancelPreview(false),
            "confirmed cancellation exits the preview path");
        Check(!BuildPlanningDeferredBuildPolicy.CanCancel(true, false, false),
            "cancellation without confirmation does not mutate pending state");
        Check(BuildPlanningDeferredBuildPolicy.CanCancel(true, false, true),
            "confirmed non-preview cancellation is allowed");
        Check(BuildPlanningDeferredBuildPolicy.IsUnfinished("pending"), "pending deferred build is unfinished");
        Check(BuildPlanningDeferredBuildPolicy.IsUnfinished("blueprint_placed"), "placed blueprint remains unfinished");
        Check(!BuildPlanningDeferredBuildPolicy.IsUnfinished("built"), "built deferred build is terminal");
        Check(!BuildPlanningDeferredBuildPolicy.IsUnfinished("cancelled"), "cancelled deferred build is terminal");
        Check(!BuildPlanningDeferredBuildPolicy.IsUnfinished("failed"), "failed deferred build is terminal");
    }

    private static void Check(bool condition, string scenario)
    {
        if (!condition)
            throw new InvalidOperationException("Deferred build policy regression: " + scenario);
    }

    private static void Equal(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException("Deferred build policy regression: expected " + expected + ", got " + actual);
    }
}
