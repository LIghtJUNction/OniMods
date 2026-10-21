using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class LogicAlarmRegressionEntry
{
    private static int assertions;

    private static void Main()
    {
        RunLogicAlarmUpdatePolicyRegression();
        RunLogicAlarmHandlerRegression();

        var existing = typeof(BenchmarkMetadataRegressionEntry).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing benchmark regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLogicAlarmUpdatePolicyRegression()
    {
        string name = "old name";
        string tooltip = "old tooltip";
        NotificationType type = NotificationType.Neutral;
        string error;

        bool accepted = LogicAlarmUpdatePolicy.TryApplyTextAndType(
            JObject.Parse("{name:'new name',tooltip:'new tooltip',type:'not-a-type'}"),
            value => name = value,
            value => tooltip = value,
            value => type = value,
            out error);

        Check(!accepted, "invalid notification type must be rejected");
        Check(name == "old name" && tooltip == "old tooltip" && type == NotificationType.Neutral,
            "invalid compound update must not mutate earlier alarm fields");
        Check(error != null && error.Contains("duplicant_threatening"),
            "invalid notification type must return the existing validation message");

        string longName = new string('n', 40);
        string longTooltip = new string('t', 100);
        accepted = LogicAlarmUpdatePolicy.TryApplyTextAndType(
            new JObject
            {
                ["name"] = longName,
                ["tooltip"] = longTooltip,
                ["type"] = "threat"
            },
            value => name = value,
            value => tooltip = value,
            value => type = value,
            out error);

        Check(accepted && error == null, "valid compound alarm update must be accepted");
        Check(name.Length == 30 && tooltip.Length == 90,
            "alarm name and tooltip truncation must remain 30/90 characters");
        Check(type == NotificationType.DuplicantThreatening,
            "legacy threat alias must preserve the existing notification type behavior");

        type = NotificationType.Bad;
        accepted = LogicAlarmUpdatePolicy.TryApplyTextAndType(
            JObject.Parse("{name:'rename only'}"),
            value => name = value,
            value => tooltip = value,
            value => type = value,
            out error);
        Check(accepted && name == "rename only" && type == NotificationType.Bad,
            "rename-only update must change the name and preserve the current notification type");

        Console.WriteLine("Logic alarm update policy regression checks passed: " + assertions);
    }

    private static void RunLogicAlarmHandlerRegression()
    {
        Components.BuildingCompletes.Items.Clear();
        var coordinateAlarm = AddAlarm(1, 2, 3, 0);
        var requestedAlarm = AddAlarm(2, 7, 8, 1);
        var setAlarm = MiscSideScreenTools.SetLogicAlarm();
        var result = setAlarm.Handler(JObject.Parse("{id:2,x:2,y:3,name:'requested'}"));
        Check(!result.IsError && requestedAlarm.notificationName == "requested",
            "explicit ID must select the requested alarm even when coordinates match an earlier alarm");
        Check(coordinateAlarm.notificationName == "old name" && coordinateAlarm.UpdateCalls == 0,
            "conflicting coordinates must not mutate another alarm");
        Check(requestedAlarm.UpdateCalls == 1,
            "an ID lookup must not inherit the active-world restriction from ignored coordinates");

        result = setAlarm.Handler(JObject.Parse("{id:999,x:2,y:3,name:'wrong target'}"));
        Check(result.IsError && coordinateAlarm.UpdateCalls == 0 && requestedAlarm.UpdateCalls == 1,
            "missing explicit ID must fail without falling back to coordinates");
        result = setAlarm.Handler(JObject.Parse("{id:2,worldId:0,name:'wrong world'}"));
        Check(result.IsError && requestedAlarm.UpdateCalls == 1,
            "explicit world filter must still constrain an ID lookup");
        result = setAlarm.Handler(JObject.Parse("{x:2,y:3,name:'coordinate target'}"));
        Check(!result.IsError && coordinateAlarm.notificationName == "coordinate target",
            "coordinate-only requests must continue to select the active-world alarm");
        result = setAlarm.Handler(JObject.Parse("{x:7,y:8,worldId:1,name:'other world target'}"));
        Check(!result.IsError && requestedAlarm.notificationName == "other world target",
            "coordinate-only requests must honor an explicit world");

        int updatesBefore = requestedAlarm.UpdateCalls;
        result = setAlarm.Handler(JObject.Parse("{id:2,name:'invalid',tooltip:'invalid',type:'invalid',pauseOnNotify:true,zoomOnNotify:true}"));
        Check(result.IsError && requestedAlarm.notificationName == "other world target"
            && requestedAlarm.notificationTooltip == "old tooltip"
            && requestedAlarm.notificationType == NotificationType.Neutral,
            "invalid compound handler request must leave text and notification type untouched");
        Check(!requestedAlarm.pauseOnNotify && !requestedAlarm.zoomOnNotify
            && requestedAlarm.UpdateCalls == updatesBefore,
            "invalid compound handler request must leave flags and notification state untouched");
        result = setAlarm.Handler(new JObject
        {
            ["id"] = 2,
            ["name"] = new string('n', 40),
            ["tooltip"] = new string('t', 100),
            ["type"] = "threat",
            ["pauseOnNotify"] = true,
            ["zoomOnNotify"] = true
        });
        Check(!result.IsError && requestedAlarm.notificationName.Length == 30
            && requestedAlarm.notificationTooltip.Length == 90
            && requestedAlarm.notificationType == NotificationType.DuplicantThreatening,
            "valid handler request must apply the already validated enum and truncated text");
        Check(requestedAlarm.pauseOnNotify && requestedAlarm.zoomOnNotify
            && requestedAlarm.UpdateCalls == updatesBefore + 1,
            "valid handler request must apply flags and refresh the notification once");
        result = setAlarm.Handler(JObject.Parse("{id:2,name:'rename only'}"));
        Check(!result.IsError && requestedAlarm.notificationName == "rename only"
            && requestedAlarm.notificationType == NotificationType.DuplicantThreatening,
            "handler rename-only request must change the name and preserve the notification type");
        Components.BuildingCompletes.Items.Clear();
        Console.WriteLine("Logic alarm policy and handler regression checks passed: " + assertions);
    }

    private static LogicAlarm AddAlarm(int id, int x, int y, int worldId)
    {
        var alarm = new LogicAlarm();
        var building = new AlarmTestBuilding
        {
            gameObject = new UnityEngine.GameObject { Cell = Grid.XYToCell(x, y) },
            WorldId = worldId
        };
        building.gameObject.Add(building).Add(alarm).Add(new KPrefabID { InstanceID = id });
        Components.BuildingCompletes.Items.Add(building);
        return alarm;
    }

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
