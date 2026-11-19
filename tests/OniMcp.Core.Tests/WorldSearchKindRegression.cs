using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class WorldSearchKindRegression
{
    internal static void Run()
    {
        // Execute the production factory, request parser and kind dispatch.
        // Only the three-cell world below is a host double; results use production rendering.
        WorldSearchTools.FixtureCellScans = 0;
        var plural = Search(new JArray("cells"));
        Require((int)plural["matched"] == 1 && (string)plural["items"][0]["elementId"] == "Water",
            "the documented cells request must reach the owned liquid fixture");

        foreach (JToken kind in new JToken[] { new JValue("cell"), new JArray("cell"), new JValue(" CeLl ") })
        {
            var singular = Search(kind);
            Require(JToken.DeepEquals(plural, singular),
                "result.kind=cell must be reusable as an equivalent kinds filter");
        }
        Require(WorldSearchTools.FixtureCellScans == 4,
            "each cell/cells request must execute the production cell-search branch");

        var buildings = Search(new JArray("buildings"));
        Require((int)buildings["matched"] == 0 && WorldSearchTools.FixtureCellScans == 4,
            "the cell alias must not broaden unrelated kind routing");
        Require(JToken.DeepEquals(Search(new JValue("element")), Search(new JArray("elements"))),
            "the existing element alias changed");
    }

    internal static JObject Search(JToken kinds, string returnMode = "hits")
    {
        var args = JObject.Parse(@"{query:'Water',state:'liquid',minMassKg:0.001,
            x1:0,y1:0,x2:2,y2:0,worldId:0,returnMode:'hits'}");
        args["kinds"] = kinds;
        args["returnMode"] = returnMode;
        CallToolResult result = WorldSearchTools.SearchWorld().Handler(args);
        Require(!result.IsError && result.Content.Count == 1, "world search returned an error");
        return JObject.Parse(result.Content[0].Text);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// These doubles are confined to the Core host; no game assembly or live world is loaded.
internal static class Game
{
    internal static readonly object Instance = new object();
}

internal sealed class ClusterManager
{
    internal static readonly ClusterManager Instance = new ClusterManager();
    internal int activeWorldId = 0;
}

internal static class Grid
{
    internal static readonly int[] WorldIdx = { 0, 0, 0 };
    internal static int XYToCell(int x, int y) { return y == 0 ? x : -1; }
    internal static bool IsValidCell(int cell) { return cell >= 0 && cell < 3; }
    internal static bool IsWorldValidCell(int cell) { return IsValidCell(cell); }
    internal static int CellColumn(int cell) { return cell; }
    internal static int CellRow(int cell) { return 0; }
    internal static bool IsVisible(int cell) { return IsValidCell(cell); }
}

namespace OniMcp.Tools
{
    internal static class WorldEditor
    {
        internal static Dictionary<string, int> ResolveRectOrCamera(JObject args, int maxCells)
        {
            return new Dictionary<string, int> {
                ["x1"] = (int)args["x1"], ["y1"] = (int)args["y1"],
                ["x2"] = (int)args["x2"], ["y2"] = (int)args["y2"]
            };
        }
    }

    internal static class ToolUtil
    {
        internal static int? GetInt(JObject args, string name) { return (int?)args[name]; }
        internal static float? GetFloat(JObject args, string name) { return (float?)args[name]; }
        internal static bool GetBool(JObject args, string name, bool fallback)
        { return (bool?)args[name] ?? fallback; }
    }

    public static partial class WorldSearchTools
    {
        internal static int FixtureCellScans;

        private static IEnumerable<SearchHit> SearchCells(SearchRequest request)
        {
            FixtureCellScans++;
            foreach (int cell in request.Cells())
            {
                string element = cell == 1 ? "Oxygen" : "Water";
                string state = cell == 1 ? "gas" : "liquid";
                float mass = cell == 2 ? 0f : 10f;
                if (!request.MatchesQuery(element) || !request.MatchesState(state)
                    || request.MinMassKg.HasValue && mass < request.MinMassKg.Value)
                    continue;
                yield return new SearchHit {
                    Kind = "cell", Id = "cell:" + cell, Name = element, PrefabId = null,
                    ElementId = element, X = cell, Y = 0, WorldId = 0, MassKg = mass,
                    TemperatureC = 20f, State = state, Solid = false, Stored = null,
                    Operational = null, Visible = true, Scanned = 3
                };
            }
        }

        private static IEnumerable<SearchHit> SearchBuildings(SearchRequest request) { return new SearchHit[0]; }
        private static IEnumerable<SearchHit> SearchItems(SearchRequest request) { return new SearchHit[0]; }
        private static IEnumerable<SearchHit> SearchDupes(SearchRequest request) { return new SearchHit[0]; }
        private static object BuildPatternResult(SearchRequest request)
        { throw new InvalidOperationException("pattern search is outside this fixture"); }

    }
}
