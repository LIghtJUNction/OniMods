using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class PublicReadGuidanceRegression
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        SmallStateStaysComplete();
        LargeStateHasBoundedPreview();
        PreviewContinuationPreservesReadOptions();
        LargeSnapshotRetainsSummary();
        InvalidJsonIsNotAdvertisedAsAPreview();
        FullDocumentReadPreservesArguments();
        PublicContinuationReachesTheDocument();
        PublicResourceAlternativesResolve();
        Console.WriteLine("Public read guidance: " + assertions + " assertions passed.");
    }

    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static JObject LargeState()
    {
        return new JObject
        {
            ["ok"] = true,
            ["kind"] = "current_state",
            ["snapshot"] = new JObject
            {
                ["cycle"] = 123,
                ["dupes"] = 3,
                ["summary"] = "氧气正常 — quote: \"; newline:\n下一行"
            },
            ["liveViewport"] = new JObject { ["x"] = 40, ["y"] = 60 },
            ["lookAroundPlan"] = new JArray(Enumerable.Range(0, 40).Select(index =>
                new JObject { ["index"] = index, ["detail"] = new string('界', 600) })),
            ["progressiveDetail"] = new JArray("one local cell", "one bounded zoom")
        };
    }

    private static void SmallStateStaysComplete()
    {
        const string text = "{\n  \"ok\": true,\n  \"snapshot\": {\"summary\":\"no omissions\"}\n}";
        Require(CurrentStateDocument.Preview(text, 9000) == text,
            "A small complete state was changed or wrapped as a preview.");
    }

    private static void LargeStateHasBoundedPreview()
    {
        var source = LargeState();
        string full = source.ToString(Formatting.None);
        Require(full.Length > 9000, "Fixture does not cross the actual index budget.");
        string previewText = CurrentStateDocument.Preview(full, 9000);
        var preview = JObject.Parse(previewText);
        Require(previewText.Length <= 9000, "Index preview exceeds its character budget.");
        Require((bool)preview["truncated"], "Omitted content was not declared.");
        Require((string)preview["kind"] == "current_state_preview", "Preview masquerades as a full state.");
        Require(JToken.DeepEquals(source["snapshot"], preview["snapshot"]),
            "A fitting snapshot lost values, Unicode, quotes, or newlines.");
        Require(JToken.DeepEquals(source["liveViewport"], preview["liveViewport"]),
            "A fitting viewport was lost.");
        Require(preview["lookAroundPlan"] == null, "Oversized section was partially copied.");
        Require(((JArray)preview["omittedSections"]).Values<string>().Contains("lookAroundPlan"),
            "The omitted section has no continuation disclosure.");
        Require(!((JArray)preview["omittedSections"]).Values<string>().Contains("snapshot"),
            "A preserved section is incorrectly listed as omitted.");
        Require(source.ToString(Formatting.None) == full, "Preview generation mutated the source.");
        Require((string)preview["nextRead"]["tool"] == "world_editor",
            "Continuation points to a retired public tool.");
        Require((string)preview["nextRead"]["arguments"]["path"] == "/active/colony/current.oni",
            "Continuation points to an unlisted virtual file.");
        Require(!string.IsNullOrWhiteSpace((string)preview["nextRead"]["arguments"]["task"]),
            "Continuation omitted the required task description.");
    }

    private static void PreviewContinuationPreservesReadOptions()
    {
        var options = new JObject
        {
            ["domain"] = "state", ["action"] = "current", ["includeState"] = true,
            ["profile"] = "full", ["includeLogs"] = true, ["logLimit"] = 80,
            ["includeReachability"] = true, ["reachabilityRadius"] = 6
        };
        string before = options.ToString(Formatting.None);
        var preview = JObject.Parse(CurrentStateDocument.Preview(LargeState().ToString(), 9000, options));
        var payload = (JObject)preview["nextRead"]["arguments"]["payload"];
        Require((string)payload["profile"] == "full" && (bool)payload["includeLogs"]
            && (int)payload["logLimit"] == 80, "Continuation lost the requested snapshot or log scope.");
        Require((bool)payload["includeReachability"] && (int)payload["reachabilityRadius"] == 6,
            "Continuation lost advanced options carried in world-editor payload.");
        Require(payload["domain"] == null && payload["action"] == null && payload["includeState"] == null,
            "Continuation exposed internal selectors or index-only arguments.");
        Require(options.ToString(Formatting.None) == before, "Continuation options were mutated.");
    }

    private static void LargeSnapshotRetainsSummary()
    {
        var state = LargeState();
        ((JObject)state["snapshot"])["items"] = new string('x', 12000);
        var preview = JObject.Parse(CurrentStateDocument.Preview(state.ToString(), 1024));
        Require(preview["snapshot"] == null, "Oversized snapshot was sliced into invalid partial content.");
        Require((string)preview["summary"] == (string)state["snapshot"]["summary"],
            "Useful small summary was lost when the full snapshot did not fit.");
        Require(preview["nextRead"] is JObject, "Large snapshot has no full continuation.");

        state["snapshot"] = new string('x', 12000);
        preview = JObject.Parse(CurrentStateDocument.Preview(state.ToString(), 1024));
        Require(preview["snapshot"] == null && preview["nextRead"] is JObject,
            "Text fallback snapshot broke preview generation.");
    }

    private static void InvalidJsonIsNotAdvertisedAsAPreview()
    {
        bool failed = false;
        try { CurrentStateDocument.Preview("{\"snapshot\":", 9000); }
        catch (JsonReaderException) { failed = true; }
        Require(failed, "Malformed producer output was silently emitted as JSON.");
    }

    private static void FullDocumentReadPreservesArguments()
    {
        var original = new JObject
        {
            ["command"] = "read",
            ["path"] = "/active/colony/current.oni",
            ["domain"] = "save",
            ["action"] = "quit",
            ["includeReachability"] = true,
            ["includeLogs"] = false,
            ["profile"] = "minimal",
            ["task"] = "Read complete state"
        };
        string before = original.ToString(Formatting.None);
        JObject received = null;
        int calls = 0;
        var expected = CallToolResult.Text(LargeState().ToString());
        Func<JObject, CallToolResult> read = args =>
        {
            calls++;
            received = args;
            return expected;
        };
        CallToolResult result;
        Require(CurrentStateDocument.TryRead("colony/current.oni", original, read, out result),
            "Listed relative virtual file did not route.");
        Require(ReferenceEquals(result, expected), "Full document was replaced or truncated.");
        Require((string)received["domain"] == "state" && (string)received["action"] == "current",
            "Caller could redirect the read to a different operation.");
        Require(received["command"] == null && received["path"] == null,
            "World-editor dispatch envelope leaked to the state reader.");
        Require((bool)received["includeReachability"] && !(bool)received["includeLogs"]
            && (string)received["profile"] == "minimal", "Read options were dropped.");
        Require(original.ToString(Formatting.None) == before, "Routing mutated caller arguments.");
        Require(!CurrentStateDocument.TryRead("colony/missing.oni", original, read, out result)
            && result == null && calls == 1, "Unrelated path was captured by the new read route.");
    }

    private static void PublicContinuationReachesTheDocument()
    {
        OniToolRegistry.Initialize();
        string full = LargeState().ToString(Formatting.None);
        var preview = JObject.Parse(CurrentStateDocument.Preview(full, 9000));
        var next = (JObject)preview["nextRead"];
        JObject received = null;
        GameStubs.CurrentStateReader = args =>
        {
            received = args;
            return CallToolResult.Text(full);
        };
        try
        {
            int before = GameStubs.HandlerCalls;
            var result = OniToolRegistry.CallTool((string)next["tool"],
                (JObject)next["arguments"].DeepClone());
            Require(!result.IsError && GameStubs.HandlerCalls == before + 1,
                "Advertised continuation did not pass real public registry dispatch.");
            Require(received != null && (string)received["action"] == "current",
                "Public continuation did not reach the production document route.");
            Require(result.Content[0].Text == full, "Full continuation lost content behind the preview.");
            Require(((JArray)JObject.Parse(result.Content[0].Text)["lookAroundPlan"]).Count == 40,
                "Continuation still contains only preview sections.");
            foreach (string retired in new[] { "read_control", "colony_control" })
            {
                before = GameStubs.HandlerCalls;
                Require(OniToolRegistry.CallTool(retired,
                    new JObject { ["task"] = "Inspect state" }).IsError
                    && GameStubs.HandlerCalls == before,
                    "Fix reopened retired public operation: " + retired);
            }
        }
        finally { GameStubs.CurrentStateReader = null; }
    }

    private static void PublicResourceAlternativesResolve()
    {
        // The production registry selects operations; only game handlers are stubbed.
        var cases = new[]
        {
            new[] { "oni://colony/status", "status" },
            new[] { "oni://colony/diagnostics", "diagnostics" },
            new[] { "oni://colony/alerts", "alerts" },
            new[] { "oni://resources/food", "food" },
            new[] { "oni://dupes", "dupes" },
            new[] { "oni://research/status", "status" },
            new[] { "oni://world/cell/4/5", "cell_info" }
        };
        foreach (var item in cases)
        {
            var result = OniResourceRegistry.ReadResource(item[0]);
            Require(result?.Contents?.Count == 1, "Advertised URI did not resolve: " + item[0]);
            var body = JObject.Parse(result.Contents[0].Text);
            Require(body["error"] == null && (string)body["arguments"]["action"] == item[1],
                "Advertised URI did not select its read operation: " + item[0]);
        }
    }
}
