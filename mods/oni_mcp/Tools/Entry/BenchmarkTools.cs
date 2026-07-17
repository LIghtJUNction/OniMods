using System;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class BenchmarkTools
    {
        public static McpTool Benchmark()
        {
            return new McpTool
            {
                Name = "benchmark",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Tags = new List<string> { "benchmark", "tests", "performance", "diagnostics", "tools", "测试" },
                Description = "工具链路基准测试入口：运行固定测试项并返回统一测试结果（ok/failed/tests/summary/duration），不执行游戏状态修改。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["cases"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "要运行的测试项，用逗号分隔。支持 all, toolList, toolLookup, jsonSerialize。默认 all。",
                        Required = false
                    },
                    ["iterations"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "每个测试项的循环次数（1-5000），默认 200。",
                        Required = false
                    },
                    ["tool"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "用于 toolLookup 的目标工具名（如 server_control），留空表示随机采样。默认 world_editor。",
                        Required = false
                    },
                    ["includeDetails"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "返回每项测试的更多细节字段，默认 false。",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    string cases = (args["cases"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    int iterations = ToolUtil.GetInt(args, "iterations") ?? 200;
                    string targetTool = args["tool"]?.ToString()?.Trim();
                    bool includeDetails = ToolUtil.GetBool(args, "includeDetails", false);

                    if (iterations < 1 || iterations > 5000)
                        return CallToolResult.Error("iterations must be an integer from 1 to 5000");

                    bool runAll = string.IsNullOrWhiteSpace(cases) || string.Equals(cases, "all", StringComparison.OrdinalIgnoreCase);
                    HashSet<string> caseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (runAll)
                    {
                        caseSet.Add("toollist");
                        caseSet.Add("toollookup");
                        caseSet.Add("jsonserialize");
                    }
                    else
                    {
                        foreach (var item in cases.Split(','))
                        {
                            var normalized = item.Trim().ToLowerInvariant();
                            if (string.IsNullOrWhiteSpace(normalized))
                                continue;
                            if (normalized == "toollist")
                                caseSet.Add("toollist");
                            else if (normalized == "toollookup" || normalized == "lookup")
                                caseSet.Add("toollookup");
                            else if (normalized == "jsonserialize")
                                caseSet.Add("jsonserialize");
                            else if (normalized == "all")
                            {
                                caseSet.Add("toollist");
                                caseSet.Add("toollookup");
                                caseSet.Add("jsonserialize");
                            }
                        }
                    }

                    if (caseSet.Count == 0)
                        return CallToolResult.Error("cases must contain one or more of: all, toolList, toolLookup, jsonSerialize");

                    var suiteStarted = DateTimeOffset.UtcNow;
                    var tests = new List<Dictionary<string, object>>();

                    if (caseSet.Contains("toollist"))
                        tests.Add(RunToolListCase(iterations, includeDetails));

                    if (caseSet.Contains("toollookup"))
                        tests.Add(RunToolLookupCase(iterations, targetTool, includeDetails));

                    if (caseSet.Contains("jsonserialize"))
                        tests.Add(RunSerializeCase(iterations, includeDetails));

                    int passed = 0;
                    int failed = 0;
                    long totalElapsedMs = 0;
                    foreach (var test in tests)
                    {
                        bool ok = string.Equals(test["status"]?.ToString(), "passed", StringComparison.OrdinalIgnoreCase);
                        if (ok) passed++; else failed++;
                        if (test.TryGetValue("durationMs", out var value) && value is long ms)
                            totalElapsedMs += ms;
                        else if (test.TryGetValue("durationMs", out var value2) && value2 is int ms2)
                            totalElapsedMs += ms2;
                    }

                    var payload = new Dictionary<string, object>
                    {
                        ["status"] = failed == 0 ? "passed" : "partial",
                        ["ok"] = failed == 0,
                        ["suite"] = "oni_mcp_benchmark",
                        ["suiteStartedAt"] = suiteStarted.ToString("O"),
                        ["suiteEndedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["durationMs"] = totalElapsedMs,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["requested"] = caseSet.Count,
                            ["passed"] = passed,
                            ["failed"] = failed,
                            ["total"] = tests.Count,
                            ["toolCount"] = OniToolRegistry.GetTools().Count
                        },
                        ["results"] = tests
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> RunToolListCase(int iterations, bool includeDetails)
        {
            var timer = Stopwatch.StartNew();
            int baseline = OniToolRegistry.GetTools().Count;
            bool failed = false;
            string error = null;

            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    var info = OniToolRegistry.GetToolInfos(true);
                    if (info == null || info.Count != baseline)
                    {
                        failed = true;
                        error = $"ToolInfos count changed unexpectedly: {info?.Count ?? 0} vs {baseline}";
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                error = ex.Message;
            }
            finally
            {
                timer.Stop();
            }

            var test = new Dictionary<string, object>
            {
                ["name"] = "toolList",
                ["status"] = failed ? "failed" : "passed",
                ["iterations"] = iterations,
                ["durationMs"] = timer.ElapsedMilliseconds,
                ["toolCount"] = baseline
            };

            if (!string.IsNullOrWhiteSpace(error))
                test["error"] = error;

            if (includeDetails)
            {
                test["details"] = new Dictionary<string, object>
                {
                    ["firstTool"] = OniToolRegistry.GetTools().Count > 0 ? OniToolRegistry.GetTools()[0].Name : null,
                    ["lastTool"] = OniToolRegistry.GetTools().Count > 0 ? OniToolRegistry.GetTools()[OniToolRegistry.GetTools().Count - 1].Name : null
                };
            }

            return test;
        }

        private static Dictionary<string, object> RunToolLookupCase(int iterations, string targetTool, bool includeDetails)
        {
            var timer = Stopwatch.StartNew();
            string lookup = targetTool;
            if (string.IsNullOrWhiteSpace(lookup))
                lookup = "world_editor";

            bool failed = false;
            string error = null;
            int foundCount = 0;
            bool missing = false;

            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (!OniToolRegistry.TryGetTool(lookup, out var tool) || tool == null)
                    {
                        failed = true;
                        missing = true;
                        break;
                    }

                    foundCount++;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                error = ex.Message;
            }
            finally
            {
                timer.Stop();
            }

            var test = new Dictionary<string, object>
            {
                ["name"] = "toolLookup",
                ["status"] = failed ? "failed" : "passed",
                ["iterations"] = iterations,
                ["durationMs"] = timer.ElapsedMilliseconds,
                ["tool"] = lookup,
                ["foundCount"] = foundCount
            };

            if (missing)
                test["error"] = $"tool '{lookup}' not found in registry";
            else if (!string.IsNullOrWhiteSpace(error))
                test["error"] = error;

            if (includeDetails)
            {
                var first = OniToolRegistry.GetTools().Find(t => string.Equals(t.Name, lookup, StringComparison.Ordinal));
                test["details"] = new Dictionary<string, object>
                {
                    ["group"] = first?.Group,
                    ["mode"] = first?.Mode,
                    ["risk"] = first?.Risk,
                    ["aliasCount"] = first?.Aliases?.Count ?? 0,
                    ["parameterCount"] = first?.Parameters?.Count ?? 0
                };
            }

            return test;
        }

        private static Dictionary<string, object> RunSerializeCase(int iterations, bool includeDetails)
        {
            var snapshot = new Dictionary<string, object>
            {
                ["version"] = "1.0",
                ["toolCount"] = OniToolRegistry.GetTools().Count,
                ["tools"] = OniToolRegistry.GetTools().ConvertAll(t => t.Name)
            };

            var payload = new Dictionary<string, object>
            {
                ["seed"] = McpJsonUtil.Settings,
                ["snapshot"] = snapshot
            };

            bool failed = false;
            string error = null;
            long bytes = 0;

            var timer = Stopwatch.StartNew();
            try
            {
                for (int i = 0; i < iterations; i++)
                {
                    string text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings);
                    bytes += text?.Length ?? 0;
                    if (string.IsNullOrEmpty(text))
                    {
                        failed = true;
                        error = "serialize produced empty output";
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                error = ex.Message;
            }
            finally
            {
                timer.Stop();
            }

            var test = new Dictionary<string, object>
            {
                ["name"] = "jsonSerialize",
                ["status"] = failed ? "failed" : "passed",
                ["iterations"] = iterations,
                ["durationMs"] = timer.ElapsedMilliseconds,
                ["bytes"] = bytes
            };

            if (!string.IsNullOrWhiteSpace(error))
                test["error"] = error;

            if (includeDetails)
            {
                test["details"] = new Dictionary<string, object>
                {
                    ["avgBytesPerIteration"] = iterations > 0 ? (long)Math.Round(bytes / (double)iterations, 2) : 0,
                    ["payloadKeys"] = new[] { "version", "toolCount", "tools" }
                };
            }

            return test;
        }
    }
}
