using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class StepResearchCompletionRegression
{
    private static int assertions;

    internal static void Run()
    {
        var jobs = new TechInstance("Jobs");
        var advanced = new TechInstance("AdvancedResearch");
        var tracker = new StepResearchTracker(jobs);
        var facts = new StepConditionFacts();
        // Observed ONI notification: localized Messages entry, reused from prior research.
        facts.Notifications.Add(new StepNotificationFact { Title = "研究完成", Type = "Messages", IsNew = false });
        Check(tracker.InitialTechId == "Jobs" && !tracker.InitiallyComplete, "capture starting technology");
        foreach (var condition in ResearchConditions())
        {
            var evaluator = new StepConditionEvaluator();
            facts.ResearchComplete = tracker.HasCompleted;
            Check(!evaluator.Evaluate(condition, facts, 0, out _), "existing research notification is not completion of current work");
            // Research.CheckBuyResearch purchases Jobs and calls GetNextTech in one update.
            jobs.Purchased();
            TechInstance current = advanced;
            facts.ResearchComplete = tracker.HasCompleted;
            Check(current != null && !current.IsComplete(), "next research remains active and incomplete");
            Check(evaluator.Evaluate(condition, facts, 1, out string reason)
                && reason == "notification:research_complete", "both legacy and until stop for the captured completed technology");
            // Use a fresh incomplete baseline for the second condition form.
            jobs = new TechInstance("Jobs");
            tracker = new StepResearchTracker(jobs);
        }

        facts.Notifications.Add(new StepNotificationFact { Title = "Research Complete: Basic Farming", Type = "Good", IsNew = false });
        facts.ResearchComplete = tracker.HasCompleted;
        foreach (var condition in ResearchConditions())
            Check(!new StepConditionEvaluator().Evaluate(condition, facts, 2, out _), "old Good research notification cannot complete a different starting technology");

        var lastTech = new TechInstance("AdvancedResearch");
        tracker = new StepResearchTracker(lastTech);
        lastTech.Purchased();
        Check(tracker.HasCompleted, "completion remains observable when there is no next research");
        Check(tracker.HasCompleted, "completion survives later observations instead of requiring an empty-slot transition");

        var cancelled = new TechInstance("Jobs");
        tracker = new StepResearchTracker(cancelled);
        TechInstance activeAfterCancel = null;
        Check(activeAfterCancel == null && !tracker.HasCompleted, "clearing the active slot is not successful completion");
        TechInstance manuallySelected = advanced;
        Check(manuallySelected != null && !tracker.HasCompleted, "switching to another incomplete technology is not completion");

        tracker = new StepResearchTracker(null);
        Check(tracker.InitialTechId == null && !tracker.HasCompleted, "no starting technology cannot complete");
        var alreadyComplete = new TechInstance("BasicFarming");
        alreadyComplete.Purchased();
        tracker = new StepResearchTracker(alreadyComplete);
        Check(tracker.InitiallyComplete && !tracker.HasCompleted, "already-complete baseline does not trigger a new completion");
        Console.WriteLine("Step research completion regression checks passed: " + assertions);
    }

    private static StepCondition[] ResearchConditions()
    {
        var result = new StepCondition[2];
        Check(StepCondition.TryParse(JObject.Parse("{condition:'research_complete'}"), out result[0], out _), "legacy research condition parses");
        Check(StepCondition.TryParse(JObject.Parse("{until:{fact:'research_complete'}}"), out result[1], out _), "research fact parses");
        return result;
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("Step research completion regression: " + message);
    }
}

// Host fixtures retain the native TechInstance IsComplete/Purchased surface; they do not run game assemblies.
internal sealed class Tech
{
    internal string Id;
}

internal sealed class TechInstance
{
    internal Tech tech;
    private bool complete;

    internal TechInstance(string id) { tech = new Tech { Id = id }; }
    internal bool IsComplete() => complete;
    internal void Purchased() { complete = true; }
}
