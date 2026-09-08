using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class Program
{
    private static int assertions;
    private static int calls;

    private static void Main()
    {
        OniToolRegistry.Tools["ok"] = new McpTool
        {
            Name = "ok", Mode = "read", Risk = "low",
            Handler = args => { calls++; return CallToolResult.Text("{\"value\":42}"); }
        };
        OniToolRegistry.Tools["fail"] = new McpTool
        {
            Name = "fail", Mode = "read", Risk = "low",
            Handler = args => { calls++; return CallToolResult.Error("expected failure"); }
        };
        OniToolRegistry.Tools["required"] = new McpTool
        {
            Name = "required", Mode = "write", Risk = "low",
            Parameters = new System.Collections.Generic.Dictionary<string, McpToolParameter>
            {
                ["value"] = new McpToolParameter { Required = true }
            },
            Handler = args => { calls++; return CallToolResult.Text("ok"); }
        };
        TestBatchFailures();
        TestProgramValidation();
        TestProgramExecution();
        TestNumbers();
        TestRegexBoundaries();
        Console.WriteLine("OniMcp tools regression checks passed: " + assertions);
    }

    private static void TestBatchFailures()
    {
        foreach (string mode in new[] { "summary", "full", "errors" })
        {
            calls = 0;
            var result = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{name:'ok'},{name:'fail'},{name:'ok'}],responseMode:'" + mode + "'}"));
            var body = Body(result);
            Check(result.IsError && calls == 3, mode + " must propagate child failure and continue by default");
            Check((int)body["failed"] == 1 && (int)body["succeeded"] == 2 && (int)body["executed"] == 3, "batch counts");
            if (mode == "errors")
                Check(((JArray)body["results"]).Count == 1 && (int)body["omitted"] == 2, "errors mode keeps error details");
        }

        calls = 0;
        var stopped = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{name:'fail'},{name:'ok'}],stopOnError:true}"));
        Check(stopped.IsError && calls == 1 && (bool)Body(stopped)["stopped"], "stopOnError must stop and propagate failure");

        calls = 0;
        var invalid = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{name:'ok'},{name:'required',args:{value:null}}]}"));
        Check(invalid.IsError && calls == 0, "null required input must fail before any child runs");
        Check((string)Body(invalid)["results"][1]["missingRequired"][0] == "value", "null required input reported");

        var valid = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{name:'required',args:{value:false}}]}"));
        Check(!valid.IsError && calls == 1, "false is a valid present required value");
    }

    private static void TestProgramValidation()
    {
        string[] invalidStatements =
        {
            "{op:'break'}", "{op:'continue'}", "{op:'set'}",
            "{op:'if',when:false,then:[{op:'break'}]}",
            "{op:'if',when:true,then:{op:'return'}}",
            "{op:'if',when:true,then:[],else:{op:'return'}}",
            "{op:'call',tool:'ok',args:[]}",
            "{op:'call',tool:'ok',arguments:'malformed'}",
            "{op:'while',when:false,do:{op:'break'}}"
        };
        foreach (string statement in invalidStatements)
        {
            foreach (bool dryRun in new[] { true, false })
            {
                calls = 0;
                var result = Run("[{op:'call',tool:'ok'}," + statement + "]", dryRun);
                Check(result.IsError && !(bool)Body(result)["valid"], "invalid structure rejected: " + statement);
                Check(calls == 0, "invalid program must never run earlier calls");
            }
        }

        var loop = Run("[{op:'repeat',count:2,do:[{op:'if',when:true,then:[{op:'continue'}]}]}]", true);
        Check(!loop.IsError, "continue nested in if inside loop is valid");
    }

    private static void TestProgramExecution()
    {
        calls = 0;
        var result = Run("{vars:{sum:0},steps:[{op:'repeat',count:3,do:[{op:'set',name:'sum',value:{add:['$sum',1]}}]},{op:'return',value:'$sum'}]}");
        Check(!result.IsError && (int)Body(result)["returnValue"] == 3, "repeat/set/return execute normally");

        result = Run("[{op:'call',tool:'fail'},{op:'call',tool:'ok'}]");
        Check(result.IsError && calls == 1, "failed call stops program");
        calls = 0;
        result = Run("[{op:'call',tool:'fail',continueOnError:true},{op:'call',tool:'ok',saveAs:'answer'},{op:'return',value:'$answer.value'}]");
        Check(!result.IsError && calls == 2 && (int)Body(result)["returnValue"] == 42, "continueOnError and saved results");
        result = Run("[{op:'while',when:true,do:[{op:'comment'}]}]");
        Check(result.IsError && ((string)Body(result)["error"]).Contains("maxLoopIterations"), "loop execution remains bounded");
    }

    private static void TestNumbers()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var result = Run("[{op:'return',value:{add:[1.25,2.5]}}]");
            Check(!result.IsError && (double)Body(result)["returnValue"] == 3.75, "numeric JSON arithmetic is culture independent");
        }
        finally { CultureInfo.CurrentCulture = original; }

        foreach (string expression in new[] { "{div:[1,0]}", "{mod:[1,0]}", "{mul:[1e308,1e308]}", "{add:['NaN',1]}", "{add:['Infinity',1]}" })
            Check(Run("[{op:'return',value:" + expression + "}]").IsError, "non-finite arithmetic rejected: " + expression);
        foreach (string count in new[] { "0.5", "2147483648", "-0.5" })
            Check(Run("[{op:'repeat',count:" + count + ",do:[]}]").IsError, "invalid repeat count rejected: " + count);
    }

    private static void TestRegexBoundaries()
    {
        WorldEditorTools.ReadText = "First Line\nsecond line\nthird";
        var result = WorldEditorTools.RunGrep(JObject.Parse("{query:'^first',regex:true,context:0}"));
        Check(!result.IsError && result.Content[0].Text.Contains("0001: First Line"), "grep regex preserves ignoreCase default");
        Check(WorldEditorTools.RunGrep(JObject.Parse("{query:'[',regex:true}")).IsError, "invalid regex returns error");
        Check(WorldEditorTools.MatchToken("Tile@(1,2)", "Tile"), "coordinate suffix token matching preserved");
        Check(WorldEditorTools.MatchToken("Tile", "/^Tile$/") && WorldEditorTools.MatchToken("Tile", "~^Tile$"), "map regex forms preserved");

        string hostile = new string('a', 20000) + "!";
        WorldEditorTools.ReadText = hostile;
        var timer = Stopwatch.StartNew();
        result = WorldEditorTools.RunGrep(JObject.Parse("{query:'^(a+)+$',regex:true}"));
        Check(result.IsError && result.Content[0].Text.Contains("timed out"), "grep catastrophic backtracking returns a clear error");
        Check(timer.Elapsed < TimeSpan.FromSeconds(5), "grep cannot hang the game thread indefinitely");
        foreach (string pattern in new[] { "/^(a+)+$/", "~^(a+)+$" })
        {
            bool timedOut = false;
            try { WorldEditorTools.MatchToken(hostile, pattern); }
            catch (RegexMatchTimeoutException) { timedOut = true; }
            Check(timedOut, "map regex propagates its timeout");
        }
    }

    private static CallToolResult Run(string program, bool dryRun = false) => AgentProgramTools.ExecuteProgram().Handler(new JObject { ["program"] = JToken.Parse(program), ["dryRun"] = dryRun });
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content[0].Text);
    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(description);
    }
}
