using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class SearchGuidanceRegression
{
    internal static void Run()
    {
        OniToolRegistry.Initialize();
        var publicNames = OniToolRegistry.GetTools().Select(tool => tool.Name).ToArray();
        Require(publicNames.Contains("world_editor") && publicNames.Contains("navigation_control"),
            "search guidance names a tool that is not public");

        foreach (string mode in new[] { "hits", "clusters", "summary" })
        {
            var result = WorldSearchKindRegression.Search(new JArray("cells"), mode);
            Require((string)result["tool"] == "world_search",
                "internal result identity must remain world_search");
            string hint = (string)result["recommendedFollowUp"];
            RequireUsable(hint);
            Require(hint.Contains("world_editor command=read path=/active/map/zoom_X1_Y1_X2_Y2.md worldId=W task="),
                "terrain guidance lacks a complete public map read");
            Require(hint.Contains("navigation_control domain=camera action=focus_cell x=X y=Y worldId=W task="),
                "visual guidance lacks a complete public camera call");
            Require(hint.Contains("navigation_control domain=camera action=set_active_world worldId=W task="),
                "cross-world map guidance lacks the required world selection");
        }

        // SearchControl includes unrelated game factories in this host. Verify only
        // the observed fallback literal; the final game run follows the actual UI/tool path.
        string source = ReadSource("mods/OniMcp/Tools/Entry/SearchControlTools.cs");
        Match match = Regex.Match(source, @"\[""coordinateFallback""\]\s*=\s*(""(?:\\.|[^""\\])*"")");
        Require(match.Success, "coordinateFallback source contract was not found");
        string fallback = JsonConvert.DeserializeObject<string>(match.Groups[1].Value);
        RequireUsable(fallback);
        Require(fallback.Contains("world_editor command=read path=/active/map/cell_X_Y.md worldId=W task="),
            "coordinate fallback lacks a complete public cell read");
        Require(fallback.Contains("navigation_control domain=camera action=set_active_world worldId=W task="),
            "coordinate fallback omits cross-world selection");
    }

    private static void RequireUsable(string hint)
    {
        Require(!string.IsNullOrWhiteSpace(hint), "search continuation is empty");
        foreach (string retired in new[] { "world_area_snapshot", "world_text_map",
            "camera_focus_cell", "coordinate_control", "read_control", "colony_control" })
            Require(!hint.Contains(retired), "search guidance still recommends retired tool " + retired);
        Require(Regex.IsMatch(hint, "task=\\\"[^\\\"]+\\\""),
            "public calls must include a nonempty task");
    }

    private static string ReadSource(string relative)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, relative);
                if (File.Exists(path))
                    return File.ReadAllText(path);
                directory = directory.Parent;
            }
        }
        throw new InvalidOperationException("Cannot locate production source: " + relative);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
