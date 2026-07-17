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

        }
    }
}
