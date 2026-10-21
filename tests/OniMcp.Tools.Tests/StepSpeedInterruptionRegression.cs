using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class StepSpeedInterruptionRegression
{
    private static int assertions;

    internal static void Run()
    {
        foreach (string action in new[] { "resume", "set_speed" })
        {
            var screen = Reset();
            var wait = GameControlTools.StartSpeedWaitForTest();
            var result = Command(action, 2);
            Check(!result.IsError && wait.Completions.Count == 1,
                action + " must complete the old wait before returning");
            Check((string)wait.Completions[0]["stopReason"] == "interrupted_by_" + action,
                action + " must report why the old wait ended");
            Check((bool)wait.Completions[0]["pausedAtCompletion"] && !screen.IsPaused,
                action + " must release the pause acquired while completing the old wait");
            Check(wait.SpeedAtCompletion == 0 && screen.GetSpeed() == (action == "set_speed" ? 1 : 0),
                "completion must happen before the new speed is applied");

            // Run the production Unity callback even after Destroy was requested.
            UnityEngine.Time.realtimeSinceStartup = 110f;
            wait.Tick();
            wait.Destroy();
            Check(wait.Completions.Count == 1 && !screen.IsPaused,
                "a stale update/destroy callback must not pause or complete again");
        }

        foreach (string action in new[] { "pause", "set_speed" })
        {
            var screen = Reset();
            var wait = GameControlTools.StartSpeedWaitForTest();
            Check(!Command(action, 0).IsError && screen.PauseCount == 1,
                "a pause command must retain exactly one MCP pause");
            Check(wait.Completions.Count == 1, "pause must invalidate the wait synchronously");
            Command("resume");
            UnityEngine.Time.realtimeSinceStartup = 110f;
            wait.Tick();
            Check(!screen.IsPaused && wait.Completions.Count == 1,
                "same-frame pause/resume must not leave the original deadline active");
        }

        var invalidScreen = Reset();
        var invalidWait = GameControlTools.StartSpeedWaitForTest();
        foreach (string value in new[] { "-1", "4", "'invalid'", "null" })
        {
            var result = GameControlTools.ControlGameSpeed().Handler(
                JObject.Parse("{action:'set_speed',speed:" + value + "}"));
            Check(result.IsError && invalidWait.Completions.Count == 0 && !invalidScreen.IsPaused,
                "invalid speed must not cancel or pause the current wait");
        }
        Command("time");
        Check(invalidWait.Completions.Count == 0, "a time read must not cancel a wait");
        UnityEngine.Time.realtimeSinceStartup = 102f;
        invalidWait.Tick();
        Check(invalidScreen.IsPaused && invalidWait.Completions.Count == 1
            && (string)invalidWait.Completions[0]["stopReason"] == "timeout_wall_clock",
            "the untouched wait must still enforce its wall deadline");

        foreach (string action in new[] { "pause", "resume", "set_speed" })
        {
            var screen = Reset();
            var wait = GameControlTools.StartSpeedWaitForTest();
            SpeedControlScreen.Instance = null;
            Check(Command(action, 2).IsError && wait.Completions.Count == 0,
                "a missing native speed controller must reject " + action + " without cancelling");
            SpeedControlScreen.Instance = screen;
            var game = Game.Instance;
            Game.Instance = null;
            Check(Command(action, 2).IsError && wait.Completions.Count == 0,
                "a missing game must reject " + action + " without cancelling");
            Game.Instance = game;
            UnityEngine.Time.realtimeSinceStartup = 102f;
            wait.Tick();
            Check(screen.IsPaused && wait.Completions.Count == 1,
                "rejected commands must leave the original deadline intact");
        }

        foreach (string action in new[] { "resume", "set_speed" })
        {
            var external = Reset();
            var externalWait = GameControlTools.StartSpeedWaitForTest();
            external.Pause();
            var result = Command(action, 2);
            Check(!result.IsError && externalWait.Completions.Count == 1 && external.PauseCount == 1
                && external.GetSpeed() == (action == "set_speed" ? 1 : 0),
                "interrupting a wait must preserve an unrelated native pause");
            Check((bool)JObject.Parse(result.Content[0].Text)["isPaused"],
                action + " must report the remaining external pause after completing the wait");
        }

        var replacement = Reset();
        var oldWait = GameControlTools.StartSpeedWaitForTest();
        Command("resume");
        var newWait = GameControlTools.StartSpeedWaitForTest(5f);
        UnityEngine.Time.realtimeSinceStartup = 102f;
        oldWait.Tick();
        oldWait.Destroy();
        newWait.Tick();
        Check(!replacement.IsPaused && newWait.Completions.Count == 0,
            "old callbacks must not cancel or pause a replacement wait");
        UnityEngine.Time.realtimeSinceStartup = 106f;
        newWait.Tick();
        Check(replacement.IsPaused && newWait.Completions.Count == 1,
            "the replacement wait must retain its own deadline");

        Reset();
        var staleLoadWait = GameControlTools.StartSpeedWaitForTest();
        Game.Instance = new Game();
        var newScreen = new SpeedControlScreen();
        SpeedControlScreen.Instance = newScreen;
        newScreen.Pause();
        Command("set_speed", 2);
        Check(staleLoadWait.Completions.Count == 1
            && (string)staleLoadWait.Completions[0]["stopReason"] == "scene_unloaded"
            && newScreen.PauseCount == 1,
            "cancelling a wait from an old game must not acquire a pause in the new game");

        Reset();
        Console.WriteLine("Step speed interruption regression checks passed: " + assertions);
    }

    private static CallToolResult Command(string action, int speed = 1) =>
        GameControlTools.ControlGameSpeed().Handler(new JObject { ["action"] = action, ["speed"] = speed });

    private static SpeedControlScreen Reset()
    {
        GameControlTools.ResetSpeedWaitForTest();
        Game.Instance = new Game();
        var screen = new SpeedControlScreen();
        SpeedControlScreen.Instance = screen;
        UnityEngine.Time.realtimeSinceStartup = 100f;
        return screen;
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Step speed interruption: " + message);
    }
}
