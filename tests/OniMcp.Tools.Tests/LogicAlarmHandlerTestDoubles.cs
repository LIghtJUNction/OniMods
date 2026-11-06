using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

// The production alarm handler, update policy, and target lookup are linked into
// this project. Only game objects and unrelated side-screen helpers are fixtures.
internal enum NotificationType { Bad, Neutral, DuplicantThreatening }

internal sealed class LogicAlarm
{
    internal string notificationName = "old name";
    internal string notificationTooltip = "old tooltip";
    internal NotificationType notificationType = NotificationType.Neutral;
    internal bool pauseOnNotify { get; set; }
    internal bool zoomOnNotify { get; set; }
    internal int UpdateCalls { get; private set; }
    internal void UpdateNotification(bool clear) { UpdateCalls++; }
}

internal sealed class AlarmTestBuilding
{
    internal GameObject gameObject { get; set; }
    internal int WorldId { get; set; }
}

internal sealed class SpaceHeater
{
    internal float UserSliderSetting => throw new NotSupportedException();
    internal float maxPower => throw new NotSupportedException();
    internal float minPower => throw new NotSupportedException();
    internal void SetUserSpecifiedPowerConsumptionValue(float value) => throw new NotSupportedException();
}

internal static partial class Components
{
    internal static class BuildingCompletes
    {
        internal static readonly List<AlarmTestBuilding> Items = new List<AlarmTestBuilding>();
    }
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static bool GameObjectMatchesWorld(GameObject target, int worldId)
        {
            return worldId < 0 || target.GetComponent<AlarmTestBuilding>().WorldId == worldId;
        }
    }

    public static partial class MiscSideScreenTools
    {
        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters) => parameters;
        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters) => parameters;
        private static CallToolResult JsonResult(Dictionary<string, object> payload) => CallToolResult.Text(JsonConvert.SerializeObject(payload));
        private static Dictionary<string, object> TargetInfo(GameObject target)
        {
            return new Dictionary<string, object> { ["id"] = target.GetComponent<KPrefabID>().InstanceID };
        }

        private static Dictionary<string, object> LogicAlarmInfo(LogicAlarm alarm)
        {
            return new Dictionary<string, object>
            {
                ["name"] = alarm.notificationName,
                ["tooltip"] = alarm.notificationTooltip,
                ["type"] = alarm.notificationType,
                ["pauseOnNotify"] = alarm.pauseOnNotify,
                ["zoomOnNotify"] = alarm.zoomOnNotify
            };
        }

        private static CallToolResult ListTargets(JObject args, Func<GameObject, bool> predicate,
            Func<GameObject, Dictionary<string, object>> selector, string payloadKey) => throw new NotSupportedException();
        private static Dictionary<string, McpToolParameter> TurboHeaterControlParams() => throw new NotSupportedException();
        private static bool IsTurboHeater(GameObject target) => throw new NotSupportedException();
        private static Dictionary<string, object> TurboHeaterInfo(SpaceHeater heater) => throw new NotSupportedException();
    }
}
