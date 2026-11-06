using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static class ProductionQueueHandlerRegression
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        try
        {
            InvalidContainers();
            LateRejections();
            RepeatedRecipePlanning();
            ClearAllAndDefaults();
            Console.WriteLine("Production queue handler regressions passed: " + assertions + " assertions");
        }
        finally
        {
            Components.ComplexFabricators.Items.Clear();
        }
    }

    private static void InvalidContainers()
    {
        foreach (JToken invalid in new JToken[] { new JObject { ["r"] = "alpha" }, new JValue("alpha"), new JValue(3), new JValue(true) })
        {
            var fixture = NewFixture();
            var args = Request();
            args["clearAll"] = true;
            args["items"] = invalid.DeepClone();
            ExpectRejected(fixture, args, "non-array items with clearAll " + invalid.Type);
        }

        foreach (string key in new[] { "defaults", "defaultArguments" })
        {
            foreach (JToken invalid in new JToken[] { new JArray(), new JValue("bad"), new JValue(7), new JValue(false) })
            {
                var fixture = NewFixture();
                var args = Request(Operation("alpha", "add", 3));
                args["clearAll"] = true;
                args[key] = invalid.DeepClone();
                // A valid alternative must not silently mask a malformed field.
                args[key == "defaults" ? "defaultArguments" : "defaults"] = new JObject { ["mode"] = "add", ["count"] = 2 };
                ExpectRejected(fixture, args, "invalid " + key + " " + invalid.Type);
            }
        }
    }

    private static void LateRejections()
    {
        foreach (bool clearAll in new[] { false, true })
        {
            foreach (JToken invalid in new JToken[]
            {
                Operation("locked", "set", 1),
                Operation("missing-recipe", "set", 1),
                Operation("alpha", "bad-mode", 1),
                new JObject { ["mode"] = "set", ["count"] = 1 },
                new JValue("not-an-operation")
            })
            {
                var fixture = NewFixture();
                var args = Request(Operation("alpha", "set", 42), invalid.DeepClone());
                args["clearAll"] = clearAll;
                ExpectRejected(fixture, args, "late invalid item, clearAll=" + clearAll);
            }
        }

        var unconfirmed = NewFixture();
        var noConfirm = Request(Operation("alpha", "set", 42));
        noConfirm["clearAll"] = true;
        noConfirm["confirm"] = false;
        ExpectRejected(unconfirmed, noConfirm, "missing confirmation");
    }

    private static void RepeatedRecipePlanning()
    {
        var fixture = NewFixture();
        JObject response = Success(ProductionTools.BatchSetQueue().Handler(Request(
            Operation("alpha", "add", 3), Operation("alpha", "remove", 2), Operation("alpha", "set", 4))));
        Check(Writes(fixture).SequenceEqual(new[] { "alpha=8", "alpha=6", "alpha=4" }),
            "repeated recipes commit in request order using the preceding planned count");
        Check(fixture.GetRecipeQueueCount(fixture.GetRecipe("alpha")) == 4
            && fixture.GetRecipeQueueCount(fixture.GetRecipe("beta")) == 7,
            "repeated updates leave unrelated recipes unchanged");
        Check(response["changes"].Select(change => (int)change["before"]).SequenceEqual(new[] { 5, 8, 6 })
            && response["changes"].Select(change => (int)change["after"]).SequenceEqual(new[] { 8, 6, 4 }),
            "change response reflects each planned before/after transition");

        fixture = NewFixture();
        var args = Request(Operation("alpha", "add", 2), Operation("alpha", "add", 3), Operation("alpha", "remove", 1), Operation("beta", "set", 4));
        args["clearAll"] = true;
        args["action"] = "batch";
        Success(ProductionTools.ControlQueue().Handler(args));
        Check(Writes(fixture).SequenceEqual(new[] { "alpha=0", "beta=0", "locked=0", "alpha=2", "alpha=5", "alpha=4", "beta=4" }),
            "public batch route plans repeated recipes from clearAll's zero counts");
        Check(fixture.GetRecipeQueueCount(fixture.GetRecipe("locked")) == 0,
            "clearAll can recover a queue whose recipe is now locked");
    }

    private static void ClearAllAndDefaults()
    {
        foreach (bool explicitNull in new[] { false, true })
        {
            var fixture = NewFixture();
            fixture.GetRecipe("locked").FailUnlockRead = true;
            var args = Request();
            args["clearAll"] = true;
            if (explicitNull)
            {
                args["items"] = JValue.CreateNull();
                args["defaults"] = JValue.CreateNull();
                args["defaultArguments"] = JValue.CreateNull();
            }
            Success(ProductionTools.BatchSetQueue().Handler(args));
            Check(Writes(fixture).SequenceEqual(new[] { "alpha=0", "beta=0", "locked=0" })
                && fixture.Counts.Values.All(count => count == 0),
                "clearAll-only request succeeds without unlock reads; null=" + explicitNull);
        }

        var noOperations = NewFixture();
        ExpectRejected(noOperations, Request(), "omitted items without clearAll");

        foreach (string key in new[] { "defaults", "defaultArguments" })
        {
            var fixture = NewFixture();
            var args = Request(new JObject { ["r"] = "alpha", ["c"] = 3 }, new JObject { ["r"] = "beta", ["m"] = "set", ["c"] = 4 });
            args[key] = new JObject { ["mode"] = "add", ["count"] = 2 };
            Success(ProductionTools.BatchSetQueue().Handler(args));
            Check(Writes(fixture).SequenceEqual(new[] { "alpha=8", "beta=4" }),
                key + " supports item aliases overriding longer default field names");
        }
    }

    private static ComplexFabricator NewFixture()
    {
        Components.ComplexFabricators.Items.Clear();
        var fixture = new ComplexFabricator();
        new GameObject { name = "Queue fixture", WorldId = 0, Cell = 120 }
            .Add(new KPrefabID { InstanceID = 501, PrefabTag = new Tag("FixtureFabricator") }).Add(fixture);
        foreach (var pair in new[] { new KeyValuePair<string, int>("alpha", 5), new KeyValuePair<string, int>("beta", 7), new KeyValuePair<string, int>("locked", 9) })
        {
            var recipe = new ComplexRecipe { id = pair.Key, Unlocked = pair.Key != "locked" };
            fixture.Recipes.Add(recipe);
            fixture.Counts.Add(recipe, pair.Value);
        }
        Components.ComplexFabricators.Items.Add(fixture);
        return fixture;
    }

    private static JObject Request(params JToken[] items)
    {
        var args = new JObject { ["id"] = 501, ["confirm"] = true };
        if (items.Length > 0)
            args["items"] = new JArray(items);
        return args;
    }

    private static JObject Operation(string recipe, string mode, int count) => new JObject
    {
        ["recipeId"] = recipe, ["mode"] = mode, ["count"] = count
    };

    private static IEnumerable<string> Writes(ComplexFabricator fixture) => fixture.Writes.Select(write => write.Key.id + "=" + write.Value);

    private static void ExpectRejected(ComplexFabricator fixture, JObject args, string message)
    {
        var before = fixture.Counts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var result = ProductionTools.BatchSetQueue().Handler(args);
        Check(result.IsError, message + " returns error");
        Check(fixture.Writes.Count == 0 && before.All(pair => fixture.GetRecipeQueueCount(pair.Key) == pair.Value),
            message + " calls no native setter and preserves every recipe count");
    }

    private static JObject Success(CallToolResult result)
    {
        Check(!result.IsError, "expected success: " + result.Content[0].Text);
        return JObject.Parse(result.Content[0].Text);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Production queue handler regression: " + message);
    }
}
