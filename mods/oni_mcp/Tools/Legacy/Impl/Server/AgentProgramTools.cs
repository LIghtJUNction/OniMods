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
    public static class AgentProgramTools
    {
        private const string ToolName = "agent_program_execute";
        private const int DefaultMaxSteps = 100;
        private const int HardMaxSteps = 200;
        private const int DefaultMaxLoopIterations = 10;
        private const int HardMaxLoopIterations = 25;
        private const int MaxChildToolCalls = 50;
        private const int MaxStoredResultChars = 4000;

        public static McpTool ExecuteProgram()
        {
            return new McpTool
            {
                Name = ToolName,
                Group = "tools",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "agent_script_run", "agent_flow_execute", "agent_program_run" },
                Tags = new List<string> { "agent", "program", "script", "flow", "if", "while", "loop", "control", "指针", "脚本", "流程" },
                Description = "执行受限 agent 流程 DSL：支持变量、if/while/repeat、break/continue/return，并可按条件调用 MCP 工具控制可视 agent 指针",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["program"] = new McpToolParameter
                    {
                        Type = "object",
                        Description = "程序对象或 JSON 字符串。格式：{vars:{...},steps:[{op:'jump',x:80,y:135},{op:'readCell',saveAs:'cell'},{op:'if',when:{eq:['$cell.element','Water']},then:[...],else:[...]}]}",
                        Required = true
                    },
                    ["dryRun"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "只做结构与工具名验证，不执行任何子调用，默认 false",
                        Required = false
                    },
                    ["maxSteps"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最大执行语句数，防止死循环，默认 100，最大 200",
                        Required = false
                    },
                    ["maxLoopIterations"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "单个 while/repeat 最大迭代次数，默认 10，最大 25",
                        Required = false
                    },
                    ["trace"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否返回详细执行 trace，默认 false；trace 中的子调用结果会被截断",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    JToken program;
                    string error;
                    if (!TryReadProgram(args["program"], out program, out error))
                        return CallToolResult.Error(error);

                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    int maxSteps = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxSteps") ?? DefaultMaxSteps, HardMaxSteps));
                    int maxLoopIterations = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxLoopIterations") ?? DefaultMaxLoopIterations, HardMaxLoopIterations));
                    bool includeTrace = ToolUtil.GetBool(args, "trace", false);

                    var runner = new AgentProgramRunner(dryRun, maxSteps, maxLoopIterations, includeTrace);
                    var payload = runner.Run(program);
                    string text = JsonConvert.SerializeObject(payload, McpJsonUtil.Settings);
                    bool ok = payload.ContainsKey("ok") && (bool)payload["ok"];
                    return ok ? CallToolResult.Text(text) : CallToolResult.Error(text);
                }
            };
        }

        private static bool TryReadProgram(JToken token, out JToken program, out string error)
        {
            program = null;
            error = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                error = "program is required";
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                try
                {
                    program = JToken.Parse(token.ToString());
                    return true;
                }
                catch (Exception ex)
                {
                    error = "program string must be valid JSON: " + ex.Message;
                    return false;
                }
            }

            if (token.Type != JTokenType.Object && token.Type != JTokenType.Array)
            {
                error = "program must be an object, array, or JSON string";
                return false;
            }

            program = token.DeepClone();
            return true;
        }

        private sealed class AgentProgramRunner
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

                if (toolName == "world_cell_info" && (args["x"] == null || args["y"] == null))
                    FillCurrentPointerCell(args);

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

            private void FillCurrentPointerCell(JObject args)
            {
                var pointerArgs = new JObject();
                if (args["agentId"] != null)
                    pointerArgs["agentId"] = args["agentId"].DeepClone();
                pointerArgs["action"] = "get";
                var pointerResult = OniToolRegistry.CallTool("navigation_control", pointerArgs);
                if (pointerResult == null || pointerResult.IsError)
                    throw new AgentProgramException("readCell requires x/y or an aimed pointer");
                var pointerJson = TryParseJson(ResultText(pointerResult)) as JObject;
                if (pointerJson == null || pointerJson["x"] == null || pointerJson["y"] == null)
                    throw new AgentProgramException("readCell could not resolve current pointer x/y");
                args["x"] = pointerJson["x"].DeepClone();
                args["y"] = pointerJson["y"].DeepClone();
                if (args["worldId"] == null && pointerJson["worldId"] != null)
                    args["worldId"] = pointerJson["worldId"].DeepClone();
            }

            private bool TryResolveCallShape(JObject stmt, out string toolName, out JObject args, out string saveAs)
            {
                toolName = null;
                args = new JObject();
                saveAs = stmt["saveAs"]?.ToString() ?? stmt["as"]?.ToString();
                string inferredAction = null;
                string inferredDomain = null;

                string op = Op(stmt);
                if (op == "call")
                {
                    toolName = stmt["tool"]?.ToString() ?? stmt["name"]?.ToString() ?? stmt["call"]?.ToString();
                    if (string.IsNullOrWhiteSpace(toolName))
                        throw new AgentProgramException("call requires tool/name");
                    args = stmt["args"] as JObject ?? stmt["arguments"] as JObject ?? new JObject();
                    return true;
                }

                if (op == "jump" || op == "move")
                {
                    toolName = "navigation_control";
                    inferredAction = "jump";
                }
                else if (op == "nudge")
                {
                    toolName = "navigation_control";
                    inferredAction = "nudge";
                }
                else if (op == "select")
                {
                    toolName = "navigation_control";
                    inferredAction = "select_tool";
                }
                else if (op == "click")
                {
                    toolName = "navigation_control";
                    inferredAction = "left_click";
                }
                else if (op == "drag" || op == "hold")
                {
                    toolName = "navigation_control";
                    inferredAction = "hold_left";
                }
                else if (op == "say")
                {
                    toolName = "navigation_control";
                    inferredAction = "say";
                }
                else if (op == "readpointer")
                {
                    toolName = "navigation_control";
                    inferredAction = "get";
                    if (string.IsNullOrWhiteSpace(saveAs))
                        saveAs = "pointer";
                }
                else if (op == "readmouse")
                {
                    toolName = "navigation_control";
                    inferredAction = "user_mouse";
                    if (string.IsNullOrWhiteSpace(saveAs))
                        saveAs = "mouse";
                }
                else if (op == "readcell")
                {
                    toolName = "read_control";
                    inferredDomain = "world";
                    inferredAction = "cell_info";
                    if (string.IsNullOrWhiteSpace(saveAs))
                        saveAs = "cell";
                }
                else
                {
                    return false;
                }

                args = InlineArguments(stmt);
                if (inferredDomain != null && args["domain"] == null)
                    args["domain"] = inferredDomain;
                if (inferredAction != null && args["action"] == null)
                    args["action"] = inferredAction;
                return true;
            }

            private JObject InlineArguments(JObject stmt)
            {
                var args = new JObject();
                foreach (var property in stmt.Properties())
                {
                    string name = property.Name;
                    if (name == "op" || name == "saveAs" || name == "as" || name == "continueOnError" || name == "comment")
                        continue;
                    args[name] = property.Value.DeepClone();
                }
                return args;
            }

            private JObject ResolveObject(JObject source)
            {
                var result = new JObject();
                if (source == null)
                    return result;
                foreach (var property in source.Properties())
                    result[property.Name] = EvalExpr(property.Value);
                return result;
            }

            private JToken EvalExpr(JToken expr)
            {
                if (expr == null)
                    return JValue.CreateNull();

                if (expr.Type == JTokenType.String)
                {
                    string text = expr.ToString();
                    if (text.StartsWith("$$", StringComparison.Ordinal))
                        return new JValue(text.Substring(1));
                    if (text.StartsWith("$", StringComparison.Ordinal))
                        return ResolvePath(text.Substring(1), required: true).DeepClone();
                    return expr.DeepClone();
                }

                if (expr.Type != JTokenType.Object)
                {
                    if (expr.Type == JTokenType.Array)
                    {
                        var array = new JArray();
                        foreach (var item in (JArray)expr)
                            array.Add(EvalExpr(item));
                        return array;
                    }
                    return expr.DeepClone();
                }

                var obj = (JObject)expr;
                if (obj.Properties().Count() == 1)
                {
                    var property = obj.Properties().First();
                    string op = property.Name;
                    if (IsExpressionOperator(op))
                        return EvalOperator(op, property.Value);
                }

                var literal = new JObject();
                foreach (var property in obj.Properties())
                    literal[property.Name] = EvalExpr(property.Value);
                return literal;
            }

            private JToken EvalOperator(string op, JToken value)
            {
                op = op.ToLowerInvariant();
                if (op == "get" || op == "var")
                    return ResolvePath(value.ToString(), required: true).DeepClone();
                if (op == "exists")
                    return new JValue(ResolvePath(value.ToString(), required: false) != null);
                if (op == "not")
                    return new JValue(!ToBool(EvalExpr(value)));
                if (op == "and")
                {
                    foreach (var item in RequireArray(value, op))
                    {
                        if (!ToBool(EvalExpr(item)))
                            return new JValue(false);
                    }
                    return new JValue(true);
                }
                if (op == "or")
                {
                    foreach (var item in RequireArray(value, op))
                    {
                        if (ToBool(EvalExpr(item)))
                            return new JValue(true);
                    }
                    return new JValue(false);
                }
                if (op == "eq" || op == "ne" || op == "lt" || op == "lte" || op == "gt" || op == "gte")
                    return new JValue(Compare(op, RequireArray(value, op)));
                if (op == "add" || op == "sub" || op == "mul" || op == "div" || op == "mod")
                    return EvalMath(op, RequireArray(value, op));
                if (op == "contains")
                {
                    var args = RequireArray(value, op);
                    if (args.Count != 2)
                        throw new AgentProgramException("contains requires 2 arguments");
                    string haystack = ToScalarString(EvalExpr(args[0]));
                    string needle = ToScalarString(EvalExpr(args[1]));
                    return new JValue(haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                throw new AgentProgramException("unknown expression operator: " + op);
            }

            private bool Compare(string op, JArray args)
            {
                if (args.Count != 2)
                    throw new AgentProgramException(op + " requires 2 arguments");
                var left = EvalExpr(args[0]);
                var right = EvalExpr(args[1]);
                if (op == "eq")
                    return ValuesEqual(left, right);
                if (op == "ne")
                    return !ValuesEqual(left, right);

                double l;
                double r;
                if (!TryNumber(left, out l) || !TryNumber(right, out r))
                    throw new AgentProgramException(op + " requires numeric arguments");
                if (op == "lt") return l < r;
                if (op == "lte") return l <= r;
                if (op == "gt") return l > r;
                return l >= r;
            }

            private JToken EvalMath(string op, JArray args)
            {
                if (args.Count == 0)
                    throw new AgentProgramException(op + " requires at least one argument");
                double result = ToDouble(EvalExpr(args[0]));
                for (int i = 1; i < args.Count; i++)
                {
                    double next = ToDouble(EvalExpr(args[i]));
                    if (op == "add") result += next;
                    else if (op == "sub") result -= next;
                    else if (op == "mul") result *= next;
                    else if (op == "div") result /= next;
                    else if (op == "mod") result %= next;
                }
                if (Math.Abs(result - Math.Round(result)) < 0.000001 && result <= int.MaxValue && result >= int.MinValue)
                    return new JValue((int)Math.Round(result));
                return new JValue(result);
            }

            private JToken ResolvePath(string path, bool required)
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new AgentProgramException("empty variable path");
                string[] parts = path.Split('.');
                JToken token;
                int index = 0;

                if (parts[0] == "vars")
                {
                    token = JObject.FromObject(vars);
                    index = 1;
                }
                else if (parts[0] == "last")
                {
                    token = last;
                    index = 1;
                }
                else if (vars.TryGetValue(parts[0], out token))
                {
                    index = 1;
                }
                else
                {
                    if (required)
                        throw new AgentProgramException("unknown variable: " + parts[0]);
                    return null;
                }

                for (int i = index; i < parts.Length; i++)
                {
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        if (required)
                            throw new AgentProgramException("path not found: " + path);
                        return null;
                    }

                    var obj = token as JObject;
                    if (obj != null)
                    {
                        token = obj[parts[i]];
                        continue;
                    }

                    var array = token as JArray;
                    int arrayIndex;
                    if (array != null && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out arrayIndex))
                    {
                        token = arrayIndex >= 0 && arrayIndex < array.Count ? array[arrayIndex] : null;
                        continue;
                    }

                    if (required)
                        throw new AgentProgramException("path not found: " + path);
                    return null;
                }

                if (token == null && required)
                    throw new AgentProgramException("path not found: " + path);
                return token;
            }

            private static bool IsExpressionOperator(string op)
            {
                op = (op ?? "").ToLowerInvariant();
                return op == "get" || op == "var" || op == "exists" || op == "eq" || op == "ne"
                    || op == "lt" || op == "lte" || op == "gt" || op == "gte"
                    || op == "and" || op == "or" || op == "not"
                    || op == "add" || op == "sub" || op == "mul" || op == "div" || op == "mod"
                    || op == "contains";
            }

            private static JArray RequireArray(JToken value, string op)
            {
                var array = value as JArray;
                if (array == null)
                    throw new AgentProgramException(op + " requires an array");
                return array;
            }

            private static bool ValuesEqual(JToken left, JToken right)
            {
                double l;
                double r;
                if (TryNumber(left, out l) && TryNumber(right, out r))
                    return Math.Abs(l - r) < 0.000001;
                if (left != null && right != null
                    && (left.Type == JTokenType.Boolean || right.Type == JTokenType.Boolean))
                    return ToBool(left) == ToBool(right);
                return string.Equals(ToScalarString(left), ToScalarString(right), StringComparison.OrdinalIgnoreCase);
            }

            private static bool TryNumber(JToken token, out double value)
            {
                value = 0;
                if (token == null || token.Type == JTokenType.Null)
                    return false;
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                    return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }

            private static double ToDouble(JToken token)
            {
                double value;
                if (!TryNumber(token, out value))
                    throw new AgentProgramException("numeric value required, got " + ToScalarString(token));
                return value;
            }

            private static int ToInt(JToken token)
            {
                double value = ToDouble(token);
                return (int)Math.Round(value);
            }

            private static bool ToBool(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return false;
                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();
                if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                    return Math.Abs(ToDouble(token)) > 0.000001;
                bool parsed;
                if (bool.TryParse(token.ToString(), out parsed))
                    return parsed;
                return !string.IsNullOrWhiteSpace(token.ToString());
            }

            private static string ToScalarString(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return "";
                var value = token as JValue;
                return value != null ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) : token.ToString(Formatting.None);
            }

            private static object ToPlain(JToken token)
            {
                if (token == null || token.Type == JTokenType.Null)
                    return null;
                return token.ToObject<object>();
            }

            private static string ResultText(CallToolResult result)
            {
                if (result == null || result.Content == null || result.Content.Count == 0 || result.Content[0] == null)
                    return "";
                return result.Content[0].Text ?? "";
            }

