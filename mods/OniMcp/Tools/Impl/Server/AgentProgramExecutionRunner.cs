using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class AgentProgramTools
    {
        private sealed partial class AgentProgramRunner
        {
            private readonly bool dryRun;
            private readonly int maxSteps;
            private readonly int maxLoopIterations;
            private readonly bool includeTrace;
            private readonly Dictionary<string, JToken> vars = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            private readonly List<Dictionary<string, object>> trace = new List<Dictionary<string, object>>();
            private readonly List<string> warnings = new List<string>();
            private readonly HashSet<string> referencedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private JToken last = JValue.CreateNull();
            private int executedSteps;
            private int childToolCalls;
            private bool returned;
            private JToken returnValue = JValue.CreateNull();

            public AgentProgramRunner(bool dryRun, int maxSteps, int maxLoopIterations, bool includeTrace)
            {
                this.dryRun = dryRun;
                this.maxSteps = maxSteps;
                this.maxLoopIterations = maxLoopIterations;
                this.includeTrace = includeTrace;
            }

            public Dictionary<string, object> Run(JToken program)
            {
                try
                {
                    JArray steps = ReadProgram(program);
                    ValidateBlock(steps, "steps");

                    if (!dryRun)
                    {
                        var signal = ExecuteBlock(steps, "steps");
                        if (signal == FlowSignal.Break || signal == FlowSignal.Continue)
                            throw new AgentProgramException(signal == FlowSignal.Break ? "break outside loop" : "continue outside loop");
                    }

                    return Payload(ok: true, valid: true, error: null);
                }
                catch (AgentProgramException ex)
                {
                    return Payload(ok: false, valid: false, error: ex.Message);
                }
                catch (Exception ex)
                {
                    return Payload(ok: false, valid: false, error: "agent program error: " + ex.Message);
                }
            }

            private Dictionary<string, object> Payload(bool ok, bool valid, string error)
            {
                var payload = new Dictionary<string, object>
                {
                    ["ok"] = ok,
                    ["dryRun"] = dryRun,
                    ["valid"] = valid,
                    ["executedSteps"] = dryRun ? 0 : executedSteps,
                    ["childToolCalls"] = dryRun ? 0 : childToolCalls,
                    ["maxChildToolCalls"] = MaxChildToolCalls,
                    ["maxSteps"] = maxSteps,
                    ["maxLoopIterations"] = maxLoopIterations,
                    ["referencedTools"] = referencedTools.OrderBy(name => name).ToList(),
                    ["warnings"] = warnings,
                    ["returned"] = returned,
                    ["returnValue"] = ToPlain(returnValue),
                    ["last"] = ToPlain(last),
                    ["vars"] = VarsToPlain()
                };
                if (!string.IsNullOrEmpty(error))
                    payload["error"] = error;
                if (includeTrace)
                    payload["trace"] = trace;
                return payload;
            }

            private Dictionary<string, object> VarsToPlain()
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in vars.OrderBy(pair => pair.Key))
                    result[pair.Key] = ToPlain(pair.Value);
                return result;
            }

            private JArray ReadProgram(JToken program)
            {
                if (program.Type == JTokenType.Array)
                    return (JArray)program;

                var obj = program as JObject;
                if (obj == null)
                    throw new AgentProgramException("program must be an object or array");

                var initialVars = obj["vars"] as JObject;
                if (initialVars != null)
                {
                    foreach (var property in initialVars.Properties())
                        vars[property.Name] = property.Value.DeepClone();
                }

                var steps = obj["steps"] as JArray ?? obj["do"] as JArray;
                if (steps == null)
                    throw new AgentProgramException("program.steps array is required");
                return steps;
            }

            private void ValidateBlock(JArray block, string path)
            {
                if (block == null)
                    throw new AgentProgramException(path + " must be an array");
                for (int i = 0; i < block.Count; i++)
                {
                    var stmt = block[i] as JObject;
                    if (stmt == null)
                        throw new AgentProgramException(path + "[" + i + "] must be an object");
                    ValidateStatement(stmt, path + "[" + i + "]");
                }
            }

            private void ValidateStatement(JObject stmt, string path)
            {
                string op = Op(stmt);
                if (op == "comment")
                    return;
                if (op == "set" || op == "break" || op == "continue" || op == "return")
                    return;

                if (op == "if")
                {
                    if (ConditionToken(stmt) == null)
                        throw new AgentProgramException(path + " if requires when/condition/if");
                    ValidateBlock(ThenBlock(stmt), path + ".then");
                    var elseBlock = ElseBlock(stmt);
                    if (elseBlock != null)
                        ValidateBlock(elseBlock, path + ".else");
                    return;
                }

                if (op == "while")
                {
                    if (ConditionToken(stmt) == null)
                        throw new AgentProgramException(path + " while requires when/condition/while");
                    ValidateBlock(DoBlock(stmt), path + ".do");
                    return;
                }

                if (op == "repeat")
                {
                    if (stmt["count"] == null && stmt["repeat"] == null)
                        throw new AgentProgramException(path + " repeat requires count");
                    ValidateBlock(DoBlock(stmt), path + ".do");
                    return;
                }

                string toolName;
                JObject ignored;
                string ignoredSave;
                if (TryResolveCallShape(stmt, out toolName, out ignored, out ignoredSave))
                {
                    referencedTools.Add(toolName);
                    if (IsBlockedChildTool(toolName, ignored))
                        throw new AgentProgramException(path + " cannot reference nested program or batch tool: " + toolName);
                    McpTool tool;
                    if (!OniToolRegistry.TryGetTool(toolName, out tool))
                        throw new AgentProgramException(path + " references unknown tool: " + toolName);
                    return;
                }

                throw new AgentProgramException(path + " unsupported op: " + op);
            }

            private FlowSignal ExecuteBlock(JArray block, string path)
            {
                for (int i = 0; i < block.Count; i++)
                {
                    if (returned)
                        return FlowSignal.Return;
                    var signal = ExecuteStatement((JObject)block[i], path + "[" + i + "]");
                    if (signal != FlowSignal.None)
                        return signal;
                }
                return FlowSignal.None;
            }

            private FlowSignal ExecuteStatement(JObject stmt, string path)
            {
                if (executedSteps >= maxSteps)
                    throw new AgentProgramException("maxSteps exceeded at " + path);
                executedSteps++;

                string op = Op(stmt);
                if (op == "comment")
                    return FlowSignal.None;
                if (op == "set")
                {
                    ExecuteSet(stmt, path);
                    return FlowSignal.None;
                }
                if (op == "break")
                {
                    Trace(path, "break", true, null);
                    return FlowSignal.Break;
                }
                if (op == "continue")
                {
                    Trace(path, "continue", true, null);
                    return FlowSignal.Continue;
                }
                if (op == "return")
                {
                    returned = true;
                    returnValue = EvalExpr(stmt["value"] ?? stmt["return"] ?? JValue.CreateNull());
                    Trace(path, "return", true, new Dictionary<string, object> { ["value"] = ToPlain(returnValue) });
                    return FlowSignal.Return;
                }
                if (op == "if")
                    return ExecuteIf(stmt, path);
                if (op == "while")
                    return ExecuteWhile(stmt, path);
                if (op == "repeat")
                    return ExecuteRepeat(stmt, path);

                string toolName;
                JObject callArgs;
                string saveAs;
                if (TryResolveCallShape(stmt, out toolName, out callArgs, out saveAs))
                {
                    ExecuteCall(toolName, callArgs, saveAs, ToolUtil.GetBool(stmt, "continueOnError", false), path);
                    return FlowSignal.None;
                }

                throw new AgentProgramException(path + " unsupported op: " + op);
            }

            private void ExecuteSet(JObject stmt, string path)
            {
                var assignments = stmt["vars"] as JObject ?? stmt["set"] as JObject;
                if (assignments == null && stmt["name"] != null)
                {
                    assignments = new JObject { [stmt["name"].ToString()] = stmt["value"] ?? JValue.CreateNull() };
                }
                if (assignments == null)
                    throw new AgentProgramException(path + " set requires vars object or name/value");

                var changed = new Dictionary<string, object>();
                foreach (var property in assignments.Properties())
                {
                    var value = EvalExpr(property.Value);
                    vars[property.Name] = value;
                    changed[property.Name] = ToPlain(value);
                }
                Trace(path, "set", true, new Dictionary<string, object> { ["vars"] = changed });
            }

            private FlowSignal ExecuteIf(JObject stmt, string path)
            {
                bool condition = ToBool(EvalExpr(ConditionToken(stmt)));
                Trace(path, "if", true, new Dictionary<string, object> { ["condition"] = condition });
                var selected = condition ? ThenBlock(stmt) : ElseBlock(stmt);
                if (selected == null)
                    return FlowSignal.None;
                return ExecuteBlock(selected, condition ? path + ".then" : path + ".else");
            }

            private FlowSignal ExecuteWhile(JObject stmt, string path)
            {
                int iterations = 0;
                while (ToBool(EvalExpr(ConditionToken(stmt))))
                {
                    if (iterations >= maxLoopIterations)
                        throw new AgentProgramException("maxLoopIterations exceeded at " + path);
                    Trace(path, "while", true, new Dictionary<string, object> { ["iteration"] = iterations });
                    var signal = ExecuteBlock(DoBlock(stmt), path + ".do[" + iterations + "]");
                    if (signal == FlowSignal.Break)
                        return FlowSignal.None;
                    if (signal == FlowSignal.Return)
                        return signal;
                    iterations++;
                }
                Trace(path, "while_done", true, new Dictionary<string, object> { ["iterations"] = iterations });
                return FlowSignal.None;
            }

            private FlowSignal ExecuteRepeat(JObject stmt, string path)
            {
                int count = ToInt(EvalExpr(stmt["count"] ?? stmt["repeat"]));
                if (count < 0)
                    throw new AgentProgramException(path + " repeat count cannot be negative");
                if (count > maxLoopIterations)
                    throw new AgentProgramException(path + " repeat count exceeds maxLoopIterations");

                string indexName = stmt["as"]?.ToString();
                for (int i = 0; i < count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(indexName))
                        vars[indexName] = new JValue(i);
                    Trace(path, "repeat", true, new Dictionary<string, object> { ["iteration"] = i, ["count"] = count });
                    var signal = ExecuteBlock(DoBlock(stmt), path + ".do[" + i + "]");
                    if (signal == FlowSignal.Break)
                        return FlowSignal.None;
                    if (signal == FlowSignal.Return)
                        return signal;
                }
                return FlowSignal.None;
            }

            private void ExecuteCall(string toolName, JObject rawArgs, string saveAs, bool continueOnError, string path)
            {
                referencedTools.Add(toolName);
                JObject args = ResolveObject(rawArgs);
                if (IsBlockedChildTool(toolName, args))
                    throw new AgentProgramException("agent program cannot call nested program or batch tools: " + toolName);
                if (childToolCalls >= MaxChildToolCalls)
                    throw new AgentProgramException("maxChildToolCalls exceeded at " + path);
                childToolCalls++;

                var result = OniToolRegistry.CallTool(toolName, args);
                string text = Truncate(ResultText(result), MaxStoredResultChars);
                JToken json = TryParseJson(text);
                var wrapped = new JObject
                {
                    ["ok"] = result != null && !result.IsError,
                    ["isError"] = result == null || result.IsError,
                    ["tool"] = toolName,
                    ["args"] = args,
                    ["json"] = json ?? JValue.CreateNull(),
                    ["text"] = json == null ? (JToken)new JValue(text) : JValue.CreateNull()
                };
                last = wrapped;
                vars["last"] = wrapped;

                if (!string.IsNullOrWhiteSpace(saveAs))
                    vars[saveAs] = json ?? new JValue(text);

                Trace(path, "call", result != null && !result.IsError, new Dictionary<string, object>
                {
                    ["tool"] = toolName,
                    ["saveAs"] = string.IsNullOrWhiteSpace(saveAs) ? null : saveAs,
                    ["isError"] = result == null || result.IsError,
                    ["result"] = ToPlain(json ?? new JValue(text))
                });

                if ((result == null || result.IsError) && !continueOnError)
                    throw new AgentProgramException("tool call failed at " + path + ": " + toolName + " -> " + text);
            }

            private static bool IsProgramCall(string toolName, JObject arguments)
            {
                McpTool tool;
                if (OniToolRegistry.TryGetTool(toolName, out tool)
                    && string.Equals(tool.Name, ToolName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return ServerTools.IsServerControlDomainCall(toolName, arguments, "program", "agent_program", "flow", "script");
            }

        }
    }
}
