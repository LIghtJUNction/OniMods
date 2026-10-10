using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class SweepHandlerRegression
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        try
        {
            foreach (bool dryRun in new[] { true, false })
                foreach (bool routed in new[] { false, true })
                    foreach (bool detail in new[] { false, true })
                        CheckMixedTargets(dryRun, routed, detail);
            CheckPreviewRechecksEligibility();
            Console.WriteLine("Sweep production-handler regression checks passed: " + assertions);
        }
        finally
        {
            Reset();
        }
    }

    private static void CheckMixedTargets(bool dryRun, bool routed, bool detail)
    {
        Reset();
        var blocked = Add(10010, false, 400);
        var normal = Add(10011, true, 5);
        var missing = Add(10012, null, 50);
        var equipped = Add(10013, true, 60);
        equipped.KPrefabID.Tags.Add(GameTags.Equipped);
        var stored = Add(10014, true, 70);
        stored.storage = new object();
        Add(30030, true, 80);
        Add(-1, true, 90);

        var args = Arguments(dryRun, detail);
        var tool = routed ? OrdersTools.AreaAction() : OrdersTools.SweepArea();
        args["action"] = "清扫";
        var result = tool.Handler(args);
        Check(!result.IsError, "mixed sweep request succeeds");
        var body = JObject.Parse(result.Content[0].Text);

        Check((int?)body["marked"] == 1, "only ordinary debris counts as marked or would-mark");
        Check((int?)body["scannedPickupables"] == 7 && (int?)body["inRect"] == 5,
            "scan and rectangle counts remain correct");
        Check((int?)body["skipped"]["notClearable"] == 1, "non-clearable skip count");
        Check((int?)body["skipped"]["noClearable"] == 1, "missing component skip count");
        Check((int?)body["skipped"]["equipped"] == 1 && (int?)body["skipped"]["stored"] == 1,
            "equipped and stored guards remain effective");
        Check((int?)body["skipped"]["invalidCell"] == 1, "invalid cells remain excluded");
        Check((double?)body["preview"]["kgTotal"] == 5, "rejected targets add no preview mass");
        Check((int?)body["execution"]["targetCount"] == 1, "only eligible cells reach execution metadata");
        Check((int?)body["execution"]["skipped"]["not_clearable"] == 1,
            "execution skip reason remains not_clearable");
        Check(blocked.GetComponent<Clearable>().MarkCalls == 0
            && !blocked.GetComponent<Clearable>().IsMarked, "non-clearable object is never designated");
        Check(blocked.GetComponent<SweepTestPriority>().Writes == 0
            && blocked.GetComponent<SweepTestPriority>().Value == 3, "non-clearable priority is unchanged");
        Check(normal.GetComponent<Clearable>().MarkCalls == (dryRun ? 0 : 1),
            "ordinary debris is designated only during execution");
        Check(normal.GetComponent<SweepTestPriority>().Writes == (dryRun ? 0 : 1)
            && normal.GetComponent<SweepTestPriority>().Value == (dryRun ? 3 : 9),
            "ordinary debris priority is changed only during execution");
        Check(missing.GetComponent<SweepTestPriority>().Writes == 0,
            "missing component does not permit priority mutation");

        foreach (var item in Components.Pickupables.Items.Where(item => item != normal))
        {
            Check(item.GetComponent<SweepTestPriority>().Writes == 0, "every skipped target retains priority");
            var clearable = item.GetComponent<Clearable>();
            Check(clearable == null || clearable.MarkCalls == 0, "every skipped target avoids designation");
        }

        var targets = (JArray)body["targets"];
        if (detail)
        {
            Check(targets.Count == 6, "detailed output includes eligible and skipped diagnostics");
            var rejected = targets.Children<JObject>().Single(row => (int)row["cell"] == blocked.Cell);
            Check((string)rejected["status"] == "skipped_not_clearable", "non-clearable diagnostic status");
            var eligible = targets.Children<JObject>().Single(row => (int)row["cell"] == normal.Cell);
            Check((string)eligible["status"] == (dryRun ? "would_mark" : "marked"), "eligible diagnostic status");
            Check(((JArray)body["preview"]["targets"]).Count == 1, "preview excludes rejected diagnostics");
        }
        else
        {
            Check(targets.Count == 0, "summary mode does not emit detailed targets");
        }
    }

    private static void CheckPreviewRechecksEligibility()
    {
        Reset();
        var target = Add(10010, true, 5);
        var tool = OrdersTools.SweepArea();
        var preview = tool.Handler(Arguments(true, true));
        Check(!preview.IsError, "preview succeeds before eligibility changes");
        var previewBody = JObject.Parse(preview.Content[0].Text);
        string token = (string)previewBody["previewToken"];
        Check(!string.IsNullOrEmpty(token), "preview produces a retry token");
        Check((int?)previewBody["marked"] == 1, "preview initially includes eligible debris");

        target.GetComponent<Clearable>().isClearable = false;
        var execution = tool.Handler(new JObject { ["previewToken"] = token });
        Check(!execution.IsError, "token execution reuses cached arguments");
        var body = JObject.Parse(execution.Content[0].Text);
        Check((bool?)body["dryRun"] == false && (int?)body["marked"] == 0,
            "token execution rechecks live eligibility instead of trusting the preview");
        Check((int?)body["skipped"]["notClearable"] == 1, "changed eligibility is counted as skipped");
        Check(target.GetComponent<Clearable>().MarkCalls == 0
            && target.GetComponent<SweepTestPriority>().Writes == 0,
            "preview and rejected token execution cause no designation or priority changes");
    }

    private static SweepTestPickupable Add(int cell, bool? clearable, float mass)
    {
        var item = new SweepTestPickupable { Cell = cell };
        item.gameObject.Add(new KPrefabID()).Add(new PrimaryElement { Mass = mass }).Add(new SweepTestPriority());
        if (clearable.HasValue)
            item.gameObject.Add(new Clearable { isClearable = clearable.Value });
        Components.Pickupables.Items.Add(item);
        return item;
    }

    private static JObject Arguments(bool dryRun, bool detail)
    {
        return new JObject
        {
            ["x1"] = 10, ["y1"] = 10, ["x2"] = 15, ["y2"] = 10,
            ["worldId"] = 0, ["dryRun"] = dryRun, ["detail"] = detail, ["priority"] = 9
        };
    }

    private static void Reset()
    {
        Components.Pickupables.Items.Clear();
        PreviewTokenRegistry.Reset();
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException("Sweep handler: " + message);
    }
}
