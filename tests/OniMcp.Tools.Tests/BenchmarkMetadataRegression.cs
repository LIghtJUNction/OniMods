using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class BenchmarkMetadataRegressionEntry
{
    private static void Main()
    {
        RunBenchmarkMetadataRegression();

        var existing = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing tools regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunBenchmarkMetadataRegression()
    {
        OniToolRegistry.Tools["world_editor"] = new McpTool
        {
            Name = "world_editor",
            Group = "core",
            Mode = "write",
            Risk = "high",
            Handler = args => CallToolResult.Text("ok")
        };

        McpTool benchmark = BenchmarkTools.Benchmark();
        McpToolParameter toolParameter = benchmark.Parameters["tool"];
        string description = toolParameter.Description ?? string.Empty;
        if (description.Contains("随机采样") || !description.Contains("world_editor"))
        {
            throw new InvalidOperationException(
                "benchmark tool schema does not describe its deterministic blank-tool default");
        }

        CallToolResult result = benchmark.Handler(new JObject
        {
            ["cases"] = "toolLookup",
            ["iterations"] = 1,
            ["tool"] = ""
        });
        if (result.IsError)
            throw new InvalidOperationException("benchmark blank-tool lookup failed unexpectedly");

        JObject body = JObject.Parse(result.Content[0].Text);
        string resolvedTool = (string)body["results"]?[0]?["tool"];
        if (!string.Equals(resolvedTool, "world_editor", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "benchmark blank-tool lookup no longer defaults to world_editor");
        }

        CallToolResult mixedInvalidCases = benchmark.Handler(new JObject
        {
            ["cases"] = "toolList,jsonSeralize",
            ["iterations"] = 1
        });
        if (!mixedInvalidCases.IsError)
        {
            throw new InvalidOperationException(
                "benchmark silently ignored an unknown case when another case was valid");
        }

        CallToolResult oversizedIterations = benchmark.Handler(new JObject
        {
            ["cases"] = "toolList",
            ["iterations"] = 2147483648L
        });
        if (!oversizedIterations.IsError)
        {
            throw new InvalidOperationException(
                "benchmark silently replaced an out-of-range integer iteration count with its default");
        }

        CallToolResult lookupAlias = benchmark.Handler(new JObject
        {
            ["cases"] = "lookup",
            ["iterations"] = 1,
            ["tool"] = "world_editor"
        });
        if (lookupAlias.IsError)
            throw new InvalidOperationException("benchmark lookup alias was rejected unexpectedly");
    }
}
