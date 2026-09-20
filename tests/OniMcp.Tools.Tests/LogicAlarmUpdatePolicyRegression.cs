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

        var existing = typeof(BenchmarkMetadataRegressionEntry).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing benchmark regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLogicAlarmUpdatePolicyRegression()
    {
        string name = "old name";
        string tooltip = "old tooltip";
        string type = "neutral";
        string error;

        bool accepted = LogicAlarmUpdatePolicy.TryApplyTextAndType(
            JObject.Parse("{name:'new name',tooltip:'new tooltip',type:'not-a-type'}"),
            value => name = value,
            value => tooltip = value,
            value => type = value,
            out error);

        Check(!accepted, "invalid notification type must be rejected");
        Check(name == "old name" && tooltip == "old tooltip" && type == "neutral",
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
        Check(type == "duplicant_threatening",
            "legacy threat alias must preserve the existing notification type behavior");

        type = "bad";
        accepted = LogicAlarmUpdatePolicy.TryApplyTextAndType(
            JObject.Parse("{name:'rename only'}"),
            value => name = value,
            value => tooltip = value,
            value => type = value,
            out error);
        Check(accepted && type == "bad",
            "omitted type must not reset the current notification type");

        Console.WriteLine("Logic alarm update policy regression checks passed: " + assertions);
    }

    private static void Check(bool condition, string description)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
