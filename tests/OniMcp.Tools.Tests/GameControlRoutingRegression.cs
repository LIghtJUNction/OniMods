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
        var stepInputs = new[] { "until", "condition", "maxGameSeconds", "maxWallSeconds", "wait", "targetCells", "requestId" };
        foreach (string parameter in stepInputs)
            Require(tool.Parameters.ContainsKey(parameter), "condition execution input must be discoverable: " + parameter);
        Require(tool.Parameters["until"].Type == "object", "until must advertise structured expressions");
        Require(tool.Parameters["requestId"].Type == "string", "wait handles must be discoverable as strings");
        Require(tool.Parameters.Count - stepInputs.Length <= 34, "unrelated game control inputs must stay within their existing schema budget");
        Require(tool.Handler(null).IsError, "missing domain must return an error");

        GameControlRouteTestDoubles.Calls = 0;
        Game.Instance = new Game();
        SpeedControlScreen.Instance = new SpeedControlScreen();
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "pause" }).IsError
            && SpeedControlScreen.Instance.PauseCount == 1, "normal pause routing must reach the production speed handler");
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "resume" }).IsError
            && SpeedControlScreen.Instance.PauseCount == 0, "normal resume routing must release the MCP pause lease");
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "step_until" }).IsError, "step_until routing must succeed");
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "step_facts" }).IsError, "step_facts read routing must succeed");
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "step_status" }).IsError, "step_status routing must succeed");
        Require(!tool.Handler(new JObject { ["domain"] = "speed", ["action"] = "step_cancel" }).IsError, "step_cancel routing must succeed");
        Console.WriteLine("PASS: survival-only game control routing and compact schema");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

// Exercise the public router and production speed handler; unrelated domains
// retain lightweight boundaries for the removed-route checks.
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
        public static McpTool ControlGameState() => GameControlRouteTestDoubles.Tool();
        public static McpTool ControlGameSave() => GameControlRouteTestDoubles.Tool();
        public static McpTool ControlDlcActivation() => GameControlRouteTestDoubles.Tool();
        internal static CallToolResult StepUntil(JObject args) => GameControlRouteTestDoubles.Tool().Handler(args);
        internal static CallToolResult StepFacts(JObject args) => GameControlRouteTestDoubles.Tool().Handler(args);
        internal static CallToolResult StepStatus(JObject args) => GameControlRouteTestDoubles.Tool().Handler(args);
        internal static CallToolResult StepCancel(JObject args) => GameControlRouteTestDoubles.Tool().Handler(args);
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
