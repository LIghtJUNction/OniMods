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
    }
}
