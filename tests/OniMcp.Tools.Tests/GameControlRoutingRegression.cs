using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class GameControlRoutingRegression
{
    internal static void Run()
    {
        McpTool tool = GameControlTools.ControlGame();
        foreach (string domain in new[] { "sandbox", "sandbox_tools", "debug", " SANDBOX " })
        {
            foreach (bool dryRun in new[] { false, true })
            {
                GameControlRouteTestDoubles.Calls = 0;
                CallToolResult result = tool.Handler(new JObject
                {
                    ["domain"] = domain, ["kind"] = "entity", ["action"] = "spawn_entity",
                    ["force"] = true, ["confirm"] = true, ["dryRun"] = dryRun
                });
                Require(result.IsError && GameControlRouteTestDoubles.Calls == 0,
                    "removed domain must not dispatch, even with force/confirm: " + domain);
            }
        }
        foreach (string parameter in new[] { "force", "element", "massKg", "prefabId", "storyId", "temperatureK", "diseaseCount" })
            Require(!tool.Parameters.ContainsKey(parameter), "removed parameter must not consume tool context: " + parameter);
        Require(!tool.Parameters["domain"].EnumValues.Contains("sandbox"), "schema must not advertise a removed route");
        Require(tool.Parameters.Count <= 34, "game control schema must remain bounded");
        Require(tool.Handler(null).IsError, "missing domain must return an error");

        GameControlRouteTestDoubles.Calls = 0;
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "pause" }).IsError
            && GameControlRouteTestDoubles.Calls == 1, "normal pause routing must remain available");
        Console.WriteLine("PASS: survival-only game control routing and compact schema");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// Exercise the production public router, replacing only the downstream game boundary.
namespace OniMcp.Tools
{
    internal static class GameControlRouteTestDoubles
    {
        internal static int Calls;
        internal static McpTool Tool()
        {
            return new McpTool { Handler = args => { Calls++; return CallToolResult.Text("game boundary"); } };
        }
    }

    public static partial class GameControlTools
    {
        public static McpTool ControlGameSpeed() => GameControlRouteTestDoubles.Tool();
        public static McpTool ControlGameState() => GameControlRouteTestDoubles.Tool();
        public static McpTool ControlGameSave() => GameControlRouteTestDoubles.Tool();
        public static McpTool ControlDlcActivation() => GameControlRouteTestDoubles.Tool();
    }

    public static class GameLaunchTools
    {
        public static McpTool ControlGameLaunch() => GameControlRouteTestDoubles.Tool();
    }

    public static class UiControlTools
    {
        public static McpTool ControlUi() => GameControlRouteTestDoubles.Tool();
    }
}
