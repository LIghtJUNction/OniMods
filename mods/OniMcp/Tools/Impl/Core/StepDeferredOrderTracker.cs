using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Captures workflow handles once. A new blueprint created after digging still
    // belongs to its original workflow, so native-dig completion cannot end the wait early.
    internal sealed class StepDeferredOrderTracker
    {
        private readonly List<KeyValuePair<string, Func<string>>> _handles;

        internal StepDeferredOrderTracker(IEnumerable<KeyValuePair<string, Func<string>>> handles)
        {
            _handles = (handles ?? Enumerable.Empty<KeyValuePair<string, Func<string>>>()).ToList();
        }

        internal int InitialCount => _handles.Count;
        internal int RemainingCount => _handles.Count(handle => IsPending(Read(handle)));

        internal Dictionary<string, object> ToJson()
        {
            var states = _handles.Select(Read).ToList();
            return new Dictionary<string, object>
            {
                ["initial"] = InitialCount,
                ["remaining"] = states.Count(IsPending),
                ["states"] = states.GroupBy(state => state).ToDictionary(group => group.Key, group => group.Count())
            };
        }

        private static bool IsPending(string state) =>
            state == "unknown" || BuildPlanningDeferredBuildPolicy.IsUnfinished(state);

        private static string Read(KeyValuePair<string, Func<string>> handle)
        {
            try { return handle.Value?.Invoke() ?? "unknown"; }
            catch (Exception) { return "unknown"; }
        }
    }
}
