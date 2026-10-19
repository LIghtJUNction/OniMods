using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class StepConditionRegression
{
    private static int assertions;

    internal static void Run()
    {
        InvalidExpressions();
        IdleQuantifiersAndStability();
        CompositionAndOrders();
        LegacyAndNotifications();
        Console.WriteLine("Step condition regression checks passed: " + assertions);
    }

    private static void InvalidExpressions()
    {
        foreach (string json in new[]
        {
            "{until:null}", "{until:[]}", "{until:true}", "{until:{}}", "{condition:{fact:'dupes_idle'}}",
            "{until:{any:[]}}", "{until:{all:'dupes_idle'}}", "{until:{all:[{}]}}",
            "{until:{all:[{fact:'alert'}],any:[{fact:'alert'}]}}",
            "{until:{fact:'dupes_idle',quanitifier:'all'}}", "{until:{fact:'dupes_idle',quantifier:'none'}}",
            "{until:{fact:'dupes_idle',schedule:'sleep'}}", "{until:{fact:'unknown'}}",
            "{until:{fact:'alert',contains:'x'}}", "{until:{fact:'alarm',contains:''}}",
            "{until:{fact:'orders_complete',stableSeconds:'2'}}", "{until:{fact:'alert',stableSeconds:-1}}",
            "{until:{fact:'alert',stableSeconds:31}}", "{until:{fact:'notification',type:'godo'}}",
            "{until:{fact:'alert'},condition:'alert'}"
        })
        {
            Check(!StepCondition.TryParse(JObject.Parse(json), out _, out string error) && !string.IsNullOrEmpty(error),
                "reject before execution: " + json);
        }
        JToken deep = new JObject { ["fact"] = "alert" };
        for (int i = 0; i < 4; i++) deep = new JObject { ["all"] = new JArray(deep) };
        Check(!StepCondition.TryParse(new JObject { ["until"] = deep }, out _, out _), "depth bounded");
        var many = new JArray();
        for (int i = 0; i < 32; i++) many.Add(new JObject { ["fact"] = "alert" });
        Check(!StepCondition.TryParse(new JObject { ["until"] = new JObject { ["any"] = many } }, out _, out _), "node count bounded");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Check(!StepCondition.TryParse(new JObject { ["until"] = new JObject { ["fact"] = "alert", ["stableSeconds"] = invalid } }, out _, out _), "finite stability bound");
    }

    private static void IdleQuantifiersAndStability()
    {
        var all = Parse("{until:{fact:'dupes_idle',quantifier:'all'}}");
        var any = Parse("{until:{fact:'dupes_idle',stableSeconds:0}}");
        var f = new StepConditionFacts();
        var e = new StepConditionEvaluator();
        Check(!e.Evaluate(all, f, 0, out _), "zero duplicants cannot satisfy all idle");
        Check(!e.Evaluate(any, f, 0, out _), "zero duplicants cannot satisfy any idle");
        f.Duplicants.Add(new StepDupeFact { Name = "one", Idle = true, WorkScheduled = true });
        f.Duplicants.Add(new StepDupeFact { Name = "two", Idle = false, WorkScheduled = true });
        f.Duplicants.Add(new StepDupeFact { Name = "three", Idle = true, WorkScheduled = true });
        Check(e.Evaluate(any, f, 1, out string reason) && reason == "dupe_idle:one", "any can match one native idle dupe");
        Check(!e.Evaluate(all, f, 1, out _), "busy dupe blocks all");
        f.Duplicants[1].Idle = true;
        Check(!e.Evaluate(all, f, 2, out _), "all must remain idle for default two game seconds");
        f.Duplicants[1].Idle = false;
        Check(!e.Evaluate(all, f, 3, out _), "chore handoff resets stability");
        f.Duplicants[1].Idle = true;
        Check(!e.Evaluate(all, f, 4, out _), "stability restarts");
        Check(e.Evaluate(all, f, 6, out reason) && reason == "all_dupes_idle", "all native idle for two seconds");
        f.Duplicants[1].WorkScheduled = false;
        Check(!e.Evaluate(all, f, 7, out _), "off-work dupe blocks work-scoped all rather than shrinking population");
        var anySchedule = Parse("{until:{fact:'dupes_idle',quantifier:'all',schedule:'any',stableSeconds:0}}");
        Check(e.Evaluate(anySchedule, f, 7, out _), "agent may choose schedule any");
    }

    private static void CompositionAndOrders()
    {
        var condition = Parse("{until:{all:[{fact:'orders_complete'},{any:[{fact:'dupes_idle',quantifier:'all',stableSeconds:2},{fact:'cycle_end'}]}]}}");
        var e = new StepConditionEvaluator();
        var f = new StepConditionFacts { InitialTrackedOrders = 5, RemainingTrackedOrders = 1 };
        f.Duplicants.Add(new StepDupeFact { Name = "one", Idle = true, WorkScheduled = true });
        Check(!e.Evaluate(condition, f, 0, out _), "pending build prevents all combination");
        f.RemainingTrackedOrders = 0;
        Check(e.Evaluate(condition, f, 2, out string reason) && reason == "condition:all", "nested any/all compose independent facts");
        f.InitialTrackedOrders = 0;
        Check(!e.Evaluate(condition, f, 3, out _), "zero untracked orders cannot masquerade as completed work");
        f.InitialTrackedOrders = 5;
        var legacyAny = Parse("{condition:'any'}");
        Check(e.Evaluate(legacyAny, f, 3, out reason) && reason == "orders_complete", "legacy any retains the matched primitive stop reason");
        var orders = Parse("{condition:'orders_complete'}");
        f.InitialTrackedOrders = 5;
        Check(e.Evaluate(orders, f, 4, out reason) && reason == "orders_complete", "captured five builds reaching zero is complete without digs");

        // The idle leaf must reset while an earlier all child is false.
        e = new StepConditionEvaluator();
        f.RemainingTrackedOrders = 1;
        e.Evaluate(condition, f, 0, out _);
        f.Duplicants[0].Idle = false;
        e.Evaluate(condition, f, 1, out _);
        f.Duplicants[0].Idle = true;
        f.RemainingTrackedOrders = 0;
        Check(!e.Evaluate(condition, f, 2, out _), "all evaluates every child and resets hidden stability");
        Check(e.Evaluate(condition, f, 4, out _), "fresh stable interval then matches");
    }

    private static void LegacyAndNotifications()
    {
        foreach (string legacy in new[] { "orders_complete", "dupe_idle", "all_dupes_idle", "cycle_end", "alert", "any",
            "print", "research", "alarm", "notification", "notify:good", "notify:bad", "notify:message", "notify:event", "alarm:WaterFull" })
        {
            var condition = Parse("{condition:'" + legacy + "'}");
            Check(StepCondition.TryParse(new JObject { ["until"] = condition.ToJson() }, out _, out _), "legacy normalizes into valid expression: " + legacy);
        }
        var f = new StepConditionFacts();
        f.Notifications.Add(new StepNotificationFact { Title = "Pipe broken", Type = "Bad", Notifier = "Pipe", IsNew = true });
        var e = new StepConditionEvaluator();
        Check(e.Evaluate(Parse("{until:{fact:'notification',type:'Bad',contains:'PIPE'}}"), f, 0, out _), "notification predicates combine type and case-insensitive substring");
        Check(!e.Evaluate(Parse("{until:{fact:'notification',type:'Good'}}"), f, 0, out _), "unmatched notification type does not pause");
        Check(e.Evaluate(Parse("{condition:'notification'}"), f, 0, out _), "new notification compatibility");
        f.Alarms.Add("WaterFull");
        Check(e.Evaluate(Parse("{until:{fact:'alarm',contains:'water'}}"), f, 0, out _), "alarm atomic predicate");
    }

    private static StepCondition Parse(string json)
    {
        if (!StepCondition.TryParse(JObject.Parse(json), out var condition, out string error)) throw new InvalidOperationException(error);
        return condition;
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("Step condition regression: " + message);
    }
}
