using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class BlueprintPlacementRegression
{
    internal static void Run()
    {
        var buildings = JArray.Parse("[{buildingdef:'Tile',offset:{x:2,y:-1}},{buildingdef:'Wire',offset:{x:3,y:4}}]");
        var success = BlueprintNativePlacement.Run(buildings, 10, 20,
            (item, x, y) => CallToolResult.Text("{}"), result => result.IsError);
        Require(success.Value<bool>("ok") && success.Value<int>("placedNatively") == 2, "all success");

        int calls = 0;
        var partial = BlueprintNativePlacement.Run(buildings, 10, 20, (item, x, y) =>
        {
            calls++;
            if (calls == 1) throw new InvalidOperationException("blocked cell");
            return CallToolResult.Text("{}");
        }, result => result.IsError);
        Require(calls == 2 && !partial.Value<bool>("ok") && partial.Value<bool>("partial"), "continue after exception");
        Require(partial.Value<string>("status") == "partial" && partial.Value<int>("failed") == 1, "partial status");
        var failure = (JObject)partial["failures"][0];
        Require(failure.Value<string>("prefab") == "Tile" && failure.Value<int>("x") == 12 && failure.Value<int>("y") == 19,
            "failure identity and absolute coordinates");
        Require(failure.Value<string>("reason").Contains("blocked cell"), "exception reason");

        var failed = BlueprintNativePlacement.Run(buildings, 10, 20,
            (item, x, y) => CallToolResult.Error("insufficient material"), result => result.IsError);
        Require(!failed.Value<bool>("ok") && !failed.Value<bool>("partial") && failed.Value<string>("status") == "failed", "all failures");
        Require(failed.Value<int>("failed") == 2 && failed["failures"][1].Value<string>("reason") == "insufficient material", "returned errors retained");
        var empty = BlueprintNativePlacement.Run(new JArray(), 0, 0, (item, x, y) => null, result => result.IsError);
        Require(empty.Value<string>("error") == "blueprint_no_placeable_buildings", "empty blueprint must not succeed");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("blueprint placement: " + message);
    }
}
