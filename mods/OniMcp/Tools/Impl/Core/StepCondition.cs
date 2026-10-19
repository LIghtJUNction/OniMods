using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    // Pure condition definitions and evaluation: no Unity reads or speed changes.
    internal sealed class StepCondition
    {
        internal bool IsLegacy;
        internal string Fact;
        internal string Quantifier = "any";
        internal string Schedule = "work";
        internal string Contains;
        internal string Type;
        internal double StableSeconds;
        internal string Combination;
        internal List<StepCondition> Children;

        internal static bool TryParse(JObject args, out StepCondition condition, out string error)
        {
            condition = null;
            error = null;
            try
            {
                JToken input = args?["until"];
                if (input != null)
                {
                    if (args["condition"] != null)
                        throw new ArgumentException("Use either until or legacy condition, not both.");
                }
                else
                {
                    JToken legacy = args?["condition"];
                    if (legacy != null && legacy.Type != JTokenType.String)
                        throw new ArgumentException("condition must be a string; use until for expressions.");
                    input = FromLegacy((string)legacy);
                }
                int nodes = 0;
                condition = Parse(input, 1, ref nodes);
                condition.IsLegacy = args?["until"] == null;
                return true;
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return false;
            }
        }

        private static StepCondition Parse(JToken input, int depth, ref int nodes)
        {
            if (depth > 4 || ++nodes > 32)
                throw new ArgumentException("until is limited to depth 4 and 32 nodes.");
            var obj = input as JObject;
            if (obj == null) throw new ArgumentException("Each until node must be an object.");
            var node = new StepCondition();
            if (obj["all"] != null || obj["any"] != null)
            {
                if (obj.Count != 1) throw new ArgumentException("A combination must contain only all or any.");
                node.Combination = obj["all"] != null ? "all" : "any";
                var children = obj[node.Combination] as JArray;
                if (children == null || children.Count == 0)
                    throw new ArgumentException("all/any must contain a nonempty array of conditions.");
                node.Children = new List<StepCondition>();
                foreach (var child in children) node.Children.Add(Parse(child, depth + 1, ref nodes));
                return node;
            }

            node.Fact = StringValue(obj, "fact", true);
            var allowed = new HashSet<string> { "fact", "stableSeconds" };
            switch (node.Fact)
            {
                case "dupes_idle":
                    allowed.UnionWith(new[] { "quantifier", "schedule" });
                    node.Quantifier = StringValue(obj, "quantifier") ?? "any";
                    node.Schedule = StringValue(obj, "schedule") ?? "work";
                    if (node.Quantifier != "any" && node.Quantifier != "all")
                        throw new ArgumentException("dupes_idle quantifier must be any or all.");
                    if (node.Schedule != "work" && node.Schedule != "any")
                        throw new ArgumentException("dupes_idle schedule must be work or any.");
                    node.StableSeconds = 2;
                    break;
                case "notification":
                    allowed.UnionWith(new[] { "contains", "type" });
                    node.Contains = StringValue(obj, "contains");
                    node.Type = StringValue(obj, "type");
                    if (node.Type != null && !new[] { "Good", "Bad", "BadMinor", "Messages", "Event" }.Contains(node.Type))
                        throw new ArgumentException("notification type must be Good, Bad, BadMinor, Messages, or Event.");
                    break;
                case "alarm":
                    allowed.Add("contains");
                    node.Contains = StringValue(obj, "contains");
                    break;
                case "orders_complete": case "cycle_end": case "alert": case "new_notification":
                case "printing_pod_ready": case "research_complete":
                    break;
                default: throw new ArgumentException("Unknown until fact: " + node.Fact);
            }
            if (obj.Properties().Any(p => !allowed.Contains(p.Name)))
                throw new ArgumentException("Unknown property for until fact " + node.Fact + ".");
            if (obj["stableSeconds"] != null)
            {
                var value = obj["stableSeconds"];
                if ((value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                    || !double.TryParse(value.ToString(Newtonsoft.Json.Formatting.None),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out node.StableSeconds)
                    || double.IsNaN(node.StableSeconds) || double.IsInfinity(node.StableSeconds)
                    || node.StableSeconds < 0 || node.StableSeconds > 30)
                    throw new ArgumentException("stableSeconds must be a finite number from 0 to 30 game seconds.");
            }
            return node;
        }

        private static string StringValue(JObject obj, string key, bool required = false)
        {
            if (obj[key] == null && !required) return null;
            if (obj[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)obj[key]))
                throw new ArgumentException(key + " must be a nonempty string.");
            string value = ((string)obj[key]).Trim();
            if (value.Length > 128) throw new ArgumentException(key + " is limited to 128 characters.");
            return value;
        }

        private static JObject Atom(string fact) => new JObject { ["fact"] = fact };

        private static JObject FromLegacy(string value)
        {
            string s = (value ?? "orders_complete").Trim().ToLowerInvariant();
            switch (s)
            {
                case "": case "orders_complete": return Atom("orders_complete");
                case "dupe_idle": case "idle": return Atom("dupes_idle");
                case "all_dupes_idle": return new JObject { ["fact"] = "dupes_idle", ["quantifier"] = "all" };
                case "cycle_end": case "cycle": case "night": return Atom("cycle_end");
                case "any": return new JObject { ["any"] = new JArray(Atom("orders_complete"), Atom("dupes_idle"), Atom("cycle_end")) };
                case "notification": case "notify": case "new_notification": return Atom("new_notification");
                case "alarm": case "automation_alarm": case "auto_notify": return Atom("alarm");
                case "alert": case "threat": return Atom("alert");
                case "print": case "printing_pod": case "printable": case "immigrant": return Atom("printing_pod_ready");
                case "research": case "research_complete": return Atom("research_complete");
                case "notify:good": case "good": return new JObject { ["fact"] = "notification", ["type"] = "Good" };
                case "notify:bad": case "bad": case "notify:problem":
                    return new JObject { ["any"] = new JArray(new JObject { ["fact"] = "notification", ["type"] = "Bad" }, new JObject { ["fact"] = "notification", ["type"] = "BadMinor" }) };
                case "notify:message": case "message": case "messages": return new JObject { ["fact"] = "notification", ["type"] = "Messages" };
                case "notify:event": case "event": return new JObject { ["fact"] = "notification", ["type"] = "Event" };
                default:
                    if (s.StartsWith("alarm:", StringComparison.Ordinal))
                        return new JObject { ["fact"] = "alarm", ["contains"] = s.Substring("alarm:".Length).Trim() };
                    foreach (string prefix in new[] { "notify:", "notification:", "alert:" })
                    {
                        if (!s.StartsWith(prefix, StringComparison.Ordinal)) continue;
                        s = s.Substring(prefix.Length).Trim();
                        break;
                    }
                    // Legacy keyword searches prioritize active alarms, then notification title/notifier.
                    return new JObject { ["any"] = new JArray(
                        new JObject { ["fact"] = "alarm", ["contains"] = s },
                        new JObject { ["fact"] = "notification", ["contains"] = s }) };
            }
        }

        internal JObject ToJson()
        {
            if (Children != null) return new JObject { [Combination] = new JArray(Children.Select(c => c.ToJson())) };
            var result = Atom(Fact);
            if (Fact == "dupes_idle") { result["quantifier"] = Quantifier; result["schedule"] = Schedule; }
            if (Contains != null) result["contains"] = Contains;
            if (Type != null) result["type"] = Type;
            result["stableSeconds"] = StableSeconds;
            return result;
        }
    }

    internal sealed class StepConditionEvaluator
    {
        private readonly Dictionary<StepCondition, double> _since = new Dictionary<StepCondition, double>();

        internal bool Evaluate(StepCondition node, StepConditionFacts facts, double gameSeconds, out string reason)
        {
            reason = null;
            if (node.Children != null)
            {
                // Evaluate every child on every observation so stability resets even behind a false sibling.
                bool matched = node.Combination == "all";
                foreach (var child in node.Children)
                {
                    bool childMatch = Evaluate(child, facts, gameSeconds, out string childReason);
                    matched = node.Combination == "all" ? matched && childMatch : matched || childMatch;
                    if (childMatch && reason == null) reason = childReason;
                }
                if (!matched) { reason = null; return false; }
                if (!node.IsLegacy) reason = "condition:" + node.Combination;
                return true;
            }
            bool value = Match(node, facts, out reason);
            if (!value) { _since.Remove(node); reason = null; return false; }
            if (!_since.TryGetValue(node, out double since)) _since[node] = since = gameSeconds;
            if (gameSeconds - since < node.StableSeconds) { reason = null; return false; }
            return true;
        }

        private static bool Match(StepCondition node, StepConditionFacts f, out string reason)
        {
            reason = node.Fact;
            switch (node.Fact)
            {
                case "orders_complete": return f.InitialTrackedOrders > 0 && f.RemainingTrackedOrders == 0;
                case "cycle_end": return f.CycleEnded;
                case "dupes_idle":
                    var matches = f.Duplicants.Where(d => d.Idle && (node.Schedule == "any" || d.WorkScheduled)).ToList();
                    if (matches.Count == 0 || (node.Quantifier == "all" && matches.Count != f.Duplicants.Count)) return false;
                    reason = node.Quantifier == "all" ? "all_dupes_idle" : "dupe_idle:" + matches[0].Name;
                    return true;
                case "alert": if (f.Alerts.Count == 0) return false; reason = "alert:" + f.Alerts[0]; return true;
                case "alarm":
                    string alarm = f.Alarms.FirstOrDefault(a => Includes(a, node.Contains));
                    if (alarm == null) return false; reason = "alarm:" + alarm; return true;
                case "new_notification":
                    var fresh = f.Notifications.FirstOrDefault(n => n.IsNew);
                    if (fresh == null) return false; reason = "notification:" + fresh.Title; return true;
                case "notification":
                    var note = f.Notifications.FirstOrDefault(n => (node.Type == null || n.Type == node.Type)
                        && (Includes(n.Title, node.Contains) || Includes(n.Notifier, node.Contains)));
                    if (note == null) return false; reason = "notification:" + note.Title; return true;
                case "printing_pod_ready": reason = "notification:printing_pod_ready"; return f.PrintingPodReady;
                case "research_complete": reason = "notification:research_complete"; return f.ResearchComplete;
                default: return false;
            }
        }

        private static bool Includes(string value, string needle) => needle == null
            || (value != null && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    internal sealed class StepConditionFacts
    {
        internal int InitialTrackedOrders;
        internal int RemainingTrackedOrders;
        internal bool CycleEnded { get; set; }
        internal bool PrintingPodReady { get; set; }
        internal bool ResearchComplete { get; set; }
        internal List<StepDupeFact> Duplicants = new List<StepDupeFact>();
        internal List<string> Alarms = new List<string>();
        internal List<string> Alerts = new List<string>();
        internal List<StepNotificationFact> Notifications = new List<StepNotificationFact>();
    }

    internal sealed class StepDupeFact
    {
        public string Name;
        public bool Idle;
        public bool WorkScheduled;
    }

    internal sealed class StepNotificationFact
    {
        public string Title;
        public string Type;
        public bool IsNew;
        public bool IsAlarm { get; set; }
        public string Notifier;
    }
}
