using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Tools;

internal static class TreeFilterEligibilityRegression
{
    internal static void Run()
    {
        var discovered = new HashSet<string>(StringComparer.Ordinal) { "Solid", "Dirt" };
        var children = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Solid"] = new[] { "Dirt", "SandStone" }
        };
        var selectable = TreeFilterEligibility.Selectable(
            new[] { "Solid", "Liquid" },
            false,
            discovered.Contains,
            category => children.ContainsKey(category) ? children[category] : new string[0],
            new[] { "SandStone" });

        Check(selectable.SetEquals(new[] { "Solid", "Dirt" }),
            "selectable tags follow discovered categories, their children, and forbidden tags");
        Check(!selectable.Contains("DefinitelyNotARealOniResource"),
            "a synthetic tag is not selectable just because a caller can construct it");

        var creature = TreeFilterEligibility.Selectable(
            new[] { "Liquid" },
            true,
            discovered.Contains,
            category => new string[0],
            null);
        Check(creature.Contains("Liquid") && !selectable.Contains("Liquid"),
            "only the creature-delivery exception accepts an undiscovered storage category");

        HashSet<string> next;
        string error;
        Check(!TreeFilterEligibility.TryApply(
                "replace",
                new[] { "Dirt" },
                new[] { "DefinitelyNotARealOniResource" },
                selectable,
                out next,
                out error)
            && next == null
            && error.Contains("DefinitelyNotARealOniResource"),
            "replace with an unknown tag is rejected before a new set is produced");
        Check(!TreeFilterEligibility.TryApply(
                "add",
                new[] { "Dirt" },
                new[] { "Dirt", "DefinitelyNotARealOniResource" },
                selectable,
                out next,
                out error)
            && next == null,
            "add is rejected as a whole when any requested tag is outside the selectable set");
        Check(TreeFilterEligibility.TryApply(
                "add",
                new[] { "Dirt" },
                new[] { "Solid" },
                selectable,
                out next,
                out error)
            && next.SetEquals(new[] { "Dirt", "Solid" }),
            "a discovered selectable tag can be added");
        Check(TreeFilterEligibility.TryApply(
                "remove",
                new[] { "Dirt", "RetiredSaveTag" },
                new[] { "RetiredSaveTag" },
                selectable,
                out next,
                out error)
            && next.SetEquals(new[] { "Dirt" }),
            "remove can clean a selected tag that is no longer selectable");
        Check(TreeFilterEligibility.TryApply(
                "clear",
                new[] { "RetiredSaveTag" },
                new string[0],
                new HashSet<string>(),
                out next,
                out error)
            && next.Count == 0,
            "clear remains available when nothing is selectable");

        var plan = TreeFilterEligibility.PlanFlatUpdates(
            new[] { "Keep", "Drop" },
            new[] { "Keep", "Add" },
            new[] { "Keep", "Drop", "Add", "Ignored" });
        Check(plan.Select(item => item.Key).SequenceEqual(new[] { "Add", "Drop" })
            && plan[0].Value && !plan[1].Value,
            "flat updates add the retained option before removing the old one");
        Check(!TreeFilterEligibility.PlanFlatUpdates(
                new[] { "Keep" },
                new[] { "Outside" },
                new[] { "Keep" })
            .Any(item => item.Key == "Outside"),
            "flat planning never selects a tag outside tagOptions");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
