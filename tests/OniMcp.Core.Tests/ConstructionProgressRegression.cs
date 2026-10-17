using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class ConstructionProgressRegression
{
    internal static void Run()
    {
        Require(ConstructionProgressReport.TryReadPage(null, out int offset, out int limit, out string error)
            && offset == 0 && limit == 20 && error == null, "Default page changed.");
        Require(ConstructionProgressReport.TryReadPage(new JObject { ["offset"] = int.MaxValue, ["limit"] = 100 },
            out offset, out limit, out error) && offset == int.MaxValue && limit == 100, "Valid page bounds rejected.");
        foreach (var value in new JToken[] { new JValue(-1), new JValue(1.5), new JValue("2"), new JValue(true), JValue.CreateNull(), new JValue((long)int.MaxValue + 1) })
            Require(!ConstructionProgressReport.TryReadPage(new JObject { ["offset"] = value }, out offset, out limit, out error),
                "Invalid offset accepted: " + value);
        foreach (var value in new JToken[] { new JValue(0), new JValue(101), new JValue("20"), new JObject() })
            Require(!ConstructionProgressReport.TryReadPage(new JObject { ["limit"] = value }, out offset, out limit, out error),
                "Invalid limit accepted: " + value);

        var rows = new List<ConstructionProgressRow>
        {
            new ConstructionProgressRow { Id = 7, PrefabId = "Tile", X = 3, Y = 9, MainStatus = "Awaiting Delivery" },
            new ConstructionProgressRow { Id = 8, PrefabId = "Ladder", X = 3, Y = 8, MainStatus = null }
        };
        string text = ConstructionProgressReport.Render(0, 7, 4, 0, rows);
        Require(text.Contains("pending=7; matching=4; returned=2; offset=0"), "Counts confuse a filtered page with all work.");
        Require(text.Contains("offset=2") && text.Contains("Awaiting Delivery") && text.Contains("unknown"), "Status or continuation lost.");
        Require(text.Contains("Task completion: not verified") && text.Contains("unknown does not mean ready"), "Readiness was inferred from missing status.");
        Require(!ConstructionProgressReport.Render(0, 4, 4, 2, rows).Contains("More matches:"), "Last page advertises a further page.");
        string empty = ConstructionProgressReport.Render(0, 0, 0, 0, new List<ConstructionProgressRow>());
        Require(empty.Contains("not verified") && empty.Contains("Cancelled or missing plans"), "No blueprints became task completion.");
        Require(ConstructionProgressReport.Render(0, 5, 0, 0, new List<ConstructionProgressRow>()).Contains("other pending blueprints still exist"),
            "Empty query hid pending work.");
        Require(ConstructionProgressReport.Render(0, 5, 5, int.MaxValue, new List<ConstructionProgressRow>()).Contains("retry with offset=0"),
            "Out-of-range page overflowed or looked complete.");
        string failed = ConstructionProgressReport.Unavailable("construction_scan_failed");
        Require(failed.Contains("observation=unavailable") && !failed.Contains("pending=0"), "Failed scan looked empty.");

        rows[0].PrefabId = "Tile|mod\nname";
        rows[0].MainStatus = new string('x', 300);
        text = ConstructionProgressReport.Render(0, 2, 2, 0, rows);
        Require(text.Contains("Tile\\|mod name") && text.Contains("[truncated]"), "Table escaping or explicit status truncation failed.");
        bool rejected = false;
        try { ConstructionProgressReport.Render(0, 1, 1, 0, rows); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Inconsistent observation counts accepted.");
        Console.WriteLine("Construction progress: pagination, unknown/error/empty states, counts and bounded text passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
