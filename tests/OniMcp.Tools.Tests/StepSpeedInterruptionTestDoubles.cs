using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// The speed handlers, interruption path and complete watcher lifecycle are production
// code. Only native observations, Unity scheduling and transport are host fixtures.
namespace OniMcp.Tools
{
    internal sealed class StepSpeedTestHandle
    {
        internal Action Tick;
        internal Action Destroy;
        internal int SpeedAtCompletion;
        internal readonly List<Dictionary<string, object>> Completions = new List<Dictionary<string, object>>();
    }

    public static partial class GameControlTools
    {
        private static readonly object StepTestWorld = new object();

        internal static void ResetSpeedWaitForTest()
        {
            _activeWatcher = null;
            _mcpPauseGame = null;
            _mcpPauseScreen = null;
        }

        internal static StepSpeedTestHandle StartSpeedWaitForTest(float seconds = 1f)
        {
            if (!StepCondition.TryParse(JObject.Parse("{condition:'orders_complete'}"), out var condition, out string error))
                throw new InvalidOperationException(error);
            var handle = new StepSpeedTestHandle();
            var watcher = new StepUntilWatcher();
            _activeWatcher = watcher;
            watcher.Initialize("host-wait", new StepUntilLoadIdentity(Game.Instance, StepTestWorld, StepTestWorld),
                condition, "orders_complete", 1, 600f, seconds, 0, new HashSet<int>(), result =>
                {
                    handle.Completions.Add(result);
                    handle.SpeedAtCompletion = SpeedControlScreen.Instance.GetSpeed();
                    if (ReferenceEquals(_activeWatcher, watcher)) _activeWatcher = null;
                });
            handle.Tick = watcher.TestUpdate;
            handle.Destroy = watcher.TestDestroy;
            return handle;
        }

        private static object ResolveStepRootWorld() => StepTestWorld;
        private static object ResolveStepWorld(int worldId) => StepTestWorld;
        private static List<StepDupeFact> ReadStepDupeFacts(int worldId) => new List<StepDupeFact>();
        private static Dictionary<string, object> StepDupeFactsJson(List<StepDupeFact> facts) => new Dictionary<string, object>();

        private sealed class StepOrderTracker
        {
            internal StepOrderTracker(int worldId, HashSet<int> cells) { }
            internal int InitialCount => 0;
            internal int RemainingCount => 0;
            internal Dictionary<string, object> ToJson() => new Dictionary<string, object>();
        }

        private sealed partial class StepUntilWatcher
        {
            internal void TestUpdate() => Update();
            internal void TestDestroy() => OnDestroy();
            private string ReadCriticalThreat() => null;
            private static List<object> GetAllNotifications() => new List<object>();
            private static string GetNotificationKey(object notification) => string.Empty;

            private void ScanNotificationsAndAlarms(out string threat, out string alarm,
                out List<string> alarms, out List<string> alerts, out List<StepNotificationFact> notes, out int newCount)
            {
                threat = alarm = null;
                alarms = new List<string>();
                alerts = new List<string>();
                notes = new List<StepNotificationFact>();
                newCount = 0;
            }
        }
    }
}

namespace UnityEngine
{
    internal class MonoBehaviour
    {
        protected static void Destroy(object component) { }
    }
}

namespace OniMcp.Server
{
    internal sealed class McpHttpServer
    {
        internal static McpHttpServer Instance => null;
        internal void EnqueueClientNotification(string level, string logger, Dictionary<string, object> data) { }
    }
}

internal sealed class Research
{
    internal static Research Instance => null;
    internal TechInstance GetActiveResearch() => null;
}
