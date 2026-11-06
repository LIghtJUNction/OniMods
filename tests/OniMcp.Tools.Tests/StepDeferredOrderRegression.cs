using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class StepDeferredOrderRegression
{
    internal static void Run()
    {
        string first = "pending";
        string second = "pending";
        var tracker = new StepDeferredOrderTracker(new[]
        {
            new KeyValuePair<string, Func<string>>("first", () => first),
            new KeyValuePair<string, Func<string>>("second", () => second)
        });
        Check(tracker.InitialCount == 2 && tracker.RemainingCount == 2, "Initial workflows were not captured.");
        first = "blueprint_placed";
        second = "built";
        Check(tracker.RemainingCount == 1, "A blueprint created after digging was treated as completed work.");
        first = "built";
        Check(tracker.RemainingCount == 0 && tracker.InitialCount == 2, "Finished workflow did not clear or initial count changed.");
        first = "cancelled";
        second = "failed";
        Check(tracker.RemainingCount == 0, "Terminal workflows remained pending.");
        var states = (Dictionary<string, int>)tracker.ToJson()["states"];
        Check(states["failed"] == 1 && states["cancelled"] == 1, "Terminal outcomes were hidden by a zero remaining count.");
        var unknown = new StepDeferredOrderTracker(new[]
        {
            new KeyValuePair<string, Func<string>>("unreadable", () => { throw new InvalidOperationException("fixture"); })
        });
        Check(unknown.RemainingCount == 1, "Unreadable workflow state was treated as completed.");
        Console.WriteLine("Deferred order tracking regressions passed (dig -> blueprint -> built and terminal outcomes)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
