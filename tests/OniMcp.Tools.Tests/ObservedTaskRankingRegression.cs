using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class ObservedTaskRankingRegression
{
    internal static void Run()
    {
        var dupeOneQueue = new List<Candidate>
        {
            new Candidate("other high", 100, false),
            new Candidate("target A", 70, true),
            new Candidate("target B", 70, true),
            new Candidate("other low", 10, false)
        };
        var dupeOne = LocateTarget(dupeOneQueue);
        Require(dupeOne.Count == 2, "both matching task contexts are located in the full consumer queue");
        Require(dupeOne[0].ObservedQueueRank == 2 && dupeOne[1].ObservedQueueRank == 2,
            "higher-priority unrelated chore precedes target and tied target contexts share competition rank");
        Require(dupeOne[0].ObservedQueueLength == 4, "queue length counts all succeeded contexts, including unrelated chores");

        var dupeTwoQueue = new List<Candidate>
        {
            new Candidate("target", 40, true),
            new Candidate("other", 5, false)
        };
        var dupeTwo = LocateTarget(dupeTwoQueue);
        Require(dupeTwo.Count == 1 && dupeTwo[0].ObservedQueueRank == 1 && dupeTwo[0].ObservedQueueLength == 2,
            "each dupe receives an independent rank from its own full queue");
        Require(dupeOneQueue[0].Name == "other high", "ranking does not mutate the consumer snapshot list");
    }

    private static List<RankedItem<Candidate>> LocateTarget(List<Candidate> queue)
    {
        return ObservedTaskRanking.Locate(queue, item => item.IsTarget,
            (left, right) => right.Priority.CompareTo(left.Priority));
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("Observed task ranking regression: " + description);
    }

    private sealed class Candidate
    {
        internal readonly string Name;
        internal readonly int Priority;
        internal readonly bool IsTarget;

        internal Candidate(string name, int priority, bool isTarget)
        {
            Name = name;
            Priority = priority;
            IsTarget = isTarget;
        }
    }
}
