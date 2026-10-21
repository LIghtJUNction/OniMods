using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class StepUntilResultCacheRegression
{
    internal static void Run()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var cache = new StepUntilResultCache(2, () => now);
        object gameA = new object();
        object gameB = new object();
        object rootA = new object();
        object rootB = new object();
        object watchedWorld = new object();
        object switchedActiveWorld = new object();
        cache.BindGame(gameA, rootA);
        cache.Add("a", gameA, rootA, new Dictionary<string, object> { ["stopReason"] = "orders_complete", ["isPaused"] = true });
        cache.Add("b", gameA, rootA, new Dictionary<string, object> { ["stopReason"] = "timeout_wall_clock" });
        if (!cache.TryGet("a", gameA, rootA, out var first) || (string)first["stopReason"] != "orders_complete")
            throw new System.InvalidOperationException("completed result should be addressable by request handle");
        if ((string)first["status"] != "completed" || !(bool)first["isHistorical"] || (double)first["resultAgeSeconds"] != 0d)
            throw new System.InvalidOperationException("completion should have an explicit historical status and initial age");
        if (!(bool)first["pausedAtCompletion"]) throw new System.InvalidOperationException("completion pause state must be retained");
        now = now.AddSeconds(7);
        if (!cache.TryGet("a", gameA, rootA, out first) || (double)first["resultAgeSeconds"] != 7d)
            throw new System.InvalidOperationException("result age must use the injected UTC clock");
        var resumed = StepUntilResultView.WithCurrentPause(first, false);
        if ((bool)resumed["isPaused"] || (bool)resumed["currentIsPaused"] || !(bool)resumed["pausedAtCompletion"])
            throw new System.InvalidOperationException("a later resume must update current pause state while preserving completion pause state");
        cache.Add("c", gameA, rootA, new Dictionary<string, object> { ["stopReason"] = "alert:old_bad_notification" });
        cache.Add("c", gameA, rootA, new Dictionary<string, object> { ["stopReason"] = "superseded" });
        if (!cache.TryGet("c", gameA, rootA, out var updated) || (string)updated["stopReason"] != "superseded")
            throw new System.InvalidOperationException("updating an existing result must replace it without adding a second capacity slot");
        if (cache.TryGet("a", gameA, rootA, out _)) throw new System.InvalidOperationException("cache must evict oldest result at capacity");
        if (!cache.TryGet("c", gameA, rootA, out _)) throw new System.InvalidOperationException("latest result should remain cached");
        if (cache.TryGet("c", gameB, rootA, out _)) throw new System.InvalidOperationException("results must not cross loaded-game identity");

        // activeWorld switches and save-as metadata changes are intentionally not inputs.
        var requestIdentity = new StepUntilLoadIdentity(gameA, rootA, watchedWorld);
        if (!requestIdentity.Matches(gameA, rootA, watchedWorld)) throw new System.InvalidOperationException("same watched world stays active across planetoid switches/save-as");
        var afterSaveAs = new StepUntilLoadIdentity(gameA, rootA, watchedWorld);
        if (!afterSaveAs.Matches(gameA, rootA, watchedWorld)) throw new System.InvalidOperationException("same-world save-as must preserve the loaded-world identity");
        if (requestIdentity.Matches(gameA, rootA, switchedActiveWorld)) throw new System.InvalidOperationException("a different container cannot masquerade as the pinned target world");
        cache.Add("d", gameA, rootA, new Dictionary<string, object>());
        if (cache.TryGet("d", gameA, rootB, out _)) throw new System.InvalidOperationException("results must clear when a newly loaded save replaces the root WorldContainer");
        if (cache.LatestId != null) throw new System.InvalidOperationException("rebinding loaded root world must clear handles");

        if (StepUntilCancelPolicy.Resolve("A", "B", true) != "completed")
            throw new System.InvalidOperationException("cancelling completed A while B runs must return A without touching B");
        if (StepUntilCancelPolicy.Resolve("unknown", "B", false) != "not_found")
            throw new System.InvalidOperationException("unknown handle must not cancel the active request");
        if (StepUntilCancelPolicy.Resolve("B", "B", false) != "cancel_active")
            throw new System.InvalidOperationException("matching active handle may cancel its own request");
        if (StepUntilCancelPolicy.Resolve(null, "B", false) != "cancel_current")
            throw new System.InvalidOperationException("no handle preserves cancel-current behavior");
    }
}

internal static class StepUntilResultCacheRegressionEntry
{
    private static void Main() => StepUntilResultCacheRegression.Run();
}
