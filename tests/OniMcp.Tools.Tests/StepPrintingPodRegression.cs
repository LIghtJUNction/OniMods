using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;
using OniMcp.Core;

internal static class StepPrintingPodRegression
{
    private static int assertions;

    internal static void Run()
    {
        Immigration.Instance = null;
        Check(!StepPrintingPodState.IsAvailable, "uninitialized immigration is not available");
        Immigration.Instance = new Immigration { ImmigrantsAvailable = true };
        // Readiness does not require initializing the screen or materializing reward choices.
        Check(StepPrintingPodState.IsAvailable, "already-ready native immigration is available without materialized choices");
        foreach (string input in new[] { "{condition:'print'}", "{condition:'printing_pod'}", "{condition:'printable'}",
            "{condition:'immigrant'}", "{condition:'printing_pod_ready'}", "{until:{fact:'printing_pod_ready'}}" })
        {
            var condition = Parse(input);
            var facts = new StepConditionFacts { PrintingPodReady = StepPrintingPodState.IsAvailable };
            var evaluator = new StepConditionEvaluator();
            Check(evaluator.EvaluateBeforeResume(condition, facts, out string reason)
                && reason == "notification:printing_pod_ready", "already-ready printing condition must match before resuming: " + input);
        }

        var printing = Parse("{until:{fact:'printing_pod_ready'}}");
        var f = new StepConditionFacts();
        var e = new StepConditionEvaluator();
        Immigration.Instance.ImmigrantsAvailable = false;
        f.PrintingPodReady = StepPrintingPodState.IsAvailable;
        Check(!e.EvaluateBeforeResume(printing, f, out _), "cooldown allows a bounded step to resume");
        Check(!e.Evaluate(printing, f, 1, out _), "cooldown remains false while running");
        Immigration.Instance.ImmigrantsAvailable = true;
        f.PrintingPodReady = StepPrintingPodState.IsAvailable;
        Check(e.Evaluate(printing, f, 2, out _), "cooldown-to-ready transition is still observed");

        var all = Parse("{until:{all:[{fact:'printing_pod_ready'},{fact:'orders_complete'}]}}");
        f.InitialTrackedOrders = 2;
        f.RemainingTrackedOrders = 1;
        Check(!new StepConditionEvaluator().EvaluateBeforeResume(all, f, out _), "ready leaf cannot bypass a false all sibling");
        f.RemainingTrackedOrders = 0;
        Check(new StepConditionEvaluator().EvaluateBeforeResume(all, f, out string allReason)
            && allReason == "condition:all", "all evaluates the complete expression before resuming");

        var any = Parse("{until:{any:[{fact:'orders_complete'},{fact:'printing_pod_ready'}]}}");
        f.RemainingTrackedOrders = 1;
        Check(new StepConditionEvaluator().EvaluateBeforeResume(any, f, out _), "any can stop for readiness with unfinished orders");
        Check(!new StepConditionEvaluator().EvaluateBeforeResume(Parse("{condition:'any'}"), f, out _),
            "legacy any does not silently acquire a printing condition");
        var unrelated = Parse("{until:{fact:'alarm'}}");
        f.Alarms.Add("ExistingAlarm");
        Check(!new StepConditionEvaluator().EvaluateBeforeResume(unrelated, f, out _), "pre-resume behavior of unrelated conditions stays unchanged");

        var stable = Parse("{until:{all:[{fact:'printing_pod_ready',stableSeconds:2},{fact:'orders_complete'}]}}");
        f.RemainingTrackedOrders = 0;
        e = new StepConditionEvaluator();
        Check(!e.EvaluateBeforeResume(stable, f, out _), "explicit stability requirement prevents an immediate match");
        Check(!e.Evaluate(stable, f, 1, out _), "stable interval uses game seconds");
        f.PrintingPodReady = false;
        Check(!e.Evaluate(stable, f, 1.5, out _), "lost readiness resets stability");
        f.PrintingPodReady = true;
        Check(!e.Evaluate(stable, f, 2, out _), "stability restarts after readiness returns");
        Check(e.Evaluate(stable, f, 4, out _), "complete expression matches after its stable interval");
        // Immediate completion may happen before the MCP transport subscribes to the deferred response.
        var deferred = new DeferredCallToolResult();
        var expected = CallToolResult.Text("already_ready");
        deferred.Complete(expected);
        object delivered = null;
        Exception failure = null;
        int deliveries = 0;
        deferred.RegisterCompletion((result, error) => { delivered = result; failure = error; deliveries++; });
        Check(deferred.IsCompleted && ReferenceEquals(delivered, expected) && failure == null && deliveries == 1,
            "pre-resume completion reaches a later transport subscriber");
        deferred.Complete(CallToolResult.Text("duplicate"));
        Check(ReferenceEquals(deferred.Result, expected) && deliveries == 1, "early completion is delivered only once");
        Immigration.Instance = null;
        Console.WriteLine("Step printing-pod regression checks passed: " + assertions);
    }

    private static StepCondition Parse(string json)
    {
        if (!StepCondition.TryParse(JObject.Parse(json), out var condition, out string error)) throw new InvalidOperationException(error);
        return condition;
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("Step printing-pod regression: " + message);
    }
}

// Native read surface only; the host does not load game assemblies or initialize reward UI.
internal sealed partial class Immigration
{
    internal static Immigration Instance;
    internal bool ImmigrantsAvailable;
}