private static bool IsBlockedChildTool(string toolName, JObject arguments)
            {
return IsProgramCall(toolName, arguments)
                    || string.Equals(toolName, "agent_script_run", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toolName, "agent_flow_execute", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toolName, "agent_program_run", StringComparison.OrdinalIgnoreCase)
|| string.Equals(toolName, ToolBatchTools.ToolName, StringComparison.OrdinalIgnoreCase)
|| ServerTools.IsServerControlDomainCall(toolName, arguments, "batch", "call_many", "many");
            }

            private static string Truncate(string text, int maxChars)
            {
                if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
                    return text ?? "";
                return text.Substring(0, maxChars) + "...[truncated]";
            }

            private static JToken TryParseJson(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                try
                {
                    return JToken.Parse(text);
                }
                catch
                {
                    return null;
                }
            }

            private void Trace(string path, string op, bool ok, Dictionary<string, object> detail)
            {
                if (!includeTrace)
                    return;
                var row = new Dictionary<string, object>
                {
                    ["step"] = executedSteps,
                    ["path"] = path,
                    ["op"] = op,
                    ["ok"] = ok
                };
                if (detail != null)
                {
                    foreach (var pair in detail)
                        row[pair.Key] = pair.Value;
                }
                trace.Add(row);
            }

            private static string Op(JObject stmt)
            {
                string op = stmt["op"]?.ToString();
                if (!string.IsNullOrWhiteSpace(op))
                    return op.Trim().ToLowerInvariant();
                if (stmt["call"] != null) return "call";
                if (stmt["if"] != null) return "if";
                if (stmt["while"] != null) return "while";
                if (stmt["repeat"] != null) return "repeat";
                if (stmt["set"] != null) return "set";
                if (stmt["return"] != null) return "return";
                if (stmt["break"] != null) return "break";
                if (stmt["continue"] != null) return "continue";
                if (stmt["comment"] != null || !stmt.Properties().Any()) return "comment";
                return "unknown";
            }

            private static JToken ConditionToken(JObject stmt)
            {
                return stmt["when"] ?? stmt["condition"] ?? stmt["if"] ?? stmt["while"];
            }

            private static JArray ThenBlock(JObject stmt)
            {
                return stmt["then"] as JArray ?? stmt["do"] as JArray ?? new JArray();
            }

            private static JArray ElseBlock(JObject stmt)
            {
                return stmt["else"] as JArray;
            }

            private static JArray DoBlock(JObject stmt)
            {
                var block = stmt["do"] as JArray ?? stmt["steps"] as JArray;
                if (block == null)
                    throw new AgentProgramException("loop requires do/steps array");
                return block;
            }
        }

        private enum FlowSignal
        {
            None,
            Break,
            Continue,
            Return
        }

        private sealed class AgentProgramException : Exception
        {
            public AgentProgramException(string message) : base(message)
            {
            }
        }
    }
}
