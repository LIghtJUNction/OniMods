using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class BenchmarkMetadataRegressionEntry
{
    private static void Main()
    {
        RunBenchmarkMetadataRegression();
        RadboltDirectionEligibilityRegression.Run();

        var existing = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing tools regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunBenchmarkMetadataRegression()
    {
        if (!HarvestMarkPolicy.ShouldMarkNow(true, true)
            || !HarvestMarkPolicy.ShouldMarkNow(true, false)
            || HarvestMarkPolicy.ShouldMarkNow(false, true)
            || HarvestMarkPolicy.ShouldMarkNow(false, false))
        {
            throw new InvalidOperationException(
                "harvest mark policy must reject unready targets regardless of readyOnly compatibility input");
        }

        OniToolRegistry.Tools["world_editor"] = new McpTool
        {
            Name = "world_editor",
            Group = "core",
            Mode = "write",
            Risk = "high",
            Handler = args => CallToolResult.Text("ok")
        };
        OniToolRegistry.Tools["server_control"] = new McpTool
        {
            Name = "server_control",
            Group = "server",
            Mode = "read/execute",
            Risk = "medium",
            Aliases = new List<string> { "mcp_server_control" },
            Parameters = new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Required = true }
            },
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

        CallToolResult invalidIncludeDetails = benchmark.Handler(new JObject
        {
            ["cases"] = "toolList",
            ["iterations"] = 1,
            ["includeDetails"] = "not-a-bool"
        });
        if (!invalidIncludeDetails.IsError)
        {
            throw new InvalidOperationException(
                "benchmark silently replaced an invalid includeDetails value with false");
        }

        CallToolResult legacyStringIncludeDetails = benchmark.Handler(new JObject
        {
            ["cases"] = "toolList",
            ["iterations"] = 1,
            ["includeDetails"] = "true"
        });
        if (legacyStringIncludeDetails.IsError)
            throw new InvalidOperationException("benchmark rejected a legacy parseable includeDetails value");

        CallToolResult lookupAlias = benchmark.Handler(new JObject
        {
            ["cases"] = "lookup",
            ["iterations"] = 1,
            ["tool"] = "world_editor"
        });
        if (lookupAlias.IsError)
            throw new InvalidOperationException("benchmark lookup alias was rejected unexpectedly");

        CallToolResult toolNameAlias = benchmark.Handler(new JObject
        {
            ["cases"] = "toolLookup",
            ["iterations"] = 1,
            ["tool"] = "mcp_server_control",
            ["includeDetails"] = true
        });
        if (toolNameAlias.IsError)
            throw new InvalidOperationException("benchmark rejected a registered tool-name alias");

        JObject aliasBody = JObject.Parse(toolNameAlias.Content[0].Text);
        JObject aliasResult = (JObject)aliasBody["results"]?[0];
        if (!string.Equals((string)aliasResult?["tool"], "mcp_server_control", StringComparison.Ordinal))
            throw new InvalidOperationException("benchmark no longer reports the requested alias in its tool field");

        JObject aliasDetails = aliasResult?["details"] as JObject;
        if (!string.Equals((string)aliasDetails?["group"], "server", StringComparison.Ordinal)
            || !string.Equals((string)aliasDetails?["mode"], "read/execute", StringComparison.Ordinal)
            || !string.Equals((string)aliasDetails?["risk"], "medium", StringComparison.Ordinal)
            || (int?)aliasDetails?["aliasCount"] != 1
            || (int?)aliasDetails?["parameterCount"] != 1)
        {
            throw new InvalidOperationException(
                "benchmark alias lookup did not report metadata from the resolved canonical tool");
        }
    }
}
