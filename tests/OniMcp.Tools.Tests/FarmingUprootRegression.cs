using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class FarmingUprootRegression
{
    internal static void Run()
    {
        try
        {
            GenericCancelFixtureReachesHarvest();
            CancelPreservesHarvest(true, true);
            CancelPreservesHarvest(true, false);
            CancelPreservesHarvest(false, true);
            CancelPreservesHarvest(false, false);
            SkipsUnrelatedPlants();
            MarkStillUsesNativeOperation();
            InvalidAreaDoesNotMutate();
            Console.WriteLine("Farming uproot handler regressions passed (host fixtures, not ONI runtime).");
        }
        finally
        {
            Components.Uprootables.Items.Clear();
        }
    }

    private static void GenericCancelFixtureReachesHarvest()
    {
        var target = new UprootTestObject
        {
            HarvestWhenReady = true,
            MarkedForHarvest = true,
            HasHarvestChore = true
        };
        target.Trigger(UprootTestObject.CancelEvent);
        Check(!target.HarvestWhenReady && !target.MarkedForHarvest && !target.HasHarvestChore,
            "generic Cancel fixture must expose the harvest side effect");
    }

    private static void CancelPreservesHarvest(bool whenReady, bool hasChore)
    {
        Components.Uprootables.Items.Clear();
        UprootTestComponent plant = AddPlant(2, 3, 0, true);
        plant.gameObject.HarvestWhenReady = whenReady;
        plant.gameObject.MarkedForHarvest = hasChore;
        plant.gameObject.HasHarvestChore = hasChore;
        JObject result = Success(Request("cancel"));

        Check(!plant.IsMarkedForUproot && plant.CancelCalls == 1, "cancel must remove only the uproot designation");
        Check(plant.gameObject.HarvestWhenReady == whenReady
            && plant.gameObject.MarkedForHarvest == hasChore
            && plant.gameObject.HasHarvestChore == hasChore,
            "uproot cancellation changed harvest state (#301)");
        Check(plant.gameObject.CancelEvents == 0, "uproot cancellation must not broadcast generic Cancel");
        Check(plant.gameObject.PriorityWrites == 0, "cancel must not write priority");
        Check((int)result["changed"] == 1 && (string)result["targets"][0]["status"] == "cancelled",
            "cancel response must report its actual target");

        JObject repeated = Success(Request("cancel"));
        Check((int)repeated["changed"] == 0 && plant.CancelCalls == 1,
            "repeated cancellation must skip an unmarked plant");
    }

    private static void SkipsUnrelatedPlants()
    {
        Components.Uprootables.Items.Clear();
        UprootTestComponent outside = AddPlant(9, 9, 0, true);
        UprootTestComponent otherWorld = AddPlant(2, 3, 1, true);
        UprootTestComponent unmarked = AddPlant(2, 3, 0, false);
        Components.Uprootables.Items.Add(null);
        Components.Uprootables.Items.Add(new UprootTestComponent());
        JObject result = Success(Request("cancel"));
        Check((int)result["changed"] == 0 && outside.CancelCalls == 0
            && otherWorld.CancelCalls == 0 && unmarked.CancelCalls == 0,
            "out-of-area, other-world, null and unmarked targets must be skipped");
        Check(outside.IsMarkedForUproot && otherWorld.IsMarkedForUproot,
            "unrelated uproot designations must remain");
    }

    private static void MarkStillUsesNativeOperation()
    {
        Components.Uprootables.Items.Clear();
        UprootTestComponent plant = AddPlant(2, 3, 0, false);
        UprootTestComponent protectedPlant = AddPlant(2, 3, 0, false);
        protectedPlant.CanBeUprooted = false;
        JObject result = Success(Request("mark"));
        Check((int)result["changed"] == 1 && plant.MarkCalls == 1 && plant.IsMarkedForUproot,
            "eligible mark must still reach MarkForUproot");
        Check(plant.gameObject.PriorityWrites == 1 && plant.gameObject.CancelEvents == 0,
            "mark must retain priority behavior without a cancel event");
        Check(protectedPlant.MarkCalls == 0 && protectedPlant.gameObject.PriorityWrites == 0,
            "a non-uprootable plant must not be marked or reprioritized");
        McpTool tool = FarmingTools.UprootArea();
        Check(tool.Hidden && tool.Name == "plants_uproot_area"
            && tool.Aliases.Contains("farming_uproot_area") && tool.Aliases.Contains("plants_cancel_uproot"),
            "existing tool name and compatibility aliases must remain");
    }

    private static void InvalidAreaDoesNotMutate()
    {
        Components.Uprootables.Items.Clear();
        UprootTestComponent plant = AddPlant(2, 3, 0, true);
        Check(FarmingTools.UprootArea().Handler(new JObject { ["action"] = "cancel" }).IsError,
            "missing area must be rejected");
        JObject large = Request("cancel");
        large["x1"] = 0;
        large["y1"] = 0;
        large["x2"] = 10;
        large["y2"] = 10;
        large["confirm"] = false;
        Check(FarmingTools.UprootArea().Handler(large).IsError, "large area requires confirmation");
        Check(plant.IsMarkedForUproot && plant.CancelCalls == 0 && plant.gameObject.CancelEvents == 0,
            "rejected requests must not mutate the plant");
        large["confirm"] = true;
        Check((int)Success(large)["changed"] == 1, "confirmed large area must remain supported");
    }

    private static UprootTestComponent AddPlant(int x, int y, int worldId, bool marked)
    {
        var plant = new UprootTestComponent
        {
            gameObject = new UprootTestObject { X = x, Y = y, WorldId = worldId },
            IsMarkedForUproot = marked
        };
        Components.Uprootables.Items.Add(plant);
        return plant;
    }

    private static JObject Request(string action)
    {
        return new JObject
        {
            ["action"] = action,
            ["x1"] = 2, ["y1"] = 3, ["x2"] = 2, ["y2"] = 3,
            ["worldId"] = 0,
            ["confirm"] = true
        };
    }

    private static JObject Success(JObject args)
    {
        CallToolResult result = FarmingTools.UprootArea().Handler(args);
        Check(!result.IsError, "uproot handler unexpectedly returned an error");
        return JObject.Parse(result.Content[0].Text);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
